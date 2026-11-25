using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace FcTimers;

// Um cartão por sub: nome e rank, a volta com a barra da viagem, as 4 peças com o ícone do jogo e o desgaste,
// e a rota embaixo. Sub pronto ganha a borda verde.
public static partial class SubCards
{
    private const float MinCardWidth = 210;

    public static void Draw(IReadOnlyList<SubView> subs, long now, double repairShow, double repairLow)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var available = ImGui.GetContentRegionAvail().X;
        var columns = Math.Clamp((int)((available + spacing) / ((MinCardWidth * scale) + spacing)), 1, subs.Count);

        using var grid = ImRaii.Table("##subcards", columns, ImGuiTableFlags.SizingStretchSame);
        if (!grid)
            return;

        foreach (var sub in subs.OrderBy(s => s.Slot))
        {
            ImGui.TableNextColumn();
            Card(sub, now, repairShow, repairLow, scale);
        }
    }

    private static void Card(SubView sub, long now, double repairShow, double repairLow, float scale)
    {
        var theme = Theme.Current;
        var style = ImGui.GetStyle();
        var width = ImGui.GetContentRegionAvail().X;
        var inner = width - (style.WindowPadding.X * 2);
        var gap = 5 * scale;
        var partSize = Math.Min(40 * scale, (inner - (3 * gap)) / 4);
        var line = ImGui.GetTextLineHeightWithSpacing();
        var height = (style.WindowPadding.Y * 2) + (4 * line) + partSize + (8 * scale) + style.ItemSpacing.Y;

        var ready = sub.ReturnTime > 0 && sub.ReturnTime <= now;
        using var border = ImRaii.PushColor(ImGuiCol.Border, Theme.Good, ready);
        using var child = ImRaii.Child($"##sub{sub.Slot}", new Vector2(-1, height), true, ImGuiWindowFlags.NoScrollbar);
        if (!child)
            return;

        var details = sub.Details;

        // Nome e rank.
        ImGui.TextUnformatted(sub.Name);
        if (details is { Rank: > 0 })
        {
            var rank = $"R{details.Rank}";
            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(rank).X);
            ImGui.TextColored(theme.Brass, rank);
            UiKit.Tooltip($"XP {details.ExpPercent}%");
        }

        // Volta: o tempo na cor do estado e o horário à direita.
        var state = UiKit.ReturnState(sub.ReturnTime, now);
        UiKit.Text(state, sub.ReturnTime == 0 ? theme.Muted : UiKit.TimeColor(sub.ReturnTime, now));
        if (sub.ReturnTime > 0)
        {
            var at = L.AtTime(UiKit.LocalTime(sub.ReturnTime, "HH:mm"));
            ImGui.SameLine(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(at).X);
            ImGui.TextColored(theme.Muted, at);
            UiKit.TimeTooltip(sub.ReturnTime);
        }

        // Barra da viagem, quando este PC viu a saída. Sem ela, a barra do XP do rank.
        if (sub.DepartedAt > 0 && sub.ReturnTime > sub.DepartedAt)
        {
            var done = (now - sub.DepartedAt) / (float)(sub.ReturnTime - sub.DepartedAt);
            UiKit.Bar(done, string.Empty, ready ? Theme.Good : theme.Accent);
        }
        else if (details != null)
        {
            UiKit.Bar(details.ExpPercent / 100f, string.Empty, Theme.Fade(theme.Brass, 0.7f));
            UiKit.Tooltip($"R{details.Rank} · XP {details.ExpPercent}%");
        }
        else
        {
            ImGui.Dummy(new Vector2(1, ImGui.GetTextLineHeight() + 2));
        }

        Parts(sub, details, partSize, gap, repairShow, repairLow, scale);

        // Rota e tanques, cortados para caber; a dica mostra tudo.
        if (details is { Route.Length: > 0 })
        {
            var route = Fit(details.Route, inner);
            ImGui.TextColored(theme.Muted, route);
            UiKit.Tooltip(L.SetupLine(details.Build, details.Route, details.TanksPerVoyage));
        }
    }

    // As 4 peças em quadrados iguais. Com o ícone lido neste PC, a letra da classe fica no canto
    // e o desgaste numa barrinha embaixo. Sem a leitura, só a letra da classe.
    private static void Parts(SubView sub, SubDetails? details, float size, float gap, double repairShow, double repairLow, float scale)
    {
        var theme = Theme.Current;
        var letters = details == null ? [] : BuildLetters(details.Build);
        var parts = sub.Parts is { Count: 4 } read ? read : null;
        var names = new[] { L.Hull, L.Stern, L.Bow, L.Bridge };
        var draw = ImGui.GetWindowDrawList();

        for (var i = 0; i < 4; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, gap);

            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(size);
            draw.AddRectFilled(min, max, ImGui.GetColorU32(theme.Raised), 4 * scale);
            draw.AddRect(min, max, ImGui.GetColorU32(theme.Line), 4 * scale);

            if (parts != null)
            {
                var pad = 3 * scale;
                ImGui.SetCursorScreenPos(min + new Vector2(pad));
                GameIcons.Item(parts[i].ItemId, size - (pad * 2));
                ImGui.SetCursorScreenPos(min);
            }

            ImGui.InvisibleButton($"##part{sub.Slot}-{i}", new Vector2(size));
            var hovered = ImGui.IsItemHovered();

            var letter = i < letters.Count ? letters[i] : string.Empty;
            if (letter.Length > 0)
            {
                var textSize = ImGui.CalcTextSize(letter);
                var position = parts != null ? max - textSize - new Vector2(2 * scale, 4 * scale) : min + ((new Vector2(size) - textSize) / 2);
                draw.AddText(position + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.9f)), letter);
                draw.AddText(position, ImGui.GetColorU32(theme.Brass), letter);
            }

            double? condition = parts?[i].Condition;
            if (condition is { } wear)
            {
                var color = wear < repairLow ? Theme.Bad : wear < repairShow ? Theme.Soon : Theme.Good;
                var barMin = new Vector2(min.X + (3 * scale), max.Y - (4 * scale));
                var barMax = new Vector2(max.X - (3 * scale), max.Y - (2 * scale));
                draw.AddRectFilled(barMin, barMax, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));
                draw.AddRectFilled(barMin, barMin with { X = barMin.X + ((barMax.X - barMin.X) * (float)Math.Clamp(wear / 100, 0, 1)), Y = barMax.Y }, ImGui.GetColorU32(color));
            }

            if (hovered)
            {
                using var tooltip = ImRaii.Tooltip();
                var name = parts != null ? GameIcons.ItemName(parts[i].ItemId) : string.Empty;
                ImGui.TextUnformatted(name.Length > 0 ? $"{names[i]}: {name}" : names[i]);
                if (condition is { } shown)
                    ImGui.TextColored(shown < repairLow ? Theme.Bad : shown < repairShow ? Theme.Soon : theme.Muted, $"{Math.Floor(shown)}%");
                else
                    ImGui.TextColored(theme.Muted, L.PartsNotRead);
            }
        }
    }

    // "S+S+U+C+" vira ["S+", "S+", "U+", "C+"].
    public static List<string> BuildLetters(string build) => PartLetter().Matches(build).Select(m => m.Value).ToList();

    private static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;

        var cut = text;
        while (cut.Length > 1 && ImGui.CalcTextSize(cut + "…").X > width)
            cut = cut[..^1];
        return cut + "…";
    }

    [GeneratedRegex(@"[A-Z?]\+?")]
    private static partial Regex PartLetter();
}
