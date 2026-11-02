using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FcTimers;

public readonly record struct SupplyCount(int Tanks, int Kits);

// Tanques de ceruleum e kits de reparo dos subs. O baú da FC só fica carregado depois que alguém abre,
// então cada página guarda a última contagem vista. O inventário de quem está jogando entra na soma.
public static class Supplies
{
    public const uint CeruleumTank = 10155;
    public const uint RepairMaterials = 10373;

    private static readonly InventoryType[] ChestPages =
    [
        InventoryType.FreeCompanyPage1,
        InventoryType.FreeCompanyPage2,
        InventoryType.FreeCompanyPage3,
        InventoryType.FreeCompanyPage4,
        InventoryType.FreeCompanyPage5,
    ];

    private static readonly InventoryType[] Bags =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
    ];

    // Só as páginas que o jogo carregou. Página 1 a 5.
    public static unsafe Dictionary<int, SupplyCount> ReadChest()
    {
        var pages = new Dictionary<int, SupplyCount>();
        var manager = InventoryManager.Instance();
        if (manager == null)
            return pages;

        for (var i = 0; i < ChestPages.Length; i++)
        {
            var container = manager->GetInventoryContainer(ChestPages[i]);
            if (container == null || !container->IsLoaded)
                continue;

            int tanks = 0, kits = 0;
            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null)
                    continue;
                if (item->ItemId == CeruleumTank)
                    tanks += item->Quantity;
                else if (item->ItemId == RepairMaterials)
                    kits += item->Quantity;
            }

            pages[i + 1] = new SupplyCount(tanks, kits);
        }

        return pages;
    }

    public static unsafe SupplyCount? ReadBags()
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return null;

        int tanks = 0, kits = 0;
        foreach (var bag in Bags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null || !container->IsLoaded)
                return null;

            tanks += manager->GetItemCountInContainer(CeruleumTank, bag);
            kits += manager->GetItemCountInContainer(RepairMaterials, bag);
        }

        return new SupplyCount(tanks, kits);
    }
}
