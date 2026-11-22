using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace FcTimers;

// Peças de interface usadas pelas janelas: cores, ícones, cartões, barras e tabelas.
public static class UiKit
{
    // Verde: pronto. Amarelo: falta menos de 1 hora. Vermelho: precisa de atenção.
    public static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    public static readonly Vector4 Soon = new(0.95f, 0.80f, 0.30f, 1f);
    public static readonly Vector4 Bad = new(1f, 0.45f, 0.35f, 1f);
    public static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1f);

    private const ImGuiTableFlags TableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;

    public static Vector4? TimeColor(long until, long now) => until <= now ? Good : until - now < 3600 ? Soon : null;

    // Ícone do Font Awesome que vem com o Dalamud, na mesma linha do que vier depois.
    public static void Icon(FontAwesomeIcon icon, Vector4? color = null)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (color is { } tint)
                ImGui.TextColored(tint, icon.ToIconString());
            else
                ImGui.TextUnformatted(icon.ToIconString());
        }

        ImGui.SameLine();
    }

    public static void Heading(FontAwesomeIcon icon, string text)
    {
        Icon(icon, Muted);
        ImGui.TextUnformatted(text);
        ImGui.Separator();
    }

    // Texto cinza com ícone, para quando ainda não há dados: diz o que fazer.
    public static void Empty(FontAwesomeIcon icon, string hint)
    {
        Icon(icon, Muted);
        ImGui.TextColored(Muted, hint);
    }

    public static void Text(string text, Vector4? color)
    {
        if (color is { } tint)
            ImGui.TextColored(tint, text);
        else
            ImGui.TextUnformatted(text);
    }

    // Barra de progresso com texto por cima. A cor segue o valor quando não vem uma cor pronta.
    public static void Bar(float fraction, string overlay, Vector4? color = null, float width = -1)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, color ?? Good, color != null || fraction >= 1f))
            ImGui.ProgressBar(fraction, new Vector2(width, ImGui.GetTextLineHeight() + 2), overlay);
    }

    // Horário absoluto ao passar o mouse sobre um tempo relativo ("em 2h10" mostra "às 15:47 · 03/11").
    public static void TimeTooltip(long unix)
    {
        if (unix > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip(L.AtTime(DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("HH:mm · dd/MM")));
    }

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    // Cartão com borda e título, como os da Visão geral.
    public static void Card(string id, FontAwesomeIcon icon, string title, float height, Action content)
    {
        using var child = ImRaii.Child(id, new Vector2(-1, height), true);
        if (!child)
            return;

        Icon(icon, Muted);
        ImGui.TextColored(Muted, title);
        ImGui.Spacing();
        content();
    }

    // Abre uma tabela com cabeçalho; o using fecha. Devolve null se a tabela não aparece neste frame.
    public static IDisposable? Table(string id, params string[] columns)
    {
        if (!ImGui.BeginTable(id, columns.Length, TableFlags))
            return null;

        foreach (var column in columns)
            ImGui.TableSetupColumn(column);
        ImGui.TableHeadersRow();
        return EndTable;
    }

    public static void Cell(string text, Vector4? color = null)
    {
        ImGui.TableNextColumn();
        Text(text, color);
    }

    private static readonly TableEnd EndTable = new();

    private sealed class TableEnd : IDisposable
    {
        public void Dispose() => ImGui.EndTable();
    }
}
