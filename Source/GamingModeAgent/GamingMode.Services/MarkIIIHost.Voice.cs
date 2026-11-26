using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GamingMode.Services;

// Core Voice in the agent: the microphone mute (GET /voice/state, POST /voice/mic) and the Xbox
// party button (POST /voice/party), for the Voice section of Quick Access > Core and the top-bar
// badge. Rules in CoreVoice.cs; Windows calls in CoreVoice.Windows.cs.
//
// Mute: Core mutes the Console and Communications default microphones and restores each exactly.
// A new default while muted is muted too (Core Audio's device notification, plus a check every five
// seconds and on resume); an unmute from outside Core ends Core's mute rather than being fought.
// State survives restarts in mic-mute.json; Windows itself keeps each endpoint's mute across sleep.
//
// Party: Microsoft offers no API to read or join parties, so Core opens the Game Bar (Win+G, or a
// configured ms-gamebar: URI) and, when it closes, brings Big Picture back if it was in front.
internal sealed partial class MarkIIIHost
{
	private readonly object _voiceSync = new();
	private string _micPath = "";
	private MicMuteState _mic = new();
	private VoiceHotkey? _hotkey;
	private WindowsMicrophone.DeviceWatcher? _deviceWatcher;
	private int _watchGeneration;
	private int _reconcileQueued;

	// ---------- lifetime ----------

	private void StartVoice()
	{
		_micPath = System.IO.Path.Combine(_paths.ConfigDirectory, "mic-mute.json");
		lock (_voiceSync) _mic = MicMutePolicy.Load(_micPath);
		try { _deviceWatcher = new WindowsMicrophone.DeviceWatcher(QueueMicReconcile); }
		catch (Exception error) { _logger.Error("Voice: microphone change notifications are unavailable; Core checks every few seconds instead.", error); }
		_hotkey = new VoiceHotkey(() => ChangeMic("toggle", "hotkey"), _logger.Info);
		ApplyMicHotkey();
		ReconcileMic("start");
	}

	private void StopVoice()
	{
		Interlocked.Increment(ref _watchGeneration);
		try { _deviceWatcher?.Dispose(); } catch { }
		try { _hotkey?.Dispose(); } catch { }
	}

	// Every five seconds from the Mark III loop: cheap when Core's mute is off.
	private void CheckVoice()
	{
		ApplyMicHotkey();
		bool muted;
		lock (_voiceSync) muted = _mic.CoreMuted;
		if (muted) ReconcileMic("check");
	}

	private void ApplyMicHotkey()
	{
		string? combo = _store.LoadConfig().Gaming.MicMuteHotkey;
		_hotkey?.Apply(combo);
	}

	// ---------- microphone ----------

	// Device notifications arrive in bursts (a headset adds two endpoints and changes two
	// defaults); one reconcile after the burst is enough.
	private void QueueMicReconcile()
	{
		if (Interlocked.Exchange(ref _reconcileQueued, 1) == 1) return;
		_ = Task.Run(async () =>
		{
			await Task.Delay(400).ConfigureAwait(false);
			Interlocked.Exchange(ref _reconcileQueued, 0);
			try { ReconcileMic("device change"); }
			catch (Exception error) { _logger.Error("Voice: microphone reconcile after a device change failed.", error); }
		});
	}

	private void ReconcileMic(string why)
	{
		lock (_voiceSync)
		{
			if (!_mic.CoreMuted) return;
			IReadOnlyList<MicEndpoint> endpoints = WindowsMicrophone.Read();
			Commit(MicMutePolicy.Reconcile(_mic, endpoints), why);
		}
	}

	private (MicState State, bool CoreMuted, int Defaults) ChangeMic(string action, string from)
	{
		lock (_voiceSync)
		{
			IReadOnlyList<MicEndpoint> endpoints = WindowsMicrophone.Read();
			MicPlan plan = action switch
			{
				"mute" => MicMutePolicy.Mute(_mic, endpoints),
				"unmute" => MicMutePolicy.Unmute(_mic, endpoints),
				_ => MicMutePolicy.Toggle(_mic, endpoints),
			};
			Commit(plan, from);
			_logger.Info($"Voice: microphone {(_mic.CoreMuted ? "muted" : "unmuted")} from {from} ({plan.Changes.Count} endpoint(s) changed).");
			return MicView();
		}
	}

	// Caller holds _voiceSync.
	private void Commit(MicPlan plan, string why)
	{
		foreach (MicChange change in plan.Changes)
		{
			bool ok = false;
			try { ok = WindowsMicrophone.SetMute(change.Id, change.Mute); }
			catch (Exception error) { _logger.Error($"Voice: could not {(change.Mute ? "mute" : "unmute")} a microphone.", error); }
			if (!ok) _logger.Info($"Voice: Windows refused to {(change.Mute ? "mute" : "unmute")} a microphone ({why}).");
		}
		if (plan.Log is not null) _logger.Info($"Voice ({why}): {plan.Log}");
		if (plan.Next == _mic) return;
		_mic = plan.Next;
		try { MicMutePolicy.Save(_micPath, _mic); }
		catch (Exception error) { _logger.Error("Voice: microphone state could not be saved.", error); }
	}

	private (MicState State, bool CoreMuted, int Defaults) MicView()
	{
		IReadOnlyList<MicEndpoint> endpoints;
		try { endpoints = WindowsMicrophone.Read(); }
		catch (Exception error) { _logger.Error("Voice: microphones could not be read.", error); endpoints = Array.Empty<MicEndpoint>(); }
		bool coreMuted;
		lock (_voiceSync) coreMuted = _mic.CoreMuted;
		return (MicMutePolicy.Observe(endpoints), coreMuted, endpoints.Count(endpoint => endpoint.IsDefault));
	}

	private string? MicCode()
	{
		try { return MicMutePolicy.Code(MicMutePolicy.Observe(WindowsMicrophone.Read())); }
		catch { return null; }
	}

	// ---------- Xbox party ----------

	private async Task<PartyLaunchChoice> OpenPartyAsync()
	{
		// Quick Access closes first (the plugin does it before calling); give Steam a moment.
		await Task.Delay(250).ConfigureAwait(false);
		GamingSettings gaming = _store.LoadConfig().Gaming;
		bool? interactive = WindowsGameBar.Interactive();
		PartyLaunchChoice choice = PartyLaunchPolicy.Choose(new PartyLaunchInputs
		{
			GameBarInstalled = WindowsGameBar.Installed(),
			GameBarInteractive = interactive,
			ProtectedGame = _antiCheat.Engaged,
			OverrideUri = gaming.VoicePartyUri,
		});
		bool steamInFront = OverlayWindowTools.IsSteamForeground();
		bool sent = false;
		switch (choice.Method)
		{
			case PartyLaunchMethod.WinG:
				sent = WindowsXboxGameBar.SendShortcut();
				break;
			case PartyLaunchMethod.Uri:
				try { using (Process.Start(new ProcessStartInfo(choice.Uri!) { UseShellExecute = true })) { } sent = true; }
				catch (Exception error) { _logger.Error("Voice: the configured Game Bar link could not be opened; trying Win+G.", error); sent = WindowsXboxGameBar.SendShortcut(); }
				break;
		}
		_logger.Info($"Voice: Xbox party button: {choice.Method} ({choice.Reason}){(choice.Method is PartyLaunchMethod.WinG or PartyLaunchMethod.Uri ? sent ? ", sent" : ", refused by Windows" : "")}; Big Picture in front: {steamInFront}.");
		if (sent) StartGameBarWatch(steamInFront && RunningAppId() == 0);
		return sent || choice.Method is PartyLaunchMethod.AlreadyOpen or PartyLaunchMethod.Unavailable ? choice : choice with { Method = PartyLaunchMethod.Unavailable, Reason = "inputRefused" };
	}

	// While the bar is open Steam Input may treat it as a desktop window; when it closes, Big
	// Picture gets the foreground back so the controller drives it again.
	private void StartGameBarWatch(bool refocusOnClose)
	{
		int generation = Interlocked.Increment(ref _watchGeneration);
		_ = Task.Run(async () =>
		{
			var watch = new GameBarWatch(DateTimeOffset.Now, refocusOnClose);
			try
			{
				while (generation == Volatile.Read(ref _watchGeneration))
				{
					await Task.Delay(GameBarWatchPolicy.PollEvery).ConfigureAwait(false);
					(GameBarWatch next, GameBarWatchAction action) = GameBarWatchPolicy.Step(watch, DateTimeOffset.Now, WindowsGameBar.Interactive(),
						OverlayWindowTools.IsSteamForeground(), GameRunning());
					if (next.SeenOpen && !watch.SeenOpen) _logger.Info("Voice: Game Bar is open.");
					watch = next;
					if (action == GameBarWatchAction.None) continue;
					if (action == GameBarWatchAction.Refocus)
					{
						bool back = OverlayWindowTools.ActivateSteam(out string report);
						_logger.Info($"Voice: Game Bar closed; Big Picture {(back ? "is back in front" : "could not be brought back")} ({report}).");
					}
					else _logger.Info(watch.SeenOpen ? "Voice: Game Bar closed." : "Voice: Game Bar did not report opening; nothing to hand back.");
					return;
				}
			}
			catch (Exception error) { _logger.Error("Voice: Game Bar watch failed; Big Picture is not brought back automatically.", error); }
		});
	}

	// ---------- HTTP ----------

	private object VoiceState()
	{
		(MicState state, bool coreMuted, int defaults) = MicView();
		return new
		{
			mic = new { state = MicMutePolicy.Code(state), coreMuted, defaults, hotkey = _hotkey?.Combo },
			gameBar = new { installed = WindowsGameBar.Installed(), open = WindowsGameBar.Interactive() },
		};
	}

	private void MapVoice(WebApplication app)
	{
		app.MapGet("/voice/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(VoiceState()) : Results.StatusCode(403)));
		app.MapPost("/voice/mic", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string action = context.Request.Query["action"].ToString();
			if (action is not ("mute" or "unmute" or "toggle")) return Results.Json(new { ok = false }, statusCode: 400);
			ChangeMic(action, "Quick Access");
			return Results.Json(VoiceState());
		}));
		app.MapPost("/voice/party", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			PartyLaunchChoice choice = await OpenPartyAsync().ConfigureAwait(false);
			bool ok = choice.Method is PartyLaunchMethod.WinG or PartyLaunchMethod.Uri or PartyLaunchMethod.AlreadyOpen;
			return Results.Json(new { ok, method = choice.Method.ToString(), reason = choice.Reason });
		}));
	}
}
