using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Shared;

namespace GamingMode.Services;

// Batch 2 in the agent (docs/eternal/BATCH-2.md):
// - B8 Home Assistant bridge: Core's state over MQTT with discovery (rules in HomeAssistantBridge.cs, client in MqttWire.cs).
// - B17 automations: trigger -> action rules (Shared/CoreAutomations.cs), every TV wake through TvWakeCoordinator.
// - B13 sleep after streaming (StreamPower.cs): the PC sleeps a while after a handheld's stream ends, the TV left alone.
// - Swipe typing's on/off switch for Core's Decky plugin.
//
// Fail-soft like the rest of Mark III: every step catches and logs its own failures. The MQTT password is read from the
// DPAPI store only to build the CONNECT packet; it is never logged, never returned by an endpoint.
internal sealed partial class MarkIIIHost
{
	private readonly object _b2Sync = new();
	// Home Assistant bridge.
	private HaBridgeStatus _bridge = new(false, false, false, null, null, null, false);
	private MqttConnection? _mqtt;
	private string _mqttNode = HomeAssistantBridgePolicy.NodeId(Environment.MachineName);
	private string? _lastStatePayload;
	private bool? _haSleepMode;
	private string? _lastBridgeFailure;
	private readonly SemaphoreSlim _bridgeKick = new(0, 1);
	private CancellationTokenSource? _bridgeCycle;
	// Automations.
	private ControllerWatchState _controllers = ControllerWatchState.Empty;
	private DateTimeOffset _controllersQuietUntil = DateTimeOffset.Now + TimeSpan.FromSeconds(60);
	private DateTimeOffset? _lastTvWakeAt;
	private bool? _gameWas;
	private bool? _streamDetectedWas;
	private readonly SemaphoreSlim _automationRun = new(1, 1);
	private readonly List<(string Rule, string Trigger, DateTimeOffset At, bool Ok)> _automationRuns = new();
	// Sleep after streaming.
	private string _streamSleepPath = "";
	private StreamSleepState _streamSleep = new();
	private DateTimeOffset? _streamSleepAt;
	private DateTimeOffset? _steamInputAt;
	private volatile bool _sleepingForStream;
	// Cached observations.
	private (DateTimeOffset At, WakeOnLanInputs Inputs)? _wol;
	private (DateTimeOffset At, string State, string? App) _media = (DateTimeOffset.MinValue, "off", null);
	private (uint AppId, string? Name) _gameName;
	private static readonly Regex OnOff = new("^(true|false)$", RegexOptions.CultureInvariant);

	// ---------- lifetime ----------

	private void StartBatchTwo(CancellationToken stopping)
	{
		_streamSleepPath = System.IO.Path.Combine(_paths.ConfigDirectory, "stream-sleep.json");
		lock (_b2Sync) _streamSleep = StreamSleepPolicy.Load(_streamSleepPath) with { WasStreaming = false, ArmedAt = null, Warned = false };
		TvSettings tv = _store.LoadConfig().Gaming.Tv ?? new TvSettings();
		// The boot's own TV wake (WakeTvForBootAsync) counts for the cooldown: a controller seen right after must not repeat it.
		if (tv.Enabled && tv.TurnOnWithCore) _lastTvWakeAt = DateTimeOffset.Now;
		_ = Task.Run(() => BridgeLoopAsync(stopping));
	}

	private List<CoreAutomationRule> Rules() => CoreAutomations.Normalize(_store.LoadConfig().Gaming.Automations);

	// Every five seconds from the Mark III loop.
	private async Task CheckBatchTwoAsync()
	{
		DateTimeOffset now = DateTimeOffset.Now;
		List<CoreAutomationRule> rules = Rules();
		bool game = GameRunning();
		if (_gameWas is bool was && was != game) StartAutomations(game ? CoreAutomations.GameStart : CoreAutomations.GameStop);
		_gameWas = game;
		if (CoreAutomations.Matching(rules, CoreAutomations.ControllerConnected).Count > 0)
		{
			IReadOnlyList<ControllerSeen>? seen = WindowsControllers.Read();
			if (seen is not null)
			{
				(IReadOnlyList<string> connected, ControllerWatchState next) = ControllerWatch.Observe(_controllers, seen.Select(controller => controller.Key).ToList(), now, now < _controllersQuietUntil);
				_controllers = next;
				if (connected.Count > 0)
				{
					_logger.Info($"Automations: a controller connected ({string.Join(", ", seen.Where(controller => connected.Contains(controller.Key, StringComparer.OrdinalIgnoreCase)).Select(controller => controller.Name).Distinct())}).");
					StartAutomations(CoreAutomations.ControllerConnected);
				}
			}
		}
		else _controllers = ControllerWatchState.Empty;
		try { CheckStreamSleep(now); }
		catch (Exception error) { _logger.Error("Sleep after streaming: check failed this round.", error); }
		if (now - _media.At > TimeSpan.FromSeconds(10))
		{
			try { (string state, string? app) = await Batch2Probes.MediaAsync().ConfigureAwait(false); _media = (now, state, app); }
			catch (Exception error) { _media = (now, "off", null); _logger.Error("Home Assistant: media check failed this round.", error); }
		}
		await PublishStateAsync().ConfigureAwait(false);
	}

	// Called by CheckStream on every evaluation: a stream starting or ending is an automation trigger ("Keep TV out of
	// it" does not count, so a rule that switches it can never trigger itself).
	private void OnStreamEvaluated(StreamState state)
	{
		bool? before;
		lock (_b2Sync) { before = _streamDetectedWas; _streamDetectedWas = state.Detected; }
		if (before is bool was && was != state.Detected) StartAutomations(state.Detected ? CoreAutomations.StreamOn : CoreAutomations.StreamOff);
	}

	// ---------- automations ----------

	private void StartAutomations(string trigger)
	{
		List<CoreAutomationRule> rules;
		try { rules = CoreAutomations.Matching(Rules(), trigger).ToList(); }
		catch (Exception error) { _logger.Error("Automations: rules could not be read.", error); return; }
		if (rules.Count == 0) return;
		_ = Task.Run(async () =>
		{
			await _automationRun.WaitAsync().ConfigureAwait(false);
			try
			{
				foreach (CoreAutomationRule rule in rules)
				{
					bool ok = true;
					foreach (string action in rule.Actions)
					{
						try { ok &= await RunAutomationActionAsync(rule, trigger, action).ConfigureAwait(false); }
						catch (Exception error) { ok = false; _logger.Error($"Automations: \"{rule.Id}\" action {action} failed; the next one runs.", error); }
					}
					_logger.Info($"Automations: {CoreAutomations.Describe(rule)} — {(ok ? "done" : "partly done")}.");
					lock (_b2Sync)
					{
						_automationRuns.Insert(0, (rule.Id, trigger, DateTimeOffset.Now, ok));
						if (_automationRuns.Count > 10) _automationRuns.RemoveAt(_automationRuns.Count - 1);
					}
				}
			}
			finally { _automationRun.Release(); }
		});
	}

	private async Task<bool> RunAutomationActionAsync(CoreAutomationRule rule, string trigger, string action)
	{
		bool gaming = InGamingMode();
		StreamState stream; lock (_streamSync) stream = _stream;
		switch (action)
		{
			case CoreAutomations.TvWake:
				// The resume path wakes the TV itself (CoreAutomations.WakeTvOnResume): never a second wake for "wake".
				if (trigger == CoreAutomations.Wake) return true;
				if (!gaming) { _logger.Info("Automations: TV wake skipped (not in Gaming Mode)."); return true; }
				return await WakeTvCoordinatedAsync($"automation \"{rule.Id}\"").ConfigureAwait(false);
			case CoreAutomations.DisplayRestore or CoreAutomations.SoundRestore:
				if (!gaming || stream.Active) { _logger.Info($"Automations: {action} skipped ({(stream.Active ? "Stream mode is on" : "not in Gaming Mode")})."); return true; }
				(bool ok, string result) = await RunConsoleActionAsync(action).ConfigureAwait(false);
				if (!ok) _logger.Info($"Automations: {action}: {result}.");
				return ok || result is "not-saved" or "not-while-playing";
			case CoreAutomations.StreamStart or CoreAutomations.StreamStop:
				SetKeepTvOut(action == CoreAutomations.StreamStart, $"automation \"{rule.Id}\"");
				return true;
			case CoreAutomations.HaEvent:
				return await PublishEventAsync(rule.Event ?? trigger).ConfigureAwait(false);
		}
		return false;
	}

	// The one way Core wakes the TV outside power-on and resume: Gaming Mode only, TV control on, at most once per
	// TvWakeCoordinator.Cooldown together with those two, and still through TvController's Stream mode hold.
	private async Task<bool> WakeTvCoordinatedAsync(string why)
	{
		TvSettings tv = _store.LoadConfig().Gaming.Tv ?? new TvSettings();
		if (!tv.Enabled) { _logger.Info($"TV ({why}): TV control is off."); return true; }
		DateTimeOffset now = DateTimeOffset.Now;
		lock (_b2Sync)
		{
			if (!TvWakeCoordinator.ShouldSend(_lastTvWakeAt, now)) { _logger.Info($"TV ({why}): woken {(int)(now - _lastTvWakeAt!.Value).TotalSeconds} s ago; not sent twice."); return true; }
			_lastTvWakeAt = now;
		}
		TvResult result = await _tv.WakeAndSwitchAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
		_logger.Info($"TV ({why}): {result.Outcome}.");
		return result.Outcome is TvOutcome.Done or TvOutcome.StreamMode || result.Ok;
	}

	private void NoteTvWake() { lock (_b2Sync) _lastTvWakeAt = DateTimeOffset.Now; }

	private void SetKeepTvOut(bool on, string why)
	{
		lock (_streamSync)
		{
			if (_streamSettings.KeepTvOut == on) return;
			_streamSettings = _streamSettings with { KeepTvOut = on };
			PersistStreamSettings();
		}
		_logger.Info($"Stream mode: \"Keep TV out of it\" turned {(on ? "on" : "off")} by {why}.");
		CheckStream();
	}

	// Resume: the wake trigger, a quiet minute for the controller watch, the bridge reconnects.
	private void OnResumeBatchTwo()
	{
		_sleepingForStream = false;
		_controllersQuietUntil = DateTimeOffset.Now + TimeSpan.FromSeconds(60);
		lock (_b2Sync) _lastStatePayload = null;
		KickBridge();
		StartAutomations(CoreAutomations.Wake);
	}

	// ---------- sleep after streaming ----------

	private void CheckStreamSleep(DateTimeOffset now)
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		StreamState stream; lock (_streamSync) stream = _stream;
		bool handheld = stream.Detected && stream.Sources.Any(source => source is StreamSource.Apollo or StreamSource.Sunshine or StreamSource.SteamRemotePlay);
		DateTimeOffset? local = Batch2Probes.LastInput();
		lock (_b2Sync) if (_steamInputAt is { } steam && (local is null || steam > local)) local = steam;
		bool busy; lock (_sync) busy = _jobState.Busy;
		StreamSleepState state; lock (_b2Sync) state = _streamSleep;
		StreamSleepDecision decision = StreamSleepPolicy.Decide(new StreamSleepInputs
		{
			Now = now, Enabled = gaming.StreamSleepEnabled, DelayMinutes = gaming.StreamSleepMinutes, InGamingMode = InGamingMode(),
			Streaming = handheld, Source = handheld ? StreamModePolicy.Label(stream.Source) : null, LastLocalInput = local, Busy = busy,
		}, state);
		lock (_b2Sync)
		{
			_streamSleep = decision.Next;
			_streamSleepAt = decision.Next.ArmedAt is null ? null : decision.SleepAt;
		}
		bool persist = decision.Action is StreamSleepAction.Armed or StreamSleepAction.Disarmed or StreamSleepAction.Sleep;
		switch (decision.Action)
		{
			case StreamSleepAction.Armed:
				_logger.Info($"Sleep after streaming: {decision.Reason}; the PC sleeps at {decision.SleepAt:HH:mm} unless someone uses Core here.");
				break;
			case StreamSleepAction.Disarmed:
				_logger.Info($"Sleep after streaming: cancelled ({decision.Reason}).");
				break;
			case StreamSleepAction.Warn:
				string body = CoreText.Format(Language(), "streamsleep.soon", 1);
				lock (_batchSync) _pendingToast = ("streamsleep-" + now.ToUnixTimeSeconds(), body, false, now + TimeSpan.FromMinutes(1));
				_logger.Info("Sleep after streaming: one minute left; a Steam toast is waiting for Core's plugin.");
				break;
			case StreamSleepAction.Sleep:
				SleepForStream();
				break;
		}
		if (persist)
		{
			try { StreamSleepPolicy.Save(_streamSleepPath, decision.Next); }
			catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { _logger.Error("Sleep after streaming: state could not be saved.", error); }
		}
	}

	// Sleep, not shutdown. The TV is left alone on the way down and on the next wake (Stream mode's wake grace), because
	// the handheld that streamed is the likely one to wake the PC again (Moonlight's Wake-on-LAN).
	private void SleepForStream()
	{
		_logger.Info("Sleep after streaming: nobody used Core after the stream; the PC goes to sleep (games stay suspended in memory).");
		_sleepingForStream = true;
		lock (_streamSync)
		{
			_streamSettings = _streamSettings with { StreamingAtSleep = true };
			PersistStreamSettings();
		}
		_ = Task.Run(async () =>
		{
			await AnnounceSleepAsync().ConfigureAwait(false);
			if (!Batch2Probes.Sleep())
			{
				_sleepingForStream = false;
				_logger.Info("Sleep after streaming: Windows did not go to sleep (SetSuspendState refused).");
			}
		});
	}

	// "Sleep PC" from Home Assistant: an ordinary sleep (the TV turns off only if it shows Core, as usual).
	private void SleepFromHomeAssistant()
	{
		StreamState stream; lock (_streamSync) stream = _stream;
		bool busy; lock (_sync) busy = _jobState.Busy;
		string? refusal = HomeAssistantBridgePolicy.SleepRefusal(GameRunning(), stream.Detected, busy);
		if (refusal is not null) { _logger.Info($"Home Assistant: \"Sleep PC\" refused ({refusal})."); return; }
		_logger.Info("Home Assistant: \"Sleep PC\"; the PC goes to sleep.");
		_ = Task.Run(async () =>
		{
			await AnnounceSleepAsync().ConfigureAwait(false);
			if (!Batch2Probes.Sleep()) _logger.Info("Home Assistant: Windows did not go to sleep (SetSuspendState refused).");
		});
	}

	// ---------- Home Assistant bridge ----------

	private void KickBridge()
	{
		try { _bridgeCycle?.Cancel(); } catch (ObjectDisposedException) { }
		try { if (_bridgeKick.CurrentCount == 0) _bridgeKick.Release(); } catch (SemaphoreFullException) { }
	}

	private async Task WaitKickAsync(TimeSpan delay, CancellationToken stopping)
	{
		try { await _bridgeKick.WaitAsync(delay, stopping).ConfigureAwait(false); }
		catch (OperationCanceledException) { }
	}

	private async Task BridgeLoopAsync(CancellationToken stopping)
	{
		int failures = 0;
		while (!stopping.IsCancellationRequested)
		{
			try
			{
				GamingSettings gaming = _store.LoadConfig().Gaming;
				string? host = HomeAssistantBridgePolicy.CleanHost(gaming.HomeAssistantHost);
				int port = HomeAssistantBridgePolicy.CleanPort(gaming.HomeAssistantPort);
				bool saved = CoreSecretStore.HasPassword(_paths.ConfigDirectory);
				if (!gaming.HomeAssistantEnabled || host is null)
				{
					SetBridge(new HaBridgeStatus(gaming.HomeAssistantEnabled, host is not null, false, host, null, null, saved));
					failures = 0;
					await WaitKickAsync(TimeSpan.FromSeconds(30), stopping).ConfigureAwait(false);
					continue;
				}
				string node = HomeAssistantBridgePolicy.NodeId(Environment.MachineName);
				var options = new MqttConnection.Options(host, port, HomeAssistantBridgePolicy.ClientId(node), string.IsNullOrWhiteSpace(gaming.HomeAssistantUser) ? null : gaming.HomeAssistantUser.Trim(),
					CoreSecretStore.ReadPassword(_paths.ConfigDirectory), HomeAssistantBridgePolicy.KeepAliveSeconds,
					HomeAssistantBridgePolicy.AvailabilityTopic(node), "offline", true);
				(MqttConnection? connection, _, string? failure) = await MqttConnection.ConnectAsync(options, TimeSpan.FromSeconds(10), stopping).ConfigureAwait(false);
				if (connection is null)
				{
					SetBridge(new HaBridgeStatus(true, true, false, host, failure, DateTimeOffset.Now, saved));
					if (failure != _lastBridgeFailure) _logger.Info($"Home Assistant: cannot connect to the MQTT broker at {host}:{port} ({failure}); retrying.");
					_lastBridgeFailure = failure;
					TimeSpan wait = HomeAssistantBridgePolicy.Backoff[Math.Min(failures++, HomeAssistantBridgePolicy.Backoff.Length - 1)];
					await WaitKickAsync(wait, stopping).ConfigureAwait(false);
					continue;
				}
				failures = 0;
				_lastBridgeFailure = null;
				using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stopping);
				lock (_b2Sync) { _mqtt = connection; _mqttNode = node; _lastStatePayload = null; _bridgeCycle = cycle; }
				SetBridge(new HaBridgeStatus(true, true, true, host, null, DateTimeOffset.Now, saved));
				_logger.Info($"Home Assistant: connected to the MQTT broker at {host}:{port} as core/{node}.");
				string reason;
				try
				{
					await connection.SubscribeAsync(new[] { HomeAssistantBridgePolicy.CommandFilter(node), HomeAssistantBridgePolicy.HaStatusTopic, HomeAssistantBridgePolicy.SleepModeTopic }, cycle.Token).ConfigureAwait(false);
					await AnnounceAsync(connection, node, cycle.Token).ConfigureAwait(false);
					reason = await connection.RunAsync(message => OnMqttMessageAsync(node, message), cycle.Token).ConfigureAwait(false);
				}
				catch (Exception error) when (error is System.IO.IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException) { reason = "connection lost"; }
				if (stopping.IsCancellationRequested || cycle.IsCancellationRequested) await connection.DisconnectAsync().ConfigureAwait(false);
				lock (_b2Sync) { _mqtt = null; _haSleepMode = null; _bridgeCycle = null; }
				await connection.DisposeAsync().ConfigureAwait(false);
				SetBridge(new HaBridgeStatus(true, true, false, host, cycle.IsCancellationRequested ? null : reason, DateTimeOffset.Now, saved));
				_logger.Info($"Home Assistant: disconnected ({(cycle.IsCancellationRequested && !stopping.IsCancellationRequested ? "settings changed or the PC woke" : reason)}).");
			}
			catch (Exception error)
			{
				_logger.Error("Home Assistant: the bridge failed this round; retrying in a minute.", error);
				await WaitKickAsync(TimeSpan.FromMinutes(1), stopping).ConfigureAwait(false);
			}
		}
	}

	private void SetBridge(HaBridgeStatus status) { lock (_b2Sync) _bridge = status; }

	private async Task AnnounceAsync(MqttConnection connection, string node, CancellationToken cancellation)
	{
		string device = $"Core ({Environment.MachineName})";
		foreach ((string topic, string payload) in HomeAssistantBridgePolicy.Discovery(node, device, Playhub.Shared.EternalRelease.Version, CoreAutomations.EventTypes(Rules())))
			await connection.PublishAsync(topic, payload, true, cancellation).ConfigureAwait(false);
		await connection.PublishAsync(HomeAssistantBridgePolicy.AvailabilityTopic(node), "online", true, cancellation).ConfigureAwait(false);
		await connection.PublishAsync(HomeAssistantBridgePolicy.AsleepTopic(node), "OFF", true, cancellation).ConfigureAwait(false);
		lock (_b2Sync) _lastStatePayload = null;
		await PublishStateAsync().ConfigureAwait(false);
	}

	private Task OnMqttMessageAsync(string node, MqttMessage message)
	{
		string text = message.Payload.Length > 256 ? "" : message.Text;
		if (message.Topic == HomeAssistantBridgePolicy.SleepModeTopic)
		{
			bool? sleepMode = HomeAssistantBridgePolicy.ParseSleepMode(text);
			lock (_b2Sync) _haSleepMode = sleepMode;
			return Task.CompletedTask;
		}
		if (message.Topic == HomeAssistantBridgePolicy.HaStatusTopic)
		{
			if (text.Trim() == "online")
			{
				MqttConnection? connection; lock (_b2Sync) connection = _mqtt;
				if (connection is not null) _ = Task.Run(async () => { try { await AnnounceAsync(connection, node, CancellationToken.None).ConfigureAwait(false); } catch { } });
			}
			return Task.CompletedTask;
		}
		HaCommand? command = HomeAssistantBridgePolicy.ParseCommand(node, message.Topic, text);
		if (command is null) { _logger.Info($"Home Assistant: ignored a message on {message.Topic} (not a command Core offers)."); return Task.CompletedTask; }
		_ = Task.Run(async () =>
		{
			try
			{
				switch (command.Kind)
				{
					case HaCommandKind.WakeTv:
						TvResult result = await _tv.WakeAndSwitchAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
						NoteTvWake();
						_logger.Info($"Home Assistant: \"Wake TV\": {result.Outcome}.");
						break;
					case HaCommandKind.Sleep:
						SleepFromHomeAssistant();
						break;
					case HaCommandKind.KeepTvOut:
						SetKeepTvOut(command.On, "Home Assistant");
						await PublishStateAsync().ConfigureAwait(false);
						break;
				}
			}
			catch (Exception error) { _logger.Error($"Home Assistant: command {command.Kind} failed.", error); }
		});
		return Task.CompletedTask;
	}

	private HaState CurrentHaState()
	{
		StreamState stream; StreamSettings settings;
		lock (_streamSync) { stream = _stream; settings = _streamSettings; }
		uint appId = RunningAppId();
		bool game = GameRunning();
		string? name = null;
		if (appId != 0)
		{
			if (_gameName.AppId != appId) _gameName = (appId, Batch2Probes.SteamGameName(SteamRoot(), appId));
			name = _gameName.Name;
		}
		var state = new HaState
		{
			Game = game, GameAppId = appId, GameName = name, Media = _media.State, MediaApp = _media.App,
			StreamMode = stream.Active, StreamDevice = stream.Device ?? (stream.Detected ? StreamModePolicy.Label(stream.Source) : null),
			KeepTvOut = settings.KeepTvOut, Vr = stream.Sources.Contains(StreamSource.SteamVr), GamingMode = InGamingMode(),
		};
		return state with { Idle = HomeAssistantBridgePolicy.IsIdle(state) };
	}

	private async Task PublishStateAsync()
	{
		MqttConnection? connection; string node;
		lock (_b2Sync) { connection = _mqtt; node = _mqttNode; }
		if (connection is null) return;
		string payload = HomeAssistantBridgePolicy.StatePayload(CurrentHaState());
		lock (_b2Sync) { if (payload == _lastStatePayload) return; _lastStatePayload = payload; }
		try
		{
			using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			await connection.PublishAsync(HomeAssistantBridgePolicy.StateTopic(node), payload, true, limit.Token).ConfigureAwait(false);
		}
		catch (Exception error) when (error is System.IO.IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException)
		{
			lock (_b2Sync) _lastStatePayload = null;
		}
	}

	private async Task<bool> PublishEventAsync(string type)
	{
		MqttConnection? connection; string node;
		lock (_b2Sync) { connection = _mqtt; node = _mqttNode; }
		if (connection is null) { _logger.Info($"Automations: Home Assistant event \"{type}\" not sent (not connected)."); return false; }
		try
		{
			using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			await connection.PublishAsync(HomeAssistantBridgePolicy.EventTopic(node), HomeAssistantBridgePolicy.EventPayload(type), false, limit.Token).ConfigureAwait(false);
			return true;
		}
		catch (Exception error) when (error is System.IO.IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException) { return false; }
	}

	// Before any sleep (Core's own, Home Assistant's, or Windows'): "asleep" on, offline, and a clean goodbye, within ~1 s.
	private async Task AnnounceSleepAsync()
	{
		MqttConnection? connection; string node;
		lock (_b2Sync) { connection = _mqtt; node = _mqttNode; }
		if (connection is null) return;
		try
		{
			using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
			await connection.PublishAsync(HomeAssistantBridgePolicy.AsleepTopic(node), "ON", true, limit.Token).ConfigureAwait(false);
			await connection.PublishAsync(HomeAssistantBridgePolicy.AvailabilityTopic(node), "offline", true, limit.Token).ConfigureAwait(false);
		}
		catch (Exception error) when (error is System.IO.IOException or OperationCanceledException or ObjectDisposedException or System.Net.Sockets.SocketException) { }
	}

	// Aincrad's input_boolean.sleep_mode, published by the core_pc.yaml package; null when not connected. Wind-down
	// (batch 1) skips its reminders while it is on.
	private bool? SleepModeActive() { lock (_b2Sync) return _haSleepMode; }

	// ---------- Core Health ----------

	private IReadOnlyList<HealthRow> BatchTwoRows()
	{
		var rows = new List<HealthRow>();
		GamingSettings gaming = _store.LoadConfig().Gaming;
		DateTimeOffset? at, last;
		lock (_b2Sync) { at = _streamSleepAt; last = _streamSleep.LastSleepAt; }
		rows.Add(StreamSleepPolicy.Row(gaming.StreamSleepEnabled, gaming.StreamSleepMinutes, at, last));
		WakeOnLanInputs wol;
		lock (_b2Sync)
		{
			if (_wol is { } cached && DateTimeOffset.Now - cached.At < TimeSpan.FromMinutes(30)) wol = cached.Inputs;
			else wol = new WakeOnLanInputs(null, null);
		}
		if (wol.Adapters is null)
		{
			try { wol = Batch2Probes.WakeOnLan(); lock (_b2Sync) _wol = (DateTimeOffset.Now, wol); }
			catch (Exception error) { _logger.Error("Wake-on-LAN check failed.", error); }
		}
		rows.Add(WakeOnLanPolicy.Row(wol));
		HaBridgeStatus bridge; lock (_b2Sync) bridge = _bridge;
		rows.Add(HomeAssistantBridgePolicy.Row(bridge with { Enabled = gaming.HomeAssistantEnabled }));
		return rows;
	}

	// ---------- HTTP ----------

	private object AutomationsView()
	{
		List<CoreAutomationRule> rules = Rules();
		GamingSettings gaming = _store.LoadConfig().Gaming;
		HaBridgeStatus bridge; DateTimeOffset? sleepAt; List<(string Rule, string Trigger, DateTimeOffset At, bool Ok)> runs;
		lock (_b2Sync) { bridge = _bridge; sleepAt = _streamSleepAt; runs = _automationRuns.ToList(); }
		TvSettings tv = gaming.Tv ?? new TvSettings();
		return new
		{
			rules = rules.Select(rule => new { id = rule.Id, enabled = rule.Enabled, trigger = rule.Trigger, actions = rule.Actions, @event = rule.Event }).ToArray(),
			tvOnWake = tv.Enabled && CoreAutomations.WakeTvOnResume(tv.TurnOnWithCore, rules),
			runs = runs.Select(run => new { rule = run.Rule, trigger = run.Trigger, at = run.At.ToString("o"), ok = run.Ok }).ToArray(),
			bridge = BridgeView(bridge with { Enabled = gaming.HomeAssistantEnabled }),
			streamSleep = new { enabled = gaming.StreamSleepEnabled, minutes = StreamSleepPolicy.CleanDelay(gaming.StreamSleepMinutes), at = sleepAt?.ToString("o") },
			typing = new { enabled = gaming.SwipeTypingEnabled },
		};
	}

	private object BridgeView(HaBridgeStatus bridge) => new
	{
		enabled = bridge.Enabled, connected = bridge.Connected, state = HomeAssistantBridgePolicy.Row(bridge).State, host = bridge.Host,
		failure = bridge.Failure, node = _mqttNode, passwordSaved = CoreSecretStore.HasPassword(_paths.ConfigDirectory),
	};

	private void MapBatchTwo(WebApplication app)
	{
		app.MapGet("/core/automations", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(AutomationsView()) : Results.StatusCode(403)));
		app.MapGet("/core/bridge", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			HaBridgeStatus bridge; lock (_b2Sync) bridge = _bridge;
			return Results.Json(BridgeView(bridge with { Enabled = _store.LoadConfig().Gaming.HomeAssistantEnabled }));
		}));
		// The desktop app saved new broker settings or a new password: connect again now.
		app.MapPost("/core/bridge/reconnect", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			KickBridge();
			return Results.Json(new { ok = true });
		}));
		app.MapGet("/core/typing", (Func<HttpContext, IResult>)(context =>
			Local(context) ? Results.Json(new { enabled = _store.LoadConfig().Gaming.SwipeTypingEnabled }) : Results.StatusCode(403)));
		app.MapPost("/core/typing", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string enabled = context.Request.Query["enabled"].ToString();
			if (!OnOff.IsMatch(enabled)) return Results.Json(new { ok = false }, statusCode: 400);
			ModeConfig config = _store.LoadConfig();
			config.Gaming.SwipeTypingEnabled = enabled == "true";
			_store.SaveConfig(config);
			_logger.Info($"Swipe typing turned {(enabled == "true" ? "on" : "off")} from Quick Access.");
			return Results.Json(new { enabled = config.Gaming.SwipeTypingEnabled });
		}));
	}

	// The heartbeat may say how long ago Core's plugin last saw Steam controller input (sleep after streaming).
	private void NoteSteamInput(string? secondsAgo)
	{
		if (!int.TryParse(secondsAgo, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int ago) || ago > 86400) return;
		lock (_b2Sync) _steamInputAt = DateTimeOffset.Now - TimeSpan.FromSeconds(ago);
	}
}
