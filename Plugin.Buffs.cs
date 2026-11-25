using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace FcTimers;

public sealed partial class Plugin
{
    private unsafe void ReadWheels(long now)
    {
        var manager = HousingManager.Instance();
        var slots = WheelReader.Read();
        if (manager == null || slots == null)
            return;

        localReadings.Wheels = now;
        Sync.ReportBuffs(slots.Select(s => new BuffReading(
            s.Slot,
            s.ItemId,
            LookupItem(s.ItemId)?.Name ?? $"Buff {s.Slot + 1}",
            s.Percent >= 100 ? 0 : s.Hours)).ToList());

        var prefix = $"{manager->GetCurrentIndoorHouseId()}:";
        var changed = false;

        foreach (var key in Config.Wheels.Keys.Where(k => k.StartsWith(prefix)).ToList())
        {
            if (slots.All(s => $"{prefix}{s.Slot}" != key))
                changed |= Config.Wheels.Remove(key);
        }

        foreach (var slot in slots)
        {
            var key = $"{prefix}{slot.Slot}";
            var hours = slot.Percent >= 100 ? 0 : slot.Hours;

            // A janela só informa horas inteiras, então o horário só muda quando esse número muda.
            if (Config.Wheels.TryGetValue(key, out var record) && record.ItemId == slot.ItemId && record.Hours == hours)
                continue;

            Config.Wheels[key] = new WheelRecord
            {
                ItemId = slot.ItemId,
                Name = LookupItem(slot.ItemId)?.Name ?? $"Buff {slot.Slot + 1}",
                Hours = hours,
                ReadyTime = now + (hours * 3600L),
            };
            changed = true;
        }

        if (changed)
            SaveConfig();
    }

    private void ReadActiveBuffs(long now)
    {
        var player = ObjectTable.LocalPlayer;

        // Em duty ou trocando de área, os status podem sumir sem o buff ter acabado.
        if (player == null)
            secondsSinceLogin = 0;

        if (player == null || Condition[ConditionFlag.BoundByDuty] || Condition[ConditionFlag.BetweenAreas] || ClientState.IsPvP)
        {
            candidateSeconds = 0;
            return;
        }

        secondsSinceLogin++;

        var active = new List<ActiveStatus>();
        foreach (var status in player.StatusList)
        {
            if (companyStatuses.TryGetValue(status.StatusId, out var name))
                active.Add(new ActiveStatus(name, status.Param));
        }
        active.Sort((a, b) => string.CompareOrdinal(a.BaseName, b.BaseName));

        var signature = string.Join('|', active);
        if (signature != candidateBuffs)
        {
            candidateBuffs = signature;
            candidateSeconds = 0;
        }

        if (++candidateSeconds < ActiveBuffStableSeconds)
            return;

        var matched = ActiveBuffMatcher.Match(active, companyActions, ReadCompanySlots());
        var readings = ActiveBuffMatcher.WithKnownTimes(matched, Config.ActiveFcBuffs, now);

        // Logo depois de logar, um status que aparece é um buff antigo carregando, não um buff novo.
        if (secondsSinceLogin >= ActiveBuffSettleSeconds)
            readings = ActiveBuffMatcher.WithEstimates(readings, active, seenBuffs, ActiveBuffStableSeconds);
        seenBuffs = active.Select(a => a.BaseName).ToHashSet();

        localReadings.ActiveBuffs = now;
        Sync.ReportActiveBuffs(readings);

        var updated = ActiveBuffMatcher.Update(Config.ActiveFcBuffs, readings, now);
        if (updated == null)
            return;

        Diagnostics.Detail($"Buffs ativos da FC: {string.Join(" | ", readings)}");
        Config.ActiveFcBuffs = updated;
        SaveConfig();
    }

    // Cada slot ocupa 3 números a partir da posição 2: ação da FC, ícone e segundos restantes.
    private static unsafe List<CompanySlot> ReadCompanySlots()
    {
        var slots = new List<CompanySlot>();
        var numbers = AtkStage.Instance()->GetNumberArrayData(NumberArrayType.FreeCompanyAction);
        if (numbers == null || numbers->Size < 2)
            return slots;

        var sheet = DataManager.GetExcelSheet<CompanyAction>();
        for (var i = 0; i < numbers->IntArray[1] && i < MaxCompanySlots; i++)
        {
            var first = 2 + (i * 3);
            if (first + 2 >= numbers->Size)
                break;

            var action = sheet.GetRowOrDefault((uint)numbers->IntArray[first]);
            if (action != null)
                slots.Add(new CompanySlot(action.Value.Name.ToString(), numbers->IntArray[first + 2]));
        }

        return slots;
    }
}
