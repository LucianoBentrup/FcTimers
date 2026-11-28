using System.Linq;
using Xunit;

namespace FcTimers.Tests;

public class BedsTests
{
    private const long Now = 1_800_000_000;
    private const ushort Paprika = 1;
    private const ushort Glazenut = 42;

    private static BedSlot[] Slots(params (ushort Seed, byte Phase)[] beds)
    {
        var slots = new BedSlot[8];
        for (var i = 0; i < beds.Length; i++)
            slots[i] = new BedSlot(beds[i].Seed, beds[i].Phase);
        return slots;
    }

    [Fact]
    public void Primeira_leitura_guarda_as_vagas_sem_inventar_hora_de_plantio()
    {
        var plot = new PlotRecord();

        var events = Beds.Apply(plot, Slots((Paprika, 2), (0, 0), (Glazenut, 4)), Now);

        Assert.Empty(events);
        Assert.Equal(2, plot.Beds.Count);
        Assert.Equal(0, plot.Beds[1].PlantedAt);
        Assert.True(Beds.IsReady(plot.Beds[3]));
    }

    [Fact]
    public void So_as_mudancas_viram_registro()
    {
        var plot = new PlotRecord();
        Beds.Apply(plot, Slots((Paprika, 2), (Glazenut, 4)), Now);

        var events = Beds.Apply(plot, Slots((Paprika, 4), (0, 0), (Paprika, 1)), Now + 60);

        Assert.Equal(new[] { BedEventKind.Ready, BedEventKind.Harvested, BedEventKind.Planted }, events.Select(e => e.Kind));
        Assert.Equal(Now + 60, plot.Beds[3].PlantedAt);
        Assert.False(plot.Beds[3].PlantedApprox);
        Assert.False(plot.Beds.ContainsKey(2));
        Assert.Equal(3, plot.History.Count);
    }

    [Fact]
    public void Colher_so_parte_do_canteiro_mantem_o_resto()
    {
        var plot = new PlotRecord();
        Beds.Apply(plot, Slots((Paprika, 4), (Paprika, 4), (Paprika, 4)), Now);

        Beds.Apply(plot, Slots((Paprika, 4), (0, 0), (Paprika, 4)), Now + 60);

        Assert.Equal(new[] { 1, 3 }, plot.Beds.Keys.Order());
    }

    [Fact]
    public void Plantio_visto_muito_depois_fica_aproximado_e_o_da_mensagem_e_exato()
    {
        var plot = new PlotRecord();
        Beds.Apply(plot, Slots(), Now);

        Beds.Apply(plot, Slots((Paprika, 1), (Paprika, 1)), Now + 7200, bed => bed == 2 ? Now + 30 : null);

        Assert.True(plot.Beds[1].PlantedApprox);
        Assert.Equal(Now + 30, plot.Beds[2].PlantedAt);
        Assert.False(plot.Beds[2].PlantedApprox);
    }

    [Fact]
    public void Adubo_tira_um_por_cento_do_que_falta()
    {
        var bed = new BedRecord { Seed = Paprika, PlantedAt = Now };
        var grow = Beds.GrowSeconds(Paprika);

        bed.Fertilized.Add(Now);

        Assert.Equal(5 * 86400, grow);
        Assert.Equal(Now + (long)(grow * 0.99), Beds.EstimatedReadyAt(bed));
    }

    [Fact]
    public void Rega_e_morte_vem_do_bed_mais_atrasado_e_pronta_nao_conta()
    {
        var plot = new PlotRecord();
        plot.Beds[1] = new BedRecord { Seed = Paprika, Phase = 2, PlantedAt = Now - 3600, TendedAt = Now - 3600 };
        plot.Beds[2] = new BedRecord { Seed = Glazenut, Phase = 2, PlantedAt = Now - 7200, TendedAt = Now - 7200 };
        plot.Beds[3] = new BedRecord { Seed = Paprika, Phase = 4, TendedAt = Now - 900000 };

        var summary = Beds.Summarize(plot, Now);

        Assert.Equal(1, summary.Ready);
        Assert.Equal(2, summary.Growing);
        Assert.Equal(Now - 7200 + PlotRecord.WaterIntervalSeconds, summary.NextWaterAt);
        // Glazenut murcha em 24 horas e morre 24 depois; Paprika em 48 + 24.
        Assert.Equal(Now - 7200 + (48 * 3600), summary.DieAt);
        Assert.Equal(Now - 3600 + (5 * 86400), summary.NextReadyAt);
    }

    [Fact]
    public void Cuidar_sem_numero_do_bed_vale_para_todas_as_vagas()
    {
        var plot = new PlotRecord();
        plot.Beds[1] = new BedRecord { Seed = Paprika };
        plot.Beds[2] = new BedRecord { Seed = Paprika };

        Beds.Tend(plot, 0, Now);
        Beds.Tend(plot, 2, Now + 10);

        Assert.Equal(Now, plot.Beds[1].TendedAt);
        Assert.Equal(Now + 10, plot.Beds[2].TendedAt);
    }

    [Fact]
    public void Aviso_de_colheita_vem_da_previsao_e_nao_repete_o_que_ja_ficou_pronto()
    {
        var plot = new PlotRecord();
        plot.Beds[1] = new BedRecord { Seed = Paprika, Phase = 2, PlantedAt = Now };
        plot.Beds[2] = new BedRecord { Seed = Paprika, Phase = 4, PlantedAt = Now - (6 * 86400), ReadyAt = Now };
        plot.Beds[3] = new BedRecord { Seed = Paprika, Phase = 4, ReadyAt = Now - 60 };

        Assert.Equal(Now + (5 * 86400), Beds.HarvestAlertAt(plot));

        plot.Beds.Remove(1);
        Assert.Equal(Now - 60, Beds.HarvestAlertAt(plot));
    }
}
