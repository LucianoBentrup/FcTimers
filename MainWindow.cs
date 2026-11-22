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
    private const int MaxFieldName = 40;

    private DateTime copiedAt = DateTime.MinValue;
    private (string Key, bool Indoor)? newField;
    private string newFieldName = string.Empty;

    public MainWindow(Plugin plugin) : base("FC Companion###FC Timers")
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
        // Os subs são reparados juntos, então um reparo conta os kits de todos.
        var kitsPerRepair = known.Sum(d => d!.RepairKits);
        ImGui.TextUnformatted(L.Tanks($"{stock.Tanks:N0}") + (tanksPerRound > 0 ? $" ({L.Rounds(stock.Tanks / tanksPerRound)})" : string.Empty));
        ImGui.TextUnformatted(L.Kits($"{stock.Kits:N0}") + (kitsPerRepair > 0 ? $" ({L.Repairs(stock.Kits / kitsPerRepair, known.Count)})" : string.Empty));
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

    // Em cima, o estado da plantação. Embaixo, a seção Campos, que só cadastra, renomeia e remove.
    private void DrawGarden(Configuration config, FcView view, long now)
    {
        var watered = view.Plots.Where(p => p.NextWaterAt != 0).ToList();
        if (watered.Count == 0)
            ImGui.TextDisabled(L.NoWaterYet);

        foreach (var plot in watered)
            ImGui.TextUnformatted($"{plot.Name}: {PlotState(plot, now)}");

        if (ImGui.Button(L.WaterAll))
            plugin.WaterAll("botão Reguei todos agora");
        ImGui.SameLine();
        ImGui.TextDisabled(L.WaterAllHint);

        ImGui.Spacing();
        ImGui.TextUnformatted(L.Fields);
        ImGui.Separator();
        if (config.Plots.Count == 0 && newField == null)
            ImGui.TextDisabled(L.NoPlots);

        foreach (var (key, record) in config.Plots.OrderBy(p => p.Value.Name))
        {
            ImGui.PushID(key);
            var name = record.Name;
            ImGui.SetNextItemWidth(170);

            // O nome só é gravado ao sair do campo de texto, para não mandar um nome por letra digitada.
            ImGui.InputText("##nome", ref name, MaxFieldName);
            if (ImGui.IsItemDeactivatedAfterEdit() && name.Trim().Length > 0)
                plugin.RenameField(key, name.Trim());

            ImGui.SameLine();
            ImGui.TextDisabled($"{(record.Indoor ? L.Inside : L.Outside)} · {Coordinates(key)}");
            ImGui.SameLine();
            if (ImGui.SmallButton(L.Remove))
                plugin.RemovePlot(key);
            ImGui.PopID();
        }

        DrawNewField(config);
    }

    // O "+" só funciona com um canteiro ou vaso da casa selecionado no jogo e que ainda não esteja na lista.
    private void DrawNewField(Configuration config)
    {
        if (newField is { } pending)
        {
            ImGui.SetNextItemWidth(170);
            ImGui.InputText(L.FieldName, ref newFieldName, MaxFieldName);
            ImGui.SameLine();
            using (ImRaii.Disabled(newFieldName.Trim().Length == 0))
            {
                if (ImGui.SmallButton(L.Save))
                {
                    plugin.AddField(pending.Key, newFieldName.Trim(), pending.Indoor);
                    newField = null;
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(L.Cancel))
                newField = null;
            return;
        }

        var target = plugin.FieldTarget();
        var exists = target is { } t && config.Plots.ContainsKey(t.Key);
        using (ImRaii.Disabled(target == null || exists))
        {
            if (ImGui.Button("+") && target is { } chosen)
            {
                newField = (chosen.Key, chosen.Indoor);
                newFieldName = chosen.Name;
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && (target == null || exists))
            ImGui.SetTooltip(exists ? L.FieldExists : L.AddFieldHint);
    }

    // A chave termina com X, Y e Z do objeto. Mostra X e Z, que é o que muda de um campo para outro.
    private static string Coordinates(string key)
    {
        var parts = key.Split(':');
        return parts.Length >= 7 ? $"X {parts[^3]} · Z {parts[^1]}" : key;
    }

    private static string PlotState(PlotView plot, long now)
    {
        if (plot.NextWaterAt == 0)
            return L.NoWaterYet;
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
