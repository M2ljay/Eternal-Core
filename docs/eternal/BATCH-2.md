# Batch 2: around the couch

Four features for the living-room PC: swipe typing on Steam's keyboard, Core's state in Home Assistant, simple
automations, and sleep after streaming. Same rules as the rest of Mark III: the rules are pure and tested on Linux,
everything fails soft (logged in `%APPDATA%\GamingMode\agent.log`, never an error at the TV), and Stream mode still
means the TV is left alone.

Settings: Core > Gaming Mode > **Home Assistant** and **Automations** (stored in `%APPDATA%\GamingMode\config.json`,
`gaming` section): `homeAssistantEnabled`, `homeAssistantHost`, `homeAssistantPort`, `homeAssistantUser`,
`automations`, `streamSleepEnabled`, `streamSleepMinutes`, `swipeTypingEnabled`. The MQTT password is not in
`config.json`: it is in `home-assistant-secret.json` next to it, sealed with DPAPI. Agent state: `stream-sleep.json`.
Quick Access > Core shows **Automations** (a summary) and **Swipe typing**; Health gains three rows.

## 1. Swipe typing on Steam's keyboard (B7)

Swipe across the trackpads (or the touchscreen) on Steam's own on-screen keyboard, with word suggestions and
autocorrect. Adapted from [VaporType](https://github.com/dmitrii-galantsev/VaporType) 0.3.2 (MIT, commit `53b499c`,
2026-11-18).

**Integrated into Core's Decky plugin, not shipped as a second plugin.** Why: Core already installs and updates its
plugin with every Core update (`GamingModePayloadSync` copies the whole `gaming-mode` folder), Decky runs one backend
instead of two, the on/off switch can be Core's own setting, and the Health and Quick Access machinery is already
there. A separate bundled plugin would have needed a second install, update and uninstall path in the app (Windows
only, untestable here) and its own Protected-fork handling. What it costs: about 3.6 MB of keyboard files in the
plugin folder (mostly word data), served by Decky beside `dist/index.js` and loaded only when swipe typing is on.

- **What ships:** VaporType's keyboard runtime under `gaming-mode/dist/keyboard/` (`patch.js`, `modes.js`,
  `swipe/engine.js`, `swipe/words.js`, `swipe/lm.js`), with its `LICENSE` (MIT, with the word-data notices),
  `swipe/DATA-LICENSES.md` and `swipe/SCOWL-COPYRIGHT.txt`. The word data is CC BY-SA 4.0 (wordfreq) and unchanged.
  Core's changes are listed in `dist/keyboard/CORE-CHANGES.md`; the plugin's `THIRD-PARTY-NOTICES.md` credits it.
- **The loader** (`src/swipeTyping.ts`) is VaporType's `src/keyboard.ts` rewritten with injected dependencies (so the
  tests can run it). Learned words, the optional typing log and VaporType's "forget everything" bookkeeping (epochs,
  retried deletes) live in Core's `main.py` (`core_typing_*`), in `homebrew\settings\gaming-mode\swipe-typing\` and
  `homebrew\data\gaming-mode\swipe-typing\`, with VaporType's file names.
- **No self-update.** VaporType's backend checks GitHub twice a day and swaps its own files, under a top-level
  `import fcntl` (no such module on Windows). None of that is taken: Core updates the files with Core.
- **Decky 3 and 4:** the plugin asks `steamClientKind()` (`steamModules.ts`) first. On the stable client (webpack) the
  keyboard is patched; on the new ES-module Steam client the patch cannot find Steam's keyboard code, so Core does not
  inject it and the row says *Not available on this Steam client yet*. Steam's trackpad-typing settings store and
  the keyboard's CSS class map are found with Core's compat helpers (`findExport`, `findClassModule`) and handed to
  `patch.js` through `window.__coreOskLookups` (the one hook Core added). The keyboard layout module is still found by
  VaporType's own search of Steam's module sources, which exists only on the webpack client.
- **Never two copies:** if the standalone VaporType plugin is installed in Decky, Core leaves the keyboard to it.
- **Off-switch:** Quick Access > Core > Swipe typing, or Core > Gaming Mode > Automations > *Swipe typing on Steam's
  keyboard* (one setting, `swipeTypingEnabled`, on by default). Off stops the patch at once and Steam's keyboard is
  back. If Steam changes its keyboard, VaporType's own check turns the patch off (*Paused: ...* in Quick Access).
- **Privacy** (VaporType's): learning is off until chosen on the keyboard's first-use card; *Private mode* (Quick
  Access, or hold Esc with a trackpad) pauses learning, autocorrect and the log; the typing log is off unless turned
  on; *Forget everything* deletes learned and blocked words, their backups and the log. Nothing leaves the PC.
- Quick Access text is in Core's 12 languages; the keyboard's own settings panel (hold the emoji key) is VaporType's,
  in English. Swiping, suggestions and autocorrect need the English QWERTY layout.

## 2. Home Assistant bridge (B8)

Core publishes its state to Aincrad's Mosquitto broker (on `serverv3`, port 1883) with MQTT discovery, so the
entities appear under one device, **Core (<PC name>)**, without YAML.

| Entity | Type | What |
| ------ | ---- | ---- |
| Game running | binary_sensor (running) | Steam reports a running app, or anti-cheat protection is engaged |
| Game | sensor | the game's name from Steam's `appmanifest_<id>.acf` (attribute `app_id`); `none` when nothing runs |
| Media | sensor (enum) | Nuvio or Stremio: `playing`, `paused`, `open` or `off` (attribute `app`) |
| Movie or show playing | binary_sensor | Media is `playing` |
| Idle | binary_sensor | in Gaming Mode with no game, nothing playing, no stream and no VR |
| Asleep | binary_sensor | on just before Core sleeps or shuts down, off when it connects again; keeps its value while the PC is offline |
| Stream mode / Streaming to | binary_sensor / sensor | Stream mode is on, and the device when known |
| VR (SteamVR) | binary_sensor | SteamVR is running |
| Gaming Mode | binary_sensor | Core is in Gaming Mode |
| Wake TV | button | the same TV wake as Core's own (Wake-on-LAN, then Core's HDMI), still held by Stream mode |
| Stream mode (keep TV out) | switch | *Keep TV out of it* on or off |
| Sleep PC | button | sleep (never shutdown), refused while a game runs, a stream runs or Core is updating |
| Automation | event | the events Core's automations send (`event_type`, for example `game-start`) |

**"Update Steam now" is not exposed** and Core refuses any command it does not offer. Topics: `core/<node>/state`
(one retained JSON), `core/<node>/asleep`, `core/<node>/availability` (`online`, and `offline` as the last will),
`core/<node>/event`, commands on `core/<node>/cmd/{wake_tv,sleep,keep_tv_out}`. `<node>` is the PC's name, lower
case. Core also listens to `homeassistant/status` (announces itself again after Home Assistant restarts) and
`aincrad/sleep_mode`: Aincrad's `input_boolean.sleep_mode`, published by the `core_pc.yaml` package. While it is on,
the 4 a.m. wind-down reminders (batch 1) are skipped.

- **Credentials:** host, port and user name in Core > Gaming Mode > Home Assistant; the password is sealed with DPAPI
  for the Windows user (`Shared/CoreSecretStore.cs`) and read only to build the MQTT CONNECT packet. It never goes into
  `config.json`, a log or an HTTP answer. Plain MQTT on the LAN (port 1883, as Aincrad's broker is set up); no TLS.
- **No new NuGet package.** The bridge needs CONNECT with a last will, retained PUBLISH, SUBSCRIBE and keep-alive,
  so Core has its own small MQTT 3.1.1 client (`MqttWire.cs`, OASIS spec sections 2 and 3) instead of MQTTnet: the
  agent is one self-contained file with no package references, and the Linux test projects build offline. The tests
  connect it to a fake broker on loopback.
- Reconnects with back-off (5 s up to 5 min); a failure is logged once until it changes. Health row *Home
  Assistant*: off, connected, connecting, login refused, broker not reachable (information only).
- **Nuvio / Stremio** come from Windows' media sessions (what the volume flyout shows), matched by app id, then from
  their processes (*open*). Inferred: their session ids were not seen on this PC (checklist).
- Aincrad side: `homeassistant/packages/core_pc.yaml` on Aincrad's `core-mqtt` branch (lights dim when a game starts,
  movie scene when Nuvio or Stremio plays, nothing while sleep mode is on, sleep mode published for Core). Core needs
  its own broker login: on the server, `docker compose exec mosquitto mosquitto_passwd -b /mosquitto/config/passwd
  core '<password>'` (no `-c`: that would replace the file), then `docker compose restart mosquitto`.

## 3. Automations (B17)

"When this happens, do that", in the spirit of [Docky](https://github.com/datbird/docky) (MIT; ideas only, no code).

| Triggers | Actions |
| -------- | ------- |
| a controller connects; Core wakes; a game starts; a game ends; a stream starts; a stream ends | wake the TV and switch to Core; restore the normal display; restore the normal sound output (the console guards' saved profiles); keep the TV out (Stream mode on); give the TV back; tell Home Assistant (an event) |

Shipped rules (only the first is on): *a controller connects -> wake the TV and switch to Core*; *Core wakes ->
restore display and sound*; *a stream ends -> restore display and sound*; *a game starts / ends -> tell Home
Assistant*; *a stream starts -> tell Home Assistant*. Edited in Core > Gaming Mode > Automations (on/off, trigger,
actions, event name, add, remove, back to defaults; at most 20). Quick Access > Core > Automations shows the rules
that are on, the Home Assistant connection and sleep after streaming.

- **One TV wake, not two.** Waking the TV on resume stays where it was (*LG webOS > Turn on at PC start*). A
  "Core wakes -> wake the TV" rule turns that same path on rather than adding a second wake; and every TV wake (boot,
  resume, a controller, Home Assistant) shares one 90-second cooldown, so the controller that woke the PC does not
  wake the TV again when it connects a few seconds later. The TV wake still only turns on a TV that is off, still
  switches to Core's HDMI, and Stream mode still holds it.
- **Controller connects** = a new controller in Windows' device list (the console guards' HID list), compared every
  5 s while a rule uses it. The first look only learns the list; nothing counts in the first minute after the agent
  starts or the PC wakes (Windows re-enumerates devices); a controller that comes back within 2 minutes (a Bluetooth
  reconnect) does not count. A Steam Controller on its Puck may stay listed while it sleeps: then the wake trigger
  covers it (checklist).
- **A stream starts / ends** = a stream detected or ended (Stream mode's sources). *Keep TV out of it* does not count,
  so a rule that switches Stream mode cannot trigger itself.
- TV, display and sound actions run only in Gaming Mode; display and sound are skipped while Stream mode is on (they
  would fight Apollo's virtual display and the stream's audio) and the display is never restored during a game.
- Every run is one agent log line, for example `Automations: when controller connected: tv wake — done.`

## 4. Sleep after streaming (B13)

When a handheld's stream ends (the Odin 2 Portal, AYN Thor, Z13 or Steam Frame through Vibepollo/Apollo/Sunshine,
or Steam Remote Play) and nobody uses Core here, Core puts the PC to sleep after a delay (10 minutes by default; 5 to
60 in the app). **Sleep, never shutdown:** a running game stays suspended in memory and is there when the handheld
wakes the PC again. Ideas from HostSleep (decky-hostsleep) and CouchLink (BSD-3); no code taken.

- **How Core sees the stream:** Stream mode's existing sources. Vibepollo is an Apollo fork that installs into
  Apollo's folder but writes one log per run under `config\logs\` (`sunshine-<date>-<time>.log`); Core now reads the
  newest one (`CLIENT CONNECTED` / `CLIENT DISCONNECTED`, verified in Vibepollo's source) and keeps the session across
  its in-place roll-overs. A handheld that suspends drops its connection, which the host logs as a disconnect. The
  log was chosen over Vibepollo's local API because the API needs a token Core would have to store, and over Windows
  events because none exists for a streaming session. SteamVR sessions are not counted (the Frame's headset idles
  differently).
- **"Nobody uses Core here":** no keyboard or mouse input (Windows' idle time) and no Steam controller button input
  (Core's plugin reports it with the heartbeat; analog input is ignored) after the stream ended. Input in the first
  10 seconds after the disconnect is the stream's own and does not count. A stream coming back cancels too.
- **Never interrupts:** waits while Core is updating Steam or Decky; only in Gaming Mode; a timer far older than its
  delay (the PC slept by itself) is dropped. One minute before, a Steam toast says "Nobody is playing: Core goes to
  sleep in 1 min. Press any button to keep it awake."
- **The TV is left alone** on the way down (no "turn off if it shows Core") and on the next wake: Core marks the
  sleep as mid-stream, so Stream mode's wake grace waits 15 s for the stream to come back before touching the TV.
- Health row *Sleep after streaming*: *Off*, *On · after 10 min*, *Sleeping at 01:24 unless someone plays here*.

### Waking the PC from a handheld (Wake-on-LAN)

1. **Cable.** Wake-on-LAN needs the PC's wired network (the row says *Wi-Fi only* otherwise).
2. **Network adapter** (Device Manager > Network adapters > the Ethernet adapter): *Advanced* > *Wake on Magic Packet*
   = Enabled; *Power Management* > *Allow this device to wake the computer* on (and *Only allow a magic packet* on).
3. **BIOS/UEFI:** *Wake on LAN* / *Power On By PCI-E* enabled, *ErP* / *Deep Sleep* disabled (otherwise the network
   card has no power in sleep or shutdown).
4. **Fast startup** (Control Panel > Power Options > *Choose what the power buttons do*): waking from **sleep** works
   either way; waking from **shutdown** often fails with fast startup on.
5. **Moonlight / Artemis** (Odin 2 Portal, Thor, Z13): pair the PC once while it is awake (Moonlight stores its MAC
   address), then long-press the PC > **Wake PC**. Moonlight sends the magic packet to the LAN broadcast address.
   **Steam Link** (Steam Frame, Z13): the PC shows as asleep; select it to wake it.
6. Health row **Wake-on-LAN** checks what Windows lets Core read without administrator rights: the wired adapter and
   its link, `*WakeOnMagicPacket`, `PnPCapabilities` (power management off), whether the adapter is in
   `powercfg /devicequery wake_armed`, and fast startup (`HiberbootEnabled`). It shows the wake address (MAC). BIOS
   settings cannot be read.

## Tests

- `Source/CoreHealth.Tests` (`BatchTwoTests.cs`): the MQTT wire format, a live connection to a fake broker on
  loopback (CONNECT, SUBSCRIBE, a QoS 1 command answered with PUBACK, a retained PUBLISH, DISCONNECT, a refused login,
  a closed port), the bridge (node id, host checks, discovery payloads, state, commands, no Steam update, sleep refusal,
  the row), automations (defaults, cleaning, the one TV wake path, the cooldown, the controller watch), sleep after
  streaming (arm, grace, input, stream back, warning, sleep, busy, stale, off), Vibepollo's log names and roll-over,
  Wake-on-LAN readiness, the Health rows' order.
- `Source/MarkIII.Tests`: the new agent text in all 12 languages. `Source/LocalizationResource.Tests`: the desktop
  cards in all 12 languages. `Source/CoreStreamMode.Tests`: Stream mode unchanged.
- `GamingModeDeckyPlugin/tests/coreBatchTwo.test.mjs`: the Quick Access text in 12 languages, the automations summary,
  the three Health rows, the local-input report, the swipe typing loader against fakes (inject with Core's lookups,
  save learned words, off, the ES-module client, the standalone plugin, forget everything), the shipped keyboard files
  and notices, and the backend's calls. `tests/optionalStartup.test.mjs` counts the two new optional integrations.

## Needs the PC (or the server)

Nothing here could be tried against the real thing on Linux: the agent and app builds (CI), swipe typing on both
Steam clients with the 2026 Steam Controller's trackpads (VaporType was written for the Steam Deck's pad numbers),
Decky serving `dist/keyboard/`, the MQTT bridge against Mosquitto on `serverv3`, Nuvio's and Stremio's media
sessions, controller connect events, Vibepollo's real log folder, sleep and Wake-on-LAN from each handheld, and the
Wake-on-LAN row's readings. See the TV checklist in the batch's report.
