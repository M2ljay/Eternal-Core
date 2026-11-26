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
// Party: Microsoft offers no API to read or join parties, so Core opens the Game Bar and, when it
// closes, brings Big Picture back if it was in front. Win+G only with no game running; with a game
// (or when Core cannot tell) the ms-gamebar: URI, never a keystroke (PartyLaunchPolicy).
//
// Calls mic (POST /voice/calls-mic) and mute scope (POST /voice/mute-scope): the chosen device is
// made the Communications default through IDefaultMicSetter (IPolicyConfig; see
// PolicyConfigDefaultMic) and made so again when it reappears, unless the user changed the default
// in Windows meanwhile (CallsMicPolicy). Settings in voice-settings.json, written only here. An
// absent calls mic is information only: no notice, no Health alert.
internal sealed partial class MarkIIIHost
{
	private readonly object _voiceSync = new();
	private string _micPath = "";
	private string _voicePath = "";
	private MicMuteState _mic = new();
	private VoiceSettings _voice = new();
	private readonly IDefaultMicSetter _micSetter = new PolicyConfigDefaultMic();
	// The last default change Windows refused ("refused" or "unavailable"), for Quick Access; not saved.
	private string? _callsMicError;
	private VoiceHotkey? _hotkey;
	private WindowsMicrophone.DeviceWatcher? _deviceWatcher;
	private int _watchGeneration;
	private int _reconcileQueued;
	private DateTimeOffset _partySentAt = DateTimeOffset.MinValue;

	// ---------- lifetime ----------

	private void StartVoice()
	{
		_micPath = System.IO.Path.Combine(_paths.ConfigDirectory, "mic-mute.json");
		_voicePath = System.IO.Path.Combine(_paths.ConfigDirectory, "voice-settings.json");
		lock (_voiceSync) { _mic = MicMutePolicy.Load(_micPath); _voice = CallsMicPolicy.Load(_voicePath); }
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

	// Every five seconds from the Mark III loop: cheap when Core's mute is off, nothing is pending and
	// no calls mic is chosen.
	private void CheckVoice()
	{
		ApplyMicHotkey();
		bool active;
		lock (_voiceSync) active = !_mic.IsEmpty || CallsMicChosen;
		if (active) ReconcileMic("check");
	}

	// Caller holds _voiceSync.
	private bool CallsMicChosen => !string.IsNullOrEmpty(_voice.CallsMicId);

	private MicScope Scope => CallsMicPolicy.Scope(_voice);

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

	// The calls mic first (it may change the defaults), then the mute on what the defaults are now.
	private void ReconcileMic(string why)
	{
		lock (_voiceSync)
		{
			if (_mic.IsEmpty && !CallsMicChosen) return;
			IReadOnlyList<MicEndpoint> endpoints = WindowsMicrophone.Read();
			if (CallsMicChosen && ApplyCallsMic(CallsMicPolicy.Step(_voice, endpoints), why)) endpoints = WindowsMicrophone.Read();
			if (!_mic.IsEmpty) Commit(MicMutePolicy.Reconcile(_mic, endpoints, Scope), why);
		}
	}

	// Caller holds _voiceSync. Sets the planned defaults, saves the settings when they changed, and
	// says whether any default was changed (so the caller reads the devices again).
	private bool ApplyCallsMic(CallsMicPlan plan, string why)
	{
		bool failed = false;
		foreach (DefaultChange change in plan.Changes)
		{
			DefaultSetResult result;
			try { result = _micSetter.SetDefault(change.Id, change.Role); }
			catch (Exception error) { _logger.Error("Voice: setting the default microphone failed.", error); result = DefaultSetResult.Unavailable; }
			if (result == DefaultSetResult.Done) continue;
			failed = true;
			_callsMicError = result == DefaultSetResult.Unavailable ? "unavailable" : "refused";
			_logger.Info($"Voice: Windows did not set the calls mic as the {change.Role} default ({result}, {why}); Core leaves Windows' default as it is.");
		}
		if (!failed && plan.Changes.Count > 0) _callsMicError = null;
		if (plan.Log is not null) _logger.Info($"Voice ({why}): {plan.Log}");
		VoiceSettings next = CallsMicPolicy.AfterApply(plan.Next, failed);
		if (next != _voice) { _voice = next; PersistVoice(); }
		return plan.Changes.Count > 0;
	}

	private void PersistVoice()
	{
		try { CallsMicPolicy.Save(_voicePath, _voice); }
		catch (Exception error) { _logger.Error("Voice: voice settings could not be saved.", error); }
	}

	// Quick Access picks a calls mic (empty id: back to Windows' own choice). "ok", "notFound"
	// (not connected), "refused" or "unavailable" (Windows did not set it; nothing crashes).
	private string ChooseCallsMic(string id, bool everything)
	{
		lock (_voiceSync)
		{
			if (string.IsNullOrEmpty(id))
			{
				_voice = CallsMicPolicy.Forget(_voice);
				_callsMicError = null;
				PersistVoice();
				_logger.Info("Voice: calls mic forgotten; Windows' own default is used.");
				return "ok";
			}
			IReadOnlyList<MicEndpoint> endpoints = WindowsMicrophone.Read();
			CallsMicPlan? plan = CallsMicPolicy.Choose(_voice, endpoints, id, everything);
			if (plan is null) return "notFound";
			_callsMicError = null;
			bool changed = ApplyCallsMic(plan, "Quick Access");
			// While Core's mute is on, the new default is muted too (recorded like any new default).
			if (changed && !_mic.IsEmpty) Commit(MicMutePolicy.Reconcile(_mic, WindowsMicrophone.Read(), Scope), "calls mic");
			return _voice.Failed ? _callsMicError ?? "refused" : "ok";
		}
	}

	private void SetMuteScope(MicScope scope)
	{
		lock (_voiceSync)
		{
			if (Scope == scope) return;
			_voice = _voice with { MuteScope = MicMutePolicy.ScopeCode(scope) };
			PersistVoice();
			MicPlan plan = MicMutePolicy.ChangeScope(_mic, WindowsMicrophone.Read(), scope);
			Commit(plan, "mute scope");
			_logger.Info($"Voice: mute scope set to {MicMutePolicy.ScopeCode(scope)}.");
		}
	}

	private (MicState State, bool CoreMuted, int Defaults) ChangeMic(string action, string from)
	{
		lock (_voiceSync)
		{
			IReadOnlyList<MicEndpoint> endpoints = WindowsMicrophone.Read();
			MicScope scope = Scope;
			MicPlan plan = action switch
			{
				"mute" => MicMutePolicy.Mute(_mic, endpoints, scope),
				"unmute" => MicMutePolicy.Unmute(_mic, endpoints, scope),
				_ => MicMutePolicy.Toggle(_mic, endpoints, scope),
			};
			Commit(plan, from);
			_logger.Info($"Voice: microphone {(_mic.CoreMuted ? "muted" : "unmuted")} from {from} ({plan.Changes.Count} endpoint(s) changed).");
			return MicView();
		}
	}

	// Caller holds _voiceSync. A mute is saved before it is applied, so a crash in between still
	// leaves the record of each microphone's own setting; anything else is saved after, with pending
	// restores that Windows refused kept for the next check.
	private void Commit(MicPlan plan, string why)
	{
		MicMuteState previous = _mic;
		if (plan.Next.CoreMuted && plan.Next != previous) Persist(plan.Next);
		var failed = new List<string>();
		foreach (MicChange change in plan.Changes)
		{
			bool ok = false;
			try { ok = WindowsMicrophone.SetMute(change.Id, change.Mute); }
			catch (Exception error) { _logger.Error($"Voice: could not {(change.Mute ? "mute" : "unmute")} a microphone.", error); }
			if (!ok) { failed.Add(change.Id); _logger.Info($"Voice: Windows refused to {(change.Mute ? "mute" : "unmute")} a microphone ({why})."); }
		}
		if (plan.Log is not null) _logger.Info($"Voice ({why}): {plan.Log}");
		MicMuteState next = MicMutePolicy.KeepUnrestored(plan.Next, previous, failed);
		if (next == previous) return;
		_mic = next;
		if (!next.CoreMuted) Persist(next);
	}

	private void Persist(MicMuteState state)
	{
		try { MicMutePolicy.Save(_micPath, state); }
		catch (Exception error) { _logger.Error("Voice: microphone state could not be saved.", error); }
	}

	private (MicState State, bool CoreMuted, int Defaults) MicView()
	{
		var view = MicViewFull();
		return (view.State, view.CoreMuted, view.Defaults);
	}

	private (MicState State, bool CoreMuted, int Defaults, string? Communications, int Pending) MicViewFull()
	{
		var view = MicSnapshot();
		return (view.State, view.CoreMuted, view.Endpoints.Count(endpoint => endpoint.IsDefault), MicMutePolicy.CommunicationsCode(view.Endpoints), view.Pending);
	}

	private (IReadOnlyList<MicEndpoint> Endpoints, MicState State, bool CoreMuted, int Pending, VoiceSettings Settings, string? Error) MicSnapshot()
	{
		IReadOnlyList<MicEndpoint> endpoints;
		try { endpoints = WindowsMicrophone.Read(); }
		catch (Exception error) { _logger.Error("Voice: microphones could not be read.", error); endpoints = Array.Empty<MicEndpoint>(); }
		bool coreMuted; int pending; VoiceSettings settings; string? failure;
		lock (_voiceSync) { coreMuted = _mic.CoreMuted; pending = _mic.Pending.Count; settings = _voice; failure = _callsMicError; }
		return (endpoints, MicMutePolicy.Observe(endpoints, CallsMicPolicy.Scope(settings)), coreMuted, pending, settings, failure);
	}

	// For the top-bar mark: the state under the chosen mute scope.
	private string? MicCode()
	{
		try
		{
			MicScope scope;
			lock (_voiceSync) scope = Scope;
			return MicMutePolicy.Code(MicMutePolicy.Observe(WindowsMicrophone.Read(), scope));
		}
		catch { return null; }
	}

	// For Core Health: the mute state, and the calls mic's name and status ("none", "active",
	// "absent", "changed", "failed"). Windows exposes no battery level for a USB audio receiver
	// (see CORE-VOICE.md), so none is reported.
	private (string? State, string? CallsMicName, string? CallsMicStatus) MicHealth()
	{
		try
		{
			var view = MicSnapshot();
			string status = CallsMicPolicy.Status(view.Settings, view.Endpoints);
			string? name = CallsMicPolicy.Find(view.Settings, view.Endpoints)?.Name ?? view.Settings.CallsMicName;
			return (MicMutePolicy.Code(view.State), status == "none" ? null : name, status);
		}
		catch { return (null, null, null); }
	}

	// ---------- Xbox party ----------

	private async Task<PartyLaunchChoice> OpenPartyAsync()
	{
		// Quick Access closes first (the plugin does it before calling); give Steam a moment.
		await Task.Delay(250).ConfigureAwait(false);
		GamingSettings gaming = _store.LoadConfig().Gaming;
		bool steamInFront = OverlayWindowTools.IsSteamForeground();
		var inputs = new PartyLaunchInputs
		{
			GameBarInstalled = WindowsGameBar.Installed(),
			GameBarInteractive = WindowsGameBar.Interactive(),
			ProtectedGame = _antiCheat.Engaged,
			GameRunning = PartyGameRunning(steamInFront),
			RecentlySent = DateTimeOffset.Now - _partySentAt < PartyLaunchPolicy.Cooldown,
			OverrideUri = gaming.VoicePartyUri,
		};
		PartyLaunchChoice choice = PartyLaunchPolicy.Choose(inputs);
		bool sent = false;
		if (choice.Method == PartyLaunchMethod.Uri)
		{
			try { using (Process.Start(new ProcessStartInfo(choice.Uri!) { UseShellExecute = true })) { } sent = true; }
			catch (Exception error)
			{
				_logger.Error("Voice: the Game Bar link could not be opened.", error);
				choice = PartyLaunchPolicy.AfterUriFailure(inputs);
			}
		}
		if (choice.Method == PartyLaunchMethod.WinG) sent = WindowsXboxGameBar.SendShortcut();
		_logger.Info($"Voice: Xbox party button: {choice.Method} ({choice.Reason}){(choice.Method is PartyLaunchMethod.WinG or PartyLaunchMethod.Uri ? sent ? ", sent" : ", refused by Windows" : "")}; game running: {inputs.GameRunning?.ToString() ?? "unknown"}; Big Picture in front: {steamInFront}.");
		if (sent) { _partySentAt = DateTimeOffset.Now; StartGameBarWatch(steamInFront && inputs.GameRunning == false); }
		return sent || choice.Method is PartyLaunchMethod.AlreadyOpen or PartyLaunchMethod.Unavailable ? choice : choice with { Method = PartyLaunchMethod.Unavailable, Reason = "inputRefused" };
	}

	// For the party button: a game counts as running when anti-cheat safe mode sees one, when Steam
	// reports one, or when Core cannot tell (Steam's registry value unreadable, or something other than
	// Big Picture in front, which may be a game started outside Steam).
	private bool? PartyGameRunning(bool steamInFront)
	{
		if (_antiCheat.Engaged) return true;
		uint? running = SteamRunningAppIdOrNull();
		if (running is null) return null;
		if (running != 0) return true;
		return steamInFront ? false : null;
	}

	private static uint? SteamRunningAppIdOrNull()
	{
		try
		{
			using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			return key?.GetValue("RunningAppID") switch { int value => unchecked((uint)value), long value => (uint)value, _ => null };
		}
		catch { return null; }
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

	private object VoiceState(string? result = null)
	{
		var view = MicSnapshot();
		IReadOnlyList<MicEndpoint> endpoints = view.Endpoints;
		string status = CallsMicPolicy.Status(view.Settings, endpoints);
		MicEndpoint? callsMic = CallsMicPolicy.Find(view.Settings, endpoints);
		return new
		{
			mic = new
			{
				state = MicMutePolicy.Code(view.State), coreMuted = view.CoreMuted, defaults = endpoints.Count(endpoint => endpoint.IsDefault),
				communications = MicMutePolicy.CommunicationsCode(endpoints), pending = view.Pending, hotkey = _hotkey?.Combo,
				scope = MicMutePolicy.ScopeCode(CallsMicPolicy.Scope(view.Settings)),
			},
			// Every connected capture device, for the calls mic picker.
			devices = endpoints.Take(16).Select(endpoint => new { id = endpoint.Id, name = endpoint.Name, console = endpoint.IsConsole, communications = endpoint.IsCommunications, muted = endpoint.Muted }).ToArray(),
			callsMic = new
			{
				id = callsMic?.Id ?? view.Settings.CallsMicId, name = callsMic?.Name ?? view.Settings.CallsMicName, status,
				everything = view.Settings.UseForEverything, error = status == "failed" ? view.Error ?? "refused" : null,
			},
			result,
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
		// id: an endpoint id from /voice/state's devices, or empty to go back to Windows' own choice.
		app.MapPost("/voice/calls-mic", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string id = context.Request.Query["id"].ToString();
			if (id.Length > 200 || id.Any(char.IsControl)) return Results.Json(new { ok = false }, statusCode: 400);
			bool everything = string.Equals(context.Request.Query["everything"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
			string result = ChooseCallsMic(id, everything);
			return Results.Json(VoiceState(result));
		}));
		app.MapPost("/voice/mute-scope", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string scope = context.Request.Query["scope"].ToString();
			if (scope is not ("all" or "calls")) return Results.Json(new { ok = false }, statusCode: 400);
			SetMuteScope(MicMutePolicy.ParseScope(scope));
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
