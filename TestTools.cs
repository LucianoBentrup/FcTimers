using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace FcTimers;

// TESTE: aba de teste para a fase de testes. Some com Enabled = false; depois, apagar o arquivo,
// a aba em MainWindow, Plugin.TestOtherFc e FcLink.TestRequest.
public static class TestTools
{
    public const bool Enabled = true;

    private static string T(string pt, string en) => L.English ? en : pt;

    public static void Draw(Plugin plugin)
    {
        UiKit.Heading(FontAwesomeIcon.Flask, T("Teste", "Test"));

        var otherFc = plugin.TestOtherFc;
        if (ImGui.Checkbox(T("Simular alt de outra FC", "Fake alt from another FC"), ref otherFc))
            plugin.TestOtherFc = otherFc;

        if (ImGui.Button(T("Notificação de teste", "Test notification")))
            plugin.Notify(T("Notificação de teste do FC Companion.", "FC Companion test notification."));
        ImGui.SameLine();
        if (ImGui.Button(T("Aviso de sub pronto", "Sub ready alert")))
            plugin.Notify(L.SubReturned("Teste"));
        ImGui.SameLine();
        if (ImGui.Button(T("Aviso da casa (41 dias)", "House alert (41 days)")))
            plugin.Notify(L.HouseWarning(41));

        if (ImGui.Button(T("Pedido de entrada de teste", "Test join request")))
            plugin.Link.TestRequest();
        ImGui.SameLine();
        if (ImGui.Button(T("Aviso de pedido aceito", "Request accepted alert")))
            plugin.Notify(L.LinkAccepted);
        ImGui.SameLine();
        if (ImGui.Button(T("Aviso de saída da FC", "Removed from FC alert")))
            plugin.Notify(L.LinkRemoved);

        if (ImGui.Button(T("Sincronizar agora", "Sync now")))
        {
            plugin.Sync.FetchSoon();
            plugin.Link.RefreshNow();
        }

        ImGui.SameLine();
        if (ImGui.Button(T("Enviar log agora", "Send log now")))
            plugin.SendLog("botão de teste");
        ImGui.SameLine();
        if (ImGui.Button(T("Novidades como atualização", "What's new as an update")))
            plugin.OpenChangelog("0.9.0");

        if (plugin.Link.Status.Length > 0)
            ImGui.TextColored(UiKit.Muted, plugin.Link.Status);
    }
}
