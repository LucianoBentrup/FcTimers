using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FcTimers;

public readonly record struct CompanyActionInfo(uint RowId, string Name, string Description);

public readonly record struct ActiveStatus(string BaseName, int Param);

public readonly record struct CompanySlot(string ActionName, long Seconds);

public readonly record struct ActiveBuffReading(string Name, long? Seconds);

[Serializable]
public class ActiveBuffRecord
{
    public string Name { get; set; } = string.Empty;
    public long EndTime { get; set; }
}

public static partial class ActiveBuffMatcher
{
    private const long MaxSeconds = 30 * 86400;
    private const long EndTimeTolerance = 60;
    private const long BuffDurationSeconds = 86400;
    private const uint MeatAndMeadFirstRow = 13;
    private const int MeatAndMeadStep = 5;

    // O parâmetro do status é o valor do efeito, e cada nível tem um valor diferente na descrição.
    // Se não houver um único nível possível, fica só o nome-base.
    public static string ResolveName(ActiveStatus status, IEnumerable<CompanyActionInfo> actions)
    {
        var family = actions.Where(a => a.Name.StartsWith(status.BaseName, StringComparison.Ordinal)).OrderBy(a => a.RowId).ToList();
        if (family.Count == 0)
            return status.BaseName;

        // A descrição desse buff escreve o valor por extenso: cinco, dez e quinze minutos.
        if (family[0].RowId == MeatAndMeadFirstRow)
        {
            var level = status.Param / MeatAndMeadStep;
            return status.Param % MeatAndMeadStep == 0 && level >= 1 && level <= family.Count ? family[level - 1].Name : status.BaseName;
        }

        var candidates = family.Where(a => NumberRegex().Matches(a.Description).Any(m => m.Value == status.Param.ToString())).ToList();
        return candidates.Count == 1 ? candidates[0].Name : status.BaseName;
    }

    // Os status dizem quais buffs estão ativos agora. Os slots, quando carregados, trazem o nível e o tempo exatos.
    public static List<ActiveBuffReading> Match(IEnumerable<ActiveStatus> statuses, IEnumerable<CompanyActionInfo> actions, IEnumerable<CompanySlot> slots)
    {
        var free = slots.Where(s => s.Seconds > 0 && s.Seconds <= MaxSeconds).ToList();
        var readings = new List<ActiveBuffReading>();
        foreach (var status in statuses)
        {
            var index = free.FindIndex(s => s.ActionName.StartsWith(status.BaseName, StringComparison.Ordinal));
            if (index < 0)
            {
                readings.Add(new ActiveBuffReading(ResolveName(status, actions), null));
                continue;
            }

            readings.Add(new ActiveBuffReading(free[index].ActionName, free[index].Seconds));
            free.RemoveAt(index);
        }

        return readings;
    }

    // Sem o tempo na leitura, usa o horário de término já conhecido do mesmo buff, se ainda não passou.
    public static List<ActiveBuffReading> WithKnownTimes(List<ActiveBuffReading> readings, List<ActiveBuffRecord> current, long now)
    {
        return readings.Select(reading =>
        {
            if (reading.Seconds != null)
                return reading;

            var known = current.FirstOrDefault(c => c.Name == reading.Name && c.EndTime > now);
            return known == null ? reading : reading with { Seconds = known.EndTime - now };
        }).ToList();
    }

    // Todos os buffs duram 24 horas. Um buff que surgiu agora, sem horário conhecido, começou há poucos segundos.
    // As leituras seguem a mesma ordem dos status.
    public static List<ActiveBuffReading> WithEstimates(List<ActiveBuffReading> readings, List<ActiveStatus> statuses, IReadOnlySet<string> alreadySeen, long elapsedSeconds)
    {
        return readings.Select((reading, index) =>
            reading.Seconds == null && !alreadySeen.Contains(statuses[index].BaseName)
                ? reading with { Seconds = BuffDurationSeconds - elapsedSeconds }
                : reading).ToList();
    }

    // Devolve a nova lista, ou null quando nada mudou de verdade.
    public static List<ActiveBuffRecord>? Update(List<ActiveBuffRecord> current, List<ActiveBuffReading> readings, long now)
    {
        var updated = readings.Select(r => new ActiveBuffRecord
        {
            Name = r.Name,
            EndTime = r.Seconds is { } seconds ? now + seconds : 0,
        }).ToList();

        var same = current.Count == updated.Count && current.Zip(updated).All(p =>
            p.First.Name == p.Second.Name
            && (p.First.EndTime == 0) == (p.Second.EndTime == 0)
            && Math.Abs(p.First.EndTime - p.Second.EndTime) <= EndTimeTolerance);

        return same ? null : updated;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();
}
