# FC Companion

[![CI](https://github.com/LucianoBentrup/FcTimers/actions/workflows/ci.yml/badge.svg)](https://github.com/LucianoBentrup/FcTimers/actions/workflows/ci.yml)

A Dalamud plugin for FFXIV that keeps track of your Free Company: submersibles, loot, workshop supplies, gardening and buffs. A Cloudflare service merges what each member has read and keeps a shared Discord panel, with alerts and commands.

The plugin **only reads what the game already shows**. It doesn't automate anything, doesn't send anything to the game servers and doesn't open windows on its own. It is a third-party tool: using it is your choice, and the game's terms apply to it.

## Features

**Submersibles**
- Return time, rank, XP, parts (build), route and wear for each sub.
- Loot from every voyage, saved the moment the result arrives (fast enough for AutoRetainer). If a capture fails, opening the sub at the workshop recovers the loot.
- Each item counts at the higher of its NPC sale price and its market value (average of recent sales on Universalis, on your character's world or one you pick).

**Airships**
- Return time, rank and loot of each airship, read at the workshop like the subs, with their own alert.

**Supplies**
- Ceruleum tanks and repair kits in the FC chest and your inventory, plus how many voyages and sub repairs they cover.
- A forecast of the day they run out, based on the last days of use, with an alert 5 days before.

**FC house**
- Days since any member last entered the FC house, with alerts at 30, 40 and 44 days, before the game demolishes it at 45. The house is recognized from the workshop, with nothing to set up.

**Gardening**
- Watering of each patch or pot, detected from the game message, with when to water again and when the plant dies.
- **Fields** section: target the patch or pot in game, click **+** and give it a name. Names and removals apply to every member.

**Buffs**
- Wheel Stand (when each buff is ready) and active FC buffs.

**Discord** (with the service)
- A single panel, always edited in place, with everything above and today's loot.
- Alerts that ping the FC role: 1-hour reminder, sub ready, sub forgotten, watering, plant about to die, repair, low supplies, expensive market item, new sector and Wheel Stand buff ready. Alerts that happen together go out as one message, and all of them are deleted after 8 hours.
- FC Companion app commands: `/subs`, `/estoque`, `/loot [hoje|semana|mês]` and `/lucro` (last 4 weeks and the best routes) for everyone; `/painel`, `/avisos limpar`, `/resumo ligar|desligar` and `/log` for server managers.
- A weekly summary every Monday at 9:00 (Brasília): last week's gil, the best sub, supplies used and how long subs sat ready.
- The panel footer warns when Dalamud hasn't been updated yet for a new game patch.

**In game**
- Tabbed window (`/fct`): Overview, Submersibles (and airships), Loot (with weekly profit and route ranking), Buffs and Gardening, plus a settings window, with tables and colors (green ready, yellow soon, red urgent).
- Notifications (toast, chat or sound) and a summary in the server info bar. Text in Portuguese and English.

## Install
1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, add the link below, click **+** and save:
   ```
   https://raw.githubusercontent.com/LucianoBentrup/FcTimers/main/repo.json
   ```
3. Type `/xlplugins` and install **FC Companion**.

## Setup
1. Type `/fct`, open the settings (cog icon) and tick your character. Only ticked characters read and send data.
2. For the Discord panel, still in settings, paste the service address and the FC key and tick **Send and receive data**. The address and key are given to you by your FC.
3. To get data showing up: open the sub list at the workshop, the chest tab where the tanks are and the Wheel Stand. Watering is recorded on its own when you tend a plant.

## Privacy
With sending on, the plugin sends to the FC's service: sub and airship times, rank, parts and loot, supplies, field watering, buffs, when a character entered the FC house and the plugin's diagnostic log. Only active characters send the log. The log contains character names and game system messages, **never** player chat or the FC key. The service keeps the current state, 120 days of loot, 14 days of supply counts and the latest log for each character. With sending off, nothing leaves your PC.

## Layout
| Folder / file | What it is |
|---|---|
| Root (`*.cs`) | The plugin. `Plugin.cs` wires everything up; `FcView.cs` merges this PC's data with the service's; `MainWindow.cs` is the main window. |
| `service/` | Cloudflare service (Worker + Durable Object): receives the data, builds the panel, sends the alerts and answers Discord commands. |
| `tests/` | Tests for the plugin logic. |
| `tools/` | Scripts: manual publish, resend the panel, regroup alerts, download logs, register the commands and the test panel. The ones that need a key ask for it in the terminal and don't store it. |
| `REGRAS.md` | Rules for the panel, alerts and sync. |
| `CHECKLIST.md`, `PLANO-*.md` | What's done, what's left and each version's plan. |

## Roadmap
- **0.11:** service for multiple FCs.
- **0.12:** the Discord app becomes a bot, posts the panel instead of the webhook and gets easy sign-up with a code.
- **0.13:** English panel and a privacy page.
- **1.0:** public release, distributed through this repository.
