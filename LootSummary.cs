using System;
using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

public readonly record struct LootLine(string Name, uint ItemId, int Count, long Value, bool Market);

public sealed record LootDay(DateTime Day, int Voyages, long Total, long Market, List<LootLine> Items, List<(string Sub, long Gil)> BySub);

// Loot agrupado por dia, já somado. A janela monta isso uma vez por visão nova, e não a cada frame,
// porque converter o dia de cada viagem dos últimos 120 dias a cada frame pesa no jogo.
public sealed class LootSummary
{
    public const int DaysShown = 7;

    public List<LootDay> Days { get; private init; } = [];
    public LootDay? Today { get; private init; }

    public static LootSummary Build(IEnumerable<VoyageView> voyages, DateTime today, TimeZoneInfo zone)
    {
        var days = voyages.Select(v => v.Voyage)
            .GroupBy(v => LootLedger.DayOf(v, zone))
            .OrderByDescending(d => d.Key)
            .Take(DaysShown)
            .Select(day =>
            {
                var all = day.SelectMany(v => v.Items).ToList();
                var items = all.GroupBy(i => i.Name)
                    .Select(g => new LootLine(g.Key, g.First().ItemId, g.Sum(i => i.Count), g.Sum(i => i.Best), g.Any(i => i.IsMarket)))
                    .OrderByDescending(i => i.Value)
                    .ToList();
                var bySub = day.GroupBy(v => v.SubName).Select(g => (g.Key, g.Sum(v => v.Items.Sum(i => i.Best)))).ToList();
                return new LootDay(day.Key, day.Count(), all.Sum(i => i.Best), all.Where(i => i.IsMarket).Sum(i => i.Best), items, bySub);
            })
            .ToList();

        return new LootSummary { Days = days, Today = days.FirstOrDefault(d => d.Day == today.Date) };
    }
}
