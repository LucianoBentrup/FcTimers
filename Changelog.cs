using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FcTimers;

// Abre sozinho uma vez depois de cada atualização. O texto fica em inglês.
public static class Changelog
{
    public static readonly (string Version, (string Section, string[] Notes)[] Sections)[] Entries =
    [
        ("0.8.4",
        [
            ("Changed",
            [
                "The diagnostic log records every game message for 30 seconds after a house object menu, to find out how watering an indoor pot is reported.",
            ]),
        ]),
        ("0.8.3",
        [
            ("Fixed",
            [
                "The same garden patch no longer shows up twice when members use different system languages.",
                "Outdoor garden patches are named after the game menu (\"Canteiro 2\" for the 2nd Patch) instead of a blank name.",
                "The general \"Plantação\" entry is hidden once an identified patch is watered after it.",
            ]),
        ]),
        ("0.8.2",
        [
            ("Changed",
            [
                "The diagnostic log is sent automatically about 20 seconds after important actions (collections, submarines leaving, watering, supply and part changes) and when the game closes, instead of every 30 minutes.",
            ]),
        ]),
        ("0.8.1",
        [
            ("Changed",
            [
                "The diagnostic log is sent to the FC service automatically every 30 minutes when it has new entries, in addition to right after missed loot.",
                "Submarine alerts that arrive minutes apart are grouped into a single Discord message.",
            ]),
        ]),
        ("0.8.0",
        [
            ("Added",
            [
                "Ceruleum tank and repair material stock, counted from the FC chest and the inventory, with how many rounds and full repairs it covers.",
                "Submarine rank, experience, build, route and part condition in the main window and the Discord panel.",
                "Discord alerts when a submarine needs repair (below 30%), when stock falls below 2 rounds of tanks or 20 repair materials (with how many voyages are left), and when a voyage unlocks a new sector.",
                "Reminders for a submarine that has been back for over 2 hours without being sent out again, and for an indoor pot left unwatered after the others were watered.",
                "Discord alerts name only indoor and private chamber pots. Outdoor garden patches are watered together, so they are mentioned as the garden.",
                "Market board prices from Universalis: items worth more on the market count at the market price, with a Discord alert for drops worth 100,000 gil or more. The world defaults to the character's home world and can be changed in Settings.",
                "English translation. The language can be changed in Settings.",
            ]),
            ("Changed",
            [
                "The main window is split into tabs: Submarines, Loot, Buffs, Garden and Settings.",
            ]),
        ]),
        ("0.7.4",
        [
            ("Changed",
            [
                "Changelog reorganized by type of change.",
            ]),
        ]),
        ("0.7.3",
        [
            ("Fixed",
            [
                "Watering is assigned to the correct plot when the target is cleared before the confirmation message.",
            ]),
            ("Changed",
            [
                "The Discord panel lists each plot on its own line.",
            ]),
        ]),
        ("0.7.2",
        [
            ("Changed",
            [
                "Watering times are fixed at 24 hours until the next watering and 48 hours until the plant withers, based on the shortest wilt time of any seed.",
            ]),
            ("Removed",
            [
                "Watering time settings from the main window.",
            ]),
        ]),
        ("0.7.1",
        [
            ("Added",
            [
                "\"Enviar log\" button to upload the diagnostic log to the FC service.",
                "Automatic log upload when submarine loot is missed, limited to once every 10 minutes.",
            ]),
            ("Changed",
            [
                "\"Plantas\" renamed to \"Plantação\".",
            ]),
        ]),
        ("0.7.0",
        [
            ("Added",
            [
                "Two-way sync: the main window, server info bar and chat alerts include data reported by other FC members.",
                "Per-plot watering tracking, detected automatically or registered with \"Reguei todos agora\" and /fct regar.",
                "Discord alerts for submarines and plots: 1-hour reminders, ready notifications and wither warnings 3, 2 and 1 hours in advance. Alerts are deleted after 8 hours.",
                "Diagnostic log with a \"Copiar log\" button and an optional detailed mode.",
                "In-game changelog.",
            ]),
            ("Changed",
            [
                "Submarine loot is captured as soon as the voyage result is received, including during AutoRetainer runs.",
                "Loot is grouped by day in Brasília time (UTC-3) on all clients.",
                "The Discord panel no longer lists the day's items. Loot totals are still shown.",
            ]),
        ]),
        ("0.6.1",
        [
            ("Fixed",
            [
                "Duplicate fifth submarine entry.",
            ]),
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
        foreach (var (version, sections) in Changelog.Entries)
        {
            ImGui.TextUnformatted($"Version {version}");
            ImGui.Separator();
            foreach (var (section, notes) in sections)
            {
                ImGui.TextDisabled(section);
                foreach (var note in notes)
                    ImGui.TextWrapped($"- {note}");
            }

            ImGui.Spacing();
        }

        if (ImGui.Button("Close"))
            IsOpen = false;
    }
}
