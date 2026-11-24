using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GamingMode.Models;
using Microsoft.Win32;

namespace GamingMode.Services;

// Windows half of the anti-cheat safe mode (see AntiCheatProtection.cs).
//
// Every observation is passive. Nothing here opens a handle to a game process,
// injects into it, enumerates its modules or reads its memory:
// - services: the service control manager, read-only (SERVICE_QUERY_STATUS and
//   SERVICE_QUERY_CONFIG on the anti-cheat services only);
// - process names: the system process list (Process.GetProcesses reads it with
//   NtQuerySystemInformation; only Id, SessionId and ProcessName are read);
// - image paths: NtQuerySystemInformation(SystemProcessIdInformation), which
//   returns a process's image name by process id without opening the process;
// - the running Steam game: HKCU\Software\Valve\Steam\RunningAppID and Steam's
//   own manifests on disk;
// - windows: EnumWindows and window attributes, as the shell does;
// - anti-cheat files: a directory listing of the game's folder.
internal sealed class AntiCheatProtectionMonitor : IDisposable
{
	private const int SampleMs = 750;
	private const int SettingsEverySamples = 4;
	private const int MaxFolderEntries = 4096;
	private static readonly int Session = CurrentSession();
	private readonly JsonStore _store;
	private readonly FileLogger _logger;
	private readonly string? _configuredSteamPath;
	private readonly AntiCheatProtectionOrchestrator _orchestrator;
	private readonly object _lifetime = new();
	private readonly Dictionary<string, AntiCheatServiceStart> _startTypes = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _reportedPermanent = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IReadOnlyCollection<string>?> _folderListings = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, string?> _imagePaths = new();
	private readonly Dictionary<uint, string?> _steamInstallFolders = new();
	private CancellationTokenSource? _stop;
	private Thread? _thread;
	private AntiCheatSettings _settings = AntiCheatSettings.Defaults;
	private string _settingsDescription = "";
	private int _sample;
	private long _startTypesReadAt;

	internal AntiCheatProtectionMonitor(JsonStore store, FileLogger logger, string? configuredSteamPath, IReadOnlyList<IAntiCheatPausable> features)
	{
		_store = store;
		_logger = logger;
		_configuredSteamPath = configuredSteamPath;
		_orchestrator = new AntiCheatProtectionOrchestrator(features, logger.Info);
	}

	internal bool Engaged => _orchestrator.Engaged;

	internal bool Pauses(string featureKey) => _orchestrator.IsPaused(featureKey);

	// The first sample runs on the caller, so a protected game that is already
	// running is detected before Gaming Mode's own services start.
	internal void Start()
	{
		lock (_lifetime)
		{
			if (_thread is not null) return;
			Sample();
			_stop = new CancellationTokenSource();
			CancellationToken token = _stop.Token;
			_thread = new Thread(() => Run(token)) { IsBackground = true, Name = "Gaming Mode Anti-cheat Safe Mode", Priority = ThreadPriority.BelowNormal };
			_thread.Start();
		}
	}

	public void Dispose()
	{
		Thread? thread;
		lock (_lifetime)
		{
			thread = _thread;
			_thread = null;
			_stop?.Cancel();
		}
		thread?.Join(TimeSpan.FromSeconds(3));
		// No resume here: the agent is stopping and disposes the paused features itself.
		_stop?.Dispose();
	}

	private void Run(CancellationToken token)
	{
		while (!token.WaitHandle.WaitOne(SampleMs))
		{
			Sample();
		}
	}

	private void Sample()
	{
		try
		{
			if (_sample++ % SettingsEverySamples == 0) ReloadSettings();
			if (!_settings.Enabled)
			{
				_orchestrator.Observe(AntiCheatVerdict.None);
				return;
			}
			AntiCheatObservation observation = Observe();
			_orchestrator.Observe(AntiCheatProtectionPolicy.Evaluate(_settings, observation, ListFolder));
		}
		catch (Exception error)
		{
			// An observation that failed proves nothing either way: keep the current state.
			if (_sample % 80 == 1) _logger.Error("Anti-cheat safe mode could not sample the system.", error);
		}
	}

	private void ReloadSettings()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		var settings = new AntiCheatSettings(gaming.AntiCheatSafeModeEnabled, gaming.AntiCheatTreatEveryGameAsProtected,
			(gaming.AntiCheatProtectedExecutables ?? new List<string>()).ToArray(), gaming.AntiCheatPausedFeatures?.ToArray());
		string description = $"enabled={settings.Enabled}, treatEveryGameAsProtected={settings.TreatEveryGameAsProtected}, "
			+ $"alwaysProtected=[{string.Join(", ", AntiCheatProtectionPolicy.NormalizedExecutables(settings.ProtectedExecutables).Order(StringComparer.OrdinalIgnoreCase))}], "
			+ $"pausedFeatures=[{(settings.PausedFeatures is { Count: > 0 } keys ? string.Join(", ", keys) : "all")}]";
		_settings = settings;
		_orchestrator.SetPausedFeatures(settings.PausedFeatures);
		if (description == _settingsDescription) return;
		_settingsDescription = description;
		_logger.Info($"Anti-cheat safe mode settings: {description}.");
	}

	private AntiCheatObservation Observe()
	{
		IReadOnlyList<AntiCheatService> services = RunningServices();
		int self = Environment.ProcessId;
		var names = new Dictionary<int, string>();
		Process[] processes = Process.GetProcesses();
		try
		{
			foreach (Process process in processes)
			{
				try
				{
					if (process.SessionId == Session && process.Id != self) names[process.Id] = process.ProcessName;
				}
				catch (Exception) { /* The process exited while the list was read. */ }
			}
		}
		finally
		{
			foreach (Process process in processes) process.Dispose();
		}
		foreach (int stale in _imagePaths.Keys.Where(pid => !names.ContainsKey(pid)).ToArray()) _imagePaths.Remove(stale);

		var folders = new List<string>();
		uint runningApp = ReadSteamRunningAppId();
		if (runningApp is > 0 and < 0x80000000 && SteamInstallFolder(runningApp) is { } installFolder) folders.Add(installFolder);

		var executables = new List<string>();
		foreach (int pid in GameWindowProcesses(names))
		{
			if (ImagePath(pid) is { } path) executables.Add(path);
		}
		bool gameRunning = runningApp != 0 || executables.Count > 0;
		return new AntiCheatObservation(services, names.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), folders,
			executables.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), gameRunning);
	}

	private static int CurrentSession()
	{
		using Process current = Process.GetCurrentProcess();
		return current.SessionId;
	}

	// ---------- Service control manager (read-only) ----------

	private IReadOnlyList<AntiCheatService> RunningServices()
	{
		var running = new List<AntiCheatService>();
		nint manager = OpenSCManager(null, null, ScManagerConnect);
		if (manager == 0) return running;
		try
		{
			bool refreshStartTypes = Environment.TickCount64 - _startTypesReadAt > 30000;
			if (refreshStartTypes) _startTypesReadAt = Environment.TickCount64;
			foreach (string name in AntiCheatCatalog.Services.Keys)
			{
				nint service = OpenService(manager, name, ServiceQueryStatus | ServiceQueryConfig);
				if (service == 0) continue; // Not installed.
				try
				{
					if (!QueryServiceStatus(service, out ServiceStatus status) || status.CurrentState == ServiceStopped) continue;
					if (refreshStartTypes || !_startTypes.ContainsKey(name)) _startTypes[name] = ReadStartType(service);
					AntiCheatServiceStart start = _startTypes[name];
					if (!AntiCheatProtectionPolicy.IsGameScoped(start) && _reportedPermanent.Add(name))
						_logger.Info($"Anti-cheat safe mode: {name} ({AntiCheatCatalog.Services[name]}) runs from Windows start-up ({start}), so it is not used to tell when a game is open. Add the game's executable to the always-protected list if needed.");
					running.Add(new AntiCheatService(name, start));
				}
				finally { CloseServiceHandle(service); }
			}
		}
		finally { CloseServiceHandle(manager); }
		return running;
	}

	private static AntiCheatServiceStart ReadStartType(nint service)
	{
		const int Size = 8192;
		nint buffer = Marshal.AllocHGlobal(Size);
		try
		{
			// QUERY_SERVICE_CONFIGW starts with dwServiceType, then dwStartType.
			if (!QueryServiceConfig(service, buffer, Size, out _)) return AntiCheatServiceStart.Unknown;
			int start = Marshal.ReadInt32(buffer, 4);
			return start is >= 0 and <= 4 ? (AntiCheatServiceStart)start : AntiCheatServiceStart.Unknown;
		}
		finally { Marshal.FreeHGlobal(buffer); }
	}

	// ---------- Steam's running game, from the registry and manifests ----------

	private static uint ReadSteamRunningAppId()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			return key?.GetValue("RunningAppID") switch
			{
				int number when number > 0 => (uint)number,
				long number when number > 0 && number <= uint.MaxValue => (uint)number,
				string text when uint.TryParse(text, out uint parsed) => parsed,
				_ => 0
			};
		}
		catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
		{
			return 0;
		}
	}

	private string? SteamInstallFolder(uint appId)
	{
		if (_steamInstallFolders.TryGetValue(appId, out string? cached)) return cached;
		string? found = null;
		try
		{
			if (SteamInstallationPath.FindRoot(_configuredSteamPath) is { } root)
			{
				string primary = Path.Combine(root, "steamapps");
				string vdf = Path.Combine(primary, "libraryfolders.vdf");
				var libraries = new List<string> { primary };
				if (File.Exists(vdf))
					libraries.AddRange(SteamLibraryText.LibraryFolders(File.ReadAllText(vdf)).Select(library => Path.Combine(library, "steamapps")));
				foreach (string steamApps in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
				{
					string manifest = Path.Combine(steamApps, $"appmanifest_{appId}.acf");
					if (!File.Exists(manifest) || SteamLibraryText.InstallDir(File.ReadAllText(manifest)) is not { } installDir) continue;
					string folder = Path.Combine(steamApps, "common", installDir);
					if (Directory.Exists(folder)) { found = folder; break; }
				}
			}
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
		{
			found = null;
		}
		if (_steamInstallFolders.Count > 64) _steamInstallFolders.Clear();
		_steamInstallFolders[appId] = found;
		return found;
	}

	// ---------- Visible game windows ----------

	private static IEnumerable<int> GameWindowProcesses(IReadOnlyDictionary<int, string> names)
	{
		var owners = new HashSet<int>();
		foreach (nint window in OverlayWindowTools.EnumerateTopLevelHandles())
		{
			if (!IsWindowVisible(window) || IsIconic(window) || IsCloaked(window) || !GetWindowRect(window, out Rect rect)
				|| rect.Right - rect.Left < 220 || rect.Bottom - rect.Top < 120) continue;
			if (GetWindowThreadProcessId(window, out uint pid) == 0 || pid == 0 || pid > int.MaxValue) continue;
			if (!names.TryGetValue((int)pid, out string? name) || IsInfrastructure(name)) continue;
			owners.Add((int)pid);
		}
		return owners;
	}

	private static bool IsInfrastructure(string name) => AntiCheatCatalog.InfrastructureProcesses.Contains(name)
		|| SteamGameIdentityPolicy.IsSteamClient(name) || SteamGameIdentityPolicy.IsXboxLaunchHelper(name)
		|| FullscreenWindowGeometry.IsOverlayHostProcess(name);

	// ---------- Image path by process id, without opening the process ----------

	private string? ImagePath(int pid)
	{
		if (_imagePaths.TryGetValue(pid, out string? cached)) return cached;
		string? path = ReadImagePath(pid);
		_imagePaths[pid] = path;
		return path;
	}

	private static string? ReadImagePath(int pid)
	{
		const ushort Capacity = 32766;
		nint buffer = Marshal.AllocHGlobal(Capacity);
		try
		{
			var information = new SystemProcessIdInformation
			{
				ProcessId = pid,
				ImageName = new UnicodeString { Length = 0, MaximumLength = Capacity, Buffer = buffer }
			};
			int status = NtQuerySystemInformation(SystemProcessIdInformationClass, ref information,
				Marshal.SizeOf<SystemProcessIdInformation>(), out _);
			if (status != 0 || information.ImageName.Length == 0) return null;
			string device = Marshal.PtrToStringUni(buffer, information.ImageName.Length / 2);
			return DevicePathToDosPath(device);
		}
		finally { Marshal.FreeHGlobal(buffer); }
	}

	private static string? DevicePathToDosPath(string device)
	{
		if (device.Length >= 3 && device[1] == ':') return device;
		var target = new StringBuilder(1024);
		foreach (string drive in Environment.GetLogicalDrives())
		{
			string letter = drive.TrimEnd('\\');
			target.Clear();
			if (QueryDosDevice(letter, target, target.Capacity) == 0) continue;
			string prefix = target.ToString();
			if (prefix.Length > 0 && device.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
				return letter + device[prefix.Length..];
		}
		return null;
	}

	// ---------- Anti-cheat files next to the game ----------

	private IReadOnlyCollection<string>? ListFolder(string folder)
	{
		if (_folderListings.TryGetValue(folder, out IReadOnlyCollection<string>? cached)) return cached;
		IReadOnlyCollection<string>? entries;
		try
		{
			entries = Directory.EnumerateFileSystemEntries(folder).Take(MaxFolderEntries)
				.Select(entry => Path.GetFileName(entry)).ToArray();
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
			or System.Security.SecurityException)
		{
			entries = null;
		}
		if (_folderListings.Count > 256) _folderListings.Clear();
		_folderListings[folder] = entries;
		return entries;
	}

	// ---------- Native ----------

	private const uint ScManagerConnect = 0x0001;
	private const uint ServiceQueryConfig = 0x0001;
	private const uint ServiceQueryStatus = 0x0004;
	private const uint ServiceStopped = 1;
	private const int SystemProcessIdInformationClass = 88;

	[StructLayout(LayoutKind.Sequential)]
	private struct ServiceStatus
	{
		public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct UnicodeString
	{
		public ushort Length;
		public ushort MaximumLength;
		public nint Buffer;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SystemProcessIdInformation
	{
		public nint ProcessId;
		public UnicodeString ImageName;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct Rect { public int Left, Top, Right, Bottom; }

	[DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint OpenSCManager(string? machineName, string? databaseName, uint access);
	[DllImport("advapi32.dll", EntryPoint = "OpenServiceW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint OpenService(nint manager, string serviceName, uint access);
	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool QueryServiceStatus(nint service, out ServiceStatus status);
	[DllImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool QueryServiceConfig(nint service, nint config, int size, out int needed);
	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool CloseServiceHandle(nint handle);
	[DllImport("ntdll.dll")]
	private static extern int NtQuerySystemInformation(int informationClass, ref SystemProcessIdInformation information, int length, out int returned);
	[DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int QueryDosDevice(string deviceName, StringBuilder targetPath, int max);
	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint window);
	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint window);
	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint window, out Rect rect);
	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

	private static bool IsCloaked(nint window) => DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
}
