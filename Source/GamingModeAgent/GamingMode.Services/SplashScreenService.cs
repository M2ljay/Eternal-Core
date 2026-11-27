using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamingMode.Models;

namespace GamingMode.Services;

public sealed class SplashScreenService : IDisposable
{
	private readonly FileLogger _logger;

	private readonly object _sync = new object();

	private sealed class Surface
	{
		internal readonly CancellationTokenSource Cancellation = new();
		internal readonly long Started = Environment.TickCount64;
		internal Dispatcher? Dispatcher;
		internal Thread? Thread;
		internal Window? Window;
		internal nint Handle, EventHook;
		internal uint NativeThread;
		internal WinEventCallback? Callback;
		// Mark III boot sequence on this surface, if one plays (created on the splash thread).
		internal BootSequencePlayer? Player;
		// Mode-switch transitions hand off by fading their scene to black (ModeTransitionVisual).
		internal bool FadeToBlack;
	}
	private Surface? _surface;
	private Func<bool>? _foregroundGuard;
	private (int Left, int Top, int Width, int Height)? _physicalBounds;
	public bool Running { get { lock (_sync) return _surface is { } surface && !surface.Cancellation.IsCancellationRequested && surface.Thread?.IsAlive == true; } }
	public SplashScreenService(FileLogger logger) => _logger = logger;
	internal void SetForegroundGuard(Func<bool> allowed) { lock (_sync) _foregroundGuard = allowed; }
	internal void SetTargetPhysicalBounds(int left, int top, int width, int height)
	{ if (width > 0 && height > 0) lock (_sync) _physicalBounds = (left, top, width, height); }
	// bootGate: the Mark III boot sequence waits for it (the TV switching to Core) before it
	// starts, for at most BootSequencePolicy.MaxStartDelay. Null starts it at once.
	public void Show(GamingSplashSettings settings, int failSafeCloseMs = 150000, Task? bootGate = null)
	{
		if (!settings.Enabled) return;
		string? logoPath = ResolveLogoPath(settings.LogoPath);
		var boot = BootSequencePolicy.Decide(BootSequenceTrigger.PowerOn, settings.BootSequenceEnabled, settings.BootSoundEnabled,
			settings.Enabled, gamingMode: true, gameRunning: false, protectedGame: false);
		if (!boot.Play) _logger.Info($"Boot sequence skipped at power-on: {boot.Reason}.");
		ShowCore(surface => CreateBootWindow(surface, boot.Play, boot.Sound) ?? CreateSplashWindow(logoPath, settings), failSafeCloseMs, bootGate);
	}

	// Mark III: the boot sequence on wake from sleep, as a short overlay over Big Picture.
	// The caller has already applied BootSequencePolicy (Gaming Mode, no game, no protected
	// game). It closes itself after the sequence, a skip, or the hard limit.
	internal void ShowWakeSequence(bool sound, Task? gate)
	{
		int failSafe = (int)(BootSequencePolicy.HardLimit + BootSequencePolicy.WakeHold).TotalMilliseconds + 2000;
		Surface? mine = ShowCore(surface => CreateBootWindow(surface, true, sound) ?? throw new InvalidOperationException("Boot sequence unavailable."), failSafe, gate);
		if (mine is null) { _logger.Info("Wake sequence skipped: another Gaming Mode surface is on screen."); return; }
		_ = Task.Run(async () =>
		{
			try
			{
				await WaitForBootSequenceAsync().ConfigureAwait(false);
				await Task.Delay(BootSequencePolicy.WakeHold).ConfigureAwait(false);
				lock (_sync) if (!ReferenceEquals(_surface, mine)) return;
				await HideAsync(fade: true, fadeMs: 400).ConfigureAwait(false);
			}
			catch (Exception error) { _logger.Error("Wake sequence close failed.", error); CancelImmediately(); }
		});
	}

	// Resolves when the boot sequence on the current surface ended, was skipped or failed,
	// or after BootSequencePolicy.HardLimit since the surface appeared, whichever is first.
	// No sequence: resolves at once. Never throws.
	internal async Task WaitForBootSequenceAsync()
	{
		Surface? surface; lock (_sync) surface = _surface;
		if (surface is null) return;
		for (int i = 0; i < 40 && surface.Player is null && surface.Thread?.IsAlive == true && Environment.TickCount64 - surface.Started < 2000; i++)
			await Task.Delay(50).ConfigureAwait(false);
		if (surface.Player is not { } player) return;
		long remaining = (long)BootSequencePolicy.HardLimit.TotalMilliseconds - (Environment.TickCount64 - surface.Started);
		if (remaining <= 0) return;
		try { await player.Completion.WaitAsync(TimeSpan.FromMilliseconds(remaining), surface.Cancellation.Token).ConfigureAwait(false); }
		catch (TimeoutException) { _logger.Info("Boot sequence reached its time box; the splash continues without it."); }
		catch (OperationCanceledException) { }
	}

	private Window? CreateBootWindow(Surface surface, bool play, bool sound)
	{
		if (!play) return null;
		BootSequencePlayer? player = BootSequencePlayer.TryCreate(sound, _logger);
		if (player is null) return null;
		Window window = CreateSplashWindow(null);
		window.Background = Brushes.Black;
		window.Content = player.Visual;
		surface.Player = player;
		return window;
	}
	public void ShowTransition(ModeKind mode, string? language, GamingSplashSettings? settings = null, int failSafeCloseMs = 90000)
	{
		ShowCore(surface =>
		{
			surface.FadeToBlack = true;
			Window window = CreateSplashWindow(null);
			window.Left = 0; window.Top = 0;
			window.Width = SystemParameters.PrimaryScreenWidth; window.Height = SystemParameters.PrimaryScreenHeight;
			window.Content = ModeTransitionVisual.Create(mode, language, settings);
			return window;
		}, failSafeCloseMs);
	}
	// Returns the surface it created, or null when another surface is already up.
	private Surface? ShowCore(Func<Surface, Window> createWindow, int failSafeCloseMs, Task? bootGate = null)
	{
		Surface surface = new(); Func<bool>? allowed; (int Left,int Top,int Width,int Height)? bounds;
		lock (_sync)
		{
			if (_surface?.Thread?.IsAlive == true) return null;
			_surface = surface; allowed = _foregroundGuard; bounds = _physicalBounds;
		}
		ManualResetEventSlim ready = new(false);
		bool Allowed() { try { return !surface.Cancellation.IsCancellationRequested && (allowed?.Invoke() ?? true); } catch { return false; } }
		surface.Thread = new Thread(() =>
		{
			DispatcherTimer? failSafe = null;
			nint previousDpi = SetThreadDpiAwarenessContext(new nint(-4));
			try
			{
				var dispatcher = Dispatcher.CurrentDispatcher; surface.Dispatcher = dispatcher; surface.NativeThread = GetCurrentThreadId();
				if (!Allowed()) { surface.Cancellation.Cancel(); ready.Set(); return; }
				Window window;
				try { window = createWindow(surface); }
				catch (Exception error) when (surface.Player is null)
				{
					// The boot sequence could not be built: Gaming Mode goes on without a surface.
					_logger.Error("Gaming splash surface could not be created.", error);
					surface.Cancellation.Cancel(); ready.Set(); return;
				}
				surface.Window = window;
				window.SourceInitialized += (_, _) =>
				{
					surface.Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
					if (bounds is { } target) SetWindowPos(surface.Handle, new nint(-1), target.Left, target.Top, target.Width, target.Height, 0x0010);
					if (allowed is not null)
					{
						surface.Callback = (_, _, _, _, _, _, _) => { if (!Allowed()) CancelSurface(surface); };
						surface.EventHook = SetWinEventHook(3, 3, 0, surface.Callback, 0, 0, 0);
						if (surface.EventHook == 0) CancelSurface(surface);
					}
				};
				window.ContentRendered += (_, _) =>
				{
					_logger.Info($"Gaming splash first frame rendered after {Environment.TickCount64 - surface.Started} ms.");
					ready.Set();
					surface.Player?.Start(dispatcher, bootGate ?? Task.CompletedTask);
					if (!Allowed()) CancelSurface(surface);
				};
				window.Closed += (_, _) =>
				{
					surface.Player?.Stop();
					failSafe?.Stop(); surface.Cancellation.Cancel();
					if (surface.EventHook != 0) { UnhookWinEvent(surface.EventHook); surface.EventHook = 0; }
					surface.Handle = 0; ready.Set(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
				};
				if (!Allowed()) { surface.Cancellation.Cancel(); ready.Set(); return; }
				window.Show();
				if (bounds is { } shownBounds && surface.Handle != 0)
					SetWindowPos(surface.Handle, new nint(-1), shownBounds.Left, shownBounds.Top, shownBounds.Width, shownBounds.Height, 0x0010);
				if (!Allowed()) CancelSurface(surface);
				failSafe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(failSafeCloseMs, 1000, 300000)) };
				failSafe.Tick += (_, _) => { _logger.Info("Gaming splash deadline reached."); CancelSurface(surface); };
				failSafe.Start(); Dispatcher.Run();
			}
			catch (Exception error) { surface.Cancellation.Cancel(); _logger.Error("Gaming splash could not be shown.", error); ready.Set(); }
			finally
			{
				failSafe?.Stop();
				surface.Player?.Stop();
				if (surface.EventHook != 0) { UnhookWinEvent(surface.EventHook); surface.EventHook = 0; }
				if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
				lock (_sync) if (ReferenceEquals(_surface, surface)) _surface = null;
			}
		}) { IsBackground = true, Name = "Gaming Mode Splash" };
		surface.Thread.SetApartmentState(ApartmentState.STA); surface.Thread.Start();
		if (!ready.Wait(TimeSpan.FromSeconds(3))) _logger.Info("Gaming splash first frame is still pending after 3000 ms.");
		return surface;
	}
	private static void CancelSurface(Surface surface)
	{
		surface.Cancellation.Cancel();
		// Lower only our own no-activate surface immediately, before its Dispatcher
		// can process a close. A foreign foreground window must stay accessible.
		if (surface.Handle != 0 && GetWindowThreadProcessId(surface.Handle, out uint owner) == surface.NativeThread
			&& owner == Environment.ProcessId) SetWindowPos(surface.Handle, new nint(-2), 0, 0, 0, 0, 0x0013);
		var dispatcher = surface.Dispatcher;
		if (dispatcher is null || dispatcher.HasShutdownStarted) return;
		dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
		{
			try { surface.Window?.Close(); } catch (InvalidOperationException) { }
			dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
		}));
	}
	internal void CancelImmediately() { Surface? surface; lock (_sync) surface = _surface; if (surface is not null) CancelSurface(surface); }
	public async Task HideAsync(int minVisibleMs = 0, bool fade = false, int fadeMs = 450)
	{
		Surface? surface; lock (_sync) surface = _surface;
		if (surface?.Dispatcher is not { } dispatcher || surface.Cancellation.IsCancellationRequested) return;
		try
		{
			long remaining = minVisibleMs - (Environment.TickCount64 - surface.Started);
			if (remaining > 0) await Task.Delay((int)Math.Min(remaining,120000), surface.Cancellation.Token).ConfigureAwait(false);
			if (surface.Cancellation.IsCancellationRequested || dispatcher.HasShutdownStarted) return;
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			using var registration = surface.Cancellation.Token.Register(() => completion.TrySetResult());
			await dispatcher.InvokeAsync(() =>
			{
				if (surface.Cancellation.IsCancellationRequested) { completion.TrySetResult(); return; }
				if (!fade || surface.Window is not { } window) { CancelSurface(surface); completion.TrySetResult(); return; }
				// A transition fades its scene to the window's black, from whatever frame is showing,
				// then the window closes onto Big Picture or the desktop. Same duration as before: the
				// hand-off never waits for the animation.
				UIElement target = surface.FadeToBlack && window.Content is UIElement scene ? scene : window;
				var animation = new DoubleAnimation { From = target.Opacity, To = 0, Duration = TimeSpan.FromMilliseconds(Math.Clamp(fadeMs,100,3000)), FillBehavior = FillBehavior.HoldEnd };
				animation.Completed += (_, _) => { CancelSurface(surface); completion.TrySetResult(); };
				target.BeginAnimation(UIElement.OpacityProperty, animation);
			}, DispatcherPriority.Send);
			await completion.Task.WaitAsync(TimeSpan.FromMilliseconds(Math.Clamp(fadeMs,100,3000)+1000)).ConfigureAwait(false);
		}
		catch (OperationCanceledException) { }
		catch (Exception error) { _logger.Error("Gaming splash close failed.", error); CancelSurface(surface); }
	}
	public void Dispose()
	{
		Surface? surface; lock (_sync) surface = _surface;
		if (surface is null) return;
		CancelSurface(surface);
		if (surface.Thread is { IsAlive:true } thread && thread != Thread.CurrentThread) thread.Join(1000);
	}
	private delegate void WinEventCallback(nint hook,uint kind,nint hwnd,int objectId,int childId,uint thread,uint tick);
	[DllImport("user32.dll")] private static extern nint SetWinEventHook(uint minimum,uint maximum,nint module,WinEventCallback callback,uint process,uint thread,uint flags);
	[DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
	[DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
	[DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);

	internal static void ShowPreview(string variant, GamingSplashSettings settings, string? language)
	{
		var window = CreateSplashWindow(ResolveLogoPath(settings.LogoPath), settings, interactive: true);
		window.Left = 0;
		window.Top = 0;
		window.Width = SystemParameters.PrimaryScreenWidth;
		window.Height = SystemParameters.PrimaryScreenHeight;
		if (variant != "boot")
			window.Content = ModeTransitionVisual.Create(variant == "desktop" ? ModeKind.Desktop : ModeKind.Gaming, language, settings);
		if (window.Content is UIElement content) content.IsHitTestVisible = true;
		window.PreviewKeyDown += (_, e) =>
		{
			if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); }
		};
		window.PreviewMouseDown += (_, e) => { e.Handled = true; window.Close(); };
		new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }.Run(window);
	}

	private static Window CreateSplashWindow(string? logoPath, GamingSplashSettings? settings = null, bool interactive = false)
	{
		Grid grid = new Grid
		{
			Background = Brushes.Black,
			ClipToBounds = true
		};
		ModeTransitionVisual.AddGlows(grid, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight, settings);
		ImageSource imageSource = LoadImage(logoPath);
		if (imageSource != null)
		{
			grid.Children.Add(new Image
			{
				Source = imageSource,
				Effect = ModeTransitionVisual.TextShadow(),
				Stretch = Stretch.Uniform,
				Width = Math.Min(460.0, SystemParameters.VirtualScreenWidth * 0.28) * Playhub.Shared.SplashLogoScale.ForPath(logoPath),
				Height = 180.0 * Playhub.Shared.SplashLogoScale.ForPath(logoPath),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		else
		{
			grid.Children.Add(new TextBlock
			{
				Text = "CORE",
				Effect = ModeTransitionVisual.TextShadow(),
				Foreground = Brushes.White,
				FontSize = 56.0,
				FontWeight = FontWeights.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		Window window = new Window
		{
			WindowStyle = WindowStyle.None,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false,
			ShowActivated = interactive,
			Topmost = true,
			Background = Brushes.Black,
			Content = grid,
			Left = SystemParameters.VirtualScreenLeft,
			Top = SystemParameters.VirtualScreenTop,
			Width = SystemParameters.VirtualScreenWidth,
			Height = SystemParameters.VirtualScreenHeight,
			WindowStartupLocation = WindowStartupLocation.Manual
		};
		window.SourceInitialized += delegate
		{
			if (interactive) return;
			try
			{
				nint handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
				if (handle != 0)
				{
					nint exStyle = GetWindowLongPtr(handle, -20);
					SetWindowLongPtr(handle, -20, new IntPtr(exStyle.ToInt64() | 0x8000000L | 0x20L | 0x80L));
				}
			}
			catch
			{
			}
		};
		return window;
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

	private static ImageSource? LoadImage(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return null;
		}
		BitmapImage bitmapImage = new BitmapImage();
		bitmapImage.BeginInit();
		bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
		bitmapImage.UriSource = new Uri(path, UriKind.Absolute);
		bitmapImage.EndInit();
		bitmapImage.Freeze();
		return bitmapImage;
	}

	private static string? ResolveLogoPath(string? configuredPath)
	{
		if (!string.IsNullOrWhiteSpace(configuredPath))
		{
			string text = Environment.ExpandEnvironmentVariables(configuredPath).Trim().Trim('"');
			if (File.Exists(text))
			{
				return text;
			}
		}
		string text2 = Path.Combine(AppContext.BaseDirectory, "assets", "base-logo.png");
		if (!File.Exists(text2))
		{
			return null;
		}
		return text2;
	}
}
