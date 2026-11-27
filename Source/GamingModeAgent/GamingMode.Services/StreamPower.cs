using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core batch 2 (B13, docs/eternal/BATCH-2.md): sleep after streaming. When a handheld's stream ends (Moonlight
// or Artemis through Vibepollo, Apollo or Sunshine, or Steam Remote Play / Steam Link) and nobody uses Core here,
// Core sleeps the PC after a delay (10 minutes by default). Sleep, never shutdown: a running game stays suspended in
// memory, and the handheld wakes the PC again with Wake-on-LAN (Moonlight's "Wake PC"). Ideas from HostSleep
// (decky-hostsleep, sleep the host when the Deck suspends) and CouchLink (auto-sleep after streaming), both BSD-3;
// no code was taken. Pure here; the wiring is MarkIIIHost.StreamPower.cs.
internal sealed record StreamSleepState
{
	// A stream was seen in the last round (the next round without one arms the timer).
	public bool WasStreaming { get; init; }
	public string? LastSource { get; init; }
	public DateTimeOffset? ArmedAt { get; init; }
	public bool Warned { get; init; }
	public DateTimeOffset? LastSleepAt { get; init; }
}

internal sealed record StreamSleepInputs
{
	public DateTimeOffset Now { get; init; }
	public bool Enabled { get; init; }
	public int DelayMinutes { get; init; } = StreamSleepPolicy.DefaultDelayMinutes;
	public bool InGamingMode { get; init; }
	// A handheld stream right now: a Sunshine/Apollo/Vibepollo client connected or Steam Remote Play reported.
	public bool Streaming { get; init; }
	public string? Source { get; init; }
	// The newest local input Core knows of: Windows' last keyboard/mouse input, and the Steam controller input Core's
	// Decky plugin reports with its heartbeat.
	public DateTimeOffset? LastLocalInput { get; init; }
	// Something that must not be interrupted: a Core job (Steam or Decky update), update night.
	public bool Busy { get; init; }
}

internal enum StreamSleepAction { Nothing, Armed, Disarmed, Warn, Sleep, Wait }

internal sealed record StreamSleepDecision(StreamSleepAction Action, StreamSleepState Next, string Reason, DateTimeOffset? SleepAt);

internal static class StreamSleepPolicy
{
	internal const int DefaultDelayMinutes = 10;
	internal static readonly TimeSpan WarnBefore = TimeSpan.FromMinutes(1);
	// Input in the first seconds after the stream ended is the stream's own (Moonlight injects keyboard and mouse).
	internal static readonly TimeSpan InputGrace = TimeSpan.FromSeconds(10);
	// A timer much older than its delay (the PC slept or the agent stalled) is dropped rather than acted on.
	internal static readonly TimeSpan Stale = TimeSpan.FromMinutes(30);

	internal static int CleanDelay(int minutes) => minutes is >= 1 and <= 120 ? minutes : DefaultDelayMinutes;

	internal static StreamSleepDecision Decide(StreamSleepInputs input, StreamSleepState state)
	{
		DateTimeOffset now = input.Now;
		TimeSpan delay = TimeSpan.FromMinutes(CleanDelay(input.DelayMinutes));
		StreamSleepState Disarmed(bool streaming) => state with { ArmedAt = null, Warned = false, WasStreaming = streaming, LastSource = streaming ? input.Source : state.LastSource };
		if (!input.Enabled || !input.InGamingMode)
			return state.ArmedAt is null ? new(StreamSleepAction.Nothing, Disarmed(input.Streaming), "off", null)
				: new(StreamSleepAction.Disarmed, Disarmed(input.Streaming), !input.Enabled ? "turned off" : "left Gaming Mode", null);
		if (input.Streaming)
			return state.ArmedAt is null ? new(StreamSleepAction.Nothing, Disarmed(true), "streaming", null)
				: new(StreamSleepAction.Disarmed, Disarmed(true), "the stream came back", null);
		if (state.WasStreaming && state.ArmedAt is null)
		{
			StreamSleepState armed = state with { WasStreaming = false, ArmedAt = now, Warned = false };
			return new(StreamSleepAction.Armed, armed, $"the stream through {state.LastSource ?? "a handheld"} ended", now + delay);
		}
		if (state.ArmedAt is not { } at) return new(StreamSleepAction.Nothing, state, "no stream ended", null);
		DateTimeOffset sleepAt = at + delay;
		if (now < at || now - sleepAt > Stale) return new(StreamSleepAction.Disarmed, Disarmed(false), "the timer is stale", null);
		if (input.LastLocalInput is { } used && used > at + InputGrace && used <= now + TimeSpan.FromMinutes(1))
			return new(StreamSleepAction.Disarmed, Disarmed(false), "someone is using Core here", null);
		if (input.Busy) return new(StreamSleepAction.Wait, state, "Core is busy", sleepAt);
		if (now >= sleepAt) return new(StreamSleepAction.Sleep, state with { ArmedAt = null, Warned = false, LastSleepAt = now }, "nobody used Core after the stream", sleepAt);
		if (!state.Warned && now >= sleepAt - WarnBefore) return new(StreamSleepAction.Warn, state with { Warned = true }, "one minute left", sleepAt);
		return new(StreamSleepAction.Nothing, state, "waiting", sleepAt);
	}

	// Core Health row "Sleep after streaming": off / on (with the delay) / counting down.
	internal static HealthRow Row(bool enabled, int delayMinutes, DateTimeOffset? sleepAt, DateTimeOffset? lastSleepAt)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal) { ["minutes"] = CleanDelay(delayMinutes), ["at"] = sleepAt, ["last"] = lastSleepAt };
		if (!enabled) return new HealthRow("stream-sleep", HealthLevel.Info, "off", data);
		return new HealthRow("stream-sleep", sleepAt is null ? HealthLevel.Ok : HealthLevel.Info, sleepAt is null ? "ready" : "counting", data);
	}

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static StreamSleepState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<StreamSleepState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, StreamSleepState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}

// ---------- Vibepollo's session logs ----------

// Vibepollo (github.com/Nonary/Vibepollo, an Apollo fork) installs into Apollo's folder (its WiX package keeps
// CPACK_PACKAGE_INSTALL_DIRECTORY "Apollo") but writes one log per run, next to the configured log file:
// config\logs\sunshine-YYYYMMDD-HHMMSS-mmm.log, rolled over in about 2 MB pieces (.log.1, .log.2...; the current piece
// keeps the name and starts empty). Its log lines are Sunshine's ("CLIENT CONNECTED" / "CLIENT DISCONNECTED",
// src/stream.cpp). Verified in Vibepollo's source (src/logging.cpp, cmake/packaging/windows.cmake), 2026-11.
internal static class VibepolloLogs
{
	internal static bool IsSessionLog(string fileName) => SunshineLogTail.IsSessionLog(fileName);

	// The current run's log: the newest session log by last write (rolled-over pieces end in .log.N and do not match).
	internal static string? Newest(IEnumerable<(string Path, DateTime LastWrite)> files) =>
		files.Where(file => IsSessionLog(System.IO.Path.GetFileName(file.Path))).OrderByDescending(file => file.LastWrite).Select(file => file.Path).FirstOrDefault();
}

// ---------- Wake-on-LAN readiness ----------

internal sealed record NetworkAdapterWake(string Name, bool Wired, bool Up, bool? MagicPacket, bool? PowerManagementOff, bool? WakeArmed, string? Mac);

internal sealed record WakeOnLanInputs(IReadOnlyList<NetworkAdapterWake>? Adapters, bool? FastStartup);

internal static class WakeOnLanPolicy
{
	// "Wake-on-LAN" row: ready (steel), something to check (amber, information), unknown. Never attention: the PC works
	// either way, only waking it from a handheld does not.
	internal static HealthRow Row(WakeOnLanInputs input)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal) { ["fastStartup"] = input.FastStartup };
		if (input.Adapters is not { } adapters) return new HealthRow("wake-on-lan", HealthLevel.Unknown, "unknown", data);
		NetworkAdapterWake? wired = adapters.Where(adapter => adapter.Wired).OrderByDescending(adapter => adapter.Up).FirstOrDefault();
		if (wired is null)
			return new HealthRow("wake-on-lan", HealthLevel.Info, adapters.Any(adapter => adapter.Up) ? "wifi-only" : "no-adapter", data);
		data["adapter"] = wired.Name;
		data["mac"] = wired.Mac;
		var problems = new List<string>();
		if (!wired.Up) problems.Add("cable");
		if (wired.MagicPacket == false) problems.Add("magic-packet");
		if (wired.PowerManagementOff == true) problems.Add("power-management");
		if (wired.WakeArmed == false) problems.Add("wake-armed");
		data["problems"] = problems;
		if (problems.Count > 0) return new HealthRow("wake-on-lan", HealthLevel.Info, "check", data);
		if (wired.MagicPacket is null && wired.WakeArmed is null) return new HealthRow("wake-on-lan", HealthLevel.Unknown, "unknown", data);
		return new HealthRow("wake-on-lan", HealthLevel.Ok, "ready", data);
	}

	// Registry values of a network adapter's class key: "*WakeOnMagicPacket" ("1"/"0", NDIS standardized keyword) and
	// PnPCapabilities (0x18 or 0x10 set: "Allow the computer to turn off this device" unchecked, wake disabled with it).
	internal static bool? MagicPacket(object? value) => value switch
	{
		string text when text.Trim() == "1" => true,
		string text when text.Trim() == "0" => false,
		int number => number != 0,
		_ => null,
	};

	internal static bool? PowerManagementOff(object? value) => value is int capabilities ? (capabilities & 0x10) != 0 : null;

	// `powercfg /devicequery wake_armed` prints one device name per line ("NONE" when there is none).
	internal static IReadOnlyList<string> WakeArmed(string output) =>
		output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.Equals("NONE", StringComparison.OrdinalIgnoreCase)).ToList();
}
