using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Playhub.Shared;

namespace GamingMode.Services;

// The Windows half of the console guards (rules in ConsoleGuards.cs). Everything here runs without
// administrator rights and without a driver, and every probe fails soft: it answers null (unknown)
// rather than throwing. Nothing reads a password, a recovery key or a sign-in token.

internal static class ConsoleProbes
{
	private static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

	private static object? MachineValue(string key, string name)
	{
		try
		{
			using RegistryKey root = Machine();
			using RegistryKey? sub = root.OpenSubKey(key);
			return sub?.GetValue(name);
		}
		catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
	}

	// ---------- BitLocker ----------

	// The Shell's System.Volume.BitLockerProtection for the system drive (what Explorer's padlock shows),
	// read on a short-lived STA thread through Shell.Application. No administrator rights needed.
	internal static string? BitLockerProtection()
	{
		string drive = BitLockerGuardWindows.SystemDrive;
		int? value = null;
		var thread = new Thread(() =>
		{
			object? shell = null;
			try
			{
				Type? type = Type.GetTypeFromProgID("Shell.Application");
				if (type is null) return;
				shell = Activator.CreateInstance(type);
				object? folder = type.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { drive + "\\" });
				object? self = folder?.GetType().InvokeMember("Self", BindingFlags.GetProperty, null, folder, null);
				object? read = self?.GetType().InvokeMember("ExtendedProperty", BindingFlags.InvokeMethod, null, self, new object[] { "System.Volume.BitLockerProtection" });
				value = read switch { int number => number, uint number => (int)number, long number => (int)number, _ => null };
			}
			catch { value = null; }
			finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
		}) { IsBackground = true, Name = "Core BitLocker probe" };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		if (!thread.Join(TimeSpan.FromSeconds(5))) return null;
		return value is null ? null : ConsoleGuardPolicy.BitLockerFromShell(value);
	}

	// Staged UEFI capsules against the installed firmware (FirmwarePolicy.Pending explains the keys).
	internal static IReadOnlyList<string> FirmwarePending()
	{
		var staged = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		var installed = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		try
		{
			using RegistryKey root = Machine();
			using (RegistryKey? resources = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FirmwareResources"))
				if (resources is not null)
					foreach (string id in resources.GetSubKeyNames().Take(32))
					{
						using RegistryKey? entry = resources.OpenSubKey(id);
						if (entry?.GetValue("Version") is int version) staged[id.Trim('{', '}')] = unchecked((uint)version);
					}
			using (RegistryKey? esrt = root.OpenSubKey(@"HARDWARE\UEFI\ESRT"))
				if (esrt is not null)
					foreach (string id in esrt.GetSubKeyNames().Take(32))
					{
						using RegistryKey? entry = esrt.OpenSubKey(id);
						if (entry?.GetValue("Version") is int version) installed[id.Trim('{', '}')] = unchecked((uint)version);
					}
		}
		catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return Array.Empty<string>(); }
		return FirmwarePolicy.Pending(staged, installed);
	}

	// When Windows last started (for "suspended since the last boot").
	internal static DateTimeOffset BootTime() => DateTimeOffset.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);

	// ---------- automatic sign-in ----------

	internal static AutoSignInState? AutoSignIn()
	{
		try
		{
			using RegistryKey root = Machine();
			using RegistryKey? winlogon = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
			if (winlogon is null) return null;
			// Only the names: DefaultPassword's value is never read.
			bool password = winlogon.GetValueNames().Any(name => string.Equals(name, "DefaultPassword", StringComparison.OrdinalIgnoreCase));
			object? passwordLess = MachineValue(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device", "DevicePasswordLessBuildVersion");
			return AutoSignInPolicy.Read(winlogon.GetValue("AutoAdminLogon") as string, winlogon.GetValue("DefaultUserName") as string, password,
				winlogon.GetValue("AutoLogonCount"), passwordLess);
		}
		catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
	}

	// ---------- Windows edition and update policy ----------

	private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

	internal static string? Edition() => MachineValue(CurrentVersion, "EditionID") as string;

	internal static string? DisplayVersion() => MachineValue(CurrentVersion, "DisplayVersion") as string;

	internal static UpdatePolicyState UpdatePolicy()
	{
		const string update = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
		return UpdatePolicyRules.Evaluate(Edition(), DisplayVersion(), MachineValue(update, "ExcludeWUDriversInQualityUpdate"),
			MachineValue(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig"),
			MachineValue(update, "TargetReleaseVersion"), MachineValue(update, "TargetReleaseVersionInfo") as string);
	}

	// Windows' setup and welcome screens: the per-user Quiet Windows values (CouchHardening "setup-nag", "tips").
	internal static bool? PromptsOff()
	{
		try
		{
			var registry = new CouchRegistryWindows();
			return CouchHardening.Fixes.Where(fix => fix.Id is "setup-nag" or "tips").All(fix => CouchHardening.IsApplied(registry, fix));
		}
		catch { return null; }
	}

	// ---------- launcher updates ----------

	internal static IReadOnlyList<string> PendingLauncherUpdates()
	{
		IEnumerable<string> renames = MachineValue(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") is string[] list ? list.Take(512) : Array.Empty<string>();
		var names = new List<string>();
		Process[] processes = Array.Empty<Process>();
		try
		{
			processes = Process.GetProcesses();
			foreach (Process process in processes) { try { names.Add(process.ProcessName); } catch (InvalidOperationException) { } }
		}
		catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
		finally { foreach (Process process in processes) process.Dispose(); }
		return LauncherUpdatePolicy.Pending(renames, names);
	}

	// ---------- network ----------

	internal static (bool? Up, string? Name) Ethernet()
	{
		try
		{
			var adapters = NetworkInterface.GetAllNetworkInterfaces().Select(adapter =>
			{
				bool gateway = false;
				try { gateway = adapter.GetIPProperties().GatewayAddresses.Any(address => !address.Address.Equals(System.Net.IPAddress.Any) && !address.Address.Equals(System.Net.IPAddress.IPv6Any)); }
				catch (NetworkInformationException) { }
				bool ethernet = adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;
				return new NetworkAdapter(adapter.Name, adapter.Description, ethernet, adapter.OperationalStatus == OperationalStatus.Up, gateway);
			}).ToList();
			return NetworkRules.Ethernet(adapters);
		}
		catch (NetworkInformationException) { return (null, null); }
	}

	// ---------- Steam ----------

	internal static SteamSignIn? Steam(string? steamRoot, bool running)
	{
		try
		{
			object? activeUser = null;
			using (RegistryKey? active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess")) activeUser = active?.GetValue("ActiveUser");
			IReadOnlyList<SteamSignInPolicy.LoginUser> users = Array.Empty<SteamSignInPolicy.LoginUser>();
			string? file = steamRoot is null ? null : Path.Combine(steamRoot, "config", "loginusers.vdf");
			if (file is not null && File.Exists(file) && new FileInfo(file).Length < 256 * 1024) users = SteamSignInPolicy.ParseLoginUsers(File.ReadAllText(file));
			return SteamSignInPolicy.Evaluate(running, activeUser, users);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
	}
}

// ---------- controllers (SetupAPI: present HID devices, no device is opened) ----------

internal static class WindowsControllers
{
	private static readonly Guid HidClass = new("745A17A0-74D3-11D0-B6FE-00A0C90F57DA");
	private const uint DigcfPresent = 0x2;
	private const uint SpdrpDeviceDesc = 0, SpdrpHardwareId = 1, SpdrpCompatibleIds = 2;

	internal static IReadOnlyList<ControllerSeen>? Read()
	{
		Guid hid = HidClass;
		nint set = SetupDiGetClassDevsW(ref hid, 0, 0, DigcfPresent);
		if (set == 0 || set == -1) return null;
		try
		{
			var seen = new List<ControllerSeen>();
			var info = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
			for (uint index = 0; index < 256 && SetupDiEnumDeviceInfo(set, index, ref info); index++)
			{
				IReadOnlyList<string> hardware = MultiString(set, ref info, SpdrpHardwareId);
				IReadOnlyList<string> compatible = MultiString(set, ref info, SpdrpCompatibleIds);
				string? description = MultiString(set, ref info, SpdrpDeviceDesc).FirstOrDefault();
				if (ControllerCatalog.FromHid(hardware, compatible, description) is { } controller) seen.Add(controller);
			}
			return ControllerCatalog.Distinct(seen);
		}
		catch (Exception error) when (error is ExternalException or EntryPointNotFoundException or DllNotFoundException) { return null; }
		finally { SetupDiDestroyDeviceInfoList(set); }
	}

	private static IReadOnlyList<string> MultiString(nint set, ref SpDevinfoData info, uint property)
	{
		var buffer = new byte[2048];
		if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, buffer, (uint)buffer.Length, out uint size) || size == 0) return Array.Empty<string>();
		return Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).Split('\0', StringSplitOptions.RemoveEmptyEntries);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct SpDevinfoData { public uint cbSize; public Guid ClassGuid; public uint DevInst; public nint Reserved; }

	[DllImport("setupapi.dll", SetLastError = true)] private static extern nint SetupDiGetClassDevsW(ref Guid classGuid, nint enumerator, nint parent, uint flags);
	[DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref SpDevinfoData data);
	[DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceRegistryPropertyW(nint set, ref SpDevinfoData data, uint property, out uint type, byte[] buffer, uint size, out uint required);
	[DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
}

// ---------- displays (DisplayConfig for names and HDR, ChangeDisplaySettingsEx for modes) ----------

internal static class WindowsDisplays
{
	// Every active display: its target path and monitor name (DisplayConfig), its GDI device's current
	// mode and position (EnumDisplaySettingsEx; the primary sits at 0,0) and HDR (advanced colour).
	internal static IReadOnlyList<DisplayProfile>? Read()
	{
		try
		{
			bool? vrr = WindowsVrr();
			var result = new List<DisplayProfile>();
			foreach (Target target in Targets())
			{
				var mode = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
				if (!EnumDisplaySettingsExW(target.Gdi, EnumCurrentSettings, ref mode, 0)) continue;
				bool primary = mode.dmPositionX == 0 && mode.dmPositionY == 0;
				result.Add(new DisplayProfile(target.Path, target.Gdi, target.Monitor, (int)mode.dmPelsWidth, (int)mode.dmPelsHeight, (int)mode.dmDisplayFrequency, target.Hdr, primary, vrr));
			}
			return result;
		}
		catch (Exception error) when (error is ExternalException or EntryPointNotFoundException or DllNotFoundException or ArgumentException) { return null; }
	}

	// Windows' own "Variable refresh rate" switch (Settings > Display > Graphics), per user:
	// HKCU\Software\Microsoft\DirectX\UserGpuPreferences "DirectXUserGlobalSettings" = "VRROptimizeEnable=1;...".
	// G-Sync/FreeSync themselves live in the GPU driver and have no readable Windows setting.
	private static bool? WindowsVrr()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences");
			if (key?.GetValue("DirectXUserGlobalSettings") is not string text) return null;
			if (text.Contains("VRROptimizeEnable=1", StringComparison.OrdinalIgnoreCase)) return true;
			if (text.Contains("VRROptimizeEnable=0", StringComparison.OrdinalIgnoreCase)) return false;
			return null;
		}
		catch { return null; }
	}

	// Restores the saved display: its mode, its place as the primary display and HDR. Each step is tried on
	// its own and reported; the VRR switch is shown but not written (it is the user's Windows setting).
	internal static (bool Mode, bool Primary, bool Hdr) Restore(DisplayProfile saved)
	{
		IReadOnlyList<DisplayProfile>? current = Read();
		DisplayProfile? now = current is null ? null : ConsoleGuardPolicy.Match(saved, current);
		if (now?.Device is not { } gdi) return (false, false, false);
		bool mode = true, primary = true, hdr = true;
		if (now.Width != saved.Width || now.Height != saved.Height || Math.Abs(now.RefreshHz - saved.RefreshHz) > 1)
		{
			var wanted = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
			if (EnumDisplaySettingsExW(gdi, EnumCurrentSettings, ref wanted, 0))
			{
				wanted.dmPelsWidth = (uint)saved.Width; wanted.dmPelsHeight = (uint)saved.Height; wanted.dmDisplayFrequency = (uint)saved.RefreshHz;
				wanted.dmFields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
				mode = ChangeDisplaySettingsExW(gdi, ref wanted, 0, CdsUpdateRegistry, 0) == DispChangeSuccessful;
			}
			else mode = false;
		}
		if (saved.Primary && !now.Primary) primary = MakePrimary(gdi);
		if (saved.Hdr is bool wantHdr && now.Hdr is bool hasHdr && wantHdr != hasHdr) hdr = SetHdr(saved, wantHdr);
		return (mode, primary, hdr);
	}

	// Moves every attached display so the chosen one sits at 0,0 and marks it primary, then applies the
	// whole layout at once (the documented way: CDS_SET_PRIMARY with CDS_NORESET, then a final apply).
	private static bool MakePrimary(string gdi)
	{
		var target = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
		if (!EnumDisplaySettingsExW(gdi, EnumCurrentSettings, ref target, 0)) return false;
		int offsetX = target.dmPositionX, offsetY = target.dmPositionY;
		foreach (Target other in Targets())
		{
			var mode = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
			if (!EnumDisplaySettingsExW(other.Gdi, EnumCurrentSettings, ref mode, 0)) continue;
			bool isTarget = string.Equals(other.Gdi, gdi, StringComparison.OrdinalIgnoreCase);
			mode.dmPositionX = isTarget ? 0 : mode.dmPositionX - offsetX;
			mode.dmPositionY = isTarget ? 0 : mode.dmPositionY - offsetY;
			mode.dmFields = DmPosition;
			uint flags = CdsUpdateRegistry | CdsNoReset | (isTarget ? CdsSetPrimary : 0);
			if (ChangeDisplaySettingsExW(other.Gdi, ref mode, 0, flags, 0) != DispChangeSuccessful) return false;
		}
		return ChangeDisplaySettingsExW(null, 0, 0, 0, 0) == DispChangeSuccessful;
	}

	private static bool SetHdr(DisplayProfile saved, bool enable)
	{
		foreach (Target target in Targets())
		{
			bool same = !string.IsNullOrEmpty(saved.Path) ? string.Equals(target.Path, saved.Path, StringComparison.OrdinalIgnoreCase)
				: string.Equals(target.Gdi, saved.Device, StringComparison.OrdinalIgnoreCase);
			if (!same) continue;
			var request = new SetAdvancedColorState
			{
				header = new DeviceInfoHeader { type = DeviceInfoSetAdvancedColorState, size = (uint)Marshal.SizeOf<SetAdvancedColorState>(), adapterId = target.Adapter, id = target.TargetId },
				value = enable ? 1u : 0u,
			};
			return DisplayConfigSetDeviceInfo(ref request) == 0;
		}
		return false;
	}

	private sealed record Target(string Gdi, string? Path, string? Monitor, bool? Hdr, Luid Adapter, uint TargetId);

	private static List<Target> Targets()
	{
		var targets = new List<Target>();
		for (int attempt = 0; attempt < 3; attempt++)
		{
			if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return targets;
			var paths = new PathInfo[pathCount];
			var modes = new ModeInfo[modeCount];
			int status = QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, 0);
			if (status == ErrorInsufficientBuffer) continue;
			if (status != 0) return targets;
			for (int i = 0; i < pathCount; i++)
			{
				PathInfo path = paths[i];
				var source = new SourceDeviceName { header = new DeviceInfoHeader { type = DeviceInfoGetSourceName, size = (uint)Marshal.SizeOf<SourceDeviceName>(), adapterId = path.sourceInfo.adapterId, id = path.sourceInfo.id } };
				if (DisplayConfigGetDeviceInfo(ref source) != 0 || string.IsNullOrEmpty(source.viewGdiDeviceName)) continue;
				var name = new TargetDeviceName { header = new DeviceInfoHeader { type = DeviceInfoGetTargetName, size = (uint)Marshal.SizeOf<TargetDeviceName>(), adapterId = path.targetInfo.adapterId, id = path.targetInfo.id } };
				bool named = DisplayConfigGetDeviceInfo(ref name) == 0;
				var color = new AdvancedColorInfo { header = new DeviceInfoHeader { type = DeviceInfoGetAdvancedColorInfo, size = (uint)Marshal.SizeOf<AdvancedColorInfo>(), adapterId = path.targetInfo.adapterId, id = path.targetInfo.id } };
				// Bit 0: advanced colour supported, bit 1: enabled. Unsupported displays report no HDR state.
				bool? hdr = DisplayConfigGetDeviceInfo(ref color) == 0 && (color.value & 0x1) != 0 ? (color.value & 0x2) != 0 : null;
				targets.Add(new Target(source.viewGdiDeviceName, named && !string.IsNullOrEmpty(name.monitorDevicePath) ? name.monitorDevicePath : null,
					named && !string.IsNullOrWhiteSpace(name.monitorFriendlyDeviceName) ? name.monitorFriendlyDeviceName.Trim() : null, hdr, path.targetInfo.adapterId, path.targetInfo.id));
			}
			return targets;
		}
		return targets;
	}

	// ---------- Win32 ----------

	private const uint QdcOnlyActivePaths = 0x2;
	private const int ErrorInsufficientBuffer = 122;
	private const uint DeviceInfoGetSourceName = 1, DeviceInfoGetTargetName = 2, DeviceInfoGetAdvancedColorInfo = 9, DeviceInfoSetAdvancedColorState = 10;
	private const int EnumCurrentSettings = -1;
	private const uint DmPosition = 0x20, DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000;
	private const uint CdsUpdateRegistry = 0x1, CdsSetPrimary = 0x10, CdsNoReset = 0x10000000;
	private const int DispChangeSuccessful = 0;

	[StructLayout(LayoutKind.Sequential)] private struct Luid { public uint LowPart; public int HighPart; }
	[StructLayout(LayoutKind.Sequential)] private struct Rational { public uint Numerator; public uint Denominator; }
	[StructLayout(LayoutKind.Sequential)] private struct PathSourceInfo { public Luid adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
	[StructLayout(LayoutKind.Sequential)]
	private struct PathTargetInfo
	{
		public Luid adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling;
		public Rational refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
	}
	[StructLayout(LayoutKind.Sequential)] private struct PathInfo { public PathSourceInfo sourceInfo; public PathTargetInfo targetInfo; public uint flags; }
	// DISPLAYCONFIG_MODE_INFO is 64 bytes; its content is not read, but QueryDisplayConfig writes the whole array.
	[StructLayout(LayoutKind.Sequential, Size = 64)] private struct ModeInfo { public uint infoType; }
	[StructLayout(LayoutKind.Sequential)] private struct DeviceInfoHeader { public uint type; public uint size; public Luid adapterId; public uint id; }
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SourceDeviceName { public DeviceInfoHeader header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName; }
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct TargetDeviceName
	{
		public DeviceInfoHeader header; public uint flags; public uint outputTechnology; public ushort edidManufactureId; public ushort edidProductCodeId; public uint connectorInstance;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
	}
	[StructLayout(LayoutKind.Sequential)] private struct AdvancedColorInfo { public DeviceInfoHeader header; public uint value; public uint colorEncoding; public uint bitsPerColorChannel; }
	[StructLayout(LayoutKind.Sequential)] private struct SetAdvancedColorState { public DeviceInfoHeader header; public uint value; }

	// DEVMODEW, display variant (220 bytes).
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DevMode
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
		public ushort dmSpecVersion; public ushort dmDriverVersion; public ushort dmSize; public ushort dmDriverExtra;
		public uint dmFields;
		public int dmPositionX; public int dmPositionY;
		public uint dmDisplayOrientation; public uint dmDisplayFixedOutput;
		public short dmColor; public short dmDuplex; public short dmYResolution; public short dmTTOption; public short dmCollate;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
		public ushort dmLogPixels; public uint dmBitsPerPel; public uint dmPelsWidth; public uint dmPelsHeight; public uint dmDisplayFlags; public uint dmDisplayFrequency;
		public uint dmICMMethod; public uint dmICMIntent; public uint dmMediaType; public uint dmDitherType; public uint dmReserved1; public uint dmReserved2;
		public uint dmPanningWidth; public uint dmPanningHeight;
	}

	[DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
	[DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, nint topologyId);
	[DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SourceDeviceName request);
	[DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TargetDeviceName request);
	[DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo request);
	[DllImport("user32.dll")] private static extern int DisplayConfigSetDeviceInfo(ref SetAdvancedColorState request);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettingsExW(string deviceName, int modeNum, ref DevMode mode, uint flags);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsExW(string deviceName, ref DevMode mode, nint window, uint flags, nint parameter);
	// The final "apply everything queued with CDS_NORESET" call: all arguments null.
	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")] private static extern int ChangeDisplaySettingsExW(string? deviceName, nint mode, nint window, uint flags, nint parameter);
}

// ---------- launcher windows: push back behind Big Picture ----------

internal static class LauncherWindows
{
	// The process behind a window, by name ("EpicGamesLauncher"); null when it cannot be read.
	internal static string? ProcessName(nint window)
	{
		if (window == 0) return null;
		GetWindowThreadProcessId(window, out uint processId);
		if (processId == 0) return null;
		try { using Process process = Process.GetProcessById((int)processId); return process.ProcessName; }
		catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return null; }
	}

	// Sends the window to the bottom of the Z order without activating, closing or minimising it.
	internal static bool PushBack(nint window) => window != 0 && SetWindowPos(window, new nint(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /* no size, move, activate */);

	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
	[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
