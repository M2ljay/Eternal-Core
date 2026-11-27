using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GamingMode.Services;

// Eternal Core Mark III: Core Repair, the couch-friendly way back when Decky's UI does not
// load. It needs no Decky: it is "GamingMode.exe repair", launched from a "Core Repair"
// tile in the Steam library (Core's Decky plugin adds the tile while it works; the tile
// stays when Decky later breaks). Full screen, AMOLED black, large buttons.
//
// Input: the arrow keys/Enter/Esc that Steam's desktop layout sends, and the D-pad, left
// stick, A and B of an XInput pad (or the virtual pad Steam Input gives a non-Steam
// shortcut), polled read-only while this window has focus. Steam launched this window
// like a game, so reading its pad is what any game does; no device is opened or changed.
//
// Every action runs in the agent (POST /core/repair/run) and this window only shows its
// progress, so closing the window never interrupts a repair.
internal static class CoreRepairWindow
{
	private sealed record Item(string Action, string TextKey);

	internal static int Run()
	{
		var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
		var window = new RepairWindow();
		app.Run(window);
		return 0;
	}

	private sealed class RepairWindow : Window
	{
		private static readonly Color Crimson = Color.FromRgb(0xD7, 0x26, 0x3D);
		private static readonly Color Gunmetal = Color.FromRgb(0x1A, 0x1D, 0x21);
		private static readonly Color Stroke = Color.FromRgb(0x3A, 0x3F, 0x46);
		private static readonly Color Ink = Color.FromRgb(0xF4, 0xF7, 0xFA);
		private static readonly Color Muted = Color.FromRgb(0xAE, 0xB8, 0xC2);

		private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
		private readonly string _baseUrl;
		private readonly string? _language;
		private readonly List<(Item Item, Border Box, TextBlock Label)> _rows = new();
		private readonly TextBlock _status = new();
		private readonly TextBlock _hint = new();
		private readonly DispatcherTimer _pad = new() { Interval = TimeSpan.FromMilliseconds(60) };
		private int _selected;
		private bool _busy;
		private int _steamConfirmations;
		private ushort _lastButtons;
		private long _nextRepeat;

		internal RepairWindow()
		{
			(_baseUrl, _language) = ReadAgentSettings();
			Title = CoreText.Get(_language, "repair.title");
			WindowStyle = WindowStyle.None;
			WindowState = WindowState.Maximized;
			ResizeMode = ResizeMode.NoResize;
			Topmost = true;
			Background = Brushes.Black;
			Cursor = Cursors.None;

			double scale = Math.Clamp(SystemParameters.PrimaryScreenHeight / 1080.0, 0.75, 2.0);
			var column = new StackPanel { Width = 900 * scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
			column.Children.Add(new TextBlock
			{
				Text = CoreText.Get(_language, "repair.title"), FontSize = 52 * scale, Foreground = new SolidColorBrush(Ink),
				FontFamily = new FontFamily("Segoe UI Semibold"), Margin = new Thickness(0, 0, 0, 12 * scale)
			});
			column.Children.Add(new TextBlock
			{
				Text = CoreText.Get(_language, "repair.subtitle"), FontSize = 24 * scale, Foreground = new SolidColorBrush(Muted),
				TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 28 * scale)
			});
			foreach (Item item in new[]
			{
				new Item("restart-plugins", "repair.restartPlugins"),
				new Item("reenable-access", "repair.reenableAccess"),
				new Item("update-decky", "repair.updateDecky"),
				new Item("rollback-steam", "repair.rollbackSteam"),
				new Item("update-steam", "repair.updateSteam"),
				new Item("continue", "repair.continue"),
				new Item("close", "repair.close"),
			})
			{
				var label = new TextBlock { Text = CoreText.Get(_language, item.TextKey), FontSize = 28 * scale, Foreground = new SolidColorBrush(Ink), VerticalAlignment = VerticalAlignment.Center };
				var box = new Border
				{
					Child = label, CornerRadius = new CornerRadius(14 * scale), BorderThickness = new Thickness(3 * scale),
					Padding = new Thickness(28 * scale, 16 * scale, 28 * scale, 16 * scale), Margin = new Thickness(0, 0, 0, 12 * scale)
				};
				int index = _rows.Count;
				box.MouseLeftButtonUp += (_, _) => { _selected = index; Paint(); Activate(_rows[index].Item); };
				_rows.Add((item, box, label));
				column.Children.Add(box);
			}
			_status.FontSize = 24 * scale; _status.Foreground = new SolidColorBrush(Ink); _status.TextWrapping = TextWrapping.Wrap;
			_status.Margin = new Thickness(0, 16 * scale, 0, 0); _status.MinHeight = 64 * scale;
			column.Children.Add(_status);
			_hint.Text = CoreText.Get(_language, "repair.hint"); _hint.FontSize = 20 * scale; _hint.Foreground = new SolidColorBrush(Muted);
			_hint.Margin = new Thickness(0, 12 * scale, 0, 0);
			column.Children.Add(_hint);
			Content = new Grid { Background = Brushes.Black, Children = { column } };
			Paint();

			PreviewKeyDown += OnKey;
			_pad.Tick += (_, _) => PollPad();
			Loaded += async (_, _) => { Activate(); Focus(); _pad.Start(); await ShowInitialStateAsync(); };
			Closed += (_, _) => { _pad.Stop(); _http.Dispose(); };
		}

		private void Paint()
		{
			for (int i = 0; i < _rows.Count; i++)
			{
				bool on = i == _selected;
				_rows[i].Box.Background = new SolidColorBrush(on ? Gunmetal : Colors.Black);
				_rows[i].Box.BorderBrush = new SolidColorBrush(on ? Crimson : Stroke);
				_rows[i].Label.Opacity = _busy && !on ? 0.5 : 1;
			}
		}

		private void Move(int delta)
		{
			if (_busy) return;
			_selected = (_selected + delta + _rows.Count) % _rows.Count;
			Paint();
		}

		private void OnKey(object sender, KeyEventArgs e)
		{
			switch (e.Key)
			{
				case Key.Up: case Key.Left: Move(-1); e.Handled = true; break;
				case Key.Down: case Key.Right: case Key.Tab: Move(1); e.Handled = true; break;
				case Key.Enter: case Key.Space: Activate(_rows[_selected].Item); e.Handled = true; break;
				case Key.Escape: case Key.Back: Close(); e.Handled = true; break;
			}
		}

		private void PollPad()
		{
			if (!IsActive) { _lastButtons = 0; return; }
			ushort buttons = 0; short stickY = 0;
			for (uint index = 0; index < 4; index++)
			{
				var reading = XInputPad.Read(index);
				if (!reading.Connected) continue;
				buttons |= reading.Buttons;
				if (Math.Abs((int)reading.LeftY) > Math.Abs((int)stickY)) stickY = reading.LeftY;
			}
			if (stickY > 16000) buttons |= XInputPad.DpadUp;
			if (stickY < -16000) buttons |= XInputPad.DpadDown;
			ushort pressed = (ushort)(buttons & ~_lastButtons);
			long now = Environment.TickCount64;
			bool repeat = (buttons & (XInputPad.DpadUp | XInputPad.DpadDown)) != 0 && now >= _nextRepeat;
			if ((pressed & XInputPad.DpadUp) != 0 || (repeat && (buttons & XInputPad.DpadUp) != 0)) { Move(-1); _nextRepeat = now + ((pressed & XInputPad.DpadUp) != 0 ? 400 : 140); }
			else if ((pressed & XInputPad.DpadDown) != 0 || (repeat && (buttons & XInputPad.DpadDown) != 0)) { Move(1); _nextRepeat = now + ((pressed & XInputPad.DpadDown) != 0 ? 400 : 140); }
			if ((pressed & XInputPad.A) != 0) Activate(_rows[_selected].Item);
			if ((pressed & XInputPad.B) != 0) Close();
			_lastButtons = buttons;
		}

		private async Task ShowInitialStateAsync()
		{
			JsonElement? state = await GetStateAsync();
			if (state is null) { _status.Text = CoreText.Get(_language, "repair.agentDown"); return; }
			if (state.Value.TryGetProperty("rollbackSuggested", out JsonElement suggested) && suggested.ValueKind == JsonValueKind.True)
			{
				_status.Text = CoreText.Get(_language, "repair.rollbackSuggested");
				_selected = _rows.FindIndex(row => row.Item.Action == "rollback-steam");
				Paint();
			}
		}

		private async void Activate(Item item)
		{
			if (_busy) return;
			if (item.Action == "close") { Close(); return; }
			_busy = true; Paint();
			_status.Text = CoreText.Get(_language, "repair.working");
			try
			{
				// "Update Steam now" passes the agent's Steam update gate: it may ask for one confirmation (Core
				// cannot tell whether the new Steam works with Decky) or two (it knows it does not). Each is
				// another press of A on the same row.
				string confirm = item.Action == "update-steam" ? "&confirm=" + _steamConfirmations.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
				using var start = await _http.PostAsync($"{_baseUrl}/core/repair/run?action={Uri.EscapeDataString(item.Action)}{confirm}", null);
				if (!start.IsSuccessStatusCode) { _status.Text = CoreText.Format(_language, "repair.failed", "HTTP " + (int)start.StatusCode); return; }
				if (item.Action == "update-steam")
				{
					string? ask = null;
					try
					{
						using JsonDocument answer = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
						if (answer.RootElement.TryGetProperty("confirm", out JsonElement code) && code.ValueKind == JsonValueKind.String) ask = code.GetString();
					}
					catch (JsonException) { }
					if (ask is not null)
					{
						_steamConfirmations = Math.Min(_steamConfirmations + 1, 2);
						_status.Text = CoreText.Get(_language, ask switch
						{
							"confirm-incompatible" => "repair.confirmIncompatible",
							"confirm-incompatible-again" => "repair.confirmIncompatibleAgain",
							_ => "repair.confirmCantTell",
						});
						return;
					}
					_steamConfirmations = 0;
				}
				if (item.Action is "reenable-access" or "rollback-steam" or "update-steam")
				{
					// These restart Steam, and Steam counts this window as a running game:
					// close it and let the agent finish. Its result appears over Big Picture.
					await Task.Delay(2500);
					Close();
					return;
				}
				// Updating Steam can take several minutes; the agent finishes it even if this window closes.
				DateTime giveUp = DateTime.UtcNow.AddMinutes(12);
				while (DateTime.UtcNow < giveUp)
				{
					await Task.Delay(1000);
					JsonElement? state = await GetStateAsync();
					if (state is null) continue;
					if (state.Value.TryGetProperty("busy", out JsonElement busy) && busy.ValueKind == JsonValueKind.True) continue;
					string key = state.Value.TryGetProperty("messageKey", out JsonElement message) && message.ValueKind == JsonValueKind.String ? message.GetString() ?? "repair.done" : "repair.done";
					string detail = state.Value.TryGetProperty("detail", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "";
					_status.Text = key == "repair.failed" ? CoreText.Format(_language, key, detail) : CoreText.Get(_language, key);
					if (key == "repair.stillBroken" && _selected < _rows.Count - 2) _selected++;
					return;
				}
				_status.Text = CoreText.Get(_language, "repair.working");
			}
			catch (Exception)
			{
				_status.Text = CoreText.Get(_language, "repair.agentDown");
			}
			finally { _busy = false; Paint(); }
		}

		private async Task<JsonElement?> GetStateAsync()
		{
			try
			{
				string text = await _http.GetStringAsync($"{_baseUrl}/core/repair/state");
				using JsonDocument document = JsonDocument.Parse(text);
				return document.RootElement.Clone();
			}
			catch { return null; }
		}

		private static (string BaseUrl, string? Language) ReadAgentSettings()
		{
			int port = 47991; string? language = null;
			try
			{
				string path = AppPaths.Create().ConfigPath;
				using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));
				if (config.RootElement.TryGetProperty("safety", out JsonElement safety) && safety.TryGetProperty("apiPort", out JsonElement value)
					&& value.TryGetInt32(out int configured) && configured is > 0 and < 65536) port = configured;
				if (config.RootElement.TryGetProperty("language", out JsonElement lang) && lang.ValueKind == JsonValueKind.String) language = lang.GetString();
			}
			catch { }
			return ($"http://127.0.0.1:{port}", language);
		}
	}
}
