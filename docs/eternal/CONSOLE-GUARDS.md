# Console guards ("stays a console")

The living-room PC should never ask for a keyboard, a mouse or a password, and nothing should quietly break the TV
picture. The console guards check the things that usually do, show them as rows in Quick Access > Core > **Health**
(see [CORE-HEALTH.md](CORE-HEALTH.md)), and offer one button at the TV wherever Windows allows one. Where it does not,
the row gives one line saying what to do on the desktop.

Rules that hold for every guard:

- No kernel driver, ever. Everything here is user-mode Windows API, registry reads or the Shell.
- UAC is never weakened. The two BitLocker steps that need administrator rights run from the desktop app behind one
  Windows prompt each; the TV never shows a prompt (see [BitLocker guard](#bitlocker) for the one scheduled task).
- No credentials are stored or read: not the Windows password, not the BitLocker recovery key, not Steam's tokens.
  Core reads only whether such things exist.
- Every probe fails soft: an unreadable value is *Unknown*, never an error over Big Picture.
- Every registry value Core changes is recorded first and can be undone from the desktop (Quiet Windows' backups,
  `%LOCALAPPDATA%\Playhub\couch-user.json` and `couch-machine.json`).
- Notices follow Core Health's rules: at most once a day per problem, never during a game, never while streaming, and
  only with **Health alerts** on.

## The rows

| Row | Healthy (steel) | Amber (information) | Crimson (needs attention) | Button at the TV |
| --- | --------------- | ------------------- | ------------------------- | ---------------- |
| Display | Matches the saved normal display | Not saved yet; TV not connected | Changed: resolution, refresh rate, HDR, main display, Windows' variable refresh switch (notice) | **Restore display settings**; open the row for **Save current display as normal** |
| Sound output | The saved default device | Not saved yet; saved device not connected; left to Stream mode | Another default device (notice) | **Switch sound back**; open the row for **Save current sound output as normal** |
| Controllers | What Windows lists | None connected; an expected controller not seen | never | open the row for **Mark these controllers as expected** |
| Steam sign-in | Signed in (or offline mode) | Steam not running | Not signed in (sign-in or Steam Guard screen); signed in but *Remember me* off | none (sign in on the Steam screen with the controller) |
| Automatic sign-in | On, with the user name | | Off (notice); ends after N sign-ins; blocked by *Windows Hello only* | none (re-setup on the desktop) |
| BitLocker | Off; on with a recovery-key protector | On but not checked from the desktop yet; device encryption waiting for activation; suspended for the update | No recovery key; firmware update waiting (notice without the guard); suspended for no known reason | **Suspend BitLocker for one restart** (only with the guard installed and firmware staged) |
| Windows setup screens | Off | On | never | **Turn off setup screens** |
| Update policy | Drivers held, feature updates pinned (Pro) / drivers held (Home) | Not applied | never | none (Quiet Windows on the desktop, needs administrator rights) |
| Launcher updates | Nothing waiting | Waiting: EA app, Riot Vanguard... | never | none |
| Launcher pop-ups | Watching, N pushed back today | Off | never | none |
| After waking | OK at the last wake | Fixed by Core (focus taken back, input re-sent); off | Big Picture not visible, TV not on Core's input, network cable unplugged (notice) | open the row for **Run the wake check now** |
| Exiting games (3.3.5) | Watching; closed cleanly; Core closed N leftovers | | A game did not exit (notice); Steam lost track of a game still on screen | **Exit <game>** |
| Back paddles (3.3.5) | L4 = A, R4 = B in Steam's Big Picture and Desktop layouts | Not set in a layout; layouts not found | never | **Open Steam's controller layout** |
| Emulator updates (3.3.5) | Up to date; waiting (soak) | Needs you (confirm a version on the desktop; a rollback) | never | none (desktop confirmation) |
| *Emulator* (one per emulator, 3.3.5) | All set · version | Check: settings, RetroAchievements, BIOS | never | **Fix <emulator> settings** |
| Hotkeys · *emulator* (one per emulator, 3.3.5) | Match the shared table; "Can't: ..." when the emulator has no controller hotkeys or save states | Drifted: functions; start it once first | never | **Re-apply hotkeys to <emulator>** |

## Each guard

### BitLocker

- **Detectable without administrator rights:** whether BitLocker protects the system drive, through the Shell's
  `System.Volume.BitLockerProtection` property (what Explorer's padlock shows). Microsoft does not document its values;
  Core reads 1 and 6 as on, 2 off, 3 encrypting, 4 decrypting, 5 suspended, 8 waiting for activation, anything else
  unknown.
- **Needs administrator rights (desktop, once):** *Check BitLocker* runs a PowerShell script (passed with
  `-EncodedCommand`) that asks `Win32_EncryptableVolume` for the key protector **types** only (1 TPM, 3 recovery
  password, 4 TPM+PIN...) and for the newest event 845 in *BitLocker Management* ("recovery information was backed up
  to your Microsoft account"). It writes `%LOCALAPPDATA%\Playhub\bitlocker-check.json` with type numbers and a time.
  It never asks for the recovery password, protector IDs or anything else. Whether the key really is in the Microsoft
  account cannot be seen locally: the row always says to confirm it at
  `account.microsoft.com/devices/recoverykey`.
- **Firmware updates Core can see:** a UEFI capsule staged by Windows Update for the next restart. Windows stages it
  under `HKLM\SYSTEM\CurrentControlSet\Control\FirmwareResources\{GUID}` (`Version`); the installed version of the same
  resource is in the ESRT, `HKLM\HARDWARE\UEFI\ESRT\{GUID}` (`Version`). A staged version newer than the installed one
  is pending. Both keys are readable without administrator rights.
- **Not detectable:** BIOS updates planned by vendor tools (Dell Command | Update, Lenovo Vantage, MSI Center, ASUS
  Armoury Crate...). Most of them suspend BitLocker themselves before flashing; Core cannot see their plans.
  Microsoft says to suspend BitLocker for manufacturer and TPM firmware updates
  (`Suspend-BitLocker -RebootCount`), which is what the guard does.
- **The guard (desktop, optional):** *Turn on BitLocker protection* creates one scheduled task, `\Core\BitLocker
  suspend once`, behind one Windows prompt. It runs as SYSTEM and does exactly one fixed thing:
  `%SystemRoot%\System32\manage-bde.exe -protectors -disable C: -RebootCount 1` (BitLocker suspended for one restart,
  then back on by itself). Its security descriptor lets this Windows user start it (`schtasks /Run`), so the Gaming Mode
  agent can run it without a prompt. No script or user-writable file is involved, so it cannot be repurposed; the worst
  any program in your session can do with it is suspend BitLocker until the next restart. *Remove BitLocker
  protection* deletes the task.
- **When Core suspends:** only when BitLocker is on, a capsule is staged, the guard is installed and Core has not
  already asked since the last boot: automatically just before an update-night restart, or when you press **Suspend
  BitLocker for one restart** on the row.

### Automatic sign-in

Read from `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`: `AutoAdminLogon` = 1 and `DefaultUserName`.
The password is either Sysinternals Autologon's LSA secret (not readable without SYSTEM; Core does not try) or a
plain-text `DefaultPassword` value, of which Core checks only that the **name** exists (the row then suggests
Autologon, which keeps it encrypted). Also flagged: `AutoLogonCount` (Windows turns automatic sign-in off after that
many sign-ins) and `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device`
`DevicePasswordLessBuildVersion` = 2 (Settings' "only allow Windows Hello sign-in", which hides and can break
automatic sign-in). The fix is a guided re-setup on the desktop: Core > Gaming Mode > **Stays a console** explains
Autologon; Core stores no password.

### Display

**Save current display as normal** stores the primary display (the LG): its DisplayConfig target path (stable across
GDI renumbering), GDI name, monitor name, resolution, refresh rate, HDR (advanced colour) and Windows' *Variable refresh
rate* switch (`HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, `DirectXUserGlobalSettings` `VRROptimizeEnable`).
G-Sync and FreeSync live in the GPU driver and are not readable from Windows; the row says so. Core compares at every
Health read, 90 seconds after the agent starts and after each wake. **Restore display settings** sets the mode with
`ChangeDisplaySettingsEx`, makes the LG primary again (every display moved so it sits at 0,0, `CDS_SET_PRIMARY`, one
final apply) and switches HDR with `DisplayConfigSetDeviceInfo` (`SET_ADVANCED_COLOR_STATE`). The VRR switch is
compared but not written. Restore is refused while a game runs. A TV that is off or on another input disappears from
Windows' list; that is shown as *TV not connected*, not as a change.

### Sound output

**Save current sound output as normal** stores the default playback device (Console role). A different default is
flagged and **Switch back** sets it through `IPolicyConfig` (undocumented, the same isolated, fail-soft class Stream
mode uses; the result is checked against what Windows reports afterwards). While Stream mode is on the row is left
alone, and a saved device that is not connected (the TV's HDMI audio while the TV is off) is information only.

### Controllers

Present HID devices from SetupAPI (no device is opened): HID gamepad, joystick and multi-axis collections, XInput
pads (`IG_` in the hardware ID), and any Valve device (vendor 28DE: the Steam Controller and its Puck use Valve's
vendor ID; their product IDs are not published, so they show as *Valve controller*). The Mayflash DolphinBar in mode 4
presents each Wii remote as Nintendo's RVL-CNT-01 (057E:0306), shown as *Wii Remote (DolphinBar)*. DualSense, Xbox and
8BitDo pads are named by vendor and product. Bluetooth hardware IDs are read too. Information only, no alarms;
**Mark these controllers as expected** makes a missing one show in amber.

### Steam sign-in

`HKCU\Software\Valve\Steam\ActiveProcess` `ActiveUser` (0 while the sign-in screen or a Steam Guard prompt is up) plus
`config\loginusers.vdf` (`MostRecent`, `RememberPassword`, `AllowAutoLogin`, `WantsOfflineMode`, `PersonaName`). No
token file is opened. Steam Guard itself is not visible from outside Steam, so "running but nobody signed in" is shown
as *sign-in or Steam Guard screen on the TV*.

### Windows setup screens (applied)

Per-user values, applied by the agent at the TV (**Turn off setup screens**) or with Quiet Windows on the desktop,
backed up in `couch-user.json` and undone with Quiet Windows' **Undo**:

| Key (HKCU) | Value | What it stops |
| ---------- | ----- | ------------- |
| `Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement` | `ScoobeSystemSettingEnabled` = 0 | "Let's finish setting up your device" |
| `Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager` | `SubscribedContent-310093Enabled` = 0 | the welcome experience after updates |
| same | `SubscribedContent-338389Enabled` = 0 | tips and suggestions |
| same | `SubscribedContent-338388Enabled` = 0 | Start suggestions |
| same | `SubscribedContent-338393Enabled`, `-353694Enabled`, `-353696Enabled` = 0 | suggested content in Settings |
| same | `SoftLandingEnabled`, `SystemPaneSuggestionsEnabled`, `RotatingLockScreenOverlayEnabled` = 0 | tips, suggestions, lock-screen overlays |

### Update policy (applied from the desktop)

Machine values, so they go with Quiet Windows' one administrator prompt on the desktop and are undone the same way
(`couch-machine.json`). The row reads the edition (`EditionID`; `Core*` is Home) and says which applies:

| Edition | Values (`HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate` unless noted) |
| ------- | ------------------------------------------------------------------------------ |
| Pro, Education, Enterprise | `ExcludeWUDriversInQualityUpdate` = 1; `ProductVersion` = "Windows 11", `TargetReleaseVersion` = 1, `TargetReleaseVersionInfo` = the installed version (for example "24H2"). Feature updates stay there; security updates continue. To move up, Undo and Apply again. |
| Home | `ExcludeWUDriversInQualityUpdate` = 1 and `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching` `SearchOrderConfig` = 0 (Device Installation Settings: No). Home ignores `TargetReleaseVersion`, so feature updates cannot be pinned there; the row says so. `SearchOrderConfig` = 0 also stops Windows Update from fetching drivers for brand-new devices. |

### Launcher and anti-cheat updates (information)

Cheap signals only, never a network call: `PendingFileRenameOperations` (files an installer replaces at the next
restart) mentioning Riot Vanguard, Easy Anti-Cheat, EA app, Epic, Ubisoft Connect or Battle.net, and running installer
processes of those vendors (`EasyAntiCheat_EOS_Setup`, `EpicInstaller`, `EAappInstaller`, `UbisoftConnectInstaller`,
`Battle.net-Setup`...). Not detectable cheaply: an update a launcher has downloaded but not started, or an Easy
Anti-Cheat service older than a game's bundled one (that needs scanning every game folder).

### Launcher pop-ups (active)

While Big Picture is in front in Gaming Mode and no game runs, a window of the Epic Games Launcher, EA app, Ubisoft
Connect, the Xbox app or Battle.net that takes the foreground is sent to the bottom of the window order
(`SetWindowPos` `HWND_BOTTOM`, no activation) and Big Picture is focused again with Core's existing focus code
(`SteamFullscreenDetector.TryFocusSteamWindow`). Nothing is closed or minimised. If a launcher comes back three times in
ten minutes, Core stops: you want it. Foreground changes come from Core's existing `WindowLifecycleObserver`. Switch:
`gaming.consoleLauncherFocusGuard`.

### After waking (active)

About 35 seconds after a wake into Gaming Mode (after the TV wake and the wake sequence): Big Picture visible and in
front (focused again once if not, never over a game); the TV on Core's input (asked read-only over webOS; the switch is
sent again once, only if Core itself switched the TV on during this wake, never in Stream mode, never when the TV was
already in use); and the Ethernet link (a physical adapter up with a gateway; VPN and virtual adapters do not count).
**CPU and GPU temperatures are not shown:** Windows has no user-mode CPU temperature, and the readers that exist need
a kernel driver (WinRing0, PawnIO), which Core does not use. Switch: `gaming.consoleWakeChecks`.

### Exiting games (active, 3.3.5)

Games are exited only through Steam's own **Exit Game** (Steam button or Quick Access). How Steam ends a game on
Windows: it ends what it started. For a non-Steam shortcut that is the program the shortcut names, so a wrapper (EmuDeck's
`tools\launchers\*.ps1`, which waits with `Start-Process -Wait`; a `.bat`; a launcher exe) can be ended while the
emulator it started keeps running, and a wrapper that exits at once makes Steam think the game is over while it still
runs, so Steam stops offering Exit Game. (The first is from EmuDeck's source, `functions\emulatorInit.ps1`; that Steam
ends only its own child is inferred from this behaviour and on the checklist.)

What Core does:

- **Signals.** Core's Big Picture plugin reports Steam's app lifetime (`SteamClient.GameSessions.RegisterForAppLifetimeNotifications`)
  and wraps `SteamClient.Apps.TerminateApp` (what Steam's Exit Game calls; the original runs first, the report is
  fire-and-forget, `src/gameExit.ts`) to `POST /game/lifetime` and `/game/exit-chosen`. Without Decky the agent uses
  the process tree alone (Steam's child gone and `RunningAppID` 0).
- **The launch tree.** Every 2 s the agent reads the kernel's process list (`NtQuerySystemInformation`; no process is
  opened): a new child of `steam.exe` starts a launch; its children join while they live, matched by pid **and start
  time** (a reused pid never counts), and so does a catalogue emulator started during the launch. Steam's own
  processes, SteamVR and store launchers never join.
- **When the launch ends.** Exit Game chosen: everything left in the tree is closed. Steam ended it by itself:
  windowless leftovers are closed; a leftover with a visible window is the game still on screen, which Steam lost track
  of: it is **left running**, the row turns crimson (*Steam lost track of <game>*) and Quick Access > Core > Controls and
  the row offer **Exit <game>**, which closes it the same way. Closing: WM_CLOSE to each process's windows, leaves first
  (the emulator before its wrapper), 8 s, then `TerminateProcess` (opened with `PROCESS_TERMINATE` only, after checking
  the start time again). Nothing is closed while anti-cheat safe mode is engaged.
- **Harmless when the launch is already tracked** (The Collector's direct ROM shortcuts): the tree is then just the
  emulator, Steam ends it itself, and nothing is left to close.
- Switch: `gaming.gameExitCleanup` (on). State: `%APPDATA%\GamingMode\game-exit.json` (last result).

Not done, on purpose: no emulator-exit hotkey (games are exited through Steam), no patch of Steam's menu: Exit Game is
Steam's own; Core adds its Exit only for a game Steam no longer tracks.

### Store launchers after a game (active, 3.3.5)

Like Playnite's *Auto Close Clients*: Epic Games Launcher, EA app, Ubisoft Connect, Battle.net, GOG Galaxy, Rockstar
Games Launcher and Amazon Games are closed after the game they were started for, when all of these hold: (a) the
launcher was not running when the game started, (b) no other game runs, (c) it is not busy. Launchers tell other
programs nothing about downloads, so Core watches what it can see: their disk writes (the kernel's per-process write
count) during a 90-second grace after the game; 32 MB or more means a download or update, and the launcher is left;
a running installer of that vendor also leaves it. The close is always polite (WM_CLOSE); Core never ends a launcher.
Per-launcher switches in Quick Access > Core > Controls and the desktop (Gaming Mode > Stays a console), all on by
default (`gaming.autoCloseLaunchers`, `{ "epic": false }` turns one off). The *Launcher pop-ups* row also says how many
Core closed today.

### Back paddles

L4 acts as **A** and R4 as **B** everywhere: Big Picture, Quick Access, Core's own UI, every game and emulator. This
belongs in Steam Input, which applies it to Big Picture (its Big Picture layout, app 413090), the desktop (413080) and the
games that inherit them, without touching a per-game layout. **Core does not write Steam's controller files:** Steam
rewrites them from memory, their format for the 2026 Steam Controller is not published, and an earlier attempt to touch
Steam's controller handling broke Steam Input. The agent cannot inject input either (that needs a kernel driver). So
Core **reads** the layouts read-only (`steamapps\common\Steam Controller Configs\<account>\config\413090|413080\*.vdf`,
the `button_back_left`/`button_back_right` bindings) and the *Back paddles* row says whether both map to A and B;
**Open Steam's controller layout** opens Steam's own editor (`steam://controllerconfig/413090`) to set them with the
controller. Inferred: the app ids and folder layout (checklist). Jarvis no longer uses L4 or R4 ([JARVIS.md](JARVIS.md)).

### Emulators (3.3.5)

For the emulators EmuDeck for Windows installs (`%APPDATA%\EmuDeck\Emulators`, located through EmuDeck's own launcher
scripts' `$emulatorFile`, then its folder names): RetroArch, Dolphin, PCSX2, DuckStation, PPSSPP, Azahar, RPCS3, Xenia
Canary, shadPS4, Cemu, xemu, Ryujinx. Read-only, at most once a minute, time-boxed. Settings files and keys are the ones
EmuDeck ships (`configs\*` in EmuDeck's source):

| Emulator | Starts fullscreen | No exit prompt | Shaders | RetroAchievements | BIOS / firmware |
| -------- | ----------------- | -------------- | ------- | ----------------- | --------------- |
| RetroArch | `video_fullscreen` | `quit_press_twice` = false | not offered | `cheevos_username` + token/password | PS1 (MD5), Sega CD, Saturn, Dreamcast (`bios\dc`), NDS (names) |
| Dolphin | `Dolphin.ini [Display] Fullscreen` | `[Interface] ConfirmStop` | `GFX.ini WaitForShadersBeforeStarting` (Compile Shaders Before Starting) | `RetroAchievements.ini` user + `ApiToken` | none needed |
| PCSX2 | `PCSX2.ini [UI] StartFullscreen` | `[UI] ConfirmShutdown` | shader cache on (`DisableShaderCache` = false) | user + `Token` (PCSX2.ini or secrets.ini) | PS2 (EmuDeck's MD5 list) |
| DuckStation | `settings.ini [Main] StartFullscreen` | `[Main] ConfirmPowerOff` | not offered | `[Cheevos]` user + token | PS1 (MD5) |
| PPSSPP | `ppsspp.ini [Graphics] FullScreen` | none | not offered | user + non-empty token file (size only) | none needed |
| Azahar | `qt-config.ini [UI] fullscreen` (Qt `\default` flag honoured) | `[UI] confirmClose` | `use_disk_shader_cache` | n/a | none needed |
| RPCS3 | `config.yml Start games in fullscreen mode` | not checked | not offered | n/a | firmware (`dev_flash\sys\external`) |
| Xenia Canary | `xenia-canary.config.toml [Display] fullscreen` | none | not offered | n/a | none needed |
| shadPS4 | `user\config.toml [General] Fullscreen` | none | not offered | n/a | none needed |
| Cemu | `settings.xml <fullscreen>` | none | not offered | n/a | none needed |
| xemu | `xemu.toml [display.window] fullscreen_on_startup` | none | not offered | n/a | the files `xemu.toml [sys.files]` names |
| Ryujinx | `Config.json start_fullscreen` | `show_confirm_exit` | `enable_shader_cache` | n/a | `prod.keys` + firmware (as EmuDeck's checker) |

- **RetroAchievements:** presence only, the same discipline as The Collector's parsers: the user name and token are
  tested for being non-empty inside the parser; neither value is returned, logged or shown (a test checks the rows).
- **BIOS:** EmuDeck's `functions\checkBIOS.ps1` lists. PS1 and PS2 are judged by the MD5 values EmuDeck accepts (files in
  `$biosPath` up to depth 3 and under 64 MB are hashed; only the verdict leaves), the others by file name. The version is
  the exe's version resource (no process is started), else EmuDeck's `emu_versions.json`.
- **Fix <emulator> settings** sets exactly the wrong keys (one line each; an absent key is added at the end of its
  section; CRLF, a BOM and comments are kept). Guard, for every write: the file is read again just before writing and
  must be byte-for-byte what was judged (*changed-since-read* refuses), the emulator must not run (*not-while-running*),
  and the old file is copied first to `%APPDATA%\GamingMode\emulator-backups\<emulator>\<time>\`. A file the emulator has
  not written yet is never created (*start it once first*).

### The shared emulator hotkeys (3.3.5)

One scheme, identical in every emulator that has the function, based on EmuDeck's own (Select is the hold modifier,
so muscle memory carries over), with every quit combo removed (EmuDeck's Select+Start): games are exited only through
Steam. **No back paddle (L4, R4, L5, R5) is ever used.** Buttons are named as on an Xbox pad, which is how Steam Input
presents every controller to an emulator.

| Function | Buttons |
| -------- | ------- |
| Save state | Select + R1 |
| Load state | Select + L1 |
| Next save slot | Select + D-pad right |
| Previous save slot | Select + D-pad left |
| Fast-forward | Select + R2 (hold) |
| Rewind | Select + L2 (hold) |
| Pause | Select + A |
| Emulator menu | Select + Y |
| FPS display | Select + X |
| Exit a game | Steam's Exit Game only (no combo) |
| L4 / R4 | A / B (Steam Input) |
| Jarvis | tap L5 + R5 to talk, hold to open |

The same table is in Quick Access > Core > Controls (`src/emulatorHotkeys.ts`; a test compares it with the agent's
`EmulatorCatalog.HotkeyTable`).

| Emulator | Written to | Can't do |
| -------- | ---------- | -------- |
| RetroArch | `retroarch.cfg` (hotkey enable = Select; button numbers for the xinput driver, or sdl2 when `input_joypad_driver` says so); quit combo and quit button cleared, screenshot moved off Select+A | rewind works only with RetroArch's rewind turned on |
| PCSX2 | `PCSX2.ini [Hotkeys]` (`SDL-0/Back & ...`; keyboard bindings kept; repeated keys allowed); `ShutdownVM` cleared, turbo/slow-motion toggles moved off Select+R2/L2 | rewind, FPS |
| DuckStation | `settings.ini [Hotkeys]` (same syntax); `PowerOff` cleared | FPS |
| Dolphin | `User\Config\Hotkeys.ini` (``@(Back+`Shoulder R`)``...); `General/Exit`, `General/Stop` and the speed-up/down on Select+R2/L2 cleared | rewind, menu, FPS |
| PPSSPP | `memstick\PSP\SYSTEM\controls.ini [ControlMapping]` (device 20, keyboard mappings kept); `Exit App` cleared | pause without its menu, FPS |
| Azahar, RPCS3 | nothing | everything: their hotkeys are keyboard-only |
| Xenia, shadPS4, Cemu, xemu, Ryujinx | nothing | everything: no save states |

**Why each emulator's own config, not Steam Input chords:** a Steam Input layout that sends each emulator's keyboard
shortcut would apply everywhere the same way, but Core cannot write Steam's controller files reliably (see Back
paddles), and keyboard shortcuts would make games and Big Picture show keyboard prompts, which breaks the rule that
button hints always show the controller. The emulators' own controller hotkeys keep prompts as they are.

**Guarded:** the *Hotkeys · <emulator>* row compares every key (and that no other binding sits on a reserved combo) and
offers **Re-apply hotkeys to <emulator>** when it drifted, with the same backup, unchanged-since-read and not-while-running
guard. Applied automatically on Core's first run, to an emulator Core has not seen before, and after an emulator update
replaced its files; a change you make later is shown, not undone. Switch: `gaming.emulatorHotkeysEnabled`. State:
`%APPDATA%\GamingMode\emulators.json`. The exact key names come from EmuDeck's shipped configs; that every emulator
version reads them the same way is on the checklist.

## Settings and files

- `%APPDATA%\GamingMode\config.json` (`gaming`): `consoleLauncherFocusGuard`, `consoleWakeChecks` (both on by default;
  Core > Gaming Mode > **Stays a console**).
- `%APPDATA%\GamingMode\console-guards.json`: the saved display and sound, expected controllers, the last wake check,
  launcher push-backs today, Core's last BitLocker suspension. **Forget normal display and sound** on the desktop
  clears the saved ones.
- `%LOCALAPPDATA%\Playhub\bitlocker-check.json`: the desktop's BitLocker check (type numbers and times only).
- 3.3.5: `gaming.gameExitCleanup`, `gaming.autoCloseLaunchers`, `gaming.emulatorHotkeysEnabled`, `gaming.emulatorUpdatesEnabled`,
  `gaming.emulatorUpdateSoakDays`; files `game-exit.json`, `emulators.json`, `emulator-updates.json`, `emulator-backups\`. Rules:
  `EmulatorHealth.cs`, `EmulatorUpdates.cs`, `GameExit.cs`, `StreamProfiles.cs` (pure; tests in `Source/CoreHealth.Tests/EmulatorTests.cs`);
  Windows calls `Emulators.Windows.cs`; wiring `MarkIIIHost.Emulators.cs`; Quick Access `src/ControlsSection.tsx`,
  `src/emulatorHotkeys.ts`, `src/gameExit.ts`, `healthReport.ts` (`tests/coreEmulators.test.mjs`).
- Agent: `POST /health/action?id=` on 127.0.0.1:47991 (`MarkIIIHost.Console.cs`) runs the buttons and answers with
  the new Health rows. Rules: `ConsoleGuards.cs` (pure, tests in `Source/CoreHealth.Tests/ConsoleGuardTests.cs`);
  Windows calls: `ConsoleGuards.Windows.cs`; BitLocker's elevated parts: `Shared/BitLockerGuard*.cs`; Quick Access:
  `healthReport.ts` and `HealthSection.tsx` (`tests/consoleGuards.test.mjs`).

## At the TV (checklist)

1. Quick Access > Core > Health: the new rows sit between *Microphone* and *PC disk space*.
2. **Display:** open the row, press **Save current display as normal** (4K, 120 Hz, HDR on, the LG primary). Change the
   refresh rate on the desktop, come back: the row is crimson; **Restore display settings** brings it back.
3. **Sound output:** open the row, press **Save current sound output as normal** with the LG selected. Switch the
   default to another device: **Switch sound back** returns it.
4. **Controllers:** open the row; the Steam Controller/Puck, DualSense, Xbox pad, 8BitDo and, with a Wii remote on,
   *Wii Remote (DolphinBar)* appear. **Mark these controllers as expected**, then switch one off: the row turns amber,
   no notice.
5. **Steam sign-in:** shows your persona name. **Automatic sign-in:** *On · <user>*.
6. **BitLocker:** on the desktop press *Check BitLocker* (one prompt); the row says whether a recovery key exists.
   Confirm the key is at `account.microsoft.com/devices/recoverykey`. Optionally *Turn on BitLocker protection*.
7. **Windows setup screens:** if *On*, press **Turn off setup screens**. **Update policy:** if *Not applied*, press
   Apply in Quiet Windows on the desktop (one prompt); the row then names the pinned version (or Home).
8. **Launcher pop-ups:** with Big Picture in front, open the Epic launcher from the desktop shortcut or let it pop up:
   it drops behind Big Picture and the controller still drives Big Picture. Bring it back three times: Core leaves it.
9. **After waking:** sleep the PC, switch the TV to another input, wake with the controller: about 35 seconds later the
   TV is back on HDMI 2 (if Core turned it on) and the row says *Fixed by Core*. Unplug the Ethernet cable: the row
   says *Network cable unplugged* (and a notice appears once).
10. **Exiting games (3.3.5):** start an EmuDeck game from its Steam shortcut (PCSX2), choose Exit Game in Steam: PCSX2
   closes within ~8 s and the *Exiting games* row says *closed cleanly* or *Core closed N leftovers*. Start a game whose
   wrapper exits at once (a `.bat` that `start`s an emulator): Steam's Exit Game disappears; the row turns crimson
   *Steam lost track of ...* and **Exit ...** closes it. A Collector ROM shortcut: Exit Game closes it, nothing left over.
11. **Store launchers:** with Epic closed, start an Epic game from Steam, quit it: about 90 s later Epic closes. Start
   Epic first, then the game: Epic stays. Switch Epic off in Controls: it stays.
12. **Back paddles:** the row says whether L4/R4 are A/B in Steam's Big Picture and Desktop layouts; **Open Steam's
   controller layout** opens Steam's editor (confirm it is the Big Picture layout; set L4 = A, R4 = B); the row turns
   steel. Confirm in a game and an emulator that L4 confirms and R4 goes back.
13. **Emulators:** each installed emulator has a row and a *Hotkeys* row. Turn PCSX2's *Start fullscreen* off in PCSX2,
   close it: amber; **Fix PCSX2 settings** fixes it and a backup appears under `emulator-backups\pcsx2`. Open PCSX2 and
   press Fix: *Not while a game runs*. In every emulator with hotkeys, try each line of the table; Select+Start does
   nothing. RetroAchievements and BIOS lines match what you set up.
