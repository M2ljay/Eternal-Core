using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// ANTI-CHEAT SAFE MODE (Eternal Core, upstream issue #25).
//
// Kernel anti-cheats can refuse to start a game while another program hooks the
// keyboard system-wide, restyles or force-foregrounds the game's window, replaces
// the system cursors or holds a handle on the game process. Gaming Mode does all
// four. While a protected game runs they are paused, and they resume after it exits.
//
// This file is the pure part: the catalogue, the decision and the pause/resume
// orchestration. AntiCheatProtection.Windows.cs gathers the observations without
// opening, enumerating the modules of, or reading the memory of any game process.
// Nothing here touches Windows, so AntiCheatProtection.Tests runs it on Linux.

internal enum AntiCheatServiceStart { Boot = 0, System = 1, Automatic = 2, Manual = 3, Disabled = 4, Unknown = -1 }

internal readonly record struct AntiCheatService(string Name, AntiCheatServiceStart Start);

// PausedFeatures narrows which features are paused (keys "qos", "window", "volume",
// "cursor"); null or empty pauses all four. It has no UI: it exists so the trigger
// of a specific anti-cheat can be bisected on a real PC (docs/eternal/README.md).
internal sealed record AntiCheatSettings(bool Enabled, bool TreatEveryGameAsProtected, IReadOnlyList<string> ProtectedExecutables,
	IReadOnlyList<string>? PausedFeatures = null)
{
	internal static AntiCheatSettings Defaults { get; } = new(true, false, Array.Empty<string>());
}

// RunningServices: catalogue services that the service control manager reports as not stopped.
// ProcessNames: image names (without ".exe") of the processes in the user's session.
// GameFolders: install folders that a launcher reports for the running game (Steam's appmanifest).
// GameExecutables: image paths of the processes that own visible game-sized windows.
// GameRunning: a launcher reports a running game, or a non-infrastructure window is open.
internal sealed record AntiCheatObservation(
	IReadOnlyList<AntiCheatService> RunningServices,
	IReadOnlyCollection<string> ProcessNames,
	IReadOnlyList<string> GameFolders,
	IReadOnlyList<string> GameExecutables,
	bool GameRunning)
{
	internal static AntiCheatObservation Empty { get; } = new(Array.Empty<AntiCheatService>(), Array.Empty<string>(),
		Array.Empty<string>(), Array.Empty<string>(), false);
}

internal sealed record AntiCheatVerdict(bool Protected, string Reason)
{
	internal static AntiCheatVerdict None { get; } = new(false, "");
}

internal static class AntiCheatCatalog
{
	// Windows service (or kernel driver) names. Only services that start on demand,
	// with the game, count: a boot, system or automatic service runs whether or not a
	// game is open, so it says nothing about the running game (Riot's vgk driver is
	// boot-start, for example). Names are the publicly documented or widely reported
	// ones; see docs/eternal/README.md for which were confirmed and how.
	internal static readonly IReadOnlyDictionary<string, string> Services = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["EasyAntiCheat"] = "Easy Anti-Cheat",
		["EasyAntiCheat_EOS"] = "Easy Anti-Cheat (Epic Online Services)",
		["BEService"] = "BattlEye",
		["BEDaisy"] = "BattlEye",
		["vgc"] = "Riot Vanguard",
		["vgk"] = "Riot Vanguard",
		["FACEIT"] = "FACEIT",
		["FACEITService"] = "FACEIT",
		["EAAntiCheatService"] = "EA Javelin",
		["atvi-randgrid_sr"] = "Ricochet (Call of Duty)",
		["atvi-randgrid_msstore"] = "Ricochet (Call of Duty, Microsoft Store)",
		["mhyprot2"] = "mhyprot (HoYoverse)",
		["mhyprot3"] = "mhyprot (HoYoverse)",
		["npggsvc"] = "nProtect GameGuard",
		["GameGuard"] = "nProtect GameGuard",
		["xhunter1"] = "XIGNCODE3",
	};

	// File or folder names that anti-cheats install next to the game they protect.
	internal static readonly IReadOnlyDictionary<string, string> FolderMarkers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["EasyAntiCheat"] = "Easy Anti-Cheat",
		["EasyAntiCheat_EOS"] = "Easy Anti-Cheat (Epic Online Services)",
		["EasyAntiCheat_Setup.exe"] = "Easy Anti-Cheat",
		["EasyAntiCheat_EOS_Setup.exe"] = "Easy Anti-Cheat (Epic Online Services)",
		["start_protected_game.exe"] = "Easy Anti-Cheat",
		["BattlEye"] = "BattlEye",
		["BEService.exe"] = "BattlEye",
		["BEService_x64.exe"] = "BattlEye",
		["randgrid.sys"] = "Ricochet (Call of Duty)",
		["EAAntiCheat.GameServiceLauncher.exe"] = "EA Javelin",
		["EAAntiCheat.Installer.exe"] = "EA Javelin",
		["GameGuard"] = "nProtect GameGuard",
		["XIGNCODE"] = "XIGNCODE3",
	};

	// Folders that hold many games, or none: never listed, and the walk up from a
	// game's executable stops there so a neighbouring game cannot match.
	internal static readonly IReadOnlySet<string> ContainerFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"common", "steamapps", "SteamLibrary", "Steam", "Program Files", "Program Files (x86)", "Epic Games",
		"WindowsApps", "XboxGames", "Games", "Windows", "System32", "Users", "AppData", "Local", "Roaming", "ProgramData",
	};

	// Processes that own windows in Gaming Mode without being a game.
	internal static readonly IReadOnlySet<string> InfrastructureProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"GamingMode", "GamingModeSetup", "Core", "Playhub", "Playhub Setup", "Core Setup", "Core-Setup", "Core-DeckyHost", "Playhub-DeckyHost",
		"Playhub.GameSession", "Playhub.XboxSession", "Core.StoreHost", "Playhub.StoreHost", "explorer", "steam", "steamwebhelper", "sunshine", "apollo", "vibepollo", "vibeshine",
		"PluginLoader", "PluginLoader_noconsole", "powershell", "pwsh", "cmd", "conhost", "WindowsTerminal", "OpenConsole",
		"ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost",
		"LockApp", "SystemSettings", "GameBar", "GameBarFTServer", "XboxGameBarWidgets", "dwm", "ctfmon", "taskmgr",
	};
}

internal static class AntiCheatProtectionPolicy
{
	internal static AntiCheatVerdict Evaluate(AntiCheatSettings settings, AntiCheatObservation observation,
		Func<string, IReadOnlyCollection<string>?> listFolder)
	{
		if (!settings.Enabled) return AntiCheatVerdict.None;
		foreach (AntiCheatService service in observation.RunningServices)
		{
			if (AntiCheatCatalog.Services.TryGetValue(service.Name, out string? product) && IsGameScoped(service.Start))
				return new(true, $"anti-cheat service {service.Name} ({product}) is running");
		}
		HashSet<string> protectedNames = NormalizedExecutables(settings.ProtectedExecutables);
		if (protectedNames.Count > 0)
		{
			foreach (string process in observation.ProcessNames)
			{
				if (protectedNames.Contains(NormalizeExecutable(process)))
					return new(true, $"{NormalizeExecutable(process)}.exe is on the \"always protected\" list");
			}
		}
		foreach (string folder in observation.GameFolders)
		{
			if (AntiCheatFolderProbe.InInstallFolder(folder, listFolder) is { } hit)
				return new(true, $"{hit.Marker} ({hit.Product}) found in the running game's folder {hit.Folder}");
		}
		foreach (string executable in observation.GameExecutables)
		{
			if (AntiCheatFolderProbe.NearExecutable(executable, listFolder) is { } hit)
				return new(true, $"{hit.Marker} ({hit.Product}) found in {hit.Folder}, next to {WindowsPath.Leaf(WindowsPath.Normalize(executable))}");
		}
		if (settings.TreatEveryGameAsProtected && observation.GameRunning)
			return new(true, "a game is running and \"Treat every game as protected\" is on");
		return AntiCheatVerdict.None;
	}

	// Boot, system and automatic services run from Windows start-up, so their
	// presence does not mean a protected game is open. Unknown counts, to stay safe.
	internal static bool IsGameScoped(AntiCheatServiceStart start) => start is AntiCheatServiceStart.Manual or AntiCheatServiceStart.Unknown;

	internal static HashSet<string> NormalizedExecutables(IEnumerable<string>? values)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string value in values ?? Array.Empty<string>())
		{
			string name = NormalizeExecutable(value);
			if (name.Length > 0) set.Add(name);
		}
		return set;
	}

	// "cod.exe", "COD", "\"D:\\Games\\cod.exe\"" -> "cod". Process names carry no extension.
	internal static string NormalizeExecutable(string? value)
	{
		string name = (value ?? "").Trim().Trim('"', '\'').Trim();
		int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
		if (slash >= 0) name = name[(slash + 1)..];
		if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
		return name.Trim().ToLowerInvariant();
	}

	// The settings box: one executable per line; commas and semicolons also separate.
	internal static List<string> ParseExecutableList(string? text)
	{
		var result = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string part in (text ?? "").Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string trimmed = part.Trim().Trim('"', '\'').Trim();
			if (trimmed.Length == 0 || NormalizeExecutable(trimmed).Length == 0 || !seen.Add(NormalizeExecutable(trimmed))) continue;
			result.Add(trimmed);
		}
		return result;
	}
}

internal readonly record struct AntiCheatFolderHit(string Marker, string Product, string Folder);

internal static class AntiCheatFolderProbe
{
	// bin\x64\game.exe has its anti-cheat folder two levels up.
	internal const int MaxParentLevels = 3;

	internal static AntiCheatFolderHit? NearExecutable(string executable, Func<string, IReadOnlyCollection<string>?> listFolder)
	{
		string? folder = WindowsPath.Parent(WindowsPath.Normalize(executable));
		for (int level = 0; folder is not null && level <= MaxParentLevels; level++)
		{
			if (IsContainer(folder)) return null;
			if (Find(folder, listFolder) is { } hit) return hit;
			folder = WindowsPath.Parent(folder);
		}
		return null;
	}

	internal static AntiCheatFolderHit? InInstallFolder(string folder, Func<string, IReadOnlyCollection<string>?> listFolder)
	{
		string normalized = WindowsPath.Normalize(folder);
		return normalized.Length == 0 || IsContainer(normalized) ? null : Find(normalized, listFolder);
	}

	internal static bool IsContainer(string folder) => WindowsPath.Parent(folder) is null
		|| AntiCheatCatalog.ContainerFolders.Contains(WindowsPath.Leaf(folder));

	private static AntiCheatFolderHit? Find(string folder, Func<string, IReadOnlyCollection<string>?> listFolder)
	{
		IReadOnlyCollection<string>? entries;
		try { entries = listFolder(folder); }
		catch { return null; }
		if (entries is null) return null;
		foreach (string entry in entries)
		{
			if (AntiCheatCatalog.FolderMarkers.TryGetValue(WindowsPath.Leaf(WindowsPath.Normalize(entry)), out string? product))
				return new AntiCheatFolderHit(WindowsPath.Leaf(WindowsPath.Normalize(entry)), product, folder);
		}
		return null;
	}
}

// Windows path arithmetic that behaves the same when the tests run on Linux.
internal static class WindowsPath
{
	internal static string Normalize(string? path)
	{
		string value = (path ?? "").Trim().Trim('"').Replace('/', '\\');
		while (value.Length > 3 && value.EndsWith('\\')) value = value[..^1];
		return value;
	}

	internal static string? Parent(string path)
	{
		string value = Normalize(path).TrimEnd('\\');
		int separator = value.LastIndexOf('\\');
		if (separator <= 0) return null;
		string parent = value[..separator];
		if (parent.EndsWith(':')) return parent + "\\";
		return parent.Length == 0 || parent == "\\" ? null : parent;
	}

	internal static string Leaf(string path)
	{
		string value = Normalize(path).TrimEnd('\\');
		int separator = value.LastIndexOf('\\');
		return separator < 0 ? value : value[(separator + 1)..];
	}

	internal static string Combine(string folder, string child) => Normalize(folder).TrimEnd('\\') + "\\" + child;
}

// Steam's text manifests, read without touching Steam or the game.
internal static class SteamLibraryText
{
	private static readonly Regex LibraryPath = new("\"path\"\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
	private static readonly Regex InstallDirValue = new("\"installdir\"\\s+\"(?<value>[^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	internal static IReadOnlyList<string> LibraryFolders(string? libraryFoldersVdf) => LibraryPath.Matches(libraryFoldersVdf ?? "")
		.Select(match => WindowsPath.Normalize(match.Groups["path"].Value.Replace("\\\\", "\\"))).Where(path => path.Length > 0)
		.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

	internal static string? InstallDir(string? appManifest)
	{
		Match match = InstallDirValue.Match(appManifest ?? "");
		string value = match.Success ? match.Groups["value"].Value.Replace("\\\\", "\\").Trim() : "";
		// A manifest names a folder under steamapps\common, never a path elsewhere.
		return value.Length == 0 || value.Contains('\\') || value.Contains('/') || value is "." or ".." ? null : value;
	}
}

internal interface IAntiCheatPausable
{
	string Key { get; }
	string Name { get; }
	void Pause();
	void Resume();
}

internal sealed class AntiCheatPausable(string key, string name, Action pause, Action resume) : IAntiCheatPausable
{
	public string Key => key;
	public string Name => name;
	public void Pause() => pause();
	public void Resume() => resume();
}

// Engages at once when a protected game appears, and releases only after the
// protection signals have been absent for several samples in a row, so a launcher
// handing over to the game (or a service restarting) does not flap the features.
internal sealed class AntiCheatProtectionOrchestrator
{
	internal const int DefaultReleaseAfterQuietSamples = 3;
	private readonly IReadOnlyList<IAntiCheatPausable> _features;
	private readonly Action<string> _log;
	private readonly int _releaseAfter;
	private readonly object _sync = new();
	private volatile bool _engaged;
	private string _reason = "";
	private int _quiet;
	private IReadOnlySet<string>? _included;
	private readonly List<IAntiCheatPausable> _paused = new();
	private volatile IReadOnlySet<string> _pausedKeys = new HashSet<string>();

	internal AntiCheatProtectionOrchestrator(IReadOnlyList<IAntiCheatPausable> features, Action<string> log,
		int releaseAfterQuietSamples = DefaultReleaseAfterQuietSamples)
	{
		_features = features;
		_log = log;
		_releaseAfter = Math.Max(1, releaseAfterQuietSamples);
	}

	// Readable from any thread. It turns true before any feature is paused and
	// false only after every feature has resumed.
	internal bool Engaged => _engaged;

	// True while the feature with this key is (being) paused. Readable from any thread.
	internal bool IsPaused(string key) => _engaged && _pausedKeys.Contains(key);

	internal string Reason { get { lock (_sync) return _reason; } }

	// Applies from the next engagement. Null, empty or only unknown keys: all features.
	internal void SetPausedFeatures(IReadOnlyCollection<string>? keys)
	{
		var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string key in keys ?? Array.Empty<string>())
		{
			if (_features.Any(feature => string.Equals(feature.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase))) known.Add(key!.Trim());
		}
		lock (_sync) _included = known.Count == 0 ? null : known;
	}

	internal void Observe(AntiCheatVerdict verdict)
	{
		lock (_sync)
		{
			if (verdict.Protected)
			{
				_quiet = 0;
				if (!_engaged)
				{
					_paused.Clear();
					_paused.AddRange(_features.Where(feature => _included is null || _included.Contains(feature.Key)));
					_pausedKeys = new HashSet<string>(_paused.Select(feature => feature.Key), StringComparer.OrdinalIgnoreCase);
					_engaged = true;
					_reason = verdict.Reason;
					_log($"Anti-cheat safe mode engaged: {verdict.Reason}. Pausing: {Names(_paused)}.");
					foreach (IAntiCheatPausable feature in _paused) Run(feature, pause: true);
				}
				else if (!string.Equals(verdict.Reason, _reason, StringComparison.Ordinal))
				{
					_reason = verdict.Reason;
					_log($"Anti-cheat safe mode still engaged: {verdict.Reason}.");
				}
				return;
			}
			if (!_engaged || ++_quiet < _releaseAfter) return;
			ReleaseLocked($"no protected game is detected any more (it was engaged because {_reason})");
		}
	}

	internal void Release(string why)
	{
		lock (_sync) ReleaseLocked(why);
	}

	private void ReleaseLocked(string why)
	{
		if (!_engaged) return;
		_log($"Anti-cheat safe mode released: {why}. Resuming: {Names(_paused)}.");
		for (int index = _paused.Count - 1; index >= 0; index--) Run(_paused[index], pause: false);
		_paused.Clear();
		_quiet = 0;
		_reason = "";
		_engaged = false;
		_pausedKeys = new HashSet<string>();
	}

	private static string Names(IEnumerable<IAntiCheatPausable> features)
	{
		string names = string.Join(", ", features.Select(feature => feature.Name));
		return names.Length == 0 ? "nothing" : names;
	}

	private void Run(IAntiCheatPausable feature, bool pause)
	{
		try
		{
			if (pause) feature.Pause();
			else feature.Resume();
		}
		catch (Exception error)
		{
			_log($"Anti-cheat safe mode could not {(pause ? "pause" : "resume")} {feature.Name}: {error.GetType().Name}: {error.Message}");
		}
	}
}
