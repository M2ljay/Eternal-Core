using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace GamingMode.Services;

// Windows half of Core Voice (rules in CoreVoice.cs): Core Audio capture endpoints, the Game Bar's
// state, and the optional mute hotkey. No controller is read here (DashboardShortcutService.cs
// explains why): the hotkey is a keyboard combination that a Steam Input chord can press.

// ---------- microphones (Core Audio, IAudioEndpointVolume) ----------

internal static class WindowsMicrophone
{
	private const int FlowCapture = 1;
	private const int RoleConsole = 0, RoleCommunications = 2;
	private const uint DeviceStateActive = 0x1;
	private const uint ClassContextAll = 0x17;
	private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
	// Passed with every SetMute, so Core's own changes are recognisable in Windows' notifications.
	internal static readonly Guid CoreEventContext = new("6E2F1C0A-3B7D-4C61-9E58-C0DE0F0CE500");

	// Every active capture endpoint, with its mute and whether it is a Console or Communications default.
	internal static IReadOnlyList<MicEndpoint> Read()
	{
		var result = new List<MicEndpoint>();
		IMMDeviceEnumerator? enumerator = null;
		IMMDeviceCollection? collection = null;
		try
		{
			enumerator = CreateEnumerator();
			var defaults = new HashSet<string>(StringComparer.Ordinal);
			string? communications = null;
			foreach (int role in new[] { RoleConsole, RoleCommunications })
			{
				// E_NOTFOUND when there is no microphone at all.
				if (enumerator.GetDefaultAudioEndpoint(FlowCapture, role, out IMMDevice? device) != 0 || device is null) continue;
				try
				{
					if (device.GetId(out string? id) == 0 && !string.IsNullOrEmpty(id))
					{
						defaults.Add(id);
						if (role == RoleCommunications) communications = id;
					}
				}
				finally { Release(device); }
			}
			Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(FlowCapture, DeviceStateActive, out collection));
			Marshal.ThrowExceptionForHR(collection!.GetCount(out uint count));
			for (uint index = 0; index < count && index < 64; index++)
			{
				if (collection.Item(index, out IMMDevice? device) != 0 || device is null) continue;
				try
				{
					if (device.GetId(out string? id) != 0 || string.IsNullOrEmpty(id)) continue;
					bool? muted = null;
					WithVolume(device, volume => { if (volume.GetMute(out bool value) == 0) muted = value; });
					if (muted is bool known) result.Add(new MicEndpoint(id, known, defaults.Contains(id), string.Equals(id, communications, StringComparison.Ordinal)));
				}
				finally { Release(device); }
			}
		}
		finally { Release(collection); Release(enumerator); }
		return result;
	}

	internal static bool SetMute(string id, bool mute)
	{
		IMMDeviceEnumerator? enumerator = null;
		IMMDevice? device = null;
		try
		{
			enumerator = CreateEnumerator();
			if (enumerator.GetDevice(id, out device) != 0 || device is null) return false;
			bool done = false;
			WithVolume(device, volume =>
			{
				Guid context = CoreEventContext;
				done = volume.SetMute(mute, ref context) >= 0; // S_FALSE: already so
			});
			return done;
		}
		finally { Release(device); Release(enumerator); }
	}

	private static IMMDeviceEnumerator CreateEnumerator() =>
		(IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClsid, throwOnError: true)!)!;

	private static void WithVolume(IMMDevice device, Action<IAudioEndpointVolume> action)
	{
		object? instance = null;
		try
		{
			Guid iid = typeof(IAudioEndpointVolume).GUID;
			if (device.Activate(ref iid, ClassContextAll, 0, out instance) != 0 || instance is null) return;
			action((IAudioEndpointVolume)instance);
		}
		finally { Release(instance); }
	}

	internal static void Release(object? instance)
	{
		if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
	}

	// Raised on a pool thread when a capture default or a capture device's state changes. Windows
	// forbids calling Core Audio from inside the callback, so the callback only queues the event.
	internal sealed class DeviceWatcher : IDisposable
	{
		private readonly IMMDeviceEnumerator _enumerator;
		private readonly Client _client;
		private bool _registered;

		internal DeviceWatcher(Action changed)
		{
			_enumerator = CreateEnumerator();
			_client = new Client(changed);
			Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_client));
			_registered = true;
		}

		public void Dispose()
		{
			if (_registered) { try { _enumerator.UnregisterEndpointNotificationCallback(_client); } catch { } _registered = false; }
			Release(_enumerator);
		}

		private sealed class Client : IMMNotificationClient
		{
			private readonly Action _changed;
			internal Client(Action changed) => _changed = changed;
			private int Raise() { ThreadPool.QueueUserWorkItem(_ => { try { _changed(); } catch { } }); return 0; }
			public int OnDeviceStateChanged(string? deviceId, uint newState) => Raise();
			public int OnDeviceAdded(string? deviceId) => Raise();
			public int OnDeviceRemoved(string? deviceId) => Raise();
			public int OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId) => flow == FlowCapture ? Raise() : 0;
			public int OnPropertyValueChanged(string? deviceId, PropertyKey key) => 0;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct PropertyKey { public Guid FormatId; public int PropertyId; }

	[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDeviceEnumerator
	{
		[PreserveSig] int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection? devices);
		[PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
		[PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
		[PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
		[PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
	}

	[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDeviceCollection
	{
		[PreserveSig] int GetCount(out uint count);
		[PreserveSig] int Item(uint index, out IMMDevice? device);
	}

	[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMDevice
	{
		[PreserveSig] int Activate(ref Guid iid, uint classContext, nint activationParameters, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
		[PreserveSig] int OpenPropertyStore(uint access, out nint properties);
		[PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
		[PreserveSig] int GetState(out uint state);
	}

	[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IMMNotificationClient
	{
		[PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string? deviceId, uint newState);
		[PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);
		[PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string? deviceId);
		[PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
		[PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string? deviceId, PropertyKey key);
	}

	// Full vtable order (IAudioEndpointVolume, endpointvolume.h); only GetMute and SetMute are used.
	[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	internal interface IAudioEndpointVolume
	{
		[PreserveSig] int RegisterControlChangeNotify(nint notify);
		[PreserveSig] int UnregisterControlChangeNotify(nint notify);
		[PreserveSig] int GetChannelCount(out uint channelCount);
		[PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
		[PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
		[PreserveSig] int GetMasterVolumeLevel(out float levelDb);
		[PreserveSig] int GetMasterVolumeLevelScalar(out float level);
		[PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
		[PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
		[PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
		[PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
		[PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
		[PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
	}
}

// ---------- Xbox Game Bar ----------

internal static class WindowsGameBar
{
	private const string PackageFamily = "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe";
	private static (long At, bool? Installed) _installed = (long.MinValue, null);

	// The bar is open and has the input (the same signal WindowsXboxGameBar uses for Xbox games).
	internal static bool? Interactive()
	{
		try { return Windows.Gaming.UI.GameBar.IsInputRedirected; }
		catch { return null; }
	}

	// Cached for ten minutes; null when Windows cannot say.
	internal static bool? Installed()
	{
		long now = Environment.TickCount64;
		if (_installed.At != long.MinValue && now - _installed.At < 600_000) return _installed.Installed;
		bool? installed;
		try { installed = new Windows.Management.Deployment.PackageManager().FindPackagesForUser(string.Empty, PackageFamily).Any(); }
		catch { installed = null; }
		_installed = (now, installed);
		return installed;
	}
}

// ---------- the mute hotkey (RegisterHotKey, like the Dashboard's) ----------

internal sealed class VoiceHotkey : IDisposable
{
	private const int HotkeyId = 0x4356; // "CV"; the Dashboard uses 0x5048
	private const uint ModNoRepeat = 0x4000, WmHotKey = 0x0312, WmQuit = 0x0012, WmUser = 0x0400;
	private readonly Action _pressed;
	private readonly Action<string> _log;
	private readonly object _sync = new();
	private Thread? _thread;
	private uint _threadId;
	private string? _combo;

	internal VoiceHotkey(Action pressed, Action<string> log) { _pressed = pressed; _log = log; }

	internal string? Combo { get { lock (_sync) return _combo; } }

	// Empty or null turns it off. Registering again only when the combination changes.
	internal void Apply(string? combo)
	{
		combo = string.IsNullOrWhiteSpace(combo) ? null : combo.Trim();
		lock (_sync)
		{
			if (string.Equals(combo, _combo, StringComparison.OrdinalIgnoreCase) && (combo is null || _thread is { IsAlive: true })) return;
			StopLocked();
			_combo = combo;
			if (combo is null) return;
			if (!DashboardShortcutService.HotkeyParser.TryParse(combo, out uint modifiers, out uint key))
			{
				_log($"Voice: microphone hotkey \"{combo}\" is not a valid combination; it stays off.");
				return;
			}
			using var ready = new ManualResetEventSlim(false);
			var thread = new Thread(() => Run(combo, modifiers, key, ready)) { IsBackground = true, Name = "Core voice hotkey" };
			thread.Start();
			ready.Wait(2000);
			_thread = thread;
		}
	}

	private void Run(string combo, uint modifiers, uint key, ManualResetEventSlim ready)
	{
		PeekMessage(out _, 0, WmUser, WmUser, 0); // creates this thread's message queue
		_threadId = GetCurrentThreadId();
		bool registered = RegisterHotKey(0, HotkeyId, modifiers | ModNoRepeat, key);
		ready.Set();
		_log(registered ? $"Voice: microphone hotkey {combo} armed." : $"Voice: microphone hotkey {combo} could not be registered (another program uses it); it stays off.");
		if (!registered) return;
		try
		{
			while (GetMessage(out Message message, 0, 0, 0) > 0)
				if (message.Value == WmHotKey && message.WParam == HotkeyId) ThreadPool.QueueUserWorkItem(_ => { try { _pressed(); } catch { } });
		}
		finally { UnregisterHotKey(0, HotkeyId); }
	}

	private void StopLocked()
	{
		Thread? thread = _thread;
		_thread = null;
		if (thread is null || !thread.IsAlive) return;
		PostThreadMessage(_threadId, WmQuit, 0, 0);
		thread.Join(1000);
	}

	public void Dispose() { lock (_sync) StopLocked(); }

	[StructLayout(LayoutKind.Sequential)]
	private struct Message { public nint Window; public uint Value; public nint WParam; public nint LParam; public uint Time; public int X; public int Y; }

	[DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);
	[DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
	[DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint filterMin, uint filterMax);
	[DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, nint window, uint filterMin, uint filterMax, uint remove);
	[DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);
	[DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
