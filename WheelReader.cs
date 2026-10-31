using System.Collections.Generic;
using System.Text.RegularExpressions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FcTimers;

public readonly record struct WheelSlot(int Slot, uint ItemId, uint Percent, int Hours);

// Lê os slots da janela da mesa. Cada slot ocupa 8 valores: item, grau, porcentagem,
// nome, compatibilidade, tempo restante, porcentagem em texto e ícone.
public static unsafe partial class WheelReader
{
    private const int FirstValue = 3;
    private const int ValuesPerSlot = 8;

    public static List<WheelSlot>? Read()
    {
        var agent = AgentModule.Instance()->GetAgentByInternalId(AgentId.AetherialWheel);
        if (agent == null || !agent->IsAgentActive())
            return null;

        var addon = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)agent->GetAddonId());
        if (addon == null || !addon->IsVisible || addon->AtkValuesCount == 0)
            return null;

        var values = addon->AtkValues;
        var count = (int)values[0].UInt;
        if (addon->AtkValuesCount < FirstValue + (count * ValuesPerSlot))
            return null;

        var slots = new List<WheelSlot>();
        for (var slot = 0; slot < count; slot++)
        {
            var offset = FirstValue + (slot * ValuesPerSlot);
            var itemId = values[offset].UInt;
            if (itemId == 0)
                continue;

            var remaining = values[offset + 5].String.ToString();
            var match = HoursRegex().Match(remaining ?? string.Empty);
            if (!match.Success)
                return null;

            slots.Add(new WheelSlot(slot, itemId, values[offset + 2].UInt, int.Parse(match.Value)));
        }

        return slots;
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex HoursRegex();
}
