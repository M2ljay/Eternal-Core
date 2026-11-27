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
	public bool ConsoleWakeChecks { get; set; } = true;

	// Core Voice (MarkIIIHost.Voice.cs), config-only. A keyboard combination that toggles the
	// microphone mute (bind it to a Steam Input chord); empty turns it off. An ms-gamebar: URI
	// the Xbox party button opens instead of Win+G, for a widget link found to work on this PC.
	public string? MicMuteHotkey { get; set; } = "Ctrl+Alt+M";
	public string? VoicePartyUri { get; set; }

	public bool ManageAudio { get; set; }
}
