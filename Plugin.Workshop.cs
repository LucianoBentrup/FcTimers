using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace FcTimers;

public sealed partial class Plugin
{
    private unsafe void ReadSubmarines(long now)
    {
        var manager = HousingManager.Instance();
        var inside = manager != null && manager->WorkshopTerritory != null;
        if (inside != inWorkshop)
        {
            inWorkshop = inside;
            Diagnostics.Write(inside ? "Entrou na oficina" : "Saiu da oficina");
        }

        if (!inside)
            return;

        var workshop = manager->WorkshopTerritory;
        if (!CheckWorkshopFc(workshop->HouseId))
            return;

        var pointers = workshop->Submersible.DataPointers;
        var changed = false;

        // Rank, peças e desgaste são lidos 1 vez por segundo, e não a cada frame como o loot.
        var conditions = DateTime.UtcNow >= nextScan ? SubmarineParts.ReadParts() : null;
        var lastVoyages = conditions != null && Config.DetailedLog ? new List<string>() : null;

        // A lista tem 5 posições, mas a FC só tem 4 submarinos: a última repete o sub selecionado.
        for (var slot = 0; slot < LootLedger.MaxSubmarines; slot++)
        {
            var sub = pointers[slot].Value;
            if (sub == null || sub->RankId == 0)
                continue;

            var key = $"{workshop->HouseId}:{slot}";
            var name = sub->NameString;
            long returnTime = sub->ReturnTime;

            changed |= CaptureLoot(slot, key, sub, name, now);
            localReadings.Subs[slot] = now;

            if (!Config.Subs.TryGetValue(key, out var record))
            {
                record = new SubRecord { Name = name, ReturnTime = returnTime };
                Config.Subs[key] = record;
                Diagnostics.Detail($"{name}: visto pela primeira vez, volta {DiagnosticLog.Time(returnTime)}");
                changed = true;
            }
            else if (record.Name != name || record.ReturnTime != returnTime)
            {
                if (record.ReturnTime != returnTime)
                {
                    LogReturnChange(key, name, record.ReturnTime, returnTime, now);
                    record.PreviousReturnTime = record.ReturnTime;
                }

                record.Name = name;
                record.ReturnTime = returnTime;
                changed = true;
            }

            if (conditions != null)
            {
                var condition = conditions.TryGetValue(slot, out var read) ? read.Min(p => p.Condition) : record.Details?.Condition;
                if (read != null && !read.SequenceEqual(record.Parts))
                {
                    record.Parts = read;
                    changed = true;
                }

                if (returnTime > 0 && sub->RegisterTime > 0 && record.DepartedAt != sub->RegisterTime)
                {
                    record.DepartedAt = sub->RegisterTime;
                    changed = true;
                }
                var details = WorkshopScan.Read(sub, DataManager, condition);
                if (details != record.Details)
                {
                    LogDetailsChange(name, record.Details, details);
                    record.Details = details;
                    changed = true;
                }

                lastVoyages?.AddRange(WorkshopScan.DescribeLastVoyage(sub, DataManager));
            }

            Sync.ReportSub(slot, name, returnTime, record.Details);
        }

        changed |= CaptureSelectedLoot(workshop, now);
        changed |= ReadAirships(workshop, now);

        if (conditions != null)
        {
            nextScan = DateTime.UtcNow.AddSeconds(1);
            var text = string.Join('\n', lastVoyages ?? []);
            if (lastVoyages != null && text != lastScan)
            {
                lastScan = text;
                if (text.Length > 0)
                    Diagnostics.Detail($"Última viagem na memória:\n{text}");
            }
        }

        if (changed)
            SaveConfig();
    }

    // Airships: nome, volta, rank e XP, e o loot de cada viagem, do mesmo jeito que os subs.
    // Só lê o que a oficina já tem na memória.
    private unsafe bool ReadAirships(WorkshopTerritory* workshop, long now)
    {
        var changed = false;
        var ships = workshop->Airship.Data;
        for (var slot = 0; slot < Math.Min(ships.Length, LootLedger.MaxSubmarines); slot++)
        {
            fixed (HousingWorkshopAirshipSubData* ship = &ships[slot])
            {
                if (ship->RankId == 0)
                    continue;

                var key = LootLedger.AirshipKey($"{workshop->HouseId}", slot);
                var name = ship->NameString;
                long returnTime = ship->ReturnTime;
                changed |= CaptureAirshipLoot(slot, key, ship, name, now);
                localReadings.Airships[slot] = now;

                var details = new SubDetails
                {
                    Rank = ship->RankId,
                    ExpPercent = ship->NextLevelExp == 0 ? 0 : (int)(100L * ship->CurrentExp / ship->NextLevelExp),
                };
                if (!Config.Airships.TryGetValue(key, out var record))
                {
                    record = new SubRecord { Name = name, ReturnTime = returnTime, Details = details };
                    Config.Airships[key] = record;
                    Diagnostics.Write($"Airship {name}: vista pela primeira vez, volta {DiagnosticLog.Time(returnTime)}");
                    changed = true;
                }
                else if (record.Name != name || record.ReturnTime != returnTime || record.Details != details)
                {
                    if (record.ReturnTime != returnTime)
                    {
                        Diagnostics.Detail($"Airship {name}: volta mudou para {DiagnosticLog.Time(returnTime)}");
                        record.PreviousReturnTime = record.ReturnTime;
                    }

                    record.Name = name;
                    record.ReturnTime = returnTime;
                    record.Details = details;
                    changed = true;
                }

                Sync.ReportAirship(slot, name, returnTime, details.Rank, details.ExpPercent);
            }
        }

        return changed;
    }

    private unsafe bool CaptureAirshipLoot(int slot, string key, HousingWorkshopAirshipSubData* ship, string name, long now)
    {
        var signature = SubmarineLoot.Signature(ship);
        if (signature.Length == 0)
            return false;

        // As airships usam as posições 100 a 103 do registro de loot visto, para não misturar com os subs.
        var seenKey = AirshipLootSeen + slot;
        var firstSight = !Config.LootSeen.TryGetValue(seenKey, out var last);
        if (!firstSight && last == signature)
            return false;

        Config.LootSeen[seenKey] = signature;
        if (firstSight && Config.Voyages.Any(v => LootLedger.IsAirship(v.Key) && LootLedger.SlotOf(v.Key) == slot
            && SubmarineLoot.Signature(v.Value.Items) == signature))
            return true;

        Config.Airships.TryGetValue(key, out var record);
        var returnTime = new[] { (long)ship->ReturnTime, record?.ReturnTime ?? 0, record?.PreviousReturnTime ?? 0 }
            .FirstOrDefault(t => t != 0 && t <= now);
        if (returnTime == 0)
            return true;

        var voyage = SubmarineLoot.Read(ship, name, returnTime, LookupItem);
        if (voyage != null && LootLedger.Upsert(Config.Voyages, LootLedger.VoyageKey(key, returnTime), voyage))
        {
            Diagnostics.Write($"Airship {name}: viagem de {DiagnosticLog.Time(returnTime)} salva, {voyage.Items.Count} itens, {LootLedger.Total([voyage]):N0} gil");
            QueueLogUpload();
        }

        return true;
    }

    // O loot na memória muda quando o resultado de uma viagem chega. É esse o momento de salvar.
    private unsafe bool CaptureLoot(int slot, string key, HousingWorkshopSubmersibleSubData* sub, string name, long now)
    {
        var signature = SubmarineLoot.Signature(sub);
        if (signature.Length == 0)
            return false;

        var firstSight = !Config.LootSeen.TryGetValue(slot, out var last);
        if (!firstSight && last == signature)
            return false;

        Config.LootSeen[slot] = signature;

        // Na primeira leitura depois de atualizar, o loot na memória pode ser de uma viagem já salva.
        if (firstSight && Config.Voyages.Any(v => LootLedger.SlotOf(v.Key) == slot && SubmarineLoot.Signature(v.Value.Items) == signature))
            return true;

        // O loot é da última viagem que já voltou. Se o sub já saiu de novo, é a volta anterior.
        Config.Subs.TryGetValue(key, out var record);
        var returnTime = new[] { (long)sub->ReturnTime, record?.ReturnTime ?? 0, record?.PreviousReturnTime ?? 0 }
            .FirstOrDefault(t => t != 0 && t <= now);
        if (returnTime == 0)
        {
            Diagnostics.Write($"{name}: loot novo na memória, mas sem horário de volta conhecido. Não foi salvo.");
            QueueLogUpload();
            return true;
        }

        var voyage = SubmarineLoot.Read(sub, name, returnTime, LookupItem, SectorName);
        if (voyage != null && LootLedger.Upsert(Config.Voyages, LootLedger.VoyageKey(key, returnTime), voyage))
        {
            Diagnostics.Write($"{name}: viagem de {DiagnosticLog.Time(returnTime)} salva, {voyage.Items.Count} itens, {LootLedger.Total([voyage]):N0} gil");
            QueueLogUpload();
        }

        return true;
    }

    // A 5ª posição da lista é o sub selecionado, preenchida pelo servidor quando o relatório da última viagem abre.
    // É o único lugar com o loot quando a coleta foi feita por alguém sem o plugin: as posições 0 a 3 ficam com o loot antigo.
    private unsafe bool CaptureSelectedLoot(WorkshopTerritory* workshop, long now)
    {
        var pointers = workshop->Submersible.DataPointers;
        if (pointers.Length <= LootLedger.MaxSubmarines)
            return false;

        var selected = pointers[LootLedger.MaxSubmarines].Value;
        if (selected == null || selected->RankId == 0)
            return false;

        var signature = SubmarineLoot.Signature(selected);
        if (signature.Length == 0 || signature == lastSelectedLoot)
            return false;
        lastSelectedLoot = signature;

        var name = selected->NameString;
        var slot = -1;
        for (var i = 0; i < LootLedger.MaxSubmarines; i++)
        {
            var sub = pointers[i].Value;
            if (sub != null && sub->RankId != 0 && sub->NameString == name)
                slot = i;
        }

        if (slot < 0)
        {
            Diagnostics.Write($"Relatório de {name}: loot na posição do sub selecionado, mas nenhum sub da lista tem esse nome. Não foi salvo.");
            QueueLogUpload();
            return false;
        }

        // O mesmo loot já salvo (pela leitura normal ou por um relatório aberto antes) não entra de novo.
        if (Config.Voyages.Any(v => !LootLedger.IsAirship(v.Key) && LootLedger.SlotOf(v.Key) == slot && SubmarineLoot.Signature(v.Value.Items) == signature))
        {
            Diagnostics.Detail($"Relatório de {name}: loot já salvo");
            return false;
        }

        var key = $"{workshop->HouseId}:{slot}";
        Config.Subs.TryGetValue(key, out var record);
        // A lista já foi lida neste frame: se o sub saiu de novo, a volta da viagem do relatório é a anterior.
        var returnTime = new[] { (long)selected->ReturnTime, record?.ReturnTime ?? 0, record?.PreviousReturnTime ?? 0 }
            .FirstOrDefault(t => t != 0 && t <= now);
        if (returnTime == 0)
        {
            Diagnostics.Write($"Relatório de {name}: loot novo, mas sem horário de volta conhecido. Não foi salvo.");
            QueueLogUpload();
            return false;
        }

        var voyage = SubmarineLoot.Read(selected, name, returnTime, LookupItem, SectorName);
        if (voyage == null || !LootLedger.Upsert(Config.Voyages, LootLedger.VoyageKey(key, returnTime), voyage))
            return false;

        Diagnostics.Write($"{name}: viagem de {DiagnosticLog.Time(returnTime)} salva pelo relatório da última viagem, {voyage.Items.Count} itens, {LootLedger.Total([voyage]):N0} gil");
        QueueLogUpload();
        return true;
    }

    private void LogReturnChange(string key, string name, long previous, long current, long now)
    {
        if (current > now && previous != 0 && previous <= now)
        {
            if (Config.Voyages.ContainsKey(LootLedger.VoyageKey(key, previous)))
                Diagnostics.Write($"{name}: saiu de novo, volta {DiagnosticLog.Time(current)}");
            else
                Diagnostics.Write($"{name}: saiu de novo, volta {DiagnosticLog.Time(current)}. O loot da viagem de {DiagnosticLog.Time(previous)} ainda não foi lido.");

            QueueLogUpload();
            return;
        }

        Diagnostics.Detail($"{name}: volta mudou para {DiagnosticLog.Time(current)}");
    }

    private void OnVoyageResult(AddonEvent type, AddonArgs args)
    {
        Diagnostics.Write($"Tela de resultado da viagem aberta ({args.AddonName})");
        if (characterEnabled)
            ReadSubmarines(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private static string? SectorName(uint point)
        => DataManager.GetExcelSheet<SubmarineExploration>().GetRowOrDefault(point)?.Destination.ToString();

    // O XP muda a cada leitura de viagem; o resto só quando rank, peças, rota ou desgaste mudam.
    private void LogDetailsChange(string name, SubDetails? previous, SubDetails current)
    {
        var text = $"{name}: rank {current.Rank} ({current.ExpPercent}%), {current.Build}, rota {current.Route} "
            + $"({current.TanksPerVoyage} tanques), peças {current.Condition?.ToString("0.#") ?? "?"}%, reparo {current.RepairKits} kits";
        if (previous == null || previous with { ExpPercent = current.ExpPercent } != current)
        {
            Diagnostics.Write(text);
            QueueLogUpload();
        }
        else
            Diagnostics.Detail(text);
    }

    private void ReadSupplies(long now)
    {
        var record = Config.Supplies;
        var changed = false;
        var emptyPages = new List<int>();
        var chest = Supplies.ReadChest(emptyPages);
        var empty = string.Join(", ", emptyPages);
        if (empty != lastEmptyPages)
        {
            lastEmptyPages = empty;
            if (empty.Length > 0)
                Diagnostics.Detail($"Baú da FC: página(s) {empty} carregada(s) sem itens, ignorada(s)");
        }

        foreach (var (page, count) in chest)
        {
            if (record.ChestPages.TryGetValue(page, out var known) && known.Tanks == count.Tanks && known.Kits == count.Kits)
                continue;

            Diagnostics.Write($"Baú da FC, página {page}: {count.Tanks} tanques, {count.Kits} kits de reparo");
            record.ChestPages[page] = new SupplyPage { Tanks = count.Tanks, Kits = count.Kits };
            changed = true;
        }

        if (Supplies.ReadBags() is { } bags && (bags.Tanks != record.Bags.Tanks || bags.Kits != record.Bags.Kits))
        {
            Diagnostics.Write($"Inventário: {bags.Tanks} tanques, {bags.Kits} kits de reparo");
            record.Bags = new SupplyPage { Tanks = bags.Tanks, Kits = bags.Kits };
            changed = true;
        }

        if (!changed)
            return;

        record.ChangedAt = now;
        SaveConfig();
        Diagnostics.Write($"Estoque total: {record.Tanks} tanques, {record.Kits} kits de reparo (páginas do baú vistas: {string.Join(", ", record.ChestPages.Keys.Order())})");
        QueueLogUpload();
    }
}
