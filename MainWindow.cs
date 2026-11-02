using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FcTimers;

public class MainWindow : Window
{
    // Abaixo disso o desgaste aparece em destaque, igual ao painel do Discord.
    private const double RepairShow = 50;
    private const double RepairLow = 30;

    private static readonly (string Code, string Name)[] Languages = [("pt", "Português"), ("en", "English")];

    private readonly Plugin plugin;
    private DateTime copiedAt = DateTime.MinValue;

    public MainWindow(Plugin plugin) : base("FC Timers")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(380, 260) };
    }

    public override void OnOpen() => plugin.Sync.FetchSoon();

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs)
            return;

        // O id depois de ### mantém a aba aberta ao trocar o idioma.
        Tab($"{L.TabSubs}###subs", () => DrawSubs(plugin.View, now));
        Tab($"{L.TabLoot}###loot", () => DrawLoot(plugin.View));
        Tab($"{L.TabBuffs}###buffs", () => DrawBuffs(plugin.View, now));
        Tab($"{L.TabGarden}###garden", () => DrawGarden(plugin.Config, plugin.View, now));
        Tab($"{L.TabSettings}###settings", () => DrawSettings(plugin.Config));
    }

    private static void Tab(string label, Action draw)
    {
        using var tab = ImRaii.TabItem(label);
        if (tab)
            draw();
    }

    private static void DrawSubs(FcView view, long now)
    {
        if (view.Subs.Count == 0)
            ImGui.TextUnformatted(L.NoSubs);

        // Build e rota numa linha discreta. Se os subs forem diferentes, cada um mostra a sua.
        var setups = view.Subs.Select(s => s.Details).Where(d => d != null && d.Build.Length > 0)
            .Select(d => L.SetupLine(d!.Build, d.Route, d.TanksPerVoyage)).Distinct().ToList();
        if (setups.Count == 1)
            ImGui.TextDisabled(setups[0]);

        foreach (var sub in view.Subs.OrderBy(s => s.ReturnTime))
        {
            var details = sub.Details;
            var rank = details == null ? string.Empty : $" (R{details.Rank}, {details.ExpPercent}%)";
            var state = sub.ReturnTime == 0 ? L.Idle : Plugin.FormatRemaining(sub.ReturnTime - now);
            ImGui.TextUnformatted($"{sub.Name}{rank}: {state}");

            if (details?.Condition is { } condition && condition < RepairShow)
            {
                ImGui.SameLine();
                var text = $"{L.Wear($"{Math.Floor(condition)}%")}{(condition < RepairLow ? $", {L.NeedsRepair}" : string.Empty)}";
                if (condition < RepairLow)
                    ImGui.TextColored(new Vector4(1f, 0.45f, 0.35f, 1f), text);
                else
                    ImGui.TextDisabled(text);
            }

            if (setups.Count > 1 && details != null)
                ImGui.TextDisabled($"  {L.SetupLine(details.Build, details.Route, details.TanksPerVoyage)}");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted(L.Supplies);
        ImGui.Separator();
        if (view.Supplies is not { } stock)
        {
            ImGui.TextUnformatted(L.NoSupplies);
            return;
        }

        var known = view.Subs.Select(s => s.Details).Where(d => d != null).ToList();
        var tanksPerRound = known.Sum(d => d!.TanksPerVoyage);
        var kitsPerRepair = known.Count == 0 ? 0 : known.Max(d => d!.RepairKits);
        ImGui.TextUnformatted(L.Tanks($"{stock.Tanks:N0}") + (tanksPerRound > 0 ? $" ({L.Rounds(stock.Tanks / tanksPerRound)})" : string.Empty));
        ImGui.TextUnformatted(L.Kits($"{stock.Kits:N0}") + (kitsPerRepair > 0 ? $" ({L.Repairs(stock.Kits / kitsPerRepair)})" : string.Empty));
        var age = now - stock.ObservedAt;
        ImGui.TextDisabled(age < 60 ? L.CountedNow : L.CountedAgo(Plugin.FormatRemaining(age)));
    }

    private static void DrawLoot(FcView view)
    {
        ImGui.TextDisabled(L.LootTitle);
        if (view.Voyages.Count == 0)
            ImGui.TextUnformatted(L.NoVoyages);

        var today = GameTime.Today;
        var days = view.Voyages.Select(v => v.Voyage)
            .GroupBy(v => LootLedger.DayOf(v, GameTime.PanelZone))
            .OrderByDescending(d => d.Key)
            .Take(7);

        foreach (var day in days)
        {
            var all = day.SelectMany(v => v.Items).ToList();
            var total = all.Sum(i => i.Best);
            var market = all.Where(i => i.IsMarket).Sum(i => i.Best);
            var extra = market > 0 ? $" ({L.IncludesMarket($"{market:N0}")})" : string.Empty;
            var label = $"{day.Key:dd/MM}: {L.DayTotal($"{(market > 0 ? "≈ " : string.Empty)}{total:N0}", L.Voyages(day.Count()))}{extra}###{day.Key:yyyyMMdd}";
            var flags = day.Key == today ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader(label, flags))
                continue;

            var items = all
                .GroupBy(i => i.Name)
                .Select(g => (Name: g.Key, Count: g.Sum(i => i.Count), Value: g.Sum(i => i.Best), Market: g.Any(i => i.IsMarket)))
                .OrderByDescending(i => i.Value);

            foreach (var item in items)
                ImGui.TextUnformatted($"{item.Count}x {item.Name}: {item.Value:N0} gil{(item.Market ? $" ({L.MarketTag})" : string.Empty)}");
        }
    }

    private static void DrawBuffs(FcView view, long now)
    {
        ImGui.TextUnformatted(L.Wheels);
        ImGui.Separator();
        if (view.Wheels.Count == 0)
            ImGui.TextUnformatted(L.NoWheels);

        foreach (var wheel in view.Wheels.OrderBy(w => w.ReadyTime))
            ImGui.TextUnformatted($"{wheel.Name}: {Plugin.FormatRemaining(wheel.ReadyTime - now)}");

        ImGui.Spacing();
        ImGui.TextUnformatted(L.ActiveBuffs);
        ImGui.Separator();
        var active = view.ActiveBuffs.Where(b => b.EndTime == 0 || b.EndTime > now).ToList();
        if (active.Count == 0)
            ImGui.TextUnformatted(L.None);

        foreach (var buff in active)
            ImGui.TextUnformatted($"{buff.Name}: {(buff.EndTime == 0 ? "?" : Plugin.FormatRemaining(buff.EndTime - now))}");
    }

    private void DrawGarden(Configuration config, FcView view, long now)
    {
        if (view.Plots.Count == 0)
            ImGui.TextUnformatted(L.NoPlots);

        foreach (var plot in view.Plots)
        {
            ImGui.PushID(plot.Key);
            var name = plot.Name;
            ImGui.SetNextItemWidth(150);
            if (config.Plots.TryGetValue(plot.Key, out var record))
            {
                // Renomear só vale para os plots que este PC já regou; o nome vai junto na próxima rega.
                if (ImGui.InputText("##nome", ref name, 40) && name.Trim().Length > 0)
                {
                    record.Name = name.Trim();
                    plugin.SaveConfig();
                }
            }
            else
            {
                ImGui.TextUnformatted(name);
            }

            ImGui.SameLine();
            ImGui.TextUnformatted(PlotState(plot, now));
            if (record != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(L.Remove))
                    plugin.RemovePlot(plot.Key);
            }

            ImGui.PopID();
        }

        ImGui.Spacing();
        if (ImGui.Button(L.WaterAll))
            plugin.WaterAll("botão Reguei todos agora");
        ImGui.SameLine();
        ImGui.TextDisabled(L.WaterAllHint);
    }

    private static string PlotState(PlotView plot, long now)
    {
        if (plot.NextWaterAt > now)
            return L.WaterIn(Plugin.FormatRemaining(plot.NextWaterAt - now));
        if (plot.DieAt > now)
            return L.WaterNow(Plugin.FormatRemaining(plot.DieAt - now));
        return plot.DieAt > 0 ? L.WaterLate : L.WaterReady;
    }

    private void DrawSettings(Configuration config)
    {
        var current = Array.FindIndex(Languages, l => l.Code == plugin.LanguageCode);
        ImGui.SetNextItemWidth(150);
        if (ImGui.Combo(L.Language, ref current, Languages.Select(l => l.Name).ToArray(), Languages.Length))
        {
            config.Language = Languages[current].Code;
            plugin.SaveConfig();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted(L.Market);
        ImGui.Separator();
        var worlds = plugin.MarketWorlds();
        var options = worlds.Prepend(L.MarketAuto(config.HomeWorld.Length > 0 ? config.HomeWorld : "?")).ToArray();
        var selected = config.MarketWorld.Length == 0 ? 0 : Math.Max(0, worlds.IndexOf(config.MarketWorld) + 1);
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("##market", ref selected, options, options.Length))
        {
            config.MarketWorld = selected == 0 ? string.Empty : worlds[selected - 1];
            plugin.SaveConfig();
        }

        ImGui.TextDisabled(L.MarketHint);

        ImGui.Spacing();
        ImGui.TextUnformatted(L.Characters);
        ImGui.Separator();
        foreach (var (id, character) in config.Characters.OrderBy(c => c.Value.Name))
        {
            var enabled = character.Enabled;
            var mark = id == plugin.CurrentCharacterId ? $" ({L.Current})" : string.Empty;
            if (ImGui.Checkbox($"{character.Name}{mark}###{id}", ref enabled))
            {
                character.Enabled = enabled;
                plugin.SaveConfig();
            }
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Discord");
        ImGui.Separator();
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

        ImGui.TextUnformatted(L.Sent(plugin.Sync.Status));
        ImGui.TextUnformatted(plugin.Sync.LastFetch is { } fetched ? L.Received($"{fetched:HH:mm}") : L.NothingReceived);

        ImGui.Spacing();
        ImGui.TextUnformatted(L.Diagnostics);
        ImGui.Separator();
        var detailed = config.DetailedLog;
        if (ImGui.Checkbox(L.DetailedLog, ref detailed))
        {
            config.DetailedLog = detailed;
            plugin.SaveConfig();
        }

        ImGui.TextDisabled(L.DetailedLogHint);

        if (ImGui.Button(L.CopyLog))
        {
            ImGui.SetClipboardText(plugin.LogText());
            copiedAt = DateTime.Now;
        }

        ImGui.SameLine();
        using (ImRaii.Disabled(!config.SyncEnabled || plugin.Sync.SendingLog))
        {
            if (ImGui.Button(L.SendLog))
                plugin.SendLog("botão Enviar log");
        }

        ImGui.SameLine();
        ImGui.TextDisabled(DateTime.Now - copiedAt < TimeSpan.FromSeconds(5)
            ? L.Copied
            : plugin.Sync.LogStatus.Length > 0 ? plugin.Sync.LogStatus : L.LogHasNoKey);
        ImGui.TextDisabled(L.SendLogHint);
    }
}
