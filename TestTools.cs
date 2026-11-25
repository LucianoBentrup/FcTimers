using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace FcTimers;

// TESTE: aba de teste para a fase de testes. Some com Enabled = false; depois, apagar o arquivo,
// a aba em MainWindow, Plugin.TestOtherFc e FcLink.TestRequest.
public static class TestTools
{
    public const bool Enabled = true;

    public static void Draw(Plugin plugin)
    {
        UiKit.Heading(FontAwesomeIcon.Flask, "Teste");

        var otherFc = plugin.TestOtherFc;
        if (ImGui.Checkbox("Simular alt de outra FC", ref otherFc))
            plugin.TestOtherFc = otherFc;

        if (ImGui.Button("Notificação de teste"))
            plugin.Notify("Notificação de teste do FC Companion.");
        ImGui.SameLine();
        if (ImGui.Button("Aviso de sub pronto"))
            plugin.Notify(L.SubReturned("Teste"));
        ImGui.SameLine();
        if (ImGui.Button("Aviso da casa (41 dias)"))
            plugin.Notify(L.HouseWarning(41));

        if (ImGui.Button("Pedido de entrada de teste"))
            plugin.Link.TestRequest();
        ImGui.SameLine();
        if (ImGui.Button("Aviso de pedido aceito"))
            plugin.Notify(L.LinkAccepted);
        ImGui.SameLine();
        if (ImGui.Button("Aviso de saída da FC"))
            plugin.Notify(L.LinkRemoved);

        if (ImGui.Button("Sincronizar agora"))
        {
            plugin.Sync.FetchSoon();
            plugin.Link.RefreshNow();
        }

        ImGui.SameLine();
        if (ImGui.Button("Enviar log agora"))
            plugin.SendLog("botão de teste");
        ImGui.SameLine();
        if (ImGui.Button("Novidades como atualização"))
            plugin.OpenChangelog("0.9.0");

        if (plugin.Link.Status.Length > 0)
            ImGui.TextColored(UiKit.Muted, plugin.Link.Status);
    }
}
