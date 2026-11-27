// Console guards tests (Linux-runnable): every row's verdict, the notices they raise, and the pure helpers
// behind the Windows probes (BitLocker's Shell values and elevated check, firmware staging, automatic
// sign-in, controllers, Steam's loginusers.vdf, update policy, launcher updates and pop-ups, wake checks,
// the Ethernet link, Core's own state file). docs/eternal/CONSOLE-GUARDS.md.
using GamingMode.Services;
using Playhub.Shared;

static class ConsoleGuardTests
{
	internal static void Run(Action<bool, string> Check)
	{
		DateTimeOffset now = new(2026, 11, 26, 20, 0, 0, TimeSpan.FromHours(-5));
		var lg = new DisplayProfile(@"\\?\DISPLAY#GSM8142#5&1a2b&0&UID4352#{e6f07b5f}", @"\\.\DISPLAY1", "LG TV SSCR2", 3840, 2160, 120, true, true, true);
		var speaker = new SoundDevice("{0.0.0.00000000}.{lg}", "LG TV SSCR2 (NVIDIA High Definition Audio)");
		var headset = new SoundDevice("{0.0.0.00000000}.{headset}", "Headset");
		ConsoleGuardInputs Good() => new()
		{
			BitLockerProtection = "on",
			BitLockerCheck = new BitLockerCheck(now.AddDays(-2), true, 1, new[] { 1, 3 }, now.AddDays(-30)),
			BitLockerGuardInstalled = true,
			AutoSignIn = new AutoSignInState(true, "vonn", false, null, false),
			DisplaySaved = lg, DisplayCurrent = lg, DisplayRead = true,
			SoundSaved = speaker, SoundCurrent = speaker, SoundAvailable = new[] { speaker.Id, headset.Id },
			Controllers = new[] { new ControllerSeen("steam", "Valve controller (Steam Controller or Puck)", "28DE:1302") },
			Steam = new SteamSignIn(true, true, "Vonn", true, true, false),
			PromptsOff = true,
			UpdatePolicy = new UpdatePolicyState("Professional", false, true, true, "24H2", "24H2"),
			LauncherFocusEnabled = true, WakeChecksEnabled = true, EthernetUp = true, EthernetName = "Ethernet",
		};
		CoreHealthInputs Inputs(ConsoleGuardInputs guards) => new()
		{
			Now = now, ServerRead = ServerStatusRead.NotConfigured, TvEnabled = true, TvPaired = true, TvReachable = true,
			PcFreeBytes = 300L * 1000 * 1000 * 1000, PcTotalBytes = 1000L * 1000 * 1000 * 1000, CoreVersion = "3.3.0", CoreUpdateAvailable = false, Microphone = "live",
			Guards = guards,
		};
		HealthRow Row(HealthReport report, string id) => report.Rows.Single(row => row.Id == id);
		HealthReport Eval(ConsoleGuardInputs guards) => CoreHealthPolicy.Evaluate(Inputs(guards));

		// ---------- rows and order ----------
		HealthReport report = Eval(Good());
		var ids = report.Rows.Select(row => row.Id).ToList();
		Check(ConsoleGuardPolicy.RowOrder.All(ids.Contains) && ids.IndexOf("display") == ids.IndexOf("microphone") + 1 && ids.IndexOf("wake-check") + 1 == ids.IndexOf("pc-disk"),
			"guards: eleven rows between the microphone and the PC's disk, in order");
		Check(ids.Where(ConsoleGuardPolicy.RowOrder.Contains).SequenceEqual(ConsoleGuardPolicy.RowOrder), "guards: rows in display order");
		Check(report.Attention == 0 && report.Problems.Count == 0 && ConsoleGuardPolicy.RowOrder.All(id => Row(report, id).Level == HealthLevel.Ok),
			"guards: a healthy console is all steel, no notice");
		Check(!CoreHealthPolicy.Evaluate(Inputs(Good()) with { Guards = null }).Rows.Any(row => ConsoleGuardPolicy.RowOrder.Contains(row.Id)), "guards: no inputs, no rows (server-only setups)");

		// ---------- display ----------
		report = Eval(Good() with { DisplayCurrent = lg with { RefreshHz = 60, Hdr = false } });
		Check(Row(report, "display") is { Level: HealthLevel.Attention, State: "changed" } row && (string?)row.Data["fix"] == "display-restore"
			&& row.Data["differences"] is IReadOnlyList<string> diff && diff.SequenceEqual(new[] { "refresh", "hdr" }), "display: refresh and HDR changed offer Restore");
		Check(report.Problems.Single().NoticeKey == "console.displayChanged", "display: a change raises a notice (once a day, never in a game)");
		Check(Row(Eval(Good() with { DisplayCurrent = lg with { RefreshHz = 119 } }), "display").State == "normal", "display: 119 vs 120 Hz is the same mode");
		Check(Row(Eval(Good() with { DisplayCurrent = lg with { Primary = false } }), "display").Data["differences"] is IReadOnlyList<string> p && p.SequenceEqual(new[] { "primary" }), "display: the LG no longer primary");
		Check(Row(Eval(Good() with { DisplayCurrent = lg with { Width = 1920, Height = 1080 } }), "display").Data["differences"] is IReadOnlyList<string> r && r.Contains("resolution"), "display: resolution");
		Check(Row(Eval(Good() with { DisplayCurrent = lg with { Vrr = false } }), "display").Data["differences"] is IReadOnlyList<string> v && v.SequenceEqual(new[] { "vrr" }), "display: Windows VRR switch");
		Check(Row(Eval(Good() with { DisplayCurrent = lg with { Hdr = null, Vrr = null } }), "display").State == "normal", "display: unreadable HDR/VRR is not a change");
		report = Eval(Good() with { DisplaySaved = null });
		Check(Row(report, "display") is { Level: HealthLevel.Info, State: "not-saved" } && report.Attention == 0, "display: nothing saved yet is information");
		report = Eval(Good() with { DisplayCurrent = null });
		Check(Row(report, "display") is { Level: HealthLevel.Info, State: "missing" } && report.Problems.Count == 0, "display: TV off or elsewhere is not a change");
		Check(Row(Eval(Good() with { DisplayRead = false }), "display").Level == HealthLevel.Unknown, "display: unreadable is unknown");
		var other = new DisplayProfile(@"\\?\DISPLAY#DEL4321", @"\\.\DISPLAY2", "DELL U2720Q", 3840, 2160, 60, false, false, true);
		Check(ConsoleGuardPolicy.Match(lg, new[] { other, lg with { Device = @"\\.\DISPLAY5" } })?.Device == @"\\.\DISPLAY5", "display: matched by its stable path when the GDI name moved");
		Check(ConsoleGuardPolicy.Match(lg with { Path = null }, new[] { other, lg with { Path = "x" } })?.Monitor == "LG TV SSCR2", "display: matched by GDI name without a path");
		Check(ConsoleGuardPolicy.Match(lg, new[] { other }) is null, "display: the LG not among the displays");

		// ---------- sound ----------
		report = Eval(Good() with { SoundCurrent = headset });
		Check(Row(report, "sound-output") is { Level: HealthLevel.Attention, State: "changed" } s && (string?)s.Data["fix"] == "sound-restore" && report.Problems.Single().NoticeKey == "console.soundChanged",
			"sound: another default offers Switch back");
		Check(Row(Eval(Good() with { SoundCurrent = headset, SoundAvailable = new[] { headset.Id } }), "sound-output") is { Level: HealthLevel.Info, State: "missing" }, "sound: the saved device gone (TV off) is information");
		Check(Row(CoreHealthPolicy.Evaluate(Inputs(Good() with { SoundCurrent = headset }) with { StreamActive = true }), "sound-output").State == "stream-mode", "sound: Stream mode moves the sound on purpose");
		Check(Row(Eval(Good() with { SoundSaved = null }), "sound-output").State == "not-saved", "sound: nothing saved yet");

		// ---------- controllers ----------
		report = Eval(Good() with { ExpectedControllers = new[] { "28DE:1302", "057E:0306" } });
		Check(Row(report, "controllers") is { Level: HealthLevel.Info, State: "expected-missing" } c && c.Data["missing"] is List<string> m && m.Single() == "057E:0306" && report.Attention == 0,
			"controllers: an expected pad missing is amber information, never an alarm");
		Check(Row(Eval(Good() with { Controllers = Array.Empty<ControllerSeen>() }), "controllers").State == "none", "controllers: none seen");
		Check(ControllerCatalog.FromHid(new[] { @"HID\VID_057E&PID_0306&REV_0100" }, new[] { "HID_DEVICE_SYSTEM_GAME", "HID_DEVICE_UP:0001_U:0005" }, "HID-compliant game controller")
			is { Kind: "wii", Key: "057E:0306" }, "controllers: the DolphinBar's Wii remote");
		Check(ControllerCatalog.FromHid(new[] { @"HID\VID_28DE&PID_1302&MI_02" }, new[] { "HID_DEVICE_UP:FF00_U:0001" }, null) is { Kind: "steam", Key: "28DE:1302" },
			"controllers: a Valve device counts by its vendor even with a vendor-defined collection");
		Check(ControllerCatalog.FromHid(new[] { @"HID\{00001124-0000-1000-8000-00805f9b34fb}_VID&0002054c_PID&0ce6" }, new[] { "HID_DEVICE_UP:0001_U:0005" }, null) is { Name: "DualSense", Key: "054C:0CE6" },
			"controllers: a DualSense over Bluetooth");
		Check(ControllerCatalog.FromHid(new[] { @"HID\VID_045E&PID_0B12&IG_00" }, Array.Empty<string>(), null) is { Kind: "xbox" }, "controllers: an XInput pad (IG_)");
		Check(ControllerCatalog.FromHid(new[] { @"HID\VID_2DC8&PID_3106" }, new[] { "HID_DEVICE_UP:0001_U:0004" }, null) is { Kind: "8bitdo" }, "controllers: 8BitDo");
		Check(ControllerCatalog.FromHid(new[] { @"HID\VID_046D&PID_C52B" }, new[] { "HID_DEVICE_UP:0001_U:0002" }, "HID-compliant mouse") is null, "controllers: a mouse is not a controller");
		Check(ControllerCatalog.Distinct(new[] { new ControllerSeen("sony", "DualSense", "054C:0CE6"), new ControllerSeen("sony", "DualSense", "054C:0CE6") }).Count == 1, "controllers: one line per pad");

		// ---------- Steam ----------
		const string loginUsers = "\"users\"\n{\n\t\"76561198000000001\"\n\t{\n\t\t\"AccountName\"\t\t\"vonn_acct\"\n\t\t\"PersonaName\"\t\t\"Vonn\"\n\t\t\"RememberPassword\"\t\t\"1\"\n\t\t\"WantsOfflineMode\"\t\t\"0\"\n\t\t\"MostRecent\"\t\t\"1\"\n\t\t\"AllowAutoLogin\"\t\t\"1\"\n\t\t\"Timestamp\"\t\t\"1764190000\"\n\t}\n\t\"76561198000000002\"\n\t{\n\t\t\"AccountName\"\t\t\"angel\"\n\t\t\"PersonaName\"\t\t\"Angel\"\n\t\t\"RememberPassword\"\t\t\"0\"\n\t\t\"MostRecent\"\t\t\"0\"\n\t}\n}\n";
		var users = SteamSignInPolicy.ParseLoginUsers(loginUsers);
		Check(users.Count == 2 && users[0] is { PersonaName: "Vonn", MostRecent: true, RememberPassword: true, AllowAutoLogin: true } && users[1].RememberPassword == false, "steam: loginusers.vdf flags");
		Check(SteamSignInPolicy.Evaluate(true, 12345, users) is { SignedIn: true, PersonaName: "Vonn" }, "steam: ActiveUser set is signed in");
		Check(SteamSignInPolicy.Evaluate(true, 0, users).SignedIn == false && SteamSignInPolicy.Evaluate(false, 0, users).SignedIn is null, "steam: ActiveUser 0 while running is signed out");
		Check(Row(Eval(Good() with { Steam = new SteamSignIn(true, false, "Vonn", true, true, false) }), "steam-sign-in") is { Level: HealthLevel.Attention, State: "signed-out" }, "steam: sign-in or Steam Guard on screen");
		Check(Row(Eval(Good() with { Steam = new SteamSignIn(true, true, "Vonn", false, true, false) }), "steam-sign-in").State == "no-remember", "steam: next start would ask for the password");
		Check(Row(Eval(Good() with { Steam = new SteamSignIn(false, null, null, null, null, false) }), "steam-sign-in") is { Level: HealthLevel.Info, State: "not-running" }, "steam: not running is information");
		Check(SteamSignInPolicy.ParseLoginUsers("garbage {").Count == 0 && SteamSignInPolicy.ParseLoginUsers("").Count == 0, "steam: a broken file reads as no users");

		// ---------- automatic sign-in ----------
		Check(AutoSignInPolicy.Read("1", "vonn", false, null, null) is { Enabled: true, UserName: "vonn", PlainTextPassword: false, AutoLogonCount: null, PasswordlessOnly: false }, "sign-in: Autologon's LSA secret");
		Check(AutoSignInPolicy.Read("1", "vonn", true, "3", 2) is { PlainTextPassword: true, AutoLogonCount: 3, PasswordlessOnly: true }, "sign-in: plain-text value, a count and Hello-only");
		report = Eval(Good() with { AutoSignIn = AutoSignInPolicy.Read("0", "vonn", false, null, 0) });
		Check(Row(report, "auto-sign-in") is { Level: HealthLevel.Attention, State: "off" } a && (string?)a.Data["guide"] == "autologon" && report.Problems.Single().NoticeKey == "console.autoSignIn",
			"sign-in: switched off alerts and points to the guided re-setup");
		Check(Row(Eval(Good() with { AutoSignIn = AutoSignInPolicy.Read("1", "vonn", false, 2, null) }), "auto-sign-in").State == "expires", "sign-in: AutoLogonCount runs out");
		Check(Row(Eval(Good() with { AutoSignIn = AutoSignInPolicy.Read("1", "vonn", false, null, 2) }), "auto-sign-in").State == "passwordless", "sign-in: Windows Hello-only setting");
		Check(Row(Eval(Good() with { AutoSignIn = AutoSignInPolicy.Read("1", " ", false, null, null) }), "auto-sign-in").State == "off", "sign-in: no user name is off");

		// ---------- BitLocker ----------
		Check(new[] { 1, 2, 3, 4, 5, 6, 8, 0, 99 }.Select(value => ConsoleGuardPolicy.BitLockerFromShell(value)).SequenceEqual(new[] { "on", "off", "encrypting", "decrypting", "suspended", "on", "waiting", "unknown", "unknown" })
			&& ConsoleGuardPolicy.BitLockerFromShell(null) == "unknown", "bitlocker: the Shell's values");
		Check(Row(Eval(Good() with { BitLockerProtection = "off" }), "bitlocker") is { Level: HealthLevel.Ok, State: "off" }, "bitlocker: off is fine for a console");
		Check(Row(Eval(Good() with { BitLockerCheck = null }), "bitlocker") is { Level: HealthLevel.Info, State: "unchecked" }, "bitlocker: on, not checked from the desktop yet");
		Check(Row(Eval(Good() with { BitLockerCheck = new BitLockerCheck(now, true, 1, new[] { 1 }, null) }), "bitlocker") is { Level: HealthLevel.Attention, State: "no-recovery-key" },
			"bitlocker: TPM only, no recovery key protector");
		report = Eval(Good() with { FirmwarePending = new[] { "6E1B2A3C@65561" } });
		Check(Row(report, "bitlocker") is { Level: HealthLevel.Attention, State: "firmware-pending" } b && (string?)b.Data["fix"] == "bitlocker-suspend" && report.Problems.Count == 0,
			"bitlocker: staged firmware with the guard offers a one-restart suspension");
		report = Eval(Good() with { FirmwarePending = new[] { "6E1B2A3C@65561" }, BitLockerGuardInstalled = false });
		Check(Row(report, "bitlocker").State == "firmware-no-guard" && report.Problems.Single().NoticeKey == "console.bitlockerFirmware", "bitlocker: staged firmware without the guard alerts");
		Check(Row(Eval(Good() with { BitLockerProtection = "suspended", FirmwarePending = new[] { "x@1" }, BitLockerSuspendedByCoreAt = now }), "bitlocker") is { Level: HealthLevel.Info, State: "suspended-for-update" },
			"bitlocker: suspended for the update is information");
		Check(Row(Eval(Good() with { BitLockerProtection = "suspended" }), "bitlocker") is { Level: HealthLevel.Attention, State: "suspended" }, "bitlocker: suspended for no known reason");
		Check(Row(Eval(Good() with { BitLockerProtection = "waiting" }), "bitlocker").State == "waiting", "bitlocker: device encryption waiting for activation");
		Check(FirmwarePolicy.Pending(new Dictionary<string, long> { ["{6E1B}"] = 65561, ["AAAA"] = 5 }, new Dictionary<string, long> { ["6E1B"] = 65560, ["aaaa"] = 5 }).SequenceEqual(new[] { "6E1B@65561" }),
			"bitlocker: a staged capsule newer than the ESRT is pending; an equal one is not");
		Check(FirmwarePolicy.ShouldSuspend("on", new[] { "x@1" }, true, null) && !FirmwarePolicy.ShouldSuspend("off", new[] { "x@1" }, true, null)
			&& !FirmwarePolicy.ShouldSuspend("on", Array.Empty<string>(), true, null) && !FirmwarePolicy.ShouldSuspend("on", new[] { "x@1" }, false, null)
			&& !FirmwarePolicy.ShouldSuspend("on", new[] { "x@1" }, true, now), "bitlocker: suspended only when on, firmware staged, guard installed and not yet this boot");

		// The elevated check and the guard task (Shared/BitLockerGuard.cs).
		BitLockerGuard.CheckResult? parsed = BitLockerGuard.ParseScriptOutput("WARNING: x\r\n{\"found\":true,\"protection\":1,\"protectors\":[1,3],\"backupSeenAt\":\"2026-10-01T10:00:00.0000000Z\"}\r\n", now);
		Check(parsed is { Found: true, Protection: 1 } && parsed.Protectors.SequenceEqual(new[] { 1, 3 }) && parsed.BackupSeenAt?.Month == 10, "bitlocker check: protector types and the backup event");
		Check(BitLockerGuard.ParseScriptOutput("{\"found\":true,\"protection\":1,\"protectors\":3,\"backupSeenAt\":null}", now) is { } single && single.Protectors.SequenceEqual(new[] { 3 }),
			"bitlocker check: PowerShell's bare number for one protector");
		Check(BitLockerGuard.ParseScriptOutput("{\"found\":true,\"protectors\":[3,\"123456-123456\",99]}", now) is { } strict && strict.Protectors.SequenceEqual(new[] { 3 }),
			"bitlocker check: anything but a type number is dropped (never a key)");
		Check(BitLockerGuard.ParseScriptOutput("not json", now) is null && BitLockerGuard.ParseScriptOutput("{broken", now) is null, "bitlocker check: garbage reads as no answer");
		string script = BitLockerGuard.CheckScript("C:");
		Check(script.Contains("GetKeyProtectorType") && !script.Contains("GetKeyProtectorNumericalPassword") && !script.Contains("RecoveryPassword") && !script.Contains("ExternalKey"),
			"bitlocker check: the script asks for types only, never the recovery password");
		string xml = BitLockerGuard.TaskXml("S-1-5-21-1111111111-2222222222-3333333333-1001", "c:", @"C:\Windows\System32");
		Check(xml.Contains(@"<Command>C:\Windows\System32/manage-bde.exe</Command>") || xml.Contains(@"<Command>C:\Windows\System32\manage-bde.exe</Command>"), "guard task: runs manage-bde from System32");
		Check(xml.Contains("<Arguments>-protectors -disable C: -RebootCount 1</Arguments>") && xml.Contains("<UserId>S-1-5-18</UserId>")
			&& xml.Contains("(A;;GRGX;;;S-1-5-21-1111111111-2222222222-3333333333-1001)") && !xml.Contains(".ps1") && !xml.Contains("powershell"),
			"guard task: one fixed command as SYSTEM for one restart, startable by this user, no script");
		bool refused = false;
		try { BitLockerGuard.TaskXml("S-1-1-0", "C:", @"C:\Windows\System32"); } catch (ArgumentException) { refused = true; }
		Check(refused && !BitLockerGuard.IsUserSid("S-1-5-32-544") && BitLockerGuard.NormalizeDrive("C:; calc") is null, "guard task: Everyone, Administrators and odd drives refused");
		string checkFile = Path.Combine(Path.GetTempPath(), "core-bitlocker-" + Guid.NewGuid().ToString("N") + ".json");
		try
		{
			BitLockerGuard.Save(checkFile, parsed!);
			Check(BitLockerGuard.Load(checkFile) is { Found: true } loaded && loaded.Protectors.SequenceEqual(new[] { 1, 3 }), "bitlocker check: the result file round-trips");
			File.WriteAllText(checkFile, "{oops");
			Check(BitLockerGuard.Load(checkFile) is null, "bitlocker check: a broken file is no check");
		}
		finally { File.Delete(checkFile); }

		// ---------- Windows prompts and update policy ----------
		report = Eval(Good() with { PromptsOff = false });
		Check(Row(report, "windows-prompts") is { Level: HealthLevel.Info, State: "on" } w && (string?)w.Data["fix"] == "prompts-off" && report.Attention == 0, "prompts: still on is amber with a one-button fix");
		Check(UpdatePolicyRules.Evaluate("Professional", "24H2", 1, null, 1, "24H2") is { Home: false, DriversBlocked: true, FeaturePinned: true, PinnedVersion: "24H2" }, "policy: Pro with both policies");
		Check(UpdatePolicyRules.Evaluate("Core", "24H2", 1, null, null, null) is { Home: true, DriversBlocked: false }, "policy: Home needs Device Installation Settings' No as well");
		Check(UpdatePolicyRules.Evaluate("CoreSingleLanguage", "24H2", 1, 0, null, null) is { Home: true, DriversBlocked: true }, "policy: Home with both driver switches");
		Check(Row(Eval(Good() with { UpdatePolicy = UpdatePolicyRules.Evaluate("Core", "24H2", 1, 0, null, null) }), "update-policy").State == "applied-home", "policy: Home applied (no pin there)");
		Check(Row(Eval(Good() with { UpdatePolicy = UpdatePolicyRules.Evaluate("Professional", "24H2", 1, null, null, null) }), "update-policy") is { Level: HealthLevel.Info, State: "not-applied" },
			"policy: Pro without the pin is not applied");

		// ---------- launcher updates and pop-ups ----------
		var pending = LauncherUpdatePolicy.Pending(new[] { @"\??\C:\Program Files\Riot Vanguard\vgk.sys.new", "", @"\??\C:\Windows\Temp\x.tmp" }, new[] { "explorer", "EasyAntiCheat_EOS_Setup", "EADesktop" });
		Check(pending.SequenceEqual(new[] { "Easy Anti-Cheat", "Riot Vanguard" }), "launchers: Vanguard's pending rename and an EAC installer; the EA app itself is not an update");
		Check(Row(Eval(Good() with { PendingLauncherUpdates = pending }), "launcher-updates") is { Level: HealthLevel.Info, State: "pending" }, "launchers: pending updates are information only");
		LauncherFocusInput Pop(string process, bool previousBigPicture = true, bool game = false, int pushes = 0) =>
			new(true, true, game, false, previousBigPicture, process, Enumerable.Range(0, pushes).Select(i => now.AddMinutes(-i - 1)).ToList(), now);
		Check(LauncherFocusPolicy.Decide(Pop("EpicGamesLauncher")) == LauncherFocusAction.PushBack, "pop-ups: Epic over Big Picture is pushed back");
		Check(new[] { "EADesktop", "upc", "XboxPcApp", "Battle.net" }.All(name => LauncherFocusPolicy.Decide(Pop(name)) == LauncherFocusAction.PushBack), "pop-ups: EA, Ubisoft, Xbox app, Battle.net");
		Check(LauncherFocusPolicy.Decide(Pop("EpicGamesLauncher", game: true)) == LauncherFocusAction.Leave, "pop-ups: never while a game runs");
		Check(LauncherFocusPolicy.Decide(Pop("EpicGamesLauncher", previousBigPicture: false)) == LauncherFocusAction.Leave, "pop-ups: only when it took the screen from Big Picture");
		Check(LauncherFocusPolicy.Decide(Pop("notepad")) == LauncherFocusAction.Leave, "pop-ups: other apps are left alone");
		Check(LauncherFocusPolicy.Decide(Pop("EpicGamesLauncher", pushes: 3)) == LauncherFocusAction.GiveUp, "pop-ups: brought back three times in ten minutes means wanted");
		Check(LauncherFocusPolicy.Decide(Pop("EpicGamesLauncher") with { Enabled = false }) == LauncherFocusAction.Leave
			&& LauncherFocusPolicy.Decide(Pop("EpicGamesLauncher") with { StreamActive = true }) == LauncherFocusAction.Leave, "pop-ups: off, or streaming, leaves it");

		// ---------- wake checks and the network ----------
		Check(WakeCheckPolicy.ShouldRetakeFocus(true, false, true, false) && !WakeCheckPolicy.ShouldRetakeFocus(true, true, true, false) && !WakeCheckPolicy.ShouldRetakeFocus(true, false, true, true),
			"wake: Big Picture re-focused once, never over a game");
		Check(WakeCheckPolicy.ShouldResendInput(true, true, false, false) && !WakeCheckPolicy.ShouldResendInput(true, false, false, false)
			&& !WakeCheckPolicy.ShouldResendInput(true, true, true, false) && !WakeCheckPolicy.ShouldResendInput(true, true, false, null),
			"wake: the input switch is re-sent only when Core switched the TV this wake, not streaming, and it shows another input");
		report = Eval(Good() with { LastWake = new WakeCheckResult { At = now, BigPictureUp = true, FocusRetaken = true, TvOnCore = true, InputResent = true, NetworkUp = true } });
		Check(Row(report, "wake-check") is { Level: HealthLevel.Info, State: "fixed" } wk && (string?)wk.Data["temperatures"] == "skipped", "wake: fixed by Core is amber; temperatures skipped");
		Check(Row(Eval(Good() with { LastWake = new WakeCheckResult { At = now, BigPictureUp = false } }), "wake-check").Level == HealthLevel.Attention, "wake: Big Picture not visible");
		report = Eval(Good() with { EthernetUp = false });
		Check(Row(report, "wake-check").State == "network-down" && report.Problems.Single().NoticeKey == "console.network", "network: Ethernet down alerts");
		Check(NetworkRules.Ethernet(new[] { new NetworkAdapter("Ethernet", "Realtek PCIe 2.5GbE", true, true, true), new NetworkAdapter("vEthernet", "Hyper-V Virtual Ethernet Adapter", true, true, true) }) == (true, "Ethernet"),
			"network: the physical adapter, up with a gateway");
		Check(NetworkRules.Ethernet(new[] { new NetworkAdapter("Ethernet", "Intel I225-V", true, false, false), new NetworkAdapter("Tailscale", "Tailscale Tunnel", true, true, true) }) == (false, "Ethernet"),
			"network: a VPN tunnel does not count as the cable");
		Check(NetworkRules.Ethernet(new[] { new NetworkAdapter("Wi-Fi", "Intel AX211", false, true, true) }) == (null, null), "network: no Ethernet adapter, no verdict");

		// ---------- notices ----------
		var due = new[] { new HealthProblem("display-changed", "display", "x", "console.displayChanged"), new HealthProblem("backup-failing", "server-backup", "y", "health.backupFailing") };
		Check(HealthAlertPolicy.NoticeKey(due) == "console.several" && HealthAlertPolicy.NoticeKey(due.Take(1).ToList()) == "console.displayChanged", "notices: mixed problems use the general summary");

		// ---------- Core's state file ----------
		string file = Path.Combine(Path.GetTempPath(), "core-console-" + Guid.NewGuid().ToString("N") + ".json");
		try
		{
			var state = new ConsoleGuardState { Display = lg, Sound = speaker, ExpectedControllers = new() { "28DE:1302" } };
			state = ConsoleGuardStore.RecordPush(ConsoleGuardStore.RecordPush(state with { LauncherPushes = new() { now.AddDays(-1) } }, "Epic Games Launcher", now.AddMinutes(-5)), "EA app", now);
			Check(ConsoleGuardStore.PushesToday(state, now) == 2 && state.LauncherLastPushed == "EA app", "state: pushes counted per day, yesterday's dropped");
			DateTimeOffset boot = now.AddHours(-3);
			state = state with { BitLockerSuspendedAt = now.AddHours(-1), BitLockerSuspendedBoot = boot };
			Check(ConsoleGuardStore.SuspendedThisBoot(state, boot.AddSeconds(30)) is not null && ConsoleGuardStore.SuspendedThisBoot(state, now) is null, "state: a suspension counts until the next boot");
			ConsoleGuardStore.Save(file, state);
			ConsoleGuardState loaded = ConsoleGuardStore.Load(file);
			Check(loaded.Display == lg && loaded.Sound == speaker && loaded.ExpectedControllers.SequenceEqual(state.ExpectedControllers) && loaded.LauncherPushes.Count == 2, "state: round-trips through its file");
			File.WriteAllText(file, "{broken");
			Check(ConsoleGuardStore.Load(file).Display is null, "state: a broken file starts fresh");
		}
		finally { File.Delete(file); }
	}
}
