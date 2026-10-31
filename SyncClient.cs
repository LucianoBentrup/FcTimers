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
public sealed class SyncClient(Configuration config, Action save) : IDisposable
{
    private const int MaxVoyagesPerRequest = 50;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveBuffRefresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VoyageScanDelay = TimeSpan.FromSeconds(30);

    private sealed record PendingSub(int Slot, string Name, long ReturnTime, DateTime ReadAt);
    private sealed record PendingBuffs(List<BuffReading> Slots, DateTime ReadAt);
    private sealed record PendingActive(List<ActiveBuffReading> Buffs, DateTime ReadAt);
    private sealed record SendResult(bool Success, string Status);

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly Dictionary<int, string> lastSubs = [];
    private readonly Dictionary<int, PendingSub> pendingSubs = [];
    private string? lastBuffs;
    private PendingBuffs? pendingBuffs;
    private string? lastActive;
    private DateTime lastActiveAt = DateTime.MinValue;
    private PendingActive? pendingActive;
    private DateTime nextAttempt = DateTime.MinValue;
    private DateTime nextVoyageScan = DateTime.MinValue;

    private Task<SendResult>? sending;
    private List<PendingSub> sendingSubs = [];
    private PendingBuffs? sendingBuffs;
    private PendingActive? sendingActive;
    private List<KeyValuePair<string, string>> sendingVoyages = [];

    public string Status { get; private set; } = "Aguardando dados";

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

    public void Tick()
    {
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

        if (pendingSubs.Count == 0 && pendingBuffs == null && pendingActive == null && sendingVoyages.Count == 0)
            return;

        sendingSubs = pendingSubs.Values.ToList();
        sendingBuffs = pendingBuffs;
        sendingActive = pendingActive;
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
            voyages = sendingVoyages.Select(v =>
            {
                var voyage = config.Voyages[v.Key];
                return new
                {
                    slot = SlotOf(v.Key),
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

    private void Finish(SendResult result)
    {
        Status = result.Success ? $"{result.Status} ({DateTime.Now:HH:mm})" : result.Status;
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

        foreach (var voyage in sendingVoyages)
            config.SyncedVoyages[voyage.Key] = voyage.Value;

        if (sendingVoyages.Count > 0)
            save();
    }

    private static long Age(DateTime readAt, DateTime now) => Math.Max(0, (long)(now - readAt).TotalSeconds);

    private static int SlotOf(string voyageKey)
    {
        var parts = voyageKey.Split(':');
        return int.Parse(parts[^2]);
    }

    private static string Signature(VoyageRecord voyage)
        => string.Join(';', voyage.Items.Select(i => $"{i.ItemId}|{i.Count}|{i.Value}"));
}
