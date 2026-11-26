using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GamingMode.Services;

// Stream mode in the agent (rules in CoreStreamMode.cs, Windows probes in StreamMode.Windows.cs; guide in
// docs/eternal/CORE-STREAM-MODE.md). Every five seconds from the Mark III loop: gather what streams
// (Sunshine/Apollo logs, SteamVR, the Decky plugin's Steam Remote Play report), decide, log changes,
// and keep the PC's sound off the TV. TvController asks StreamHold() before it sends anything.
//
// Endpoints (loopback only): GET /stream/state, POST /stream/keep-tv-out?on=true|false (Quick Access),
// POST /stream/steam?active=&device=&session= (the plugin's report, repeated every 30 s).
internal sealed partial class MarkIIIHost
{
	private readonly object _streamSync = new();
	private string _streamSettingsPath = "", _streamAudioPath = "";
	private StreamSettings _streamSettings = new();
	private StreamAudioState _streamAudio = new();
	private StreamState _stream = StreamState.Idle;
	private StreamSignal? _steamReport;
	private DateTimeOffset? _streamSince;
	private readonly Dictionary<string, SunshineLogTail> _hostLogs = new(StringComparer.OrdinalIgnoreCase);
	private (StreamSource Source, SunshineLogState State, bool Running)? _hostSeen;

	// ---------- lifetime ----------

	private void StartStream()
	{
		_streamSettingsPath = Path.Combine(_paths.ConfigDirectory, "stream-settings.json");
		_streamAudioPath = Path.Combine(_paths.ConfigDirectory, "stream-audio.json");
		lock (_streamSync)
		{
			_streamSettings = StreamSettings.Load(_streamSettingsPath);
			_streamAudio = StreamAudioState.Load(_streamAudioPath);
		}
		if (_streamSettings.KeepTvOut) _logger.Info("Stream mode: \"Keep TV out of it\" is on from the last session.");
		if (!_streamAudio.IsEmpty) _logger.Info("Stream mode: a sound change from the last session is still recorded; the first check restores or keeps it.");
		CheckStream();
	}

	// Also called by the TV hold, the wake path and the endpoints, so it must stay cheap: two process
	// lookups and reading what a log gained since the last call.
	private StreamState CheckStream()
	{
		var signals = new List<StreamSignal>();
		DateTimeOffset now = DateTimeOffset.Now;
		StreamSettings settings;
		lock (_streamSync) { settings = _streamSettings; if (_steamReport is { } report) signals.Add(report); }
		signals.AddRange(AgentSignals(settings, now, remember: true));
		StreamState next = StreamModePolicy.Evaluate(signals, settings.KeepTvOut, now);
		StreamState before;
		lock (_streamSync)
		{
			before = _stream;
			_stream = next;
			if (next.Active && !before.Active) _streamSince = now;
			else if (!next.Active) _streamSince = null;
		}
		string? change = StreamModePolicy.Describe(before, next);
		if (change is not null) _logger.Info(change);
		try { ApplyStreamAudio(next, settings); }
		catch (Exception error) { _logger.Error("Stream mode: the PC's sound could not be checked this round.", error); }
		return next;
	}

	// What the agent sees by itself: a Sunshine/Apollo session in progress (their log, while the host
	// runs) and SteamVR. Also used at power-on, before the plugin can report anything.
	private IEnumerable<StreamSignal> AgentSignals(StreamSettings settings, DateTimeOffset now, bool remember)
	{
		var found = new List<StreamSignal>();
		bool running = StreamProbes.HostRunning();
		(StreamSource Source, SunshineLogState State)? newest = null;
		DateTime newestWrite = DateTime.MinValue;
		foreach ((StreamSource source, string path) in StreamProbes.HostLogs(settings.HostLog))
		{
			SunshineLogTail tail;
			lock (_streamSync)
			{
				if (!_hostLogs.TryGetValue(path, out SunshineLogTail? known)) { known = new SunshineLogTail(path); _hostLogs[path] = known; }
				tail = known;
			}
			SunshineLogState state;
			lock (tail) state = tail.Poll();
			if (tail.LastWrite is { } written && written > newestWrite) { newestWrite = written; newest = (source, state); }
		}
		if (newest is { } host)
		{
			if (remember) lock (_streamSync) _hostSeen = (host.Source, host.State, running);
			if (running && host.State.Streaming) found.Add(new StreamSignal(host.Source, host.State.Client ?? "Moonlight", now));
		}
		else if (remember) lock (_streamSync) _hostSeen = null;
		if (StreamProbes.SteamVrRunning()) found.Add(new StreamSignal(StreamSource.SteamVr, null, now));
		return found;
	}

	// For TvController: why the TV must be left alone, or null. Looks again rather than trusting the last
	// round, so a stream that started seconds ago already counts.
	private string? StreamHold()
	{
		try { return CheckStream().HoldReason; }
		catch (Exception error) { _logger.Error("Stream mode: check failed; the TV is treated as held.", error); return "Stream mode check failed"; }
	}

	private bool StreamHoldsNotices(string what)
	{
		StreamState state;
		lock (_streamSync) state = _stream;
		if (!state.Active) return false;
		_logger.Info($"Stream mode: {what} not shown on the TV ({state.HoldReason}).");
		return true;
	}

	// Sleep or shutdown: remember whether a stream was running, for the next wake's grace.
	private void RememberStreamAtSleep()
	{
		StreamState state = CheckStream();
		lock (_streamSync)
		{
			if (_streamSettings.StreamingAtSleep == state.Detected) return;
			_streamSettings = _streamSettings with { StreamingAtSleep = state.Detected };
			PersistStreamSettings();
		}
	}

	private void ClearStreamAtSleep()
	{
		lock (_streamSync)
		{
			if (!_streamSettings.StreamingAtSleep) return;
			_streamSettings = _streamSettings with { StreamingAtSleep = false };
			PersistStreamSettings();
		}
	}

	// Caller holds _streamSync.
	private void PersistStreamSettings()
	{
		try { StreamSettings.Save(_streamSettingsPath, _streamSettings); }
		catch (Exception error) { _logger.Error("Stream mode: settings could not be saved.", error); }
	}

	// The wake decision: hold at once, give a stream that ended with the sleep a moment to come back, or
	// go ahead. Returns the reason the TV stays alone, or null to wake it as usual.
	private async Task<string?> StreamWakeHoldAsync()
	{
		StreamState now = CheckStream();
		bool streamedAtSleep;
		lock (_streamSync) streamedAtSleep = _streamSettings.StreamingAtSleep;
		ClearStreamAtSleep();
		switch (StreamModePolicy.OnWake(now, streamedAtSleep))
		{
			case StreamWakeDecision.Hold: return now.HoldReason;
			case StreamWakeDecision.Proceed: return null;
		}
		_logger.Info($"Stream mode: the PC slept while streaming; the TV waits up to {StreamModePolicy.WakeGrace.TotalSeconds:0} s for the stream to come back.");
		DateTimeOffset giveUp = DateTimeOffset.Now + StreamModePolicy.WakeGrace;
		while (DateTimeOffset.Now < giveUp)
		{
			await Task.Delay(1000).ConfigureAwait(false);
			StreamState seen = CheckStream();
			if (seen.Active) return seen.HoldReason;
		}
		_logger.Info("Stream mode: no stream came back after the wake; the TV is handled as usual.");
		return null;
	}

	// Power-on, before the agent's services exist (WakeTvForBootAsync): "Keep TV out of it", a stream the
	// agent can see by itself, or a grace period when the PC went down mid-stream. Steam Remote Play cannot
	// be seen this early (the plugin is not loaded yet).
	// Returns the reason the TV stays alone (or null), and a hold for TvController to ask again later.
	internal static async Task<(string? Reason, Func<string?> Hold)> StreamBootHoldAsync(AppPaths paths, FileLogger logger)
	{
		string path = Path.Combine(paths.ConfigDirectory, "stream-settings.json");
		StreamSettings settings = StreamSettings.Load(path);
		var probe = new BootProbe(settings);
		Func<string?> hold = probe.Look;
		if (settings.KeepTvOut) return ((StreamState.Idle with { Active = true, Forced = true }).HoldReason, hold);
		if (probe.Look() is { } reason) return (reason, hold);
		if (!settings.StreamingAtSleep) return (null, hold);
		try { StreamSettings.Save(path, settings with { StreamingAtSleep = false }); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		logger.Info($"Stream mode: Core went down while streaming; the TV waits up to {StreamModePolicy.WakeGrace.TotalSeconds:0} s for the stream to come back.");
		DateTimeOffset giveUp = DateTimeOffset.Now + StreamModePolicy.WakeGrace;
		while (DateTimeOffset.Now < giveUp)
		{
			await Task.Delay(1000).ConfigureAwait(false);
			if (probe.Look() is { } found) return (found, hold);
		}
		logger.Info("Stream mode: no stream at power-on; the TV is handled as usual.");
		return (null, hold);
	}

	private sealed class BootProbe
	{
		private readonly List<(StreamSource Source, SunshineLogTail Tail)> _tails;
		internal BootProbe(StreamSettings settings) => _tails = StreamProbes.HostLogs(settings.HostLog).Select(log => (log.Source, new SunshineLogTail(log.Path))).ToList();

		internal string? Look()
		{
			lock (_tails) return LookLocked();
		}

		private string? LookLocked()
		{
			var signals = new List<StreamSignal>();
			DateTimeOffset now = DateTimeOffset.Now;
			bool running = StreamProbes.HostRunning();
			foreach ((StreamSource source, SunshineLogTail tail) in _tails)
				if (tail.Poll().Streaming && running) signals.Add(new StreamSignal(source, tail.State.Client, now));
			if (StreamProbes.SteamVrRunning()) signals.Add(new StreamSignal(StreamSource.SteamVr, null, now));
			return StreamModePolicy.Evaluate(signals, false, now).HoldReason;
		}
	}

	// ---------- the PC's sound ----------

	private void ApplyStreamAudio(StreamState state, StreamSettings settings)
	{
		bool engage = state.Active && settings.AudioOffTv;
		if (_streamAudioPath.Length == 0) return; // before StartStream: no record path yet
		lock (_streamSync)
		{
			if (!engage && _streamAudio.IsEmpty) return;
			IReadOnlyList<SpeakerEndpoint> endpoints = WindowsSpeakers.Read();
			StreamAudioPlan plan = engage
				? StreamAudioPolicy.Engage(_streamAudio, endpoints, StreamModePolicy.RouteAudioToVirtual(state))
				: StreamAudioPolicy.Release(_streamAudio, endpoints);
			StreamAudioState previous = _streamAudio;
			// The record goes to disk before anything is changed: a crash in between still knows what to put back.
			if (plan.Changes.Any(change => change.Mute) || (plan.SetDefault is not null && engage)) SaveStreamAudio(plan.Next);
			bool defaultFailed = false;
			if (plan.SetDefault is { } id)
			{
				bool done;
				try { done = PolicyConfigDefaultSpeaker.SetDefault(id); }
				catch (Exception error) { _logger.Error("Stream mode: setting the default speaker failed.", error); done = false; }
				if (!done) { defaultFailed = true; _logger.Info("Stream mode: Windows did not change the default speaker; Core leaves it as it is."); }
			}
			var failed = new List<SpeakerChange>();
			foreach (SpeakerChange change in plan.Changes)
			{
				bool ok = false;
				try { ok = WindowsSpeakers.SetMute(change.Id, change.Mute); }
				catch (Exception error) { _logger.Error($"Stream mode: could not {(change.Mute ? "mute" : "unmute")} a speaker.", error); }
				if (!ok) failed.Add(change);
			}
			if (plan.Log is not null) _logger.Info(plan.Log);
			if (failed.Count > 0) _logger.Info($"Stream mode: Windows refused {failed.Count} speaker change(s); the next check tries again.");
			StreamAudioState next = StreamAudioPolicy.KeepFailed(plan.Next, failed);
			// Moving to the virtual speakers failed: nothing to move back later; fall back to muting the TV next round.
			if (defaultFailed && engage) next = next with { Engaged = false, Mode = null, SwitchedFrom = null, SwitchedTo = null };
			if (!next.SameAs(previous)) { _streamAudio = next; SaveStreamAudio(next); }
		}
	}

	private void SaveStreamAudio(StreamAudioState state)
	{
		try { StreamAudioState.Save(_streamAudioPath, state); }
		catch (Exception error) { _logger.Error("Stream mode: the sound record could not be saved.", error); }
	}

	// ---------- HTTP ----------

	private object StreamView()
	{
		StreamState state = CheckStream();
		StreamSettings settings; StreamAudioState audio; (StreamSource Source, SunshineLogState State, bool Running)? host; DateTimeOffset? since;
		lock (_streamSync) { settings = _streamSettings; audio = _streamAudio; host = _hostSeen; since = _streamSince; }
		string? audioDevice = null;
		if (audio.Mode == "virtual" || audio.Mode == "already")
		{
			try { audioDevice = WindowsSpeakers.Read().FirstOrDefault(endpoint => endpoint.IsDefault)?.Name; } catch { }
		}
		bool apolloInstalled = Directory.Exists(StreamProbes.ApolloFolder);
		bool apolloRunning = host is { } seenApollo && seenApollo.Source == StreamSource.Apollo && seenApollo.Running;
		bool sunshineRunning = host is { } seenSunshine && seenSunshine.Source == StreamSource.Sunshine && seenSunshine.Running;
		bool virtualDisplay = apolloRunning && host!.Value.State.VirtualDisplay;
		return new
		{
			active = state.Active, forced = settings.KeepTvOut, detected = state.Detected,
			source = StreamModePolicy.Code(state.Source), device = state.Device,
			sources = state.Sources.Select(source => StreamModePolicy.Code(source)).ToArray(),
			since = since?.ToString("o"),
			audio = new { enabled = settings.AudioOffTv, mode = audio.Engaged ? audio.Mode : null, device = audioDevice, pending = audio.Pending.Count },
			apollo = new
			{
				installed = apolloInstalled,
				running = apolloRunning,
				virtualDisplay,
			},
			sunshine = new { installed = Directory.Exists(StreamProbes.SunshineFolder), running = sunshineRunning },
		};
	}

	// For Core Health.
	private (bool Active, bool Forced, bool Detected, string? Source, string? Device, bool? VirtualDisplay) StreamHealth()
	{
		try
		{
			StreamState state = CheckStream();
			(StreamSource Source, SunshineLogState State, bool Running)? host;
			lock (_streamSync) host = _hostSeen;
			bool? display = host is { } seen && seen.Source == StreamSource.Apollo && seen.Running ? seen.State.VirtualDisplay : null;
			return (state.Active, state.Forced, state.Detected, StreamModePolicy.Code(state.Source), state.Device, display);
		}
		catch { return (false, false, false, null, null, null); }
	}

	private void MapStream(WebApplication app)
	{
		app.MapGet("/stream/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(StreamView()) : Results.StatusCode(403)));
		app.MapPost("/stream/keep-tv-out", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string on = context.Request.Query["on"].ToString();
			if (on is not ("true" or "false")) return Results.Json(new { ok = false }, statusCode: 400);
			lock (_streamSync)
			{
				_streamSettings = _streamSettings with { KeepTvOut = on == "true" };
				PersistStreamSettings();
			}
			_logger.Info($"Stream mode: \"Keep TV out of it\" turned {(on == "true" ? "on" : "off")} from Quick Access.");
			return Results.Json(StreamView());
		}));
		// The plugin's view of Steam Remote Play (SteamClient.RemotePlay on the host), repeated every 30 s.
		app.MapPost("/stream/steam", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string active = context.Request.Query["active"].ToString();
			if (active is not ("true" or "false")) return Results.Json(new { ok = false }, statusCode: 400);
			string? device = StreamModePolicy.CleanDevice(context.Request.Query["device"].ToString());
			bool wasActive;
			lock (_streamSync)
			{
				wasActive = _steamReport is not null;
				_steamReport = active == "true" ? new StreamSignal(StreamSource.SteamRemotePlay, device, DateTimeOffset.Now) : null;
			}
			if (wasActive != (active == "true")) CheckStream();
			return Results.Json(new { ok = true });
		}));
	}
}
