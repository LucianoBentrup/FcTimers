using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace FcTimers;

// Linha do tempo da Visão geral: as próximas 24 horas numa barra só, uma faixa por assunto.
// Marco que já passou da hora fica no "agora" com um anel verde. O que passa das 24 horas aparece como texto na ponta da faixa.
public static class TimelineView
{
    private const float HitRadius = 8f;
    private const float LaneHeight = 24f;

    // Cabeçalho, o rótulo do "agora", uma faixa por assunto e a linha das horas.
    public static float Height(List<TimelineMarker> markers)
    {
        var lanes = Timeline.Lanes(markers).Count;
        var style = ImGui.GetStyle();
        var line = ImGui.GetTextLineHeight();
        var body = lanes == 0 ? line : line + (lanes * LaneHeight * ImGuiHelpers.GlobalScale) + line + 4;
        return (style.WindowPadding.Y * 2) + line + style.ItemSpacing.Y + body + 4;
    }

    public static void Draw(List<TimelineMarker> markers, long now)
    {
        var lanes = Timeline.Lanes(markers);
        var theme = Theme.Current;

        ImGui.TextUnformatted(L.Next24h);
        var ready = markers.Count(m => m.Ready);
        if (ready > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Good, L.DtrReady(ready));
        }

        if (Timeline.Next(markers.Where(m => m.Time <= now + Timeline.WindowSeconds), now) is { } next)
        {
            ImGui.SameLine();
            ImGui.TextColored(theme.Muted, $"· {L.NextUp($"{Label(next)} {L.InTime(Plugin.FormatRemaining(next.Time - now))}")}");
        }

        if (lanes.Count == 0)
        {
            ImGui.TextColored(theme.Muted, L.NothingNext);
            return;
        }

        var scale = ImGuiHelpers.GlobalScale;
        var laneHeight = LaneHeight * scale;
        var axisHeight = ImGui.GetTextLineHeight() + (4 * scale);
        var nowLabel = ImGui.GetTextLineHeight();
        var labelWidth = lanes.Max(l => ImGui.CalcTextSize(LaneName(l)).X) + (12 * scale);
        var edgeWidth = ImGui.CalcTextSize(L.InDays(44)).X + (8 * scale);

        var start = ImGui.GetCursorScreenPos();
        var width = Math.Max(ImGui.GetContentRegionAvail().X, 200 * scale);
        var height = nowLabel + (lanes.Count * laneHeight) + axisHeight;
        ImGui.InvisibleButton("##timeline", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var top = start.Y + nowLabel;
        var bottom = top + (lanes.Count * laneHeight);
        var left = start.X + labelWidth;
        var right = start.X + width - edgeWidth;
        float X(long time) => left + ((right - left) * Math.Clamp((time - now) / (float)Timeline.WindowSeconds, 0f, 1f));

        draw.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom), ImGui.GetColorU32(theme.Field), 3 * scale);

        // Marcas a cada 3 horas do relógio do PC, com a hora embaixo.
        var local = DateTimeOffset.FromUnixTimeSeconds(now).ToLocalTime();
        var hour = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).AddHours(1);
        while (hour.Hour % 3 != 0)
            hour = hour.AddHours(1);
        for (; hour.ToUnixTimeSeconds() < now + Timeline.WindowSeconds; hour = hour.AddHours(3))
        {
            var x = X(hour.ToUnixTimeSeconds());
            draw.AddLine(new Vector2(x, top), new Vector2(x, bottom), ImGui.GetColorU32(Theme.Fade(theme.Line, 0.8f)));
            var text = hour.ToString("HH:mm");
            var size = ImGui.CalcTextSize(text);
            draw.AddText(new Vector2(x - (size.X / 2), bottom + (2 * scale)), ImGui.GetColorU32(theme.Muted), text);
        }

        var hits = new List<(Vector2 Position, TimelineMarker Marker)>();
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var y = top + (i * laneHeight) + (laneHeight / 2);
            var name = LaneName(lane);
            var nameSize = ImGui.CalcTextSize(name);
            draw.AddText(new Vector2(start.X, y - (nameSize.Y / 2)), ImGui.GetColorU32(theme.Muted), name);
            if (i > 0)
                draw.AddLine(new Vector2(left, y - (laneHeight / 2)), new Vector2(right, y - (laneHeight / 2)), ImGui.GetColorU32(Theme.Fade(theme.Line, 0.5f)));

            var inLane = markers.Where(m => m.Lane == lane).ToList();
            foreach (var marker in inLane.Where(m => m.Time <= now + Timeline.WindowSeconds))
            {
                var position = new Vector2(X(Math.Max(marker.Time, now)), y);
                DrawMarker(draw, position, MarkerColor(marker, now), marker.Ready, scale);
                hits.Add((position, marker));
            }

            // Depois das 24 horas: só o próximo, como texto na ponta.
            var later = inLane.FirstOrDefault(m => m.Time > now + Timeline.WindowSeconds);
            if (later.Time > 0)
            {
                var text = Later(later.Time - now);
                var size = ImGui.CalcTextSize(text);
                var position = new Vector2(right + (6 * scale), y - (size.Y / 2));
                draw.AddText(position, ImGui.GetColorU32(later.Kind == TimelineKind.HouseDemolish ? MarkerColor(later, now) : theme.Muted), text);
                hits.Add((position + (size / 2), later));
            }
        }

        // "Agora" na ponta esquerda.
        draw.AddLine(new Vector2(left, top - (2 * scale)), new Vector2(left, bottom), ImGui.GetColorU32(theme.Accent), 2 * scale);
        draw.AddText(new Vector2(left - (2 * scale), start.Y), ImGui.GetColorU32(theme.Accent), L.Now);

        if (!hovered)
            return;

        // Passando o mouse: todos os marcos debaixo do cursor, com o horário.
        var mouse = ImGui.GetMousePos();
        var under = hits.Where(h => Vector2.Distance(h.Position, mouse) <= HitRadius * scale * 1.5f).Select(h => h.Marker).ToList();
        if (under.Count == 0)
            return;

        using var tooltip = Dalamud.Interface.Utility.Raii.ImRaii.Tooltip();
        foreach (var marker in under)
        {
            ImGui.TextColored(MarkerColor(marker, now), Label(marker));
            var when = DateTimeOffset.FromUnixTimeSeconds(marker.Time).ToLocalTime().ToString("HH:mm · dd/MM");
            ImGui.TextColored(theme.Muted, marker.Ready ? L.AtTime(when) : $"{L.InTime(Plugin.FormatRemaining(marker.Time - now))} · {L.AtTime(when)}");
        }
    }

    // Losango com borda da cor do fundo, para dois marcos juntos não virarem uma mancha.
    private static void DrawMarker(ImDrawListPtr draw, Vector2 center, Vector4 color, bool ready, float scale)
    {
        var r = 6 * scale;
        var top = center with { Y = center.Y - r };
        var rightPoint = center with { X = center.X + r };
        var bottomPoint = center with { Y = center.Y + r };
        var leftPoint = center with { X = center.X - r };

        if (ready)
            draw.AddCircle(center, r + (3 * scale), ImGui.GetColorU32(Theme.Good), 16, 2 * scale);

        draw.AddQuadFilled(top, rightPoint, bottomPoint, leftPoint, ImGui.GetColorU32(color));
        draw.AddQuad(top, rightPoint, bottomPoint, leftPoint, ImGui.GetColorU32(Theme.Current.Panel), 1.5f * scale);
    }

    private static Vector4 MarkerColor(TimelineMarker marker, long now)
    {
        var theme = Theme.Current;
        return marker.Kind switch
        {
            TimelineKind.PlotDies => Theme.Bad,
            TimelineKind.HouseDemolish => marker.Time - now < 5 * 86400 ? Theme.Bad : marker.Time - now < 15 * 86400 ? Theme.Soon : theme.Muted,
            _ => marker.Lane switch
            {
                TimelineLane.Subs => theme.Accent,
                TimelineLane.Airships => theme.Sky,
                TimelineLane.Garden => theme.Leaf,
                TimelineLane.Buffs => theme.Brass,
                _ => theme.Muted,
            },
        };
    }

    private static string LaneName(TimelineLane lane) => lane switch
    {
        TimelineLane.Subs => L.TabSubs,
        TimelineLane.Airships => L.Airships,
        TimelineLane.Garden => L.LaneGarden,
        TimelineLane.Buffs => L.TabBuffs,
        _ => L.LaneHouse,
    };

    public static string Label(TimelineMarker marker) => marker.Kind switch
    {
        TimelineKind.Returns => marker.Ready ? L.MarkBack(marker.Name) : L.MarkReturns(marker.Name),
        TimelineKind.WaterFrom => marker.Ready ? L.MarkWaterNow(marker.Name) : L.MarkWater(marker.Name),
        TimelineKind.PlotDies => L.MarkDies(marker.Name),
        TimelineKind.WheelReady => L.MarkWheel(marker.Name),
        TimelineKind.BuffEnds => L.MarkBuffEnds(marker.Name),
        _ => L.MarkHouse,
    };

    private static string Later(long seconds)
        => seconds >= 2 * 86400 ? L.InDays((int)(seconds / 86400)) : L.InTime(Plugin.FormatRemaining(seconds));
}
