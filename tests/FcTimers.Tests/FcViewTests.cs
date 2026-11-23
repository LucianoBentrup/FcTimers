using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FcTimers.Tests;

public class FcViewTests
{
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static FcView Build(Configuration config, RemoteState? remote = null, LocalReadings? local = null)
        => FcView.Build(config, local ?? new LocalReadings(), remote, 0);

    [Fact]
    public void Vale_a_leitura_mais_recente_do_sub_entre_este_PC_e_o_servico()
    {
        var config = new Configuration();
        config.Subs["1:0"] = new SubRecord { Name = "Navi", ReturnTime = Now + 100 };
        var local = new LocalReadings();
        local.Subs[0] = Now - 60;
        var remote = new RemoteState { Subs = { ["0"] = new RemoteSub { Name = "Navi", ReturnTime = Now + 900, ObservedAt = Now - 10 } } };

        Assert.Equal(Now + 900, Build(config, remote, local).Subs.Single().ReturnTime);

        local.Subs[0] = Now;
        Assert.Equal(Now + 100, Build(config, remote, local).Subs.Single().ReturnTime);
    }

    [Fact]
    public void A_mesma_viagem_dos_dois_PCs_conta_uma_vez_preferindo_a_que_tem_preco_de_market()
    {
        var config = new Configuration();
        config.Voyages["1:0:500"] = new VoyageRecord
        {
            SubName = "Navi",
            ReturnTime = 500,
            Items = [new LootItem { ItemId = 1, Count = 19, Value = 1900 }],
        };
        var remote = new RemoteState
        {
            Voyages =
            [
                new RemoteVoyage { Slot = 0, SubName = "Navi", ReturnTime = 500, Items = [new LootItem { ItemId = 1, Count = 19, Value = 1900, MarketValue = 380000 }] },
            ],
        };

        var voyage = Assert.Single(Build(config, remote).Voyages);
        Assert.Equal(380000, voyage.Voyage.Items.Single().Best);
    }

    [Fact]
    public void Rega_mais_nova_de_outro_PC_vale_e_a_antiga_nao()
    {
        var config = new Configuration();
        config.Plots["k"] = new PlotRecord { Name = "Canteiro 1", WateredAt = Now - 3600 };
        var newer = new RemoteState { Plots = { ["k"] = new RemotePlot { Name = "Canteiro 1", NextWaterAt = Now + 86000, ObservedAt = Now - 60 } } };
        var older = new RemoteState { Plots = { ["k"] = new RemotePlot { Name = "Canteiro 1", NextWaterAt = Now + 1000, ObservedAt = Now - 7200 } } };

        Assert.Equal(Now + 86000, Build(config, newer).Plots.Single().NextWaterAt);
        Assert.Equal(config.Plots["k"].NextWaterAt, Build(config, older).Plots.Single().NextWaterAt);
    }

    [Fact]
    public void Plot_geral_some_quando_um_canteiro_e_regado_depois()
    {
        var config = new Configuration();
        config.Plots[ConfigMigration.GeneralPlot] = new PlotRecord { Name = "Plantação", WateredAt = Now - 7200 };
        config.Plots["k"] = new PlotRecord { Name = "Canteiro 1", WateredAt = Now - 60 };

        Assert.Equal(["k"], Build(config).Plots.Select(p => p.Key));
    }

    [Fact]
    public void Campo_cadastrado_sem_rega_aparece_por_ultimo_e_sem_horario()
    {
        var config = new Configuration();
        config.Plots["novo"] = new PlotRecord { Name = "Cebolas" };
        config.Plots["k"] = new PlotRecord { Name = "Canteiro 1", WateredAt = Now - 60 };

        var plots = Build(config).Plots;
        Assert.Equal(["k", "novo"], plots.Select(p => p.Key));
        Assert.Equal(0, plots[1].NextWaterAt);
    }

    [Fact]
    public void Canteiro_morto_ha_mais_de_2_dias_some()
    {
        var config = new Configuration();
        config.Plots["k"] = new PlotRecord { Name = "Canteiro 1", WateredAt = Now - (5 * 86400) };

        Assert.Empty(Build(config).Plots);
    }

    [Fact]
    public void Estoque_mais_recente_vale_entre_este_PC_e_o_servico()
    {
        var config = new Configuration();
        config.Supplies.ChestPages[5] = new SupplyPage { Tanks = 579, Kits = 65 };
        config.Supplies.ChangedAt = Now - 3600;
        var remote = new RemoteState { Supplies = new RemoteSupplies { Tanks = 495, Kits = 65, ObservedAt = Now - 60 } };

        Assert.Equal(495, Build(config, remote).Supplies!.Value.Tanks);
        Assert.Equal(579, Build(config).Supplies!.Value.Tanks);
    }
    [Fact]
    public void Airship_e_sub_na_mesma_posicao_e_horario_sao_viagens_diferentes()
    {
        var config = new Configuration();
        config.Voyages[LootLedger.VoyageKey("1:0", 500)] = new VoyageRecord { SubName = "Navi", ReturnTime = 500, Items = [new LootItem { ItemId = 1, Count = 1, Value = 10 }] };
        config.Voyages[LootLedger.VoyageKey(LootLedger.AirshipKey("1", 0), 500)] = new VoyageRecord { SubName = "Nuvem", ReturnTime = 500, Items = [new LootItem { ItemId = 2, Count = 1, Value = 5 }] };
        var remote = new RemoteState { Voyages = [new RemoteVoyage { Slot = 0, Airship = true, SubName = "Nuvem", ReturnTime = 500, Items = [new LootItem { ItemId = 2, Count = 1, Value = 5 }] }] };

        var voyages = Build(config, remote).Voyages;
        Assert.Equal(2, voyages.Count);
        Assert.Equal("Nuvem", voyages.Single(v => v.Airship).Voyage.SubName);
    }

    [Fact]
    public void Airship_mais_recente_vale_entre_este_PC_e_o_servico()
    {
        var config = new Configuration();
        config.Airships[LootLedger.AirshipKey("1", 2)] = new SubRecord { Name = "Nuvem", ReturnTime = Now + 100 };
        var remote = new RemoteState { Airships = { ["2"] = new RemoteAirship { Name = "Nuvem", ReturnTime = Now + 900, ObservedAt = Now - 10, Rank = 12 } } };

        var ship = Assert.Single(Build(config, remote).Airships);
        Assert.Equal(2, ship.Slot);
        Assert.Equal(Now + 900, ship.ReturnTime);
        Assert.Equal(12, ship.Details!.Rank);
    }

    [Fact]
    public void Visita_mais_recente_na_casa_vale_entre_este_PC_e_o_servico()
    {
        var config = new Configuration { HouseVisitAt = Now - 3600 };
        Assert.Null(Build(new Configuration()).House);
        Assert.Equal(Now - 3600, Build(config).House!.Value.VisitedAt);

        var remote = new RemoteState { House = new RemoteHouse { VisitedAt = Now - 60, By = "Amigo" } };
        Assert.Equal("Amigo", Build(config, remote).House!.Value.By);
    }
}
