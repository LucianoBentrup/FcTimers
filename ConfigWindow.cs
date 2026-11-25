using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
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

    private string fcCode = string.Empty;
    private string accountCode = string.Empty;
    private bool leaveArmed;

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

        DrawLink(config);

        // A chave antiga e o endereço continuam valendo por uma versão, para quem já usa.
        if (ImGui.CollapsingHeader(L.Advanced))
        {
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
        }

        ImGui.TextColored(UiKit.Muted, L.Sent(plugin.Sync.Status));
        ImGui.TextColored(UiKit.Muted, plugin.Sync.LastFetch is { } fetched ? L.Received($"{fetched:HH:mm}") : L.NothingReceived);
        ImGui.Spacing();
    }

    // Ligação por código: colar o código, conferir a FC e confirmar. Depois, ligar a conta do Discord ou desligar.
    private void DrawLink(Configuration config)
    {
        var link = plugin.Link;
        ImGui.Spacing();
        using (Fonts.Heading())
            ImGui.TextUnformatted(L.LinkTitle);

        using (ImRaii.Disabled(link.Busy))
        {
            if (link.Pending)
            {
                ImGui.TextColored(UiKit.Soon, L.LinkRequested);
                if (ImGui.Button(L.LinkRefresh))
                    link.RefreshNow();
                ImGui.SameLine();
                if (ImGui.Button(leaveArmed ? L.LinkLeaveSure : L.LinkCancelRequest))
                {
                    if (leaveArmed)
                        link.Leave();
                    leaveArmed = !leaveArmed;
                }
            }
            else if (link.Linked)
            {
                ImGui.TextColored(UiKit.Good, L.LinkedTo(config.LinkedFcName.Length > 0 ? config.LinkedFcName : "?", config.LinkedServerName));
                ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
                ImGui.InputText(L.LinkAccountCode, ref accountCode, 6, ImGuiInputTextFlags.CharsDecimal);
                UiKit.Tooltip(L.LinkAccountHint);
                ImGui.SameLine();
                using (ImRaii.Disabled(accountCode.Length != 6))
                {
                    if (ImGui.Button(L.LinkAccount))
                    {
                        link.LinkAccount(accountCode);
                        accountCode = string.Empty;
                    }
                }

                // Desligar pede dois cliques, porque o token deixa de valer na hora.
                if (ImGui.Button(leaveArmed ? L.LinkLeaveSure : L.LinkLeave))
                {
                    if (leaveArmed)
                        link.Leave();
                    leaveArmed = !leaveArmed;
                }
            }
            else if (link.Checked is { } pending)
            {
                var fcName = pending.FcName ?? (plugin.CurrentFcTag.Length > 0 ? $"«{plugin.CurrentFcTag}»" : "?");
                ImGui.TextUnformatted(L.LinkConfirm(fcName, pending.ServerName ?? string.Empty));
                if (ImGui.Button(L.LinkYes))
                    link.Confirm(plugin.CurrentFcId, fcName, config.Characters.GetValueOrDefault(plugin.CurrentCharacterId), config.Characters.Values);
                ImGui.SameLine();
                if (ImGui.Button(L.Cancel))
                    link.CancelPending();
            }
            else
            {
                if (plugin.CurrentFcId == 0)
                    ImGui.TextColored(UiKit.Soon, L.LinkNeedsFcId);

                ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
                ImGui.InputText(L.LinkCode, ref fcCode, 16);
                UiKit.Tooltip(L.LinkHint);
                ImGui.SameLine();
                using (ImRaii.Disabled(fcCode.Trim().Length < 6 || plugin.CurrentFcId == 0))
                {
                    if (ImGui.Button(L.LinkCheck))
                        link.CheckCode(fcCode.Trim(), plugin.CurrentFcId);
                }
            }
        }

        if (link.Status.Length > 0)
            ImGui.TextColored(UiKit.Muted, link.Status);
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
            : plugin.Sync.LogStatus);

        ImGui.Spacing();
        if (ImGui.Button(L.WhatsNew))
            plugin.OpenChangelog();
        ImGui.SameLine();
        Support.KofiButton(plugin.IsDevBuild);

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
