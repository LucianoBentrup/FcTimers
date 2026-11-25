using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FcTimers;

// Configurações numa janela própria, aberta pela engrenagem do instalador do Dalamud ou pela janela principal.
public sealed class ConfigWindow : ThemedWindow
{
    // Largo o bastante para "Automático (Behemoth)" e nomes de data center.
    private const float ComboWidth = 230;

    private static readonly (string Code, string Name)[] Languages = [("pt", "Português"), ("en", "English")];

    private readonly Plugin plugin;
    private DateTime copiedAt = DateTime.MinValue;

    public ConfigWindow(Plugin plugin) : base("FC Companion - Settings###FcTimersConfig")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300) };
    }

    public override void Draw()
    {
        var config = plugin.Config;
        DrawGeneral(config);
        DrawAlerts(config);
        DrawDiscord(config);
        DrawDiagnostics(config);
    }

    private void DrawGeneral(Configuration config)
    {
        UiKit.Heading(FontAwesomeIcon.User, L.Characters);
        // Com nomes repetidos, o servidor aparece junto: "Bunny Chan (Behemoth)".
        var repeated = config.Characters.Values.GroupBy(c => c.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        foreach (var (id, character) in config.Characters.OrderBy(c => c.Value.Name).ThenBy(c => c.Value.World))
        {
            var enabled = character.Enabled;
            var world = repeated.Contains(character.Name) ? $" ({(character.World.Length > 0 ? character.World : L.UnknownWorld)})" : string.Empty;
            var mark = (id == plugin.CurrentCharacterId ? $" · {L.Current}" : string.Empty) + (character.OtherFc ? $" · {L.OtherFc}" : string.Empty);
            if (ImGui.Checkbox($"{character.Name}{world}{mark}###{id}", ref enabled))
            {
                character.Enabled = enabled;
                plugin.SaveConfig();
            }
        }

        if (config.Characters.Values.Any(c => c.OtherFc))
        {
            using (ImRaii.PushColor(ImGuiCol.Text, UiKit.Muted))
                ImGui.TextWrapped(L.OtherFcHint);
        }

        ImGui.Spacing();
        var current = Array.FindIndex(Languages, l => l.Code == plugin.LanguageCode);
        ImGui.SetNextItemWidth(ComboWidth);
        if (ImGui.Combo(L.Language, ref current, Languages.Select(l => l.Name).ToArray(), Languages.Length))
        {
            config.Language = Languages[current].Code;
            plugin.SaveConfig();
        }

        var worlds = plugin.MarketWorlds();
        var options = worlds.Prepend(L.MarketAuto(config.HomeWorld.Length > 0 ? config.HomeWorld : "?")).ToArray();
        var selected = config.MarketWorld.Length == 0 ? 0 : Math.Max(0, worlds.IndexOf(config.MarketWorld) + 1);
        ImGui.SetNextItemWidth(ComboWidth);
        if (ImGui.Combo(L.Market, ref selected, options, options.Length))
        {
            config.MarketWorld = selected == 0 ? string.Empty : worlds[selected - 1];
            plugin.SaveConfig();
        }

        UiKit.Tooltip(L.MarketHint);
        ImGui.Spacing();
    }

    private void DrawAlerts(Configuration config)
    {
        UiKit.Heading(FontAwesomeIcon.Bell, L.Alerts);
        Toggle(L.AlertSubs, () => config.AlertSubs, v => config.AlertSubs = v);
        Toggle(L.AlertWheels, () => config.AlertWheels, v => config.AlertWheels = v);
        Toggle(L.AlertPlots, () => config.AlertPlots, v => config.AlertPlots = v);
        Toggle(L.AlertAirships, () => config.AlertAirships, v => config.AlertAirships = v);
        Toggle(L.AlertHouse, () => config.AlertHouse, v => config.AlertHouse = v);
        ImGui.Spacing();
        Toggle(L.AlertToast, () => config.AlertToast, v => config.AlertToast = v);
        Toggle(L.AlertChat, () => config.AlertChat, v => config.AlertChat = v);
        Toggle(L.AlertSound, () => config.AlertSound, v => config.AlertSound = v);
        if (config.AlertSound)
        {
            var sound = config.AlertSoundId;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderInt(L.AlertSoundId, ref sound, 1, 16))
            {
                config.AlertSoundId = sound;
                plugin.SaveConfig();
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(L.Test))
                plugin.Notify(L.SubReturned("Navi"));
        }

        ImGui.Spacing();
    }

    private void DrawDiscord(Configuration config)
    {
        UiKit.Heading(FontAwesomeIcon.Wifi, "Discord");
        var sync = config.SyncEnabled;
        if (ImGui.Checkbox(L.SyncEnabled, ref sync))
        {
            config.SyncEnabled = sync;
            plugin.SaveConfig();
        }

        var url = config.ServiceUrl;
        if (ImGui.InputText(L.ServiceUrl, ref url, 256))
        {
            config.ServiceUrl = url;
            plugin.SaveConfig();
        }

        var key = config.ServiceKey;
        if (ImGui.InputText(L.ServiceKey, ref key, 256, ImGuiInputTextFlags.Password))
        {
            config.ServiceKey = key;
            plugin.SaveConfig();
        }

        ImGui.TextColored(UiKit.Muted, L.Sent(plugin.Sync.Status));
        ImGui.TextColored(UiKit.Muted, plugin.Sync.LastFetch is { } fetched ? L.Received($"{fetched:HH:mm}") : L.NothingReceived);
        ImGui.Spacing();
    }

    private void DrawDiagnostics(Configuration config)
    {
        UiKit.Heading(FontAwesomeIcon.Stethoscope, L.Diagnostics);
        var detailed = config.DetailedLog;
        if (ImGui.Checkbox(L.DetailedLog, ref detailed))
        {
            config.DetailedLog = detailed;
            plugin.SaveConfig();
        }

        UiKit.Tooltip(L.DetailedLogHint);

        if (ImGui.Button(L.CopyLog))
        {
            ImGui.SetClipboardText(plugin.LogText());
            copiedAt = DateTime.Now;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!config.SyncEnabled || plugin.Sync.SendingLog || !plugin.CanSendLog))
        {
            if (ImGui.Button(L.SendLog))
                plugin.SendLog("botão Enviar log");
        }

        if (!plugin.CanSendLog && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(L.SendLogInactive);

        ImGui.SameLine();
        ImGui.TextColored(UiKit.Muted, DateTime.Now - copiedAt < TimeSpan.FromSeconds(5)
            ? L.Copied
            : plugin.Sync.LogStatus.Length > 0 ? plugin.Sync.LogStatus : L.LogHasNoKey);
        ImGui.TextColored(UiKit.Muted, L.SendLogHint);

        ImGui.Spacing();
        if (ImGui.Button(L.WhatsNew))
            plugin.OpenChangelog();

        if (plugin.IsDevBuild)
        {
            ImGui.Spacing();
            if (ImGui.Button(L.ReplayWelcome))
                plugin.ReplayWelcome();
            UiKit.Tooltip(L.DevOnly);
        }
    }

    private void Toggle(string label, Func<bool> get, Action<bool> set)
    {
        var value = get();
        if (ImGui.Checkbox(label, ref value))
        {
            set(value);
            plugin.SaveConfig();
        }
    }
}
