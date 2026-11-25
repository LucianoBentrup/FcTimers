using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;

namespace FcTimers;

// Link de apoio ao projeto. Só ajuda a pagar a hospedagem: nada no plugin depende dele.
public static class Support
{
    // Trocar pelo endereço do Ko-fi quando ele existir. Vazio: o botão não aparece (na versão de dev aparece desligado).
    public const string KofiUrl = "";

    public static bool Ready => KofiUrl.Length > 0;

    // Azul do Ko-fi, com texto branco. Não é cor de estado, então não confunde com pronto ou urgente.
    private static readonly Vector4 KofiBlue = Theme.Hex(0x29abe0);
    private static readonly Vector4 KofiHover = Theme.Hex(0x4fbde8);

    public static void Open()
    {
        if (Ready)
            Util.OpenLink(KofiUrl);
    }

    // Botão com a cor do Ko-fi. Sem link, some (na versão de dev aparece desligado, para ver onde fica).
    public static void KofiButton(bool devBuild)
    {
        if (!Ready && !devBuild)
            return;

        using (ImRaii.Disabled(!Ready))
        using (ImRaii.PushColor(ImGuiCol.Button, KofiBlue).Push(ImGuiCol.ButtonHovered, KofiHover).Push(ImGuiCol.ButtonActive, KofiBlue)
            .Push(ImGuiCol.Text, new Vector4(1, 1, 1, 1)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Coffee, L.SupportKofi))
                Open();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(Ready ? L.SupportHint : L.SupportNotSet);
    }

    // Largura do botão, para alinhar à direita.
    public static float KofiButtonWidth()
        => ImGui.CalcTextSize(L.SupportKofi).X + ImGui.GetFrameHeight() + (ImGui.GetStyle().FramePadding.X * 3);

    // Xícara na barra de título da janela, ao lado dos botões do Dalamud.
    public static TitleBarButton TitleButton() => new()
    {
        Icon = FontAwesomeIcon.Coffee,
        IconColor = KofiBlue,
        ShowTooltip = () => ImGui.SetTooltip(L.SupportKofi),
        Click = _ => Open(),
        Priority = 1,
    };
}
