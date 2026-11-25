# Changelog

All notable changes to FC Companion are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

This file is also what the plugin shows in its "What's new" window. An entry that
starts with a bold title (`**Title:** text`) is shown there as a highlight card.

## [Unreleased]

### Added
- **Timeline:** the Overview opens with the next 24 hours on one bar: submarines, airships, watering, buffs and the house deadline. Hover a marker to see the exact time.
- **Submarine cards:** each submarine gets a card with its rank, return time, voyage progress and the four parts with their game icons and wear.
- **Item icons:** loot shows each item's icon from the game, and the Overview highlights the best item of the day.
- **New look:** the windows use the FC Companion colors (the Maré theme). Your Dalamud style is not changed.
- A "What's new" button in Settings to open this list again.

### Changed
- The command is now `/fcc`. `/fct` still works for this version but no longer shows in the help.
- The "What's new" window shows the highlights of each version as cards, grouped by type of change.
- The windows do less work per frame, so they stay light with many days of loot.

### Fixed
- A character from another FC (a submarine alt, for example) no longer sends its submarines, loot or supplies to your FC's panel. It is detected by its FC tag or by a workshop in a house that is not your FC house.

## [0.10.0] - 2026-11-23

### Added
- **Airships:** return time, rank and loot of each voyage, read from the workshop like the submarines. They count in the daily loot and have their own alert.
- **FC house:** every visit to the FC house is recorded. The Overview shows the days since anyone last went in, and alerts go out at 30, 40 and 44 days, before the game demolishes it at 45. A button on the card marks the house you are in.
- **Supply forecast:** the date your ceruleum tanks and repair materials run out, based on recent use. The low supply alert now comes 5 days ahead.
- **Profit:** the Loot tab shows the last 4 weeks and how much each route makes per voyage and per tank.
- Discord: `/lucro`, `/loot mes` and a weekly summary every Monday at 9:00 (Brasília), turned on or off with `/resumo`.

## [0.9.0] - 2026-11-22

### Added
- **Discord commands:** `/painel`, `/avisos limpar`, `/loot`, `/estoque`, `/subs` and `/log`, answered by the FC Companion app.
- **Fields:** in the Garden tab, target a garden patch or pot and press + to add it with a name. Names and removals are shared with the other members.

### Changed
- FC Timers is now called FC Companion.
- **Overview:** a new first tab with cards for submarines, today's loot, garden, supplies and buffs.
- Submarines, loot, buffs and the garden are shown in tables, with green for ready, yellow for soon and red for urgent.
- Settings have their own window, opened from the cog, and a sync status dot is always visible.
- Welcome steps on first use. Alerts are Dalamud notifications, with an optional chat line and sound for each type.
- Hovering a remaining time shows the exact local time.
- Only enabled characters send the diagnostic log, including the one sent when the game closes.

## [0.8.6] - 2026-11-22

### Changed
- Repair materials are counted per repair of all submarines together, since they are always repaired as a group.
- Submarines that need repair are reported in one Discord message instead of one per submarine.

## [0.8.5] - 2026-11-03

### Fixed
- Garden patch names no longer carry the game's icon character, which showed as a box on Discord.
- Opening a garden patch menu renames an automatically named patch to its number from the game ("Canteiro 2").

## [0.8.4] - 2026-11-03

### Changed
- The diagnostic log records game messages for 30 seconds after a house object menu, to learn how watering an indoor pot is reported.

## [0.8.3] - 2026-11-03

### Fixed
- The same garden patch no longer shows up twice when members use different game languages.
- Outdoor garden patches are named after the game menu ("Canteiro 2" for the 2nd Patch) instead of a blank name.
- The general "Plantação" entry is hidden once an identified patch is watered after it.

## [0.8.2] - 2026-11-03

### Changed
- The diagnostic log is sent about 20 seconds after important actions (collections, departures, watering, supply and part changes) and when the game closes, instead of every 30 minutes.

## [0.8.1] - 2026-11-03

### Changed
- The diagnostic log is sent every 30 minutes when it has new entries, besides right after missed loot.
- Submarine alerts that arrive minutes apart are grouped into one Discord message.

## [0.8.0] - 2026-11-02

### Added
- **Supplies:** ceruleum tanks and repair materials, counted from the FC chest and your inventory, with how many rounds and full repairs they cover.
- **Submarine details:** rank, experience, build, route and part condition, in the window and on the Discord panel.
- **Market prices:** items worth more on the market board count at the Universalis price, with a Discord alert for drops worth 100,000 gil or more. The world defaults to your home world and can be changed in Settings.
- Discord alerts when a submarine needs repair (below 30%), when supplies drop below 2 rounds of tanks or 20 repair materials, and when a voyage unlocks a new sector.
- Reminders for a submarine back for over 2 hours without leaving again, and for an indoor pot left dry after the others were watered.
- Discord alerts name only indoor and private chamber pots. Outdoor patches are watered together and show as the garden.
- **English:** the plugin is translated, and the language can be changed in Settings.

### Changed
- The main window is split into tabs: Submarines, Loot, Buffs, Garden and Settings.

## [0.7.4] - 2026-11-02

### Changed
- The changelog is grouped by type of change.

## [0.7.3] - 2026-11-02

### Fixed
- Watering goes to the right plot when the target is cleared before the confirmation message.

### Changed
- The Discord panel lists each plot on its own line.

## [0.7.2] - 2026-11-02

### Changed
- Watering times are fixed at 24 hours until the next watering and 48 hours until the plant withers, the shortest of any seed.

### Removed
- Watering time settings from the main window.

## [0.7.1] - 2026-11-02

### Added
- "Enviar log" button to upload the diagnostic log to the FC service.
- The log is uploaded on its own when submarine loot is missed, at most once every 10 minutes.

### Changed
- "Plantas" renamed to "Plantação".

## [0.7.0] - 2026-11-02

### Added
- **Two-way sync:** the window, the server info bar and chat alerts include what other FC members read.
- **Watering per plot:** detected on its own, or recorded with "Reguei todos agora" and `/fct regar`.
- Discord alerts for submarines and plots: 1 hour before, when ready, and wither warnings 3, 2 and 1 hours ahead. Alerts are deleted after 8 hours.
- Diagnostic log with a "Copiar log" button and an optional detailed mode.
- This changelog, inside the game.

### Changed
- Submarine loot is captured as soon as the voyage result arrives, including during AutoRetainer runs.
- Loot is grouped by day in Brasília time (UTC-3) on every PC.
- The Discord panel no longer lists the day's items. Totals are still shown.

## [0.6.1]

### Fixed
- The submarine list no longer shows a duplicate fifth entry.

[Unreleased]: https://github.com/LucianoBentrup/FcTimers/compare/v0.10.0...HEAD
[0.10.0]: https://github.com/LucianoBentrup/FcTimers/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/LucianoBentrup/FcTimers/releases/tag/v0.9.0
