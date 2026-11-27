using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core: "stays a console". Checks and guards that keep the living-room PC from asking for a
// keyboard, a mouse or a password, or from quietly breaking the TV picture: BitLocker, automatic sign-in,
// the display and sound the user called normal, the controllers Windows sees, Steam's sign-in, Windows'
// setup screens and update policy, launcher updates and pop-ups, and the checks after a wake.
// Pure: observations in, Core Health rows and problems out (CoreHealthPolicy.Evaluate inserts them).
// The agent gathers the observations (ConsoleGuards.Windows.cs, MarkIIIHost.Console.cs) and the Quick
// Access Health section renders them (src/healthReport.ts). See docs/eternal/CONSOLE-GUARDS.md.

// ---------- observations ----------

// BitLocker's elevated check, run once from the desktop app (the only part that needs administrator
// rights): which key protector types exist, never a key or a protector ID. Types follow
// Win32_EncryptableVolume.GetKeyProtectorType: 1 TPM, 2 external key, 3 recovery password (the
// 48-digit recovery key), 4 TPM+PIN, 5 TPM+startup key, 6 TPM+PIN+startup key, 7 public key,
// 8 passphrase, 9 TPM certificate, 10 SID.
internal sealed record BitLockerCheck(DateTimeOffset CheckedAt, bool Found, int? ProtectionStatus, IReadOnlyList<int> Protectors, DateTimeOffset? BackupSeenAt)
{
	internal bool HasRecoveryPassword => Protectors.Contains(3);
}

internal sealed record AutoSignInState(bool Enabled, string? UserName, bool PlainTextPassword, int? AutoLogonCount, bool PasswordlessOnly);

// A display as Core saves it: the target's stable path (DisplayConfig monitorDevicePath), the GDI name it
// had, the monitor's own name (the LG reports "LG TV SSCR2"), the mode, HDR and the Windows VRR switch.
internal sealed record DisplayProfile(string? Path, string? Device, string? Monitor, int Width, int Height, int RefreshHz, bool? Hdr, bool Primary, bool? Vrr);

internal sealed record SoundDevice(string Id, string? Name);

// A game controller Windows lists: its catalogue kind, a readable name and VID:PID (hex, upper case).
internal sealed record ControllerSeen(string Kind, string Name, string Key);

internal sealed record SteamSignIn(bool Running, bool? SignedIn, string? PersonaName, bool? RememberPassword, bool? AllowAutoLogin, bool OfflineMode);

internal sealed record UpdatePolicyState(string? Edition, bool Home, bool DriversBlocked, bool FeaturePinned, string? PinnedVersion, string? CurrentVersion);

internal sealed record WakeCheckResult
{
	public DateTimeOffset At { get; init; }
	public bool? BigPictureUp { get; init; }
	public bool FocusRetaken { get; init; }
	public bool? TvOnCore { get; init; }
	public bool InputResent { get; init; }
	public bool? NetworkUp { get; init; }
}

internal sealed record ConsoleGuardInputs
{
	// BitLocker: Windows' own answer for the system drive (no administrator rights needed), the desktop's
	// elevated check, whether the one-restart suspend task exists, a staged firmware update, and whether
	// Core asked for a one-restart suspension since the last boot.
	public string? BitLockerProtection { get; init; }
	public BitLockerCheck? BitLockerCheck { get; init; }
	public bool BitLockerGuardInstalled { get; init; }
	public IReadOnlyList<string> FirmwarePending { get; init; } = Array.Empty<string>();
	public DateTimeOffset? BitLockerSuspendedByCoreAt { get; init; }
	public AutoSignInState? AutoSignIn { get; init; }
	public DisplayProfile? DisplaySaved { get; init; }
	public DisplayProfile? DisplayCurrent { get; init; }
	public bool DisplayRead { get; init; }
	public SoundDevice? SoundSaved { get; init; }
	public SoundDevice? SoundCurrent { get; init; }
	public IReadOnlyList<string> SoundAvailable { get; init; } = Array.Empty<string>();
	public bool StreamActive { get; init; }
	public IReadOnlyList<ControllerSeen>? Controllers { get; init; }
	public IReadOnlyList<string> ExpectedControllers { get; init; } = Array.Empty<string>();
	public SteamSignIn? Steam { get; init; }
	public bool? PromptsOff { get; init; }
	public UpdatePolicyState? UpdatePolicy { get; init; }
	public IReadOnlyList<string> PendingLauncherUpdates { get; init; } = Array.Empty<string>();
	public bool LauncherFocusEnabled { get; init; }
	public int LauncherPushedToday { get; init; }
	public string? LauncherLastPushed { get; init; }
	public bool WakeChecksEnabled { get; init; }
	public WakeCheckResult? LastWake { get; init; }
	public bool? EthernetUp { get; init; }
	public string? EthernetName { get; init; }
}

// ---------- verdicts ----------

internal static class ConsoleGuardPolicy
{
	// The order the rows appear in Quick Access (after the microphone, before the PC's disk).
	internal static readonly string[] RowOrder = { "display", "sound-output", "controllers", "steam-sign-in", "auto-sign-in", "bitlocker", "windows-prompts", "update-policy", "launcher-updates", "launcher-focus", "wake-check" };

	// The one-button fixes Quick Access can run (POST /health/action?id=), and the secondary actions
	// shown when a row is open. Every other fix is a one-line instruction for the desktop.
	internal static readonly string[] Actions = { "display-restore", "display-save", "sound-restore", "sound-save", "controllers-expect", "prompts-off", "bitlocker-suspend", "wake-check-now" };

	internal static IReadOnlyList<HealthRow> Rows(ConsoleGuardInputs input, DateTimeOffset now, List<HealthProblem> problems)
	{
		var rows = new List<HealthRow>
		{
			Display(input, problems),
			Sound(input, problems),
			Controllers(input),
			Steam(input),
			AutoSignIn(input, problems),
			BitLocker(input, problems),
			Prompts(input),
			UpdatePolicy(input),
			LauncherUpdates(input),
			LauncherFocus(input),
			Wake(input, now, problems),
		};
		return rows;
	}

	private static Dictionary<string, object?> Data(params (string Key, object? Value)[] values)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach ((string key, object? value) in values) data[key] = value;
		return data;
	}

	// ---------- display ----------

	internal static IReadOnlyList<string> DisplayDifferences(DisplayProfile saved, DisplayProfile current)
	{
		var differences = new List<string>();
		if (saved.Width != current.Width || saved.Height != current.Height) differences.Add("resolution");
		// Windows reports 59 or 60 for the same 59.94 mode depending on the API; 1 Hz is not a change.
		if (Math.Abs(saved.RefreshHz - current.RefreshHz) > 1) differences.Add("refresh");
		if (saved.Hdr is bool hdr && current.Hdr is bool now && hdr != now) differences.Add("hdr");
		if (saved.Primary && !current.Primary) differences.Add("primary");
		if (saved.Vrr is bool vrr && current.Vrr is bool vrrNow && vrr != vrrNow) differences.Add("vrr");
		return differences;
	}

	// The saved display among the current ones: by its stable path first, then by its GDI name, then by
	// the monitor's own name.
	internal static DisplayProfile? Match(DisplayProfile saved, IReadOnlyList<DisplayProfile> current)
	{
		if (!string.IsNullOrEmpty(saved.Path) && current.FirstOrDefault(display => string.Equals(display.Path, saved.Path, StringComparison.OrdinalIgnoreCase)) is { } byPath) return byPath;
		if (!string.IsNullOrEmpty(saved.Device) && current.FirstOrDefault(display => string.Equals(display.Device, saved.Device, StringComparison.OrdinalIgnoreCase)
			&& (saved.Monitor is null || display.Monitor is null || string.Equals(display.Monitor, saved.Monitor, StringComparison.OrdinalIgnoreCase))) is { } byDevice) return byDevice;
		if (!string.IsNullOrEmpty(saved.Monitor) && current.Where(display => string.Equals(display.Monitor, saved.Monitor, StringComparison.OrdinalIgnoreCase)).ToList() is { Count: 1 } byName) return byName[0];
		return null;
	}

	private static HealthRow Display(ConsoleGuardInputs input, List<HealthProblem> problems)
	{
		DisplayProfile? saved = input.DisplaySaved, current = input.DisplayCurrent;
		var data = Data(("saved", Describe(saved)), ("current", Describe(current)), ("monitor", saved?.Monitor ?? current?.Monitor),
			("vrrReadable", (saved ?? current)?.Vrr is not null), ("actions", new[] { "display-save" }));
		if (!input.DisplayRead) return new HealthRow("display", HealthLevel.Unknown, "unknown", data);
		if (saved is null) return new HealthRow("display", HealthLevel.Info, "not-saved", data);
		// The TV switched off or on another input disappears from Windows' list: not a change to undo.
		if (current is null) return new HealthRow("display", HealthLevel.Info, "missing", data);
		IReadOnlyList<string> differences = DisplayDifferences(saved, current);
		data["differences"] = differences;
		if (differences.Count == 0) return new HealthRow("display", HealthLevel.Ok, "normal", data);
		data["fix"] = "display-restore";
		problems.Add(new HealthProblem("display-changed", "display", "display|" + string.Join(",", differences) + "|" + Describe(current), "console.displayChanged"));
		return new HealthRow("display", HealthLevel.Attention, "changed", data);
	}

	internal static string? Describe(DisplayProfile? profile) => profile is null ? null
		: string.Create(CultureInfo.InvariantCulture, $"{profile.Width}x{profile.Height} {profile.RefreshHz} Hz{(profile.Hdr == true ? " HDR" : "")}{(profile.Primary ? " primary" : "")}");

	// ---------- sound ----------

	private static HealthRow Sound(ConsoleGuardInputs input, List<HealthProblem> problems)
	{
		var data = Data(("saved", input.SoundSaved?.Name), ("current", input.SoundCurrent?.Name), ("actions", new[] { "sound-save" }));
		if (input.SoundSaved is not { } saved) return new HealthRow("sound-output", input.SoundCurrent is null ? HealthLevel.Unknown : HealthLevel.Info, input.SoundCurrent is null ? "unknown" : "not-saved", data);
		// Stream mode keeps the PC's sound off the TV on purpose (CoreStreamMode.cs): never argue with it.
		if (input.StreamActive) return new HealthRow("sound-output", HealthLevel.Info, "stream-mode", data);
		if (input.SoundCurrent is { } current && string.Equals(current.Id, saved.Id, StringComparison.Ordinal)) return new HealthRow("sound-output", HealthLevel.Ok, "normal", data);
		// The TV's HDMI audio vanishes while the TV is off: nothing to switch back to.
		if (!input.SoundAvailable.Contains(saved.Id, StringComparer.Ordinal)) return new HealthRow("sound-output", HealthLevel.Info, "missing", data);
		data["fix"] = "sound-restore";
		problems.Add(new HealthProblem("sound-changed", "sound-output", "sound|" + (input.SoundCurrent?.Id ?? "none"), "console.soundChanged"));
		return new HealthRow("sound-output", HealthLevel.Attention, "changed", data);
	}

	// ---------- controllers (information only) ----------

	private static HealthRow Controllers(ConsoleGuardInputs input)
	{
		if (input.Controllers is not { } seen) return new HealthRow("controllers", HealthLevel.Unknown, "unknown", Data());
		var missing = input.ExpectedControllers.Where(key => !seen.Any(controller => string.Equals(controller.Key, key, StringComparison.OrdinalIgnoreCase))).ToList();
		var data = Data(("controllers", seen.Select(controller => new Dictionary<string, object?> { ["kind"] = controller.Kind, ["name"] = controller.Name, ["key"] = controller.Key }).ToArray()),
			("count", seen.Count), ("missing", missing), ("actions", new[] { "controllers-expect" }));
		// No alarms: a pad that sleeps or charges elsewhere is normal. Missing expected pads tint the row amber.
		if (missing.Count > 0) return new HealthRow("controllers", HealthLevel.Info, "expected-missing", data);
		return new HealthRow("controllers", seen.Count == 0 ? HealthLevel.Info : HealthLevel.Ok, seen.Count == 0 ? "none" : "seen", data);
	}

	// ---------- Steam sign-in ----------

	private static HealthRow Steam(ConsoleGuardInputs input)
	{
		if (input.Steam is not { } steam) return new HealthRow("steam-sign-in", HealthLevel.Unknown, "unknown", Data());
		var data = Data(("persona", steam.PersonaName), ("rememberPassword", steam.RememberPassword), ("allowAutoLogin", steam.AllowAutoLogin), ("offline", steam.OfflineMode));
		if (!steam.Running) return new HealthRow("steam-sign-in", HealthLevel.Info, "not-running", data);
		if (steam.SignedIn == false) return new HealthRow("steam-sign-in", HealthLevel.Attention, "signed-out", data);
		if (steam.SignedIn != true) return new HealthRow("steam-sign-in", HealthLevel.Unknown, "unknown", data);
		// Signed in now, but the next start would ask for the password.
		if (steam.RememberPassword == false || steam.AllowAutoLogin == false) return new HealthRow("steam-sign-in", HealthLevel.Attention, "no-remember", data);
		return new HealthRow("steam-sign-in", HealthLevel.Ok, steam.OfflineMode ? "offline" : "signed-in", data);
	}

	// ---------- automatic sign-in ----------

	private static HealthRow AutoSignIn(ConsoleGuardInputs input, List<HealthProblem> problems)
	{
		if (input.AutoSignIn is not { } sign) return new HealthRow("auto-sign-in", HealthLevel.Unknown, "unknown", Data());
		// The user name is shown; the password is never read, only whether Windows keeps one in plain text.
		var data = Data(("user", sign.UserName), ("plainTextPassword", sign.PlainTextPassword), ("count", sign.AutoLogonCount), ("guide", "autologon"));
		if (!sign.Enabled || string.IsNullOrWhiteSpace(sign.UserName))
		{
			problems.Add(new HealthProblem("auto-sign-in-off", "auto-sign-in", "off", "console.autoSignIn"));
			return new HealthRow("auto-sign-in", HealthLevel.Attention, "off", data);
		}
		if (sign.AutoLogonCount is not null) return new HealthRow("auto-sign-in", HealthLevel.Attention, "expires", data);
		if (sign.PasswordlessOnly) return new HealthRow("auto-sign-in", HealthLevel.Attention, "passwordless", data);
		return new HealthRow("auto-sign-in", HealthLevel.Ok, "on", data);
	}

	// ---------- BitLocker ----------

	// System.Volume.BitLockerProtection, read through the Shell without administrator rights. Microsoft does
	// not document the values; these are the ones observed in the field (Explorer shows the same state).
	internal static string BitLockerFromShell(int? value) => value switch
	{
		1 => "on",
		2 => "off",
		3 => "encrypting",
		4 => "decrypting",
		5 => "suspended",
		6 => "on", // on and locked (a data drive); the system drive is never locked while Windows runs
		8 => "waiting", // device encryption waiting for activation: not protected until a key is backed up
		_ => "unknown",
	};

	private static HealthRow BitLocker(ConsoleGuardInputs input, List<HealthProblem> problems)
	{
		BitLockerCheck? check = input.BitLockerCheck;
		var data = Data(("protection", input.BitLockerProtection), ("checkedAt", check?.CheckedAt), ("recoveryKey", check?.HasRecoveryPassword),
			("backupSeenAt", check?.BackupSeenAt), ("guard", input.BitLockerGuardInstalled), ("firmware", input.FirmwarePending),
			("confirmAt", "account.microsoft.com/devices/recoverykey"));
		string protection = input.BitLockerProtection ?? "unknown";
		if (protection == "unknown") return new HealthRow("bitlocker", HealthLevel.Unknown, "unknown", data);
		if (protection is "off" or "decrypting") return new HealthRow("bitlocker", HealthLevel.Ok, "off", data);
		if (protection == "waiting") return new HealthRow("bitlocker", HealthLevel.Info, "waiting", data);
		bool firmware = input.FirmwarePending.Count > 0;
		bool coreSuspended = input.BitLockerSuspendedByCoreAt is not null;
		if (protection == "suspended")
			return coreSuspended || firmware ? new HealthRow("bitlocker", HealthLevel.Info, "suspended-for-update", data)
				: new HealthRow("bitlocker", HealthLevel.Attention, "suspended", data);
		// On (or encrypting): a firmware update staged for the next restart would ask for the recovery key.
		if (firmware && !coreSuspended)
		{
			string fingerprint = "firmware|" + string.Join(",", input.FirmwarePending);
			if (input.BitLockerGuardInstalled)
			{
				data["fix"] = "bitlocker-suspend";
				return new HealthRow("bitlocker", HealthLevel.Attention, "firmware-pending", data);
			}
			problems.Add(new HealthProblem("bitlocker-firmware", "bitlocker", fingerprint, "console.bitlockerFirmware"));
			return new HealthRow("bitlocker", HealthLevel.Attention, "firmware-no-guard", data);
		}
		if (check is null || !check.Found) return new HealthRow("bitlocker", HealthLevel.Info, "unchecked", data);
		if (!check.HasRecoveryPassword) return new HealthRow("bitlocker", HealthLevel.Attention, "no-recovery-key", data);
		return new HealthRow("bitlocker", HealthLevel.Ok, protection == "encrypting" ? "encrypting" : "on", data);
	}

	// ---------- Windows setup screens, update policy, launcher updates ----------

	private static HealthRow Prompts(ConsoleGuardInputs input)
	{
		var data = Data(("desktop", "quiet-windows"));
		if (input.PromptsOff is not bool off) return new HealthRow("windows-prompts", HealthLevel.Unknown, "unknown", data);
		if (off) return new HealthRow("windows-prompts", HealthLevel.Ok, "off", data);
		data["fix"] = "prompts-off";
		return new HealthRow("windows-prompts", HealthLevel.Info, "on", data);
	}

	private static HealthRow UpdatePolicy(ConsoleGuardInputs input)
	{
		if (input.UpdatePolicy is not { } policy) return new HealthRow("update-policy", HealthLevel.Unknown, "unknown", Data());
		var data = Data(("edition", policy.Edition), ("home", policy.Home), ("driversBlocked", policy.DriversBlocked), ("featurePinned", policy.FeaturePinned),
			("pinnedVersion", policy.PinnedVersion), ("currentVersion", policy.CurrentVersion), ("desktop", "quiet-windows"));
		// Windows Home ignores the feature-update pin (TargetReleaseVersion): only the driver half applies there.
		bool applied = policy.DriversBlocked && (policy.Home || policy.FeaturePinned);
		if (!applied) return new HealthRow("update-policy", HealthLevel.Info, "not-applied", data);
		return new HealthRow("update-policy", HealthLevel.Ok, policy.Home ? "applied-home" : "applied", data);
	}

	private static HealthRow LauncherUpdates(ConsoleGuardInputs input)
	{
		var data = Data(("pending", input.PendingLauncherUpdates));
		return input.PendingLauncherUpdates.Count == 0 ? new HealthRow("launcher-updates", HealthLevel.Ok, "none", data)
			: new HealthRow("launcher-updates", HealthLevel.Info, "pending", data);
	}

	private static HealthRow LauncherFocus(ConsoleGuardInputs input)
	{
		var data = Data(("pushedToday", input.LauncherPushedToday), ("last", input.LauncherLastPushed));
		if (!input.LauncherFocusEnabled) return new HealthRow("launcher-focus", HealthLevel.Info, "off", data);
		return new HealthRow("launcher-focus", HealthLevel.Ok, "watching", data);
	}

	// ---------- wake checks ----------

	private static HealthRow Wake(ConsoleGuardInputs input, DateTimeOffset now, List<HealthProblem> problems)
	{
		WakeCheckResult? wake = input.LastWake;
		var data = Data(("at", wake?.At), ("bigPicture", wake?.BigPictureUp), ("focusRetaken", wake?.FocusRetaken), ("tvOnCore", wake?.TvOnCore),
			("inputResent", wake?.InputResent), ("network", input.EthernetUp), ("adapter", input.EthernetName), ("temperatures", "skipped"),
			("actions", new[] { "wake-check-now" }));
		// The network matters whether or not there was a wake: no Ethernet, no Steam, no server.
		if (input.EthernetUp == false)
		{
			problems.Add(new HealthProblem("network-down", "wake-check", "network|" + (input.EthernetName ?? "?"), "console.network"));
			return new HealthRow("wake-check", HealthLevel.Attention, "network-down", data);
		}
		if (!input.WakeChecksEnabled) return new HealthRow("wake-check", HealthLevel.Info, "off", data);
		if (wake is null) return new HealthRow("wake-check", HealthLevel.Ok, "none-yet", data);
		bool stillWrong = wake.BigPictureUp == false || wake.TvOnCore == false || wake.NetworkUp == false;
		if (stillWrong) return new HealthRow("wake-check", HealthLevel.Attention, "problem", data);
		if (wake.FocusRetaken || wake.InputResent) return new HealthRow("wake-check", HealthLevel.Info, "fixed", data);
		return new HealthRow("wake-check", HealthLevel.Ok, "ok", data);
	}
}

// ---------- BitLocker: firmware staged for the next restart ----------

internal static class FirmwarePolicy
{
	// Windows stages a UEFI capsule (a BIOS/firmware update from Windows Update) under
	// HKLM\SYSTEM\CurrentControlSet\Control\FirmwareResources\{GUID} with the staged "Version"; the
	// installed version of the same resource is in the ESRT, HKLM\HARDWARE\UEFI\ESRT\{GUID} "Version".
	// A staged version newer than the installed one is applied at the next restart.
	internal static IReadOnlyList<string> Pending(IReadOnlyDictionary<string, long> staged, IReadOnlyDictionary<string, long> installed)
	{
		static string Id(string key) => key.Trim().Trim('{', '}').ToUpperInvariant();
		var current = new Dictionary<string, long>(StringComparer.Ordinal);
		foreach (var pair in installed) current[Id(pair.Key)] = pair.Value;
		return staged.Where(pair => !current.TryGetValue(Id(pair.Key), out long version) || pair.Value > version)
			.Select(pair => Id(pair.Key) + "@" + pair.Value.ToString(CultureInfo.InvariantCulture))
			.OrderBy(text => text, StringComparer.Ordinal).ToList();
	}

	// BitLocker is suspended for one restart only when all of these hold; Core never suspends it otherwise.
	internal static bool ShouldSuspend(string? protection, IReadOnlyList<string> firmware, bool guardInstalled, DateTimeOffset? alreadySuspendedAt) =>
		protection is "on" or "encrypting" && firmware.Count > 0 && guardInstalled && alreadySuspendedAt is null;
}

// ---------- automatic sign-in ----------

internal static class AutoSignInPolicy
{
	// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon: AutoAdminLogon "1" and DefaultUserName.
	// The password is either Sysinternals Autologon's LSA secret (unreadable without SYSTEM, and Core never
	// tries) or a plain-text DefaultPassword value, whose presence alone is reported (it is never read).
	// AutoLogonCount makes Windows switch automatic sign-in off after that many sign-ins.
	// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device DevicePasswordLessBuildVersion = 2
	// is Settings' "Only allow Windows Hello sign-in", which hides and can break automatic sign-in.
	internal static AutoSignInState Read(string? autoAdminLogon, string? userName, bool passwordValuePresent, object? autoLogonCount, object? passwordLess)
	{
		bool enabled = autoAdminLogon?.Trim() == "1";
		int? count = autoLogonCount switch { int number => number, string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed, _ => null };
		bool passwordlessOnly = passwordLess is int value && value == 2;
		return new AutoSignInState(enabled, string.IsNullOrWhiteSpace(userName) ? null : userName.Trim(), passwordValuePresent, count, passwordlessOnly);
	}
}

// ---------- controllers ----------

internal static class ControllerCatalog
{
	// USB: "HID\VID_057E&PID_0306"; Bluetooth: "HID\{00001124-...}_VID&0002054C_PID&0CE6" (a 4-digit source prefix).
	private static readonly Regex VidPid = new(@"VID[_&](?:[0-9A-F]{4})?([0-9A-F]{4})[_&]PID[_&]([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	// Known vendors and products. Valve's 2026 Steam Controller and its Puck receiver use Valve's vendor ID
	// (28DE); their product IDs are not published, so any Valve HID device is shown as Valve hardware.
	// The Mayflash DolphinBar in mode 4 presents each Wii remote as Nintendo's RVL-CNT-01 (057E:0306).
	private static readonly Dictionary<string, (string Kind, string Name)> Products = new(StringComparer.OrdinalIgnoreCase)
	{
		["28DE:1102"] = ("steam", "Steam Controller (2015, wired)"),
		["28DE:1142"] = ("steam", "Steam Controller receiver (2015)"),
		["057E:0306"] = ("wii", "Wii Remote (DolphinBar)"),
		["057E:0330"] = ("wii", "Wii Remote Plus (DolphinBar)"),
		["057E:2009"] = ("nintendo", "Switch Pro Controller"),
		["054C:0CE6"] = ("sony", "DualSense"),
		["054C:0DF2"] = ("sony", "DualSense Edge"),
		["054C:05C4"] = ("sony", "DualShock 4"),
		["054C:09CC"] = ("sony", "DualShock 4"),
		["045E:0B12"] = ("xbox", "Xbox Wireless Controller"),
		["045E:0B13"] = ("xbox", "Xbox Wireless Controller"),
		["045E:0B20"] = ("xbox", "Xbox Wireless Controller"),
		["045E:0B22"] = ("xbox", "Xbox Elite Controller Series 2"),
		["045E:02FF"] = ("xbox", "Xbox controller"),
		["045E:02E0"] = ("xbox", "Xbox Wireless Controller"),
	};

	private static readonly Dictionary<string, (string Kind, string Name)> Vendors = new(StringComparer.OrdinalIgnoreCase)
	{
		["28DE"] = ("steam", "Valve controller (Steam Controller or Puck)"),
		["054C"] = ("sony", "PlayStation controller"),
		["045E"] = ("xbox", "Xbox controller"),
		["2DC8"] = ("8bitdo", "8BitDo controller"),
		["057E"] = ("nintendo", "Nintendo controller"),
	};

	internal static string? Key(IEnumerable<string> hardwareIds)
	{
		foreach (string id in hardwareIds)
			if (VidPid.Match(id) is { Success: true } match)
				return (match.Groups[1].Value + ":" + match.Groups[2].Value).ToUpperInvariant();
		return null;
	}

	// One HID device from Windows' device list. Game controllers are the HID gamepad (usage page 1,
	// usage 5), joystick (4) and multi-axis (8) collections; Valve devices use vendor-defined collections,
	// so they count by their vendor ID. Anything else (keyboards, mice, headsets) is not a controller.
	internal static ControllerSeen? FromHid(IReadOnlyList<string> hardwareIds, IReadOnlyList<string> compatibleIds, string? description)
	{
		string? key = Key(hardwareIds);
		bool gameCollection = compatibleIds.Any(id => id.Contains("HID_DEVICE_UP:0001_U:0005", StringComparison.OrdinalIgnoreCase)
			|| id.Contains("HID_DEVICE_UP:0001_U:0004", StringComparison.OrdinalIgnoreCase)
			|| id.Contains("HID_DEVICE_UP:0001_U:0008", StringComparison.OrdinalIgnoreCase));
		bool xinput = hardwareIds.Any(id => id.Contains("IG_", StringComparison.OrdinalIgnoreCase));
		string vendor = key?.Split(':')[0] ?? "";
		if (!gameCollection && !xinput && vendor != "28DE") return null;
		if (key is not null && Products.TryGetValue(key, out var product)) return new ControllerSeen(product.Kind, product.Name, key);
		if (Vendors.TryGetValue(vendor, out var known)) return new ControllerSeen(known.Kind, known.Name, key!);
		string name = string.IsNullOrWhiteSpace(description) ? "Game controller" : description.Trim();
		if (name.Length > 60) name = name[..60];
		return new ControllerSeen(xinput ? "xinput" : "hid", name, key ?? "");
	}

	// One entry per controller: a pad's several HID collections and a duplicate listing collapse by VID:PID
	// (two identical pads count as one line; the list is information, not an inventory).
	internal static IReadOnlyList<ControllerSeen> Distinct(IEnumerable<ControllerSeen> seen) =>
		seen.GroupBy(controller => controller.Key.Length > 0 ? controller.Key : controller.Name, StringComparer.OrdinalIgnoreCase)
			.Select(group => group.First()).OrderBy(controller => controller.Kind, StringComparer.Ordinal).ThenBy(controller => controller.Name, StringComparer.Ordinal).ToList();
}

// ---------- Steam sign-in ----------

internal static class SteamSignInPolicy
{
	internal sealed record LoginUser(string SteamId, string? AccountName, string? PersonaName, bool RememberPassword, bool MostRecent, bool AllowAutoLogin, bool WantsOfflineMode);

	// config\loginusers.vdf (Valve KeyValues text). Only the per-account flags are read; no token or
	// password lives in this file, and Core opens no other Steam file for this.
	internal static IReadOnlyList<LoginUser> ParseLoginUsers(string text)
	{
		var users = new List<LoginUser>();
		var tokens = Tokenize(text).ToList();
		int index = 0;
		// "users" { "<steamid>" { "Key" "Value" ... } ... }
		while (index < tokens.Count && tokens[index] != "{") index++;
		index++;
		while (index < tokens.Count && tokens[index] != "}")
		{
			string id = tokens[index++];
			if (index >= tokens.Count || tokens[index] != "{") break;
			index++;
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			while (index + 1 < tokens.Count && tokens[index] != "}")
			{
				string key = tokens[index++];
				if (tokens[index] == "{") { index = Skip(tokens, index); continue; }
				values[key] = tokens[index++];
			}
			index++;
			bool Flag(string name, bool fallback) => values.TryGetValue(name, out string? value) ? value == "1" : fallback;
			users.Add(new LoginUser(id, values.GetValueOrDefault("AccountName"), values.GetValueOrDefault("PersonaName"), Flag("RememberPassword", false),
				Flag("MostRecent", false), Flag("AllowAutoLogin", true), Flag("WantsOfflineMode", false)));
		}
		return users;
	}

	private static int Skip(List<string> tokens, int index)
	{
		int depth = 0;
		for (; index < tokens.Count; index++)
		{
			if (tokens[index] == "{") depth++;
			else if (tokens[index] == "}" && --depth == 0) return index + 1;
		}
		return index;
	}

	private static IEnumerable<string> Tokenize(string text)
	{
		int i = 0;
		while (i < text.Length)
		{
			char c = text[i];
			if (char.IsWhiteSpace(c)) { i++; continue; }
			if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
			if (c is '{' or '}') { yield return c.ToString(); i++; continue; }
			if (c == '"')
			{
				var value = new System.Text.StringBuilder();
				i++;
				while (i < text.Length && text[i] != '"')
				{
					if (text[i] == '\\' && i + 1 < text.Length) { value.Append(text[i + 1]); i += 2; continue; }
					value.Append(text[i++]);
				}
				i++;
				yield return value.ToString();
				continue;
			}
			int start = i;
			while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"')) i++;
			yield return text[start..i];
		}
	}

	// HKCU\Software\Valve\Steam\ActiveProcess "ActiveUser" is the signed-in account (0 while the sign-in
	// screen or a Steam Guard prompt is up). Steam Guard itself is not visible from outside Steam, so a
	// running Steam with no active user is reported as "sign-in or Steam Guard prompt on screen".
	internal static SteamSignIn Evaluate(bool running, object? activeUser, IReadOnlyList<LoginUser> users)
	{
		bool? signedIn = !running ? null : activeUser switch { int value => value != 0, long value => value != 0, _ => null };
		LoginUser? recent = users.FirstOrDefault(user => user.MostRecent) ?? (users.Count == 1 ? users[0] : null);
		return new SteamSignIn(running, signedIn, recent?.PersonaName, recent?.RememberPassword, recent?.AllowAutoLogin, recent?.WantsOfflineMode ?? false);
	}
}

// ---------- Windows edition and update policy ----------

internal static class UpdatePolicyRules
{
	// EditionID in HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion: Core* is Home.
	internal static bool IsHome(string? edition) => edition is not null && edition.StartsWith("Core", StringComparison.OrdinalIgnoreCase);

	internal static UpdatePolicyState Evaluate(string? edition, string? displayVersion, object? excludeDrivers, object? searchOrder, object? targetRelease, string? targetVersion)
	{
		bool home = IsHome(edition);
		bool policy = excludeDrivers is int exclude && exclude == 1;
		// Windows Home does not reliably honour ExcludeWUDriversInQualityUpdate; Device Installation Settings'
		// "No" (DriverSearching\SearchOrderConfig = 0) is the switch Home has.
		bool search = searchOrder is int order && order == 0;
		bool drivers = home ? policy && search : policy;
		bool pinned = targetRelease is int target && target == 1 && !string.IsNullOrWhiteSpace(targetVersion);
		return new UpdatePolicyState(edition, home, drivers, pinned, pinned ? targetVersion!.Trim() : null, displayVersion);
	}
}

// ---------- launcher updates that will ask for administrator rights ----------

internal static class LauncherUpdatePolicy
{
	// Cheap signals only. (1) PendingFileRenameOperations (HKLM\SYSTEM\CurrentControlSet\Control\Session
	// Manager, readable by users) lists files an installer replaces at the next restart: a launcher or
	// anti-cheat path there means its update is half done. (2) A running installer process of these vendors
	// is about to raise (or is waiting behind) a UAC prompt.
	private static readonly (string Pattern, string Name)[] Paths =
	{
		(@"\riot vanguard\", "Riot Vanguard"), ("vgk.sys", "Riot Vanguard"),
		(@"\easyanticheat", "Easy Anti-Cheat"),
		(@"\electronic arts\ea desktop", "EA app"), (@"\ea desktop\", "EA app"),
		(@"\epic games\launcher", "Epic Games Launcher"), (@"\epic online services", "Epic Online Services"),
		(@"\ubisoft game launcher", "Ubisoft Connect"),
		(@"\battle.net", "Battle.net"),
	};

	private static readonly (string Process, string Name)[] Installers =
	{
		("EasyAntiCheat_EOS_Setup", "Easy Anti-Cheat"), ("EasyAntiCheat_Setup", "Easy Anti-Cheat"),
		("EpicInstaller", "Epic Games Launcher"), ("EpicGamesLauncherInstaller", "Epic Games Launcher"), ("EpicOnlineServicesInstaller", "Epic Online Services"),
		("EAappInstaller", "EA app"), ("EADesktopInstaller", "EA app"),
		("UbisoftConnectInstaller", "Ubisoft Connect"), ("UplayInstaller", "Ubisoft Connect"),
		("Battle.net-Setup", "Battle.net"),
		("vgc_installer", "Riot Vanguard"),
	};

	internal static IReadOnlyList<string> Pending(IEnumerable<string> pendingRenames, IEnumerable<string> processNames)
	{
		var found = new SortedSet<string>(StringComparer.Ordinal);
		foreach (string path in pendingRenames)
		{
			string lower = path.ToLowerInvariant();
			foreach ((string pattern, string name) in Paths) if (lower.Contains(pattern, StringComparison.Ordinal)) found.Add(name);
		}
		foreach (string process in processNames)
			foreach ((string installer, string name) in Installers)
				if (string.Equals(process, installer, StringComparison.OrdinalIgnoreCase)) found.Add(name);
		return found.ToList();
	}
}

// ---------- launcher windows popping over Big Picture ----------

internal sealed record LauncherFocusInput(bool Enabled, bool InGamingMode, bool GameRunning, bool StreamActive, bool PreviousWasBigPicture, string ForegroundProcess, IReadOnlyList<DateTimeOffset> RecentPushes, DateTimeOffset Now);

internal enum LauncherFocusAction { Leave, PushBack, GiveUp }

internal static class LauncherFocusPolicy
{
	internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
	internal const int MaxPushesPerWindow = 3;

	// Process names of the launcher windows that pop up on their own (update banners, "what's new", friend
	// lists, sign-in reminders). Riot Client and others are not on the list: add them only when seen.
	private static readonly Dictionary<string, string> Launchers = new(StringComparer.OrdinalIgnoreCase)
	{
		["EpicGamesLauncher"] = "Epic Games Launcher",
		["EADesktop"] = "EA app",
		["upc"] = "Ubisoft Connect",
		["UbisoftConnect"] = "Ubisoft Connect",
		["XboxPcApp"] = "Xbox app",
		["Battle.net"] = "Battle.net",
	};

	internal static string? LauncherName(string? processName) => processName is not null && Launchers.TryGetValue(processName, out string? name) ? name : null;

	// Pushed back (behind Big Picture, never closed or minimised) only when the launcher took the screen
	// from Big Picture by itself: Gaming Mode, no game running (a launcher a game needs stays), not
	// streaming. If the user keeps bringing it back (three times in ten minutes), Core stops: it is wanted.
	internal static LauncherFocusAction Decide(LauncherFocusInput input)
	{
		if (!input.Enabled || !input.InGamingMode || input.GameRunning || input.StreamActive || !input.PreviousWasBigPicture) return LauncherFocusAction.Leave;
		if (LauncherName(input.ForegroundProcess) is null) return LauncherFocusAction.Leave;
		int recent = input.RecentPushes.Count(at => input.Now - at < Window && at <= input.Now);
		return recent >= MaxPushesPerWindow ? LauncherFocusAction.GiveUp : LauncherFocusAction.PushBack;
	}
}

// ---------- after a wake ----------

internal static class WakeCheckPolicy
{
	// How long after the resume the check runs: the TV wake (up to 30 s) and the wake sequence come first.
	internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(35);

	// Big Picture is re-focused once when it is up but not in front, in Gaming Mode with no game.
	internal static bool ShouldRetakeFocus(bool inGamingMode, bool gameRunning, bool bigPictureUp, bool steamForeground) =>
		inGamingMode && !gameRunning && bigPictureUp && !steamForeground;

	// The input switch is sent again once only when Core itself switched the TV on this wake (the TV
	// was not already in use by someone else) and the TV now shows another input. Never during Stream mode.
	internal static bool ShouldResendInput(bool tvControlOn, bool coreSwitchedTv, bool streamHeld, bool? tvOnCore) =>
		tvControlOn && coreSwitchedTv && !streamHeld && tvOnCore == false;
}

// ---------- the Ethernet link ----------

internal sealed record NetworkAdapter(string Name, string Description, bool Ethernet, bool Up, bool HasGateway);

internal static class NetworkRules
{
	private static readonly string[] Virtual = { "virtual", "hyper-v", "vpn", "tap-", "tailscale", "wireguard", "zerotier", "loopback", "vmware", "virtualbox", "pseudo", "bluetooth" };

	// The physical Ethernet adapter(s): up with a gateway counts as the network being up. Null when the PC
	// has no physical Ethernet adapter at all (then the row does not judge the network).
	internal static (bool? Up, string? Name) Ethernet(IEnumerable<NetworkAdapter> adapters)
	{
		var physical = adapters.Where(adapter => adapter.Ethernet && !Virtual.Any(word => adapter.Description.Contains(word, StringComparison.OrdinalIgnoreCase)
			|| adapter.Name.Contains(word, StringComparison.OrdinalIgnoreCase))).ToList();
		if (physical.Count == 0) return (null, null);
		NetworkAdapter? up = physical.FirstOrDefault(adapter => adapter.Up && adapter.HasGateway);
		return up is not null ? (true, up.Name) : (false, physical[0].Name);
	}
}

// ---------- Core's own memory: %APPDATA%\GamingMode\console-guards.json ----------

internal sealed record ConsoleGuardState
{
	public DisplayProfile? Display { get; init; }
	public SoundDevice? Sound { get; init; }
	public List<string> ExpectedControllers { get; init; } = new();
	public WakeCheckResult? LastWake { get; init; }
	// When Core last asked for a one-restart BitLocker suspension, and the boot it belonged to.
	public DateTimeOffset? BitLockerSuspendedAt { get; init; }
	public DateTimeOffset? BitLockerSuspendedBoot { get; init; }
	public List<DateTimeOffset> LauncherPushes { get; init; } = new();
	public string? LauncherLastPushed { get; init; }
}

internal static class ConsoleGuardStore
{
	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static ConsoleGuardState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<ConsoleGuardState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, ConsoleGuardState state)
	{
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}

	// A suspension Core asked for counts until the PC restarts (BitLocker resumes by itself after it).
	internal static DateTimeOffset? SuspendedThisBoot(ConsoleGuardState state, DateTimeOffset bootTime) =>
		state.BitLockerSuspendedAt is { } at && state.BitLockerSuspendedBoot is { } boot && Math.Abs((boot - bootTime).TotalMinutes) < 2 ? at : null;

	// Pushes from today (for the row) and the last ten minutes (for the policy); older ones are dropped.
	internal static ConsoleGuardState RecordPush(ConsoleGuardState state, string launcher, DateTimeOffset now) => state with
	{
		LauncherPushes = state.LauncherPushes.Where(at => at.Date == now.Date && at <= now).Append(now).ToList(),
		LauncherLastPushed = launcher,
	};

	internal static int PushesToday(ConsoleGuardState state, DateTimeOffset now) => state.LauncherPushes.Count(at => at.Date == now.Date && at <= now);
}
