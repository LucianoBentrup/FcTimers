using System;
using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

[Serializable]
public class BedRecord
{
    // Linha da sheet GardeningSeed.
    public ushort Seed { get; set; }

    // Fase lida da memória do jogo. 4: pronta para colher.
    public byte Phase { get; set; }

    // 0: plantada antes de o plugin ver o canteiro.
    public long PlantedAt { get; set; }

    // Só se sabe que foi plantada entre duas leituras distantes.
    public bool PlantedApprox { get; set; }

    public long TendedAt { get; set; }
    public List<long> Fertilized { get; set; } = [];

    // Quando o plugin viu a fase virar pronta.
    public long ReadyAt { get; set; }
}

[Serializable]
public class BedEvent
{
    public long At { get; set; }
    public int Bed { get; set; }
    public BedEventKind Kind { get; set; }
    public ushort Seed { get; set; }
}

public enum BedEventKind
{
    Planted,
    Ready,
    Harvested,
    Removed,
    Fertilized,
}

public readonly record struct BedSlot(ushort Seed, byte Phase);

public readonly record struct BedSummary(int Ready, int Growing, long NextWaterAt, long DieAt, long NextReadyAt);

public static class Beds
{
    public const byte ReadyPhase = 4;
    public const int MaxHistory = 40;
    private const long ApproxAfterSeconds = 600;
    private const double FertilizerCut = 0.01;

    // Horas para crescer e para murchar sem cuidado, por linha da GardeningSeed. 0: sem dado.
    // Tabela da comunidade (lotlab/ffxiv-gardening-tracker, seeds_time.json).
    private static readonly Dictionary<ushort, (int Grow, int Wilt)> Times = new()
    {
        [1] = (120, 48), [2] = (120, 48), [3] = (120, 48), [4] = (120, 48), [5] = (120, 48), [6] = (120, 48),
        [7] = (120, 48), [8] = (120, 48), [9] = (120, 48), [10] = (120, 48), [11] = (120, 48), [12] = (120, 48),
        [13] = (120, 48), [14] = (120, 48), [15] = (120, 48), [16] = (120, 48), [17] = (120, 48), [18] = (120, 48),
        [19] = (120, 48), [20] = (120, 48), [21] = (120, 48), [22] = (120, 48), [23] = (120, 48), [24] = (120, 48),
        [25] = (120, 48), [26] = (120, 48), [27] = (120, 48), [28] = (120, 48), [29] = (120, 48), [30] = (120, 48),
        [31] = (144, 36), [32] = (144, 36), [33] = (144, 36), [34] = (144, 36), [35] = (144, 36), [36] = (144, 36),
        [37] = (144, 36), [38] = (144, 36), [39] = (144, 36), [40] = (168, 24), [41] = (168, 24), [42] = (168, 24),
        [43] = (168, 24), [44] = (120, 48), [45] = (120, 48), [46] = (120, 48), [47] = (120, 48), [48] = (120, 48),
        [49] = (72, 24), [50] = (240, 24), [51] = (168, 24), [52] = (168, 24), [53] = (168, 24), [54] = (168, 24),
        [55] = (168, 24), [56] = (144, 36), [57] = (72, 24), [58] = (48, 0), [59] = (48, 0), [60] = (48, 0),
        [61] = (48, 0), [62] = (48, 0), [63] = (48, 0), [64] = (48, 0), [65] = (120, 48), [66] = (120, 48),
        [67] = (120, 48), [68] = (120, 48), [69] = (168, 24), [70] = (24, 0), [71] = (18, 0), [72] = (18, 0),
        [73] = (18, 0), [74] = (18, 0), [75] = (18, 0), [76] = (18, 0), [77] = (22, 0), [78] = (12, 0), [79] = (24, 0),
        [80] = (24, 0), [81] = (24, 0), [82] = (24, 0), [83] = (24, 0), [84] = (120, 48), [85] = (120, 24),
        [86] = (24, 0), [87] = (24, 0), [88] = (24, 0), [89] = (24, 0), [90] = (24, 0), [91] = (24, 0), [92] = (24, 0),
        [93] = (24, 0), [94] = (168, 24), [95] = (24, 0), [96] = (24, 0), [97] = (24, 0), [98] = (24, 0),
        [99] = (24, 0), [100] = (24, 0), [101] = (24, 0), [102] = (24, 0), [103] = (24, 0), [104] = (24, 0),
        [105] = (24, 0), [106] = (24, 0), [107] = (24, 0), [108] = (24, 0)
    };

    public static long GrowSeconds(ushort seed) => Times.TryGetValue(seed, out var t) ? t.Grow * 3600L : 0;

    // Murcha no prazo da semente e morre 24 horas depois. Sem dado, o caso mais curto.
    public static long DieAfterSeconds(ushort seed)
        => Times.TryGetValue(seed, out var t) && t.Wilt > 0 ? (t.Wilt + 24) * 3600L : PlotRecord.DieAfterSeconds;

    public static bool IsReady(BedRecord bed) => bed.Phase == ReadyPhase;

    // Cada adubo tira 1% do que faltava naquele momento.
    public static long EstimatedReadyAt(BedRecord bed)
    {
        var grow = GrowSeconds(bed.Seed);
        if (bed.PlantedAt == 0 || grow == 0)
            return 0;

        double readyAt = bed.PlantedAt + grow;
        foreach (var at in bed.Fertilized.Order())
        {
            var remaining = readyAt - at;
            if (remaining > 0)
                readyAt -= remaining * FertilizerCut;
        }

        return (long)readyAt;
    }

    public static long LastCare(BedRecord bed) => Math.Max(bed.TendedAt, bed.PlantedAt);

    // Compara a leitura com o último estado e guarda só o que mudou. plantedAt diz quando o próprio jogador
    // plantou naquela vaga, pela mensagem do jogo.
    public static List<BedEvent> Apply(PlotRecord plot, IReadOnlyList<BedSlot> slots, long now, Func<int, long?>? plantedAt = null)
    {
        var events = new List<BedEvent>();
        var firstRead = plot.BedsReadAt == 0;
        for (var i = 0; i < slots.Count; i++)
        {
            var number = i + 1;
            var slot = slots[i];
            plot.Beds.TryGetValue(number, out var old);

            if (slot.Seed == 0)
            {
                if (old != null)
                {
                    events.Add(new BedEvent { At = now, Bed = number, Seed = old.Seed, Kind = IsReady(old) ? BedEventKind.Harvested : BedEventKind.Removed });
                    plot.Beds.Remove(number);
                }

                continue;
            }

            if (old == null || old.Seed != slot.Seed)
            {
                var bed = new BedRecord { Seed = slot.Seed, Phase = slot.Phase };
                if (plantedAt?.Invoke(number) is { } mine)
                {
                    bed.PlantedAt = mine;
                }
                else if (!firstRead)
                {
                    bed.PlantedAt = now;
                    bed.PlantedApprox = now - plot.BedsReadAt > ApproxAfterSeconds;
                }

                // Na primeira leitura, a última rega conhecida do campo vale para todas as vagas.
                bed.TendedAt = firstRead ? plot.WateredAt : bed.PlantedAt;
                if (IsReady(bed) && !firstRead)
                    bed.ReadyAt = now;
                plot.Beds[number] = bed;
                if (old != null)
                    events.Add(new BedEvent { At = now, Bed = number, Seed = old.Seed, Kind = IsReady(old) ? BedEventKind.Harvested : BedEventKind.Removed });
                if (!firstRead)
                    events.Add(new BedEvent { At = bed.PlantedAt, Bed = number, Seed = bed.Seed, Kind = BedEventKind.Planted });
                continue;
            }

            if (old.Phase != slot.Phase)
            {
                if (slot.Phase == ReadyPhase)
                {
                    old.ReadyAt = now;
                    events.Add(new BedEvent { At = now, Bed = number, Seed = old.Seed, Kind = BedEventKind.Ready });
                }

                old.Phase = slot.Phase;
            }
        }

        plot.BedsReadAt = now;
        AddHistory(plot, events);
        return events;
    }

    // bed 0: vale para todas as vagas (vaso, ou canteiro sem o número do Bed).
    public static void Tend(PlotRecord plot, int bed, long now)
    {
        foreach (var record in Targets(plot, bed))
            record.TendedAt = now;
    }

    public static void Fertilize(PlotRecord plot, int bed, long now)
    {
        var events = new List<BedEvent>();
        foreach (var (number, record) in plot.Beds.Where(b => bed == 0 || b.Key == bed))
        {
            record.Fertilized.Add(now);
            events.Add(new BedEvent { At = now, Bed = number, Seed = record.Seed, Kind = BedEventKind.Fertilized });
        }

        AddHistory(plot, events);
    }

    private static IEnumerable<BedRecord> Targets(PlotRecord plot, int bed)
        => bed == 0 ? plot.Beds.Values : plot.Beds.TryGetValue(bed, out var one) ? [one] : [];

    private static void AddHistory(PlotRecord plot, List<BedEvent> events)
    {
        if (events.Count == 0)
            return;
        plot.History.AddRange(events);
        if (plot.History.Count > MaxHistory)
            plot.History.RemoveRange(0, plot.History.Count - MaxHistory);
    }

    // Horário do aviso de colheita: o próximo Bed com previsão, ou, sem previsão, quando o plugin viu ficar pronta.
    // Bed com previsão que já ficou pronto não avisa de novo.
    public static long HarvestAlertAt(PlotRecord plot)
    {
        var upcoming = plot.Beds.Values.Where(b => !IsReady(b)).Select(EstimatedReadyAt).Where(t => t > 0).DefaultIfEmpty(0).Min();
        if (upcoming > 0)
            return upcoming;
        return plot.Beds.Values.Where(b => IsReady(b) && EstimatedReadyAt(b) == 0).Select(b => b.ReadyAt).DefaultIfEmpty(0).Max();
    }

    // Rega e morte pelo Bed mais atrasado; próxima colheita pelo primeiro Bed a ficar pronto.
    public static BedSummary Summarize(PlotRecord plot, long now)
    {
        if (plot.Beds.Count == 0)
            return new BedSummary(0, 0, plot.WateredAt == 0 ? 0 : plot.NextWaterAt, plot.WateredAt == 0 ? 0 : plot.DieAt, 0);

        var ready = 0;
        var growing = 0;
        var nextWater = long.MaxValue;
        var die = long.MaxValue;
        var nextReady = long.MaxValue;
        foreach (var bed in plot.Beds.Values)
        {
            if (IsReady(bed))
            {
                ready++;
                continue;
            }

            growing++;
            var care = LastCare(bed);
            if (care > 0)
            {
                nextWater = Math.Min(nextWater, care + PlotRecord.WaterIntervalSeconds);
                die = Math.Min(die, care + DieAfterSeconds(bed.Seed));
            }

            var estimate = EstimatedReadyAt(bed);
            if (estimate > 0)
                nextReady = Math.Min(nextReady, estimate);
        }

        return new BedSummary(
            ready,
            growing,
            nextWater == long.MaxValue ? 0 : nextWater,
            die == long.MaxValue ? 0 : die,
            nextReady == long.MaxValue ? 0 : nextReady);
    }
}
