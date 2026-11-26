using System;
using System.Collections.Generic;
using Xunit;

namespace FcTimers.Tests;

public class LootLedgerTests
{
    private static VoyageRecord Voyage(long returnTime, params (uint Id, int Count, long Value)[] items)
    {
        var voyage = new VoyageRecord { SubName = "Navi", ReturnTime = returnTime };
        foreach (var (id, count, value) in items)
            voyage.Items.Add(new LootItem { ItemId = id, Name = $"Item {id}", Count = count, Value = value });
        return voyage;
    }

    [Fact]
    public void A_mesma_viagem_salva_de_novo_nao_conta_duas_vezes()
    {
        var voyages = new Dictionary<string, VoyageRecord>();
        var key = LootLedger.VoyageKey("1:0", 1000);

        Assert.True(LootLedger.Upsert(voyages, key, Voyage(1000, (5, 2, 300))));
        Assert.False(LootLedger.Upsert(voyages, key, Voyage(1000, (5, 2, 300))));
        Assert.Single(voyages);
    }

    [Fact]
    public void Leitura_sem_itens_nunca_apaga_uma_viagem()
    {
        var voyages = new Dictionary<string, VoyageRecord>();
        var key = LootLedger.VoyageKey("1:0", 1000);
        LootLedger.Upsert(voyages, key, Voyage(1000, (5, 2, 300)));

        Assert.False(LootLedger.Upsert(voyages, key, Voyage(1000)));
        Assert.Single(voyages[key].Items);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("3", true)]
    [InlineData("4", false)]
    [InlineData("x", false)]
    public void So_as_posicoes_0_a_3_sao_submarinos(string slot, bool real)
        => Assert.Equal(real, LootLedger.IsRealSlot(slot));

    [Fact]
    public void O_slot_sai_da_chave_da_viagem()
        => Assert.Equal(2, LootLedger.SlotOf(LootLedger.VoyageKey("123456:2", 1000)));

    [Fact]
    public void O_dia_da_viagem_segue_o_fuso_do_painel()
    {
        // 02/11 01:00 UTC ainda é 01/11 em Brasília (UTC-3).
        var returnTime = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var brasilia = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

        Assert.Equal(new DateTime(2026, 11, 1), LootLedger.DayOf(Voyage(returnTime, (1, 1, 1)), brasilia));
    }

    [Fact]
    public void Item_de_market_conta_pelo_maior_valor()
    {
        var npc = new LootItem { Value = 1900, MarketValue = 380000 };
        var cheapMarket = new LootItem { Value = 120000, MarketValue = 5000 };

        Assert.True(npc.IsMarket);
        Assert.Equal(380000, npc.Best);
        Assert.False(cheapMarket.IsMarket);
        Assert.Equal(120000, cheapMarket.Best);
    }

    [Fact]
    public void Chave_de_airship_guarda_o_slot_e_se_separa_dos_subs()
    {
        var key = LootLedger.VoyageKey(LootLedger.AirshipKey("123", 3), 1000);
        Assert.True(LootLedger.IsAirship(key));
        Assert.Equal(3, LootLedger.SlotOf(key));
        Assert.False(LootLedger.IsAirship(LootLedger.VoyageKey("123:3", 1000)));
    }

    [Fact]
    public void LootReturnTimeUsesDepartureWhenKnownReturnIsStale()
    {
        const long day = 86400;
        // Volta conhecida de 3 semanas atrás, saída vista agora, volta daqui a 1 dia e meio: o loot fica com a hora da saída.
        Assert.Equal(30 * day, LootLedger.LootReturnTime(9 * day, 30 * day, 31 * day + (day / 2)));
        // Volta conhecida logo antes da saída: continua valendo.
        Assert.Equal(29 * day, LootLedger.LootReturnTime(29 * day, 30 * day, 31 * day + (day / 2)));
        // Sub ainda no porto (a volta já passou): não muda.
        Assert.Equal(31 * day, LootLedger.LootReturnTime(31 * day, 32 * day, 31 * day));
        Assert.Equal(0, LootLedger.LootReturnTime(0, 30 * day, 31 * day));
    }
}
