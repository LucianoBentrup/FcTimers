using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FcTimers;

// As novidades vêm do CHANGELOG.md, que vai dentro do plugin. O texto fica em inglês, como no arquivo.
public static class Changelog
{
    private static List<Release>? releases;

    public static List<Release> Releases => releases ??= Load();

    private static List<Release> Load()
    {
        using var stream = typeof(Changelog).Assembly.GetManifestResourceStream("FcTimers.CHANGELOG.md");
        if (stream == null)
            return [];

        using var reader = new StreamReader(stream);
        return ChangelogDoc.Parse(reader.ReadToEnd());
    }
}

// Abre sozinha uma vez depois de cada atualização. Em cima, o que é novo desde a última versão vista:
// os destaques em cards com ícone e o resto em lista curta, por tipo. Embaixo, as versões anteriores fechadas.
public sealed class ChangelogWindow : ThemedWindow
{
    private const float CardMinWidth = 260;
    private static readonly Vector2 CardPadding = new(12, 10);

    private static readonly (string Word, FontAwesomeIcon Icon)[] Icons =
    [
        ("airship", FontAwesomeIcon.Plane),
        ("submarine", FontAwesomeIcon.Ship),
        ("house", FontAwesomeIcon.Home),
        ("suppl", FontAwesomeIcon.GasPump),
        ("profit", FontAwesomeIcon.ChartLine),
        ("loot", FontAwesomeIcon.Coins),
        ("item", FontAwesomeIcon.Gem),
        ("market", FontAwesomeIcon.Coins),
        ("discord", FontAwesomeIcon.Comments),
        ("field", FontAwesomeIcon.Seedling),
        ("water", FontAwesomeIcon.Seedling),
        ("timeline", FontAwesomeIcon.Stream),
        ("overview", FontAwesomeIcon.ThLarge),
        ("look", FontAwesomeIcon.Palette),
        ("sync", FontAwesomeIcon.Sync),
        ("english", FontAwesomeIcon.Language),
    ];

    private readonly bool isDev;
    private List<Release> fresh = [];

    public ChangelogWindow(bool isDev) : base("FC Companion - What's new###FcTimersChangelog")
    {
        this.isDev = isDev;
        Size = new Vector2(640, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 260) };
    }

    // lastSeen: a versão que a pessoa tinha antes. Vazio mostra só a versão mais recente.
    public void Show(string? lastSeen)
    {
        fresh = ChangelogDoc.NewSince(Changelog.Releases, lastSeen);
        IsOpen = true;
    }

    public override void OnOpen()
    {
        if (fresh.Count == 0)
            fresh = ChangelogDoc.NewSince(Changelog.Releases, null);
    }

    public override void Draw()
    {
        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        using (var body = ImRaii.Child("##changes", new Vector2(-1, -footer), false))
        {
            if (body)
                DrawBody();
        }

        ImGui.Separator();
        if (ImGui.Button("Close"))
            IsOpen = false;

        // Apoio no canto direito do rodapé, longe do Close.
        if (Support.Ready || isDev)
        {
            ImGui.SameLine(ImGui.GetContentRegionMax().X - Support.KofiButtonWidth());
            Support.KofiButton(isDev);
        }
    }

    private void DrawBody()
    {
        var theme = Theme.Current;
        if (Changelog.Releases.Count == 0)
        {
            ImGui.TextColored(theme.Muted, "The list of changes could not be read.");
            return;
        }

        foreach (var release in fresh)
        {
            Header(release);
            // Versão comum: lista curta por tipo. Versão grande: os destaques em cards e o resto em lista.
            if (release.Big)
                Highlights(release);
            Lists(release, !release.Big);
            ImGui.Spacing();
            ImGui.Spacing();
        }

        var older = Changelog.Releases.Where(r => !r.IsUnreleased && !fresh.Contains(r)).ToList();
        if (older.Count == 0)
            return;

        ImGui.TextColored(theme.Muted, "Earlier versions");
        foreach (var release in older)
        {
            var date = Date(release.Date);
            if (!ImGui.CollapsingHeader($"{release.Version}{(date.Length > 0 ? $"   {date}" : string.Empty)}###v{release.Version}"))
                continue;

            using (ImRaii.PushIndent(8 * ImGuiHelpers.GlobalScale))
                Lists(release, true);
            ImGui.Spacing();
        }
    }

    private static void Header(Release release)
    {
        var theme = Theme.Current;
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(theme.Accent, FontAwesomeIcon.Star.ToIconString());
        ImGui.SameLine();
        ImGui.TextUnformatted($"What's new in {release.Version}");
        var date = Date(release.Date);
        if (date.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(theme.Muted, date);
        }

        var min = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(min, min with { X = min.X + ImGui.GetContentRegionAvail().X }, ImGui.GetColorU32(theme.Accent), 1.5f * ImGuiHelpers.GlobalScale);
        ImGui.Dummy(new Vector2(1, 4 * ImGuiHelpers.GlobalScale));
    }

    // Um card por destaque, em colunas que cabem na largura. A altura de cada linha acompanha o card mais alto.
    private static void Highlights(Release release)
    {
        var cards = release.Sections.SelectMany(s => s.Entries.Where(e => e.Title.Length > 0).Select(e => (s.Type, Entry: e))).ToList();
        if (cards.Count == 0)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var style = ImGui.GetStyle();
        var available = ImGui.GetContentRegionAvail().X;
        var columns = Math.Clamp((int)((available + style.ItemSpacing.X) / ((CardMinWidth * scale) + style.ItemSpacing.X)), 1, Math.Min(cards.Count, 3));
        var cardWidth = (available - ((columns - 1) * style.ItemSpacing.X)) / columns;
        var padding = CardPadding * scale;
        var textWidth = cardWidth - (padding.X * 2) - BodyIndent();

        using var grid = ImRaii.Table($"##cards{release.Version}", columns, ImGuiTableFlags.SizingStretchSame);
        if (!grid)
            return;

        for (var row = 0; row < cards.Count; row += columns)
        {
            var inRow = cards.Skip(row).Take(columns).ToList();
            var height = inRow.Max(c => ImGui.CalcTextSize(ChangelogDoc.Plain(c.Entry.Text), false, textWidth).Y)
                + ImGui.GetTextLineHeightWithSpacing() + (padding.Y * 2) + (6 * scale);

            ImGui.TableNextRow();
            foreach (var (type, entry) in inRow)
            {
                ImGui.TableNextColumn();
                Card($"##{release.Version}{row}{entry.Title}", type, entry, height);
            }
        }
    }

    // Ícone, título e tipo numa linha; o texto embaixo começa alinhado com o título, não com o ícone.
    private static void Card(string id, ChangeType type, ChangeEntry entry, float height)
    {
        var theme = Theme.Current;
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, CardPadding * ImGuiHelpers.GlobalScale);
        using var child = ImRaii.Child(id, new Vector2(-1, height), true, ImGuiWindowFlags.NoScrollbar);
        if (!child)
            return;

        // Faixa da cor do tipo na borda esquerda.
        var min = ImGui.GetWindowPos();
        ImGui.GetWindowDrawList().AddRectFilled(min, min + new Vector2(3 * ImGuiHelpers.GlobalScale, ImGui.GetWindowHeight()), ImGui.GetColorU32(TypeColor(type)));

        var left = ImGui.GetCursorPosX();
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextColored(TypeColor(type), IconFor(entry.Title).ToIconString());
        ImGui.SameLine(left + BodyIndent());
        ImGui.TextUnformatted(ChangelogDoc.Plain(entry.Title));
        ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(TypeName(type)).X);
        ImGui.TextColored(TypeColor(type), TypeName(type));

        ImGui.Dummy(new Vector2(1, 2 * ImGuiHelpers.GlobalScale));
        ImGui.SetCursorPosX(left + BodyIndent());
        using (ImRaii.PushColor(ImGuiCol.Text, theme.Muted))
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionMax().X);
            ImGui.TextUnformatted(ChangelogDoc.Plain(entry.Text));
            ImGui.PopTextWrapPos();
        }
    }

    // Largura do ícone mais o espaço até o título: é onde o texto do card começa.
    private static float BodyIndent()
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
            return ImGui.CalcTextSize(FontAwesomeIcon.Star.ToIconString()).X + (8 * ImGuiHelpers.GlobalScale);
    }

    // Itens sem destaque, agrupados por tipo. Nas versões antigas, os destaques entram na lista também.
    private static void Lists(Release release, bool withHighlights)
    {
        foreach (var section in release.Sections)
        {
            var entries = section.Entries.Where(e => withHighlights || e.Title.Length == 0).ToList();
            if (entries.Count == 0)
                continue;

            ImGui.Spacing();
            ImGui.TextColored(TypeColor(section.Type), TypeName(section.Type));
            foreach (var entry in entries)
            {
                ImGui.TextColored(TypeColor(section.Type), "·");
                ImGui.SameLine();
                var text = entry.Title.Length > 0 ? $"{entry.Title}: {entry.Text}" : entry.Text;
                ImGui.TextWrapped(ChangelogDoc.Plain(text));
            }
        }
    }

    // Cores da decoração, não as de estado: novo, mudado e corrigido não são "pronto" ou "urgente".
    private static Vector4 TypeColor(ChangeType type) => type switch
    {
        ChangeType.Added => Theme.Current.Accent,
        ChangeType.Changed => Theme.Current.Brass,
        ChangeType.Fixed => Theme.Current.Sky,
        _ => Theme.Current.Muted,
    };

    private static string TypeName(ChangeType type) => type switch
    {
        ChangeType.Added => "New",
        ChangeType.Changed => "Changed",
        ChangeType.Fixed => "Fixed",
        ChangeType.Removed => "Removed",
        _ => "Other",
    };

    private static FontAwesomeIcon IconFor(string title)
        => Icons.FirstOrDefault(i => title.Contains(i.Word, StringComparison.OrdinalIgnoreCase)) is { Word: not null } match ? match.Icon : FontAwesomeIcon.Star;

    // "2026-11-23" vira "23 Nov 2026".
    private static string Date(string iso)
        => DateTime.TryParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day)
            ? day.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
