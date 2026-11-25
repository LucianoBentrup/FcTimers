using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;

namespace FcTimers;

// Peças de interface usadas pelas janelas: cores, ícones, cartões, barras e tabelas.
public static class UiKit
{
    // Verde: pronto. Amarelo: falta menos de 1 hora. Vermelho: precisa de atenção. Iguais em todos os temas.
    public static Vector4 Good => Theme.Good;
    public static Vector4 Soon => Theme.Soon;
    public static Vector4 Bad => Theme.Bad;
    public static Vector4 Muted => Theme.Current.Muted;
    public static Vector4 Accent => Theme.Current.Accent;

    private const ImGuiTableFlags TableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;

    public static Vector4? TimeColor(long until, long now) => until <= now ? Good : until - now < 3600 ? Soon : null;

    // Volta de sub ou airship: "parado" sem viagem, senão o tempo que falta, e a cor do estado.
    public static string ReturnState(long returnTime, long now) => returnTime == 0 ? L.Idle : Plugin.FormatRemaining(returnTime - now);

    public static Vector4? ReturnColor(long returnTime, long now) => returnTime == 0 ? null : TimeColor(returnTime, now);

    // Prazos longos em dias ("44 dias"); no último dia, em horas e minutos.
    public static string DaysOrTime(long seconds) => seconds >= 86400 ? L.Days((int)(seconds / 86400)) : Plugin.FormatRemaining(seconds);

    public static string LocalTime(long unix, string format) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString(format);

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

    // Título de seção: ícone na cor do assunto, texto maior e uma linha fina embaixo.
    public static void Heading(FontAwesomeIcon icon, string text, Vector4? color = null)
    {
        ImGui.Spacing();
        Icon(icon, color ?? Accent);
        using (Fonts.Heading())
            ImGui.TextUnformatted(text);
        var min = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(min, min with { X = min.X + ImGui.GetContentRegionAvail().X }, ImGui.GetColorU32(Theme.Current.Line));
        ImGui.Dummy(new Vector2(1, 3));
    }

    // Linha da cor da marca em cima da aba aberta, como no protótipo.
    public static void ActiveTabLine()
    {
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddRectFilled(min, new Vector2(max.X, min.Y + 2), ImGui.GetColorU32(Accent));
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

    // Barra de progresso com o texto centralizado. O ImGui põe o texto logo depois da parte cheia,
    // então a barra é desenhada sem texto e o texto vai por cima, no meio.
    public static void Bar(float fraction, string overlay, Vector4? color = null, float width = -1)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        using (ImRaii.PushColor(ImGuiCol.PlotHistogram, color ?? Good, color != null || fraction >= 1f))
            ImGui.ProgressBar(fraction, new Vector2(width, ImGui.GetTextLineHeight() + 2), string.Empty);

        if (overlay.Length == 0)
            return;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var size = ImGui.CalcTextSize(overlay);
        var position = new Vector2(min.X + ((max.X - min.X - size.X) / 2), min.Y + ((max.Y - min.Y - size.Y) / 2));
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(position + Vector2.One, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), overlay);
        drawList.AddText(position, ImGui.GetColorU32(ImGuiCol.Text), overlay);
    }

    // Horário absoluto ao passar o mouse sobre um tempo relativo ("em 2h10" mostra "às 15:47 · 03/11").
    public static void TimeTooltip(long unix)
    {
        if (unix > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip(L.AtTime(LocalTime(unix, "HH:mm · dd/MM")));
    }

    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(text);
    }

    // Altura de um cartão para caber o título e tantas linhas de conteúdo, mais uma folga.
    // O título do card usa a fonte maior, então ganha uns pixels a mais que uma linha comum.
    public static float CardHeight(int lines)
        => (ImGui.GetStyle().WindowPadding.Y * 2) + ((lines + 1) * ImGui.GetTextLineHeightWithSpacing()) + ImGui.GetStyle().ItemSpacing.Y + 12;

    // Cartão com borda e título, como os da Visão geral.
    public static void Card(string id, FontAwesomeIcon icon, string title, float height, Action content, Vector4? color = null)
    {
        using var child = ImRaii.Child(id, new Vector2(-1, height), true);
        if (!child)
            return;

        Icon(icon, color ?? Accent);
        using (Fonts.Heading())
            ImGui.TextUnformatted(title);
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
