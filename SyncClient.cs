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
// isMarketable diz se o item pode ir para o market; marketWorld é o mundo ou data center dos preços.
public sealed class SyncClient(Configuration config, Action save, Action<string> log, Func<uint, bool> isMarketable, Func<string> marketWorld) : IDisposable
{
    private const int MaxVoyagesPerRequest = 50;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActiveBuffRefresh = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VoyageScanDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FetchOpen = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FetchClosed = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record PendingSub(int Slot, string Name, long ReturnTime, SubDetails? Details, DateTime ReadAt);
    private sealed record PendingBuffs(List<BuffReading> Slots, DateTime ReadAt);
    private sealed record PendingActive(List<ActiveBuffReading> Buffs, DateTime ReadAt);
    private sealed record PendingPlot(string Key, string Name, long NextWaterAt, long DieAt, bool Indoor, DateTime ReadAt, long ReadyAt = 0, int Ready = 0);
    private sealed record PendingName(string Key, string Name, bool Indoor, long At);
    private sealed record PendingSupplies(int Tanks, int Kits, DateTime ReadAt);
    private sealed record PendingAirship(int Slot, string Name, long ReturnTime, int Rank, int Exp, DateTime ReadAt);
    private sealed record PendingVisit(string Name, DateTime ReadAt);
    private sealed record SendResult(bool Success, string Status);

    private readonly HttpClient http = NetHttp.Create(TimeSpan.FromSeconds(20));
    private readonly Dictionary<int, string> lastSubs = [];
    private readonly Dictionary<int, PendingSub> pendingSubs = [];
    private readonly Dictionary<int, string> lastAirships = [];
    private readonly Dictionary<int, PendingAirship> pendingAirships = [];
    private List<PendingAirship> sendingAirships = [];
    private PendingVisit? pendingVisit;
    private PendingVisit? sendingVisit;
    private string? lastBuffs;
    private PendingBuffs? pendingBuffs;
    private string? lastActive;
    private DateTime lastActiveAt = DateTime.MinValue;
    private PendingActive? pendingActive;
    private readonly Dictionary<string, string> lastPlots = [];
    private readonly Dictionary<string, PendingPlot> pendingPlots = [];
    private readonly Dictionary<string, PendingName> pendingNames = [];
    private readonly Dictionary<string, long> pendingRemovals = [];
    private List<PendingName> sendingNames = [];
    private List<KeyValuePair<string, long>> sendingRemovals = [];
    private long lastSupplies;
    private PendingSupplies? pendingSupplies;
    private PendingSupplies? sendingSupplies;
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

    // Último envio deu certo. Antes comparava o texto do status, que agora muda com o idioma.
    public bool LastOk { get; private set; } = true;
    private List<KeyValuePair<string, string>> sendingVoyages = [];
    private Task<string>? sendingLog;

    public string Status { get; private set; } = L.StWaiting;
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

    // ID da FC do personagem que está mandando. O serviço descarta o envio de outra FC. 0: não manda o campo.
    public ulong FcId { get; set; }

    private string? FcIdText => FcId == 0 ? null : FcId.ToString();

    public void ReportSub(int slot, string name, long returnTime, SubDetails? details)
    {
        if (!Enabled)
            return;

        var signature = $"{name}|{returnTime}|{details}";
        if (lastSubs.GetValueOrDefault(slot) == signature)
            return;

        lastSubs[slot] = signature;
        pendingSubs[slot] = new PendingSub(slot, name, returnTime, details, DateTime.UtcNow);
    }

    public void ReportAirship(int slot, string name, long returnTime, int rank, int exp)
    {
        var signature = $"{name}|{returnTime}|{rank}|{exp}";
        if (!Enabled || lastAirships.GetValueOrDefault(slot) == signature)
            return;

        lastAirships[slot] = signature;
        pendingAirships[slot] = new PendingAirship(slot, name, returnTime, rank, exp, DateTime.UtcNow);
    }

    // Entrada na casa da FC, para o aviso de 45 dias sem visita.
    public void ReportHouseVisit(long at, string character)
    {
        if (Enabled)
            pendingVisit = new PendingVisit(character, DateTimeOffset.FromUnixTimeSeconds(at).UtcDateTime);
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
    public void ReportPlot(string key, string name, long wateredAt, long nextWaterAt, long dieAt, bool indoor, long readyAt = 0, int ready = 0)
    {
        var signature = $"{name}|{nextWaterAt}|{dieAt}|{indoor}|{readyAt}|{ready}";
        if (!Enabled || wateredAt == 0 || lastPlots.GetValueOrDefault(key) == signature)
            return;

        lastPlots[key] = signature;
        pendingPlots[key] = new PendingPlot(key, name, nextWaterAt, dieAt, indoor, DateTimeOffset.FromUnixTimeSeconds(wateredAt).UtcDateTime, readyAt, ready);
    }

    // Nome dado ou campo cadastrado na janela. O serviço guarda e manda para os outros PCs.
    public void ReportPlotName(string key, string name, bool indoor, long at)
    {
        if (Enabled)
            pendingNames[key] = new PendingName(key, name, indoor, at);
    }

    public void ReportPlotRemoval(string key, long at)
    {
        if (!Enabled)
            return;

        pendingNames.Remove(key);
        pendingRemovals[key] = at;
    }

    // Vale a partir de quando a contagem mudou, e não do envio, para uma contagem antiga não vencer uma nova.
    public void ReportSupplies(int tanks, int kits, long changedAt)
    {
        if (!Enabled || changedAt == 0 || changedAt == lastSupplies)
            return;

        lastSupplies = changedAt;
        pendingSupplies = new PendingSupplies(tanks, kits, DateTimeOffset.FromUnixTimeSeconds(changedAt).UtcDateTime);
    }

    // Manda o log de diagnóstico ao serviço, que guarda só o último de cada personagem.
    public bool SendLog(string character, string text)
    {
        if (!Enabled || sendingLog != null)
            return false;

        var body = JsonSerializer.Serialize(new { character, text, fcId = FcIdText });
        sendingLog = SendLogAsync(body, config.ServiceUrl.Trim().TrimEnd('/'), config.ServiceKey.Trim());
        LogStatus = L.StSendingLog;
        return true;
    }

    // Para quando o plugin está fechando: envia na hora e espera um pouco, sem travar o jogo.
    public void SendLogNow(string character, string text, TimeSpan wait)
    {
        if (!Enabled)
            return;

        var body = JsonSerializer.Serialize(new { character, text, fcId = FcIdText });
        try
        {
            SendLogAsync(body, config.ServiceUrl.Trim().TrimEnd('/'), config.ServiceKey.Trim()).Wait(wait);
        }
        catch (AggregateException)
        {
        }
    }

    private async Task<string> SendLogAsync(string body, string url, string key)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{url}/log");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? L.StLogSent($"{DateTime.Now:HH:mm}") : L.StError((int)response.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return L.StOffline;
        }
    }

    // Busca já na próxima rodada, por exemplo ao abrir a janela.
    public void FetchSoon() => nextFetch = DateTime.MinValue;

    public void Tick()
    {
        TickFetch();

        if (sendingLog is { IsCompleted: true })
        {
            LogStatus = Outcome(sendingLog, L.StError(0));
            sendingLog = null;
            log($"Diagnóstico: {LogStatus}");
        }

        if (sending != null)
        {
            if (!sending.IsCompleted)
                return;

            Finish(sending.IsCompletedSuccessfully ? sending.Result : new SendResult(false, Failure(sending, L.StError(0))));
            sending = null;
        }

        if (!Enabled)
        {
            Status = L.StOff;
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

        if (pendingSubs.Count == 0 && pendingAirships.Count == 0 && pendingVisit == null && pendingBuffs == null && pendingActive == null && pendingPlots.Count == 0 && pendingNames.Count == 0 && pendingRemovals.Count == 0 && pendingSupplies == null && sendingVoyages.Count == 0)
            return;

        sendingSubs = pendingSubs.Values.ToList();
        sendingAirships = pendingAirships.Values.ToList();
        sendingVisit = pendingVisit;
        sendingBuffs = pendingBuffs;
        sendingActive = pendingActive;
        sendingPlots = pendingPlots.Values.ToList();
        sendingNames = pendingNames.Values.ToList();
        sendingRemovals = pendingRemovals.ToList();
        sendingSupplies = pendingSupplies;
        sending = SendAsync(BuildBody(DateTime.UtcNow), config.ServiceUrl.Trim().TrimEnd('/'), config.ServiceKey.Trim());
    }

    private string BuildBody(DateTime now)
    {
        var body = new
        {
            fcId = FcIdText,
            subs = sendingSubs.Select(s => new
            {
                slot = s.Slot,
                name = s.Name,
                returnTime = s.ReturnTime,
                age = Age(s.ReadAt, now),
                details = s.Details == null ? null : new
                {
                    rank = s.Details.Rank,
                    exp = s.Details.ExpPercent,
                    build = s.Details.Build,
                    route = s.Details.Route,
                    tanks = s.Details.TanksPerVoyage,
                    kits = s.Details.RepairKits,
                    condition = s.Details.Condition == null ? (double?)null : Math.Round(s.Details.Condition.Value, 1),
                },
            }),
            airships = sendingAirships.Select(s => new { slot = s.Slot, name = s.Name, returnTime = s.ReturnTime, rank = s.Rank, exp = s.Exp, age = Age(s.ReadAt, now) }),
            houseVisit = sendingVisit == null ? null : new { name = sendingVisit.Name, age = Age(sendingVisit.ReadAt, now) },
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
            supplies = sendingSupplies == null ? null : new { tanks = sendingSupplies.Tanks, kits = sendingSupplies.Kits, age = Age(sendingSupplies.ReadAt, now) },
            plotNames = sendingNames.Select(n => new { key = n.Key, name = n.Name, indoor = n.Indoor, at = n.At }),
            plotRemovals = sendingRemovals.Select(r => new { key = r.Key, at = r.Value }),
            plots = sendingPlots.Select(p => new { key = p.Key, name = p.Name, nextWaterAt = p.NextWaterAt, dieAt = p.DieAt, indoor = p.Indoor, readyAt = p.ReadyAt, ready = p.Ready, age = Age(p.ReadAt, now) }),
            voyages = sendingVoyages.Select(v =>
            {
                var voyage = config.Voyages[v.Key];
                return new
                {
                    slot = LootLedger.SlotOf(v.Key),
                    airship = LootLedger.IsAirship(v.Key),
                    subName = voyage.SubName,
                    returnTime = voyage.ReturnTime,
                    movedFrom = voyage.MovedFrom == 0 ? (long?)null : voyage.MovedFrom,
                    world = marketWorld(),
                    unlocked = voyage.Unlocked,
                    items = voyage.Items.Select(i => new { itemId = i.ItemId, name = i.Name, count = i.Count, value = i.Value, market = isMarketable(i.ItemId) }),
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
            // 202: envio de personagem de outra FC, aceito e descartado pelo serviço. Conta como enviado.
            return response.IsSuccessStatusCode
                ? new SendResult(true, L.StConnected)
                : response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? new SendResult(false, L.LinkRequested)
                : response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? new SendResult(false, L.LinkExpired)
                    : new SendResult(false, L.StError((int)response.StatusCode));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new SendResult(false, L.StOffline);
        }
    }

    private void TickFetch()
    {
        if (fetching != null)
        {
            if (!fetching.IsCompleted)
                return;

            var state = fetching.IsCompletedSuccessfully ? fetching.Result : null;
            if (fetching.IsFaulted)
                log(Failure(fetching, "Erro ao receber"));
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
        LastOk = result.Success;
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

        foreach (var ship in sendingAirships.Where(s => ReferenceEquals(pendingAirships.GetValueOrDefault(s.Slot), s)))
            pendingAirships.Remove(ship.Slot);

        if (ReferenceEquals(pendingVisit, sendingVisit))
            pendingVisit = null;

        if (ReferenceEquals(pendingBuffs, sendingBuffs))
            pendingBuffs = null;

        if (ReferenceEquals(pendingActive, sendingActive))
            pendingActive = null;

        if (ReferenceEquals(pendingSupplies, sendingSupplies))
            pendingSupplies = null;

        foreach (var name in sendingNames.Where(n => ReferenceEquals(pendingNames.GetValueOrDefault(n.Key), n)))
            pendingNames.Remove(name.Key);

        foreach (var removal in sendingRemovals.Where(r => pendingRemovals.GetValueOrDefault(r.Key) == r.Value))
            pendingRemovals.Remove(removal.Key);

        foreach (var plot in sendingPlots.Where(p => ReferenceEquals(pendingPlots.GetValueOrDefault(p.Key), p)))
            pendingPlots.Remove(plot.Key);

        if (sendingVoyages.Count > 0)
            log($"Serviço: {sendingVoyages.Count} viagem(ns) enviada(s)");

        foreach (var voyage in sendingVoyages)
            config.SyncedVoyages[voyage.Key] = voyage.Value;

        if (sendingVoyages.Count > 0)
            save();
    }

    // Uma tarefa que terminou com erro vira uma mensagem, em vez de jogar a exceção na thread do jogo.
    private static string Outcome(Task<string> task, string error) => task.IsCompletedSuccessfully ? task.Result : Failure(task, error);

    private static string Failure(Task task, string error)
        => $"{error}: {task.Exception?.GetBaseException().Message ?? "cancelado"}";

    private static long Age(DateTime readAt, DateTime now) => Math.Max(0, (long)(now - readAt).TotalSeconds);

    private static string Signature(VoyageRecord voyage)
        => string.Join(';', voyage.Items.Select(i => $"{i.ItemId}|{i.Count}|{i.Value}"));
}
