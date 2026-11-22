using System;
using System.Linq;

namespace FcTimers;

// Atualiza um config salvo por uma versão antiga. Cada passo roda uma vez só, guiado por Config.Version.
// Fica fora do Plugin para dar para testar sem o jogo.
public static class ConfigMigration
{
    public const int CurrentVersion = 11;
    public const string GeneralPlot = "geral";
    public const string GeneralPlotName = "Plantação";
    public const int VoyageHistoryDays = 60;

    public static void Run(Configuration config, long now)
    {
        if (config.Version < 4)
        {
            config.Voyages = LootLedger.MigrateKeys(config.Voyages);
            config.Version = 4;
        }

        // Até a versão 0.6.0 a 5ª posição era lida como um submarino, duplicando timer e loot.
        if (config.Version < 5)
        {
            foreach (var key in config.Subs.Keys.Where(k => !LootLedger.IsRealSlot(k.Split(':')[^1])).ToList())
                config.Subs.Remove(key);
            foreach (var key in config.Voyages.Keys.Where(k => !LootLedger.IsRealSlot(k.Split(':')[^2])).ToList())
                config.Voyages.Remove(key);
            foreach (var key in config.SyncedVoyages.Keys.Where(k => !LootLedger.IsRealSlot(k.Split(':')[^2])).ToList())
                config.SyncedVoyages.Remove(key);
            config.Version = 5;
        }

        // A versão de testes guardava uma rega só, que vira o plot "Plantação".
        if (config.Version < 7 && config.Garden.WateredAt != 0 && !config.Plots.ContainsKey(GeneralPlot))
            config.Plots[GeneralPlot] = new PlotRecord { Name = GeneralPlotName, WateredAt = config.Garden.WateredAt };

        // Quem já usava o plugin continua em português, sem depender do idioma do Dalamud.
        if (config.Version < 8 && config.Language.Length == 0)
            config.Language = "pt";

        // Até a 0.8.2 a posição na chave do plot seguia o idioma do PC (vírgula ou ponto), e um mesmo canteiro
        // tinha chaves diferentes em cada PC. Fica a rega mais recente de cada canteiro.
        if (config.Version < 9)
        {
            foreach (var (oldKey, plot) in config.Plots.Where(p => p.Key.Contains(',')).ToList())
            {
                config.Plots.Remove(oldKey);
                var newKey = oldKey.Replace(',', '.');
                if (!config.Plots.TryGetValue(newKey, out var other) || other.WateredAt < plot.WateredAt)
                    config.Plots[newKey] = plot;
            }
        }

        // Da 0.8.3 até a 0.8.5 o nome dos canteiros de fora levava o ícone do jogo (" 1") ou só o número.
        if (config.Version < 11)
        {
            foreach (var plot in config.Plots.Values)
            {
                var clean = PlotNames.Clean(plot.Name);
                plot.Name = PlotNames.IsUnnamed(clean) ? $"Canteiro {clean}".Trim() : clean;
            }
        }

        config.Version = CurrentVersion;
        PruneVoyages(config, now);
    }

    // O serviço guarda o histórico; aqui ficam 60 dias, mais que os 7 da janela.
    public static void PruneVoyages(Configuration config, long now)
    {
        var before = now - (VoyageHistoryDays * 86400L);
        foreach (var key in config.Voyages.Where(v => v.Value.ReturnTime < before).Select(v => v.Key).ToList())
        {
            config.Voyages.Remove(key);
            config.SyncedVoyages.Remove(key);
        }
    }
}
