using System;
using System.Linq;
using Xunit;

namespace FcTimers.Tests;

public class TimelineTests
{
    private const long Now = 1_800_000_000;
    private const long HouseLimit = 45 * 86400;

    private static FcView Build(Configuration config) => FcView.Build(config, new LocalReadings(), null, 0);

    [Fact]
    public void Sub_que_ja_voltou_fica_pronto_e_o_proximo_e_o_que_ainda_vai_voltar()
    {
        var config = new Configuration();
        config.Subs["1:0"] = new SubRecord { Name = "Navi", ReturnTime = Now - 60 };
        config.Subs["1:1"] = new SubRecord { Name = "Tael", ReturnTime = Now + 3600 };
        config.Subs["1:2"] = new SubRecord { Name = "Parado" };

        var markers = Timeline.Build(Build(config), Now, HouseLimit);

        Assert.Equal(2, markers.Count);
        Assert.True(markers.Single(m => m.Name == "Navi").Ready);
        Assert.Equal("Tael", Timeline.Next(markers, Now)?.Name);
    }

    [Fact]
    public void Planta_que_ja_pode_ser_regada_ganha_tambem_o_prazo_para_nao_morrer()
    {
        var config = new Configuration();
        config.Plots["a"] = new PlotRecord { Name = "Canteiro 1", WateredAt = Now - 90000 };

        var markers = Timeline.Build(Build(config), Now, HouseLimit).Where(m => m.Lane == TimelineLane.Garden).ToList();

        Assert.Contains(markers, m => m.Kind == TimelineKind.WaterFrom && m.Ready);
        Assert.Contains(markers, m => m.Kind == TimelineKind.PlotDies && m.Time == Now + 82800);
    }

    [Fact]
    public void Faixas_aparecem_so_com_marco_e_na_ordem_fixa()
    {
        var config = new Configuration { HouseVisitAt = Now - 86400 };
        config.Airships["1:air:0"] = new SubRecord { Name = "Nuvem", ReturnTime = Now + 100 };
        config.Subs["1:0"] = new SubRecord { Name = "Navi", ReturnTime = Now + 200 };

        var lanes = Timeline.Lanes(Timeline.Build(Build(config), Now, HouseLimit));

        Assert.Equal([TimelineLane.Subs, TimelineLane.Airships, TimelineLane.House], lanes);
    }

    [Fact]
    public void Marcos_perto_demais_viram_um_grupo_que_comeca_no_mais_cedo()
    {
        var lane = new[] { 0L, 5, 30, 100, 104 }
            .Select(t => new TimelineMarker(TimelineLane.Subs, TimelineKind.Returns, Now + t, $"s{t}", false));

        var clusters = Timeline.Clusters(lane, t => t - Now, 12);

        Assert.Equal([2, 1, 2], clusters.Select(c => c.Count));
        Assert.Equal(Now + 100, clusters[2][0].Time);
    }

    [Fact]
    public void Periodo_fora_da_lista_volta_para_24_horas()
    {
        Assert.Equal(7, Timeline.ValidDays(7));
        Assert.Equal(1, Timeline.ValidDays(0));
        Assert.Equal(1, Timeline.ValidDays(5));
    }

    [Fact]
    public void Prazo_da_casa_e_a_ultima_visita_mais_os_45_dias()
    {
        var config = new Configuration { HouseVisitAt = Now - (10 * 86400) };

        var house = Timeline.Build(Build(config), Now, HouseLimit).Single();

        Assert.Equal(TimelineKind.HouseDemolish, house.Kind);
        Assert.Equal(Now + (35 * 86400), house.Time);
    }
}
