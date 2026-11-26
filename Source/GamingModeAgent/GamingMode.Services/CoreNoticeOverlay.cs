using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GamingMode.Services;

// Eternal Core Mark III: a calm, non-interactive notice over Big Picture (plugins not
// loaded, Steam hold expired, Steam updating). It cannot take focus or input: no
// activation, click-through, tool window. Steam keeps the controller the whole time
// (upstream's rule: the agent never reads controllers for UI), and the notice disappears
// on its own after a few seconds.
//
// Callers check anti-cheat safe mode and running games before showing it; Show checks the
// guard again on its own thread right before the window appears.
internal static class CoreNoticeOverlay
{
	private static int _showing;

	internal static void Show(string text, TimeSpan visible, Func<bool> stillAllowed, FileLogger logger)
	{
		if (Interlocked.Exchange(ref _showing, 1) != 0) { logger.Info("Core notice skipped: another notice is on screen."); return; }
		var thread = new Thread(() =>
		{
			try
			{
				if (!stillAllowed()) { logger.Info("Core notice withdrawn: a game started."); return; }
				Window window = Create(text);
				var dispatcher = Dispatcher.CurrentDispatcher;
				window.Closed += (_, _) => dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
				window.SourceInitialized += (_, _) =>
				{
					nint handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
					nint style = GetWindowLongPtr(handle, -20);
					SetWindowLongPtr(handle, -20, new IntPtr(style.ToInt64() | 0x8000000L | 0x20L | 0x80L | 0x8L)); // NOACTIVATE | TRANSPARENT | TOOLWINDOW | TOPMOST
				};
				window.Opacity = 0;
				window.Show();
				window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
				var close = new DispatcherTimer { Interval = visible };
				close.Tick += (_, _) =>
				{
					close.Stop();
					var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400));
					fade.Completed += (_, _) => window.Close();
					window.BeginAnimation(UIElement.OpacityProperty, fade);
				};
				close.Start();
				// A game appearing mid-notice removes it at once.
				var guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
				guard.Tick += (_, _) => { if (!stillAllowed()) { guard.Stop(); close.Stop(); window.Close(); } };
				guard.Start();
				logger.Info("Core notice shown: " + text);
				Dispatcher.Run();
			}
			catch (Exception error) { logger.Error("Core notice could not be shown.", error); }
			finally { Volatile.Write(ref _showing, 0); }
		}) { IsBackground = true, Name = "Core Notice" };
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
	}

	private static Window Create(string text)
	{
		double width = SystemParameters.PrimaryScreenWidth, height = SystemParameters.PrimaryScreenHeight;
		double scale = Math.Clamp(height / 1080.0, 0.75, 2.0);
		var bar = new Border { Width = 6 * scale, Background = new SolidColorBrush(Color.FromRgb(0xD7, 0x26, 0x3D)), CornerRadius = new CornerRadius(3 * scale) };
		var label = new TextBlock
		{
			Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFA)), FontFamily = new FontFamily("Segoe UI"),
			FontSize = 24 * scale, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(20 * scale, 0, 0, 0), MaxWidth = Math.Min(1100 * scale, width * 0.7)
		};
		var row = new DockPanel { LastChildFill = true };
		DockPanel.SetDock(bar, Dock.Left);
		row.Children.Add(bar);
		row.Children.Add(label);
		var card = new Border
		{
			Background = Brushes.Black, // AMOLED black, like every dark Core surface
			BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x46)), BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(18 * scale), Padding = new Thickness(24 * scale, 20 * scale, 28 * scale, 20 * scale), Child = row
		};
		var window = new Window
		{
			WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false, ShowActivated = false, Topmost = true, Focusable = false, IsHitTestVisible = false,
			SizeToContent = SizeToContent.WidthAndHeight, Content = card, WindowStartupLocation = WindowStartupLocation.Manual
		};
		window.Loaded += (_, _) =>
		{
			window.Left = (width - window.ActualWidth) / 2;
			window.Top = height - window.ActualHeight - 72 * scale;
		};
		window.Left = -10000; window.Top = -10000;
		return window;
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hWnd, int index);
	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hWnd, int index, nint value);
}
