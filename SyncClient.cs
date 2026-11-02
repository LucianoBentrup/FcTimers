using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FcTimers;

public readonly record struct BuffReading(int Slot, uint ItemId, string Name, int Hours);

// Envia ao serviço só o que acabou de ser lido do jogo. Roda sempre na thread do jogo,
// e só a requisição HTTP fica em segundo plano.
public sealed class SyncClient(Configuration config, Action save, Action<string> log) : IDisposable
{
    private const int MaxVoyagesPerRequest = 50;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveBuffRefresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VoyageScanDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FetchOpen = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FetchClosed = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record PendingSub(int Slot, string Name, long ReturnTime, DateTime ReadAt);
    private sealed record PendingBuffs(List<BuffReading> Slots, DateTime ReadAt);
    private sealed record PendingActive(List<ActiveBuffReading> Buffs, DateTime ReadAt);
    private sealed record PendingPlot(string Key, string Name, long NextWaterAt, long DieAt, DateTime ReadAt);
    private sealed record SendResult(bool Success, string Status);

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Dictionary<int, string> lastSubs = [];
    private readonly Dictionary<int, PendingSub> pendingSubs = [];
    private string? lastBuffs;
    private PendingBuffs? pendingBuffs;
    private string? lastActive;
    private DateTime lastActiveAt = DateTime.MinValue;
    private PendingActive? pendingActive;
    private readonly Dictionary<string, string> lastPlots = [];
    private readonly Dictionary<string, PendingPlot> pendingPlots = [];
    private DateTime nextAttempt = DateTime.MinValue;
    private DateTime nextVoyageScan = DateTime.MinValue;

    private Task<SendResult>? sending;
    private List<PendingSub> sendingSubs = [];
    private PendingBuffs? sendingBuffs;
    private PendingActive? sendingActive;
    private List<PendingPlot> sendingPlots = [];
    private Task<RemoteState?>? fetching;
    private DateTime nextFetch = DateTime.MinValue;
    private string lastStatus = string.Empty;
    private List<KeyValuePair<string, string>> sendingVoyages = [];
    private Task<string>? sendingLog;

    public string Status { get; private set; } = "Aguardando dados";
    public string LogStatus { get; private set; } = string.Empty;
    public bool SendingLog => sendingLog != null;

    // Último estado recebido do serviço, e a diferença entre o relógio do PC e o do servidor.
    public RemoteState? Remote { get; private set; }
    public long RemoteOffset { get; private set; }
    public DateTime? LastFetch { get; private set; }

    // Com a janela aberta, busca com mais frequência.
    public bool WindowOpen { get; set; }

    private bool Enabled => config.SyncEnabled && !string.IsNullOrWhiteSpace(config.ServiceUrl) && !string.IsNullOrWhiteSpace(config.ServiceKey);

    public void Dispose() => http.Dispose();

    public void ReportSub(int slot, string name, long returnTime)
    {
        if (!Enabled)
            return;

        var signature = $"{name}|{returnTime}";
        if (lastSubs.GetValueOrDefault(slot) == signature)
            return;

        lastSubs[slot] = signature;
        pendingSubs[slot] = new PendingSub(slot, name, returnTime, DateTime.UtcNow);
    }

    public void ReportBuffs(List<BuffReading> slots)
    {
        if (!Enabled)
            return;

        var signature = string.Join(';', slots.Select(s => $"{s.Slot}|{s.ItemId}|{s.Hours}"));
        if (lastBuffs == signature)
            return;

        lastBuffs = signature;
        pendingBuffs = new PendingBuffs(slots, DateTime.UtcNow);
    }

    public void ReportActiveBuffs(List<ActiveBuffReading> buffs)
    {
        if (!Enabled)
            return;

        // Mesmo sem mudança, reenvia de tempos em tempos para o painel mostrar quando foi lido.
        var signature = string.Join('|', buffs.Select(b => $"{b.Name}:{b.Seconds.HasValue}"));
        if (lastActive == signature && DateTime.UtcNow - lastActiveAt < ActiveBuffRefresh)
            return;

        lastActive = signature;
        lastActiveAt = DateTime.UtcNow;
        pendingActive = new PendingActive(buffs, DateTime.UtcNow);
    }

    // A leitura da rega vale a partir do horário em que a planta foi regada, e não do envio.
    // Assim uma rega antiga deste PC nunca passa por cima de uma rega mais nova de outro membro.
    public void ReportPlot(string key, string name, long wateredAt, long nextWaterAt, long dieAt)
    {
        var signature = $"{name}|{nextWaterAt}|{dieAt}";
        if (!Enabled || wateredAt == 0 || lastPlots.GetValueOrDefault(key) == signature)
            return;

        lastPlots[key] = signature;
        pendingPlots[key] = new PendingPlot(key, name, nextWaterAt, dieAt, DateTimeOffset.FromUnixTimeSeconds(wateredAt).UtcDateTime);
    }

    // Manda o log de diagnóstico ao serviço, que guarda só o último de cada personagem.
    public bool SendLog(string character, string text)
    {
        if (!Enabled || sendingLog != null)
            return false;

        var body = JsonSerializer.Serialize(new { character, text });
        sendingLog = SendLogAsync(body, config.ServiceUrl.Trim().TrimEnd('/'), config.ServiceKey.Trim());
        LogStatus = "Enviando log...";
        return true;
    }

    private async Task<string> SendLogAsync(string body, string url, string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/log");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? $"Log enviado ({DateTime.Now:HH:mm})" : $"Erro {(int)response.StatusCode} ao enviar o log";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return "Sem conexão com o serviço";
        }
    }

    // Busca já na próxima rodada, por exemplo ao abrir a janela.
    public void FetchSoon() => nextFetch = DateTime.MinValue;

    public void Tick()
    {
        TickFetch();

        if (sendingLog is { IsCompleted: true })
        {
            LogStatus = sendingLog.Result;
            sendingLog = null;
            log($"Diagnóstico: {LogStatus}");
        }

        if (sending != null)
        {
            if (!sending.IsCompleted)
                return;

            Finish(sending.Result);
            sending = null;
        }

        if (!Enabled)
        {
            Status = "Desligado";
            return;
        }

        if (DateTime.UtcNow < nextAttempt)
            return;

        // O histórico só cresce, então a busca por viagens novas não roda a cada segundo.
        sendingVoyages = [];
        if (DateTime.UtcNow >= nextVoyageScan)
        {
            sendingVoyages = config.Voyages
                .Select(v => new KeyValuePair<string, string>(v.Key, Signature(v.Value)))
                .Where(v => config.SyncedVoyages.GetValueOrDefault(v.Key) != v.Value)
                .Take(MaxVoyagesPerRequest)
                .ToList();
            nextVoyageScan = sendingVoyages.Count == MaxVoyagesPerRequest ? DateTime.MinValue : DateTime.UtcNow + VoyageScanDelay;
        }

        if (pendingSubs.Count == 0 && pendingBuffs == null && pendingActive == null && pendingPlots.Count == 0 && sendingVoyages.Count == 0)
            return;

        sendingSubs = pendingSubs.Values.ToList();
        sendingBuffs = pendingBuffs;
        sendingActive = pendingActive;
        sendingPlots = pendingPlots.Values.ToList();
        sending = SendAsync(BuildBody(DateTime.UtcNow), config.ServiceUrl.Trim().TrimEnd('/'), config.ServiceKey.Trim());
    }

    private string BuildBody(DateTime now)
    {
        var body = new
        {
            subs = sendingSubs.Select(s => new { slot = s.Slot, name = s.Name, returnTime = s.ReturnTime, age = Age(s.ReadAt, now) }),
            buffs = sendingBuffs == null ? null : new
            {
                age = Age(sendingBuffs.ReadAt, now),
                slots = sendingBuffs.Slots.Select(b => new { slot = b.Slot, itemId = b.ItemId, name = b.Name, hours = b.Hours }),
            },
            activeBuffs = sendingActive == null ? null : new
            {
                age = Age(sendingActive.ReadAt, now),
                buffs = sendingActive.Buffs.Select(b => new { name = b.Name, seconds = b.Seconds }),
            },
            plots = sendingPlots.Select(p => new { key = p.Key, name = p.Name, nextWaterAt = p.NextWaterAt, dieAt = p.DieAt, age = Age(p.ReadAt, now) }),
            voyages = sendingVoyages.Select(v =>
            {
                var voyage = config.Voyages[v.Key];
                return new
                {
                    slot = LootLedger.SlotOf(v.Key),
                    subName = voyage.SubName,
                    returnTime = voyage.ReturnTime,
                    items = voyage.Items.Select(i => new { itemId = i.ItemId, name = i.Name, count = i.Count, value = i.Value }),
                };
            }),
        };

        return JsonSerializer.Serialize(body);
    }

    private async Task<SendResult> SendAsync(string body, string url, string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/update");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new SendResult(true, "Conectado")
                : new SendResult(false, $"Erro {(int)response.StatusCode} ao enviar");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new SendResult(false, "Sem conexão com o serviço");
        }
    }

    private void TickFetch()
    {
        if (fetching != null)
        {
            if (!fetching.IsCompleted)
                return;

            var state = fetching.Result;
            fetching = null;
            if (state != null)
            {
                Remote = state;
                RemoteOffset = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - state.Now;
                LastFetch = DateTime.Now;
            }
        }

        if (!Enabled)
        {
            Remote = null;
            LastFetch = null;
            return;
        }

        if (DateTime.UtcNow < nextFetch)
            return;

        nextFetch = DateTime.UtcNow + (WindowOpen ? FetchOpen : FetchClosed);
        fetching = FetchAsync(config.ServiceUrl.Trim().TrimEnd('/'), config.ServiceKey.Trim());
    }

    private async Task<RemoteState?> FetchAsync(string url, string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{url}/state");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<RemoteState>(text, ReadOptions);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException or JsonException)
        {
            return null;
        }
    }

    private void Finish(SendResult result)
    {
        Status = result.Success ? $"{result.Status} ({DateTime.Now:HH:mm})" : result.Status;
        if (result.Status != lastStatus)
        {
            lastStatus = result.Status;
            log($"Serviço: {result.Status}");
        }

        if (!result.Success)
        {
            nextAttempt = DateTime.UtcNow + RetryDelay;
            return;
        }

        // Uma leitura nova que chegou durante o envio continua pendente.
        foreach (var sub in sendingSubs.Where(s => ReferenceEquals(pendingSubs.GetValueOrDefault(s.Slot), s)))
            pendingSubs.Remove(sub.Slot);

        if (ReferenceEquals(pendingBuffs, sendingBuffs))
            pendingBuffs = null;

        if (ReferenceEquals(pendingActive, sendingActive))
            pendingActive = null;

        foreach (var plot in sendingPlots.Where(p => ReferenceEquals(pendingPlots.GetValueOrDefault(p.Key), p)))
            pendingPlots.Remove(plot.Key);

        if (sendingVoyages.Count > 0)
            log($"Serviço: {sendingVoyages.Count} viagem(ns) enviada(s)");

        foreach (var voyage in sendingVoyages)
            config.SyncedVoyages[voyage.Key] = voyage.Value;

        if (sendingVoyages.Count > 0)
            save();
    }

    private static long Age(DateTime readAt, DateTime now) => Math.Max(0, (long)(now - readAt).TotalSeconds);

    private static string Signature(VoyageRecord voyage)
        => string.Join(';', voyage.Items.Select(i => $"{i.ItemId}|{i.Count}|{i.Value}"));
}
