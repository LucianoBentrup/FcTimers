using Xunit;

namespace FcTimers.Tests;

public class ConfigMigrationTests
{
    private const long Now = 1_800_000_000;

    [Fact]
    public void Canteiro_com_virgula_e_com_ponto_vira_um_so_com_a_rega_mais_recente()
    {
        var config = new Configuration { Version = 8 };
        config.Plots["340:25:26:0:-111,1:31,5:-28,1"] = new PlotRecord { Name = "Canteiro 1", WateredAt = 200 };
        config.Plots["340:25:26:0:-111.1:31.5:-28.1"] = new PlotRecord { Name = "Canteiro 1", WateredAt = 100 };

        ConfigMigration.Run(config, Now);

        var plot = Assert.Single(config.Plots);
        Assert.Equal("340:25:26:0:-111.1:31.5:-28.1", plot.Key);
        Assert.Equal(200, plot.Value.WateredAt);
    }

    [Theory]
    [InlineData(" 1", "Canteiro 1")]
    [InlineData("Canteiro  1", "Canteiro 1")]
    [InlineData(" 2", "Canteiro 2")]
    [InlineData("Cebolas", "Cebolas")]
    public void Nome_antigo_perde_o_icone_do_jogo(string old, string expected)
    {
        var config = new Configuration { Version = 10 };
        config.Plots["k"] = new PlotRecord { Name = old };

        ConfigMigration.Run(config, Now);

        Assert.Equal(expected, config.Plots["k"].Name);
    }

    [Fact]
    public void A_quinta_posicao_de_sub_das_versoes_antigas_some()
    {
        var config = new Configuration { Version = 4 };
        config.Subs["1:3"] = new SubRecord { Name = "Tael" };
        config.Subs["1:4"] = new SubRecord { Name = "Tael" };

        ConfigMigration.Run(config, Now);

        Assert.Equal(["1:3"], config.Subs.Keys);
    }

    [Fact]
    public void Quem_ja_usava_o_plugin_continua_em_portugues()
    {
        var old = new Configuration { Version = 7 };
        ConfigMigration.Run(old, Now);
        Assert.Equal("pt", old.Language);

        var fresh = new Configuration();
        ConfigMigration.Run(fresh, Now);
        Assert.Equal(string.Empty, fresh.Language);
    }

    [Fact]
    public void Quem_ja_usava_nao_ve_as_boas_vindas()
    {
        var old = new Configuration { Version = 11 };
        old.Characters[1] = new CharacterRecord { Name = "Faire", Enabled = true };
        ConfigMigration.Run(old, Now);
        Assert.True(old.OnboardingDone);

        var fresh = new Configuration();
        ConfigMigration.Run(fresh, Now);
        Assert.False(fresh.OnboardingDone);
    }

    [Fact]
    public void Viagens_com_mais_de_60_dias_saem_do_config()
    {
        var config = new Configuration();
        config.Voyages["1:0:100"] = new VoyageRecord { ReturnTime = Now - (61 * 86400L) };
        config.Voyages["1:0:200"] = new VoyageRecord { ReturnTime = Now - 86400 };
        config.SyncedVoyages["1:0:100"] = "x";

        ConfigMigration.Run(config, Now);

        Assert.Equal(["1:0:200"], config.Voyages.Keys);
        Assert.Empty(config.SyncedVoyages);
        Assert.Equal(ConfigMigration.CurrentVersion, config.Version);
    }
}
