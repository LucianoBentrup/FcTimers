using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FcTimers;

public class MainWindow : Window
{
    private readonly Plugin plugin;
    private DateTime copiedAt = DateTime.MinValue;

    public MainWindow(Plugin plugin) : base("FC Timers")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 240) };
    }

    public override void OnOpen() => plugin.Sync.FetchSoon();

    public override void Draw()
    {
        var config = plugin.Config;
        var view = plugin.View;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        ImGui.TextUnformatted("Submarinos");
        ImGui.Separator();
        if (view.Subs.Count == 0)
            ImGui.TextUnformatted("Nenhum dado ainda. Abra o painel de submarinos na oficina da FC.");

        foreach (var sub in view.Subs.OrderBy(s => s.ReturnTime))
        {
            var state = sub.ReturnTime == 0 ? "parado" : Plugin.FormatRemaining(sub.ReturnTime - now);
            ImGui.TextUnformatted($"{sub.Name}: {state}");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Buffs");
        ImGui.Separator();
        if (view.Wheels.Count == 0)
            ImGui.TextUnformatted("Nenhum buff registrado. Abra a mesa da roda na casa da FC.");

        foreach (var wheel in view.Wheels.OrderBy(w => w.ReadyTime))
            ImGui.TextUnformatted($"{wheel.Name}: {Plugin.FormatRemaining(wheel.ReadyTime - now)}");

        DrawGarden(config, view, now);
        DrawCharacters(config);
        DrawLoot(view);

        ImGui.Spacing();
        ImGui.TextUnformatted("Buffs ativos");
        ImGui.Separator();
        var active = view.ActiveBuffs.Where(b => b.EndTime == 0 || b.EndTime > now).ToList();
        if (active.Count == 0)
            ImGui.TextUnformatted("Nenhum");

        foreach (var buff in active)
            ImGui.TextUnformatted($"{buff.Name}: {(buff.EndTime == 0 ? "?" : Plugin.FormatRemaining(buff.EndTime - now))}");

        DrawDiscord(config);
        DrawDiagnostics(config);
    }

    private void DrawGarden(Configuration config, FcView view, long now)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Plantação");
        ImGui.Separator();

        if (view.Plots.Count == 0)
            ImGui.TextUnformatted("Nenhuma rega registrada. Regue um plot com o plugin ligado.");

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
                if (ImGui.SmallButton("Remover"))
                    plugin.RemovePlot(plot.Key);
            }

            ImGui.PopID();
        }

        if (ImGui.Button("Reguei todos agora"))
            plugin.WaterAll("botão Reguei todos agora");
        ImGui.SameLine();
        ImGui.TextDisabled("ou /fct regar. Cada plot também é registrado sozinho ao regar.");

        if (!ImGui.TreeNode("Tempos da rega"))
            return;

        var interval = config.WaterIntervalHours;
        if (ImGui.InputInt("Horas até poder regar de novo", ref interval) && interval is >= 1 and <= 240)
        {
            config.WaterIntervalHours = interval;
            plugin.SaveConfig();
        }

        var die = config.DieAfterHours;
        if (ImGui.InputInt("Horas até morrer sem rega", ref die) && die is >= 1 and <= 480)
        {
            config.DieAfterHours = die;
            plugin.SaveConfig();
        }

        ImGui.TextDisabled("Contados a partir da última rega. Valores ainda a confirmar no jogo.");
        ImGui.TreePop();
    }

    private static string PlotState(PlotView plot, long now)
    {
        if (plot.NextWaterAt > now)
            return $"regar em {Plugin.FormatRemaining(plot.NextWaterAt - now)}";
        if (plot.DieAt > now)
            return $"pronta para regar. Morre em {Plugin.FormatRemaining(plot.DieAt - now)}";
        return plot.DieAt > 0 ? "passou do prazo de rega" : "pronta para regar";
    }

    private void DrawCharacters(Configuration config)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Personagens que leem e enviam dados");
        ImGui.Separator();

        foreach (var (id, character) in config.Characters.OrderBy(c => c.Value.Name))
        {
            var enabled = character.Enabled;
            var current = id == plugin.CurrentCharacterId ? " (atual)" : string.Empty;
            if (ImGui.Checkbox($"{character.Name}{current}###{id}", ref enabled))
            {
                character.Enabled = enabled;
                plugin.SaveConfig();
            }
        }
    }

    private void DrawDiscord(Configuration config)
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Discord"))
            return;

        var enabled = config.SyncEnabled;
        if (ImGui.Checkbox("Enviar e receber dados do painel da FC", ref enabled))
        {
            config.SyncEnabled = enabled;
            plugin.SaveConfig();
        }

        var url = config.ServiceUrl;
        if (ImGui.InputText("Endereço do serviço", ref url, 256))
        {
            config.ServiceUrl = url;
            plugin.SaveConfig();
        }

        var key = config.ServiceKey;
        if (ImGui.InputText("Chave da FC", ref key, 256, ImGuiInputTextFlags.Password))
        {
            config.ServiceKey = key;
            plugin.SaveConfig();
        }

        ImGui.TextUnformatted($"Envio: {plugin.Sync.Status}");
        ImGui.TextUnformatted(plugin.Sync.LastFetch is { } fetched
            ? $"Recebido: {fetched:HH:mm}"
            : "Recebido: ainda nada, mostrando só os dados deste PC");
    }

    private void DrawDiagnostics(Configuration config)
    {
        ImGui.Spacing();
        if (!ImGui.CollapsingHeader("Diagnóstico"))
            return;

        var detailed = config.DetailedLog;
        if (ImGui.Checkbox("Log detalhado", ref detailed))
        {
            config.DetailedLog = detailed;
            plugin.SaveConfig();
        }

        ImGui.TextDisabled("Desligado: só loot, rega, horta e erros. Ligado: também cada leitura e envio.");

        if (ImGui.Button("Copiar log"))
        {
            var header = $"FC Timers {Plugin.Version} | fuso UTC{DateTimeOffset.Now:zzz} | copiado {DateTime.Now:dd/MM HH:mm}";
            ImGui.SetClipboardText(plugin.Diagnostics.Text(header));
            copiedAt = DateTime.Now;
        }

        ImGui.SameLine();
        ImGui.TextDisabled(DateTime.Now - copiedAt < TimeSpan.FromSeconds(5)
            ? "Copiado. Cole no Discord."
            : "O log não inclui a chave da FC.");
    }

    private static void DrawLoot(FcView view)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Loot por dia (valor de venda para NPC, dia de Brasília)");
        ImGui.Separator();
        if (view.Voyages.Count == 0)
            ImGui.TextUnformatted("Nenhuma viagem registrada ainda.");

        var today = GameTime.Today;
        var days = view.Voyages.Select(v => v.Voyage)
            .GroupBy(v => LootLedger.DayOf(v, GameTime.PanelZone))
            .OrderByDescending(d => d.Key)
            .Take(7);

        foreach (var day in days)
        {
            var total = LootLedger.Total(day);
            var voyages = day.Count() == 1 ? "1 viagem" : $"{day.Count()} viagens";
            var label = $"{day.Key:dd/MM}: {total:N0} gil em {voyages}###{day.Key:yyyyMMdd}";
            var flags = day.Key == today ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
            if (!ImGui.CollapsingHeader(label, flags))
                continue;

            var items = day.SelectMany(v => v.Items)
                .GroupBy(i => i.Name)
                .Select(g => (Name: g.Key, Count: g.Sum(i => i.Count), Value: g.Sum(i => i.Value)))
                .OrderByDescending(i => i.Value);

            foreach (var item in items)
                ImGui.TextUnformatted($"{item.Count}x {item.Name}: {item.Value:N0} gil");
        }
    }
}
