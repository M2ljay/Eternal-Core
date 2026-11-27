using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GamingMode.Services;

// Windows half of Stream mode (rules in CoreStreamMode.cs): what the agent can see of a stream by itself
// (Sunshine/Apollo through their log, SteamVR through its server process), and the playback endpoints
// (Core Audio) for keeping the PC's sound off the TV. Steam Remote Play is reported by the Decky plugin
// (SteamClient.RemotePlay), which runs inside Steam; the agent has no other view of it.

internal static class StreamProbes
{
	// Sunshine's documented default config folder is %ProgramFiles%\Sunshine\config (log_path defaults to
	// sunshine.log there). Apollo is a Sunshine fork and installs beside it as %ProgramFiles%\Apollo with
	// the same layout (inferred from its installer; set HostLog in stream-settings.json if yours differs).
	internal static string SunshineFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sunshine");
	internal static string ApolloFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Apollo");

	internal static IEnumerable<(StreamSource Source, string Path)> HostLogs(string? configured)
	{
		if (!string.IsNullOrWhiteSpace(configured))
		{
			string path = configured.Trim();
			yield return (path.Contains("apollo", StringComparison.OrdinalIgnoreCase) ? StreamSource.Apollo : StreamSource.Sunshine, path);
			yield break;
		}
		// Vibepollo (an Apollo fork, installed into Apollo's folder) writes one log per run under config\logs (batch 2,
		// VibepolloLogs in StreamPower.cs); the newest is the current run.
		if (NewestVibepolloLog() is { } vibepollo) yield return (StreamSource.Apollo, vibepollo);
		yield return (StreamSource.Apollo, Path.Combine(ApolloFolder, "config", "sunshine.log"));
		yield return (StreamSource.Sunshine, Path.Combine(SunshineFolder, "config", "sunshine.log"));
	}

	internal static string? NewestVibepolloLog()
	{
		try
		{
			string folder = Path.Combine(ApolloFolder, "config", "logs");
			if (!Directory.Exists(folder)) return null;
			return VibepolloLogs.Newest(new DirectoryInfo(folder).EnumerateFiles("*.log").Take(200).Select(file => (file.FullName, file.LastWriteTimeUtc)));
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
	}

	// Both Sunshine and Apollo run as sunshine.exe (Sunshine's service starts it in the user's session).
	// A log saying "connected" means nothing once the process is gone (a crash writes no disconnect).
	internal static bool HostRunning() => AnyProcess("sunshine") || AnyProcess("apollo");

	// SteamVR's server runs for the whole VR session, wired or wireless (a Steam Frame over its USB
	// adapter connects through SteamVR, per Valve's Steam Frame setup page).
	internal static bool SteamVrRunning() => AnyProcess("vrserver");

	private static bool AnyProcess(string name)
	{
		Process[] processes = Array.Empty<Process>();
		try { processes = Process.GetProcessesByName(name); return processes.Length > 0; }
		catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
		finally { foreach (Process process in processes) process.Dispose(); }
	}
}

// ---------- playback endpoints (Core Audio), for StreamAudioPolicy ----------

internal static class WindowsSpeakers
{
	private const int FlowRender = 0;
	private const int RoleConsole = 0;
	private const uint DeviceStateActive = 0x1;
	private const uint ClassContextAll = 0x17;
	private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
	private static readonly WindowsMicrophone.PropertyKey FriendlyNameKey = new() { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
	// PKEY_AudioEndpoint_FormFactor (mmdeviceapi.h), a VT_UI4 EndpointFormFactor.
	private static readonly WindowsMicrophone.PropertyKey FormFactorKey = new() { FormatId = new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), PropertyId = 0 };
	private const ushort VtLpwstr = 31, VtUi4 = 19;

	internal static IReadOnlyList<SpeakerEndpoint> Read()
	{
		var result = new List<SpeakerEndpoint>();
		WindowsMicrophone.IMMDeviceEnumerator? enumerator = null;
		WindowsMicrophone.IMMDeviceCollection? collection = null;
		try
		{
			enumerator = CreateEnumerator();
			string? console = DefaultId(enumerator);
			Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(FlowRender, DeviceStateActive, out collection));
			Marshal.ThrowExceptionForHR(collection!.GetCount(out uint count));
			for (uint index = 0; index < count && index < 64; index++)
			{
				if (collection.Item(index, out WindowsMicrophone.IMMDevice? device) != 0 || device is null) continue;
				try
				{
					if (device.GetId(out string? id) != 0 || string.IsNullOrEmpty(id)) continue;
					bool? muted = null;
					WithVolume(device, volume => { if (volume.GetMute(out bool value) == 0) muted = value; });
					if (muted is not bool known) continue;
					(string? name, int formFactor) = Properties(device);
					result.Add(new SpeakerEndpoint(id, name, formFactor, string.Equals(id, console, StringComparison.Ordinal), known));
				}
				finally { WindowsMicrophone.Release(device); }
			}
		}
		finally { WindowsMicrophone.Release(collection); WindowsMicrophone.Release(enumerator); }
		return result;
	}

	internal static string? DefaultId()
	{
		WindowsMicrophone.IMMDeviceEnumerator? enumerator = null;
		try { enumerator = CreateEnumerator(); return DefaultId(enumerator); }
		finally { WindowsMicrophone.Release(enumerator); }
	}

	private static string? DefaultId(WindowsMicrophone.IMMDeviceEnumerator enumerator)
	{
		if (enumerator.GetDefaultAudioEndpoint(FlowRender, RoleConsole, out WindowsMicrophone.IMMDevice? device) != 0 || device is null) return null;
		try { return device.GetId(out string? id) == 0 && !string.IsNullOrEmpty(id) ? id : null; }
		finally { WindowsMicrophone.Release(device); }
	}

	// Mute or unmute by endpoint id, with Core's event context (the same call the microphone mute uses;
	// IAudioEndpointVolume does not care about the endpoint's direction).
	internal static bool SetMute(string id, bool mute) => WindowsMicrophone.SetMute(id, mute);

	private static (string? Name, int FormFactor) Properties(WindowsMicrophone.IMMDevice device)
	{
		WindowsMicrophone.IPropertyStore? store = null;
		try
		{
			if (device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0 || store is null) return (null, -1);
			return (ReadString(store, FriendlyNameKey), ReadUInt(store, FormFactorKey));
		}
		catch { return (null, -1); }
		finally { WindowsMicrophone.Release(store); }
	}

	private static string? ReadString(WindowsMicrophone.IPropertyStore store, WindowsMicrophone.PropertyKey key)
	{
		var value = new WindowsMicrophone.PropVariant();
		try
		{
			if (store.GetValue(ref key, out value) != 0 || value.Type != VtLpwstr || value.Pointer == 0) return null;
			string? text = Marshal.PtrToStringUni(value.Pointer);
			return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
		}
		finally { try { PropVariantClear(ref value); } catch { } }
	}

	// VT_UI4 sits in the first four bytes of the PROPVARIANT's value union.
	private static int ReadUInt(WindowsMicrophone.IPropertyStore store, WindowsMicrophone.PropertyKey key)
	{
		var value = new WindowsMicrophone.PropVariant();
		try
		{
			if (store.GetValue(ref key, out value) != 0 || value.Type != VtUi4) return -1;
			return unchecked((int)(uint)((long)value.Pointer & 0xFFFFFFFF));
		}
		finally { try { PropVariantClear(ref value); } catch { } }
	}

	[DllImport("ole32.dll")] private static extern int PropVariantClear(ref WindowsMicrophone.PropVariant value);

	private static WindowsMicrophone.IMMDeviceEnumerator CreateEnumerator() =>
		(WindowsMicrophone.IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClsid, throwOnError: true)!)!;

	private static void WithVolume(WindowsMicrophone.IMMDevice device, Action<WindowsMicrophone.IAudioEndpointVolume> action)
	{
		object? instance = null;
		try
		{
			Guid iid = typeof(WindowsMicrophone.IAudioEndpointVolume).GUID;
			if (device.Activate(ref iid, ClassContextAll, 0, out instance) != 0 || instance is null) return;
			action((WindowsMicrophone.IAudioEndpointVolume)instance);
		}
		finally { WindowsMicrophone.Release(instance); }
	}
}

// Moves default playback (Console and Multimedia, as Windows' Sound panel does for "Set as default
// device") through the same undocumented IPolicyConfig as PolicyConfigDefaultMic (see the reasons and
// risks there). The result is checked against what Windows reports afterwards; every failure is a false.
internal static class PolicyConfigDefaultSpeaker
{
	private static readonly Guid PolicyConfigClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
	private const int RoleConsole = 0, RoleMultimedia = 1;

	internal static bool SetDefault(string id)
	{
		object? instance = null;
		try
		{
			Type? type = Type.GetTypeFromCLSID(PolicyConfigClsid, throwOnError: false);
			if (type is null) return false;
			instance = Activator.CreateInstance(type);
			if (instance is not IPolicyConfig policy) return false;
			foreach (int role in new[] { RoleConsole, RoleMultimedia })
				if (policy.SetDefaultEndpoint(id, role) < 0) return false;
			return string.Equals(WindowsSpeakers.DefaultId(), id, StringComparison.Ordinal);
		}
		catch (Exception error) when (error is COMException or InvalidCastException or UnauthorizedAccessException or TypeLoadException or NotSupportedException)
		{
			return false;
		}
		finally { WindowsMicrophone.Release(instance); }
	}

	[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPolicyConfig
	{
		[PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out nint format);
		[PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, out nint format);
		[PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
		[PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, nint endpointFormat, nint mixFormat);
		[PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, out long defaultPeriod, out long minimumPeriod);
		[PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
		[PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);
		[PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);
		[PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, nint key, nint value);
		[PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int fxStore, nint key, nint value);
		[PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
		[PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
	}
}
