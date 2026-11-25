using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FcTimers;

// Toda cor das janelas sai daqui, por nome. Um tema troca só a decoração; pronto, em breve e urgente
// (Good, Soon, Bad) são iguais em todos os temas e nenhum tema usa essas cores como destaque.
// Os outros temas e o seletor chegam na UI-4; por enquanto só existe o Maré.
public sealed record Theme
{
    public required string Name { get; init; }

    // Fundo da janela, dos cartões e dos campos.
    public required Vector4 Panel { get; init; }
    public required Vector4 Card { get; init; }
    public required Vector4 Raised { get; init; }
    public required Vector4 Field { get; init; }
    public required Vector4 Line { get; init; }

    public required Vector4 Ink { get; init; }
    public required Vector4 Muted { get; init; }

    // Cor da marca e a versão escura dela, para botões e abas.
    public required Vector4 Accent { get; init; }
    public required Vector4 AccentDeep { get; init; }
    public required Vector4 AccentHover { get; init; }

    public required Vector4 TitleBar { get; init; }
    public required Vector4 TitleBarActive { get; init; }
    public required Vector4 Button { get; init; }
    public required Vector4 ButtonHover { get; init; }

    // Cores de cada assunto na linha do tempo e nos cartões.
    public required Vector4 Brass { get; init; }
    public required Vector4 Sky { get; init; }
    public required Vector4 Leaf { get; init; }
    public required Vector4 Violet { get; init; }

    public static readonly Vector4 Good = Hex(0x6fd08c);
    public static readonly Vector4 Soon = Hex(0xf0c24b);
    public static readonly Vector4 Bad = Hex(0xff6b57);

    // Maré: azul do mar com detalhe dourado. É o padrão.
    public static readonly Theme Mare = new()
    {
        Name = "Maré",
        Panel = Hex(0x121a25),
        Card = Hex(0x18222f),
        Raised = Hex(0x1f2b3a),
        Field = Hex(0x0d141d),
        Line = Hex(0x2a3747),
        Ink = Hex(0xe4ebf2),
        Muted = Hex(0x8796a8),
        Accent = Hex(0x3cc4c4),
        AccentDeep = Hex(0x12464b),
        AccentHover = Hex(0x175a60),
        TitleBar = Hex(0x152c35),
        TitleBarActive = Hex(0x1c3a45),
        Button = Hex(0x1d3040),
        ButtonHover = Hex(0x24394c),
        Brass = Hex(0xd8b06a),
        Sky = Hex(0x74a9ff),
        Leaf = Hex(0x6fcf8a),
        Violet = Hex(0xa690ff),
    };

    public static Theme Current { get; set; } = Mare;

    public static Vector4 Hex(uint rgb, float alpha = 1f)
        => new(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, alpha);

    public static Vector4 Fade(Vector4 color, float alpha) => color with { W = color.W * alpha };

    // Aplica o tema só na janela que está sendo desenhada. O estilo global do Dalamud não muda.
    public IDisposable Push()
    {
        return ImRaii.PushColor(ImGuiCol.WindowBg, Panel)
            .Push(ImGuiCol.ChildBg, Card)
            .Push(ImGuiCol.PopupBg, Panel)
            .Push(ImGuiCol.Border, Line)
            .Push(ImGuiCol.Text, Ink)
            .Push(ImGuiCol.TextDisabled, Muted)
            .Push(ImGuiCol.TitleBg, TitleBar)
            .Push(ImGuiCol.TitleBgActive, TitleBarActive)
            .Push(ImGuiCol.TitleBgCollapsed, TitleBar)
            .Push(ImGuiCol.FrameBg, Field)
            .Push(ImGuiCol.FrameBgHovered, Raised)
            .Push(ImGuiCol.FrameBgActive, Raised)
            .Push(ImGuiCol.Button, Button)
            .Push(ImGuiCol.ButtonHovered, ButtonHover)
            .Push(ImGuiCol.ButtonActive, AccentHover)
            .Push(ImGuiCol.Header, Raised)
            .Push(ImGuiCol.HeaderHovered, ButtonHover)
            .Push(ImGuiCol.HeaderActive, AccentHover)
            .Push(ImGuiCol.Tab, Panel)
            .Push(ImGuiCol.TabHovered, ButtonHover)
            .Push(ImGuiCol.TabActive, AccentDeep)
            .Push(ImGuiCol.TabUnfocused, Panel)
            .Push(ImGuiCol.TabUnfocusedActive, AccentDeep)
            .Push(ImGuiCol.Separator, Line)
            .Push(ImGuiCol.TableHeaderBg, Raised)
            .Push(ImGuiCol.TableBorderLight, Line)
            .Push(ImGuiCol.TableBorderStrong, Line)
            .Push(ImGuiCol.TableRowBgAlt, Fade(Ink, 0.03f))
            .Push(ImGuiCol.CheckMark, Accent)
            .Push(ImGuiCol.SliderGrab, Accent)
            .Push(ImGuiCol.SliderGrabActive, Accent)
            .Push(ImGuiCol.ScrollbarGrab, Raised)
            .Push(ImGuiCol.PlotHistogram, Accent);
    }
}

// Janela do FC Companion: as cores do tema valem do título ao fundo, e só nela.
public abstract class ThemedWindow : Window
{
    private IDisposable? colors;

    protected ThemedWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None) : base(name, flags)
    {
    }

    public override void PreDraw() => colors = Theme.Current.Push();

    public override void PostDraw()
    {
        colors?.Dispose();
        colors = null;
    }
}
