using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace FcTimers;

public class MainWindow : Window
{
    // Abaixo disso o desgaste aparece em destaque, igual ao painel do Discord.
    private const double RepairShow = 50;
    private const double RepairLow = 30;
    private const int MaxFieldName = 40;

    private readonly Plugin plugin;
    private (string Key, bool Indoor)? newField;
    private string newFieldName = string.Empty;

    public MainWindow(Plugin plugin) : base("FC Companion###FC Timers")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(520, 360) };
    }

    public override void OnOpen() => plugin.Sync.FetchSoon();

    public override void Draw()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        DrawToolbar();

        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs)
            return;

        // O id depois de ### mantém a aba aberta ao trocar o idioma.
        Tab($"{L.TabOverview}###overview", () => DrawOverview(plugin.View, now));
        Tab($"{L.TabSubs}###subs", () => DrawSubs(plugin.View, now));
        Tab($"{L.TabLoot}###loot", () => DrawLoot(plugin.View));
        Tab($"{L.TabBuffs}###buffs", () => DrawBuffs(plugin.View, now));
        Tab($"{L.TabGarden}###garden", () => DrawGarden(plugin.Config, plugin.View, now));
    }

    private static void Tab(string label, Action draw)
    {
        using var tab = ImRaii.TabItem(label);
        if (tab)
            draw();
    }

    // Status da sincronia e engrenagem das configurações, sempre à vista no topo.
    private void DrawToolbar()
    {
        var (color, text) = plugin.SyncState();
        UiKit.Icon(FontAwesomeIcon.Circle, color);
        ImGui.TextColored(UiKit.Muted, text);

        ImGui.SameLine(ImGui.GetContentRegionMax().X - (ImGui.GetFrameHeight() + 4));
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
            plugin.OpenSettings();
        UiKit.Tooltip(L.TabSettings);
    }

    private void DrawOverview(FcView view, long now)
    {
        if (!plugin.Config.OnboardingDone)
            DrawWelcome(view);

        using var grid = ImRaii.Table("##cards", 2, ImGuiTableFlags.SizingStretchSame);
        if (!grid)
            return;

        // A altura de cada linha de cartões acompanha o cartão com mais linhas, para nada ficar cortado.
        var subsLines = Math.Max(1, view.Subs.Count) + (WorstCondition(view) < RepairShow ? 1 : 0);
        var lootLines = 1 + view.Voyages.Select(v => v.Voyage).Where(v => LootLedger.DayOf(v, GameTime.PanelZone) == GameTime.Today)
            .Select(v => v.SubName).Distinct().Count();
        var gardenLines = Math.Clamp(view.Plots.Count(p => p.NextWaterAt != 0), 1, 4);
        var buffLines = Math.Max(1, view.Wheels.Count) + 1;

        var top = UiKit.CardHeight(Math.Max(subsLines, lootLines));
        var middle = UiKit.CardHeight(Math.Max(gardenLines, 4));
        ImGui.TableNextColumn();
        UiKit.Card("##c-subs", FontAwesomeIcon.Ship, L.TabSubs, top, () => OverviewSubs(view, now));
        ImGui.TableNextColumn();
        UiKit.Card("##c-loot", FontAwesomeIcon.Coins, L.LootToday, top, () => OverviewLoot(view));
        ImGui.TableNextColumn();
        UiKit.Card("##c-garden", FontAwesomeIcon.Seedling, L.TabGarden, middle, () => OverviewGarden(view, now));
        ImGui.TableNextColumn();
        UiKit.Card("##c-stock", FontAwesomeIcon.GasPump, L.Supplies, middle, () => OverviewSupplies(view, now));
        ImGui.TableNextColumn();
        UiKit.Card("##c-buffs", FontAwesomeIcon.Dharmachakra, L.TabBuffs, UiKit.CardHeight(buffLines), () => OverviewBuffs(view, now));
    }

    // Primeira vez: três passos com o que falta, e um botão para cada um.
    private void DrawWelcome(FcView view)
    {
        using var child = ImRaii.Child("##welcome", new Vector2(-1, 140), true);
        if (!child)
            return;

        UiKit.Icon(FontAwesomeIcon.HandSparkles, UiKit.Good);
        ImGui.TextUnformatted(L.Welcome);
        ImGui.Spacing();

        var enabled = plugin.CurrentCharacterEnabled;
        Step(enabled, L.StepCharacter);
        if (!enabled && plugin.CurrentCharacterId != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(L.EnableCharacter))
                plugin.EnableCurrentCharacter();
        }

        var synced = plugin.Config.SyncEnabled && plugin.Config.ServiceKey.Length > 0;
        Step(synced, L.StepDiscord);
        ImGui.SameLine();
        if (ImGui.SmallButton(L.TabSettings))
            plugin.OpenSettings();

        Step(view.Subs.Count > 0 || view.Supplies != null, L.StepData);

        if (ImGui.Button(L.Done))
        {
            plugin.Config.OnboardingDone = true;
            plugin.SaveConfig();
        }
    }

    private static void Step(bool done, string text)
    {
        UiKit.Icon(done ? FontAwesomeIcon.CheckCircle : FontAwesomeIcon.Circle, done ? UiKit.Good : UiKit.Muted);
        UiKit.Text(text, done ? UiKit.Muted : null);
    }

    private static void OverviewSubs(FcView view, long now)
    {
        if (view.Subs.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoSubs);
            return;
        }

        foreach (var sub in view.Subs.OrderBy(s => s.ReturnTime))
        {
            var state = sub.ReturnTime == 0 ? L.Idle : Plugin.FormatRemaining(sub.ReturnTime - now);
            UiKit.Text($"{sub.Name}: {state}", sub.ReturnTime == 0 ? null : UiKit.TimeColor(sub.ReturnTime, now));
            UiKit.TimeTooltip(sub.ReturnTime);
        }

        var worst = WorstCondition(view);
        if (worst < RepairShow)
        {
            UiKit.Icon(FontAwesomeIcon.Wrench, worst < RepairLow ? UiKit.Bad : UiKit.Soon);
            UiKit.Text(L.Wear($"{Math.Floor(worst)}%"), worst < RepairLow ? UiKit.Bad : UiKit.Soon);
        }
    }

    private static void OverviewLoot(FcView view)
    {
        var today = GameTime.Today;
        var voyages = view.Voyages.Select(v => v.Voyage).Where(v => LootLedger.DayOf(v, GameTime.PanelZone) == today).ToList();
        if (voyages.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoVoyages);
            return;
        }

        ImGui.TextColored(UiKit.Good, $"{voyages.Sum(v => v.Items.Sum(i => i.Best)):N0} gil");
        foreach (var bySub in voyages.GroupBy(v => v.SubName))
            ImGui.TextUnformatted($"{bySub.Key}: {bySub.Sum(v => v.Items.Sum(i => i.Best)):N0}");
    }

    private static void OverviewGarden(FcView view, long now)
    {
        var watered = view.Plots.Where(p => p.NextWaterAt != 0).ToList();
        if (watered.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoPlots);
            return;
        }

        foreach (var plot in watered.Take(4))
        {
            UiKit.Text($"{plot.Name}: {PlotState(plot, now)}", PlotColor(plot, now));
            UiKit.TimeTooltip(plot.NextWaterAt > now ? plot.NextWaterAt : plot.DieAt);
        }
    }

    private static void OverviewSupplies(FcView view, long now)
    {
        if (view.Supplies is not { } stock)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoSupplies);
            return;
        }

        var (tanksPerRound, kitsPerRepair, subs) = Costs(view);
        var rounds = tanksPerRound > 0 ? stock.Tanks / tanksPerRound : -1;
        UiKit.Text(L.Tanks($"{stock.Tanks:N0}"), rounds is >= 0 and < 2 ? UiKit.Bad : null);
        if (rounds >= 0)
            UiKit.Bar(Math.Min(rounds, 10) / 10f, L.Rounds(rounds), rounds < 2 ? UiKit.Bad : rounds < 4 ? UiKit.Soon : UiKit.Good);
        ImGui.TextUnformatted(L.Kits($"{stock.Kits:N0}") + (kitsPerRepair > 0 ? $" ({L.Repairs(stock.Kits / kitsPerRepair, subs)})" : string.Empty));
        var age = now - stock.ObservedAt;
        ImGui.TextColored(UiKit.Muted, age < 60 ? L.CountedNow : L.CountedAgo(Plugin.FormatRemaining(age)));
    }

    private static void OverviewBuffs(FcView view, long now)
    {
        if (view.Wheels.Count == 0)
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoWheels);

        foreach (var wheel in view.Wheels.OrderBy(w => w.ReadyTime))
        {
            UiKit.Text($"{wheel.Name}: {Plugin.FormatRemaining(wheel.ReadyTime - now)}", UiKit.TimeColor(wheel.ReadyTime, now));
            UiKit.TimeTooltip(wheel.ReadyTime);
        }

        var active = view.ActiveBuffs.Count(b => b.EndTime == 0 || b.EndTime > now);
        ImGui.TextColored(UiKit.Muted, $"{L.ActiveBuffs}: {active}");
    }

    // Desgaste da peça mais gasta entre todos os subs; 100 quando ainda não foi lido.
    private static double WorstCondition(FcView view)
        => view.Subs.Where(s => s.Details?.Condition != null).Select(s => s.Details!.Condition!.Value).DefaultIfEmpty(100).Min();

    // Os subs saem e são reparados juntos, então uma rodada e um reparo contam todos.
    private static (int TanksPerRound, int KitsPerRepair, int Subs) Costs(FcView view)
    {
        var known = view.Subs.Select(s => s.Details).Where(d => d != null).ToList();
        return (known.Sum(d => d!.TanksPerVoyage), known.Sum(d => d!.RepairKits), known.Count);
    }

    private static void DrawSubs(FcView view, long now)
    {
        if (view.Subs.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoSubs);
        }
        else
        {
            // Build e rota numa linha discreta. Se os subs forem diferentes, cada um mostra a sua na dica.
            var setups = view.Subs.Select(s => s.Details).Where(d => d != null && d.Build.Length > 0)
                .Select(d => L.SetupLine(d!.Build, d.Route, d.TanksPerVoyage)).Distinct().ToList();
            if (setups.Count == 1)
                ImGui.TextColored(UiKit.Muted, setups[0]);

            using var table = UiKit.Table("##subs", L.ColSub, L.ColReturn, L.ColRank, L.ColParts);
            if (table != null)
            {
                foreach (var sub in view.Subs.OrderBy(s => s.ReturnTime))
                {
                    var details = sub.Details;
                    ImGui.TableNextRow();
                    UiKit.Cell(sub.Name);
                    if (setups.Count > 1 && details != null)
                        UiKit.Tooltip(L.SetupLine(details.Build, details.Route, details.TanksPerVoyage));

                    UiKit.Cell(sub.ReturnTime == 0 ? L.Idle : Plugin.FormatRemaining(sub.ReturnTime - now), sub.ReturnTime == 0 ? null : UiKit.TimeColor(sub.ReturnTime, now));
                    UiKit.TimeTooltip(sub.ReturnTime);

                    ImGui.TableNextColumn();
                    if (details != null)
                        UiKit.Bar(details.ExpPercent / 100f, $"R{details.Rank} · {details.ExpPercent}%", UiKit.Muted);
                    else
                        ImGui.TextUnformatted("-");

                    ImGui.TableNextColumn();
                    if (details?.Condition is { } condition)
                        UiKit.Bar((float)(condition / 100), $"{Math.Floor(condition)}%", condition < RepairLow ? UiKit.Bad : condition < RepairShow ? UiKit.Soon : UiKit.Good);
                    else
                        ImGui.TextUnformatted("-");
                }
            }
        }

        ImGui.Spacing();
        UiKit.Heading(FontAwesomeIcon.GasPump, L.Supplies);
        OverviewSupplies(view, now);
    }

    private static void DrawLoot(FcView view)
    {
        ImGui.TextColored(UiKit.Muted, L.LootTitle);
        if (view.Voyages.Count == 0)
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoVoyages);

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
            var label = $"{day.Key:dd/MM}: {L.DayTotal($"{(market > 0 ? "~" : string.Empty)}{total:N0}", L.Voyages(day.Count()))}{extra}###{day.Key:yyyyMMdd}";
            var flags = day.Key == today ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader(label, flags))
                continue;

            using var table = UiKit.Table($"##loot{day.Key:yyyyMMdd}", L.ColItem, L.ColCount, L.ColGil);
            if (table == null)
                continue;

            var items = all
                .GroupBy(i => i.Name)
                .Select(g => (Name: g.Key, Count: g.Sum(i => i.Count), Value: g.Sum(i => i.Best), Market: g.Any(i => i.IsMarket)))
                .OrderByDescending(i => i.Value);

            foreach (var item in items)
            {
                ImGui.TableNextRow();
                UiKit.Cell(item.Name);
                UiKit.Cell($"{item.Count}");
                UiKit.Cell($"{item.Value:N0}", item.Market ? UiKit.Soon : null);
                if (item.Market)
                    UiKit.Tooltip(L.MarketHint);
            }
        }
    }

    private static void DrawBuffs(FcView view, long now)
    {
        UiKit.Heading(FontAwesomeIcon.Dharmachakra, L.Wheels);
        if (view.Wheels.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoWheels);
        }
        else
        {
            using var wheels = UiKit.Table("##wheels", L.ColBuff, L.ColReady);
            if (wheels != null)
            {
                foreach (var wheel in view.Wheels.OrderBy(w => w.ReadyTime))
                {
                    ImGui.TableNextRow();
                    UiKit.Cell(wheel.Name);
                    UiKit.Cell(Plugin.FormatRemaining(wheel.ReadyTime - now), UiKit.TimeColor(wheel.ReadyTime, now));
                    UiKit.TimeTooltip(wheel.ReadyTime);
                }
            }
        }

        ImGui.Spacing();
        UiKit.Heading(FontAwesomeIcon.Bolt, L.ActiveBuffs);
        var active = view.ActiveBuffs.Where(b => b.EndTime == 0 || b.EndTime > now).ToList();
        if (active.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.None);
            return;
        }

        using var table = UiKit.Table("##active", L.ColBuff, L.ColEnds);
        if (table == null)
            return;

        foreach (var buff in active)
        {
            ImGui.TableNextRow();
            UiKit.Cell(buff.Name);
            UiKit.Cell(buff.EndTime == 0 ? "?" : Plugin.FormatRemaining(buff.EndTime - now), buff.EndTime != 0 && buff.EndTime - now < 3600 ? UiKit.Soon : null);
            UiKit.TimeTooltip(buff.EndTime);
        }
    }

    // Em cima, o estado da plantação. Embaixo, a seção Campos, que só cadastra, renomeia e remove.
    private void DrawGarden(Configuration config, FcView view, long now)
    {
        var watered = view.Plots.Where(p => p.NextWaterAt != 0).ToList();
        if (watered.Count == 0)
        {
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoWaterYet);
        }
        else
        {
            using var table = UiKit.Table("##garden", L.ColField, L.ColState, L.ColWater);
            if (table != null)
            {
                foreach (var plot in watered)
                {
                    ImGui.TableNextRow();
                    UiKit.Cell(plot.Name);
                    UiKit.Cell(PlotState(plot, now), PlotColor(plot, now));
                    UiKit.TimeTooltip(plot.NextWaterAt > now ? plot.NextWaterAt : plot.DieAt);

                    // Quanto do dia de rega já passou: cheia quando a planta já pode ser regada.
                    ImGui.TableNextColumn();
                    var passed = 1f - ((plot.NextWaterAt - now) / (float)PlotRecord.WaterIntervalSeconds);
                    UiKit.Bar(passed, string.Empty, PlotColor(plot, now) ?? UiKit.Muted);
                }
            }
        }

        if (ImGui.Button(L.WaterAll))
            plugin.WaterAll("botão Reguei todos agora");
        UiKit.Tooltip(L.WaterAllHint);

        ImGui.Spacing();
        UiKit.Heading(FontAwesomeIcon.MapMarkerAlt, L.Fields);
        if (config.Plots.Count == 0 && newField == null)
            UiKit.Empty(FontAwesomeIcon.InfoCircle, L.NoPlots);

        if (config.Plots.Count > 0)
        {
            using var fields = UiKit.Table("##fields", L.ColName, L.ColPlace, string.Empty);
            if (fields != null)
            {
                foreach (var (key, record) in config.Plots.OrderBy(p => p.Value.Name))
                {
                    ImGui.PushID(key);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    var name = record.Name;
                    ImGui.SetNextItemWidth(-1);

                    // O nome só é gravado ao sair do campo de texto, para não mandar um nome por letra digitada.
                    ImGui.InputText("##nome", ref name, MaxFieldName);
                    if (ImGui.IsItemDeactivatedAfterEdit() && name.Trim().Length > 0)
                        plugin.RenameField(key, name.Trim());

                    ImGui.TableNextColumn();
                    ImGui.TextColored(UiKit.Muted, $"{(record.Indoor ? L.Inside : L.Outside)} · {Coordinates(key)}");
                    ImGui.TableNextColumn();
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash))
                        plugin.RemovePlot(key);
                    UiKit.Tooltip(L.Remove);
                    ImGui.PopID();
                }
            }
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
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus) && target is { } chosen)
            {
                newField = (chosen.Key, chosen.Indoor);
                newFieldName = chosen.Name;
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(exists ? L.FieldExists : target == null ? L.AddFieldHint : L.AddField);
    }

    // A chave termina com X, Y e Z do objeto. Mostra X e Z, que é o que muda de um campo para outro.
    private static string Coordinates(string key)
    {
        var parts = key.Split(':');
        return parts.Length >= 7 ? $"X {parts[^3]} · Z {parts[^1]}" : key;
    }

    // Pronta para regar fica verde; perto de morrer (menos de 3 horas) ou já passada, vermelha.
    private static Vector4? PlotColor(PlotView plot, long now)
        => plot.NextWaterAt > now ? (plot.NextWaterAt - now < 3600 ? UiKit.Soon : null)
            : plot.DieAt > now + (3 * 3600) ? UiKit.Good : UiKit.Bad;

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
}
