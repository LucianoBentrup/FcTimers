using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FcTimers;

public static unsafe class SubmarineLoot
{
    // Só devolve a viagem depois que o submarino voltou. Em viagem, ou sem itens, devolve null.
    public static VoyageRecord? Read(
        HousingWorkshopSubmersibleSubData* sub,
        string name,
        long now,
        Func<uint, (string Name, uint Price)?> lookup)
    {
        if (sub->ReturnTime == 0 || sub->ReturnTime > now)
            return null;

        var items = new List<LootItem>();
        foreach (ref readonly var sector in sub->GatheredData)
        {
            Add(items, sector.ItemIdPrimary, sector.ItemCountPrimary, sector.ItemHQPrimary, lookup);
            Add(items, sector.ItemIdAdditional, sector.ItemCountAdditional, sector.ItemHQAdditional, lookup);
        }

        return items.Count == 0 ? null : new VoyageRecord { SubName = name, ReturnTime = sub->ReturnTime, Items = items };
    }

    private static void Add(List<LootItem> items, uint itemId, ushort count, bool hq, Func<uint, (string Name, uint Price)?> lookup)
    {
        if (itemId == 0 || count == 0 || lookup(itemId) is not { } item)
            return;

        // O NPC paga 10% a mais por itens HQ.
        var price = hq ? (long)Math.Ceiling(item.Price * 1.1) : item.Price;
        items.Add(new LootItem { ItemId = itemId, Name = item.Name, Count = count, Value = price * count });
    }
}
