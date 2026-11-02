using System;
using System.Collections.Generic;
using System.Linq;

namespace FcTimers;

[Serializable]
public class LootItem
{
    public uint ItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public long Value { get; set; }
}

[Serializable]
public class VoyageRecord
{
    public string SubName { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public List<LootItem> Items { get; set; } = [];
}

// Regras do loot:
// - cada viagem é um registro único, identificado pelo submarino e pelo horário de retorno;
// - registrar a mesma viagem de novo nunca soma duas vezes;
// - o total de um dia é sempre recalculado a partir de todas as viagens daquele dia, então nada zera entre coletas;
// - nenhum registro é apagado, e os dias anteriores continuam disponíveis.
public static class LootLedger
{
    public const int MaxSubmarines = 4;

    public static string VoyageKey(string subKey, long returnTime) => $"{subKey}:{returnTime}";

    public static bool IsRealSlot(string slot) => int.TryParse(slot, out var value) && value < MaxSubmarines;

    // Chaves locais são "<id da casa>:<slot>:<hora de retorno>"; o slot é o penúltimo pedaço.
    public static int SlotOf(string voyageKey) => int.Parse(voyageKey.Split(':')[^2]);

    // Se a mesma viagem vier com dados diferentes, vale a leitura mais recente. Leitura sem itens é ignorada.
    public static bool Upsert(Dictionary<string, VoyageRecord> voyages, string key, VoyageRecord voyage)
    {
        if (voyage.Items.Count == 0)
            return false;

        if (voyages.TryGetValue(key, out var existing) && SameItems(existing.Items, voyage.Items))
            return false;

        voyages[key] = voyage;
        return true;
    }

    public static Dictionary<string, VoyageRecord> MigrateKeys(Dictionary<string, VoyageRecord> voyages)
    {
        var migrated = new Dictionary<string, VoyageRecord>();
        foreach (var (key, voyage) in voyages)
        {
            var separator = key.LastIndexOf(':');
            var subKey = separator < 0 ? key : key[..separator];
            migrated[VoyageKey(subKey, voyage.ReturnTime)] = voyage;
        }

        return migrated;
    }

    public static DateTime DayOf(VoyageRecord voyage, TimeZoneInfo timeZone)
        => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(voyage.ReturnTime), timeZone).Date;

    public static List<VoyageRecord> VoyagesOn(IEnumerable<VoyageRecord> voyages, DateTime day, TimeZoneInfo timeZone)
        => voyages.Where(v => DayOf(v, timeZone) == day.Date).ToList();

    public static long Total(IEnumerable<VoyageRecord> voyages) => voyages.Sum(v => v.Items.Sum(i => i.Value));

    private static bool SameItems(List<LootItem> left, List<LootItem> right)
        => left.Count == right.Count && left.Zip(right).All(p =>
            p.First.ItemId == p.Second.ItemId && p.First.Count == p.Second.Count && p.First.Value == p.Second.Value);
}
