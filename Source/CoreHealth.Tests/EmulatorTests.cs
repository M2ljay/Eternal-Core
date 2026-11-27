// 3.3.5 tests (Linux-runnable): emulator settings parsers and fixes (EmuDeck's own config shapes), the shared
// hotkey scheme (identical table, no exit combo, no paddles, drift and re-apply), RetroAchievements presence
// without leaking a credential, BIOS rules, the fix guard (backup path, unchanged since read, refused while
// running), emulator update soak/verification/overlay/smoke/rollback, game exit process trees, store
// launcher closing, per-device stream profiles and the back paddle check.
using GamingMode.Services;

static class EmulatorTests
{
	private static EmulatorObservation Obs(string id, Dictionary<string, string?> files, bool running = false, bool? token = null, Dictionary<string, string>? bios = null) => new()
	{
		Def = EmulatorCatalog.Find(id)!, Files = files, Running = running, TokenFileNonEmpty = token, Bios = bios ?? new Dictionary<string, string>(),
	};

	internal static void Run(Action<bool, string> Check)
	{
		DateTimeOffset now = new(2026, 11, 27, 5, 30, 0, TimeSpan.FromHours(-5));

		// ---------- the hotkey table ----------
		Check(EmulatorCatalog.HotkeyTable.Length == 9 && EmulatorCatalog.HotkeyTable.All(row => row.Buttons.StartsWith("Select + ", StringComparison.Ordinal)), "hotkeys: nine functions, all on the Select modifier");
		Check(!EmulatorCatalog.HotkeyTable.Any(row => row.Buttons.Contains("Start", StringComparison.Ordinal) || row.Buttons.Contains("L4") || row.Buttons.Contains("R4") || row.Buttons.Contains("L5") || row.Buttons.Contains("R5")),
			"hotkeys: no Start (EmuDeck's quit) and no back paddle in the table");
		Check(EmulatorCatalog.HotkeyTable.Select(row => row.Buttons).Distinct().Count() == 9, "hotkeys: every combo is used once");
		foreach (EmulatorDef def in EmulatorCatalog.All)
		{
			HotkeyPlan hotkeyPlan = EmulatorCatalog.Hotkeys(def.Id);
			bool covered = EmulatorCatalog.Functions.All(function => hotkeyPlan.Writes.Any(write => write.Function == function) || hotkeyPlan.CantDo.ContainsKey(function));
			Check(covered, $"hotkeys: {def.Id} maps every function or says it can't");
			Check(!hotkeyPlan.Writes.Any(write => write.Value is { } value && (value.Contains("Start") || value.Contains("Paddle"))), $"hotkeys: {def.Id} writes nothing on Start or a paddle");
		}
		Check(EmulatorCatalog.Hotkeys("dolphin").CantDo.Keys.OrderBy(key => key).SequenceEqual(new[] { "fps", "menu", "rewind" }), "hotkeys: Dolphin says it can't rewind, has no menu and no FPS hotkey");
		Check(EmulatorCatalog.Hotkeys("cemu").Unsupported == "no-save-states" && EmulatorCatalog.Hotkeys("azahar").Unsupported == "keyboard-only", "hotkeys: Cemu has no save states, Azahar's hotkeys are keyboard-only");
		Check(EmulatorCatalog.Hotkeys("retroarch").Writes.Single(write => write.Function == "save").Value == "5" && EmulatorCatalog.Hotkeys("retroarch", "sdl2").Writes.Single(write => write.Function == "save").Value == "10",
			"hotkeys: RetroArch button numbers follow its joypad driver");

		// ---------- PCSX2 (EmuDeck's PCSX2.ini shape: repeated keys, keyboard and pad bindings) ----------
		string pcsx2 = "[UI]\r\nConfirmShutdown = true\r\nStartFullscreen = false\r\n\r\n[EmuCore/GS]\r\nDisableShaderCache = false\r\n\r\n[Achievements]\r\nEnabled = true\r\nUsername = someone\r\nToken = SECRET-TOKEN-VALUE\r\n\r\n"
			+ "[Hotkeys]\r\nToggleFullscreen = SDL-0/Back & SDL-0/RightStick\r\nScreenshot = Keyboard/F8\r\nLoadStateFromSlot = SDL-0/Back & SDL-0/LeftShoulder\r\nSaveStateToSlot = SDL-0/Back & SDL-0/RightShoulder\r\nShutdownVM = SDL-0/Back & SDL-0/Start\r\n"
			+ "TogglePause = SDL-0/Back & SDL-0/A\r\nToggleSlowMotion = SDL-0/Back & SDL-0/+LeftTrigger\r\nToggleTurbo = SDL-0/Back & SDL-0/+RightTrigger\r\nHoldTurbo = Keyboard/Period\r\nOpenPauseMenu = SDL-0/Start & SDL-0/LeftStick\r\n"
			+ "ToggleSoftwareRendering = SDL-0/Start & SDL-0/DPadLeft\r\nToggleSoftwareRendering = Keyboard/F9\r\nNextSaveStateSlot = SDL-0/Start & SDL-0/RightShoulder\r\nPreviousSaveStateSlot = SDL-0/Start & SDL-0/LeftShoulder\r\n";
		var p = Obs("pcsx2", new() { ["inis/PCSX2.ini"] = pcsx2, ["inis/secrets.ini"] = null });
		var settings = EmulatorHealthPolicy.Settings(p);
		Check(settings.Single(s => s.Kind == "fullscreen").State == "wrong" && settings.Single(s => s.Kind == "exit-prompt").State == "wrong" && settings.Single(s => s.Kind == "shaders").State == "ok", "pcsx2: fullscreen off and exit prompt on are found, shader cache is on");
		Check(EmulatorHealthPolicy.RetroAchievements(p) == "signed-in", "pcsx2: RetroAchievements signed in");
		var rows = EmulatorHealthPolicy.Rows(p);
		Check(rows.All(row => !System.Text.Json.JsonSerializer.Serialize(row.Data).Contains("SECRET-TOKEN-VALUE") && !System.Text.Json.JsonSerializer.Serialize(row.Data).Contains("someone")), "pcsx2: neither the token nor the user name reaches a Health row");
		Check(rows[0] is { Id: "emulator-pcsx2", Level: HealthLevel.Info } && (string?)rows[0].Data["fix"] == "emu-fix-pcsx2", "pcsx2: amber row with a one-tap fix");
		var fixedSettings = EmulatorHealthPolicy.ApplySettings(p);
		string fixedIni = fixedSettings["inis/PCSX2.ini"]!;
		Check(fixedIni.Contains("ConfirmShutdown = false\r\n") && fixedIni.Contains("StartFullscreen = true\r\n") && fixedIni.Replace("ConfirmShutdown = false", "ConfirmShutdown = true").Replace("StartFullscreen = true", "StartFullscreen = false") == pcsx2,
			"pcsx2: the fix changes exactly two values and keeps CRLF and everything else");
		Check(EmulatorHealthPolicy.Hotkeys(p) is { State: "drifted" } drift && drift.Drifted.Contains("slot-next") && drift.Drifted.Contains("guard:ShutdownVM"), "pcsx2: EmuDeck's hotkeys drift from the scheme (slots on Start, a quit combo)");
		string applied = EmulatorHealthPolicy.ApplyHotkeys(p)["inis/PCSX2.ini"]!;
		var p2 = Obs("pcsx2", new() { ["inis/PCSX2.ini"] = applied, ["inis/secrets.ini"] = null });
		Check(EmulatorHealthPolicy.Hotkeys(p2).State == "matches", "pcsx2: after the re-apply the scheme matches");
		Check(!applied.Contains("SDL-0/Back & SDL-0/Start") && !applied.Contains("ToggleTurbo = SDL") && applied.Contains("HoldTurbo = Keyboard/Period") && applied.Contains("HoldTurbo = SDL-0/Back & SDL-0/+RightTrigger")
			&& applied.Contains("ToggleSoftwareRendering = Keyboard/F9") && applied.Contains("Screenshot = Keyboard/F8"), "pcsx2: quit removed, turbo moved, keyboard bindings kept");
		Check(applied.Contains("NextSaveStateSlot = SDL-0/Back & SDL-0/DPadRight") && applied.Contains("OpenPauseMenu = SDL-0/Back & SDL-0/Y"), "pcsx2: slots and menu on the shared combos");

		// ---------- Dolphin ----------
		string dolphin = "[Display]\nFullscreen = True\n[Interface]\nConfirmStop = False\n";
		string gfx = "[Settings]\nShaderCompilationMode = 2\n";
		string hotkeys = "[Hotkeys]\nDevice = XInput/0/Gamepad\nGeneral/Toggle Pause = @(Back+`Button A`)\nGeneral/Stop = @(Back+`Button X`)\nGeneral/Exit = @(Back+Start)\nLoad State/Load from Selected Slot = @(Back+`Shoulder L`)\n"
			+ "Save State/Save to Selected Slot = @(Back+`Shoulder R`)\nOther State Hotkeys/Increase Selected State Slot = @(Start+`Shoulder R`)\nEmulation Speed/Disable Emulation Speed Limit = @(Start+`Trigger R`)\nEmulation Speed/Increase Emulation Speed = @(Back+`Trigger R`)\n";
		var d = Obs("dolphin", new() { ["User/Config/Dolphin.ini"] = dolphin, ["User/Config/GFX.ini"] = gfx, ["User/Config/Hotkeys.ini"] = hotkeys, ["User/Config/RetroAchievements.ini"] = "[Achievements]\nEnabled = True\nUsername = x\n" });
		Check(EmulatorHealthPolicy.Settings(d).Single(s => s.Kind == "shaders") is { State: "wrong", Writable: true }, "dolphin: Compile Shaders Before Starting is off and fixable");
		Check(EmulatorHealthPolicy.ApplySettings(d)["User/Config/GFX.ini"] == "[Settings]\nShaderCompilationMode = 2\nWaitForShadersBeforeStarting = True\n", "dolphin: the absent key is added at the end of its section");
		Check(EmulatorHealthPolicy.RetroAchievements(d) == "signed-out", "dolphin: a user name without a token is signed out");
		string dolphinApplied = EmulatorHealthPolicy.ApplyHotkeys(d)["User/Config/Hotkeys.ini"]!;
		Check(dolphinApplied.Contains("General/Exit = \n") && dolphinApplied.Contains("General/Stop = \n") && dolphinApplied.Contains("Emulation Speed/Increase Emulation Speed = \n")
			&& dolphinApplied.Contains("Emulation Speed/Disable Emulation Speed Limit = @(Back+`Trigger R`)") && dolphinApplied.Contains("Other State Hotkeys/Decrease Selected State Slot = @(Back+`Pad W`)"),
			"dolphin: exit and stop unbound, speed-up moved off Select+R2, slots on the D-pad");
		Check(EmulatorHealthPolicy.Hotkeys(d with { Files = new Dictionary<string, string?>(d.Files) { ["User/Config/Hotkeys.ini"] = dolphinApplied } }).State == "matches", "dolphin: matches after the re-apply");

		// ---------- RetroArch, Azahar (Qt defaults), RPCS3 (YAML), Ryujinx (JSON), Cemu (XML), xemu (TOML) ----------
		string ra = "input_enable_hotkey_btn = \"7\"\ninput_quit_gamepad_combo = \"4\"\nvideo_fullscreen = \"true\"\ncheevos_enable = \"true\"\ncheevos_username = \"u\"\ncheevos_token = \"t\"\n";
		var r = Obs("retroarch", new() { ["retroarch.cfg"] = ra });
		Check(EmulatorHealthPolicy.Settings(r).Single(s => s.Kind == "fullscreen").State == "ok" && EmulatorHealthPolicy.RetroAchievements(r) == "signed-in", "retroarch: fullscreen and signed in");
		string raApplied = EmulatorHealthPolicy.ApplyHotkeys(r)["retroarch.cfg"]!;
		Check(raApplied.Contains("input_quit_gamepad_combo = \"0\"") && raApplied.Contains("input_exit_emulator_btn = \"nul\"") && raApplied.Contains("input_menu_toggle_btn = \"3\""), "retroarch: quit combo off, menu on Select+Y");
		Check(EmulatorHealthPolicy.Hotkeys(r with { Files = new Dictionary<string, string?> { ["retroarch.cfg"] = raApplied } }).State == "matches", "retroarch: matches after the re-apply");

		string qt = "[UI]\nfullscreen\\default=true\nfullscreen=true\nconfirmClose\\default=false\nconfirmClose=false\n[Renderer]\nuse_disk_shader_cache\\default=true\nuse_disk_shader_cache=true\n";
		var a = Obs("azahar", new() { ["user/config/qt-config.ini"] = qt });
		Check(EmulatorHealthPolicy.Settings(a).Single(s => s.Kind == "fullscreen").State == "wrong", "azahar: fullscreen=true with \\default=true means the default (windowed) applies");
		string qtFixed = EmulatorHealthPolicy.ApplySettings(a)["user/config/qt-config.ini"]!;
		Check(qtFixed.Contains("fullscreen\\default=false\nfullscreen=true"), "azahar: the fix clears the default flag in Qt's own spelling");

		var y = Obs("rpcs3", new() { ["config/config.yml"] = "Video:\n  Shader Mode: Async\nMiscellaneous:\n  Exit RPCS3 when process finishes: false\n  Start games in fullscreen mode: false\n" });
		Check(EmulatorHealthPolicy.ApplySettings(y)["config/config.yml"]!.Contains("  Start games in fullscreen mode: true\n"), "rpcs3: the YAML key is set in place");
		var j = Obs("ryujinx", new() { ["portable/Config.json"] = "{\n  \"show_confirm_exit\": true,\n  \"enable_shader_cache\": true,\n  \"start_fullscreen\": false\n}" });
		string json = EmulatorHealthPolicy.ApplySettings(j)["portable/Config.json"]!;
		Check(json.Contains("\"show_confirm_exit\": false") && json.Contains("\"start_fullscreen\": true"), "ryujinx: JSON values set in place");
		var x = Obs("cemu", new() { ["settings.xml"] = "<content>\n    <fullscreen_menubar>true</fullscreen_menubar>\n    <fullscreen>false</fullscreen>\n</content>" });
		Check(EmulatorHealthPolicy.ApplySettings(x)["settings.xml"]!.Contains("<fullscreen>true</fullscreen>") && EmulatorHealthPolicy.ApplySettings(x)["settings.xml"]!.Contains("<fullscreen_menubar>true</fullscreen_menubar>"), "cemu: the right XML element");
		var t = Obs("xenia", new() { ["xenia-canary.config.toml"] = "[Display]\nfullscreen = false                                	# Whether to launch the emulator in fullscreen.\n" });
		Check(EmulatorHealthPolicy.ApplySettings(t)["xenia-canary.config.toml"] == "[Display]\nfullscreen = true                                	# Whether to launch the emulator in fullscreen.\n", "xenia: TOML value set, its comment kept");
		Check(EmulatorHealthPolicy.Settings(Obs("ppsspp", new() { ["memstick/PSP/SYSTEM/ppsspp.ini"] = null }))[0].State == "no-config", "ppsspp: a settings file the emulator has not written yet is not created by Core");
		Check(EmulatorHealthPolicy.RetroAchievements(Obs("ppsspp", new() { ["memstick/PSP/SYSTEM/ppsspp.ini"] = "[Achievements]\nAchievementsEnable = True\nAchievementsUserName = u\n" }, token: true)) == "signed-in", "ppsspp: user name plus a non-empty token file");

		// ---------- PPSSPP controls.ini (EmuDeck's shape) ----------
		string controls = "[ControlMapping]\nSave State = 20-109:20-103\nLoad State = 20-109:20-102\nPause = 1-111,20-108:20-109\nExit App = 20-108:20-109\n";
		var pp = Obs("ppsspp", new() { ["memstick/PSP/SYSTEM/controls.ini"] = controls });
		string ppApplied = EmulatorHealthPolicy.ApplyHotkeys(pp)["memstick/PSP/SYSTEM/controls.ini"]!;
		Check(ppApplied.Contains("Pause = 1-111,20-109:20-99") && ppApplied.Contains("Exit App = \n") && ppApplied.Contains("Rewind = 20-109:20-4034"), "ppsspp: keyboard binding kept, menu on Select+Y, exit unbound, rewind added");
		Check(EmulatorHealthPolicy.Hotkeys(pp with { Files = new Dictionary<string, string?> { ["memstick/PSP/SYSTEM/controls.ini"] = ppApplied } }).State == "matches", "ppsspp: matches after the re-apply");

		// ---------- the fix guard ----------
		Check(EmulatorFixGuard.Decide(true, "a", "a", "x", "y") == FixRefusal.Running, "guard: refused while the emulator runs");
		Check(EmulatorFixGuard.Decide(false, "a", "b", "x", "y") == FixRefusal.Changed, "guard: refused when the file changed since it was read");
		Check(EmulatorFixGuard.Decide(false, "a", "a", null, "y") == FixRefusal.Unsafe && EmulatorFixGuard.Decide(false, "a", "a", "y", "y") == FixRefusal.NoChange && EmulatorFixGuard.Decide(false, "a", "a", "x", "y") == FixRefusal.None, "guard: unsafe, no change, ok");
		Check(EmulatorFixGuard.BackupPath("/b", "pcsx2", now, "inis/PCSX2.ini").EndsWith(Path.Combine("pcsx2", "20261127-053000", "inis", "PCSX2.ini")), "guard: backups by emulator and time");
		Check(EmulatorStateStore.ShouldAutoApply(new EmulatorState(), "pcsx2", false) && !EmulatorStateStore.ShouldAutoApply(new EmulatorState { Known = new() { "pcsx2" } }, "pcsx2", false)
			&& EmulatorStateStore.ShouldAutoApply(new EmulatorState { Known = new() { "pcsx2" }, UpdatedSinceHotkeys = new() { "pcsx2" } }, "pcsx2", false), "auto-apply: new emulator and after an update, not a drift the user made");

		// ---------- BIOS, EmuDeck settings, launcher scripts ----------
		var ps2 = EmulatorCatalog.Find("pcsx2")!.Bios[0];
		Check(EmulatorHealthPolicy.BiosState(ps2, new (string, string, Func<string?>)[] { ("x.bin", "", () => "32F2E4D5FF5EE11072A6BC45530F5765") }) == "ok"
			&& EmulatorHealthPolicy.BiosState(ps2, new (string, string, Func<string?>)[] { ("scph10000.bin", "", () => "00") }) == "missing", "bios: PS2 judged by EmuDeck's MD5 list, not by name");
		var dc = EmulatorCatalog.Find("retroarch")!.Bios.Single(spec => spec.System == "dreamcast");
		Check(EmulatorHealthPolicy.BiosState(dc, new (string, string, Func<string?>)[] { ("dc_boot.bin", "dc", () => null) }) == "ok" && EmulatorHealthPolicy.BiosState(dc, new (string, string, Func<string?>)[] { ("dc_boot.bin", "", () => null) }) == "missing", "bios: Dreamcast in bios\\dc");
		Check(EmulatorHealthPolicy.ParseEmuDeckSettings("$biosPath=\"C:\\Emulation\\bios\"\r\n$romsPath=\"C:\\Emulation\\roms\"")["biosPath"] == @"C:\Emulation\bios", "emudeck: settings.ps1");
		Check(EmulatorHealthPolicy.LauncherEmulatorFile("$emulatorFile = \"$env:APPDATA/emudeck/Emulators/PCSX2-Qt/pcsx2-qtx64.exe\"", @"C:\Users\V\AppData\Roaming", @"C:\Users\V") == @"C:\Users\V\AppData\Roaming\emudeck\Emulators\PCSX2-Qt\pcsx2-qtx64.exe", "emudeck: launcher script names the emulator");
		Check(EmulatorHealthPolicy.ByExe(@"C:\x\PCSX2-Qt\pcsx2-qtx64.exe")?.Id == "pcsx2" && EmulatorHealthPolicy.ByExe("notepad.exe") is null, "emudeck: emulator by exe name");

		// ---------- emulator updates ----------
		UpdateSource cemu = EmulatorUpdateSources.Find("cemu")!;
		string releaseJson = "{\"tag_name\":\"v2.6\",\"draft\":false,\"prerelease\":false,\"published_at\":\"2026-11-01T10:00:00Z\",\"assets\":[{\"name\":\"cemu-2.6-windows-x64.zip\",\"browser_download_url\":\"https://github.com/cemu-project/Cemu/releases/download/v2.6/cemu-2.6-windows-x64.zip\",\"size\":1000,\"digest\":\"sha256:" + new string('a', 64) + "\"},{\"name\":\"cemu-2.6-ubuntu.zip\",\"browser_download_url\":\"https://github.com/cemu-project/Cemu/releases/download/v2.6/cemu-2.6-ubuntu.zip\",\"size\":5}]}";
		ReleaseInfo release = EmulatorUpdateSources.ParseRelease(releaseJson)!;
		ReleaseAsset asset = EmulatorUpdateSources.PickAsset(cemu, release)!;
		Check(asset.Name == "cemu-2.6-windows-x64.zip" && EmulatorUpdateSources.GitHubSha256(asset.Digest) == new string('a', 64), "updates: the Windows asset and GitHub's digest");
		Check(EmulatorUpdateSources.PickAsset(cemu, release with { Assets = new[] { asset with { Url = "https://evil.example/cemu-windows-x64.zip" } } }) is null, "updates: only GitHub's release host of the official project");
		var entry = EmulatorUpdatePolicy.Observe(new EmulatorUpdateEntry(), cemu, release, asset, null, now);
		Check(entry.Installed == "v2.6" && entry.Pending is null, "updates: the first release seen is taken as installed (no surprise update)");
		entry = EmulatorUpdatePolicy.Observe(entry with { Installed = "v2.5" }, cemu, release, asset, null, now);
		Check(entry.Pending is { Version: "v2.6", Verification: UpdateVerification.GitHubOnly }, "updates: a new version is pending; GitHub's digest alone needs the user's OK");
		Check(EmulatorUpdatePolicy.Phase("cemu", true, entry, 10, now.AddDays(11)) == EmulatorUpdatePhase.NeedsConfirmation, "updates: no confirmation, no update");
		entry = EmulatorUpdatePolicy.Confirm(entry, "v2.6");
		Check(EmulatorUpdatePolicy.Phase("cemu", true, entry, 10, now.AddDays(9)) == EmulatorUpdatePhase.Soaking && EmulatorUpdatePolicy.Phase("cemu", true, entry, 10, now.AddDays(10)) == EmulatorUpdatePhase.Due, "updates: 10-day soak per version");
		Check(EmulatorUpdatePolicy.MayRunAutomatically(EmulatorUpdatePhase.Due, false, false, false, now) && !EmulatorUpdatePolicy.MayRunAutomatically(EmulatorUpdatePhase.Due, true, false, false, now)
			&& !EmulatorUpdatePolicy.MayRunAutomatically(EmulatorUpdatePhase.Due, false, false, false, now.AddHours(3)), "updates: only on an update night, idle");
		var failed = EmulatorUpdatePolicy.Failed(entry, "v2.6", "crashed", true);
		Check(EmulatorUpdatePolicy.Phase("cemu", true, failed, 10, now.AddDays(30)) == EmulatorUpdatePhase.WaitingForChange && failed.LastResult == "rolled-back:crashed", "updates: after a rollback the same version is not retried");
		Check(EmulatorUpdatePolicy.RefuseManual(EmulatorUpdatePhase.WaitingForChange, false) is null && EmulatorUpdatePolicy.RefuseManual(EmulatorUpdatePhase.Due, true) == "not-while-running"
			&& EmulatorUpdatePolicy.RefuseManual(EmulatorUpdatePhase.NeedsConfirmation, false) == "needs-confirmation", "updates: manual update skips soak and no-retry, never the confirmation or the running check");
		Check(EmulatorUpdatePolicy.Phase("retroarch", true, null, 10, now) == EmulatorUpdatePhase.NotOffered, "updates: RetroArch is left to EmuDeck");
		var withSums = release with { Assets = release.Assets.Append(new ReleaseAsset("SHA256SUMS", "https://github.com/cemu-project/Cemu/releases/download/v2.6/SHA256SUMS", 100, null)).ToList() };
		Check(EmulatorUpdateSources.ChecksumAsset(withSums, asset)?.Name == "SHA256SUMS" && EmulatorUpdateSources.ChecksumFor(new string('b', 64) + "  cemu-2.6-windows-x64.zip\n", asset.Name, false) == new string('b', 64), "updates: a project's own checksum file is used");
		var overlay = EmulatorUpdatePolicy.PlanOverlay(cemu, new[] { "Cemu_2.6/Cemu.exe", "Cemu_2.6/resources/x.png", "Cemu_2.6/settings.xml", "Cemu_2.6/mlc01/sys/a", "Cemu_2.6/../evil.dll" });
		Check(overlay.Select(item => item.To).SequenceEqual(new[] { "Cemu.exe", "resources/x.png" }), "updates: top folder stripped, user data and escapes skipped");
		var pcsx2Overlay = EmulatorUpdatePolicy.PlanOverlay(EmulatorUpdateSources.Find("pcsx2")!, new[] { "pcsx2-qt.exe", "translations/x.qm", "inis/PCSX2.ini" });
		Check(pcsx2Overlay.Select(item => item.To).SequenceEqual(new[] { "pcsx2-qtx64.exe", "translations/x.qm" }), "updates: EmuDeck's rename of pcsx2-qt.exe, inis untouched");
		Check(EmulatorUpdatePolicy.SmokeFailure(true, true, true, null, true) is null && EmulatorUpdatePolicy.SmokeFailure(true, true, true, 0, false) is null
			&& EmulatorUpdatePolicy.SmokeFailure(true, true, true, -1073741515, false) == "crashed" && EmulatorUpdatePolicy.SmokeFailure(false, false, false, null, false) == "exe-missing", "updates: smoke test verdicts");

		// ---------- game exit ----------
		DateTimeOffset launch = now;
		var steam = new ProcessSeen(100, 4, "steam.exe", null, now.AddHours(-2), true);
		var wrapper = new ProcessSeen(200, 100, "powershell.exe", null, launch.AddSeconds(1), false);
		var emu = new ProcessSeen(300, 200, "pcsx2-qtx64.exe", null, launch.AddSeconds(3), true);
		var old = new ProcessSeen(400, 100, "notepad.exe", null, now.AddHours(-1), true);
		var epic = new ProcessSeen(500, 200, "EpicGamesLauncher.exe", null, launch.AddSeconds(5), true);
		var session = GameExitPolicy.Observe(new GameSession { AppId = 1, StartedAt = launch }, new[] { steam, wrapper, emu, old, epic }, 100);
		Check(session.Tree.Select(member => member.Pid).OrderBy(pid => pid).SequenceEqual(new[] { 200, 300 }), "exit: the launch tree is Steam's new child and its children, never older processes or a store launcher");
		var afterExit = new[] { steam, emu, old };
		var decision = GameExitPolicy.Decide(session with { ExitChosen = true }, afterExit);
		Check(decision is { Action: LeftoverAction.Close, Reason: "exit-chosen" } && decision.Processes.Single().Pid == 300, "exit: Exit Game chosen closes the emulator the wrapper left");
		Check(GameExitPolicy.Decide(session, afterExit) is { Action: LeftoverAction.Leave, Reason: "steam-lost-track" }, "exit: Steam ended the wrapper by itself while the game is on screen: left, Core offers its own Exit");
		Check(GameExitPolicy.Decide(session, new[] { steam, emu with { HasWindow = false } }) is { Action: LeftoverAction.Close, Reason: "steam-ended" }, "exit: a windowless leftover is closed");
		Check(GameExitPolicy.Decide(session, new[] { steam, emu with { Started = emu.Started.AddSeconds(30) } }) is { Reason: "nothing-left" }, "exit: a reused pid is not the game");
		Check(GameExitPolicy.CloseOrder(session.Tree, new[] { steam, wrapper, emu }).First().Pid == 300, "exit: the emulator closes before its wrapper");
		Check(GameExitPolicy.Observe(new GameSession { StartedAt = launch }, new[] { steam, new ProcessSeen(600, 1, "Dolphin.exe", null, launch.AddSeconds(2), true) }, 100).Tree.Count == 1, "exit: a catalogue emulator started during the launch joins even without a parent link");
		Check(GameExitPolicy.Row(new GameExitRecord("Ico", now, 0, 1, "terminate-failed", new[] { "x.exe" }), null, now) is { Level: HealthLevel.Attention, State: "did-not-exit" }
			&& GameExitPolicy.Row(null, "Ico", now) is { State: "untracked" } untracked && (string?)untracked.Data["fix"] == "game-exit-now", "exit: Health reports a game that did not exit, and offers Core's Exit when Steam lost track");

		// ---------- store launchers ----------
		LauncherCloseInput Input(bool before = false, bool started = true, bool other = false, long written = 0, int seconds = 120) => new(true, before, started, other, false, written, TimeSpan.FromSeconds(seconds));
		Check(LauncherClosePolicy.Decide(Input()) == LauncherCloseAction.Close, "launchers: started for the game, quiet after the grace: closed");
		Check(LauncherClosePolicy.Decide(Input(before: true)) == LauncherCloseAction.Leave && LauncherClosePolicy.Decide(Input(other: true)) == LauncherCloseAction.Leave, "launchers: running before, or another game of it running: left");
		Check(LauncherClosePolicy.Decide(Input(written: 100L * 1024 * 1024)) == LauncherCloseAction.Leave && LauncherClosePolicy.Decide(Input(seconds: 30)) == LauncherCloseAction.Wait, "launchers: busy writing (a download) is left; within the grace Core waits");
		Check(LauncherClosePolicy.Decide(Input() with { Enabled = false }) == LauncherCloseAction.Leave && LauncherCatalog.Enabled(null, "epic") && !LauncherCatalog.Enabled(new Dictionary<string, bool> { ["epic"] = false }, "epic"), "launchers: per-launcher switch, on by default");
		Check(LauncherCatalog.ByProcess("EADesktop.exe")?.Id == "ea" && LauncherCatalog.ByProcess("upc")?.Id == "ubisoft" && LauncherCatalog.ByProcess("game.exe") is null, "launchers: catalogue by process name");

		// ---------- stream profiles ----------
		string[] roots = { @"C:\Users\V" };
		var profileSettings = new StreamProfileSettings { Games = new() { new StreamProfileGame { Name = "Cyberpunk", Process = "Cyberpunk2077.exe", Files = new() { @"C:\Users\V\AppData\Local\CDPR\UserSettings.json", @"C:\Windows\win.ini" } } } };
		var plan = StreamProfilePolicy.Plan(profileSettings, "tv", "steam-frame", _ => false, (_, client, _) => client == "steam-frame", roots);
		Check(plan.Waiting is null && plan.Actions.Select(action => action.Kind + ":" + action.Client).SequenceEqual(new[] { "snapshot:tv", "restore:steam-frame" }), "profiles: snapshot for the TV, restore the Frame's copy; a file outside the user folders is ignored");
		Check(StreamProfilePolicy.Plan(profileSettings, "tv", "steam-frame", _ => true, (_, _, _) => true, roots).Waiting == "Cyberpunk", "profiles: never while the game runs");
		Check(StreamProfilePolicy.Plan(profileSettings, "tv", "tv", _ => false, (_, _, _) => true, roots).Actions.Count == 0, "profiles: no device change, nothing to do");
		Check(StreamProfilePolicy.ClientKey(true, "Steam Frame", "steam") == "steam-frame" && StreamProfilePolicy.ClientKey(false, "x", null) == "tv", "profiles: device keys");

		// ---------- back paddles ----------
		string vdf = "\"controller_mappings\"\n{\n\t\"group\"\n\t{\n\t\t\"inputs\"\n\t\t{\n\t\t\t\"button_back_left\"\n\t\t\t{\n\t\t\t\t\"activators\"\n\t\t\t\t{\n\t\t\t\t\t\"Full_Press\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"bindings\"\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\"binding\"\t\t\"xinput_button A\"\n\t\t\t\t\t\t}\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n"
			+ "\t\t\t\"button_back_right\"\n\t\t\t{\n\t\t\t\t\"activators\"\n\t\t\t\t{\n\t\t\t\t\t\"Full_Press\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"bindings\"\n\t\t\t\t\t\t{\n\t\t\t\t\t\t\t\"binding\"\t\t\"xinput_button B\"\n\t\t\t\t\t\t}\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n";
		Check(BackPaddlePolicy.Verdict(vdf) == "a-b" && BackPaddlePolicy.Verdict(vdf.Replace("xinput_button B", "xinput_button X")) == "other" && BackPaddlePolicy.Verdict("\"x\" {}") == "unbound", "paddles: L4 = A and R4 = B read from a Steam Input layout");
		Check(BackPaddlePolicy.Row(new[] { ("big-picture", "a-b"), ("desktop", "unbound") }) is { State: "not-set" } paddles && (string?)paddles.Data["fix"] == "paddles-setup", "paddles: a layout without the mapping offers the setup");
	}
}
