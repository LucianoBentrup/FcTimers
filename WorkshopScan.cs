using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace FcTimers;

// Rank, peças, rota e desgaste de cada sub, lidos quando a lista de subs da oficina é aberta. Só lê, sem abrir nenhuma tela.
public static class WorkshopScan
{
    // As peças vêm em grupos de 4 por classe; a partir da 6ª classe são as modificadas.
    private const string ClassLetters = "SUWCY";

    public static unsafe SubDetails Read(HousingWorkshopSubmersibleSubData* sub, IDataManager data, double? condition)
    {
        var exploration = data.GetExcelSheet<SubmarineExploration>();
        var parts = data.GetExcelSheet<SubmarinePart>();

        var partIds = new[] { sub->HullId, sub->SternId, sub->BowId, sub->BridgeId };
        var route = sub->CurrentExplorationPoints.ToArray().Where(p => p != 0)
            .Select(p => exploration.GetRowOrDefault(p)).Where(p => p != null).Select(p => p!.Value).ToList();

        return new SubDetails
        {
            Rank = sub->RankId,
            ExpPercent = sub->NextLevelExp == 0 ? 0 : (int)(100L * sub->CurrentExp / sub->NextLevelExp),
            Build = string.Concat(partIds.Select(BuildLetter)),
            Route = string.Join(" > ", route.Select(p => p.Location.ToString())),
            TanksPerVoyage = route.Sum(p => p.CeruleumTankReq),
            RepairKits = partIds.Sum(id => parts.GetRowOrDefault(id)?.RepairMaterials ?? 0),
            Condition = condition,
        };
    }

    private static string BuildLetter(ushort partId)
    {
        if (partId == 0)
            return "?";

        var group = (partId - 1) / 4;
        if (group >= ClassLetters.Length * 2)
            return "?";

        return group < ClassLetters.Length ? $"{ClassLetters[group]}" : $"{ClassLetters[group - ClassLetters.Length]}+";
    }

    // Só com o log detalhado: o que a oficina tem da última viagem, para conferir com o jogo.
    public static unsafe List<string> DescribeLastVoyage(HousingWorkshopSubmersibleSubData* sub, IDataManager data)
    {
        var lines = new List<string>();
        var exploration = data.GetExcelSheet<SubmarineExploration>();
        var items = data.GetExcelSheet<Item>();
        string ItemName(uint id) => id == 0 ? "nada" : items.GetRowOrDefault(id)?.Name.ToString() ?? id.ToString();

        foreach (var point in sub->GatheredData)
        {
            if (point.Point == 0)
                continue;

            var name = exploration.GetRowOrDefault(point.Point)?.Location.ToString() ?? point.Point.ToString();
            lines.Add($"{sub->NameString}, última viagem, {name}: XP {point.ExpGained}, {point.ItemCountPrimary}x {ItemName(point.ItemIdPrimary)}"
                + $" + {point.ItemCountAdditional}x {ItemName(point.ItemIdAdditional)}, pesca dupla {point.DoubleDip}, setor novo {point.UnlockedPoint}");
        }

        return lines;
    }
}
