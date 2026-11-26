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

## What to test on the PC after installing Mark III

1. **Boot:** power on into Gaming Mode. The sequence and sound play; press A once on a
   second boot to check skipping; Big Picture appears normally. In `agent.log`
   (`%APPDATA%\GamingMode\agent.log`) look for *Boot sequence finished/skipped*.
2. **Sleep and wake:** sleep from Big Picture, wake with the controller: TV on, HDMI 2,
   short sequence. Then put the TV on another input or app, sleep Core: the TV must stay on.
   Wake Core again while the TV shows that app: the TV must not switch to Core.
3. **Shutdown** from Big Picture with the TV on HDMI 2: the TV turns off.
4. **Plugins check:** with Decky working, `agent.log` shows *Decky heartbeat received* and
   *Decky health: Healthy*. Open Core Repair > Restart plugins: it answers *Fixed*. To see the
   notice itself you need plugins that do not load (for example after a Steam update);
   ending *PluginLoader* in Task Manager is not a reliable test, because Core's Decky
   watchdog starts it again within seconds.
5. **Core Repair tile:** it is in the Steam library and opens with the controller.
6. **Steam update:** Core Repair > Update Steam now. Steam closes, a backup appears under
   `%LOCALAPPDATA%\Playhub\backups\SteamClient`, Steam updates and comes back, and
   `steam.cfg` is back in the Steam folder. Check `Steam\logs\bootstrap_log.txt` after a new
   Steam release to confirm the hold still blocks the self-update on the 64-bit client.
7. **Anti-cheat:** launch Call of Duty: no notice or sequence appears over it.
