using System;
using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

// O que o serviço devolve em GET /state. Os horários de leitura usam o relógio do servidor.
public sealed class RemoteState
{
    public long Now { get; set; }
    public Dictionary<string, RemoteSub> Subs { get; set; } = [];
    public Dictionary<string, RemoteAirship> Airships { get; set; } = [];
    public RemoteHouse? House { get; set; }
    public RemoteForecast? Forecast { get; set; }
    public RemoteProfit? Profit { get; set; }
    public Dictionary<string, RemoteWheel> Buffs { get; set; } = [];
    public long BuffsObservedAt { get; set; }
    public RemoteActive? ActiveBuffs { get; set; }
    public Dictionary<string, RemotePlot> Plots { get; set; } = [];
    public Dictionary<string, long> RemovedPlots { get; set; } = [];
    public RemoteSupplies? Supplies { get; set; }
    public List<RemoteVoyage> Voyages { get; set; } = [];
}

public sealed class RemoteSub
{
    public string Name { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public long ObservedAt { get; set; }
    public RemoteSubDetails? Details { get; set; }
}

public sealed class RemoteSubDetails
{
    public int Rank { get; set; }
    public int Exp { get; set; }
    public string Build { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int Tanks { get; set; }
    public int Kits { get; set; }
    public double? Condition { get; set; }

    public SubDetails ToDetails() => new()
    {
        Rank = Rank, ExpPercent = Exp, Build = Build, Route = Route, TanksPerVoyage = Tanks, RepairKits = Kits, Condition = Condition,
    };
}

public sealed class RemoteAirship
{
    public string Name { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public long ObservedAt { get; set; }
    public int Rank { get; set; }
    public int Exp { get; set; }
}

public sealed class RemoteHouse
{
    public long VisitedAt { get; set; }
    public string? By { get; set; }
}

// Quando o estoque acaba, pelo gasto dos últimos dias. 0: sem previsão.
public sealed class RemoteForecast
{
    public long TanksUntil { get; set; }
    public long KitsUntil { get; set; }
}

public sealed class RemoteProfit
{
    public List<RemoteWeek> Weeks { get; set; } = [];
    public List<RemoteRoute> Routes { get; set; } = [];
}

public sealed class RemoteWeek
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public long Gil { get; set; }
    public int Voyages { get; set; }
}

public sealed class RemoteRoute
{
    public string Route { get; set; } = string.Empty;
    public int Voyages { get; set; }
    public long PerVoyage { get; set; }
    public long PerTank { get; set; }
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
    public long NameAt { get; set; }
    public bool Indoor { get; set; }
}

public sealed class RemoteSupplies
{
    public int Tanks { get; set; }
    public int Kits { get; set; }
    public long ObservedAt { get; set; }
}

public sealed class RemoteVoyage
{
    public int Slot { get; set; }
    public bool Airship { get; set; }
    public string SubName { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public List<LootItem> Items { get; set; } = [];
    public List<string> Unlocked { get; set; } = [];
}

// Parts e DepartedAt só existem para os subs lidos neste PC.
public readonly record struct SubView(int Slot, string Name, long ReturnTime, SubDetails? Details, IReadOnlyList<PartRecord>? Parts = null, long DepartedAt = 0);

public readonly record struct WheelView(int Slot, string Name, long ReadyTime);

public readonly record struct VoyageView(int Slot, VoyageRecord Voyage, bool Airship = false);

// Última entrada na casa da FC, deste PC ou de outro membro.
public readonly record struct HouseView(long VisitedAt, string? By);

public readonly record struct PlotView(string Key, string Name, long NextWaterAt, long DieAt);

public readonly record struct SupplyView(int Tanks, int Kits, long ObservedAt);

// Horários de leitura deste PC, só em memória. Depois de reiniciar, o serviço vale até a próxima leitura.
public sealed class LocalReadings
{
    public Dictionary<int, long> Subs { get; } = [];
    public Dictionary<int, long> Airships { get; } = [];
    public long Wheels { get; set; }
    public long ActiveBuffs { get; set; }
}

// Junta o que este PC leu com o que o serviço recebeu dos outros. Vale sempre a leitura mais recente.
public sealed class FcView
{
    private const long PlotStaleSeconds = 2 * 86400;

    public List<SubView> Subs { get; private init; } = [];
    public List<SubView> Airships { get; private init; } = [];
    public HouseView? House { get; private init; }
    public RemoteForecast? Forecast { get; private init; }
    public RemoteProfit? Profit { get; private init; }
    public List<WheelView> Wheels { get; private init; } = [];
    public List<ActiveBuffRecord> ActiveBuffs { get; private init; } = [];
    public List<PlotView> Plots { get; private init; } = [];
    public SupplyView? Supplies { get; private init; }
    public List<VoyageView> Voyages { get; private init; } = [];

    public static FcView Build(Configuration config, LocalReadings local, RemoteState? remote, long remoteOffset)
    {
        // O relógio do servidor pode estar alguns segundos diferente do PC.
        long Local(long serverTime) => serverTime == 0 ? 0 : serverTime + remoteOffset;

        var subs = new Dictionary<int, (SubView View, long ObservedAt)>();
        foreach (var (key, record) in config.Subs)
        {
            var slot = int.Parse(key.Split(':')[^1]);
            subs[slot] = (new SubView(slot, record.Name, record.ReturnTime, record.Details, record.Parts, record.DepartedAt), local.Subs.GetValueOrDefault(slot));
        }

        foreach (var (slotText, sub) in remote?.Subs ?? [])
        {
            if (!int.TryParse(slotText, out var slot) || slot >= LootLedger.MaxSubmarines)
                continue;
            // As peças são as mesmas de antes. A saída só vale se a volta não mudou, senão é de outra viagem.
            if (!subs.TryGetValue(slot, out var current) || Local(sub.ObservedAt) > current.ObservedAt)
                subs[slot] = (new SubView(slot, sub.Name, sub.ReturnTime, sub.Details?.ToDetails() ?? current.View.Details, current.View.Parts,
                    current.View.ReturnTime == sub.ReturnTime ? current.View.DepartedAt : 0), Local(sub.ObservedAt));
        }

        var airships = new Dictionary<int, (SubView View, long ObservedAt)>();
        foreach (var (key, record) in config.Airships)
        {
            var slot = int.Parse(key.Split(':')[^1]);
            airships[slot] = (new SubView(slot, record.Name, record.ReturnTime, record.Details), local.Airships.GetValueOrDefault(slot));
        }

        foreach (var (slotText, ship) in remote?.Airships ?? [])
        {
            if (!int.TryParse(slotText, out var slot) || slot >= LootLedger.MaxSubmarines)
                continue;
            if (!airships.TryGetValue(slot, out var current) || Local(ship.ObservedAt) > current.ObservedAt)
            {
                var details = new SubDetails { Rank = ship.Rank, ExpPercent = ship.Exp };
                airships[slot] = (new SubView(slot, ship.Name, ship.ReturnTime, details), Local(ship.ObservedAt));
            }
        }

        HouseView? house = config.HouseVisitAt == 0 ? null : new HouseView(config.HouseVisitAt, null);
        if (remote?.House is { VisitedAt: > 0 } sharedHouse && Local(sharedHouse.VisitedAt) > (house?.VisitedAt ?? 0))
            house = new HouseView(Local(sharedHouse.VisitedAt), sharedHouse.By);

        var wheels = remote != null && Local(remote.BuffsObservedAt) > local.Wheels
            ? remote.Buffs.Select(b => new WheelView(int.TryParse(b.Key, out var s) ? s : 0, b.Value.Name, b.Value.ReadyTime)).ToList()
            : config.Wheels.Select(w => new WheelView(int.Parse(w.Key.Split(':')[^1]), w.Value.Name, w.Value.ReadyTime)).ToList();

        var active = remote?.ActiveBuffs != null && Local(remote.ActiveBuffs.ObservedAt) > local.ActiveBuffs
            ? remote.ActiveBuffs.Buffs.Select(b => new ActiveBuffRecord { Name = b.Name, EndTime = b.EndTime ?? 0 }).ToList()
            : config.ActiveFcBuffs;

        // Cada plot fica com a rega mais recente, seja deste PC ou de outro membro.
        // Um plot morto há mais de 2 dias some, como no painel.
        var plots = new Dictionary<string, (PlotView View, long WateredAt)>();
        // Campo cadastrado e ainda sem rega entra com horários zerados.
        foreach (var (key, plot) in config.Plots)
        {
            var view = plot.WateredAt == 0 ? new PlotView(key, plot.Name, 0, 0) : new PlotView(key, plot.Name, plot.NextWaterAt, plot.DieAt);
            plots[key] = (view, plot.WateredAt);
        }

        foreach (var (key, plot) in remote?.Plots ?? [])
        {
            if (!plots.TryGetValue(key, out var current) || Local(plot.ObservedAt) > current.WateredAt)
                plots[key] = (new PlotView(key, plot.Name, plot.NextWaterAt, plot.DieAt), Local(plot.ObservedAt));
        }

        var staleBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - PlotStaleSeconds;

        // O plot geral some quando algum canteiro identificado é regado depois dele, como no painel.
        if (plots.TryGetValue("geral", out var general) && plots.Any(p => p.Key != "geral" && p.Value.WateredAt > general.WateredAt))
            plots.Remove("geral");

        var stock = config.Supplies;
        SupplyView? supplies = stock.ChangedAt == 0 ? null : new SupplyView(stock.Tanks, stock.Kits, stock.ChangedAt);
        if (remote?.Supplies is { } shared && Local(shared.ObservedAt) > (supplies?.ObservedAt ?? 0))
            supplies = new SupplyView(shared.Tanks, shared.Kits, Local(shared.ObservedAt));

        // A mesma viagem lida aqui e por outro membro conta uma vez, ficando a leitura com mais itens.
        var voyages = new Dictionary<(bool, int, long), VoyageView>();
        void AddVoyage(int slot, bool airship, VoyageRecord voyage)
        {
            // Com a mesma quantidade de itens, vale a leitura que já tem o preço do market.
            var key = (airship, slot, voyage.ReturnTime);
            if (!voyages.TryGetValue(key, out var known) || voyage.Items.Count > known.Voyage.Items.Count
                || (voyage.Items.Count == known.Voyage.Items.Count && voyage.Items.Any(i => i.MarketValue != null) && known.Voyage.Items.All(i => i.MarketValue == null)))
                voyages[key] = new VoyageView(slot, voyage, airship);
        }

        foreach (var (key, voyage) in config.Voyages)
            AddVoyage(LootLedger.SlotOf(key), LootLedger.IsAirship(key), voyage);
        foreach (var voyage in remote?.Voyages ?? [])
        {
            if (voyage.Slot < LootLedger.MaxSubmarines)
                AddVoyage(voyage.Slot, voyage.Airship, new VoyageRecord { SubName = voyage.SubName, ReturnTime = voyage.ReturnTime, Items = voyage.Items, Unlocked = voyage.Unlocked });
        }

        return new FcView
        {
            Subs = subs.Values.Select(s => s.View).OrderBy(s => s.Slot).ToList(),
            Airships = airships.Values.Select(s => s.View).OrderBy(s => s.Slot).ToList(),
            House = house,
            Forecast = remote?.Forecast,
            Profit = remote?.Profit,
            Wheels = wheels.OrderBy(w => w.Slot).ToList(),
            ActiveBuffs = active,
            Plots = plots.Values.Select(p => p.View)
                .Where(p => p.DieAt == 0 || p.DieAt >= staleBefore)
                .OrderBy(p => p.NextWaterAt == 0).ThenBy(p => p.NextWaterAt).ThenBy(p => p.Name).ToList(),
            Supplies = supplies,
            Voyages = voyages.Values.ToList(),
        };
    }
}
