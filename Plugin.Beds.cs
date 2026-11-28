using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace FcTimers;

public sealed partial class Plugin
{
    private static readonly TimeSpan BedReadInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PlantMarkWindow = TimeSpan.FromMinutes(5);

    private readonly Dictionary<ushort, string> seedNames = [];

    // Chaves do mapa de objetos que ainda batem com o que o menu de cada campo mostrou. Uma só: ligação feita.
    private readonly Dictionary<string, HashSet<ushort>> linkCandidates = [];
    private readonly Dictionary<string, ushort> pendingLinks = [];
    private readonly Dictionary<(string Key, int Bed), DateTime> plantMarks = [];
    private DateTime nextBedRead;

    // Nome do que a semente dá ("Old World Fig"), que é como a planta aparece no jogo.
    public string SeedName(ushort seed)
    {
        if (seedNames.TryGetValue(seed, out var name))
            return name;

        var row = DataManager.GetExcelSheet<GardeningSeed>()?.GetRowOrDefault(seed);
        name = row?.Item.ValueNullable?.Name.ToString() ?? $"#{seed}";
        seedNames[seed] = name;
        return name;
    }

    private unsafe void ReadBeds(long now)
    {
        if (DateTime.UtcNow < nextBedRead)
            return;
        nextBedRead = DateTime.UtcNow + BedReadInterval;

        var manager = HousingManager.Instance();
        if (manager == null || (manager->OutdoorTerritory == null && manager->IndoorTerritory == null))
            return;

        var changed = false;
        foreach (var (key, plot) in Config.Plots)
        {
            if (plot.ObjectKey == 0 || !IsHere(key, plot.Indoor, manager) || ReadSlots(manager, plot.Indoor, plot.ObjectKey) is not { } slots)
                continue;

            var first = plot.BedsReadAt == 0;
            var events = Beds.Apply(plot, slots, now, bed => PlantMark(key, bed));
            foreach (var e in events)
                Diagnostics.Write($"{plot.Name}, Bed {e.Bed}: {e.Kind} {SeedName(e.Seed)}");
            changed |= first || events.Count > 0;
        }

        if (changed)
            SaveConfig();
    }

    private long? PlantMark(string key, int bed)
    {
        if (!plantMarks.Remove((key, bed), out var at) && !plantMarks.Remove((key, 0), out at))
            return null;
        return DateTime.UtcNow - at < PlantMarkWindow ? new DateTimeOffset(at).ToUnixTimeSeconds() : null;
    }

    // A chave do campo começa com território, ward, plot e quarto. O mapa de fora cobre o ward inteiro.
    private unsafe bool IsHere(string key, bool indoor, HousingManager* manager)
    {
        var parts = key.Split(':');
        if (parts.Length < 4 || parts[0] != ClientState.TerritoryType.ToString() || parts[1] != manager->GetCurrentWard().ToString())
            return false;
        if (!indoor)
            return manager->OutdoorTerritory != null;
        return manager->IndoorTerritory != null && parts[2] == manager->GetCurrentPlot().ToString() && parts[3] == manager->GetCurrentRoom().ToString();
    }

    private static unsafe HousingObjectManager* ObjectManager(HousingManager* manager, bool indoor)
    {
        if (indoor)
            return manager->IndoorTerritory == null ? null : &manager->IndoorTerritory->FurnitureManager.ObjectManager;
        return manager->OutdoorTerritory == null ? null : &manager->OutdoorTerritory->FurnitureManager.ObjectManager;
    }

    private static unsafe BedSlot[]? ReadSlots(HousingManager* manager, bool indoor, ushort objectKey)
    {
        var objects = ObjectManager(manager, indoor);
        if (objects == null)
            return null;

        foreach (var pair in objects->DataMap)
        {
            if (pair.Item1 == objectKey)
                return ToSlots(pair.Item2);
        }

        return null;
    }

    private static BedSlot[] ToSlots(HousingObjectManager.HousingObjectData data)
    {
        var slots = new List<BedSlot>();
        foreach (var value in data.ValueSets)
            slots.Add(new BedSlot(value.Value1, value.Value2));
        return slots.ToArray();
    }

    // Bate o menu aberto (Bed e se está pronta) com o mapa de objetos. As chaves que não batem saem da lista;
    // quando sobra uma, o campo fica ligado a ela.
    private unsafe void LinkGardenObject(string key, bool indoor, int bed, bool ready)
    {
        var manager = HousingManager.Instance();
        var objects = manager == null ? null : ObjectManager(manager, indoor);
        if (objects == null)
            return;

        var matching = new HashSet<ushort>();
        foreach (var pair in objects->DataMap)
        {
            if (Matches(ToSlots(pair.Item2), bed, ready))
                matching.Add(pair.Item1);
        }

        if (Config.Plots.TryGetValue(key, out var known) && known.ObjectKey != 0)
        {
            if (matching.Contains(known.ObjectKey))
                return;
            Diagnostics.Write($"Ligação do campo {known.Name} não bate mais com o menu; refazendo");
            known.ObjectKey = 0;
            SaveConfig();
        }

        var direct = DirectKeys(objects).Where(matching.Contains).ToList();
        if (direct.Count > 0)
            matching = [direct[0]];
        else if (linkCandidates.TryGetValue(key, out var before))
            matching.IntersectWith(before);

        if (matching.Count == 0)
        {
            linkCandidates.Remove(key);
            return;
        }

        if (matching.Count > 1)
        {
            linkCandidates[key] = matching;
            return;
        }

        linkCandidates.Remove(key);
        pendingLinks[key] = matching.First();
        if (Config.Plots.TryGetValue(key, out var plot))
        {
            ApplyPendingLink(key, plot);
            SaveConfig();
        }
    }

    private static bool Matches(BedSlot[] slots, int bed, bool ready)
    {
        if (bed > 0)
            return bed <= slots.Length && slots[bed - 1].Seed != 0 && (slots[bed - 1].Phase == Beds.ReadyPhase) == ready;
        return slots.Any(s => s.Seed != 0 && (s.Phase == Beds.ReadyPhase) == ready);
    }

    // Se o alvo é um dos objetos da casa, o próprio objeto diz a chave.
    private unsafe IEnumerable<ushort> DirectKeys(HousingObjectManager* objects)
    {
        var target = TargetManager.Target;
        if (target == null)
            return [];

        var address = (GameObject*)target.Address;
        var count = Math.Min((int)objects->ObjectArray.ObjectCount, objects->ObjectArray.Objects.Length);
        for (var i = 0; i < count; i++)
        {
            if (objects->ObjectArray.Objects[i].Value != address)
                continue;
            var housing = (HousingObject*)address;
            return new[] { housing->HousingObjectId.EntryId, (ushort)housing->HousingFurnitureIndex, (ushort)i }.Distinct().ToArray();
        }

        return [];
    }

    private void ApplyPendingLink(string key, PlotRecord plot)
    {
        if (!pendingLinks.Remove(key, out var objectKey) || plot.ObjectKey == objectKey)
            return;
        plot.ObjectKey = objectKey;
        plot.Beds.Clear();
        plot.BedsReadAt = 0;
        Diagnostics.Write($"Campo {plot.Name} ligado ao objeto {objectKey}");
        nextBedRead = DateTime.MinValue;
    }

    private int BedOfMessage()
        => lastHousingObject is { } last && DateTime.UtcNow - last.SeenAt < TimeSpan.FromMinutes(2) ? lastBed : 0;

    private string? RecentGardenKey()
    {
        if (ReadHousingTarget() is var (key, _, _))
            return key;
        return lastHousingObject is { } last && DateTime.UtcNow - last.SeenAt < TimeSpan.FromMinutes(2) ? last.Key : null;
    }

    private void TendBeds(string key, int bed)
    {
        if (Config.Plots.TryGetValue(key, out var plot) && plot.Beds.Count > 0)
        {
            Beds.Tend(plot, bed, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            SaveConfig();
        }
    }

    private void MarkPlanted(string key, int bed)
    {
        plantMarks[(key, bed)] = DateTime.UtcNow;
        nextBedRead = DateTime.UtcNow + TimeSpan.FromSeconds(1);
    }

    private void FertilizeBeds(int bed)
    {
        if (RecentGardenKey() is not { } key || !Config.Plots.TryGetValue(key, out var plot) || plot.Beds.Count == 0)
            return;
        Beds.Fertilize(plot, bed, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        SaveConfig();
        Diagnostics.Write($"Adubo em {plot.Name}{(bed > 0 ? $", Bed {bed}" : string.Empty)}");
    }
}
