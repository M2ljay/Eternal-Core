using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace GamingMode.Services;

// Eternal Core batch 2: the Windows observations the Home Assistant bridge, the automations and sleep after streaming
// need. Each one fails soft (null or "unknown") and none of them needs administrator rights.
internal static class Batch2Probes
{
	// ---------- last local input (GetLastInputInfo: keyboard and mouse, including Steam's desktop trackpad mouse) ----------

	[StructLayout(LayoutKind.Sequential)]
	private struct LastInputInfo { public uint Size; public uint Time; }

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetLastInputInfo(ref LastInputInfo info);

	internal static DateTimeOffset? LastInput()
	{
		try
		{
			var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
			if (!GetLastInputInfo(ref info)) return null;
			uint idle = unchecked((uint)Environment.TickCount - info.Time);
			return DateTimeOffset.Now - TimeSpan.FromMilliseconds(idle);
		}
		catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return null; }
	}

	// ---------- sleep (S3 or Modern Standby; never hibernate, never shutdown) ----------

	[DllImport("powrprof.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetSuspendState([MarshalAs(UnmanagedType.Bool)] bool hibernate, [MarshalAs(UnmanagedType.Bool)] bool force, [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

	// Wake events stay enabled, so Wake-on-LAN and the controller can wake the PC.
	internal static bool Sleep()
	{
		try { return SetSuspendState(false, false, false); }
		catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
	}

	// ---------- the running game's name (Steam's appmanifest) ----------

	private static readonly Regex ManifestName = new("\"name\"\\s+\"((?:[^\"\\\\]|\\\\.){1,200})\"", RegexOptions.CultureInvariant);
	private static readonly Regex LibraryPath = new("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.){1,400})\"", RegexOptions.CultureInvariant);

	internal static string? NameFromManifest(string text)
	{
		Match match = ManifestName.Match(text);
		return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
	}

	internal static IReadOnlyList<string> LibraryFolders(string libraryFoldersVdf) =>
		LibraryPath.Matches(libraryFoldersVdf).Select(match => match.Groups[1].Value.Replace("\\\\", "\\")).ToList();

	internal static string? SteamGameName(string? steamRoot, uint appId)
	{
		if (appId == 0 || string.IsNullOrEmpty(steamRoot)) return null;
		try
		{
			var folders = new List<string> { steamRoot };
			string vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
			if (File.Exists(vdf)) folders.AddRange(LibraryFolders(File.ReadAllText(vdf)));
			foreach (string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				string manifest = Path.Combine(folder, "steamapps", $"appmanifest_{appId}.acf");
				if (File.Exists(manifest)) return NameFromManifest(File.ReadAllText(manifest));
			}
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
		return null;
	}

	// ---------- Nuvio / Stremio ----------

	// Windows' media sessions (what the volume flyout shows) first: playing or paused, from an app whose id names
	// Stremio or Nuvio. Then the processes: the app is open but has no session. Matching by name is inferred (the apps'
	// session ids were not seen on this PC); see BATCH-2.md's checklist.
	internal static async Task<(string State, string? App)> MediaAsync()
	{
		try
		{
			var manager = await global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
			string? paused = null;
			foreach (var session in manager.GetSessions())
			{
				string id = session.SourceAppUserModelId ?? "";
				string? app = MediaApp(id);
				if (app is null) continue;
				var status = session.GetPlaybackInfo()?.PlaybackStatus;
				if (status == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return ("playing", app);
				if (status == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) paused ??= app;
			}
			if (paused is not null) return ("paused", paused);
		}
		catch (Exception error) when (error is COMException or InvalidOperationException or UnauthorizedAccessException or TypeLoadException) { }
		foreach (string name in new[] { "stremio", "stremio-shell-ng", "stremio-runtime", "nuvio" })
		{
			Process[] processes = Array.Empty<Process>();
			try { processes = Process.GetProcessesByName(name); if (processes.Length > 0) return ("open", MediaApp(name)); }
			catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
			finally { foreach (Process process in processes) process.Dispose(); }
		}
		return ("off", null);
	}

	internal static string? MediaApp(string id) =>
		id.Contains("stremio", StringComparison.OrdinalIgnoreCase) ? "Stremio" : id.Contains("nuvio", StringComparison.OrdinalIgnoreCase) ? "Nuvio" : null;

	// ---------- Wake-on-LAN readiness ----------

	private const string AdapterClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

	internal static WakeOnLanInputs WakeOnLan()
	{
		bool? fastStartup = null;
		try
		{
			using RegistryKey? power = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
			if (power?.GetValue("HiberbootEnabled") is int hiberboot) fastStartup = hiberboot == 1;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
		IReadOnlyList<string> armed = WakeArmedDevices();
		var adapters = new List<NetworkAdapterWake>();
		try
		{
			foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
			{
				bool wired = nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT;
				bool wireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
				if (!wired && !wireless) continue;
				(bool? magic, bool? powerOff, string? description) = AdapterRegistry(nic.Id);
				// Virtual adapters (Hyper-V, VPNs, VirtualBox) have no class entry with a driver of their own or say so.
				string name = description ?? nic.Description;
				if (Regex.IsMatch(name, "virtual|hyper-v|vpn|tap-|wan miniport|loopback|bluetooth", RegexOptions.IgnoreCase)) continue;
				bool? isArmed = armed.Count == 0 && armedUnknown ? (bool?)null : armed.Any(device => device.Equals(name, StringComparison.OrdinalIgnoreCase));
				string mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(value => value.ToString("X2")));
				adapters.Add(new NetworkAdapterWake(name, wired, nic.OperationalStatus == OperationalStatus.Up, wired ? magic : null, wired ? powerOff : null, wired ? isArmed : null, mac.Length == 17 ? mac : null));
			}
		}
		catch (NetworkInformationException) { return new WakeOnLanInputs(null, fastStartup); }
		return new WakeOnLanInputs(adapters, fastStartup);
	}

	private static bool armedUnknown;

	private static (bool? Magic, bool? PowerOff, string? Description) AdapterRegistry(string interfaceId)
	{
		try
		{
			using RegistryKey? root = Registry.LocalMachine.OpenSubKey(AdapterClass);
			if (root is null) return (null, null, null);
			foreach (string sub in root.GetSubKeyNames())
			{
				if (!Regex.IsMatch(sub, "^[0-9]{4}$")) continue;
				try
				{
					using RegistryKey? key = root.OpenSubKey(sub);
					if (key?.GetValue("NetCfgInstanceId") is not string id || !id.Equals(interfaceId, StringComparison.OrdinalIgnoreCase)) continue;
					return (WakeOnLanPolicy.MagicPacket(key.GetValue("*WakeOnMagicPacket")), WakeOnLanPolicy.PowerManagementOff(key.GetValue("PnPCapabilities")), key.GetValue("DriverDesc") as string);
				}
				catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
			}
		}
		catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
		return (null, null, null);
	}

	// `powercfg /devicequery wake_armed` (no administrator rights needed): the devices allowed to wake the PC.
	private static IReadOnlyList<string> WakeArmedDevices()
	{
		armedUnknown = true;
		try
		{
			var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
			{
				UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
			};
			start.ArgumentList.Add("/devicequery");
			start.ArgumentList.Add("wake_armed");
			using Process? process = Process.Start(start);
			if (process is null) return Array.Empty<string>();
			Task<string> output = process.StandardOutput.ReadToEndAsync();
			if (!process.WaitForExit(4000)) { try { process.Kill(); } catch { } return Array.Empty<string>(); }
			if (process.ExitCode != 0) return Array.Empty<string>();
			armedUnknown = false;
			return WakeOnLanPolicy.WakeArmed(output.Result);
		}
		catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return Array.Empty<string>(); }
	}
}
