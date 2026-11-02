using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace FcTimers;

// O jogo guarda na memória de cada sub o loot da última viagem coletada. Esse loot muda
// no instante em que o resultado de uma viagem chega, então o plugin salva quando ele muda,
// e não depende do intervalo entre o sub voltar e sair de novo.
public static unsafe class SubmarineLoot
{
    // Itens na mesma ordem em que Read monta a lista, para comparar com viagens já salvas.
    public static string Signature(HousingWorkshopSubmersibleSubData* sub)
    {
        var parts = new List<string>();
        foreach (ref readonly var sector in sub->GatheredData)
        {
            if (sector.ItemIdPrimary != 0 && sector.ItemCountPrimary != 0)
                parts.Add($"{sector.ItemIdPrimary}|{sector.ItemCountPrimary}");
            if (sector.ItemIdAdditional != 0 && sector.ItemCountAdditional != 0)
                parts.Add($"{sector.ItemIdAdditional}|{sector.ItemCountAdditional}");
        }

        return string.Join(';', parts);
    }

    public static string Signature(IEnumerable<LootItem> items) => string.Join(';', items.Select(i => $"{i.ItemId}|{i.Count}"));

    public static VoyageRecord? Read(
        HousingWorkshopSubmersibleSubData* sub,
        string name,
        long returnTime,
        Func<uint, (string Name, uint Price)?> lookup,
        Func<uint, string?> sectorName)
    {
        var items = new List<LootItem>();
        var unlocked = new List<string>();
        foreach (ref readonly var sector in sub->GatheredData)
        {
            Add(items, sector.ItemIdPrimary, sector.ItemCountPrimary, sector.ItemHQPrimary, lookup);
            Add(items, sector.ItemIdAdditional, sector.ItemCountAdditional, sector.ItemHQAdditional, lookup);
            if (sector.UnlockedPoint != 0 && sectorName(sector.UnlockedPoint) is { Length: > 0 } unlockedName)
                unlocked.Add(unlockedName);
        }

        return items.Count == 0 ? null : new VoyageRecord { SubName = name, ReturnTime = returnTime, Items = items, Unlocked = unlocked };
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
