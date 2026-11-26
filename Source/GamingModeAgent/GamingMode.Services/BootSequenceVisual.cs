using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GamingMode.Services;

// Eternal Core Mark III: the boot sequence, drawn natively in WPF like the rest of the
// splash (no WebView, no frames). It reproduces docs/eternal/brand/preview/index.html on
// a 1280x720 canvas scaled to the screen: Eternal's line charges in from both ends and
// drops into the key that reads as an E, glints, then Core's ring charges, the chrome
// ticks turn and lock, the core pulses and "CORE" appears. AMOLED black background.
// Eternal's stars: a sparse field fades in behind the key, then rushes outward and falls
// away as Core's ring charges, so Core lands on plain black.
//
// The player owns everything time-based: the storyboard, the sound, and the skip
// polling. Every part is optional: if the sound or the input polling cannot start, the
// animation still plays; if the animation cannot be built, the caller shows the plain
// splash. Completion always resolves: on the end, on a skip, on a failure, or on Stop.
internal sealed class BootSequencePlayer
{
	private static readonly Color Crimson = Color.FromRgb(0xD7, 0x26, 0x3D);
	private static readonly Color RingUnlit = Color.FromRgb(0x3A, 0x3F, 0x46);
	private static readonly Color Glint = Color.FromRgb(0xFF, 0x8A, 0x96);

	private readonly Storyboard _storyboard = new();
	private readonly Canvas _canvas = new() { Width = 1280, Height = 720, Background = Brushes.Black, ClipToBounds = true };
	private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly bool _sound;
	private readonly FileLogger _logger;
	private BootSequenceInput? _input;
	private int _started, _finished;

	internal FrameworkElement Visual { get; }
	internal Task Completion => _completion.Task;

	private BootSequencePlayer(bool sound, FileLogger logger)
	{
		_sound = sound;
		_logger = logger;
		BuildStars();
		BuildEternal();
		BuildCore();
		_storyboard.Completed += (_, _) => Finish("finished");
		Visual = new Grid { Background = Brushes.Black, Children = { new Viewbox { Stretch = Stretch.Uniform, Child = _canvas } } };
	}

	internal static BootSequencePlayer? TryCreate(bool sound, FileLogger logger)
	{
		try { return new BootSequencePlayer(sound, logger); }
		catch (Exception error) { logger.Error("Boot sequence could not be built; showing the plain splash.", error); return null; }
	}

	// Called on the splash dispatcher once the first frame is on screen. The gate is the
	// TV becoming ready (or nothing); it never delays the start beyond MaxStartDelay.
	internal void Start(Dispatcher dispatcher, Task gate)
	{
		if (Interlocked.Exchange(ref _started, 1) != 0) return;
		_ = Task.Run(async () =>
		{
			try { await gate.WaitAsync(BootSequencePolicy.MaxStartDelay).ConfigureAwait(false); }
			catch { /* Timed out or failed: start anyway. */ }
			try
			{
				await dispatcher.InvokeAsync(() =>
				{
					if (Volatile.Read(ref _finished) != 0) return;
					_storyboard.Begin(_canvas, true);
					if (_sound) BootSequenceSound.Play(_logger);
					_input = BootSequenceInput.TryStart(() => dispatcher.BeginInvoke(new Action(Skip)), _logger);
				});
			}
			catch (Exception error) { _logger.Error("Boot sequence could not start.", error); Finish("failed"); }
		});
	}

	// Any controller button or key: jump to the final frame (Core's lit logo) at once.
	internal void Skip()
	{
		if (Volatile.Read(ref _finished) != 0) return;
		try { _storyboard.SkipToFill(_canvas); } catch { }
		Finish("skipped");
	}

	internal void Stop() => Finish("stopped");

	private void Finish(string how)
	{
		if (Interlocked.Exchange(ref _finished, 1) != 0) return;
		try { _input?.Dispose(); } catch { }
		if (how != "finished") BootSequenceSound.Stop();
		_logger.Info($"Boot sequence {how}.");
		_completion.TrySetResult();
	}

	// ---------- drawing (coordinates of the 1280x720 preview) ----------

	private static LinearGradientBrush Chrome(bool absolute = false, double top = 0, double bottom = 1)
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = new Point(0, top), EndPoint = new Point(0, bottom),
			MappingMode = absolute ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox,
			GradientStops =
			{
				new GradientStop(Color.FromRgb(0xF4, 0xF7, 0xFA), 0),
				new GradientStop(Color.FromRgb(0xAE, 0xB8, 0xC2), 0.48),
				new GradientStop(Color.FromRgb(0x5B, 0x63, 0x6C), 0.52),
				new GradientStop(Color.FromRgb(0xC9, 0xD1, 0xD9), 1),
			}
		};
		brush.Freeze();
		return brush;
	}

	// Widely spaced capitals, centred on x, with the baseline at y.
	private static FrameworkElement Spaced(string text, double size, double spacing, double centerX, double baseline)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal };
		var fill = Chrome();
		for (int i = 0; i < text.Length; i++)
		{
			row.Children.Add(new TextBlock
			{
				Text = text[i].ToString(), FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground = fill,
				Margin = new Thickness(0, 0, i == text.Length - 1 ? 0 : spacing, 0)
			});
		}
		var holder = new Grid { Width = 1280, Children = { row } };
		row.HorizontalAlignment = HorizontalAlignment.Center;
		holder.RenderTransform = new TranslateTransform(centerX - 640, 0);
		Canvas.SetLeft(holder, 0);
		Canvas.SetTop(holder, baseline - size * 1.08); // Segoe UI ascent is about 1.08 em
		return holder;
	}

	private static Line Stroke(double x1, double y1, double x2, double y2, Brush brush, double width) => new()
	{
		X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = width,
		StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
	};

	private void Animate(DependencyObject target, DependencyProperty property, double? from, double to, double begin, double seconds, IEasingFunction? ease = null)
		=> Animate(target, new PropertyPath(property), from, to, begin, seconds, ease);

	private void Animate(DependencyObject target, PropertyPath path, double? from, double to, double begin, double seconds, IEasingFunction? ease = null)
	{
		var animation = new DoubleAnimation { To = to, BeginTime = TimeSpan.FromSeconds(begin), Duration = TimeSpan.FromSeconds(seconds), EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
		if (from is { } start) animation.From = start;
		Storyboard.SetTarget(animation, target);
		Storyboard.SetTargetProperty(animation, path);
		_storyboard.Children.Add(animation);
	}

	private void KeyFrames(DependencyObject target, PropertyPath path, double begin, params (double At, double Value)[] frames)
	{
		var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(begin), FillBehavior = FillBehavior.HoldEnd };
		foreach (var (at, value) in frames) animation.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
		Storyboard.SetTarget(animation, target);
		Storyboard.SetTargetProperty(animation, path);
		_storyboard.Children.Add(animation);
	}

	private void BuildStars()
	{
		// Fixed seed: the same sky every boot. Steel and near-white, one in twenty crimson.
		var random = new Random(7);
		var field = new Canvas { Width = 1280, Height = 720, Opacity = 0, RenderTransform = new ScaleTransform(1, 1, 640, 345.6) };
		var steel = new SolidColorBrush(Color.FromRgb(0xC9, 0xD1, 0xD9));
		var white = new SolidColorBrush(Color.FromRgb(0xE6, 0xEC, 0xF2));
		var crimson = new SolidColorBrush(Crimson);
		steel.Freeze(); white.Freeze(); crimson.Freeze();
		for (int i = 0; i < 220; i++)
		{
			double x = random.NextDouble() * 1280, y = random.NextDouble() * 720, size = 1 + random.NextDouble() * 2.2;
			double pick = random.NextDouble();
			var star = new Ellipse { Width = size, Height = size, Fill = pick < 0.05 ? crimson : pick < 0.3 ? white : steel, Opacity = 0.25 + random.NextDouble() * 0.6 };
			Canvas.SetLeft(star, x - size / 2);
			Canvas.SetTop(star, y - size / 2);
			field.Children.Add(star);
		}
		_canvas.Children.Add(field);
		KeyFrames(field, new PropertyPath(UIElement.OpacityProperty), 0, (0, 0), (0.8, 1), (4.3, 1), (4.9, 0));
		var rush = new QuadraticEase { EasingMode = EasingMode.EaseIn };
		Animate(field, new PropertyPath("RenderTransform.ScaleX"), 1, 2.6, 3.9, 0.8, rush);
		Animate(field, new PropertyPath("RenderTransform.ScaleY"), 1, 2.6, 3.9, 0.8, rush);
	}

	private void BuildEternal()
	{
		var group = new Canvas { Width = 1280, Height = 720, Opacity = 0 };
		_canvas.Children.Add(group);
		KeyFrames(group, new PropertyPath(UIElement.OpacityProperty), 0, (0, 0), (0.205, 1), (3.608, 1), (4.1, 0));
		group.Children.Add(Spaced("ETERNAL", 56, 20, 636, 352));

		// translate(640,400) scale(1.9) translate(-310,-122)
		static double X(double x) => 640 + 1.9 * (x - 310);
		static double Y(double y) => 400 + 1.9 * (y - 122);
		const double W = 3 * 1.9;
		var unlit = new SolidColorBrush(RingUnlit); unlit.Freeze();
		var crimson = new SolidColorBrush(Crimson); crimson.Freeze();
		var glint = new SolidColorBrush(Glint); glint.Freeze();
		group.Children.Add(Stroke(X(170), Y(122), X(450), Y(122), unlit, W));
		var keyLines = new (double X1, double Y1, double X2, double Y2)[] { (310, 122, 310, 140), (310, 140, 321, 140), (310, 131, 319, 131) };
		foreach (var k in keyLines) group.Children.Add(Stroke(X(k.X1), Y(k.Y1), X(k.X2), Y(k.Y2), unlit, W));

		// The two halves charge from the ends to the centre (1.8 s ease-in-out from 0.5 s).
		foreach (double from in new[] { 170.0, 450.0 })
		{
			var half = Stroke(X(from), Y(122), X(from), Y(122), crimson, W);
			half.Opacity = 0;
			group.Children.Add(half);
			Animate(half, UIElement.OpacityProperty, 0, 1, 0.5, 0.01);
			Animate(half, Line.X2Property, X(from), X(310), 0.5, 1.8, new CubicEase { EasingMode = EasingMode.EaseInOut });
		}
		// The key draws in along its strokes (0.6 s ease-out from 2.3 s), then glints at 2.9 s.
		double begin = 2.3;
		double[] share = { 0.28, 0.18, 0.14 };
		for (int i = 0; i < keyLines.Length; i++)
		{
			var k = keyLines[i];
			var line = Stroke(X(k.X1), Y(k.Y1), X(k.X1), Y(k.Y1), crimson, W);
			line.Opacity = 0;
			group.Children.Add(line);
			Animate(line, UIElement.OpacityProperty, 0, 1, begin, 0.01);
			Animate(line, Line.X2Property, X(k.X1), X(k.X2), begin, share[i], new QuadraticEase { EasingMode = EasingMode.EaseOut });
			Animate(line, Line.Y2Property, Y(k.Y1), Y(k.Y2), begin, share[i], new QuadraticEase { EasingMode = EasingMode.EaseOut });
			begin += share[i];
		}
		var glintGroup = new Canvas { Width = 1280, Height = 720, Opacity = 0 };
		foreach (var k in keyLines) glintGroup.Children.Add(Stroke(X(k.X1), Y(k.Y1), X(k.X2), Y(k.Y2), glint, W));
		group.Children.Add(glintGroup);
		KeyFrames(glintGroup, new PropertyPath(UIElement.OpacityProperty), 2.9, (0, 0), (0.225, 1), (0.45, 0));
	}

	private void BuildCore()
	{
		const double CX = 640, CY = 320, S = 1.7;
		var group = new Canvas { Width = 1280, Height = 720, Opacity = 0 };
		_canvas.Children.Add(group);
		Animate(group, UIElement.OpacityProperty, 0, 1, 4.05, 0.3);

		double radius = 62 * S, ring = 9 * S, size = radius * 2 + ring;
		Ellipse Ring(Brush brush)
		{
			var ellipse = new Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = ring };
			Canvas.SetLeft(ellipse, CX - size / 2);
			Canvas.SetTop(ellipse, CY - size / 2);
			return ellipse;
		}
		var unlit = new SolidColorBrush(RingUnlit); unlit.Freeze();
		var crimson = new SolidColorBrush(Crimson); crimson.Freeze();
		group.Children.Add(Ring(unlit));

		// The lit ring charges from 9 o'clock (2.3 s ease-in-out from 4.3 s). WPF dash
		// lengths are in multiples of the stroke thickness.
		double dash = 2 * Math.PI * radius / ring;
		var lit = Ring(crimson);
		lit.StrokeDashArray = new DoubleCollection { dash, dash };
		lit.StrokeDashCap = PenLineCap.Round;
		lit.StrokeDashOffset = dash;
		lit.Opacity = 0;
		lit.RenderTransformOrigin = new Point(0.5, 0.5);
		lit.RenderTransform = new RotateTransform(180);
		group.Children.Add(lit);
		Animate(lit, UIElement.OpacityProperty, 0, 1, 4.3, 0.01);
		Animate(lit, Shape.StrokeDashOffsetProperty, dash, 0, 4.3, 2.3, new CubicEase { EasingMode = EasingMode.EaseInOut });

		// Chrome ticks turn like a lock dial, settle, then light (opacity .3 -> 1 at 6.6 s).
		var ticks = new Canvas { Width = 1280, Height = 720, Opacity = 0.3, RenderTransform = new RotateTransform(-180, CX, CY) };
		var chrome = Chrome(absolute: true, top: CY - 48 * S, bottom: CY + 48 * S);
		var tickLines = new (double, double, double, double)[]
		{
			(0, -48, 0, -38), (0, 38, 0, 48), (-48, 0, -38, 0), (38, 0, 48, 0),
			(-33.9, -33.9, -26.9, -26.9), (26.9, 26.9, 33.9, 33.9), (33.9, -33.9, 26.9, -26.9), (-26.9, 26.9, -33.9, 33.9),
		};
		foreach (var (x1, y1, x2, y2) in tickLines) ticks.Children.Add(Stroke(CX + x1 * S, CY + y1 * S, CX + x2 * S, CY + y2 * S, chrome, 5 * S));
		group.Children.Add(ticks);
		KeyFrames(ticks, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"), 4.3,
			(0, -180), (0.92, -80), (1.84, -12), (2.024, 6), (2.162, -2), (2.3, 0));
		Animate(ticks, UIElement.OpacityProperty, 0.3, 1, 6.6, 0.3);

		// The chrome core with its crimson centre pulses once at 6.6 s.
		var core = new Canvas { Width = 1280, Height = 720, RenderTransform = new ScaleTransform(1, 1, CX, CY) };
		var outer = new Ellipse { Width = 44 * S, Height = 44 * S, Fill = Chrome() };
		Canvas.SetLeft(outer, CX - 22 * S); Canvas.SetTop(outer, CY - 22 * S);
		var centre = new Ellipse { Width = 18 * S, Height = 18 * S, Fill = crimson };
		Canvas.SetLeft(centre, CX - 9 * S); Canvas.SetTop(centre, CY - 9 * S);
		core.Children.Add(outer);
		core.Children.Add(centre);
		group.Children.Add(core);
		foreach (string axis in new[] { "ScaleX", "ScaleY" })
			KeyFrames(core, new PropertyPath($"(UIElement.RenderTransform).(ScaleTransform.{axis})"), 6.6, (0, 1), (0.3, 1.15), (0.6, 1));

		var word = Spaced("CORE", 40, 26, 646, 540);
		word.Opacity = 0;
		group.Children.Add(word);
		Animate(word, UIElement.OpacityProperty, 0, 1, 6.9, 0.7, new QuadraticEase { EasingMode = EasingMode.EaseOut });
	}
}

// The arcade boot sound (docs/eternal/brand/sound/eternal-core-boot.ogg, converted to
// 16-bit PCM WAV and embedded in GamingMode.exe). Played with winmm's PlaySound from
// memory, asynchronously: it never blocks, and a missing audio device only means silence.
internal static class BootSequenceSound
{
	private const uint SndAsync = 0x0001, SndNoDefault = 0x0002, SndMemory = 0x0004;
	private static readonly object Sync = new();
	private static GCHandle _pinned;

	internal static void Play(FileLogger logger)
	{
		try
		{
			lock (Sync)
			{
				if (!_pinned.IsAllocated)
				{
					using var stream = typeof(BootSequenceSound).Assembly.GetManifestResourceStream("GamingMode.BootSound.wav");
					if (stream is null) { logger.Info("Boot sound is not embedded in this build; playing silently."); return; }
					var bytes = new byte[stream.Length];
					stream.ReadExactly(bytes);
					// PlaySound reads the buffer while it plays: keep it pinned for the process lifetime.
					_pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
				}
				if (!PlaySound(_pinned.AddrOfPinnedObject(), 0, SndAsync | SndNoDefault | SndMemory))
					logger.Info("Boot sound could not be played (no audio device?); playing silently.");
			}
		}
		catch (Exception error) { logger.Error("Boot sound failed; playing silently.", error); }
	}

	internal static void Stop()
	{
		try { lock (Sync) if (_pinned.IsAllocated) PlaySound(0, 0, 0); } catch { }
	}

	[DllImport("winmm.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PlaySound(nint sound, nint module, uint flags);
}

// Skip polling while the sequence plays: XInput controllers and the keyboard/mouse
// button state, read-only, every 40 ms, for at most the sequence's hard limit.
//
// Upstream's rule stands: the agent never opens controllers (SDL's HIDAPI drivers fought
// Steam Input; see DashboardShortcutService.cs). XInputGetState only reads the state
// Windows already publishes for XInput pads (and for the virtual pad Steam Input creates);
// it opens no device and changes no configuration. A Steam Controller without Steam
// running is in its keyboard-and-mouse mode, which the key-state read covers.
internal sealed class BootSequenceInput : IDisposable
{
	private readonly Timer _timer;
	private readonly BootSkipDetector _detector = new();
	private readonly Action _skip;
	private readonly long _deadline = Environment.TickCount64 + (long)BootSequencePolicy.HardLimit.TotalMilliseconds;
	private int _disposed, _busy;

	private BootSequenceInput(Action skip)
	{
		_skip = skip;
		_timer = new Timer(_ => Poll(), null, 0, 40);
	}

	internal static BootSequenceInput? TryStart(Action skip, FileLogger logger)
	{
		try { return new BootSequenceInput(skip); }
		catch (Exception error) { logger.Error("Boot sequence skip input is unavailable; it plays to the end.", error); return null; }
	}

	private void Poll()
	{
		if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _busy, 1) != 0) return;
		try
		{
			if (Environment.TickCount64 > _deadline) { Dispose(); return; }
			var sources = new List<ulong>(8);
			for (uint pad = 0; pad < 4; pad++) sources.Add(XInputPad.ButtonsAndTriggers(pad));
			// Virtual keys 1..254 (mouse buttons included), packed 64 per source.
			for (int block = 0; block < 4; block++)
			{
				ulong bits = 0;
				for (int bit = 0; bit < 64; bit++)
				{
					int vk = block * 64 + bit;
					if (vk is 0 or 255) continue;
					if ((GetAsyncKeyState(vk) & 0x8000) != 0) bits |= 1UL << bit;
				}
				sources.Add(bits);
			}
			if (_detector.Observe(sources)) { Dispose(); _skip(); }
		}
		catch { Dispose(); }
		finally { Volatile.Write(ref _busy, 0); }
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		_timer.Dispose();
	}

	[DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}

// Read-only XInput state (see BootSequenceInput for why this does not break the rule).
internal static class XInputPad
{
	internal const ushort DpadUp = 0x0001, DpadDown = 0x0002, A = 0x1000, B = 0x2000;

	[StructLayout(LayoutKind.Sequential)]
	private struct Gamepad { public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LeftX, LeftY, RightX, RightY; }

	[StructLayout(LayoutKind.Sequential)]
	private struct State { public uint Packet; public Gamepad Pad; }

	private static int _unavailable;

	internal readonly record struct Reading(bool Connected, ushort Buttons, short LeftY);

	internal static Reading Read(uint index)
	{
		if (Volatile.Read(ref _unavailable) != 0) return default;
		try { return XInputGetState(index, out State state) == 0 ? new(true, state.Pad.Buttons, state.Pad.LeftY) : default; }
		catch (DllNotFoundException) { Volatile.Write(ref _unavailable, 1); return default; }
		catch (EntryPointNotFoundException) { Volatile.Write(ref _unavailable, 1); return default; }
	}

	// Buttons in the low 16 bits, triggers past a third of their travel as bits 16 and 17.
	internal static ulong ButtonsAndTriggers(uint index)
	{
		if (Volatile.Read(ref _unavailable) != 0) return 0;
		try
		{
			if (XInputGetState(index, out State state) != 0) return 0;
			ulong bits = state.Pad.Buttons;
			if (state.Pad.LeftTrigger > 85) bits |= 1UL << 16;
			if (state.Pad.RightTrigger > 85) bits |= 1UL << 17;
			return bits;
		}
		catch (DllNotFoundException) { Volatile.Write(ref _unavailable, 1); return 0; }
		catch (EntryPointNotFoundException) { Volatile.Write(ref _unavailable, 1); return 0; }
	}

	[DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index, out State state);
}
