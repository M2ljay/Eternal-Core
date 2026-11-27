# Core Stream mode

When Core streams a game to another device (the Steam Frame, the ROG Flow Z13, the AYN Odin 2 Portal or AYN Thor),
the living-room TV may be showing something else entirely, for example the NVIDIA Shield on another HDMI input.
**Stream mode keeps Core away from the TV while the PC streams:** no wake, no input switch, nothing sent to it,
the PC's own sound kept off it, and Core's notices held back. It turns on and off by itself; **Keep TV out of it**
in Quick Access forces it on.

## In Quick Access and Health

Quick Access > **Core** > **Stream mode**:

- The row says **Off**, or **On · <device>** (for example *On · Steam Frame*), **On · <source>** when the device's
  name is not known (*On · SteamVR*), or **On · kept on by you**.
- **Keep TV out of it**: Stream mode stays on even when Core sees no stream. It survives restarts, so a forgotten
  switch shows here and in Health. Useful when Core cannot see the stream (see the table below) or before you start one.
- A line says what happens to the PC's sound, and one says what Core sees of Apollo (not installed, installed, virtual
  display active).

Health has a **Streaming** row: *Idle*, *Streaming to <device>*, *Streaming · <source>* or *TV kept out by you*
(information only, never an alert). While Stream mode is on, the *TV* row says *Left alone (Stream mode)*: Core does
not even open a connection to the TV to check it.

## How Core knows the PC is streaming

| Stream | How Core sees it | Status |
| ------ | ---------------- | ------ |
| Steam Remote Play / Steam Link (Z13, AYN handhelds with Steam Link, the Steam Frame playing a flat game through Steam) | Core's Big Picture plugin runs inside Steam and listens to `SteamClient.RemotePlay.RegisterForDevicesChanges`. A paired device with a non-zero `unStreamingSessionID` is streaming; its `clientName` is the device name. The plugin tells the agent on every change and every 30 s; the agent forgets a report after 90 s | **Inferred.** Valve does not document this client API. The names come from the community typings in `@decky/ui` (`globals/steam-client/RemotePlay.ts`). Remote Play Together guests are not in that list, so a friend joining a game played on the TV does not turn Stream mode on (also inferred). Needs Decky; without it, use *Keep TV out of it* |
| Steam Frame through its USB 6 GHz adapter (PC VR) | The SteamVR server process (`vrserver.exe`) is running | **Verified that the Frame's PC connection goes through SteamVR** (Valve's [Steam Frame setup page](https://partner.steamgames.com/doc/steamhardware/steamframe/setup): plug in the adapter, launch SteamVR, pair). That `vrserver.exe` is the process name and runs for the whole session is inferred from SteamVR's usual layout. Any SteamVR session counts, wired or wireless |
| Moonlight / Artemis through **Sunshine** | Core reads Sunshine's log (`%ProgramFiles%\Sunshine\config\sunshine.log`, Sunshine's documented default folder): `CLIENT CONNECTED` / `CLIENT DISCONNECTED`, and only while `sunshine.exe` runs (a crash writes no disconnect line) | **Log lines verified** in published Sunshine logs. The file is read-only and shared; Core reads only what was added since the last check. A session that starts but never connects does not count |
| Moonlight / Artemis through **Apollo** | The same lines, plus Apollo's `Display mode for client [<name>] requested` (the device name) and `Virtual Display created at …` / `Virtual Display removed successfully` (Apollo's virtual display), from `%ProgramFiles%\Apollo\config\sunshine.log` | **Log lines verified** in a published Apollo log (2026). The folder is **inferred** (Apollo is a Sunshine fork with the same layout): if yours differs, set `hostLog` (below) |

**Vibepollo** (an Apollo fork, [Nonary/Vibepollo](https://github.com/Nonary/Vibepollo)) installs into Apollo's folder and
writes one log per run, `%ProgramFiles%\Apollo\config\logs\sunshine-<date>-<time>.log`, with the same `CLIENT CONNECTED` /
`CLIENT DISCONNECTED` lines; Core reads the newest one and shows it as Apollo. Its log rolls over in place every ~2 MB
(the full piece is renamed `.log.N`), so for these files Core keeps the session state across a shorter file. Verified in
Vibepollo's source (batch 2, [BATCH-2.md](BATCH-2.md)).

Not used, on purpose:

- **Sunshine's and Apollo's web API** (`https://localhost:47990/api/...`) needs the web UI's user name and password.
  Core never reads credentials, so it reads the log instead.
- **Steam's own log files**: their content is undocumented and changes between client versions.
- **Network traffic** on Sunshine's ports: UDP has no connection state Windows can report.

## The TV while streaming

Every place Core talks to the TV asks Stream mode first (`TvController`), and stops with one line in
`%APPDATA%\GamingMode\agent.log`, for example:

```
TV: Stream mode (streaming to Steam Frame through Steam Remote Play); nothing sent to the TV (wake and input switch).
```

| Moment | Normally | While streaming |
| ------ | -------- | --------------- |
| Power-on into Gaming Mode | TV off: Wake-on-LAN, then Core's HDMI. TV on: left alone | Nothing sent. At power-on Core sees *Keep TV out of it*, Sunshine/Apollo and SteamVR (the plugin is not loaded yet) |
| Wake from sleep | Same as power-on | Nothing sent. If the PC slept **while streaming**, Core waits up to 15 s for the stream to come back before it touches the TV (Moonlight and Steam Link wake the PC first and connect a few seconds later) |
| A stream starting while the TV is waking | — | The input switch is skipped (checked before each attempt) |
| Sleep / shutdown | Off only if the TV shows Core's HDMI | Nothing sent, even if the TV shows Core |
| Core > Gaming Mode > LG webOS buttons (*Turn On Now*, *Switch to HDMI*, *Turn Off*, *Pair*) | Sent | Refused with *Stream mode is on: Core leaves the TV alone.* (*Find the TV* still works: a network search that shows nothing on the TV) |
| Health's TV check (a plain connection) | Once a minute | Skipped |

The rule that Core only wakes a TV that is **off** stays underneath: a TV showing the Shield is "on and in use"
anyway. Stream mode adds the cases where that rule is not enough: a TV that is off (Core should not turn it on for
a stream nobody watches on it), a TV that does not answer its power query, and sleep or shutdown while it shows Core.

If Stream mode's own check fails, the TV is treated as held (logged): the TV is never disturbed by mistake.

A wake by Wake-on-LAN (Moonlight's or Steam Link's "wake PC") is not a "wake by you" for Windows
(`PBT_APMRESUMESUSPEND` is only raised for user input), so it normally does not trigger the TV wake at all; when
Windows does raise it later, the stream is usually already running and holds the TV. This is inferred from
Microsoft's power-event documentation and the existing Mark III code, and is on the checklist below.

## The PC's sound

While Stream mode is on (and `audioOffTv` is on, the default), Core keeps the PC's own sound off the TV, with the same
discipline as the microphone mute: a record is written to `%APPDATA%\GamingMode\stream-audio.json` **before** anything
changes, only what Core changed is put back, and a change you make yourself meanwhile wins.

1. **The default playback device is already the stream's virtual speakers** (*Steam Streaming Speakers*): nothing to
   do. This is the usual case with Moonlight: Sunshine's documented `virtual_sink` / `install_steam_audio_drivers`
   streams through Steam Streaming Speakers "while muting the speakers", and Steam Remote Play installs the same
   driver.
2. **Steam Remote Play, and Steam Streaming Speakers exist but are not the default**: Core makes them the default
   (Console and Multimedia, like Windows' "Set as default device", through the same `IPolicyConfig` Core Voice uses),
   and moves the default back afterwards, unless you or an app changed it meanwhile.
3. **Everything else** (Sunshine or Apollo with *Play audio on host*, SteamVR, *Keep TV out of it* without a stream,
   or no virtual speakers): Core **mutes the TV's speaker endpoint** (Windows' form factor *Digital Audio Display
   Device*, i.e. HDMI/DisplayPort audio to a display) and unmutes it afterwards. An endpoint you had muted yourself
   stays muted; one you unmute during the stream is left live; a TV speaker that appears during the stream (after a
   wake) is muted too; one that is unplugged when the stream ends (TV off) is unmuted when it comes back.

Why not always move the default: Sunshine and Apollo pick the device they capture when the session starts (their
`audio_sink` defaults to Windows' default device), so moving the default mid-session could silence the stream; and
SteamVR's sound belongs to the headset. Muting an endpoint does not stop a loopback capture of it on the hardware
reported so far (a GStreamer issue shows WASAPI loopback "captures audio from a muted device"; Microsoft's loopback
page does not say either way), so the stream should keep its sound. **Inferred; on the checklist.**

The TV only plays the sound of the input it shows, so while it shows the Shield the PC's HDMI sound is silent there
anyway; the mute matters when the TV is on Core's input, or off and turned on by someone else.

## Notices

Core's own notices (the plugin notice, Steam update notices, Core Repair results, Health alerts) appear on the PC's
screen, which nobody is looking at while it streams (or which shows someone else's Shield). While Stream mode is on
they are not shown; the log says *Stream mode: Core notice "…" not shown on the TV*. A Health alert is not marked as
shown, so it comes once Core is back on the TV. The wake sequence does not play while streaming, or after a sleep
mid-stream. Steam's and Decky's own toasts are Steam's: they appear in the stream as usual.

## Resolution: Apollo and its virtual display

Findings:

- **Apollo** ([ClassicOldSong/Apollo](https://github.com/ClassicOldSong/Apollo)) is a Sunshine fork with a built-in
  virtual display (SudoVDA): "The virtual display is created upon the stream starts and removed once the app quits",
  with "auto resolution and framerate matching for your Artemis/Moonlight clients" and a fixed display identity per
  client. Windows only. Its FAQ says to remove other virtual display tools and not to run another Sunshine; it will
  "no longer be compatible with OG Sunshine and OG Moonlight eventually" (its client is Artemis, an Android Moonlight
  fork, which suits the AYN handhelds).
- Apollo can keep the physical display dark during a stream: *Audio/Video > Advanced display device options > Device
  configuration > Deactivate other displays and activate only the specified display* (FAQ). The FAQ also warns that
  *Activate the display automatically and make it a primary display* "may fail".
- Steam Remote Play and the Steam Frame do not use Apollo: Steam scales on its own (its host option "change desktop
  resolution" in Steam > Settings > Remote Play > Advanced host options), and PC VR renders to the headset.

What Core does: **nothing is installed or configured by Core.** Core detects Apollo (its folder and its log) and shows
in Quick Access whether its virtual display is active (and in Health's *Streaming* details). Recommendation, if the
handhelds' resolution matters: install **Apollo instead of Sunshine** (not both: they use the same ports), pair the
AYN devices with Artemis and the Z13 with Moonlight, and try *Deactivate other displays* so the TV's input goes dark
for the stream. Test it on test day: switching displays can move Big Picture and Core's windows; if Big Picture does
not come back to the TV after a session, leave *Device configuration* at its default.

## Settings

In `%APPDATA%\GamingMode\stream-settings.json`, written by the agent (Quick Access changes `keepTvOut`):

| Field | Default | What |
| ----- | ------- | ---- |
| `keepTvOut` | `false` | *Keep TV out of it* |
| `audioOffTv` | `true` | Keep the PC's sound off the TV while Stream mode is on (config-only; read when the agent starts, so edit it with Core closed) |
| `hostLog` | none | A Sunshine/Apollo log to read instead of the default places (config-only). A path containing "apollo" counts as Apollo |
| `streamingAtSleep` | `false` | Internal: the PC slept or shut down while streaming (the wake grace) |

## On the TV: checklist

Do these with the TV showing the Shield (HDMI 1, say), unless a step says otherwise. After each stream ends, check
`agent.log` says *Stream mode: off*, the PC's sound is back where it was, and nothing on the TV changed.

1. **Steam Frame, PC VR** (USB adapter, SteamVR): start SteamVR from the Frame. Quick Access > Core > Stream mode:
   *On · SteamVR*. The TV stays on the Shield, no sound from the PC on the TV, sound in the headset.
2. **Steam Frame, a flat game through Steam**: stream a non-VR game. Note what the row says (*On · Steam Frame*
   means the plugin sees Steam Remote Play; *On · SteamVR* means it runs through SteamVR). Either way, the TV is untouched.
3. **ROG Flow Z13, Steam Link / Remote Play**: *On · <Z13's name>*; sound on the Z13. In Health: *Streaming to …*.
4. **AYN Odin 2 Portal or Thor, Moonlight/Artemis** (Sunshine or Apollo): *On · <device>* (Apollo) or
   *On · Moonlight* (Sunshine). Sound on the handheld, none on the TV. With Apollo: *Apollo: virtual display active*,
   and the stream at the handheld's own resolution.
5. **AYN with Steam Link**: as step 3.
6. **Sound check** (TV on Core's HDMI, someone watching nothing): start a Moonlight stream with *Play audio on host*
   on. The TV must be silent and the handheld must still have sound (this checks the mute fallback against
   loopback capture). Then a Steam stream: the default device should become *Steam Streaming Speakers* (or already be it).
7. **No input switch on wake**: start a stream, put the PC to sleep from the stream (or let it sleep), wake it with
   the device's "wake PC" (Moonlight or Steam Link). The TV must not switch to Core. `agent.log`: *TV on wake: Stream
   mode …* or no TV line at all (Wake-on-LAN wakes are not "by you").
8. **TV off**: with the TV off, start a stream from the Z13: the TV stays off. Stop the stream, sleep and wake Core
   with the controller: now the TV turns on and switches to Core as usual.
9. **Sleep while streaming, TV on Core's HDMI**: the TV is not turned off (Stream mode holds it).
10. **Keep TV out of it**: switch it on with nothing streaming: *On · kept on by you*; Core > Gaming Mode > LG webOS >
    *Switch to HDMI* says *Stream mode is on: Core leaves the TV alone.* Switch it off again.
11. **Notices**: while streaming, nothing of Core's appears on the TV; a pending Health alert appears after the stream.
12. Afterwards, `%APPDATA%\GamingMode\stream-audio.json` is gone (nothing left to restore).

## How it works

- Rules (pure, tested on Linux by `Source/CoreStreamMode.Tests`): `GamingModeAgent/GamingMode.Services/CoreStreamMode.cs`:
  `StreamModePolicy` (sources, expiry, wake decision, when to move the default), `SunshineLog` and `SunshineLogTail`
  (the log), `StreamAudioPolicy` (engage, reconcile, exact release, pending restores), `StreamSettings`.
- TV hold: `TvControl.cs` (`TvController` asks `hold` before every sending action; outcome `StreamMode`), tested
  against Mark III's fake TV.
- Windows half: `StreamMode.Windows.cs` (process probes, render endpoints with their form factor, `IPolicyConfig` for the
  default speaker).
- Agent: `MarkIIIHost.Stream.cs` (check every 5 s and before any TV action, wake grace, power-on hold, notices, sound),
  endpoints on 127.0.0.1:47991: `GET /stream/state`, `POST /stream/keep-tv-out?on=true|false`,
  `POST /stream/steam?active=true|false&device=`.
- Plugin: `src/streamMode.ts` (the Steam Remote Play watch), `src/StreamSection.tsx` and `src/streamReport.ts` (texts
  in 12 languages; `tests/coreStreamMode.test.mjs`), Health row in `src/healthReport.ts`; `main.py`
  `core_stream_state`, `core_stream_keep_tv_out`, `core_stream_report` (device names trimmed to 64 printable characters).
