using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Playhub.Shared;

namespace GamingMode.Services;

// Windows half of Jarvis (rules in Jarvis.cs, wiring in MarkIIIHost.Jarvis.cs, docs/eternal/JARVIS.md):
// - JarvisRecorder: push-to-talk capture from one microphone (WASAPI shared mode), in memory only.
// - JarvisScreenGrab: one frame of the screen the game is on (Windows Graphics Capture, a GDI copy as fallback), scaled
//   and JPEG-encoded in memory only.
// - JarvisSpeaker: plays Piper's audio or Windows' own en-GB voice.
// - JarvisTyper: types dictated text with SendInput (Unicode) when Steam's own keyboard path is not available.
// - JarvisIndicator: the small crimson "listening" / "screenshot taken" mark over the screen.
//
// Anti-cheat: nothing here touches a game's process. The screenshot comes from Windows' compositor (the same API the
// Game Bar, OBS and Discord use), the microphone from the audio engine, the mark is Core's own window that takes no
// focus or input. SendInput is the one synthetic-input path, and it is refused while anti-cheat safe mode is engaged.

// ---------- microphone capture (WASAPI) ----------

internal sealed class JarvisRecorder : IDisposable
{
	private static readonly Guid EnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
	private const uint ClassContextAll = 0x17;
	private const int SharedMode = 0;
	private const uint BufferSilent = 0x2;
	private readonly MemoryStream _audio = new();
	private readonly CancellationTokenSource _stop = new();
	private Thread? _thread;
	private int _rate, _channels, _bits;
	private bool _float;
	private volatile string? _failure;

	internal string? Failure => _failure;
	internal DateTimeOffset StartedAt { get; private set; }
	// Tap to talk (3.3.5): end the recording on a pause (JarvisListenLevels.Next). Ended says it stopped by itself.
	internal bool AutoStop { get; init; }
	internal volatile bool Ended;
	// One input level per ~50 ms window of exactly what is being recorded (the listening pill's bars).
	internal Action<float>? OnLevel { get; init; }
	private double _windowSum;
	private int _windowCount;
	private JarvisListenLevels.SilenceState _silence = new(false, TimeSpan.Zero, TimeSpan.Zero);

	// Opens the endpoint and starts capturing on its own thread; null when Windows refuses (the reason in failure).
	internal static JarvisRecorder? Start(string endpointId, out string? failure, bool autoStop = false, Action<float>? onLevel = null)
	{
		var recorder = new JarvisRecorder { AutoStop = autoStop, OnLevel = onLevel };
		var ready = new ManualResetEventSlim(false);
		recorder._thread = new Thread(() => recorder.Run(endpointId, ready)) { IsBackground = true, Name = "Core Jarvis microphone" };
		recorder._thread.SetApartmentState(ApartmentState.MTA);
		recorder.StartedAt = DateTimeOffset.Now;
		recorder._thread.Start();
		ready.Wait(TimeSpan.FromSeconds(3));
		failure = recorder._failure;
		if (failure is not null || !ready.IsSet) { recorder.Dispose(); failure ??= "timeout"; return null; }
		return recorder;
	}

	// Stops and returns 16 kHz mono 16-bit samples. The capture buffer is cleared.
	internal short[] Stop()
	{
		_stop.Cancel();
		_thread?.Join(TimeSpan.FromSeconds(2));
		byte[] raw;
		lock (_audio) { raw = _audio.ToArray(); _audio.SetLength(0); }
		short[] samples = JarvisAudio.ToMono16k(raw, _rate, _channels, _bits, _float);
		Array.Clear(raw);
		return samples;
	}

	public void Dispose()
	{
		try { _stop.Cancel(); } catch (ObjectDisposedException) { }
		_thread?.Join(TimeSpan.FromSeconds(2));
		lock (_audio) _audio.SetLength(0);
	}

	private void Run(string endpointId, ManualResetEventSlim ready)
	{
		WindowsMicrophone.IMMDeviceEnumerator? enumerator = null;
		WindowsMicrophone.IMMDevice? device = null;
		object? clientObject = null, captureObject = null;
		nint format = 0;
		try
		{
			enumerator = (WindowsMicrophone.IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClsid, throwOnError: true)!)!;
			if (enumerator.GetDevice(endpointId, out device) != 0 || device is null) { _failure = "no-mic"; return; }
			Guid clientIid = typeof(IAudioClient).GUID;
			if (device.Activate(ref clientIid, ClassContextAll, 0, out clientObject) != 0 || clientObject is null) { _failure = "refused"; return; }
			var client = (IAudioClient)clientObject;
			if (client.GetMixFormat(out format) != 0 || format == 0) { _failure = "refused"; return; }
			ReadFormat(format);
			if (client.Initialize(SharedMode, 0, 2_000_000, 0, format, 0) != 0) { _failure = "refused"; return; }
			Guid captureIid = typeof(IAudioCaptureClient).GUID;
			if (client.GetService(ref captureIid, out captureObject) != 0 || captureObject is null) { _failure = "refused"; return; }
			var capture = (IAudioCaptureClient)captureObject;
			if (client.Start() != 0) { _failure = "refused"; return; }
			ready.Set();
			int blockAlign = _channels * _bits / 8;
			var deadline = DateTimeOffset.Now + JarvisAudio.MaximumRecording;
			while (!_stop.IsCancellationRequested && DateTimeOffset.Now < deadline)
			{
				Thread.Sleep(10);
				while (capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
				{
					if (capture.GetBuffer(out nint data, out uint frames, out uint flags, out _, out _) != 0) break;
					int length = (int)frames * blockAlign;
					var chunk = new byte[length];
					if ((flags & BufferSilent) == 0 && data != 0) Marshal.Copy(data, chunk, 0, length);
					lock (_audio) _audio.Write(chunk, 0, length);
					capture.ReleaseBuffer(frames);
					if (Measure(chunk)) { Ended = true; break; }
				}
				if (Ended) break;
			}
			client.Stop();
		}
		catch (Exception error) when (error is COMException or InvalidCastException or ArgumentException or TypeLoadException) { _failure ??= "refused"; }
		finally
		{
			if (format != 0) Marshal.FreeCoTaskMem(format);
			WindowsMicrophone.Release(captureObject);
			WindowsMicrophone.Release(clientObject);
			WindowsMicrophone.Release(device);
			WindowsMicrophone.Release(enumerator);
			ready.Set();
		}
	}

	// Levels per ~50 ms window of the captured samples (all channels), and the end of speech when AutoStop is on.
	// Returns true when tap to talk should end the recording.
	private bool Measure(byte[] chunk)
	{
		int bytes = _bits / 8;
		if (bytes is not (2 or 4) || _rate <= 0 || _channels <= 0) return false;
		int perWindow = Math.Max(1, _rate * _channels * JarvisListenLevels.WindowMs / 1000);
		bool end = false;
		for (int offset = 0; offset + bytes <= chunk.Length; offset += bytes)
		{
			double sample = _float && bytes == 4 ? BitConverter.ToSingle(chunk, offset) : bytes == 2 ? BitConverter.ToInt16(chunk, offset) / 32768.0 : BitConverter.ToInt32(chunk, offset) / 2147483648.0;
			_windowSum += sample * sample;
			if (++_windowCount < perWindow) continue;
			float level = (float)Math.Min(1, Math.Sqrt(_windowSum / _windowCount));
			_windowSum = 0;
			_windowCount = 0;
			try { OnLevel?.Invoke(level); } catch { }
			if (!AutoStop) continue;
			(_silence, bool stop) = JarvisListenLevels.Next(_silence, level, TimeSpan.FromMilliseconds(JarvisListenLevels.WindowMs));
			end |= stop;
		}
		return end;
	}

	// WAVEFORMATEX, or WAVEFORMATEXTENSIBLE (tag 0xFFFE) whose SubFormat says PCM or IEEE float.
	private void ReadFormat(nint format)
	{
		ushort tag = (ushort)Marshal.ReadInt16(format, 0);
		_channels = Marshal.ReadInt16(format, 2);
		_rate = Marshal.ReadInt32(format, 4);
		_bits = Marshal.ReadInt16(format, 14);
		_float = tag == 3;
		if (tag == 0xFFFE)
		{
			var subFormat = new byte[16];
			Marshal.Copy(format + 24, subFormat, 0, 16);
			_float = new Guid(subFormat) == new Guid("00000003-0000-0010-8000-00AA00389B71");
		}
	}

	[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioClient
	{
		[PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, nint format, nint sessionGuid);
		[PreserveSig] int GetBufferSize(out uint frames);
		[PreserveSig] int GetStreamLatency(out long latency);
		[PreserveSig] int GetCurrentPadding(out uint padding);
		[PreserveSig] int IsFormatSupported(int shareMode, nint format, out nint closest);
		[PreserveSig] int GetMixFormat(out nint format);
		[PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
		[PreserveSig] int Start();
		[PreserveSig] int Stop();
		[PreserveSig] int Reset();
		[PreserveSig] int SetEventHandle(nint handle);
		[PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object? service);
	}

	[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IAudioCaptureClient
	{
		[PreserveSig] int GetBuffer(out nint data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
		[PreserveSig] int ReleaseBuffer(uint frames);
		[PreserveSig] int GetNextPacketSize(out uint frames);
	}
}

// ---------- screenshot (Windows Graphics Capture) ----------

internal static class JarvisScreenGrab
{
	// The monitor the foreground window (the game, or Big Picture) is on, as a JPEG no larger than 1280 px on its long
	// edge. Null when Windows gives no picture. The pixels live only in memory and are dropped once encoded.
	internal static async Task<(byte[]? Jpeg, string Method)> CaptureAsync(TimeSpan timeout)
	{
		nint monitor = MonitorFromWindow(GetForegroundWindow(), 1 /* MONITOR_DEFAULTTOPRIMARY */);
		try
		{
			byte[]? jpeg = await GraphicsCaptureAsync(monitor, timeout).ConfigureAwait(false);
			if (jpeg is { Length: > 0 }) return (jpeg, "graphics-capture");
		}
		catch (Exception error) when (error is COMException or InvalidCastException or NotSupportedException or TypeLoadException or UnauthorizedAccessException or InvalidOperationException) { }
		try { return (await GdiCaptureAsync(monitor).ConfigureAwait(false), "gdi"); }
		catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException or OutOfMemoryException) { return (null, "none"); }
	}

	private static async Task<byte[]?> GraphicsCaptureAsync(nint monitor, TimeSpan timeout)
	{
		if (!global::Windows.Graphics.Capture.GraphicsCaptureSession.IsSupported()) return null;
		global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? device = CreateDirect3DDevice();
		if (device is null) return null;
		var interop = global::Windows.Graphics.Capture.GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
		Guid itemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
		nint itemPointer = interop.CreateForMonitor(monitor, ref itemIid);
		if (itemPointer == 0) return null;
		global::Windows.Graphics.Capture.GraphicsCaptureItem item;
		try { item = global::Windows.Graphics.Capture.GraphicsCaptureItem.FromAbi(itemPointer); }
		finally { Marshal.Release(itemPointer); }
		using var pool = global::Windows.Graphics.Capture.Direct3D11CaptureFramePool.CreateFreeThreaded(device,
			global::Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, item.Size);
		using var session = pool.CreateCaptureSession(item);
		try { session.IsCursorCaptureEnabled = false; } catch (Exception error) when (error is COMException or InvalidCastException) { }
		var done = new TaskCompletionSource<global::Windows.Graphics.Imaging.SoftwareBitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
		pool.FrameArrived += async (sender, _) =>
		{
			if (done.Task.IsCompleted) return;
			try
			{
				using var frame = sender.TryGetNextFrame();
				if (frame is null) return;
				done.TrySetResult(await global::Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, global::Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied));
			}
			catch (Exception error) { done.TrySetException(error); }
		};
		session.StartCapture();
		Task finished = await Task.WhenAny(done.Task, Task.Delay(timeout)).ConfigureAwait(false);
		if (finished != done.Task) return null;
		using global::Windows.Graphics.Imaging.SoftwareBitmap? bitmap = await done.Task.ConfigureAwait(false);
		return bitmap is null ? null : await EncodeAsync(bitmap).ConfigureAwait(false);
	}

	private static async Task<byte[]> EncodeAsync(global::Windows.Graphics.Imaging.SoftwareBitmap bitmap)
	{
		using var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
		var quality = new global::Windows.Graphics.Imaging.BitmapPropertySet
		{
			{ "ImageQuality", new global::Windows.Graphics.Imaging.BitmapTypedValue((float)(JarvisScreenSize.JpegQuality / 100.0), global::Windows.Foundation.PropertyType.Single) },
		};
		var encoder = await global::Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(global::Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId, stream, quality);
		using var converted = global::Windows.Graphics.Imaging.SoftwareBitmap.Convert(bitmap, global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, global::Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
		encoder.SetSoftwareBitmap(converted);
		(int width, int height) = JarvisScreenSize.Fit(bitmap.PixelWidth, bitmap.PixelHeight);
		encoder.BitmapTransform.ScaledWidth = (uint)width;
		encoder.BitmapTransform.ScaledHeight = (uint)height;
		encoder.BitmapTransform.InterpolationMode = global::Windows.Graphics.Imaging.BitmapInterpolationMode.Fant;
		await encoder.FlushAsync();
		return await ReadAllAsync(stream).ConfigureAwait(false);
	}

	internal static async Task<byte[]> ReadAllAsync(global::Windows.Storage.Streams.IRandomAccessStream stream)
	{
		using var reader = new global::Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
		uint size = (uint)stream.Size;
		await reader.LoadAsync(size);
		var bytes = new byte[size];
		reader.ReadBytes(bytes);
		return bytes;
	}

	private static global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice? CreateDirect3DDevice()
	{
		nint d3d = 0, context = 0, dxgi = 0, inspectable = 0;
		try
		{
			// D3D_DRIVER_TYPE_HARDWARE, D3D11_CREATE_DEVICE_BGRA_SUPPORT, D3D11_SDK_VERSION.
			if (D3D11CreateDevice(0, 1, 0, 0x20, 0, 0, 7, out d3d, out _, out context) < 0 || d3d == 0) return null;
			Guid dxgiIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
			if (Marshal.QueryInterface(d3d, ref dxgiIid, out dxgi) < 0 || dxgi == 0) return null;
			if (CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable) < 0 || inspectable == 0) return null;
			return WinRT.MarshalInterface<global::Windows.Graphics.DirectX.Direct3D11.IDirect3DDevice>.FromAbi(inspectable);
		}
		finally
		{
			if (inspectable != 0) Marshal.Release(inspectable);
			if (dxgi != 0) Marshal.Release(dxgi);
			if (context != 0) Marshal.Release(context);
			if (d3d != 0) Marshal.Release(d3d);
		}
	}

	// GDI copy of the monitor: works for Big Picture and windowed games; full-screen DirectX games may come out black.
	private static async Task<byte[]?> GdiCaptureAsync(nint monitor)
	{
		var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
		if (!GetMonitorInfo(monitor, ref info)) return null;
		int width = info.Right - info.Left, height = info.Bottom - info.Top;
		if (width <= 0 || height <= 0) return null;
		nint screen = GetDC(0), memory = 0, bitmapHandle = 0, old = 0;
		byte[] pixels = new byte[width * height * 4];
		try
		{
			memory = CreateCompatibleDC(screen);
			bitmapHandle = CreateCompatibleBitmap(screen, width, height);
			old = SelectObject(memory, bitmapHandle);
			if (!BitBlt(memory, 0, 0, width, height, screen, info.Left, info.Top, 0x00CC0020 /* SRCCOPY */)) return null;
			var header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
			if (GetDIBits(memory, bitmapHandle, 0, (uint)height, pixels, ref header, 0) == 0) return null;
		}
		finally
		{
			if (old != 0) SelectObject(memory, old);
			if (bitmapHandle != 0) DeleteObject(bitmapHandle);
			if (memory != 0) DeleteDC(memory);
			ReleaseDC(0, screen);
		}
		using var bitmap = new global::Windows.Graphics.Imaging.SoftwareBitmap(global::Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, width, height, global::Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);
		using (var writer = new global::Windows.Storage.Streams.DataWriter())
		{
			writer.WriteBytes(pixels);
			bitmap.CopyFromBuffer(writer.DetachBuffer());
		}
		Array.Clear(pixels);
		return await EncodeAsync(bitmap).ConfigureAwait(false);
	}

	[ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IGraphicsCaptureItemInterop
	{
		nint CreateForWindow([In] nint window, [In] ref Guid iid);
		nint CreateForMonitor([In] nint monitor, [In] ref Guid iid);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MonitorInfo { public int Size; public int Left, Top, Right, Bottom; public int WorkLeft, WorkTop, WorkRight, WorkBottom; public uint Flags; }

	[StructLayout(LayoutKind.Sequential)]
	private struct BitmapInfoHeader { public int Size; public int Width; public int Height; public short Planes; public short BitCount; public int Compression; public int SizeImage; public int XPelsPerMeter; public int YPelsPerMeter; public int ClrUsed; public int ClrImportant; }

	[DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels, uint levels, uint sdkVersion, out nint device, out int featureLevel, out nint context);
	[DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);
	[DllImport("user32.dll")] private static extern nint GetForegroundWindow();
	[DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
	[DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
	[DllImport("user32.dll")] private static extern nint GetDC(nint window);
	[DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
	[DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
	[DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
	[DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
	[DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
	[DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
	[DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
	[DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}

// ---------- speaking ----------

internal static class JarvisSpeaker
{
	private static global::Windows.Media.Playback.MediaPlayer? _player;
	private static readonly object Sync = new();

	// Plays WAV or MP3 bytes; a new answer interrupts the previous one. Returns when playback ends (at most two minutes).
	internal static async Task<bool> PlayAsync(byte[] audio, string contentType)
	{
		var stream = new global::Windows.Storage.Streams.InMemoryRandomAccessStream();
		using (var writer = new global::Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
		{
			writer.WriteBytes(audio);
			await writer.StoreAsync();
			await writer.FlushAsync();
			writer.DetachStream();
		}
		stream.Seek(0);
		return await PlayStreamAsync(stream, contentType).ConfigureAwait(false);
	}

	// Windows' own voice, British English when installed (Microsoft George / Hazel / Susan / Ryan / Sonia), else the default.
	internal static async Task<bool> SpeakWindowsAsync(string text)
	{
		using var synthesizer = new global::Windows.Media.SpeechSynthesis.SpeechSynthesizer();
		foreach (var voice in global::Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices)
		{
			if (!string.Equals(voice.Language, "en-GB", StringComparison.OrdinalIgnoreCase)) continue;
			synthesizer.Voice = voice;
			if (voice.Gender == global::Windows.Media.SpeechSynthesis.VoiceGender.Male) break;
		}
		var speech = await synthesizer.SynthesizeTextToStreamAsync(text);
		return await PlayStreamAsync(speech, speech.ContentType).ConfigureAwait(false);
	}

	internal static void Stop()
	{
		lock (Sync) { try { _player?.Pause(); } catch (COMException) { } }
	}

	private static async Task<bool> PlayStreamAsync(global::Windows.Storage.Streams.IRandomAccessStream stream, string contentType)
	{
		var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var player = new global::Windows.Media.Playback.MediaPlayer { AutoPlay = false };
		player.MediaEnded += (_, _) => ended.TrySetResult(true);
		player.MediaFailed += (_, _) => ended.TrySetResult(false);
		player.Source = global::Windows.Media.Core.MediaSource.CreateFromStream(stream, contentType);
		lock (Sync)
		{
			try { _player?.Pause(); _player?.Dispose(); } catch (COMException) { }
			_player = player;
		}
		player.Play();
		bool ok = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromMinutes(2))).ConfigureAwait(false) == ended.Task && ended.Task.Result;
		lock (Sync)
		{
			if (ReferenceEquals(_player, player)) { _player = null; try { player.Dispose(); } catch (COMException) { } }
		}
		stream.Dispose();
		return ok;
	}
}

// ---------- typing (SendInput) ----------

internal static class JarvisTyper
{
	private const uint InputKeyboard = 1, KeyUnicode = 0x0004, KeyUp = 0x0002;

	// Types the text into whatever has the keyboard focus, as Unicode key presses. Returns false if Windows took none.
	internal static bool Type(string text)
	{
		if (string.IsNullOrEmpty(text)) return true;
		var inputs = new List<Input>(text.Length * 2);
		foreach (char character in text)
		{
			if (char.IsControl(character) && character != '\n') continue;
			ushort code = character == '\n' ? (ushort)' ' : character;
			inputs.Add(Key(code, KeyUnicode));
			inputs.Add(Key(code, KeyUnicode | KeyUp));
		}
		if (inputs.Count == 0) return true;
		uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
		return sent == inputs.Count;
	}

	private static Input Key(ushort scan, uint flags) => new() { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = scan, Flags = flags } } };

	[StructLayout(LayoutKind.Sequential)]
	private struct Input { public uint Type; public InputUnion Data; }

	// The union is as large as MOUSEINPUT, the biggest member, so the struct matches Windows' INPUT on x86 and x64.
	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; [FieldOffset(0)] public MouseInput Mouse; }

	[StructLayout(LayoutKind.Sequential)]
	private struct KeyboardInput { public ushort VirtualKey; public ushort Scan; public uint Flags; public uint Time; public nint ExtraInfo; }

	[StructLayout(LayoutKind.Sequential)]
	private struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public nint ExtraInfo; }

	[DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
}

// ---------- the indicator ----------

// Top right, 32 px from the top and 40 px from the right edge (at 1920x1080, scaled): a glass pill (black at 72%,
// a 1 px #1A1D21 border, 10 px corners, 52 px high, 19 px semibold text) that takes no focus or input (no activation,
// click-through, tool window) and never pauses or minimises anything (board 8/9 of "Core: current vision").
// - Listening: nine crimson bars (4 px wide, 3 px apart, 2 px corners) whose heights follow the input level of the
//   recording itself (JarvisRecorder.OnLevel, ~50 ms windows, smoothed, 6 to 30 px), and "Listening". It shows from
//   the first sample to the moment recording stops (a pause, a second tap, the 30 s cap, a mute) and at no other time.
//   Height and opacity only, no glow; with Windows' animations off the bars stand still at mid height.
// - Thinking: a 12 px outlined steel dot and "Thinking..." until the answer starts, then it goes.
// - Answer: a 480-560 px card in the same spot: the orb, a small crimson "JARVIS", the short answer for a few seconds
//   while he speaks, and the hint to hold L5 + R5 for the full answer.
// - "Screenshot taken for Jarvis" for two seconds after every screenshot.
// WPF layered windows cannot blur what is behind them, so the glass is a plain 72% black (the 16 px backdrop blur of
// the design is not drawn). A game in exclusive fullscreen can hide any topmost window; the agent then tells the
// plugin, which shows the orb and a toast instead (MarkIIIHost.Jarvis.cs, JarvisIndicator.ExclusiveFullscreen).
internal static class JarvisIndicator
{
	private enum Mode { Hidden, Listening, Thinking, Answer, Flash }
	private static System.Windows.Threading.Dispatcher? _dispatcher;
	private static System.Windows.Window? _window;
	private static System.Windows.Controls.Border? _pill;
	private static System.Windows.Controls.TextBlock? _label, _answer, _hint, _brand;
	private static System.Windows.Controls.StackPanel? _bars, _answerPanel;
	private static System.Windows.Shapes.Ellipse? _dot, _orb;
	private static System.Windows.Threading.DispatcherTimer? _hide;
	private static Mode _mode = Mode.Hidden;
	private static readonly List<float> Levels = new();
	private static double[]? _heights;
	private static readonly object Sync = new();
	private static double Scale => Math.Clamp(System.Windows.SystemParameters.PrimaryScreenHeight / 1080.0, 0.75, 2.0);
	private static readonly System.Windows.Media.Color Crimson = System.Windows.Media.Color.FromRgb(0xD7, 0x26, 0x3D), Steel = System.Windows.Media.Color.FromRgb(0x9A, 0xA4, 0xAE);

	// Recording started (on) or stopped (off). Stopping leaves the pill to Thinking or hides it.
	internal static void Listening(bool on, string text, FileLogger logger)
	{
		lock (Levels) { Levels.Clear(); _heights = null; }
		Post(() =>
		{
			if (on) Show(Mode.Listening, text, null);
			else if (_mode == Mode.Listening) Hide();
		}, logger);
	}

	internal static void Level(float level, FileLogger logger)
	{
		lock (Levels) { Levels.Add(level); if (Levels.Count > 32) Levels.RemoveAt(0); }
		Post(UpdateBars, logger);
	}

	internal static void Thinking(string text, FileLogger logger) => Post(() => Show(Mode.Thinking, text, null), logger);

	internal static void Done(FileLogger logger) => Post(() => { if (_mode == Mode.Thinking) Hide(); }, logger);

	internal static void Answer(string brand, string text, string hint, FileLogger logger) => Post(() =>
	{
		if (_brand is not null) _brand.Text = brand;
		if (_answer is not null) _answer.Text = text.Length > 180 ? text[..179] + "\u2026" : text;
		if (_hint is not null) _hint.Text = hint;
		Show(Mode.Answer, "", TimeSpan.FromSeconds(Math.Clamp(text.Length / 15.0, 4, 9)));
	}, logger);

	internal static void Flash(string text, FileLogger logger) => Post(() => Show(Mode.Flash, text, TimeSpan.FromSeconds(2)), logger);

	// Windows reports a Direct3D exclusive-fullscreen app (SHQueryUserNotificationState: QUNS_RUNNING_D3D_FULL_SCREEN),
	// over which a topmost window may not show.
	internal static bool ExclusiveFullscreen()
	{
		try { return SHQueryUserNotificationState(out int state) == 0 && state == 3; }
		catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
	}

	private static void Show(Mode mode, string text, TimeSpan? duration)
	{
		if (_window is null || _label is null || _bars is null || _dot is null || _answerPanel is null || _pill is null) return;
		_mode = mode;
		bool listening = mode == Mode.Listening, thinking = mode == Mode.Thinking, answer = mode == Mode.Answer;
		_label.Text = text;
		_label.Foreground = new System.Windows.Media.SolidColorBrush(thinking ? Steel : System.Windows.Media.Color.FromRgb(0xF4, 0xF7, 0xFA));
		_label.Visibility = answer ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
		_bars.Visibility = listening ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		_dot.Visibility = thinking || mode == Mode.Flash ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		_dot.Fill = mode == Mode.Flash ? new System.Windows.Media.SolidColorBrush(Crimson) : System.Windows.Media.Brushes.Transparent;
		_dot.Stroke = new System.Windows.Media.SolidColorBrush(mode == Mode.Flash ? Crimson : Steel);
		_answerPanel.Visibility = answer ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
		_pill.MinWidth = answer ? 480 * Scale : 0;
		_pill.MaxWidth = answer ? 560 * Scale : double.PositiveInfinity;
		if (listening) foreach (System.Windows.Shapes.Rectangle bar in _bars.Children) bar.Height = 18 * Scale;
		_window.Show();
		Place(_window);
		_hide?.Stop();
		if (duration is { } visible)
		{
			_hide = new System.Windows.Threading.DispatcherTimer { Interval = visible };
			_hide.Tick += (_, _) => { _hide!.Stop(); if (_mode is Mode.Answer or Mode.Flash) Hide(); };
			_hide.Start();
		}
	}

	private static void Hide()
	{
		_mode = Mode.Hidden;
		_window?.Hide();
	}

	private static void UpdateBars()
	{
		if (_mode != Mode.Listening || _bars is null) return;
		bool reduced = !System.Windows.SystemParameters.ClientAreaAnimation;
		double[] heights;
		lock (Levels) { heights = JarvisListenLevels.Heights(Levels, _heights, reduced); _heights = heights; }
		for (int i = 0; i < _bars.Children.Count && i < heights.Length; i++) ((System.Windows.Shapes.Rectangle)_bars.Children[i]).Height = heights[i] * Scale;
	}

	private static void Post(Action action, FileLogger logger)
	{
		try
		{
			EnsureThread(logger);
			_dispatcher?.InvokeAsync(() => { try { action(); } catch (Exception error) { logger.Error("Jarvis: the indicator could not update.", error); } });
		}
		catch (Exception error) { logger.Error("Jarvis: the indicator is unavailable.", error); }
	}

	private static void EnsureThread(FileLogger logger)
	{
		lock (Sync)
		{
			if (_dispatcher is not null) return;
			var ready = new ManualResetEventSlim(false);
			var thread = new Thread(() =>
			{
				try
				{
					_dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
					_window = Create();
					_window.SourceInitialized += (_, _) =>
					{
						nint handle = new System.Windows.Interop.WindowInteropHelper(_window).Handle;
						nint style = GetWindowLongPtr(handle, -20);
						SetWindowLongPtr(handle, -20, new IntPtr(style.ToInt64() | 0x8000000L | 0x20L | 0x80L | 0x8L)); // NOACTIVATE | TRANSPARENT | TOOLWINDOW | TOPMOST
					};
				}
				catch (Exception error) { logger.Error("Jarvis: the indicator window could not be created.", error); }
				finally { ready.Set(); }
				System.Windows.Threading.Dispatcher.Run();
			}) { IsBackground = true, Name = "Core Jarvis indicator" };
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			ready.Wait(TimeSpan.FromSeconds(3));
		}
	}

	private static System.Windows.Window Create()
	{
		double s = Scale;
		var crimson = new System.Windows.Media.SolidColorBrush(Crimson);
		var white = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0xF7, 0xFA));
		var font = new System.Windows.Media.FontFamily("Segoe UI");
		_bars = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Height = 30 * s, VerticalAlignment = System.Windows.VerticalAlignment.Center };
		for (int i = 0; i < JarvisListenLevels.Bars; i++)
			_bars.Children.Add(new System.Windows.Shapes.Rectangle { Width = 4 * s, Height = 6 * s, RadiusX = 2 * s, RadiusY = 2 * s, Fill = crimson,
				Margin = new System.Windows.Thickness(i == 0 ? 0 : 3 * s, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
		_dot = new System.Windows.Shapes.Ellipse { Width = 12 * s, Height = 12 * s, StrokeThickness = 1.5 * s, VerticalAlignment = System.Windows.VerticalAlignment.Center };
		_label = new System.Windows.Controls.TextBlock { Foreground = white, FontFamily = font, FontSize = 19 * s, FontWeight = System.Windows.FontWeights.SemiBold,
			Margin = new System.Windows.Thickness(14 * s, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
		_orb = new System.Windows.Shapes.Ellipse { Width = 36 * s, Height = 36 * s, Stroke = crimson, StrokeThickness = 3 * s, VerticalAlignment = System.Windows.VerticalAlignment.Top };
		_brand = new System.Windows.Controls.TextBlock { Foreground = crimson, FontFamily = font, FontSize = 12 * s, FontWeight = System.Windows.FontWeights.Bold };
		_answer = new System.Windows.Controls.TextBlock { Foreground = white, FontFamily = font, FontSize = 19 * s, FontWeight = System.Windows.FontWeights.SemiBold, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 4 * s, 0, 4 * s) };
		_hint = new System.Windows.Controls.TextBlock { Foreground = new System.Windows.Media.SolidColorBrush(Steel), FontFamily = font, FontSize = 14 * s };
		var text = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(14 * s, 0, 0, 0) };
		text.Children.Add(_brand);
		text.Children.Add(_answer);
		text.Children.Add(_hint);
		var answerRow = new System.Windows.Controls.DockPanel();
		answerRow.Children.Add(_orb);
		answerRow.Children.Add(text);
		_answerPanel = new System.Windows.Controls.StackPanel { Visibility = System.Windows.Visibility.Collapsed };
		_answerPanel.Children.Add(answerRow);
		var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
		row.Children.Add(_bars);
		row.Children.Add(_dot);
		row.Children.Add(_label);
		var content = new System.Windows.Controls.Grid();
		content.Children.Add(row);
		content.Children.Add(_answerPanel);
		_pill = new System.Windows.Controls.Border
		{
			Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB8, 0, 0, 0)),
			BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1D, 0x21)), BorderThickness = new System.Windows.Thickness(1),
			CornerRadius = new System.Windows.CornerRadius(10 * s), Padding = new System.Windows.Thickness(22 * s, 11 * s, 22 * s, 11 * s), MinHeight = 52 * s, Child = content,
		};
		return new System.Windows.Window
		{
			WindowStyle = System.Windows.WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent,
			Topmost = true, ShowInTaskbar = false, ShowActivated = false, Focusable = false, IsHitTestVisible = false,
			SizeToContent = System.Windows.SizeToContent.WidthAndHeight, ResizeMode = System.Windows.ResizeMode.NoResize, Content = _pill,
		};
	}

	private static void Place(System.Windows.Window window)
	{
		window.UpdateLayout();
		window.Left = System.Windows.SystemParameters.PrimaryScreenWidth - window.ActualWidth - 40 * Scale;
		window.Top = 32 * Scale;
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
	[DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
}
