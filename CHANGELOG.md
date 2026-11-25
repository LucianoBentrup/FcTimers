# Changelog

All notable changes to FC Companion are listed here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The plugin's "What's new" window reads this file (and `CHANGELOG.pt.md` when the plugin is in
Portuguese). Rules for every entry:

- One line, `- **Label:** short text`, about 12 words at most.
- Sections in this order: Added, Changed, Fixed, Removed.
- Same terms as the plugin: submarine, airship, field, watering, supplies, repair materials, FC house.
- A big update gets `<!-- big -->` after its version, and its entries show as cards.

## [0.10.1] - 2026-11-25 <!-- big -->

### Added
- **Timeline:** the next 24 hours, 3 days or 7 days on one bar.
- **Submarine cards:** rank, return, voyage progress, parts and wear.
- **Item icons:** game icons in the loot, plus the day's best item.
- **New look:** FC Companion colors; your Dalamud style stays the same.
- **What's new button:** in Settings, to open this list again.
- **Ko-fi button:** in the window, in Settings and here.
- **FC code:** link the plugin to your FC with a short code.
- **Discord account:** link it with a 6-digit code for events.
- **Join requests:** the FC code sends a request; the owner or an admin accepts.
- **FC tab:** members and requests for everyone; owner and admins accept and kick.

### Changed
- **One command:** `/fcc` in the game and on Discord.
- **Discord menu:** buttons replace the old Portuguese commands.
- **What's new:** a short list by type; cards only for big updates.
- **Supplies:** one fact per line.
- **FC house:** days left shown in a red bar.
- **Performance:** less work per frame with a long loot history.

### Fixed
- **Alts in another FC:** no longer send data to your FC panel.
- **Privacy:** the log no longer keeps any chat.

## [0.10.0] - 2026-11-23

### Added
- **Airships:** return time, rank, loot and their own alert.
- **FC house:** days since the last visit, alerts at 30, 40 and 44.
- **Supply forecast:** the date your tanks and materials run out.
- **Profit:** the last 4 weeks and gil per route.
- **Weekly summary:** a Discord post every Monday morning.

## [0.9.0] - 2026-11-22

### Added
- **Discord commands:** panel, alerts, loot, supplies, submarines and log.
- **Fields:** add a patch or pot by targeting it.

### Changed
- **Name:** FC Timers is now FC Companion.
- **Overview:** a new first tab with one card per subject.
- **Colors:** green for ready, yellow for soon, red for urgent.
- **Settings:** their own window, opened from the cog.
- **Welcome:** first-use steps.
- **Alerts:** Dalamud notifications, chat line and sound per type.
- **Times:** hover a countdown to see the exact time.
- **Log:** only enabled characters send it.

## [0.8.6] - 2026-11-22

### Changed
- **Repair materials:** counted per repair of all submarines.
- **Repair alert:** one Discord message for every submarine.

## [0.8.5] - 2026-11-03

### Fixed
- **Field names:** no stray game icon on Discord.
- **Automatic names:** take the patch number from the game.

## [0.8.4] - 2026-11-03

### Changed
- **Log:** records game messages after house object menus.

## [0.8.3] - 2026-11-03

### Fixed
- **Fields:** no duplicates across game languages.
- **Outdoor patches:** named after their number in the menu.
- **Garden entry:** hidden once a named field is watered.

## [0.8.2] - 2026-11-03

### Changed
- **Log:** sent right after important actions and on exit.

## [0.8.1] - 2026-11-03

### Changed
- **Log:** sent every 30 minutes when there is something new.
- **Submarine alerts:** alerts minutes apart become one message.

## [0.8.0] - 2026-11-02

### Added
- **Supplies:** tanks and repair materials, with rounds and repairs left.
- **Submarine details:** rank, build, route and part wear.
- **Market prices:** items count at the Universalis price when higher.
- **Discord alerts:** repairs, low supplies and new sectors.
- **Reminders:** idle submarines and dry indoor pots.
- **English:** language choice in Settings.

### Changed
- **Main window:** split into tabs.

## [0.7.4] - 2026-11-02

### Changed
- **Changelog:** grouped by type of change.

## [0.7.3] - 2026-11-02

### Changed
- **Discord panel:** one line per field.

### Fixed
- **Watering:** goes to the right field after the target is cleared.

## [0.7.2] - 2026-11-02

### Changed
- **Watering times:** 24 hours to water, 48 hours to wither.

### Removed
- **Watering settings:** gone from the main window.

## [0.7.1] - 2026-11-02

### Added
- **Send log:** uploads the diagnostic log to the FC service.
- **Automatic log:** sent when submarine loot is missed.

### Changed
- **Garden:** "Plants" is now called "Garden".

## [0.7.0] - 2026-11-02

### Added
- **Two-way sync:** see what other members read.
- **Watering per field:** detected on its own, or marked with a button.
- **Discord alerts:** submarines and fields, with reminders.
- **Diagnostic log:** a copy button and a detailed mode.
- **What's new:** this list, inside the game.

### Changed
- **Loot capture:** right when the voyage result arrives.
- **Loot days:** the same day on every PC.
- **Discord panel:** daily totals without the item list.

## [0.6.1]

### Fixed
- **Submarine list:** no duplicate fifth entry.

[0.10.1]: https://github.com/LucianoBentrup/FcTimers/compare/v0.10.0...v0.10.1
[0.10.0]: https://github.com/LucianoBentrup/FcTimers/compare/v0.9.0...v0.10.0
[0.9.0]: https://github.com/LucianoBentrup/FcTimers/releases/tag/v0.9.0
