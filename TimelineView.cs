using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace FcTimers;

// Linha do tempo da Visão geral: as próximas 24 horas, 3 ou 7 dias numa barra só, uma faixa por assunto.
// Marco que já passou da hora fica no "agora" com um anel verde. O que passa do período aparece como texto na ponta da faixa.
public static class TimelineView
{
    private const float HitRadius = 8f;
    private const float LaneHeight = 24f;
    private const float ClusterGap = 12f;
    private const int MaxTooltip = 8;

    // Cabeçalho, o rótulo do "agora", uma faixa por assunto e a linha das horas.
    public static float Height(List<TimelineMarker> markers)
    {
        var lanes = Timeline.Lanes(markers).Count;
        var style = ImGui.GetStyle();
        var line = ImGui.GetTextLineHeight();
        var body = lanes == 0 ? line : line + (lanes * LaneHeight * ImGuiHelpers.GlobalScale) + line + 4;
        return (style.WindowPadding.Y * 2) + line + style.ItemSpacing.Y + body + 12;
    }

    // days: 1, 3 ou 7. setDays troca o período e grava na config.
    public static void Draw(List<TimelineMarker> markers, long now, int days, Action<int> setDays)
    {
        var lanes = Timeline.Lanes(markers);
        var theme = Theme.Current;
        var window = days * Timeline.DaySeconds;
        var end = now + window;

        using (Fonts.Heading())
            ImGui.TextUnformatted(L.NextPeriod(days));
        var ready = markers.Count(m => m.Ready);
        if (ready > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.Good, L.DtrReady(ready));
        }

        if (Timeline.Next(markers.Where(m => m.Time <= end), now) is { } next)
        {
            ImGui.SameLine();
            ImGui.TextColored(theme.Muted, $"· {L.NextUp($"{Label(next)} {L.InTime(Plugin.FormatRemaining(next.Time - now))}")}");
        }

        PeriodPicker(days, setDays);

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
        float X(long time) => left + ((right - left) * Math.Clamp((time - now) / (float)window, 0f, 1f));

        draw.AddRectFilled(new Vector2(left, top), new Vector2(right, bottom), ImGui.GetColorU32(theme.Field), 3 * scale);
        DrawAxis(draw, now, days, X, top, bottom, scale);

        var hits = new List<(Vector2 Position, List<TimelineMarker> Markers)>();
        for (var i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            var y = top + (i * laneHeight) + (laneHeight / 2);
            var name = LaneName(lane);
            var nameSize = ImGui.CalcTextSize(name);
            draw.AddText(new Vector2(start.X, y - (nameSize.Y / 2)), ImGui.GetColorU32(theme.Muted), name);
            if (i > 0)
                draw.AddLine(new Vector2(left, y - (laneHeight / 2)), new Vector2(right, y - (laneHeight / 2)), ImGui.GetColorU32(Theme.Fade(theme.Line, 0.5f)));

            // Marcos muito perto viram um só, com o número de marcos ao lado.
            var inLane = markers.Where(m => m.Lane == lane).ToList();
            foreach (var cluster in Timeline.Clusters(inLane.Where(m => m.Time <= end), X, ClusterGap * scale))
            {
                var first = cluster[0];
                var position = new Vector2(X(first.Time), y);
                DrawMarker(draw, position, MarkerColor(first, now), cluster.Any(m => m.Ready), scale);
                if (cluster.Count > 1)
                    draw.AddText(position + new Vector2(8 * scale, -laneHeight / 2), ImGui.GetColorU32(theme.Ink), $"{cluster.Count}");
                hits.Add((position, cluster));
            }

            // Depois do período: só o próximo, como texto na ponta.
            var later = inLane.FirstOrDefault(m => m.Time > end);
            if (later.Time > 0)
            {
                var text = Later(later.Time - now);
                var size = ImGui.CalcTextSize(text);
                var position = new Vector2(right + (6 * scale), y - (size.Y / 2));
                draw.AddText(position, ImGui.GetColorU32(later.Kind == TimelineKind.HouseDemolish ? MarkerColor(later, now) : theme.Muted), text);
                hits.Add((position + (size / 2), [later]));
            }
        }

        // "Agora" na ponta esquerda.
        draw.AddLine(new Vector2(left, top - (2 * scale)), new Vector2(left, bottom), ImGui.GetColorU32(theme.Accent), 2 * scale);
        draw.AddText(new Vector2(left - (2 * scale), start.Y), ImGui.GetColorU32(theme.Accent), L.Now);

        if (!hovered)
            return;

        // Passando o mouse: todos os marcos debaixo do cursor, com o horário.
        var mouse = ImGui.GetMousePos();
        var under = hits.Where(h => Vector2.Distance(h.Position, mouse) <= HitRadius * scale * 1.5f).SelectMany(h => h.Markers).ToList();
        if (under.Count == 0)
            return;

        using var tooltip = Dalamud.Interface.Utility.Raii.ImRaii.Tooltip();
        foreach (var marker in under.Take(MaxTooltip))
        {
            ImGui.TextColored(MarkerColor(marker, now), Label(marker));
            var when = UiKit.LocalTime(marker.Time, "HH:mm · dd/MM");
            ImGui.TextColored(theme.Muted, marker.Ready ? L.AtTime(when) : $"{L.InTime(Plugin.FormatRemaining(marker.Time - now))} · {L.AtTime(when)}");
        }

        if (under.Count > MaxTooltip)
            ImGui.TextColored(theme.Muted, L.MoreMarkers(under.Count - MaxTooltip));
    }

    // 24h | 3 dias | 7 dias, no canto direito do cabeçalho. O período escolhido fica com a cor da marca.
    private static void PeriodPicker(int days, Action<int> setDays)
    {
        var theme = Theme.Current;
        var style = ImGui.GetStyle();
        var widths = Timeline.Periods.Select(d => ImGui.CalcTextSize(L.PeriodShort(d)).X + (style.FramePadding.X * 2)).ToList();
        var total = widths.Sum() + (style.ItemSpacing.X * (widths.Count - 1));
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetContentRegionMax().X - total));

        for (var i = 0; i < Timeline.Periods.Length; i++)
        {
            var period = Timeline.Periods[i];
            if (i > 0)
                ImGui.SameLine();

            using (Dalamud.Interface.Utility.Raii.ImRaii.PushColor(ImGuiCol.Button, theme.AccentDeep, period == days)
                .Push(ImGuiCol.Text, period == days ? theme.Ink : theme.Muted))
            {
                if (ImGui.SmallButton($"{L.PeriodShort(period)}###period{period}") && period != days)
                    setDays(period);
            }
        }
    }

    // Em 24 horas, marcas a cada 3 horas. Em 3 dias, a cada 6 horas. Em 7 dias, a cada 12 horas, com texto só na data.
    // A meia-noite sempre ganha uma linha mais forte, para separar os dias.
    private static void DrawAxis(ImDrawListPtr draw, long now, int days, Func<long, float> x, float top, float bottom, float scale)
    {
        var theme = Theme.Current;
        var step = days switch { 1 => 3, 3 => 6, _ => 12 };
        var local = DateTimeOffset.FromUnixTimeSeconds(now).ToLocalTime();
        var tick = new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset).AddHours(1);
        while (tick.Hour % step != 0)
            tick = tick.AddHours(1);

        var end = now + (days * Timeline.DaySeconds);
        for (; tick.ToUnixTimeSeconds() < end; tick = tick.AddHours(step))
        {
            var position = x(tick.ToUnixTimeSeconds());
            var midnight = tick.Hour == 0;
            draw.AddLine(new Vector2(position, top), new Vector2(position, bottom),
                ImGui.GetColorU32(midnight ? theme.Line : Theme.Fade(theme.Line, 0.5f)), midnight ? 1.5f * scale : 1f);

            if (days == 7 && !midnight)
                continue;

            var dated = midnight && days > 1;
            var text = dated ? tick.ToString("dd/MM") : tick.ToString("HH:mm");
            var size = ImGui.CalcTextSize(text);
            draw.AddText(new Vector2(position - (size.X / 2), bottom + (2 * scale)), ImGui.GetColorU32(dated ? theme.Ink : theme.Muted), text);
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
