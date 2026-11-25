using System;
using System.Linq;
using Xunit;

namespace FcTimers.Tests;

public class LootSummaryTests
{
    private static VoyageView Voyage(string sub, DateTime day, params LootItem[] items)
        => new(0, new VoyageRecord { SubName = sub, ReturnTime = new DateTimeOffset(day.AddHours(12), TimeSpan.Zero).ToUnixTimeSeconds(), Items = [.. items] });

    [Fact]
    public void Soma_o_dia_por_item_e_por_sub_com_o_mais_valioso_primeiro()
    {
        var today = new DateTime(2026, 11, 25);
        var voyages = new[]
        {
            Voyage("Navi", today, new LootItem { ItemId = 1, Name = "Pedra", Count = 2, Value = 100 }, new LootItem { ItemId = 2, Name = "Joia", Count = 1, Value = 5000 }),
            Voyage("Tael", today, new LootItem { ItemId = 1, Name = "Pedra", Count = 3, Value = 150 }),
            Voyage("Navi", today.AddDays(-1), new LootItem { ItemId = 1, Name = "Pedra", Count = 1, Value = 50 }),
        };

        var summary = LootSummary.Build(voyages, today, TimeZoneInfo.Utc);

        Assert.Equal(2, summary.Days.Count);
        var day = summary.Today!;
        Assert.Equal(5250, day.Total);
        Assert.Equal(2, day.Voyages);
        Assert.Equal("Joia", day.Items[0].Name);
        Assert.Equal(5, day.Items.Single(i => i.Name == "Pedra").Count);
        Assert.Equal([("Navi", 5100L), ("Tael", 150L)], day.BySub);
    }

    [Fact]
    public void Sem_viagem_hoje_nao_ha_dia_de_hoje()
    {
        var today = new DateTime(2026, 11, 25);
        var summary = LootSummary.Build([Voyage("Navi", today.AddDays(-2), new LootItem { Name = "Pedra", Count = 1, Value = 1 })], today, TimeZoneInfo.Utc);

        Assert.Null(summary.Today);
        Assert.Single(summary.Days);
    }
}
