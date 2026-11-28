using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FcTimers;

public sealed partial class Plugin
{
    // Rega na mão: vale para todos os plots conhecidos, ou cria o plot geral se ainda não houver nenhum.
    public void WaterAll(string source)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Config.Plots.Count == 0)
            Config.Plots[GeneralPlot] = new PlotRecord { Name = GeneralPlotName };

        foreach (var plot in Config.Plots.Values)
        {
            plot.WateredAt = now;
            Beds.Tend(plot, 0, now);
        }
        SaveConfig();
        Diagnostics.Write($"Rega registrada em todos os plots ({source})");
        QueueLogUpload();
    }

    public void RemovePlot(string key)
    {
        Config.Plots.Remove(key);
        SaveConfig();
        Sync.ReportPlotRemoval(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Diagnostics.Write($"Campo removido da lista: {key}");
        QueueLogUpload();
    }

    // O objeto da casa selecionado agora, com o nome do menu quando houver ("Canteiro 2"). Null se não for um campo válido.
    public (string Key, string Name, bool Indoor)? FieldTarget()
    {
        if (ReadHousingTarget() is not var (key, name, indoor))
            return null;

        if (lastHousingObject is { } menu && menu.Key == key)
            name = menu.Name;
        return (key, name.Length > 0 ? name : "Canteiro", indoor);
    }

    public void AddField(string key, string name, bool indoor)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!Config.Plots.TryGetValue(key, out var plot))
        {
            plot = new PlotRecord();
            Config.Plots[key] = plot;
        }

        plot.Name = name;
        plot.Indoor = indoor;
        plot.NamedAt = now;
        ApplyPendingLink(key, plot);
        SaveConfig();
        Sync.ReportPlotName(key, name, indoor, now);
        Diagnostics.Write($"Campo cadastrado: {name} ({(indoor ? "dentro de casa" : "fora")}, chave {key})");
        QueueLogUpload();
    }

    public void RenameField(string key, string name)
    {
        if (!Config.Plots.TryGetValue(key, out var plot) || plot.Name == name)
            return;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        plot.Name = name;
        plot.NamedAt = now;
        SaveConfig();
        Sync.ReportPlotName(key, name, plot.Indoor, now);
    }

    // Nomes e remoções feitos em outro PC chegam pelo serviço e valem aqui também.
    private void ApplyRemoteFields(RemoteState remote)
    {
        var changed = false;
        foreach (var (key, removedAt) in remote.RemovedPlots)
        {
            if (Config.Plots.TryGetValue(key, out var plot) && Math.Max(plot.WateredAt, plot.NamedAt) <= removedAt)
            {
                Config.Plots.Remove(key);
                Diagnostics.Write($"Campo removido em outro PC: {plot.Name}");
                changed = true;
            }
        }

        foreach (var (key, shared) in remote.Plots.Where(p => p.Value.NameAt > 0))
        {
            if (!Config.Plots.TryGetValue(key, out var plot))
            {
                if (remote.RemovedPlots.GetValueOrDefault(key) >= shared.NameAt)
                    continue;
                plot = new PlotRecord { Indoor = shared.Indoor };
                Config.Plots[key] = plot;
            }
            else if (shared.NameAt <= plot.NamedAt)
            {
                continue;
            }

            plot.Name = shared.Name;
            plot.NamedAt = shared.NameAt;
            changed = true;
        }

        if (changed)
            SaveConfig();
    }

    // O plot regado é o objeto selecionado no momento da mensagem. A chave junta a casa, o quarto e a posição dele,
    // que só muda se alguém mover o plot. Sem um alvo da casa, a rega vai para o plot geral.
    private unsafe string WaterTargetedPlot(string source)
    {
        // Se o alvo já sumiu quando a mensagem chega, vale o último objeto da casa cujo menu foi aberto há pouco.
        // O menu também dá o nome do canteiro ("2nd Patch"), que o alvo não tem.
        var housing = ReadHousingTarget();
        var recent = lastHousingObject is { } last && DateTime.UtcNow - last.SeenAt < TimeSpan.FromMinutes(2) ? last : default((string Key, string Name, bool Indoor, DateTime SeenAt)?);
        if (housing == null && recent is { } fallback)
            housing = (fallback.Key, fallback.Name, fallback.Indoor);
        if (housing is var (targetKey, _, targetIndoor) && recent is { } menu && menu.Key == targetKey)
            housing = (targetKey, menu.Name, targetIndoor);

        string key;
        string name;
        var indoor = false;
        if (housing is var (housingKey, baseName, inside))
        {
            key = housingKey;
            indoor = inside;
            var label = baseName.Trim().Length > 0 ? baseName.Trim() : "Canteiro";
            // O nome do menu ("Canteiro 2") corrige os nomes automáticos; um nome dado na janela fica.
            var fromMenu = PlotNames.AutoName.IsMatch(label);
            name = Config.Plots.TryGetValue(key, out var known) && (known.NamedAt > 0 || (!PlotNames.IsUnnamed(known.Name) && !(fromMenu && PlotNames.AutoName.IsMatch(known.Name))))
                ? known.Name
                : fromMenu ? label : $"{label} {Config.Plots.Values.Count(p => p.Name.StartsWith(label, StringComparison.Ordinal)) + 1}";
        }
        else
        {
            key = GeneralPlot;
            name = GeneralPlotName;
        }

        if (!Config.Plots.TryGetValue(key, out var plot))
        {
            plot = new PlotRecord { Name = name };
            Config.Plots[key] = plot;
        }

        plot.Name = name;
        plot.Indoor = indoor;
        plot.WateredAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SaveConfig();
        ApplyPendingLink(key, plot);
        Diagnostics.Write($"Rega registrada em {plot.Name} ({source}, {(indoor ? "dentro de casa" : "fora")}, chave {key})");
        QueueLogUpload();
        return key;
    }

    // Chave, nome e se está dentro de casa (sala ou quarto privado), ou null se o alvo não for um objeto da casa.
    private unsafe (string Key, string Name, bool Indoor)? ReadHousingTarget()
    {
        var target = TargetManager.Target;
        var manager = HousingManager.Instance();
        if (target == null || manager == null || target.ObjectKind is not (ObjectKind.HousingEventObject or ObjectKind.EventObj))
            return null;

        // A posição sai sempre com ponto, para a chave ser a mesma em PCs com idiomas diferentes.
        var position = target.Position;
        var coordinates = string.Join(':', new[] { position.X, position.Y, position.Z }.Select(v => v.ToString("F1", CultureInfo.InvariantCulture)));
        var key = $"{ClientState.TerritoryType}:{manager->GetCurrentWard()}:{manager->GetCurrentPlot()}:{manager->GetCurrentRoom()}:{coordinates}";
        return (key, PlotNames.Clean(target.Name.TextValue), manager->IsInside());
    }

    // Registra o menu de objetos da casa, como os plots, e guarda qual objeto foi, para a rega que vier em seguida.
    private unsafe void OnSelectString(AddonEvent type, AddonArgs args)
    {
        var target = TargetManager.Target;
        if (target == null || target.ObjectKind is not (ObjectKind.HousingEventObject or ObjectKind.EventObj))
            return;

        var addon = (AtkUnitBase*)args.Addon.Address;
        var texts = new List<string>();
        for (var i = 0; i < addon->AtkValuesCount; i++)
        {
            var value = addon->AtkValues[i];
            if (value.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString)
            {
                var text = value.String.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text);
            }
        }

        // Os canteiros de fora não têm nome no alvo; o menu diz qual é ("6th Bed, 2nd Patch").
        if (ReadHousingTarget() is var (key, name, indoor))
        {
            var patch = PlotNames.FromMenu(texts);
            var label = patch ?? name;
            lastHousingObject = (key, label, indoor, DateTime.UtcNow);
            lastBed = PlotNames.BedFromMenu(texts);
            if (texts.Any(t => PlotNames.GardenMenu.IsMatch(t)))
                LinkGardenObject(key, indoor, lastBed, texts.Any(t => PlotNames.HarvestMenu.IsMatch(t)));

            // Só de abrir o menu, um canteiro já conhecido com nome automático ganha o nome certo.
            if (patch != null && Config.Plots.TryGetValue(key, out var known) && known.Name != label && known.NamedAt == 0
                && (PlotNames.IsUnnamed(known.Name) || PlotNames.AutoName.IsMatch(known.Name)))
            {
                Diagnostics.Write($"Plot renomeado: {PlotNames.Clean(known.Name)} -> {label} (chave {key})");
                known.Name = label;
                SaveConfig();
            }
        }

        Diagnostics.Write($"Menu de {target.Name.TextValue} ({target.ObjectKind}): {string.Join(" | ", texts)}");
    }

    private void OnLogMessage(ILogMessage message)
    {
        var id = message.LogMessageId;
        if (id == PotTendedMessage)
        {
            Diagnostics.Write($"Mensagem do vaso {id}: {message.GameData.ValueNullable?.Text.ToString()}");
            if (characterEnabled && !currentOtherFc)
                TendBeds(WaterTargetedPlot($"mensagem do jogo {id}"), 0);
            return;
        }

        // Fora das mensagens da horta, nada vai para o log: elas podem trazer nome de outros jogadores.
        if (id < FirstGardenMessage || id > LastGardenMessage)
            return;

        Diagnostics.Write($"Mensagem da horta {id}: {message.GameData.ValueNullable?.Text.ToString()}");
        if (!characterEnabled || currentOtherFc)
            return;

        if (id == CropTendedMessage)
            TendBeds(WaterTargetedPlot($"mensagem do jogo {id}"), BedOfMessage());
        else if (id == SeedsPlantedMessage)
            MarkPlanted(WaterTargetedPlot($"mensagem do jogo {id}"), BedOfMessage());
        else if (id == FertilizedMessage)
            FertilizeBeds(BedOfMessage());
    }

}
