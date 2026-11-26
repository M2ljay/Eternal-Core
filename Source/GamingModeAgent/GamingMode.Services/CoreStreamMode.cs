using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core: Stream mode. While the PC streams a game to another device (Steam Remote Play / Steam
// Link, a Steam Frame through SteamVR, Moonlight through Sunshine or Apollo), the living-room TV may be
// showing something else entirely, so Core leaves it alone: no wake, no input switch, nothing sent to
// it (TvController's hold), the PC's own sound kept off it (StreamAudioPolicy), and Core's notices held
// back (they would appear on a screen nobody is looking at). "Keep TV out of it" in Quick Access forces
// the same while nothing is detected. Pure rules; the Windows probes and Core Audio calls are in
// StreamMode.Windows.cs and the agent wiring in MarkIIIHost.Stream.cs. Guide: docs/eternal/CORE-STREAM-MODE.md.

internal enum StreamSource { Apollo, Sunshine, SteamRemotePlay, SteamVr }

// One source seen streaming. Steam reports come from the Decky plugin (SteamClient.RemotePlay) and carry
// the time they arrived; the others are polled by the agent and are stamped "now".
internal sealed record StreamSignal(StreamSource Source, string? Device, DateTimeOffset SeenAt);

internal sealed record StreamState(bool Active, bool Forced, bool Detected, StreamSource? Source, string? Device, IReadOnlyList<StreamSource> Sources)
{
	internal static readonly StreamState Idle = new(false, false, false, null, null, Array.Empty<StreamSource>());

	// Why the TV is held, for log lines; null when it is not.
	internal string? HoldReason => !Active ? null
		: Detected ? Device is null ? $"streaming through {StreamModePolicy.Label(Source)}" : $"streaming to {Device} through {StreamModePolicy.Label(Source)}"
		: "\"Keep TV out of it\" is on";
}

internal enum StreamWakeDecision { Proceed, Hold, WaitForStream }

internal static class StreamModePolicy
{
	// The plugin repeats its report every 30 s while Big Picture runs; three missed reports end it.
	internal static readonly TimeSpan SteamReportLifetime = TimeSpan.FromSeconds(90);
	// After a wake (or a cold boot) that follows a session which ended mid-stream, the TV waits this long
	// for the stream to come back before Core takes it over.
	internal static readonly TimeSpan WakeGrace = TimeSpan.FromSeconds(15);

	// When several sources stream at once, the one most likely to name the device wins.
	private static readonly StreamSource[] Priority = { StreamSource.Apollo, StreamSource.Sunshine, StreamSource.SteamRemotePlay, StreamSource.SteamVr };

	internal static StreamState Evaluate(IEnumerable<StreamSignal> signals, bool forced, DateTimeOffset now)
	{
		var fresh = signals.Where(signal => signal.Source != StreamSource.SteamRemotePlay || (now - signal.SeenAt <= SteamReportLifetime && signal.SeenAt - now <= SteamReportLifetime))
			.OrderBy(signal => Array.IndexOf(Priority, signal.Source)).ToList();
		if (fresh.Count == 0) return forced ? StreamState.Idle with { Active = true, Forced = true } : StreamState.Idle;
		string? device = fresh.Select(signal => CleanDevice(signal.Device)).FirstOrDefault(name => name is not null);
		return new StreamState(true, forced, true, fresh[0].Source, device, fresh.Select(signal => signal.Source).Distinct().ToArray());
	}

	internal static string Code(StreamSource? source) => source switch
	{
		StreamSource.Apollo => "apollo",
		StreamSource.Sunshine => "sunshine",
		StreamSource.SteamRemotePlay => "steam",
		StreamSource.SteamVr => "steamvr",
		_ => "none",
	};

	internal static string Label(StreamSource? source) => source switch
	{
		StreamSource.Apollo => "Apollo",
		StreamSource.Sunshine => "Sunshine",
		StreamSource.SteamRemotePlay => "Steam Remote Play",
		StreamSource.SteamVr => "SteamVR",
		_ => "nothing",
	};

	// Device names come from Steam or a streaming host's log: one short printable line.
	internal static string? CleanDevice(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		var builder = new StringBuilder();
		foreach (char character in name.Trim())
		{
			if (char.IsControl(character)) continue;
			builder.Append(character);
			if (builder.Length == 64) break;
		}
		string clean = builder.ToString().Trim();
		return clean.Length == 0 ? null : clean;
	}

	// Default playback is moved to the stream's virtual speakers only for Steam Remote Play, which
	// itself streams from Steam Streaming Speakers. Sunshine and Apollo choose the device they capture
	// when the session starts (their audio_sink defaults to Windows' default device), so moving the default
	// mid-session could silence the stream; for them, and for SteamVR (whose sound belongs to the headset),
	// Core only mutes the TV's own speaker endpoint. Forced without a stream: mute too.
	internal static bool RouteAudioToVirtual(StreamState state) => state.Detected && state.Source == StreamSource.SteamRemotePlay;

	internal static StreamWakeDecision OnWake(StreamState now, bool streamingAtSleep) =>
		now.Active ? StreamWakeDecision.Hold : streamingAtSleep ? StreamWakeDecision.WaitForStream : StreamWakeDecision.Proceed;

	// A log line when Stream mode starts, ends, or the device or source changes; null otherwise.
	internal static string? Describe(StreamState before, StreamState after)
	{
		if (before.Active == after.Active && before.Detected == after.Detected && before.Source == after.Source && before.Device == after.Device && before.Forced == after.Forced) return null;
		if (!after.Active) return "Stream mode: off; the TV is Core's again.";
		if (!after.Detected) return "Stream mode: on (\"Keep TV out of it\"); nothing is sent to the TV.";
		return $"Stream mode: on, {after.HoldReason}{(after.Forced ? " (\"Keep TV out of it\" is on too)" : "")}; nothing is sent to the TV.";
	}
}

// ---------- settings (stream-settings.json, written only by the agent) ----------

internal sealed record StreamSettings
{
	// Quick Access > Core > Stream mode > "Keep TV out of it": Stream mode stays on without a stream.
	public bool KeepTvOut { get; init; }
	// Config-only: keep the PC's own sound off the TV while Stream mode is on (default on).
	public bool AudioOffTv { get; init; } = true;
	// Set when the PC sleeps or shuts down while a stream is running, so the next wake gives the stream
	// a moment to come back before the TV is taken over (StreamModePolicy.WakeGrace).
	public bool StreamingAtSleep { get; init; }
	// Config-only: a Sunshine or Apollo log to read instead of the default places.
	public string? HostLog { get; init; }

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

	internal static StreamSettings Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new StreamSettings();
			return JsonSerializer.Deserialize<StreamSettings>(File.ReadAllText(path)) ?? new StreamSettings();
		}
		catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return new StreamSettings(); }
	}

	internal static void Save(string path, StreamSettings settings)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
		File.Move(temporary, path, true);
	}
}

// ---------- Sunshine and Apollo: their log ----------

// What a Sunshine or Apollo log says about the current session. Lines verified in published logs of
// both (Sunshine and Apollo issue reports, 2025-2026):
//   "Info: New streaming session started [active sessions: 1]"
//   "Info: CLIENT CONNECTED" / "Info: CLIENT DISCONNECTED"
// and, Apollo only (its SudoVDA virtual display):
//   "Info: Display mode for client [Steam Deck] requested to [1280x720x60]"
//   "Info: Virtual Display created at \\.\DISPLAY49" / "Info: Virtual Display removed successfully"
// Neither has a local API that works without the web UI's password, which Core does not read.
// Only the CONNECTED/DISCONNECTED pair decides "streaming": a session that starts but never connects
// must not hold the TV. Every caller also requires the host process to be running (a crash writes no
// disconnect line).
internal sealed record SunshineLogState(int Clients, string? Client, string? PendingClient, bool VirtualDisplay, string? Display)
{
	internal static readonly SunshineLogState Empty = new(0, null, null, false, null);
	internal bool Streaming => Clients > 0;
}

internal static class SunshineLog
{
	private static readonly Regex ClientMode = new(@"Display mode for client \[(.{1,80}?)\] requested", RegexOptions.CultureInvariant);
	private static readonly Regex DisplayCreated = new(@"Virtual Display created at (\S{1,64})", RegexOptions.CultureInvariant);

	internal static SunshineLogState Apply(SunshineLogState state, string line)
	{
		if (line.Contains("CLIENT DISCONNECTED", StringComparison.Ordinal))
		{
			int clients = Math.Max(0, state.Clients - 1);
			return state with { Clients = clients, Client = clients == 0 ? null : state.Client };
		}
		if (line.Contains("CLIENT CONNECTED", StringComparison.Ordinal))
			return state with { Clients = Math.Min(state.Clients + 1, 16), Client = state.PendingClient ?? state.Client, PendingClient = null };
		Match match = ClientMode.Match(line);
		if (match.Success) return state with { PendingClient = StreamModePolicy.CleanDevice(match.Groups[1].Value) };
		match = DisplayCreated.Match(line);
		if (match.Success) return state with { VirtualDisplay = true, Display = match.Groups[1].Value };
		if (line.Contains("Virtual Display removed", StringComparison.Ordinal)) return state with { VirtualDisplay = false, Display = null };
		// Sunshine shutting down: nothing streams any more.
		if (line.Contains("Terminate handler called", StringComparison.Ordinal)) return SunshineLogState.Empty;
		return state;
	}
}

// Follows one log file: reads only what was appended since the last poll. Sunshine starts a new file at
// each start (the previous one becomes .1), which shows up as a shorter file or a new creation time:
// the state then starts over. Opened with full sharing, read-only: Sunshine keeps writing it.
internal sealed class SunshineLogTail
{
	private const long FirstReadLimit = 4L * 1024 * 1024, ReadLimit = 1024 * 1024;
	private readonly string _path;
	private long _position;
	private DateTime _created;
	private string _partial = "";
	// Keeps a character split across two reads whole.
	private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
	private readonly char[] _chars = new char[ReadLimit + 4];

	internal SunshineLogTail(string path) => _path = path;

	internal string Path => _path;
	internal SunshineLogState State { get; private set; } = SunshineLogState.Empty;
	internal DateTime? LastWrite { get; private set; }

	internal SunshineLogState Poll()
	{
		try
		{
			var info = new FileInfo(_path);
			if (!info.Exists) { Reset(); LastWrite = null; return State; }
			LastWrite = info.LastWriteTimeUtc;
			if (info.Length < _position || (_created != default && info.CreationTimeUtc != _created)) Reset();
			_created = info.CreationTimeUtc;
			using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			long length = stream.Length;
			bool skipFirstLine = false;
			if (_position == 0 && length > FirstReadLimit) { _position = length - FirstReadLimit; skipFirstLine = true; }
			stream.Seek(_position, SeekOrigin.Begin);
			byte[] buffer = new byte[ReadLimit];
			// Catch up in 1 MB steps; never more than the first-read window in one poll.
			for (long budget = FirstReadLimit; _position < length && budget > 0; budget -= ReadLimit)
			{
				int read = stream.Read(buffer, 0, (int)Math.Min(length - _position, ReadLimit));
				if (read <= 0) break;
				_position += read;
				int count = _decoder.GetChars(buffer, 0, read, _chars, 0);
				string text = _partial + new string(_chars, 0, count);
				int end = text.LastIndexOf('\n');
				if (end < 0) { _partial = text.Length > 8192 ? "" : text; continue; }
				_partial = text[(end + 1)..];
				if (_partial.Length > 8192) _partial = "";
				string[] lines = text[..end].Split('\n');
				for (int index = skipFirstLine ? 1 : 0; index < lines.Length; index++) State = SunshineLog.Apply(State, lines[index]);
				skipFirstLine = false;
			}
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		return State;
	}

	private void Reset()
	{
		_position = 0;
		_partial = "";
		_created = default;
		_decoder.Reset();
		State = SunshineLogState.Empty;
	}
}

// ---------- the PC's own sound ----------

// A playback endpoint. FormFactor is PKEY_AudioEndpoint_FormFactor (EndpointFormFactor): 9 is
// DigitalAudioDisplayDevice, the speaker of a display over HDMI or DisplayPort, i.e. the TV.
internal sealed record SpeakerEndpoint(string Id, string? Name, int FormFactor, bool IsDefault, bool Muted);

internal sealed record SpeakerChange(string Id, bool Mute);

internal sealed record StreamAudioState
{
	public bool Engaged { get; init; }
	// "virtual" (default moved to the stream's speakers), "already" (the stream had done it itself),
	// "muted" (the TV's speaker muted), "none" (nothing to do), or null when not engaged.
	public string? Mode { get; init; }
	public string? SwitchedFrom { get; init; }
	public string? SwitchedTo { get; init; }
	// Endpoints Core muted (each was live before), and every endpoint present when Core engaged: an
	// endpoint that appears later (the TV's HDMI speaker after a wake) is muted too, one the user unmutes
	// is left alone.
	public List<string> Muted { get; init; } = new();
	public List<string> Seen { get; init; } = new();
	// Endpoints Core muted that were gone when Stream mode ended: unmuted when they come back.
	public List<string> Pending { get; init; } = new();
	// The default was changed by someone else while Core had moved it: never moved back.
	public bool DefaultChangedOutside { get; init; }

	internal bool IsEmpty => !Engaged && Pending.Count == 0;

	internal static StreamAudioState Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new StreamAudioState();
			return JsonSerializer.Deserialize<StreamAudioState>(File.ReadAllText(path)) ?? new StreamAudioState();
		}
		catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { return new StreamAudioState(); }
	}

	internal static void Save(string path, StreamAudioState state)
	{
		if (state.IsEmpty) { try { File.Delete(path); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } return; }
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state));
		File.Move(temporary, path, true);
	}

	internal bool SameAs(StreamAudioState other) =>
		Engaged == other.Engaged && Mode == other.Mode && SwitchedFrom == other.SwitchedFrom && SwitchedTo == other.SwitchedTo
		&& DefaultChangedOutside == other.DefaultChangedOutside && Muted.SequenceEqual(other.Muted) && Seen.SequenceEqual(other.Seen) && Pending.SequenceEqual(other.Pending);
}

// SetDefault: an endpoint id to make the Console and Multimedia default (and nothing else).
internal sealed record StreamAudioPlan(StreamAudioState Next, string? SetDefault, IReadOnlyList<SpeakerChange> Changes, string? Log);

internal static class StreamAudioPolicy
{
	internal const int FormFactorDigitalDisplay = 9;
	internal const string VirtualSpeakersName = "Steam Streaming Speakers";

	internal static bool IsStreamVirtual(SpeakerEndpoint endpoint) =>
		endpoint.Name?.Contains(VirtualSpeakersName, StringComparison.OrdinalIgnoreCase) == true;

	internal static bool IsTvSpeaker(SpeakerEndpoint endpoint) => endpoint.FormFactor == FormFactorDigitalDisplay && !IsStreamVirtual(endpoint);

	// Stream mode is on: keep the PC's sound off the TV. Called again on every check while on.
	internal static StreamAudioPlan Engage(StreamAudioState state, IReadOnlyList<SpeakerEndpoint> endpoints, bool routeToVirtual)
	{
		if (state.Engaged) return Reconcile(state, endpoints);
		var seen = endpoints.Select(endpoint => endpoint.Id).ToList();
		// Endpoints Core muted last time and that were away: still muted by Core, so they belong to this record.
		var muted = state.Pending.Where(id => endpoints.Any(endpoint => endpoint.Id == id && endpoint.Muted)).ToList();
		var engaged = new StreamAudioState { Engaged = true, Seen = seen, Muted = muted, Pending = state.Pending.Where(id => endpoints.All(endpoint => endpoint.Id != id)).ToList() };
		SpeakerEndpoint? current = endpoints.FirstOrDefault(endpoint => endpoint.IsDefault);
		if (current is not null && IsStreamVirtual(current))
			return new(engaged with { Mode = "already" }, null, Array.Empty<SpeakerChange>(), "Stream mode: the PC's sound already plays on the stream's virtual speakers; left as it is.");
		SpeakerEndpoint? speakers = endpoints.FirstOrDefault(IsStreamVirtual);
		if (routeToVirtual && speakers is not null)
			return new(engaged with { Mode = "virtual", SwitchedFrom = current?.Id, SwitchedTo = speakers.Id }, speakers.Id, Array.Empty<SpeakerChange>(),
				$"Stream mode: the PC's sound moves to {speakers.Name} (was {current?.Name ?? "no default"}); it moves back afterwards.");
		var targets = endpoints.Where(endpoint => IsTvSpeaker(endpoint) && !endpoint.Muted).ToList();
		if (targets.Count == 0)
			return new(engaged with { Mode = "none" }, null, Array.Empty<SpeakerChange>(), "Stream mode: no live TV speaker (HDMI) found; the PC's sound is left as it is.");
		muted.AddRange(targets.Select(endpoint => endpoint.Id));
		return new(engaged with { Mode = "muted", Muted = muted }, null, targets.Select(endpoint => new SpeakerChange(endpoint.Id, true)).ToArray(),
			$"Stream mode: muted the TV's speaker ({string.Join(", ", targets.Select(endpoint => endpoint.Name ?? "HDMI"))}); it is unmuted afterwards.");
	}

	private static StreamAudioPlan Reconcile(StreamAudioState state, IReadOnlyList<SpeakerEndpoint> endpoints)
	{
		var logs = new List<string>();
		var changes = new List<SpeakerChange>();
		StreamAudioState next = state;
		if (state.Mode == "virtual")
		{
			SpeakerEndpoint? current = endpoints.FirstOrDefault(endpoint => endpoint.IsDefault);
			if (!state.DefaultChangedOutside && current is not null && current.Id != state.SwitchedTo)
			{
				next = next with { DefaultChangedOutside = true };
				logs.Add($"Stream mode: the default speaker was changed to {current.Name ?? "another device"} outside Core; Core leaves it there and will not move it back.");
			}
		}
		else
		{
			// An unmute from outside Core wins: that endpoint leaves Core's record.
			var unmuted = state.Muted.Where(id => endpoints.Any(endpoint => endpoint.Id == id && !endpoint.Muted)).ToList();
			if (unmuted.Count > 0)
			{
				next = next with { Muted = state.Muted.Except(unmuted).ToList() };
				logs.Add("Stream mode: a TV speaker Core had muted was unmuted outside Core; left live.");
			}
			var appeared = endpoints.Where(endpoint => !state.Seen.Contains(endpoint.Id) && IsTvSpeaker(endpoint) && !endpoint.Muted).ToList();
			if (appeared.Count > 0)
			{
				next = next with { Muted = next.Muted.Concat(appeared.Select(endpoint => endpoint.Id)).ToList(), Mode = "muted" };
				changes.AddRange(appeared.Select(endpoint => new SpeakerChange(endpoint.Id, true)));
				logs.Add($"Stream mode: a TV speaker appeared ({string.Join(", ", appeared.Select(endpoint => endpoint.Name ?? "HDMI"))}); muted it too.");
			}
		}
		var newcomers = endpoints.Select(endpoint => endpoint.Id).Where(id => !next.Seen.Contains(id)).ToList();
		if (newcomers.Count > 0) next = next with { Seen = next.Seen.Concat(newcomers).ToList() };
		return new(next, null, changes, logs.Count == 0 ? null : string.Join(" ", logs));
	}

	// Stream mode is off: put back exactly what Core changed, and nothing it did not.
	internal static StreamAudioPlan Release(StreamAudioState state, IReadOnlyList<SpeakerEndpoint> endpoints)
	{
		if (!state.Engaged) return Settle(state, endpoints);
		var logs = new List<string>();
		string? setDefault = null;
		if (state.Mode == "virtual" && state.SwitchedTo is not null)
		{
			SpeakerEndpoint? current = endpoints.FirstOrDefault(endpoint => endpoint.IsDefault);
			SpeakerEndpoint? before = state.SwitchedFrom is null ? null : endpoints.FirstOrDefault(endpoint => endpoint.Id == state.SwitchedFrom);
			if (state.DefaultChangedOutside || current?.Id != state.SwitchedTo) logs.Add("Stream mode ended: the default speaker was changed outside Core meanwhile; left as it is.");
			else if (before is null) logs.Add("Stream mode ended: the speaker that was the default before is not connected; the default is left on the stream's virtual speakers.");
			else { setDefault = before.Id; logs.Add($"Stream mode ended: the PC's sound moves back to {before.Name ?? "the previous default"}."); }
		}
		var changes = new List<SpeakerChange>();
		var pending = new List<string>(state.Pending);
		foreach (string id in state.Muted)
		{
			SpeakerEndpoint? endpoint = endpoints.FirstOrDefault(item => item.Id == id);
			if (endpoint is null) { if (!pending.Contains(id)) pending.Add(id); continue; }
			if (endpoint.Muted) changes.Add(new SpeakerChange(id, false));
		}
		if (changes.Count > 0) logs.Add($"Stream mode ended: the TV's speaker is unmuted ({changes.Count}).");
		if (pending.Count > state.Pending.Count) logs.Add("Stream mode ended: a TV speaker Core had muted is not connected; it is unmuted when it comes back.");
		return new(new StreamAudioState { Pending = pending }, setDefault, changes, logs.Count == 0 ? null : string.Join(" ", logs));
	}

	// Not engaged: a pending endpoint that is back gets its own (live) setting again.
	private static StreamAudioPlan Settle(StreamAudioState state, IReadOnlyList<SpeakerEndpoint> endpoints)
	{
		if (state.Pending.Count == 0) return new(state, null, Array.Empty<SpeakerChange>(), null);
		var back = state.Pending.Where(id => endpoints.Any(endpoint => endpoint.Id == id)).ToList();
		if (back.Count == 0) return new(state, null, Array.Empty<SpeakerChange>(), null);
		var changes = back.Where(id => endpoints.First(endpoint => endpoint.Id == id).Muted).Select(id => new SpeakerChange(id, false)).ToArray();
		return new(state with { Pending = state.Pending.Except(back).ToList() }, null, changes,
			changes.Length > 0 ? "Stream mode: a TV speaker Core had muted is back; unmuted it." : null);
	}

	// Failed changes stay in the record so the next check retries them (and a failed mute is not restored).
	internal static StreamAudioState KeepFailed(StreamAudioState next, IReadOnlyCollection<SpeakerChange> failed)
	{
		if (failed.Count == 0) return next;
		var muted = next.Muted.Where(id => !failed.Any(change => change.Mute && change.Id == id)).ToList();
		var pending = next.Pending.Concat(failed.Where(change => !change.Mute).Select(change => change.Id)).Distinct().ToList();
		return next with { Muted = muted, Pending = pending };
	}
}
