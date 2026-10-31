using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FcTimers;

public class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("FC Timers")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(360, 240) };
    }

    public override void Draw()
    {
        var config = plugin.Config;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        ImGui.TextUnformatted("Submarinos");
        ImGui.Separator();
        if (config.Subs.Count == 0)
            ImGui.TextUnformatted("Nenhum dado ainda. Abra o painel de submarinos na oficina da FC.");

        foreach (var sub in config.Subs.Values.OrderBy(s => s.ReturnTime))
        {
            var state = sub.ReturnTime == 0 ? "parado" : Plugin.FormatRemaining(sub.ReturnTime - now);
            ImGui.TextUnformatted($"{sub.Name}: {state}");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Buffs");
        ImGui.Separator();
        if (config.Wheels.Count == 0)
            ImGui.TextUnformatted("Nenhum buff registrado. Abra a mesa da roda na casa da FC.");

        foreach (var wheel in config.Wheels.Values.OrderBy(w => w.ReadyTime))
            ImGui.TextUnformatted($"{wheel.Name}: {Plugin.FormatRemaining(wheel.ReadyTime - now)}");

        DrawCharacters(config);
        DrawLoot(config);

        ImGui.Spacing();
        ImGui.TextUnformatted("Buffs ativos");
        ImGui.Separator();
        var active = config.ActiveFcBuffs.Where(b => b.EndTime == 0 || b.EndTime > now).ToList();
        if (active.Count == 0)
            ImGui.TextUnformatted("Nenhum");

        foreach (var buff in active)
            ImGui.TextUnformatted($"{buff.Name}: {(buff.EndTime == 0 ? "?" : Plugin.FormatRemaining(buff.EndTime - now))}");

        DrawDiscord(config);
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
        if (ImGui.Checkbox("Enviar leituras para o painel da FC", ref enabled))
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

        ImGui.TextUnformatted($"Situação: {plugin.Sync.Status}");
    }

    private static void DrawLoot(Configuration config)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Loot por dia (valor de venda para NPC)");
        ImGui.Separator();
        if (config.Voyages.Count == 0)
            ImGui.TextUnformatted("Nenhuma viagem registrada ainda.");

        var today = DateTime.Today;
        var days = config.Voyages.Values
            .GroupBy(v => LootLedger.DayOf(v, TimeZoneInfo.Local))
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
