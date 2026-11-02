using System;
using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

// O que o serviço devolve em GET /state. Os horários de leitura usam o relógio do servidor.
public sealed class RemoteState
{
    public long Now { get; set; }
    public Dictionary<string, RemoteSub> Subs { get; set; } = [];
    public Dictionary<string, RemoteWheel> Buffs { get; set; } = [];
    public long BuffsObservedAt { get; set; }
    public RemoteActive? ActiveBuffs { get; set; }
    public Dictionary<string, RemotePlot> Plots { get; set; } = [];
    public List<RemoteVoyage> Voyages { get; set; } = [];
}

public sealed class RemoteSub
{
    public string Name { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public long ObservedAt { get; set; }
}

public sealed class RemoteWheel
{
    public string Name { get; set; } = string.Empty;
    public long ReadyTime { get; set; }
}

public sealed class RemoteActive
{
    public List<RemoteActiveBuff> Buffs { get; set; } = [];
    public long ObservedAt { get; set; }
}

public sealed class RemoteActiveBuff
{
    public string Name { get; set; } = string.Empty;
    public long? EndTime { get; set; }
}

public sealed class RemotePlot
{
    public string Name { get; set; } = string.Empty;
    public long NextWaterAt { get; set; }
    public long DieAt { get; set; }
    public long ObservedAt { get; set; }
}

public sealed class RemoteVoyage
{
    public int Slot { get; set; }
    public string SubName { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public List<LootItem> Items { get; set; } = [];
}

public readonly record struct SubView(int Slot, string Name, long ReturnTime);

public readonly record struct WheelView(int Slot, string Name, long ReadyTime);

public readonly record struct VoyageView(int Slot, VoyageRecord Voyage);

public readonly record struct PlotView(string Key, string Name, long NextWaterAt, long DieAt);

// Horários de leitura deste PC, só em memória. Depois de reiniciar, o serviço vale até a próxima leitura.
public sealed class LocalReadings
{
    public Dictionary<int, long> Subs { get; } = [];
    public long Wheels { get; set; }
    public long ActiveBuffs { get; set; }
}

// Junta o que este PC leu com o que o serviço recebeu dos outros. Vale sempre a leitura mais recente.
public sealed class FcView
{
    private const long PlotStaleSeconds = 2 * 86400;

    public List<SubView> Subs { get; private init; } = [];
    public List<WheelView> Wheels { get; private init; } = [];
    public List<ActiveBuffRecord> ActiveBuffs { get; private init; } = [];
    public List<PlotView> Plots { get; private init; } = [];
    public List<VoyageView> Voyages { get; private init; } = [];

    public static FcView Build(Configuration config, LocalReadings local, RemoteState? remote, long remoteOffset)
    {
        // O relógio do servidor pode estar alguns segundos diferente do PC.
        long Local(long serverTime) => serverTime == 0 ? 0 : serverTime + remoteOffset;

        var subs = new Dictionary<int, (SubView View, long ObservedAt)>();
        foreach (var (key, record) in config.Subs)
        {
            var slot = int.Parse(key.Split(':')[^1]);
            subs[slot] = (new SubView(slot, record.Name, record.ReturnTime), local.Subs.GetValueOrDefault(slot));
        }

        foreach (var (slotText, sub) in remote?.Subs ?? [])
        {
            if (!int.TryParse(slotText, out var slot) || slot >= LootLedger.MaxSubmarines)
                continue;
            if (!subs.TryGetValue(slot, out var current) || Local(sub.ObservedAt) > current.ObservedAt)
                subs[slot] = (new SubView(slot, sub.Name, sub.ReturnTime), Local(sub.ObservedAt));
        }

        var wheels = remote != null && Local(remote.BuffsObservedAt) > local.Wheels
            ? remote.Buffs.Select(b => new WheelView(int.TryParse(b.Key, out var s) ? s : 0, b.Value.Name, b.Value.ReadyTime)).ToList()
            : config.Wheels.Select(w => new WheelView(int.Parse(w.Key.Split(':')[^1]), w.Value.Name, w.Value.ReadyTime)).ToList();

        var active = remote?.ActiveBuffs != null && Local(remote.ActiveBuffs.ObservedAt) > local.ActiveBuffs
            ? remote.ActiveBuffs.Buffs.Select(b => new ActiveBuffRecord { Name = b.Name, EndTime = b.EndTime ?? 0 }).ToList()
            : config.ActiveFcBuffs;

        // Cada plot fica com a rega mais recente, seja deste PC ou de outro membro.
        // Um plot morto há mais de 2 dias some, como no painel.
        var plots = new Dictionary<string, (PlotView View, long WateredAt)>();
        foreach (var (key, plot) in config.Plots.Where(p => p.Value.WateredAt != 0))
        {
            var view = new PlotView(key, plot.Name, plot.WateredAt + (config.WaterIntervalHours * 3600L), plot.WateredAt + (config.DieAfterHours * 3600L));
            plots[key] = (view, plot.WateredAt);
        }

        foreach (var (key, plot) in remote?.Plots ?? [])
        {
            if (!plots.TryGetValue(key, out var current) || Local(plot.ObservedAt) > current.WateredAt)
                plots[key] = (new PlotView(key, plot.Name, plot.NextWaterAt, plot.DieAt), Local(plot.ObservedAt));
        }

        var staleBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - PlotStaleSeconds;

        // A mesma viagem lida aqui e por outro membro conta uma vez, ficando a leitura com mais itens.
        var voyages = new Dictionary<(int, long), VoyageView>();
        void AddVoyage(int slot, VoyageRecord voyage)
        {
            if (!voyages.TryGetValue((slot, voyage.ReturnTime), out var known) || voyage.Items.Count > known.Voyage.Items.Count)
                voyages[(slot, voyage.ReturnTime)] = new VoyageView(slot, voyage);
        }

        foreach (var (key, voyage) in config.Voyages)
            AddVoyage(LootLedger.SlotOf(key), voyage);
        foreach (var voyage in remote?.Voyages ?? [])
        {
            if (voyage.Slot < LootLedger.MaxSubmarines)
                AddVoyage(voyage.Slot, new VoyageRecord { SubName = voyage.SubName, ReturnTime = voyage.ReturnTime, Items = voyage.Items });
        }

        return new FcView
        {
            Subs = subs.Values.Select(s => s.View).OrderBy(s => s.Slot).ToList(),
            Wheels = wheels.OrderBy(w => w.Slot).ToList(),
            ActiveBuffs = active,
            Plots = plots.Values.Select(p => p.View)
                .Where(p => p.DieAt == 0 || p.DieAt >= staleBefore)
                .OrderBy(p => p.NextWaterAt).ThenBy(p => p.Name).ToList(),
            Voyages = voyages.Values.ToList(),
        };
    }
}
