using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FcTimers;

// Abre sozinho uma vez depois de cada atualização. O texto fica em inglês.
public static class Changelog
{
    public static readonly (string Version, string[] Notes)[] Entries =
    [
        ("0.7.2",
        [
            "Watering times are now fixed: water again 24 hours after the last watering, and the plant is counted as dying 48 hours after it. The setting in the window was removed.",
        ]),
        ("0.7.1",
        [
            "New \"Enviar log\" button: sends the diagnostic log to the FC service, so it can be checked without copying and pasting.",
            "If a submarine's loot is missed, the log is sent automatically (at most once every 10 minutes).",
            "\"Plantas\" is now called \"Plantação\".",
        ]),
        ("0.7.0",
        [
            "Submarine loot is now captured the moment the voyage result arrives, fast enough for AutoRetainer.",
            "Two-way sync: every member's /fct window now shows what the others read, not only what this PC saw.",
            "Plants: each plot is tracked on its own. Watering is picked up automatically, or use \"Reguei todos agora\" (/fct regar).",
            "Discord: 1-hour reminders for submarines and plots, a ping when they're ready, and warnings 3, 2 and 1 hours before a plant dies. Every alert is removed after 8 hours.",
            "The Discord panel no longer lists today's items, to keep the message light. The loot totals are still there.",
            "Loot days now follow the panel's time zone (Brasília) on every PC.",
            "New diagnostic log with a Copy log button, to help track down missing loot.",
        ]),
    ];
}

public sealed class ChangelogWindow : Window
{
    public ChangelogWindow() : base("FC Timers - What's new###FcTimersChangelog")
    {
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 200) };
    }

    public override void Draw()
    {
        foreach (var (version, notes) in Changelog.Entries)
        {
            ImGui.TextUnformatted($"Version {version}");
            ImGui.Separator();
            foreach (var note in notes)
                ImGui.TextWrapped($"- {note}");
            ImGui.Spacing();
        }

        if (ImGui.Button("Close"))
            IsOpen = false;
    }
}
