using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace FcTimers;

// Nomes dos campos da plantação. Fica fora do Plugin para dar para testar sem o jogo.
public static class PlotNames
{
    // Nome automático vindo do menu do jogo ("6th Bed, 2nd Patch" vira "Canteiro 2").
    public static readonly Regex AutoName = new(@"^Canteiro \d+$", RegexOptions.Compiled);
    public static readonly Regex PatchPattern = new(@"(\d+)(?:st|nd|rd|th) Patch", RegexOptions.Compiled);

    // Menu de canteiro ou vaso; só depois dele o log guarda as mensagens do sistema.
    public static readonly Regex GardenMenu = new(@"Crop|Seeds|Patch|Fertiliz", RegexOptions.Compiled);

    // Nome vazio ou só um número, das versões que não sabiam o nome dos canteiros de fora.
    public static bool IsUnnamed(string name) => Clean(name).All(char.IsDigit);

    // O jogo põe ícones (caracteres de uso privado) no nome de alguns objetos, que o Discord mostra como "▯".
    public static string Clean(string name)
        => Regex.Replace(
            new string(name.Where(c => char.GetUnicodeCategory(c) is not (UnicodeCategory.PrivateUse or UnicodeCategory.Control or UnicodeCategory.Format)).ToArray()),
            @"\s+",
            " ").Trim();

    // "Canteiro 2" a partir do texto do menu, ou null se o menu não for de um canteiro de fora.
    public static string? FromMenu(System.Collections.Generic.IEnumerable<string> texts)
    {
        var patch = texts.Select(t => PatchPattern.Match(t)).FirstOrDefault(m => m.Success);
        return patch == null ? null : $"Canteiro {patch.Groups[1].Value}";
    }
}
