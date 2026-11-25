using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FcTimers;

// Desgaste das peças dos subs. O jogo carrega isso ao entrar na oficina, em 5 posições por sub:
// casco, popa, proa, ponte e uma vazia. O desgaste do sub é o da peça mais gasta.
public static class SubmarineParts
{
    private const int SlotsPerSub = 5;
    private const int PartsPerSub = 4;

    // O jogo guarda o desgaste de 0 a 30000, que vira 0% a 100%. Chave: posição do sub, de 0 a 3.
    public static unsafe Dictionary<int, List<PartRecord>> ReadParts()
    {
        var conditions = new Dictionary<int, List<PartRecord>>();
        var manager = InventoryManager.Instance();
        if (manager == null)
            return conditions;

        var container = manager->GetInventoryContainer(InventoryType.HousingInteriorPlacedItems2);
        if (container == null || !container->IsLoaded)
            return conditions;

        for (var sub = 0; sub < LootLedger.MaxSubmarines; sub++)
        {
            var parts = new List<PartRecord>();
            for (var slot = sub * SlotsPerSub; slot < (sub * SlotsPerSub) + PartsPerSub && slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item != null && item->ItemId != 0)
                    parts.Add(new PartRecord { ItemId = item->ItemId, Condition = item->Condition / 300.0 });
            }

            if (parts.Count == PartsPerSub)
                conditions[sub] = parts;
        }

        return conditions;
    }
}
