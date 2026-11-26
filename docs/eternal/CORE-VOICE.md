# Core Voice

Quick Access > Core has a **Voice** section at the top: a big microphone button, a **Calls mic** picker with **Use for
everything**, the **Mute** scope, an **Xbox party** button and a **Discord** row. Core also shows a small crimson "microphone muted" mark in Steam's top bar while the microphone is
muted, adds a *Microphone* row to [Core Health](CORE-HEALTH.md), and Quiet Windows gains a fix that stops voice calls
from lowering the game's volume.

## Microphone mute

Press the microphone button (or the keyboard shortcut, see below) to mute; press it again to unmute.

- Windows keeps two default microphones: one for games and apps (*Console*) and one for calls (*Communications*).
  They are often the same device; when they differ, Core mutes both.
- Core notes each microphone's own mute first and puts every one back exactly as it was. A microphone you had muted
  yourself before stays muted after Core unmutes.
- **While muted, a new default microphone is muted too**: a headset plugged in, a change of default in Windows, or a
  different device after sleep. Core hears about device changes at once from Windows and also checks every five
  seconds and on wake.
- **An unmute from outside Core wins.** If something unmutes a microphone Core had muted (Windows' sound settings,
  or a driver resetting it), Core does not fight it: its mute ends, the other microphones are restored and the
  button, the top-bar mark and Health all show the microphone as live. Core never shows "muted" unless Windows
  reports it.
- If the microphone was muted outside Core, the section says *Muted outside Core*; the button then unmutes every
  default microphone.
- **Unplugged while muted.** Windows keeps a microphone's mute while it is unplugged. If you unmute in Core while a
  microphone Core had muted is unplugged, Core keeps its record and, as soon as it is plugged back in, gives it its
  own setting back (the device notification, or the five-second check). The record goes only once that worked, so a
  headset never comes back silently muted.
- **Partly muted.** When one default microphone is muted and the other is live (for example a muted call headset and
  a live webcam), the section says *Microphone partly muted* with a warning, the button is crimson, the top-bar mark
  shows, and Health counts it as something to check. The button then goes the way of the *Communications* default,
  the one calls use: if it is muted, the button unmutes; if it is live, the button mutes everything.
- The mute survives restarts (`%APPDATA%\GamingMode\mic-mute.json`, which also keeps the records of unplugged
  microphones) and sleep (Windows keeps each microphone's mute itself). Core writes the record before it mutes, so a
  crash in between cannot lose the microphones' own settings.

## Calls mic

The **Calls mic** list shows every connected microphone by its Windows name, with *calls* next to the one Windows uses
for calls (the default *Communications* device) and *games and apps* next to the default *Console* device. Pick one
with the controller and Core makes it Windows' default for calls (Xbox party, Discord). **Use for everything** also
makes it the default for games and apps. Turning that off leaves Windows' games-and-apps default as it is (Core does
not know what it was before). **Windows' choice** forgets the calls mic and changes nothing in Windows.

- **Not connected.** When the calls mic is unplugged (for example the DJI receiver is on the streaming PC), the line
  says *Not connected. Calls use Windows' default microphone.* This is information only: no notice, no Health alert,
  nothing to do. The list keeps showing it as *(not connected)*.
- **Back.** When it is plugged in again, Core makes it the calls default again within a few seconds (the device
  notification, or the five-second check). Windows often does this by itself; Core only steps in when it did not. A
  USB receiver plugged into another port can get a new Windows id: Core then finds it by its name, but only if exactly
  one connected microphone has that name.
- **Changed in Windows.** If you choose another default microphone for calls in Windows while the calls mic is
  connected, or while it is away (and Windows' own fallback is still connected, so the change was yours), Core
  respects it: it does not take the default back when the calls mic returns, and the line says *Windows uses another
  microphone for calls. Pick it again to use it.* Picking it again in Core (or making it the default in Windows)
  restores Core's choice. When Core starts and finds the calls mic connected but not the default, it treats that as
  your choice too.
- **Windows refused.** If Windows does not let Core set the default (see below), the line says so and Core does not
  retry by itself; pick the microphone again or set it in Windows' sound settings. Nothing else is affected.
- **Battery.** Core shows no battery level for the calls mic. Windows reports battery levels for Bluetooth devices and
  HID power devices; a USB audio receiver like the DJI Mic receiver is a plain USB audio input and gives Windows no
  battery reading, and DJI documents none over USB. The receiver's own screen shows it.

### How Core sets the default microphone, and why

Windows has no documented API for changing the default audio device. Windows' own Sound panel uses the undocumented COM
class `CPolicyConfigClient` (`{870AF99C-171D-4F9E-AF0D-E63DF40C2BC9}`) through the `IPolicyConfig` interface
(`{F8679F50-850A-41CF-9C72-430F290290C8}`, Windows 7 and later). It is what the widely used device switchers call:
EarTrumpet, SoundSwitch, NirSoft's SoundVolumeView, and Sunshine
([its PolicyConfig.h](https://docs.lizardbyte.dev/projects/sunshine/master/PolicyConfig_8h.html)). Core uses only
its `SetDefaultEndpoint` method: the *Communications* role for calls, and *Console* plus *Multimedia* (as Windows' Sound
panel does for "Set as default device") for **Use for everything**.

Risks, and what Core does about them: the interface is undocumented, so a Windows update could change or remove it.
Core keeps it behind one small class (`PolicyConfigDefaultMic`, behind `IDefaultMicSetter`), checks afterwards that
Windows really reports the new default, and turns every failure (class missing, interface refused, an error code, or a
default that did not change) into the *Windows did not let Core set the default* line. It never crashes the agent and
never retries in a loop. Mute and everything else keep working.

## Mute scope

**Mute** has two settings:

- **Mute everything** (the default): the microphone button mutes the calls mic and the games-and-apps microphone, as
  described above.
- **Mute calls only**: the button mutes only the calls microphone (Windows' *Communications* default). The
  games-and-apps microphone stays live, so a game, an app or a recording that uses it keeps your voice. Windows mutes a
  microphone for every app at once, though: an app that records from the calls mic itself (OBS set to that device, for
  example) is muted too. With **Use for everything** on, both defaults are the same device, so both settings mute it.

The restore rules are the same for both settings: Core notes each microphone's own mute first, a microphone unplugged
while muted keeps its record until it is back, an unmute from outside Core ends Core's mute, and a new calls default
while muted is muted too. With *calls only*, *Partly muted* does not appear: Core watches only the calls mic (if Windows
reports no calls default, the button covers every default microphone, so it never does nothing). Switching the setting
while muted applies the new one at once: switching to *calls only* gives the other microphone its own setting back,
switching to *everything* mutes it (and records it first).

**Top-bar mark.** A crimson microphone-off icon appears before the clock in Steam's top bar while the default
microphones are muted (or partly muted), in Big Picture (and in a game's Steam overlay wherever Steam shows its clock). It is drawn the same way as Core's date next to
the clock (the plugin's backend asks the agent every four seconds and updates Steam's page). If the agent does not
answer, no mark is shown rather than an old one.

**Controller shortcut.** The agent registers a keyboard shortcut, **Ctrl+Alt+M** by default, the same way as the
Dashboard's Ctrl+Alt+P: Windows tells the agent the keys were pressed, and nothing reads the controller. To use it
from the controller, bind a Steam Input chord to it once (Steam > Settings > Controller > Guide Button Chord Layout,
for example Steam + Y → keyboard Ctrl+Alt+M). It works in games too, since Steam presses the keys. Change or turn it
off with `micMuteHotkey` in the `gaming` section of `%APPDATA%\GamingMode\config.json` (empty string = off). If
another program already owns the combination, `agent.log` says so and the shortcut stays off.

## Xbox party

**Xbox party** closes Quick Access and opens the Xbox Game Bar over Big Picture. Your party is in the Game Bar's
*Xbox Social* widget: open it once from the Game Bar's widget menu, and the Game Bar normally reopens the widgets you
left open. The party keeps running when the Game Bar closes.

### How Core opens the Game Bar, and why

| Method | Result of research | Used |
| ------ | ------------------ | ---- |
| Win+G (`SendInput` from the agent) | Windows' own documented shortcut for the Game Bar. The agent reads `Windows.Gaming.UI.GameBar.IsInputRedirected` to know whether the bar is open | **Only with no game running** and the bar known to be closed |
| `ms-gamebar://launch/` | Microsoft's [Game Bar URI template](https://learn.microsoft.com/gaming/game-bar/api/xgb-widgetcontrol) is `ms-gamebar:[//coreCommand/][widgetCommand]/[widgetId]`; the `launch` core command (the default) opens Game Bar normally. Launched through the shell, so no keystroke reaches any window | **Whenever a game runs, or Core cannot tell**, and when the bar's state is unknown |
| `ms-gamebar:` URI to a widget | `ms-gamebar:[//launch/]activate/<packageFamilyName>_<appId>_<appExtensionId>`, but Microsoft also says the Game Bar "currently doesn't support URI activation apart from widget to widget activation", and the Xbox Social widget's id is not published | Only if you configure one (below) |
| Opening the Xbox app | Leaves Big Picture for a desktop window; worse for the controller | No |
| Controller button for the Game Bar | Core's setup turns *Open Game Bar with the controller button* off so Steam owns the Xbox button | Untouched |

**Win+G is never sent while a game runs.** Anti-cheat safe mode only recognises the games in its catalogue, and a game
launched seconds earlier may not be detected yet, so Core does not rely on it here: a game counts as running when
safe mode sees one, when Steam reports one (`RunningAppID`), or when Core cannot tell (Steam's value unreadable, or
something other than Big Picture in front, which may be a game started outside Steam). Then Core launches
`ms-gamebar://launch/` instead, and if Windows cannot open it, the button says *Windows could not open the Game Bar*
rather than falling back to the keystroke. With no game and Big Picture in front, Win+G is used as before. While a
game protected by kernel anti-cheat runs, the button does nothing at all and says so. Core also sends nothing when the
bar is already open, or within two seconds of opening it (it may still be animating in), and when the bar's state is
unknown it uses the link rather than the toggle. If a stuck key ever left only *Win* pressed, Core releases it behind
a neutral key so the Start menu does not open over Big Picture.

If you find a widget link that works on your PC, put it in `voicePartyUri` in the `gaming` section of `config.json`:
Core then opens that link instead of `ms-gamebar://launch/` (only `ms-gamebar:` links are accepted). Whether
`ms-gamebar://launch/` brings the Game Bar up over a full-screen game, and over Big Picture, is to be confirmed on the
PC (checklist below).

### Coming back with the controller

While the Game Bar is in front, Steam Input treats it as a desktop window and uses your **Desktop Layout** (by default
the right trackpad or stick moves the mouse). Steam still owns the controller: Core reads no controller input, by the
rule in `DashboardShortcutService.cs`. So:

- **Close the Game Bar** with whatever your Desktop Layout sends as **Esc** (check that B sends Esc in Steam >
  Settings > Controller > Desktop Layout), or by clicking outside it with the trackpad, or press the **Steam button**,
  which should bring Big Picture forward (the Game Bar hides itself when it loses the foreground; to be confirmed on
  the PC).
- **Core watches the Game Bar close** (every 0.3 s while it is open, for up to six hours) and, if Big Picture was in
  front when you pressed the button and is not in front afterwards, brings it back so the controller drives Big
  Picture again. It never takes the screen from a game that started meanwhile.
- A "B closes the Game Bar" button handled by Core is not possible without reading the controller, which Core does not
  do.

Whether the Game Bar opens over Big Picture with Win+G, whether the Xbox Social widget reopens by itself, and how your
Desktop Layout behaves inside the Game Bar can only be checked on the PC (checklist below).

## Discord

The Discord row only says whether Core's Discord plugin (Eternal-Assemble, Decky name *Discord*) is installed. Decky
has no supported way for one plugin to open another plugin's panel, so Core does not try; open it from Decky's tab.
Core does not duplicate anything the Discord plugin does.

## Party invites in Big Picture: not built

Core cannot see Xbox party invites without a fragile hack, so nothing was built.

- **Windows notification listener** (`Windows.UI.Notifications.Management.UserNotificationListener`): needs the
  *User Notification Listener* capability declared in an app manifest, which means package identity. Core's app and
  agent are unpackaged (the app's AppUserModelID `LoZazaMastro.Playhub` is not a package identity). Microsoft's way to
  add identity to an unpackaged app is a signed "package with external location", and the package must be signed with
  a certificate the PC trusts (a self-signed one has to be added to *Trusted People* first).
- **Reading Windows' notification database** (`wpndatabase.db`) or the toast windows with UI Automation: undocumented,
  breaks with Windows updates, and Quiet Windows hides banners in Gaming Mode anyway.
- **Xbox services**: there is no public API for parties or party invites.

Options, if invites matter enough:

1. **Package identity for the agent** (a sparse MSIX with external location, signed): unlocks the listener and an
   "Open party" notice with one Windows consent prompt. Needs a signing certificate and installer work; a natural fit
   for the Phase 2 identity migration.
2. **Let Xbox banners through in Gaming Mode**: instead of turning all Windows banners off, Quiet Windows could keep
   them off only for other apps. Windows' own invite banner (with its Join button) would then appear, but Windows may
   still hold banners back while a full-screen app is in front, so this needs testing on the PC.
3. **Leave it as is**: invites show inside the Game Bar's Xbox Social widget, one press away.

## Quiet Windows: voice calls do not lower the volume

Core > Gaming Mode > Quiet Windows has a new fix, *Voice calls do not lower the volume of games and music*. It sets
Sound > Communications, "When Windows detects communications activity", to **Do nothing**
(`HKCU\Software\Microsoft\Multimedia\Audio\UserDuckingPreference = 3`). It is a per-user value, applied with the
other per-user fixes by **Apply** without an administrator prompt, backed up first, and put back (or removed again)
by **Undo changes**.

## On the TV: checklist

1. Quick Access > **Core**: the **Voice** section is at the top.
2. Press the microphone button: it turns crimson, says *Microphone muted*, and a crimson microphone mark appears
   before the clock in the top bar. Join a party or a Discord call and check that nobody hears you. Press it again:
   the mark disappears.
3. If your call headset is a different device from your default microphone, check both are muted (Windows sound
   settings > Input, from the desktop once) and that both come back as they were.
4. While muted, plug in (or switch to) another headset: within a second or two it is muted too.
5. While muted, unplug the headset, unmute in Core, then plug the headset back in: within a few seconds it is live
   again (`agent.log`: *A microphone Core had muted is back*).
6. With two different default microphones, mute only one in Windows' sound settings: the section says *Microphone
   partly muted*, the mark shows, Health's *Microphone* row says *Partly muted*.
7. Sleep and wake Core while muted: still muted, mark still there.
8. Bind a Steam chord to **Ctrl+Alt+M** and try it in Big Picture and in a game.
9. Press **Xbox party** with no game open: Quick Access closes and the Game Bar opens over Big Picture
   (`agent.log`: *Xbox party button: WinG (shortcut)*). Open *Xbox Social* if it is not already open, and start or
   join a party.
10. In a game without anti-cheat, open Quick Access > Core > **Xbox party**: `agent.log` shows *Uri (gameRunning)*,
    no keystroke is sent, and the Game Bar should open over the game. If it does not, note what happens: that is the
    `ms-gamebar://launch/` link to confirm.
11. Close the Game Bar with B (or Esc from your Desktop Layout), and once with the Steam button: Big Picture is in front
    and the controller drives it. `agent.log` shows *Voice: Game Bar closed* and, if needed, *Big Picture is back in
    front*.
12. In a game with anti-cheat (Call of Duty), the party button says it is not available, and the mic button still works.
13. Core > Gaming Mode > Quiet Windows: the new line is ticked after **Apply**; during a party the game volume no
    longer drops.
14. Quick Access > Core > Health: the *Microphone* row says *Muted* in crimson, *Partly muted* or *Live*.
15. **Calls mic:** pick the DJI receiver in **Calls mic**: it is marked *calls*; with **Use for everything**, also
    *games and apps*. Health: *Live · <name>*.
16. Unplug the receiver: *Not connected. Calls use Windows' default microphone.*, no notice, Health *Live · calls mic
    not connected* without crimson. Plug it back (also into another port): marked *calls* again within a few seconds.
17. Make another microphone the calls default in Windows, then replug the receiver: Core does not take the default
    back; picking it again in Core does.
18. **Mute calls only** with a recording app on the other microphone: the party hears nothing, the recording keeps
    sound. With the recording app set to the DJI itself, it is silent too. Under **Mute everything** both are muted.
19. Switch the mute setting while muted, and unplug the DJI while muted under *calls only*: everything comes back as
    it was after the unmute.

If Win+G does not open the Game Bar over Big Picture, note what happens (nothing, the Game Bar behind Big Picture, a
"Is this a game?" prompt) and the `agent.log` lines starting with *Voice:*.

## How it works

- Rules (pure, tested on Linux by `Source/CoreVoice.Tests`): `GamingModeAgent/GamingMode.Services/CoreVoice.cs`:
  `MicMutePolicy` (mute, exact restore, reconcile on device changes, persistence), `PartyLaunchPolicy` (which way to
  open the Game Bar) and `GameBarWatchPolicy` (when to hand the screen back).
- Windows half: `CoreVoice.Windows.cs`: Core Audio (`IMMDeviceEnumerator`, `IAudioEndpointVolume`, an
  `IMMNotificationClient` for device changes), the Game Bar's state (`IsInputRedirected`, the Game Bar package for the
  current user) and the `RegisterHotKey` thread. Win+G: `WindowsXboxGameBar.SendShortcut`.
- Calls mic and mute scope rules (pure, same tests): `CallsMicPolicy` (pick, not connected, back, changed in Windows,
  found by name in another port, refused) and `MicMutePolicy` with a `MicScope` (`Targets`, `ChangeScope`). Setting
  the default: `PolicyConfigDefaultMic` in `CoreVoice.Windows.cs`, behind `IDefaultMicSetter`. Friendly names come
  from each endpoint's `PKEY_Device_FriendlyName`.
- Agent endpoints (loopback only, `MarkIIIHost.Voice.cs`): `GET /voice/state` (now with `devices`, `callsMic` and
  `mic.scope`), `POST /voice/mic?action=mute|unmute|toggle`, `POST /voice/calls-mic?id=<endpoint id or empty>&everything=true|false`,
  `POST /voice/mute-scope?scope=all|calls`, `POST /voice/party`. The Health row reads the same state, plus the calls
  mic's name and status (information only).
- Settings chosen in Quick Access: `%APPDATA%\GamingMode\voice-settings.json` (calls mic id and name, *Use for
  everything*, mute scope, and what Windows fell back to while the calls mic was away), written only by the agent.
- Plugin: `src/VoiceSection.tsx` and `src/voiceReport.ts` (texts in 12 languages; `tests/coreVoiceSection.test.mjs`);
  `main.py` `core_voice_state`, `core_voice_mic`, `core_voice_calls_mic` (accepts only Windows endpoint ids),
  `core_voice_mute_scope`, `core_voice_party` and the top-bar mark (`_topbar_mic_script`).
- Quiet Windows: the `ducking` entry in `Source/Shared/CouchHardening.cs`.
- Config-only settings (`gaming` section of `config.json`, kept by the app when it rewrites the file):
  `micMuteHotkey` (default `Ctrl+Alt+M`), `voicePartyUri` (empty).
