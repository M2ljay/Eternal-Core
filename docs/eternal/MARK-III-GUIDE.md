# Core Mark III: user guide

Mark III (3.3.0) makes Core quieter and sturdier from the couch. Everything below works
with a controller only, and nothing in it can stop Gaming Mode or Steam from starting.

## Boot sequence

At power-on and when Core wakes from sleep, the Eternal logo appears among faint stars,
then the stars rush outward and Core's appears on black, with the arcade boot sound, before Big Picture (about 7 seconds). **Any controller
button skips it.** It plays while Steam is starting underneath, so it costs no time.

- On wake it does not play if a game is open, and never while anti-cheat safe mode is
  engaged.
- Turn it off, or just its sound: Core > Gaming Mode > *Sequenza di avvio* /
  *Boot sequence*. It also follows the startup splash switch.

## Quiet Windows

Core > Gaming Mode > **Quiet Windows** turns off the Windows interruptions that can reach
the TV. **Apply** changes your user settings at once and asks Windows for administrator
permission once for the PC settings. Every value is backed up first; **Undo changes** puts
each one back exactly as it was (a value that did not exist is removed again).

- Your user: no "Let's finish setting up" screens, no tips or ads, no "program has stopped
  working" windows, no Sticky/Filter/Toggle Keys pop-ups, and voice calls (an Xbox party,
  Discord) no longer lower the volume of games and music.
- The PC (admin): no update restarts while you are signed in (active hours 11:00 to 5:00),
  Windows Update leaves the graphics driver alone, no night-time maintenance wake-ups, no
  password on wake, no Edge first-run or default-browser prompts.
- **Hide Windows notifications in Gaming Mode** (on by default): Windows banners are off
  while Gaming Mode runs and come back for the desktop. Steam and Decky notifications are
  unaffected.

**Update night.** With restarts held, updates need an outlet: when Windows has a restart
pending, or a month has passed, Quick Access > Core shows "Windows has updates waiting.
Restart tonight at 5:00?". One press schedules it; the agent restarts into Gaming Mode at
5:00, never during a game (it retries until 7:00, then asks again). Cancel from the same
card.

## Core Health

Quick Access > Core > **Health** shows in one place whether the home server's nightly backup,
its drive and RomM, Windows updates, the TV, the PC's disk and Core itself are fine, and Core
gives one quiet notice a day for serious server problems. Setup at the server and on the PC:
[CORE-HEALTH.md](CORE-HEALTH.md).

## Voice: microphone mute and Xbox party

Quick Access > Core > **Voice** has a big button that mutes your microphone (both of Windows'
default microphones when they differ, each restored exactly afterwards), with a crimson mark
in Steam's top bar while muted and a Ctrl+Alt+M shortcut you can bind to a Steam chord. **Xbox
party** opens the Xbox Game Bar over Big Picture, and Core brings Big Picture back when the Game
Bar closes. Details, what Core cannot do (party invites) and a checklist for the TV:
[CORE-VOICE.md](CORE-VOICE.md).

## Screensaver: Core Starfield

Steam's screensaver list offers **Core Starfield** (it replaces Playhub Circles; pick it once
in Steam > Settings > Display if Circles was selected). The Core mark bounces around the
screen like the old DVD logo. Its ring fills to show how close each bounce came to a
corner; a true corner fills it, flashes the mark and rushes the stars. When the minute
changes, stars gather into the time, hold a few seconds and scatter. Nothing stays lit in
one place, so it is safe on an OLED.

## TV control (LG webOS, for example the C9)

When Core starts or wakes into Gaming Mode and the TV is **off**, Core turns it on and
switches it to Core's HDMI. A TV that is already on is in use, so Core leaves it on whatever
it shows. When Core sleeps or shuts down, it turns the TV off **only if the TV is showing
Core**. If someone is watching something else, the TV stays as it is either way.

### Once, on the TV (with the TV remote)

1. Settings > General > **Mobile TV On > Turn on via Wi-Fi**: On (needed even on a cable).
2. Settings > General > **Quick Start+**: On.
3. Network: **LG Connect Apps** on, if your firmware shows it.
4. Give the TV a fixed address: a DHCP reservation on your router.

### Once, in Core on the PC

1. Core > Gaming Mode > **LG webOS**: switch on *TV control*.
2. Press **Find the TV** (or type its IP address). Pick Core's input; HDMI 2 is the default.
3. Press **Pair**. The TV shows a pairing request: accept it with the remote. Core fills in
   the MAC address if it was empty.
4. Try **Turn Off**, then **Turn On Now** and **Switch to HDMI**.

The pairing key is stored encrypted for your Windows user and is never written to a log.
If the TV is reset or its certificate changes, press **Pair** again.

A 3 a.m. Windows maintenance wake does not turn the TV on: Core reacts only to a wake you
caused (controller, keyboard or power button).

## Steam updates: held until you choose

*Hold Steam updates* (on the Steam updates card) keeps Steam on its current version for
at most 30 days. You can update whenever you like:

- From the couch: open **Core Repair** in your library and choose **Update Steam now**.
- On the PC: Core > Settings > Steam updates > **Update Steam now**.

Updating closes Steam, saves a copy of it first (in the same place as *Backup della
versione di Steam*), lets Steam update, then checks that Big Picture's plugins came back.
If they did not, Core restarts them once; if they still do not load, a notice tells you
to open Core Repair, which offers **Roll back Steam** first.

After 30 days Core says so once and updates at the next quiet moment: in Gaming Mode,
with no game open for 10 minutes. It never updates during a game.

The hold uses Steam's `steam.cfg`, which Valve does not document. If a Steam version
ignores it and updates anyway, nothing breaks: Core starts a new hold from the new version
and the plugin check below tells you if anything went wrong.

## When Big Picture's plugins do not load

After a Steam update, Decky's plugins sometimes do not appear. Steam, your games and Core
keep working. About 90 seconds after Big Picture appears, a short notice at the bottom of
the screen says so (never during a game).

Open **Core Repair** from your Steam library (the tile is added automatically the first
time Core's plugin runs). Try the steps in order; each says *Fixed* or *Still not working*:

1. **Restart plugins**
2. **Turn Steam's plugin access back on** (restarts Steam)
3. **Update Decky**
4. **Roll back Steam** (to the copy saved before the last update)
5. **Update Steam now**
6. **Continue without plugins**: no more notices until the next Steam version.

Steps that restart Steam close the Core Repair window first; the result appears as a
notice over Big Picture. Controls: D-pad or stick to move, **A** to choose, **B** to close
(or arrow keys, Enter and Esc). On the PC: Core > Gaming Mode > *Open Core Repair*.

## Waking Core with the Steam Controller (2026 model, Puck receiver)

No setting in Core is needed; this is Windows and BIOS setup, done once with a mouse.

1. **Sleep type.** In an admin terminal run `powercfg /a`. An AMD desktop normally shows
   *Standby (S3)*.
2. **BIOS/UEFI** (names vary by board): enable *USB wake* / *Wake by USB* / *Resume by USB
   device* / *Power On By USB*. To also wake from full shutdown, disable *ErP*, *EuP* or
   *Deep Sleep*, which cut USB power when the PC is off.
3. **Port.** Plug the Puck into a **rear motherboard USB port**, not a hub or front port.
4. **Device Manager.** Find the Puck's entries (under *Human Interface Devices* and
   *Universal Serial Bus controllers*; Valve, USB ID `28de:1304`). On each one's *Power
   Management* tab, tick **Allow this device to wake the computer**. Leave *Only allow a
   magic packet* unticked.
   - From an admin terminal instead: `powercfg /devicequery wake_programmable` lists the
     candidates, `powercfg /deviceenablewake "<name>"` arms one, and
     `powercfg /devicequery wake_armed` confirms.
5. **Test.** Sleep from Big Picture, press the Steam button, then run `powercfg /lastwake`
   to see what woke the PC.

Check once that resting the controller on the Puck to charge does not wake Core (and the
TV with it). Turn off *Allow wake timers* in the power plan if Core wakes up by itself at
night.

## Test day: one pass at the TV

One checklist for the combined test build (Mark III, Core Health, Voice and the Core theme), in the order you would
do it. The per-feature checklists in [CORE-HEALTH.md](CORE-HEALTH.md#at-the-tv) and
[CORE-VOICE.md](CORE-VOICE.md#on-the-tv-checklist) are folded in here. Logs: `%APPDATA%\GamingMode\agent.log`.

### Before, at the desk (mouse and keyboard)

1. **Install** the test build's `Core-Setup.exe` over the current Core. Settings and data are kept.
2. **Core Health, server side** (once): [CORE-HEALTH.md](CORE-HEALTH.md#at-the-server-once). Then on the PC: Core >
   Gaming Mode > **Core Health**, type the server name, leave **Health alerts** on, press **Check now**: "All good" or
   "Things to check: N".
3. **Quiet Windows:** Core > Gaming Mode > Quiet Windows > **Apply**. The line *Voice calls do not lower the volume of
   games and music* is ticked afterwards.
4. **TV control** (once, if not paired yet): the TV settings and **Pair** steps in [TV control](#tv-control-lg-webos-for-example-the-c9).
5. **Theme:** copy `docs/eternal/brand/css-theme/Core` to `%USERPROFILE%\homebrew\themes\Core`. Optional:
   `python -I check_classes.py %USERPROFILE%\homebrew\themes\css_translations.json Core` reports no *NOT FOUND*.
6. If your call headset differs from your default microphone, open Windows sound settings > Input once and note both
   devices (for the mute check below).
7. Shut the PC down and turn the TV off.

### At the TV

1. **Boot:** power on. The TV turns on and switches to Core's HDMI; the boot sequence and sound play; Big Picture
   appears normally. `agent.log`: *Boot sequence finished*. On a second boot press **A** during the sequence: it skips
   (*Boot sequence skipped*).
2. **Plugins:** `agent.log` shows *Decky heartbeat received* and *Decky health: Healthy*. The **Core Repair** tile is in
   the Steam library and opens with the controller; **Restart plugins** answers *Fixed*. (Ending *PluginLoader* in
   Task Manager is not a valid test: the watchdog restarts it within seconds.)
3. **Theme:** Decky > CSS Loader: switch **Core** on (all four screen options on). Check, and switch off any option that
   looks wrong:
   - Steam menu: Core wordmark in line with the icons, black panel with drifting stars, gunmetal item with a crimson line.
   - Quick Access: black tab column, gunmetal selected tab with a crimson bar.
   - Home: black page, hero art fading into black, focused game with a crimson ring and a small lift.
   - Library: black grid, flat steel tabs with a crimson underline on the open one, focused cover with the crimson ring.
   - Game page: hero art fading into black, solid crimson Play (white ring when focused), translucent gunmetal play bar.
   - Context menu (press the menu button on a game): black panel, gunmetal selected item with a crimson underline.
   - A notification (for example a screenshot, or a download finishing): black with a crimson left edge, white title.
4. **Health:** Quick Access > **Core** > **Health**. Check the top line, move through the rows, press **A** on any
   crimson row for details; a row with an alert offers **Dismiss alert: <row>** below the list.
5. **Voice, microphone:** Quick Access > Core: **Voice** is at the top.
   - Press the mic button: it turns crimson, says *Microphone muted*, and a crimson mic mark appears before the clock
     in the top bar. Health's *Microphone* row says *Muted*. Press again: mark gone, row says *Live*.
   - With a separate call headset: both devices are muted, and both come back as they were.
   - While muted, plug in or switch to another headset: it is muted too within a second or two.
   - Bind a Steam chord to **Ctrl+Alt+M** and try it in Big Picture and in a game.
6. **Voice, Xbox party:** press **Xbox party**: Quick Access closes and the Game Bar opens over Big Picture. Open
   *Xbox Social* if needed and start or join a party (or a Discord call). Check that nobody hears you while muted, and
   that game volume no longer drops during the call. Close the Game Bar with **B** (or Esc), and once with the Steam
   button: Big Picture is in front and the controller drives it (`agent.log`: *Voice: Game Bar closed*). If Win+G does
   not open the Game Bar over Big Picture, note what happens and the *Voice:* lines in `agent.log`.
7. **Sleep and wake:** mute the mic, sleep from Big Picture, wake with the controller: TV on, Core's HDMI, short
   sequence, mic still muted with the mark. Then put the TV on another input or app and sleep Core: the TV stays on.
   Wake Core while the TV shows that app: the TV does not switch to Core.
8. **Steam update:** Core Repair > **Update Steam now**. Steam closes, a backup appears under
   `%LOCALAPPDATA%\Playhub\backups\SteamClient`, Steam updates and comes back, the plugins load, and `steam.cfg` is
   back in the Steam folder. After a new Steam release, `Steam\logs\bootstrap_log.txt` shows whether the hold still
   blocks the self-update on the 64-bit client.
9. **Anti-cheat:** launch Call of Duty. No notice or boot sequence appears over it; in Quick Access > Core the party
   button says it is not available and the mic button still works.
10. **Shutdown** from Big Picture with the TV on Core's HDMI: the TV turns off.
