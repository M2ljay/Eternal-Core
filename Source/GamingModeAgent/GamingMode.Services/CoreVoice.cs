using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core: "Voice" in Quick Access > Core. Pure rules, no Windows calls: the agent observes
// (CoreVoice.Windows.cs) and applies (MarkIIIHost.Voice.cs); Source/CoreVoice.Tests runs these on Linux.
//
// 1. Microphone mute. Core mutes every default capture endpoint (Windows keeps one for the Console
//    role and one for Communications; they can differ) and records each one's own mute first, so
//    unmuting puts each endpoint back exactly as it was. While Core's mute is on:
//    - a microphone that becomes a default (a headset plugged in, a change in Windows, a new device
//      after sleep) is muted too and recorded;
//    - if an endpoint Core muted is found unmuted (someone unmuted it in Windows, or a driver reset
//      it), Core does not fight it: its mute ends, the other endpoints are restored, and the
//      indicator shows the microphone live. The top-bar badge always shows the actual state.
// 2. Xbox party. Microsoft offers no API for parties; Core opens the Game Bar (where the Xbox
//    Social widget and its party live) with the Win+G shortcut, or a user-configured ms-gamebar:
//    URI, and watches it close to hand the screen back to Big Picture.

internal enum MicState { None, Live, Muted }

// One active capture endpoint. IsDefault: the default for the Console or Communications role.
internal sealed record MicEndpoint(string Id, bool Muted, bool IsDefault);

// What Core remembers between restarts (%APPDATA%\GamingMode\mic-mute.json).
internal sealed record MicMuteState
{
	public bool CoreMuted { get; init; }
	// Endpoint id -> its own mute before Core muted it.
	public Dictionary<string, bool> Before { get; init; } = new(StringComparer.Ordinal);
}

internal sealed record MicChange(string Id, bool Mute);

internal sealed record MicPlan(MicMuteState Next, IReadOnlyList<MicChange> Changes, string? Log);

internal static class MicMutePolicy
{
	internal const int MaxRemembered = 16;

	internal static MicState Observe(IReadOnlyList<MicEndpoint> endpoints)
	{
		var defaults = endpoints.Where(endpoint => endpoint.IsDefault).ToList();
		if (defaults.Count == 0) return MicState.None;
		return defaults.All(endpoint => endpoint.Muted) ? MicState.Muted : MicState.Live;
	}

	internal static string Code(MicState state) => state switch { MicState.Muted => "muted", MicState.Live => "live", _ => "none" };

	internal static MicPlan Mute(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		var before = new Dictionary<string, bool>(state.CoreMuted ? state.Before : new Dictionary<string, bool>(), StringComparer.Ordinal);
		var changes = new List<MicChange>();
		foreach (MicEndpoint endpoint in Distinct(endpoints).Where(endpoint => endpoint.IsDefault))
		{
			if (!before.ContainsKey(endpoint.Id)) before[endpoint.Id] = endpoint.Muted;
			if (!endpoint.Muted) changes.Add(new MicChange(endpoint.Id, true));
		}
		if (before.Count == 0) return new MicPlan(state, changes, null); // no microphone: nothing to remember
		return new MicPlan(new MicMuteState { CoreMuted = true, Before = Trim(before, endpoints) }, changes, null);
	}

	// Core's mute on: each recorded endpoint that is still present goes back to its own value.
	// Endpoints Core never touched keep theirs. Core's mute off (the microphone was muted
	// elsewhere): "unmute" means a live microphone, so every muted default is unmuted.
	internal static MicPlan Unmute(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		var present = Distinct(endpoints).ToDictionary(endpoint => endpoint.Id, StringComparer.Ordinal);
		var changes = new List<MicChange>();
		if (state.CoreMuted)
		{
			foreach ((string id, bool wasMuted) in state.Before)
				if (present.TryGetValue(id, out MicEndpoint? endpoint) && endpoint.Muted != wasMuted) changes.Add(new MicChange(id, wasMuted));
		}
		else
		{
			foreach (MicEndpoint endpoint in present.Values.Where(endpoint => endpoint.IsDefault && endpoint.Muted)) changes.Add(new MicChange(endpoint.Id, false));
		}
		return new MicPlan(new MicMuteState(), changes, null);
	}

	internal static MicPlan Toggle(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints) =>
		Observe(endpoints) == MicState.Muted ? Unmute(state, endpoints) : Mute(state, endpoints);

	// Run on a default-device change, on resume and every few seconds while Core's mute is on.
	internal static MicPlan Reconcile(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		if (!state.CoreMuted) return new MicPlan(state, Array.Empty<MicChange>(), null);
		List<MicEndpoint> present = Distinct(endpoints).ToList();
		MicEndpoint? unmutedElsewhere = present.FirstOrDefault(endpoint => endpoint.IsDefault && !endpoint.Muted && state.Before.ContainsKey(endpoint.Id));
		if (unmutedElsewhere is not null)
		{
			MicPlan restore = Unmute(state, endpoints);
			return restore with { Log = "A microphone Core had muted was unmuted outside Core; Core's mute ends and the others are restored." };
		}
		List<MicEndpoint> fresh = present.Where(endpoint => endpoint.IsDefault && !state.Before.ContainsKey(endpoint.Id)).ToList();
		if (fresh.Count == 0) return new MicPlan(state, Array.Empty<MicChange>(), null);
		MicPlan mute = Mute(state, endpoints);
		return mute with { Log = fresh.Any(endpoint => !endpoint.Muted) ? "A new default microphone appeared while Core's mute is on; muted it too." : null };
	}

	private static IEnumerable<MicEndpoint> Distinct(IEnumerable<MicEndpoint> endpoints) =>
		endpoints.Where(endpoint => !string.IsNullOrEmpty(endpoint.Id))
			.GroupBy(endpoint => endpoint.Id, StringComparer.Ordinal)
			.Select(group => group.First() with { IsDefault = group.Any(endpoint => endpoint.IsDefault) });

	// Unplugged endpoints stay remembered (they may come back before the unmute), oldest first out.
	private static Dictionary<string, bool> Trim(Dictionary<string, bool> before, IReadOnlyList<MicEndpoint> endpoints)
	{
		if (before.Count <= MaxRemembered) return before;
		var present = new HashSet<string>(endpoints.Select(endpoint => endpoint.Id), StringComparer.Ordinal);
		var keep = before.Where(pair => present.Contains(pair.Key)).Concat(before.Where(pair => !present.Contains(pair.Key)).Reverse()).Take(MaxRemembered);
		return keep.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
	}

	// ---------- persistence ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static MicMuteState Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new MicMuteState();
			MicMuteState? state = JsonSerializer.Deserialize<MicMuteState>(File.ReadAllText(path), Json);
			if (state is null) return new MicMuteState();
			return state with { Before = new Dictionary<string, bool>(state.Before ?? new Dictionary<string, bool>(), StringComparer.Ordinal) };
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new MicMuteState(); }
	}

	internal static void Save(string path, MicMuteState state)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		if (!state.CoreMuted) { try { File.Delete(path); } catch (IOException) { } return; }
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}

// ---------- Xbox party: how to open the Game Bar ----------

internal enum PartyLaunchMethod { WinG, Uri, AlreadyOpen, Unavailable }

internal sealed record PartyLaunchChoice(PartyLaunchMethod Method, string Reason, string? Uri = null);

internal sealed record PartyLaunchInputs
{
	// Null: Windows could not say (Core tries anyway).
	public bool? GameBarInstalled { get; init; }
	// Windows.Gaming.UI.GameBar.IsInputRedirected: the bar is open and has the input. Null: unknown.
	public bool? GameBarInteractive { get; init; }
	public bool ProtectedGame { get; init; }
	// Config-only override (gaming.voicePartyUri), for a widget URI found to work on this PC.
	public string? OverrideUri { get; init; }
}

internal static class PartyLaunchPolicy
{
	internal static PartyLaunchChoice Choose(PartyLaunchInputs input)
	{
		if (input.GameBarInstalled == false) return new(PartyLaunchMethod.Unavailable, "notInstalled");
		// Synthetic keystrokes into a game protected by kernel anti-cheat are not worth the risk.
		if (input.ProtectedGame) return new(PartyLaunchMethod.Unavailable, "protectedGame");
		// Win+G toggles: sending it to an open bar would close it.
		if (input.GameBarInteractive == true) return new(PartyLaunchMethod.AlreadyOpen, "alreadyOpen");
		if (ValidGameBarUri(input.OverrideUri)) return new(PartyLaunchMethod.Uri, "override", input.OverrideUri!.Trim());
		return new(PartyLaunchMethod.WinG, input.GameBarInteractive is null ? "stateUnknown" : "shortcut");
	}

	// Only ms-gamebar: URIs; anything else (a web link, a file, an exe) is ignored.
	internal static bool ValidGameBarUri(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return false;
		string text = value.Trim();
		if (text.Length > 512 || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '"')) return false;
		return System.Uri.TryCreate(text, UriKind.Absolute, out System.Uri? uri) && string.Equals(uri.Scheme, "ms-gamebar", StringComparison.OrdinalIgnoreCase);
	}
}

// ---------- Xbox party: handing the screen back to Big Picture ----------

internal enum GameBarWatchAction { None, Refocus, Stop }

// Started when Core opened the bar. RefocusOnClose: Big Picture was in front (no game) then.
internal sealed record GameBarWatch(DateTimeOffset Started, bool RefocusOnClose, bool SeenOpen = false);

internal static class GameBarWatchPolicy
{
	internal static readonly TimeSpan OpenWait = TimeSpan.FromSeconds(6);
	internal static readonly TimeSpan MaxWatch = TimeSpan.FromHours(6);
	internal static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(300);

	internal static (GameBarWatch Next, GameBarWatchAction Action) Step(GameBarWatch watch, DateTimeOffset now, bool? interactive, bool steamForeground, bool gameRunning)
	{
		if (now - watch.Started > MaxWatch) return (watch, GameBarWatchAction.Stop);
		if (interactive == true) return (watch.SeenOpen ? watch : watch with { SeenOpen = true }, GameBarWatchAction.None);
		if (!watch.SeenOpen) return (watch, now - watch.Started > OpenWait ? GameBarWatchAction.Stop : GameBarWatchAction.None);
		if (interactive is null) return (watch, GameBarWatchAction.None);
		// Closed. Windows normally gives the foreground back by itself; Core steps in only when
		// Big Picture was in front before, is not now, and no game has started meanwhile.
		bool refocus = watch.RefocusOnClose && !steamForeground && !gameRunning;
		return (watch, refocus ? GameBarWatchAction.Refocus : GameBarWatchAction.Stop);
	}
}
