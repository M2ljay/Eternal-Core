using System.Collections.Generic;

namespace GamingMode.Models;

public sealed class GamingSettings
{
	public string? SteamPath { get; set; }

	public string SteamArguments { get; set; } = "-gamepadui";

	public string? DeckyPath { get; set; }

	public string? SunshinePath { get; set; }

	public bool DeckyRequired { get; set; } = true;

	public bool SunshineRequired { get; set; } = true;

	public int DelaySteamAfterDeckyMs { get; set; } = 1500;

	public bool CloseExplorerInGamingMode { get; set; }

	public bool AllowExplorerCloseInGamingMode { get; set; }

	public bool RestoreExplorerOnDesktop { get; set; } = true;

	public bool RestoreStartupAppsOnDesktop { get; set; }

	public bool OpenSteamDesktopOnInteractiveDesktopMode { get; set; }

	public bool EnsureInputCompatibilityInGamingMode { get; set; } = true;

	public bool EnsureSunshineCompatibilityInGamingMode { get; set; } = true;

	public bool EnsureHandheldVendorCompatibilityInGamingMode { get; set; } = true;

	public bool AutoHideMouseCursorInGamingMode { get; set; } = true;

	// Playhub owns this value; this fallback only applies to a config written before the
	// setting existed, and must match the app default in Playhub/Models/GamingModeConfig.cs.
	public int AutoHideMouseCursorAfterMs { get; set; } = 500;

	public bool BorderlessFullscreenWindowsInGamingMode { get; set; } = true;

	// ---------- ANTI-CHEAT SAFE MODE (Eternal Core) ----------
	//
	// While a game protected by a kernel anti-cheat runs, Gaming Mode pauses the
	// volume-key hook, borderless/foreground handling, cursor auto-hide and game
	// power QoS. See AntiCheatProtection.cs. Defaults must match the app's
	// Playhub/Models/GamingModeConfig.cs.
	public bool AntiCheatSafeModeEnabled { get; set; } = true;

	public bool AntiCheatTreatEveryGameAsProtected { get; set; }

	// Executable names ("cod.exe") always treated as protected.
	public List<string> AntiCheatProtectedExecutables { get; set; } = new List<string>();

	// Config-only, for bisecting which feature an anti-cheat reacts to: "qos",
	// "window", "volume", "cursor". Null or empty pauses all four.
	public List<string>? AntiCheatPausedFeatures { get; set; }

	public List<GamingStartupApp> CustomStartupApps { get; set; } = new List<GamingStartupApp>();

	// ---------- PLAYHUB DASHBOARD ----------
	//
	// La Dashboard e' una schermata del plugin di Steam. Qui restano solo le
	// cose che il plugin non puo' sapere da solo.

	// Le sue scorciatoie (le "preferite" nella schermata Home).
	public List<GamingOverlayShortcut> DashboardShortcuts { get; set; } = new List<GamingOverlayShortcut>();

	// Interruttore principale condiviso fra Playhub, agente e plugin Decky.
	// Rimane attivo per compatibilita' con le installazioni precedenti.
	public bool DashboardEnabled { get; set; } = true;

	// Scorciatoia da tastiera per aprirla. Serve perche' mentre un gioco e' in
	// primo piano l'interfaccia di Steam non riceve nulla. Chi la vuole aprire
	// col pad lega questa stessa combinazione a un accordo DENTRO Steam: cosi'
	// e' Steam a premere i tasti e nessuno tocca il controller.
	public bool DashboardKeyboardShortcutEnabled { get; set; } = true;

	public string DashboardHotkey { get; set; } = "Ctrl+Alt+P";

	public bool NavigationHapticsEnabled { get; set; }

	public int NavigationHapticsIntensity { get; set; } = 55;

	// Quando attivo, Playhub avvia i programmi desktop con i riferimenti SDL
	// necessari al riconoscimento nativo dello Steam Controller.
	public bool Sdl3NativeControllerEnabled { get; set; }

	public bool SteamControllerTranslationEnabled { get; set; } = true;
	public string SteamControllerTranslationRevision { get; set; } = "";

	public GamingSplashSettings Splash { get; set; } = new GamingSplashSettings();

	// ---------- MARK III (Eternal Core) ----------
	// Defaults must match the app's Playhub/Models/GamingModeConfig.cs.

	// LG webOS TV control: see TvSettings and GamingMode.Services/TvControl.cs.
	public TvSettings Tv { get; set; } = new TvSettings();

	// The calm notice over Big Picture when Decky's UI did not load (DeckyHeartbeat.cs).
	public bool DeckyHealthNoticeEnabled { get; set; } = true;

	// Windows notification banners off while Gaming Mode runs, back for the desktop
	// (Shared/CouchHardening.cs, GamingToasts).
	public bool QuietWindowsToasts { get; set; } = true;

	// Core Health (MarkIIIHost.Health.cs): the home server (Aincrad) whose Home Assistant serves
	// /local/core-status.json for Core to read, and whether serious problems raise a Core notice in Gaming Mode.
	public string? CoreHealthServer { get; set; }
	public bool CoreHealthAlertsEnabled { get; set; } = true;

	// Steam updates (SteamUpdateHold.cs, docs/eternal/STEAM-UPDATES.md): how many days a pending Steam client
	// version waits before Core updates Steam on an update night (1-60).
	public int SteamUpdateSoakDays { get; set; } = 10;

	// Console guards (ConsoleGuards.cs, docs/eternal/CONSOLE-GUARDS.md): launcher windows that pop over Big Picture
	// are pushed back behind it (never closed), and Big Picture, the TV input and the network are checked after a wake.
	public bool ConsoleLauncherFocusGuard { get; set; } = true;
	// 3.3.5 (docs/eternal/CONSOLE-GUARDS.md, EMULATOR-UPDATES.md). Emulator health is read-only except its one-tap fixes and the
	// shared hotkey scheme, which Core applies on its first run, to a new emulator and after an emulator update.
	public bool EmulatorHotkeysEnabled { get; set; } = true;
	public bool EmulatorUpdatesEnabled { get; set; } = true;
	public int EmulatorUpdateSoakDays { get; set; } = 10;
	// After Steam ends a game (or Exit Game is chosen), Core closes what the launch left running.
	public bool GameExitCleanup { get; set; } = true;
	// Close a store launcher after the game it was started for exits; per launcher id ("epic", "ea", "ubisoft",
	// "battlenet", "gog", "rockstar", "amazon"), on unless set to false here.
	public Dictionary<string, bool>? AutoCloseLaunchers { get; set; }
	public bool ConsoleWakeChecks { get; set; } = true;

	// Batch 1 (docs/eternal/BATCH-1.md). Save backup: before the update-night restart and an automatic Steam update,
	// Ludusavi backs up every game's saves (only when Ludusavi is installed; a failure never blocks the update).
	// Wind-down: calm "good moment to save" reminders while a game runs, MinutesBefore (comma-separated) before
	// Bedtime (HH:mm). Completed: games at 100% achievements join the Steam collection "Completed"; Create makes
	// that collection when it does not exist yet (otherwise nothing is created).
	public bool SaveBackupBeforeUpdates { get; set; } = true;
	public bool WindDownEnabled { get; set; } = true;
	public string WindDownBedtime { get; set; } = "04:00";
	public string WindDownMinutes { get; set; } = "30, 10";
	public bool AutoCompletedEnabled { get; set; } = true;
	public bool AutoCompletedCreate { get; set; }

	// Batch 2 (docs/eternal/BATCH-2.md). Home Assistant: Core's state over MQTT with discovery (the password is not here:
	// it is DPAPI-sealed in home-assistant-secret.json, Shared/CoreSecretStore.cs). Automations: trigger -> action rules
	// (Shared/CoreAutomations.cs; null means the defaults). Stream sleep: sleep the PC this many minutes after a handheld's
	// stream ends when nobody uses Core here. Swipe typing: VaporType on Steam's keyboard (Decky plugin).
	public bool HomeAssistantEnabled { get; set; }
	public string HomeAssistantHost { get; set; } = "serverv3";
	public int HomeAssistantPort { get; set; } = 1883;
	public string? HomeAssistantUser { get; set; }
	public List<Playhub.Shared.CoreAutomationRule>? Automations { get; set; }
	public bool StreamSleepEnabled { get; set; } = true;
	public int StreamSleepMinutes { get; set; } = 10;
	public bool SwipeTypingEnabled { get; set; } = true;

	// Jarvis (docs/eternal/JARVIS.md, MarkIIIHost.Jarvis.cs). Edited in the desktop app's Jarvis card; the token and keys
	// are not here: they are DPAPI-sealed in jarvis-secrets.json (Shared/JarvisSecretStore.cs). Combos: two to four
	// controller buttons held together ("L5+R5"; empty turns one off), detected by Core's Decky plugin.
	public bool JarvisEnabled { get; set; } = true;
	public string JarvisBackend { get; set; } = "home-assistant";
	public string JarvisHaUrl { get; set; } = "http://serverv3:8123";
	public string? JarvisHaAgent { get; set; } = "conversation.jarvis";
	public string JarvisModel { get; set; } = "claude-haiku-5-5";
	// 3.3.5: dictation and the screen helper have no combo (L4/R4 are A and B); kept so old files still load, ignored.
	public string? JarvisDictateCombo { get; set; } = "";
	public string? JarvisTalkCombo { get; set; } = "L5+R5";
	public string? JarvisScreenCombo { get; set; } = "";
	// 3.3.5: a tap (under about 300 ms) on L5+R5 talks (listening ends on a pause); with this on, holding it opens or closes
	// Jarvis's Quick Access tab, with it off holding it is push-to-talk. The key keeps its old name so settings carry over.
	public bool JarvisTapOpensQuickAccess { get; set; } = true;
	public bool JarvisSpoilerSafe { get; set; } = true;
	public bool JarvisVoice { get; set; } = true;
	public bool JarvisSpeakScreen { get; set; }
	public bool JarvisCloudWhisper { get; set; }
	// Config-only: where Aincrad's Whisper and Piper are, and the Home Assistant entities for them.
	public string JarvisWyomingHost { get; set; } = "serverv3";
	public int JarvisWyomingPort { get; set; } = 10300;
	public string? JarvisSttEntity { get; set; } = "stt.faster_whisper";
	public string? JarvisTtsEntity { get; set; } = "tts.piper";
	public string? JarvisOpenAiUrl { get; set; } = "https://api.openai.com/v1";

	// Core Voice (MarkIIIHost.Voice.cs), config-only. A keyboard combination that toggles the
	// microphone mute (bind it to a Steam Input chord); empty turns it off. An ms-gamebar: URI
	// the Xbox party button opens instead of Win+G, for a widget link found to work on this PC.
	public string? MicMuteHotkey { get; set; } = "Ctrl+Alt+M";
	public string? VoicePartyUri { get; set; }

	public bool ManageAudio { get; set; }
}
