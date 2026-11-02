using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FcTimers;

public readonly record struct PartReading(InventoryType Container, int Slot, uint ItemId, string Name, double Condition);

// Peças dos subs e airships ("Shark-class Bow" e afins), com o desgaste. Ainda em teste:
// o plugin só registra no log o que encontrar na oficina, para comparar com a tela de reparo do jogo.
public static class SubmarineParts
{
    private static readonly InventoryType[] Containers =
    [
        InventoryType.HousingInteriorPlacedItems1,
        InventoryType.HousingInteriorPlacedItems2,
        InventoryType.HousingInteriorPlacedItems3,
    ];

    // O jogo guarda o desgaste de 0 a 30000, que vira 0% a 100%.
    public static unsafe List<PartReading> Read(Func<uint, string?> name)
    {
        var parts = new List<PartReading>();
        var manager = InventoryManager.Instance();
        if (manager == null)
            return parts;

        foreach (var type in Containers)
        {
            var container = manager->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                    continue;

                var itemName = name(item->ItemId);
                if (itemName == null || !itemName.Contains("-class", StringComparison.OrdinalIgnoreCase))
                    continue;

                parts.Add(new PartReading(type, slot, item->ItemId, itemName, item->Condition / 300.0));
            }
        }

        return parts;
    }
}
