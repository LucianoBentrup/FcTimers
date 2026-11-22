using Xunit;

namespace FcTimers.Tests;

public class PlotNamesTests
{
    [Theory]
    [InlineData(" 1", "1")]
    [InlineData("  Oasis   Flowerpot ", "Oasis Flowerpot")]
    [InlineData("Canteiro  2", "Canteiro 2")]
    public void Clean_tira_icones_do_jogo_e_espacos_repetidos(string raw, string expected)
        => Assert.Equal(expected, PlotNames.Clean(raw));

    [Theory]
    [InlineData(" 1", true)]
    [InlineData("", true)]
    [InlineData("Canteiro 1", false)]
    public void Nome_so_com_numero_conta_como_sem_nome(string name, bool unnamed)
        => Assert.Equal(unnamed, PlotNames.IsUnnamed(name));

    [Fact]
    public void O_menu_do_canteiro_de_fora_da_o_numero_do_canteiro()
    {
        Assert.Equal("Canteiro 2", PlotNames.FromMenu(["6th Bed, 2nd Patch", "Tend Crop", "Quit"]));
        Assert.Null(PlotNames.FromMenu(["Plant Seeds", "Quit"]));
    }

    [Theory]
    [InlineData("Tend Crop", true)]
    [InlineData("Plant Seeds", true)]
    [InlineData("Retainer: Lilinyan", false)]
    [InlineData("Submersible Management", false)]
    public void So_menus_de_plantacao_ligam_o_registro_de_mensagens(string text, bool garden)
        => Assert.Equal(garden, PlotNames.GardenMenu.IsMatch(text));
}
