using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core: emulator health. For the emulators EmuDeck for Windows installs, Core reports, read-only by
// default: starts fullscreen, no exit confirmation, shader pre-compilation where offered, RetroAchievements
// signed in (presence only), BIOS/firmware present, the installed version, and whether the shared controller
// hotkeys are in place. Fixes are text edits computed here; the agent backs the file up, checks it is
// unchanged since it was read and refuses while the emulator runs (EmulatorHealth.Windows.cs).
// Pure: text in, verdicts and new text out. See docs/eternal/CONSOLE-GUARDS.md ("Emulators").
//
// Sources: EmuDeck for Windows (github.com/EmuDeck/emudeck-we): vars.ps1 ($emusPath =
// %APPDATA%\EmuDeck\Emulators), tools\launchers\*.ps1 ($emulatorFile), configs\* (the settings files and
// keys below), functions\checkBIOS.ps1 (the BIOS lists). The Collector's domain/retroachievements.py for the
// RetroAchievements files.

internal enum ConfigFormat { Ini, IniMulti, RetroArchCfg, QtIni, Toml, Yaml, Xml, Json, Ppsspp }

// A key in an emulator's settings file. File is relative to the emulator's folder, '/'-separated.
internal sealed record ConfigKey(string File, ConfigFormat Format, string Section, string Key);

// "fullscreen", "exit-prompt" or "shaders". Default is what the emulator does when the key is absent.
internal sealed record SettingCheck(string Kind, ConfigKey Where, string Wanted, string Default);

internal sealed record RaSpec(ConfigKey Enabled, ConfigKey User, IReadOnlyList<ConfigKey> Token, string? TokenFile = null);

// One system's BIOS: file names (any one of them counts), a folder below the BIOS root to look in, and the
// MD5 values EmuDeck's checker accepts (empty: presence of a name only).
internal sealed record BiosSpec(string System, IReadOnlyList<string> Names, IReadOnlyList<string> Md5, string? Folder = null, bool InEmulator = false, bool Directory = false);

// One write of the shared hotkey scheme. Function null: a guard write (an exit combo removed, or another
// action moved off a combo the table reserves). Value null: remove the controller bindings of that key.
internal sealed record HotkeyWrite(string? Function, ConfigKey Where, string? Value);

internal sealed record HotkeyPlan(IReadOnlyList<HotkeyWrite> Writes, IReadOnlyDictionary<string, string> CantDo, string? Unsupported = null);

internal sealed record EmulatorDef(string Id, string Name, string Folder, IReadOnlyList<string> Exes, IReadOnlyList<SettingCheck> Settings, RaSpec? Ra,
	IReadOnlyList<BiosSpec> Bios, string? BiosNote = null);

internal static class EmulatorCatalog
{
	// The shared controller hotkey scheme: one table, identical in every emulator that has the function.
	// Select (Back/View) is the hold modifier, as in EmuDeck's own scheme. Nothing quits an emulator: games are
	// closed only through Steam's Exit Game. The back paddles (L4, R4, L5, R5) are never used: L4 is A, R4 is B
	// and L5+R5 is Jarvis. Buttons are named as on an Xbox pad (A bottom, B right, X left, Y top), which is how
	// Steam Input presents every controller to an emulator. Keep in sync with src/emulatorHotkeys.ts
	// (tests/coreEmulators.test.mjs compares them).
	internal static readonly (string Function, string Buttons)[] HotkeyTable =
	{
		("save", "Select + R1"),
		("load", "Select + L1"),
		("slot-next", "Select + D-pad right"),
		("slot-prev", "Select + D-pad left"),
		("fast-forward", "Select + R2 (hold)"),
		("rewind", "Select + L2 (hold)"),
		("pause", "Select + A"),
		("menu", "Select + Y"),
		("fps", "Select + X"),
	};

	internal static readonly string[] Functions = HotkeyTable.Select(row => row.Function).ToArray();

	private static ConfigKey K(string file, ConfigFormat format, string section, string key) => new(file, format, section, key);

	private static readonly string[] Ps1Md5 = { "239665b1a3dade1b5a52c06338011044", "2118230527a9f51bd9216e32fa912842", "849515939161e62f6b866f6853006780", "dc2b9bf8da62ec93e868cfd29f0d067d", "54847e693405ffeb0359c6287434cbef", "cba733ceeff5aef5c32254f1d617fa62", "da27e8b6dab242d8f91a9b25d80c63b8", "417b34706319da7cf001e76e40136c23", "57a06303dfa9cf9351222dfcbb4a29d9", "81328b966e6dcf7ea1e32e55e1c104bb", "924e392ed05558ffdb115408c263dccf", "e2110b8a2b97a8e0b857a45d32f7e187", "ca5cfc321f916756e3f0effbfaeba13b", "8dd7d5296a650fac7319bce665a6a53c", "490f666e1afb15b7362b406ed1cea246", "32736f17079d0b2b7024407c39bd3050", "8e4c14f567745eff2f0408c8129f72a6", "b84be139db3ee6cbd075630aa20a6553", "1e68c231d0896b7eadcad1d7d8e76129", "b9d9a0286c33dc6b7237bb13cd46fdee", "8abc1b549a4a80954addc48ef02c4521", "9a09ab7e49b422c007e6d54d7c49b965", "b10f5e0e3d9eb60e5159690680b1e774", "6e3735ff4c7dc899ee98981385f6f3d0", "de93caec13d1a141a40a79f5c86168d6", "c53ca5908936d412331790f4426c6c33", "476d68a94ccec3b9c8303bbd1daf2810", "d8f485717a5237285e4d7c5f881b7f32", "fbb5f59ec332451debccf1e377017237", "81bbe60ba7a3d1cea1d48c14cbcc647b" };
	private static readonly string[] Ps2Md5 = { "32f2e4d5ff5ee11072a6bc45530f5765", "acf4730ceb38ac9d8c7d8e21f2614600", "acf9968c8f596d2b15f42272082513d1", "b1459d7446c69e3e97e6ace3ae23dd1c", "d3f1853a16c2ec18f3cd1ae655213308", "63e6fd9b3c72e0d7b920e80cf76645cd", "a20c97c02210f16678ca3010127caf36", "8db2fbbac7413bf3e7154c1e0715e565", "91c87cb2f2eb6ce529a2360f80ce2457", "3016b3dd42148a67e2c048595ca4d7ce", "b7fa11e87d51752a98b38e3e691cbf17", "f63bc530bd7ad7c026fcd6f7bd0d9525", "cee06bd68c333fc5768244eae77e4495", "0bf988e9c7aaa4c051805b0fa6eb3387", "8accc3c49ac45f5ae2c5db0adc854633", "6f9a6feb749f0533aaae2cc45090b0ed", "838544f12de9b0abc90811279ee223c8", "bb6bbc850458fff08af30e969ffd0175", "815ac991d8bc3b364696bead3457de7d", "b107b5710042abe887c0f6175f6e94bb", "ab55cceea548303c22c72570cfd4dd71", "18bcaadb9ff74ed3add26cdf709fff2e", "491209dd815ceee9de02dbbc408c06d6", "7200a03d51cacc4c14fcdfdbc4898431", "8359638e857c8bc18c3c18ac17d9cc3c", "352d2ff9b3f68be7e6fa7e6dd8389346", "d5ce2c7d119f563ce04bc04dbc3a323e", "0d2228e6fd4fb639c9c39d077a9ec10c", "72da56fccb8fcd77bba16d1b6f479914", "5b1f47fbeb277c6be2fccdd6344ff2fd", "315a4003535dfda689752cb25f24785c", "312ad4816c232a9606e56f946bc0678a", "666018ffec65c5c7e04796081295c6c7", "6e69920fa6eef8522a1d688a11e41bc6", "eb960de68f0c0f7f9fa083e9f79d0360", "8aa12ce243210128c5074552d3b86251", "240d4c5ddd4b54069bdc4a3cd2faf99d", "1c6cd089e6c83da618fbf2a081eb4888", "463d87789c555a4a7604e97d7db545d1", "35461cecaa51712b300b2d6798825048", "bd6415094e1ce9e05daabe85de807666", "2e70ad008d4ec8549aada8002fdf42fb", "b53d51edc7fc086685e31b811dc32aad", "1b6e631b536247756287b916f9396872", "00da1b177096cfd2532c8fa22b43e667", "afde410bd026c16be605a1ae4bd651fd", "81f4336c1de607dd0865011c0447052e", "0eee5d1c779aa50e94edd168b4ebf42e", "d333558cc14561c1fdc334c75d5f37b7", "dc752f160044f2ed5fc1f4964db2a095", "63ead1d74893bf7f36880af81f68a82d", "3e3e030c0f600442fa05b94f87a1e238", "1ad977bb539fc9448a08ab276a836bbc", "eb4f40fcf4911ede39c1bbfe91e7a89a", "9959ad7a8685cad66206e7752ca23f8b", "929a14baca1776b00869f983aa6e14d2", "573f7d4a430c32b3cc0fd0c41e104bbd", "df63a604e8bff5b0599bd1a6c2721bd0", "5b1ba4bb914406fae75ab8e38901684d", "cb801b7920a7d536ba07b6534d2433ca", "af60e6d1a939019d55e5b330d24b1c25", "549a66d0c698635ca9fa3ab012da7129", "5de9d0d730ff1e7ad122806335332524", "21fe4cad111f7dc0f9af29477057f88d", "40c11c063b3b9409aa5e4058e984e30c", "80bbb237a6af9c611df43b16b930b683", "c37bce95d32b2be480f87dd32704e664", "80ac46fa7e77b8ab4366e86948e54f83", "21038400dc633070a78ad53090c53017", "dc69f0643a3030aaa4797501b483d6c4", "30d56e79d89fbddf10938fa67fe3f34e", "93ea3bcee4252627919175ff1b16a1d9", "d3e81e95db25f5a86a7b7474550a2155" };

	private static readonly BiosSpec Ps1 = new("ps1", Array.Empty<string>(), Ps1Md5);
	private static readonly BiosSpec Ps2 = new("ps2", Array.Empty<string>(), Ps2Md5);

	internal static readonly IReadOnlyList<EmulatorDef> All = new List<EmulatorDef>
	{
		new("retroarch", "RetroArch", "RetroArch", new[] { "retroarch.exe" },
			new[]
			{
				new SettingCheck("fullscreen", K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "video_fullscreen"), "true", "false"),
				new SettingCheck("exit-prompt", K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "quit_press_twice"), "false", "true"),
			},
			new RaSpec(K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "cheevos_enable"), K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "cheevos_username"),
				new[] { K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "cheevos_token"), K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "cheevos_password") }),
			new[]
			{
				Ps1,
				new BiosSpec("segacd", new[] { "bios_CD_E.bin", "bios_CD_J.bin", "bios_CD_U.bin" }, Array.Empty<string>()),
				new BiosSpec("saturn", new[] { "saturn_bios.bin", "sega_101.bin", "mpr-17933.bin" }, Array.Empty<string>()),
				new BiosSpec("dreamcast", new[] { "dc_boot.bin" }, Array.Empty<string>(), "dc"),
				new BiosSpec("nds", new[] { "bios7.bin", "bios9.bin", "firmware.bin" }, Array.Empty<string>()),
			}),
		new("dolphin", "Dolphin", "Dolphin-x64", new[] { "Dolphin.exe" },
			new[]
			{
				new SettingCheck("fullscreen", K("User/Config/Dolphin.ini", ConfigFormat.Ini, "Display", "Fullscreen"), "True", "False"),
				new SettingCheck("exit-prompt", K("User/Config/Dolphin.ini", ConfigFormat.Ini, "Interface", "ConfirmStop"), "False", "True"),
				new SettingCheck("shaders", K("User/Config/GFX.ini", ConfigFormat.Ini, "Settings", "WaitForShadersBeforeStarting"), "True", "False"),
			},
			new RaSpec(K("User/Config/RetroAchievements.ini", ConfigFormat.Ini, "Achievements", "Enabled"), K("User/Config/RetroAchievements.ini", ConfigFormat.Ini, "Achievements", "Username"),
				new[] { K("User/Config/RetroAchievements.ini", ConfigFormat.Ini, "Achievements", "ApiToken") }),
			Array.Empty<BiosSpec>(), "none-needed"),
		new("pcsx2", "PCSX2", "PCSX2-Qt", new[] { "pcsx2-qtx64.exe", "pcsx2-qt.exe" },
			new[]
			{
				new SettingCheck("fullscreen", K("inis/PCSX2.ini", ConfigFormat.Ini, "UI", "StartFullscreen"), "true", "false"),
				new SettingCheck("exit-prompt", K("inis/PCSX2.ini", ConfigFormat.Ini, "UI", "ConfirmShutdown"), "false", "true"),
				new SettingCheck("shaders", K("inis/PCSX2.ini", ConfigFormat.Ini, "EmuCore/GS", "DisableShaderCache"), "false", "false"),
			},
			new RaSpec(K("inis/PCSX2.ini", ConfigFormat.Ini, "Achievements", "Enabled"), K("inis/PCSX2.ini", ConfigFormat.Ini, "Achievements", "Username"),
				new[] { K("inis/PCSX2.ini", ConfigFormat.Ini, "Achievements", "Token"), K("inis/secrets.ini", ConfigFormat.Ini, "Achievements", "Token") }),
			new[] { Ps2 }),
		new("duckstation", "DuckStation", "duckstation", new[] { "duckstation-qt-x64-ReleaseLTCG.exe" },
			new[]
			{
				new SettingCheck("fullscreen", K("settings.ini", ConfigFormat.Ini, "Main", "StartFullscreen"), "true", "false"),
				new SettingCheck("exit-prompt", K("settings.ini", ConfigFormat.Ini, "Main", "ConfirmPowerOff"), "false", "true"),
			},
			new RaSpec(K("settings.ini", ConfigFormat.Ini, "Cheevos", "Enabled"), K("settings.ini", ConfigFormat.Ini, "Cheevos", "Username"),
				new[] { K("settings.ini", ConfigFormat.Ini, "Cheevos", "Token") }),
			new[] { Ps1 }),
		new("ppsspp", "PPSSPP", "PPSSPP", new[] { "PPSSPPWindows64.exe" },
			new[] { new SettingCheck("fullscreen", K("memstick/PSP/SYSTEM/ppsspp.ini", ConfigFormat.Ini, "Graphics", "FullScreen"), "True", "False") },
			new RaSpec(K("memstick/PSP/SYSTEM/ppsspp.ini", ConfigFormat.Ini, "Achievements", "AchievementsEnable"), K("memstick/PSP/SYSTEM/ppsspp.ini", ConfigFormat.Ini, "Achievements", "AchievementsUserName"),
				Array.Empty<ConfigKey>(), "memstick/PSP/SYSTEM/ppsspp_retroachievements.dat"),
			Array.Empty<BiosSpec>(), "none-needed"),
		new("azahar", "Azahar", "azahar", new[] { "azahar.exe" },
			new[]
			{
				new SettingCheck("fullscreen", K("user/config/qt-config.ini", ConfigFormat.QtIni, "UI", "fullscreen"), "true", "false"),
				new SettingCheck("exit-prompt", K("user/config/qt-config.ini", ConfigFormat.QtIni, "UI", "confirmClose"), "false", "true"),
				new SettingCheck("shaders", K("user/config/qt-config.ini", ConfigFormat.QtIni, "Renderer", "use_disk_shader_cache"), "true", "true"),
			},
			null, Array.Empty<BiosSpec>(), "none-needed"),
		new("rpcs3", "RPCS3", "RPCS3", new[] { "rpcs3.exe" },
			new[] { new SettingCheck("fullscreen", K("config/config.yml", ConfigFormat.Yaml, "Miscellaneous", "Start games in fullscreen mode"), "true", "false") },
			null,
			new[] { new BiosSpec("ps3", new[] { "dev_flash/sys/external" }, Array.Empty<string>(), InEmulator: true, Directory: true) }),
		new("xenia", "Xenia Canary", "xenia", new[] { "xenia_canary.exe" },
			new[] { new SettingCheck("fullscreen", K("xenia-canary.config.toml", ConfigFormat.Toml, "Display", "fullscreen"), "true", "false") },
			null, Array.Empty<BiosSpec>(), "none-needed"),
		new("shadps4", "shadPS4", "shadps4-qt", new[] { "shadPS4QtLauncher.exe", "shadPS4.exe" },
			new[] { new SettingCheck("fullscreen", K("user/config.toml", ConfigFormat.Toml, "General", "Fullscreen"), "true", "false") },
			null, Array.Empty<BiosSpec>(), "none-needed"),
		new("cemu", "Cemu", "cemu", new[] { "Cemu.exe" },
			new[] { new SettingCheck("fullscreen", K("settings.xml", ConfigFormat.Xml, "", "fullscreen"), "true", "false") },
			null, Array.Empty<BiosSpec>(), "none-needed"),
		new("xemu", "xemu", "xemu", new[] { "xemu.exe" },
			new[] { new SettingCheck("fullscreen", K("xemu.toml", ConfigFormat.Toml, "display.window", "fullscreen_on_startup"), "true", "false") },
			null,
			new[]
			{
				new BiosSpec("xbox-mcpx", new[] { "toml:sys.files/bootrom_path" }, Array.Empty<string>(), InEmulator: true),
				new BiosSpec("xbox-flash", new[] { "toml:sys.files/flashrom_path" }, Array.Empty<string>(), InEmulator: true),
				new BiosSpec("xbox-hdd", new[] { "toml:sys.files/hdd_path" }, Array.Empty<string>(), InEmulator: true),
			}),
		new("ryujinx", "Ryujinx", "Ryujinx", new[] { "ryujinx.exe", "Ryujinx.exe" },
			new[]
			{
				new SettingCheck("fullscreen", K("portable/Config.json", ConfigFormat.Json, "", "start_fullscreen"), "true", "false"),
				new SettingCheck("exit-prompt", K("portable/Config.json", ConfigFormat.Json, "", "show_confirm_exit"), "false", "true"),
				new SettingCheck("shaders", K("portable/Config.json", ConfigFormat.Json, "", "enable_shader_cache"), "true", "true"),
			},
			null,
			new[]
			{
				new BiosSpec("switch-keys", new[] { "portable/system/prod.keys" }, Array.Empty<string>(), InEmulator: true),
				new BiosSpec("switch-firmware", new[] { "portable/bis/system/Contents/registered" }, Array.Empty<string>(), InEmulator: true, Directory: true),
			}),
	};

	internal static EmulatorDef? Find(string id) => All.FirstOrDefault(emulator => emulator.Id == id);

	// ---------- the hotkey scheme, per emulator ----------

	// What the scheme writes for one emulator. RetroArch's button numbers depend on its joypad driver: Windows'
	// default is xinput (EmuDeck sets none), and sdl2 numbers follow EmuDeck's "Steam Virtual Gamepad" autoconfig.
	internal static HotkeyPlan Hotkeys(string id, string? retroArchDriver = null)
	{
		var cant = new Dictionary<string, string>(StringComparer.Ordinal);
		switch (id)
		{
			case "retroarch":
			{
				bool sdl = string.Equals(retroArchDriver?.Trim(), "sdl2", StringComparison.OrdinalIgnoreCase);
				// xinput: A 0, B 1, X 2, Y 3, LB 4, RB 5, Start 6, Back 7, hats for the D-pad.
				// sdl2: A 0, B 1, X 2, Y 3, Back 4, Start 6, LB 9, RB 10, D-pad left 13, right 14.
				string select = sdl ? "4" : "7", lb = sdl ? "9" : "4", rb = sdl ? "10" : "5", left = sdl ? "13" : "h0left", right = sdl ? "14" : "h0right";
				ConfigKey R(string key) => K("retroarch.cfg", ConfigFormat.RetroArchCfg, "", key);
				return new HotkeyPlan(new[]
				{
					new HotkeyWrite(null, R("input_enable_hotkey_btn"), select),
					new HotkeyWrite("save", R("input_save_state_btn"), rb),
					new HotkeyWrite("load", R("input_load_state_btn"), lb),
					new HotkeyWrite("slot-next", R("input_state_slot_increase_btn"), right),
					new HotkeyWrite("slot-prev", R("input_state_slot_decrease_btn"), left),
					new HotkeyWrite("fast-forward", R("input_hold_fast_forward_axis"), "+5"),
					new HotkeyWrite("rewind", R("input_rewind_axis"), "+4"),
					new HotkeyWrite("pause", R("input_pause_toggle_btn"), "0"),
					new HotkeyWrite("menu", R("input_menu_toggle_btn"), "3"),
					new HotkeyWrite("fps", R("input_fps_toggle_btn"), "2"),
					// No quitting from the controller (EmuDeck's Select+Start), and nothing else on Select+A.
					new HotkeyWrite(null, R("input_exit_emulator_btn"), "nul"),
					new HotkeyWrite(null, R("input_quit_gamepad_combo"), "0"),
					new HotkeyWrite(null, R("input_screenshot_btn"), "nul"),
				}, cant);
			}
			case "pcsx2":
			case "duckstation":
			{
				bool pcsx2 = id == "pcsx2";
				string file = pcsx2 ? "inis/PCSX2.ini" : "settings.ini";
				ConfigKey H(string key) => K(file, ConfigFormat.IniMulti, "Hotkeys", key);
				static string Combo(string button) => "SDL-0/Back & SDL-0/" + button;
				var writes = new List<HotkeyWrite>
				{
					new("save", H(pcsx2 ? "SaveStateToSlot" : "SaveSelectedSaveState"), Combo("RightShoulder")),
					new("load", H(pcsx2 ? "LoadStateFromSlot" : "LoadSelectedSaveState"), Combo("LeftShoulder")),
					new("slot-next", H(pcsx2 ? "NextSaveStateSlot" : "SelectNextSaveStateSlot"), Combo("DPadRight")),
					new("slot-prev", H(pcsx2 ? "PreviousSaveStateSlot" : "SelectPreviousSaveStateSlot"), Combo("DPadLeft")),
					new("fast-forward", H(pcsx2 ? "HoldTurbo" : "FastForward"), Combo("+RightTrigger")),
					new("pause", H("TogglePause"), Combo("A")),
					new("menu", H("OpenPauseMenu"), Combo("Y")),
					new(null, H(pcsx2 ? "ShutdownVM" : "PowerOff"), null),
				};
				if (pcsx2)
				{
					// EmuDeck puts turbo and slow motion on Select+R2/L2 as toggles: moved off the reserved combos.
					writes.Add(new(null, H("ToggleTurbo"), null));
					writes.Add(new(null, H("ToggleSlowMotion"), null));
					cant["rewind"] = "no-rewind";
				}
				else writes.Add(new("rewind", H("Rewind"), Combo("+LeftTrigger")));
				cant["fps"] = "no-fps-hotkey";
				return new HotkeyPlan(writes, cant);
			}
			case "dolphin":
			{
				ConfigKey H(string key) => K("User/Config/Hotkeys.ini", ConfigFormat.Ini, "Hotkeys", key);
				static string Combo(string button) => "@(Back+`" + button + "`)";
				cant["rewind"] = "no-rewind";
				cant["menu"] = "no-menu";
				cant["fps"] = "no-fps-hotkey";
				return new HotkeyPlan(new[]
				{
					new HotkeyWrite("save", H("Save State/Save to Selected Slot"), Combo("Shoulder R")),
					new HotkeyWrite("load", H("Load State/Load from Selected Slot"), Combo("Shoulder L")),
					new HotkeyWrite("slot-next", H("Other State Hotkeys/Increase Selected State Slot"), Combo("Pad E")),
					new HotkeyWrite("slot-prev", H("Other State Hotkeys/Decrease Selected State Slot"), Combo("Pad W")),
					new HotkeyWrite("fast-forward", H("Emulation Speed/Disable Emulation Speed Limit"), Combo("Trigger R")),
					new HotkeyWrite("pause", H("General/Toggle Pause"), Combo("Button A")),
					new HotkeyWrite(null, H("General/Exit"), ""),
					new HotkeyWrite(null, H("General/Stop"), ""),
					new HotkeyWrite(null, H("Emulation Speed/Increase Emulation Speed"), ""),
					new HotkeyWrite(null, H("Emulation Speed/Decrease Emulation Speed"), ""),
				}, cant);
			}
			case "ppsspp":
			{
				// controls.ini: device 20 is the first XInput pad; 109 Select, 103 R1, 102 L1, 21/22 D-pad left/right,
				// 96 Cross (A), 99 Triangle (Y), 4036/4034 the right/left trigger axes (as EmuDeck maps them).
				ConfigKey H(string key) => K("memstick/PSP/SYSTEM/controls.ini", ConfigFormat.Ppsspp, "ControlMapping", key);
				static string Combo(string code) => "20-109:20-" + code;
				cant["pause"] = "pause-is-menu";
				cant["fps"] = "no-fps-hotkey";
				return new HotkeyPlan(new[]
				{
					new HotkeyWrite("save", H("Save State"), Combo("103")),
					new HotkeyWrite("load", H("Load State"), Combo("102")),
					new HotkeyWrite("slot-next", H("Next Slot"), Combo("22")),
					new HotkeyWrite("slot-prev", H("Previous Slot"), Combo("21")),
					new HotkeyWrite("fast-forward", H("Fast-forward"), Combo("4036")),
					new HotkeyWrite("rewind", H("Rewind"), Combo("4034")),
					new HotkeyWrite("menu", H("Pause"), Combo("99")),
					new HotkeyWrite(null, H("Exit App"), null),
				}, cant);
			}
			case "azahar":
				return Unsupported(cant, "keyboard-only");
			case "rpcs3":
				return Unsupported(cant, "keyboard-only");
			case "xenia":
			case "shadps4":
			case "cemu":
			case "xemu":
			case "ryujinx":
				return Unsupported(cant, "no-save-states");
		}
		return Unsupported(cant, "unknown");
	}

	private static HotkeyPlan Unsupported(Dictionary<string, string> cant, string reason)
	{
		foreach (string function in Functions) cant[function] = reason;
		return new HotkeyPlan(Array.Empty<HotkeyWrite>(), cant, reason);
	}
}

// ---------- settings text: read and write one key, keeping everything else byte for byte ----------

internal static class ConfigText
{
	private static readonly Regex Section = new(@"^\s*\[([^\]]*)\]\s*$", RegexOptions.CultureInvariant);
	private static readonly Regex Pair = new(@"^(\s*)([^=;#\[\s][^=]*?)(\s*=\s*)(.*?)(\s*)$", RegexOptions.CultureInvariant);

	private sealed record Line(int Index, string Section, string? Key, string Value);

	private static List<Line> Lines(string[] raw)
	{
		var lines = new List<Line>();
		string section = "";
		for (int i = 0; i < raw.Length; i++)
		{
			string text = raw[i];
			string trimmed = text.TrimStart();
			if (trimmed.StartsWith(";", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.Length == 0) { lines.Add(new Line(i, section, null, "")); continue; }
			if (Section.Match(text) is { Success: true } header) { section = header.Groups[1].Value.Trim(); lines.Add(new Line(i, section, null, "")); continue; }
			if (Pair.Match(text) is { Success: true } pair) lines.Add(new Line(i, section, pair.Groups[2].Value.Trim(), pair.Groups[4].Value));
			else lines.Add(new Line(i, section, null, ""));
		}
		return lines;
	}

	private static string[] Split(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

	private static string Join(string[] lines, string original) => string.Join(original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n", lines);

	private static bool SameSection(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	private static bool SameKey(string? a, string b) => a is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	// A TOML or cfg value without its quotes or trailing comment.
	private static string Bare(string value, ConfigFormat format)
	{
		string text = value.Trim();
		if (format == ConfigFormat.Toml)
		{
			int hash = text.IndexOf('#');
			if (hash >= 0 && !text.StartsWith("\"", StringComparison.Ordinal)) text = text[..hash].Trim();
		}
		if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
		return text;
	}

	// The effective value, or null when the key is absent (the emulator's default applies). Qt settings with
	// "key\default=true" also mean the default applies.
	internal static string? Read(string? text, ConfigKey key)
	{
		if (text is null) return null;
		switch (key.Format)
		{
			case ConfigFormat.Xml:
			{
				Match match = Regex.Match(text, "<" + Regex.Escape(key.Key) + @">([^<]*)</" + Regex.Escape(key.Key) + ">", RegexOptions.CultureInvariant);
				return match.Success ? match.Groups[1].Value.Trim() : null;
			}
			case ConfigFormat.Json:
			{
				Match match = Regex.Match(text, "\"" + Regex.Escape(key.Key) + "\"\\s*:\\s*(true|false|-?\\d+|\"[^\"]*\")", RegexOptions.CultureInvariant);
				return match.Success ? match.Groups[1].Value.Trim('"') : null;
			}
			case ConfigFormat.Yaml:
				return YamlLine(Split(text), key) is { } found ? found.Value : null;
		}
		string[] raw = Split(text);
		var lines = Lines(raw);
		string? value = null;
		bool qtDefault = false;
		foreach (Line line in lines)
		{
			if (line.Key is null) continue;
			bool sectionOk = key.Format == ConfigFormat.RetroArchCfg || SameSection(line.Section, key.Section);
			if (!sectionOk) continue;
			if (key.Format == ConfigFormat.QtIni && SameKey(line.Key, key.Key + "\\default")) qtDefault = Bare(line.Value, key.Format).Equals("true", StringComparison.OrdinalIgnoreCase);
			else if (SameKey(line.Key, key.Key)) value ??= Bare(line.Value, key.Format);
		}
		return qtDefault ? null : value;
	}

	// Every value of a key that may repeat (PCSX2's and DuckStation's [Hotkeys]).
	internal static IReadOnlyList<string> ReadAll(string? text, ConfigKey key)
	{
		if (text is null) return Array.Empty<string>();
		return Lines(Split(text)).Where(line => SameSection(line.Section, key.Section) && SameKey(line.Key, key.Key)).Select(line => line.Value.Trim()).ToList();
	}

	private static (int Index, string Value, Match Match)? YamlLine(string[] raw, ConfigKey key)
	{
		bool inside = false;
		var header = new Regex("^" + Regex.Escape(key.Section) + @":\s*$", RegexOptions.CultureInvariant);
		var item = new Regex(@"^(\s+)" + Regex.Escape(key.Key) + @":(\s*)(.*?)\s*$", RegexOptions.CultureInvariant);
		for (int i = 0; i < raw.Length; i++)
		{
			string line = raw[i];
			if (line.Length > 0 && !char.IsWhiteSpace(line[0])) { inside = header.IsMatch(line); continue; }
			if (inside && item.Match(line) is { Success: true } match) return (i, match.Groups[3].Value, match);
		}
		return null;
	}

	// The text with exactly one key set to value, or null when that cannot be done safely (a file that does not
	// parse, a YAML/XML/JSON key that is not already there). An absent INI key is added at the end of its
	// section (a new section at the end when there is none). Line endings follow the file's own.
	internal static string? Set(string text, ConfigKey key, string value)
	{
		switch (key.Format)
		{
			case ConfigFormat.Xml:
			{
				var pattern = new Regex("(<" + Regex.Escape(key.Key) + @">)([^<]*)(</" + Regex.Escape(key.Key) + ">)", RegexOptions.CultureInvariant);
				return pattern.IsMatch(text) ? pattern.Replace(text, match => match.Groups[1].Value + value + match.Groups[3].Value, 1) : null;
			}
			case ConfigFormat.Json:
			{
				var pattern = new Regex("(\"" + Regex.Escape(key.Key) + "\"\\s*:\\s*)(true|false|-?\\d+)", RegexOptions.CultureInvariant);
				return pattern.IsMatch(text) ? pattern.Replace(text, match => match.Groups[1].Value + value, 1) : null;
			}
			case ConfigFormat.Yaml:
			{
				string[] yaml = Split(text);
				if (YamlLine(yaml, key) is not { } found) return null;
				yaml[found.Index] = found.Match.Groups[1].Value + key.Key + ":" + (found.Match.Groups[2].Value.Length == 0 ? " " : found.Match.Groups[2].Value) + value;
				return Join(yaml, text);
			}
		}
		string[] raw = Split(text);
		var lines = Lines(raw);
		string written = key.Format switch
		{
			ConfigFormat.RetroArchCfg => "\"" + value + "\"",
			_ => value,
		};
		var list = raw.ToList();
		if (key.Format == ConfigFormat.QtIni)
		{
			string? withDefault = SetLine(list, lines, key with { Key = key.Key + "\\default", Format = ConfigFormat.Ini }, "false");
			if (withDefault is null) return null;
			raw = Split(withDefault);
			list = raw.ToList();
			lines = Lines(raw);
		}
		return SetLine(list, lines, key, written) is { } result ? Restore(result, text) : null;
	}

	private static string Restore(string joinedLf, string original) => original.Contains("\r\n", StringComparison.Ordinal) ? joinedLf.Replace("\n", "\r\n", StringComparison.Ordinal) : joinedLf;

	private static string? SetLine(List<string> raw, List<Line> lines, ConfigKey key, string written)
	{
		int? sectionEnd = null;
		foreach (Line line in lines)
		{
			bool inSection = key.Format == ConfigFormat.RetroArchCfg || SameSection(line.Section, key.Section);
			if (!inSection) continue;
			if (line.Key is null)
			{
				if (key.Format != ConfigFormat.RetroArchCfg && Section.IsMatch(raw[line.Index])) sectionEnd = line.Index;
				continue;
			}
			sectionEnd = line.Index;
			if (!SameKey(line.Key, key.Key)) continue;
			Match pair = Pair.Match(raw[line.Index]);
			string suffix = "";
			if (key.Format == ConfigFormat.Toml)
			{
				string current = pair.Groups[4].Value;
				int hash = current.IndexOf('#');
				if (hash >= 0 && !current.TrimStart().StartsWith("\"", StringComparison.Ordinal))
				{
					string before = current[..hash];
					string gap = before[before.TrimEnd().Length..];
					suffix = (gap.Length == 0 ? " " : gap) + current[hash..];
				}
			}
			raw[line.Index] = pair.Groups[1].Value + pair.Groups[2].Value + pair.Groups[3].Value + written + suffix;
			return string.Join("\n", raw);
		}
		string separator = key.Format is ConfigFormat.QtIni or ConfigFormat.Ini && raw.Any(line => Regex.IsMatch(line, @"^[^=\[;#]+=[^ ]")) && !raw.Any(line => line.Contains(" = ", StringComparison.Ordinal)) ? "=" : " = ";
		string newLine = key.Key + separator + written;
		if (key.Format == ConfigFormat.RetroArchCfg)
		{
			int last = raw.Count;
			while (last > 0 && raw[last - 1].Length == 0) last--;
			raw.Insert(last, newLine);
		}
		else if (sectionEnd is int end) raw.Insert(end + 1, newLine);
		else
		{
			while (raw.Count > 0 && raw[^1].Length == 0) raw.RemoveAt(raw.Count - 1);
			if (raw.Count > 0) raw.Add("");
			raw.Add("[" + key.Section + "]");
			raw.Add(newLine);
			raw.Add("");
		}
		return string.Join("\n", raw);
	}

	// A controller binding (PCSX2/DuckStation "SDL-0/...", "XInput-0/...", "DInput-0/..."; PPSSPP device ids 10
	// and up). Keyboard and mouse bindings are left as they are.
	internal static bool IsControllerBinding(string value, ConfigFormat format)
	{
		string text = value.Trim();
		if (format == ConfigFormat.Ppsspp)
		{
			int dash = text.IndexOf('-');
			return dash > 0 && int.TryParse(text[..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int device) && device >= 10;
		}
		return text.StartsWith("SDL-", StringComparison.OrdinalIgnoreCase) || text.StartsWith("XInput-", StringComparison.OrdinalIgnoreCase) || text.StartsWith("DInput-", StringComparison.OrdinalIgnoreCase);
	}

	// The controller bindings a key holds now.
	internal static IReadOnlyList<string> ControllerBindings(string? text, ConfigKey key)
	{
		if (text is null) return Array.Empty<string>();
		if (key.Format == ConfigFormat.IniMulti) return ReadAll(text, key).Where(value => IsControllerBinding(value, key.Format)).ToList();
		if (key.Format == ConfigFormat.Ppsspp)
			return (Read(text, key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(part => IsControllerBinding(part, key.Format)).ToList();
		string? single = Read(text, key);
		return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single.Trim() };
	}

	// Replaces a key's controller bindings with one binding (or none), keeping its keyboard ones.
	internal static string? SetControllerBinding(string text, ConfigKey key, string? value)
	{
		if (key.Format == ConfigFormat.Ppsspp)
		{
			var parts = (Read(text, key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(part => !IsControllerBinding(part, key.Format)).ToList();
			if (value is not null) parts.Add(value);
			if (parts.Count == 0 && Read(text, key) is null) return text;
			return Set(text, key with { Format = ConfigFormat.Ini }, string.Join(",", parts));
		}
		if (key.Format != ConfigFormat.IniMulti) return Set(text, key, value ?? "");
		string[] raw = Split(text);
		var lines = Lines(raw);
		var remove = lines.Where(line => SameSection(line.Section, key.Section) && SameKey(line.Key, key.Key) && IsControllerBinding(line.Value, key.Format)).Select(line => line.Index).ToHashSet();
		var kept = raw.Where((_, index) => !remove.Contains(index)).ToList();
		string joined = Restore(string.Join("\n", kept), text);
		if (value is null) return joined;
		// Added as a new line in the section (the key may repeat).
		string[] again = Split(joined);
		var list = again.ToList();
		var parsed = Lines(again);
		int? end = null;
		foreach (Line line in parsed)
			if (SameSection(line.Section, key.Section) && (line.Key is not null || Section.IsMatch(again[line.Index]))) end = line.Index;
		string newLine = key.Key + " = " + value;
		if (end is int at) list.Insert(at + 1, newLine);
		else
		{
			while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
			list.Add("");
			list.Add("[" + key.Section + "]");
			list.Add(newLine);
			list.Add("");
		}
		return Restore(string.Join("\n", list), text);
	}

	internal static string Hash(string? text) => text is null ? "absent" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

// ---------- verdicts ----------

// What the agent read for one emulator. Files maps a relative file to its text (null: not there).
internal sealed record EmulatorObservation
{
	public required EmulatorDef Def { get; init; }
	public string? Exe { get; init; }
	public string? Version { get; init; }
	public bool Running { get; init; }
	public IReadOnlyDictionary<string, string?> Files { get; init; } = new Dictionary<string, string?>();
	public bool? TokenFileNonEmpty { get; init; }
	public IReadOnlyDictionary<string, string> Bios { get; init; } = new Dictionary<string, string>();
}

internal sealed record SettingVerdict(string Kind, string State, bool Writable);

internal sealed record HotkeyVerdict(string State, IReadOnlyList<string> Drifted, IReadOnlyDictionary<string, string> CantDo, string? Unsupported);

internal static class EmulatorHealthPolicy
{
	private static string? File(EmulatorObservation observation, string file) => observation.Files.TryGetValue(file, out string? text) ? text : null;

	internal static bool Same(string? a, string b) => a is not null && string.Equals(a.Trim(), b, StringComparison.OrdinalIgnoreCase);

	internal static IReadOnlyList<SettingVerdict> Settings(EmulatorObservation observation) => observation.Def.Settings.Select(check =>
	{
		string? text = File(observation, check.Where.File);
		// A file the emulator has not written yet: its default applies, and Core does not create it.
		if (text is null) return new SettingVerdict(check.Kind, Same(check.Default, check.Wanted) ? "ok" : "no-config", false);
		string effective = ConfigText.Read(text, check.Where) ?? check.Default;
		if (Same(effective, check.Wanted)) return new SettingVerdict(check.Kind, "ok", true);
		bool writable = ConfigText.Set(text, check.Where, check.Wanted) is not null;
		return new SettingVerdict(check.Kind, "wrong", writable);
	}).ToList();

	// "signed-in", "signed-out", "off", "n/a" or "unknown". Booleans only: the user name and token are tested
	// for presence inside this method and never leave it.
	internal static string RetroAchievements(EmulatorObservation observation)
	{
		if (observation.Def.Ra is not { } ra) return "n/a";
		string? text = File(observation, ra.Enabled.File);
		if (text is null) return "unknown";
		static bool Present(string? value) => !string.IsNullOrWhiteSpace(value?.Trim().Trim('"'));
		bool enabled = (ConfigText.Read(text, ra.Enabled) ?? "false").Trim().Trim('"').ToLowerInvariant() is "true" or "1" or "yes" or "on";
		bool user = Present(ConfigText.Read(File(observation, ra.User.File), ra.User));
		bool token = ra.TokenFile is not null ? observation.TokenFileNonEmpty == true : ra.Token.Any(key => Present(ConfigText.Read(File(observation, key.File), key)));
		if (!enabled) return "off";
		return user && token ? "signed-in" : "signed-out";
	}

	internal static HotkeyVerdict Hotkeys(EmulatorObservation observation)
	{
		string? driver = observation.Def.Id == "retroarch" ? ConfigText.Read(File(observation, "retroarch.cfg"), new ConfigKey("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "input_joypad_driver")) : null;
		HotkeyPlan plan = EmulatorCatalog.Hotkeys(observation.Def.Id, driver);
		if (plan.Unsupported is not null) return new HotkeyVerdict("unsupported", Array.Empty<string>(), plan.CantDo, plan.Unsupported);
		var drifted = new List<string>();
		bool missing = false;
		foreach (HotkeyWrite write in plan.Writes)
		{
			string? text = File(observation, write.Where.File);
			if (text is null) { missing = true; continue; }
			IReadOnlyList<string> bindings = ConfigText.ControllerBindings(text, write.Where);
			bool ok = write.Value switch
			{
				null => bindings.Count == 0,
				"" => bindings.Count == 0,
				_ when write.Where.Format == ConfigFormat.RetroArchCfg => Same(ConfigText.Read(text, write.Where), write.Value),
				_ => bindings.Count == 1 && string.Equals(bindings[0], write.Value, StringComparison.Ordinal),
			};
			if (!ok) drifted.Add(write.Function ?? ("guard:" + write.Where.Key));
		}
		if (missing && drifted.Count == 0) return new HotkeyVerdict("no-config", drifted, plan.CantDo, null);
		return new HotkeyVerdict(drifted.Count == 0 && !missing ? "matches" : "drifted", drifted.Distinct().ToList(), plan.CantDo, null);
	}

	// The new text of every file the scheme touches, or null for a file that cannot be edited safely.
	internal static IReadOnlyDictionary<string, string?> ApplyHotkeys(EmulatorObservation observation)
	{
		string? driver = observation.Def.Id == "retroarch" ? ConfigText.Read(File(observation, "retroarch.cfg"), new ConfigKey("retroarch.cfg", ConfigFormat.RetroArchCfg, "", "input_joypad_driver")) : null;
		HotkeyPlan plan = EmulatorCatalog.Hotkeys(observation.Def.Id, driver);
		var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		foreach (var group in plan.Writes.GroupBy(write => write.Where.File, StringComparer.OrdinalIgnoreCase))
		{
			string? text = File(observation, group.Key);
			if (text is null) { result[group.Key] = null; continue; }
			string? next = text;
			foreach (HotkeyWrite write in group)
			{
				if (next is null) break;
				if (write.Where.Format == ConfigFormat.RetroArchCfg) next = ConfigText.Set(next, write.Where, write.Value ?? "nul");
				else if (write.Value == "" && ConfigText.Read(next, write.Where) is null) continue; // nothing bound, nothing to blank
				else if (write.Value is null && ConfigText.ControllerBindings(next, write.Where).Count == 0) continue;
				else next = ConfigText.SetControllerBinding(next, write.Where, write.Value);
			}
			result[group.Key] = next;
		}
		return result;
	}

	// The new text of every file the setting fixes touch (fullscreen, exit prompt, shaders), or null when unsafe.
	internal static IReadOnlyDictionary<string, string?> ApplySettings(EmulatorObservation observation)
	{
		var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		foreach (SettingCheck check in observation.Def.Settings)
		{
			string? current = result.TryGetValue(check.Where.File, out string? edited) ? edited : File(observation, check.Where.File);
			if (current is null) continue;
			string effective = ConfigText.Read(current, check.Where) ?? check.Default;
			if (Same(effective, check.Wanted)) { result.TryAdd(check.Where.File, current); continue; }
			result[check.Where.File] = ConfigText.Set(current, check.Where, check.Wanted);
		}
		// Files left unchanged are not written.
		return result.Where(pair => pair.Value is null || pair.Value != File(observation, pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
	}

	// The emulator row (settings, RetroAchievements, BIOS, version) and its hotkeys row. Healthy rows are steel
	// (Ok); anything to look at is amber (Info): these never raise a notice.
	internal static IReadOnlyList<HealthRow> Rows(EmulatorObservation observation)
	{
		EmulatorDef def = observation.Def;
		IReadOnlyList<SettingVerdict> settings = Settings(observation);
		string ra = RetroAchievements(observation);
		var bios = def.Bios.Select(spec => new Dictionary<string, object?> { ["system"] = spec.System, ["state"] = observation.Bios.TryGetValue(spec.System, out string? state) ? state : "unknown" }).ToArray();
		bool settingsOk = settings.All(verdict => verdict.State is "ok");
		bool biosOk = bios.All(entry => entry["state"] is "ok");
		var data = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["emulator"] = def.Id, ["name"] = def.Name, ["version"] = observation.Version, ["running"] = observation.Running,
			["settings"] = settings.Select(verdict => new Dictionary<string, object?> { ["kind"] = verdict.Kind, ["state"] = verdict.State }).ToArray(),
			["ra"] = ra, ["bios"] = bios, ["biosNote"] = def.BiosNote,
		};
		if (settings.Any(verdict => verdict.State == "wrong" && verdict.Writable)) data["fix"] = "emu-fix-" + def.Id;
		var issues = new List<string>();
		if (!settingsOk) issues.Add("settings");
		if (ra is "signed-out" or "off") issues.Add("ra");
		if (!biosOk) issues.Add("bios");
		data["issues"] = issues;
		var rows = new List<HealthRow> { new("emulator-" + def.Id, issues.Count == 0 ? HealthLevel.Ok : HealthLevel.Info, issues.Count == 0 ? "ok" : "check", data) };
		HotkeyVerdict hotkeys = Hotkeys(observation);
		var hotkeyData = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["emulator"] = def.Id, ["name"] = def.Name, ["drifted"] = hotkeys.Drifted, ["cantDo"] = hotkeys.CantDo.Keys.OrderBy(key => Array.IndexOf(EmulatorCatalog.Functions, key)).ToArray(),
			["cantDoReason"] = hotkeys.CantDo.Values.Distinct().ToArray(), ["unsupported"] = hotkeys.Unsupported, ["running"] = observation.Running,
		};
		if (hotkeys.State == "drifted") hotkeyData["fix"] = "emu-hotkeys-" + def.Id;
		rows.Add(new HealthRow("hotkeys-" + def.Id, hotkeys.State is "matches" or "unsupported" ? HealthLevel.Ok : HealthLevel.Info, hotkeys.State, hotkeyData));
		return rows;
	}

	// ---------- BIOS ----------

	// A BIOS file is judged by its name or by an MD5 EmuDeck's checker accepts; files larger than 64 MB are
	// never hashed (no BIOS is that big). Only the verdict leaves: never a path's contents.
	internal const long MaxBiosHashBytes = 64L * 1024 * 1024;

	internal static string BiosState(BiosSpec spec, IEnumerable<(string Name, string RelativeFolder, Func<string?> Md5)> files)
	{
		var list = files.ToList();
		string? folder = spec.Folder;
		IEnumerable<(string Name, string RelativeFolder, Func<string?> Md5)> candidates = folder is null ? list
			: list.Where(file => file.RelativeFolder.Split('/', '\\').FirstOrDefault()?.Equals(folder, StringComparison.OrdinalIgnoreCase) == true);
		if (spec.Md5.Count > 0)
		{
			foreach (var file in candidates)
				if (file.Md5() is { } md5 && spec.Md5.Contains(md5.ToLowerInvariant())) return "ok";
			return "missing";
		}
		return candidates.Any(file => spec.Names.Any(name => string.Equals(name, file.Name, StringComparison.OrdinalIgnoreCase))) ? "ok" : "missing";
	}

	// EmuDeck's settings.ps1: $biosPath="C:\Emulation\bios" and the like (as The Collector reads it).
	internal static IReadOnlyDictionary<string, string> ParseEmuDeckSettings(string text)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
			if (Regex.Match(line, @"^\s*\$(\w+)\s*=\s*""?(.*?)""?\s*$", RegexOptions.CultureInvariant) is { Success: true } match) values[match.Groups[1].Value] = match.Groups[2].Value;
		return values;
	}

	// The emulator a launcher script names: `$emulatorFile = "$env:APPDATA/emudeck/Emulators/<folder>/<exe>"`.
	internal static string? LauncherEmulatorFile(string script, string appData, string userProfile)
	{
		Match match = Regex.Match(script, @"^\s*\$emulatorFile\s*=\s*""([^""]+)""", RegexOptions.Multiline | RegexOptions.CultureInvariant);
		if (!match.Success) return null;
		string path = Regex.Replace(match.Groups[1].Value, @"\$env:APPDATA", appData.Replace("$", "$$", StringComparison.Ordinal), RegexOptions.IgnoreCase);
		path = Regex.Replace(path, @"\$env:USERPROFILE", userProfile.Replace("$", "$$", StringComparison.Ordinal), RegexOptions.IgnoreCase);
		return path.Contains('$') ? null : path.Replace('/', '\\');
	}

	// Which catalogue emulator an executable is, by its file name.
	internal static EmulatorDef? ByExe(string? exe)
	{
		if (string.IsNullOrEmpty(exe)) return null;
		string name = Path.GetFileName(exe.Replace('\\', '/'));
		return EmulatorCatalog.All.FirstOrDefault(emulator => emulator.Exes.Any(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase)));
	}
}

// ---------- the fix guard: backup, unchanged since read, never while running ----------

internal enum FixRefusal { None, Running, Changed, Unsafe, NoChange }

internal static class EmulatorFixGuard
{
	internal static FixRefusal Decide(bool running, string readHash, string currentHash, string? newText, string? currentText)
	{
		if (running) return FixRefusal.Running;
		if (!string.Equals(readHash, currentHash, StringComparison.Ordinal)) return FixRefusal.Changed;
		if (newText is null) return FixRefusal.Unsafe;
		if (currentText is not null && newText == currentText) return FixRefusal.NoChange;
		return FixRefusal.None;
	}

	internal static string Code(FixRefusal refusal) => refusal switch
	{
		FixRefusal.Running => "not-while-running",
		FixRefusal.Changed => "changed-since-read",
		FixRefusal.Unsafe => "unsafe",
		FixRefusal.NoChange => "no-change",
		_ => "ok",
	};

	// Backups: %APPDATA%\GamingMode\emulator-backups\<emulator>\<yyyyMMdd-HHmmss>\<relative file>.
	internal static string BackupPath(string root, string emulator, DateTimeOffset at, string relativeFile) =>
		Path.Combine(root, emulator, at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), relativeFile.Replace('/', Path.DirectorySeparatorChar));
}

// ---------- Core's memory: %APPDATA%\GamingMode\emulators.json ----------

internal sealed record EmulatorState
{
	// Emulators seen before (a new one gets the hotkey scheme once, automatically).
	public List<string> Known { get; init; } = new();
	// Emulators whose program files an update replaced since the last hotkey check (the scheme is re-applied once).
	public List<string> UpdatedSinceHotkeys { get; init; } = new();
	public DateTimeOffset? FirstRunAt { get; init; }
}

internal static class EmulatorStateStore
{
	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static EmulatorState Load(string path)
	{
		try { return System.IO.File.Exists(path) ? JsonSerializer.Deserialize<EmulatorState>(System.IO.File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, EmulatorState state)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		System.IO.File.Move(temporary, path, true);
	}

	// The scheme is written automatically only on Core's first run, for an emulator Core has not seen before, or
	// after an update replaced the emulator's files. A drift the user makes later is shown, not undone.
	internal static bool ShouldAutoApply(EmulatorState state, string emulator, bool firstRun) =>
		firstRun || !state.Known.Contains(emulator, StringComparer.Ordinal) || state.UpdatedSinceHotkeys.Contains(emulator, StringComparer.Ordinal);
}
