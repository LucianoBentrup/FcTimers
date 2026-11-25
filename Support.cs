using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;

namespace FcTimers;

// Link de apoio ao projeto. Só ajuda a pagar a hospedagem: nada no plugin depende dele.
public static class Support
{
    // Trocar pelo endereço do Ko-fi quando ele existir. Vazio: o botão não aparece (na versão de dev aparece desligado).
    public const string KofiUrl = "";

    // Botão pequeno e discreto, sem cor de destaque. Abre o navegador só com o clique.
    public static void KofiButton(bool devBuild)
    {
        if (KofiUrl.Length == 0 && !devBuild)
            return;

        using (ImRaii.Disabled(KofiUrl.Length == 0))
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Current.Muted))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(FontAwesomeIcon.Coffee.ToIconString());
            ImGui.SameLine();
            if (ImGui.SmallButton(L.SupportKofi))
                Util.OpenLink(KofiUrl);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(KofiUrl.Length == 0 ? L.SupportNotSet : L.SupportHint);
    }
}
