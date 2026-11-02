using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace FcTimers;

// Em teste: descreve no log tudo o que a oficina deixa carregado de cada sub, para conferir com o jogo
// antes de mostrar no painel. Só lê, sem abrir nenhuma tela.
public static class WorkshopScan
{
    public static unsafe List<string> Describe(HousingWorkshopSubmersibleSubData* sub, IDataManager data)
    {
        var lines = new List<string>();
        var exploration = data.GetExcelSheet<SubmarineExploration>();
        var parts = data.GetExcelSheet<SubmarinePart>();
        var ranks = data.GetExcelSheet<SubmarineRank>();
        var items = data.GetExcelSheet<Item>();
        string ItemName(uint id) => id == 0 ? "nada" : items.GetRowOrDefault(id)?.Name.ToString() ?? id.ToString();

        var partIds = new[] { sub->HullId, sub->SternId, sub->BowId, sub->BridgeId };
        var repair = partIds.Sum(id => parts.GetRowOrDefault(id)?.RepairMaterials ?? 0);
        var expToNext = ranks.GetRowOrDefault(sub->RankId)?.ExpToNext ?? 0;
        var percent = sub->NextLevelExp == 0 ? 0 : 100.0 * sub->CurrentExp / sub->NextLevelExp;
        lines.Add($"{sub->NameString}: rank {sub->RankId}, XP {sub->CurrentExp}/{sub->NextLevelExp} ({percent:0.#}%, tabela {expToNext}), "
            + $"peças {string.Join("/", partIds)}, reparo completo {repair} kits");

        lines.Add($"  atributos (base+bônus): Surveillance {sub->SurveillanceBase}+{sub->SurveillanceBonus}, "
            + $"Retrieval {sub->RetrievalBase}+{sub->RetrievalBonus}, Speed {sub->SpeedBase}+{sub->SpeedBonus}, "
            + $"Range {sub->RangeBase}+{sub->RangeBonus}, Favor {sub->FavorBase}+{sub->FavorBonus}, nota {sub->SubmarineRating}");

        var route = sub->CurrentExplorationPoints.ToArray().Where(p => p != 0)
            .Select(p => exploration.GetRowOrDefault(p)).Where(p => p != null).Select(p => p!.Value).ToList();
        var tanks = route.Sum(p => p.CeruleumTankReq);
        lines.Add($"  rota: {(route.Count == 0 ? "nenhuma" : string.Join(" > ", route.Select(p => p.Location.ToString())))}, "
            + $"tanques da rota {tanks}, saiu {DiagnosticLog.Time(sub->RegisterTime)}, volta {DiagnosticLog.Time(sub->ReturnTime)}");

        foreach (var point in sub->GatheredData)
        {
            if (point.Point == 0)
                continue;

            var name = exploration.GetRowOrDefault(point.Point)?.Location.ToString() ?? point.Point.ToString();
            lines.Add($"  última viagem, {name}: XP {point.ExpGained}, itens {point.ItemCountPrimary}x {ItemName(point.ItemIdPrimary)}"
                + $" + {point.ItemCountAdditional}x {ItemName(point.ItemIdAdditional)}, pesca dupla {point.DoubleDip}, "
                + $"setor novo {point.UnlockedPoint}, primeira vez {point.FirstExploration}, sub extra {point.AdditionalSubmarineUnlocked}");
        }

        var logs = 0;
        foreach (var log in sub->Logs)
        {
            if (log.Length > 0)
                logs++;
        }

        lines.Add($"  textos do histórico carregados: {logs}");
        return lines;
    }
}
