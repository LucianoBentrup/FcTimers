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

    // Valor no market para a quantidade toda, calculado pelo serviço quando a viagem chega. Null: sem preço.
    public long? MarketValue { get; set; }

    // Conta o maior entre o NPC e o market.
    public bool IsMarket => MarketValue > Value;
    public long Best => IsMarket ? MarketValue!.Value : Value;
}

[Serializable]
public class VoyageRecord
{
    public string SubName { get; set; } = string.Empty;
    public long ReturnTime { get; set; }
    public List<LootItem> Items { get; set; } = [];

    // Setores desbloqueados nesta viagem.
    public List<string> Unlocked { get; set; } = [];

    // Volta antiga com que a viagem foi salva antes de mudar de horário. O serviço apaga a cópia antiga. 0: não mudou.
    public long MovedFrom { get; set; }
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

    // Chave da airship: "<id da casa>:air:<slot>". O slot continua sendo o penúltimo pedaço da chave da viagem.
    public const string AirshipTag = "air";

    public static string AirshipKey(string house, int slot) => $"{house}:{AirshipTag}:{slot}";

    public static bool IsAirship(string voyageKey) => voyageKey.Split(':') is { Length: >= 4 } parts && parts[^3] == AirshipTag;

    public static bool IsRealSlot(string slot) => int.TryParse(slot, out var value) && value < MaxSubmarines;

    // Chaves locais são "<id da casa>:<slot>:<hora de retorno>"; o slot é o penúltimo pedaço.
    public static int SlotOf(string voyageKey) => int.Parse(voyageKey.Split(':')[^2]);

    // Volta da viagem dona do loot. Se ninguém com o plugin viu as voltas do meio, a última volta conhecida
    // pode ser de semanas atrás. O loot é da viagem que voltou antes da saída atual, então vale a hora da saída.
    public static long LootReturnTime(long known, long departedAt, long nextReturn)
    {
        if (known == 0 || departedAt == 0 || nextReturn <= departedAt)
            return known;

        var duration = nextReturn - departedAt;
        return departedAt - known > duration * 3 / 2 ? departedAt : known;
    }

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
