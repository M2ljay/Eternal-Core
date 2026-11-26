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
//    Unmuting restores the endpoints that are present. One that is unplugged at that moment keeps
//    its record ("pending") and gets its own mute back when it reappears: Windows keeps an
//    endpoint's mute while it is unplugged, so without this a headset unplugged while muted would
//    come back silently muted. A pending record is dropped only once it has been restored.
//    State: muted when every default microphone is muted, live when none is, partial when only
//    some are (shown like muted, with a warning). The button follows the Communications default
//    (the one calls use) when the two defaults differ.
// 2. Xbox party. Microsoft offers no API for parties; Core opens the Game Bar (where the Xbox
//    Social widget and its party live) and watches it close to hand the screen back to Big Picture.
//    Win+G (a synthetic keystroke) is sent only when Core knows no game is running and the bar's
//    state is known; otherwise Core launches the documented ms-gamebar://launch/ URI (no keystroke
//    reaches a game, anti-cheat or not), and if that fails says so instead of falling back to keys.
//    A user-configured ms-gamebar: URI (gaming.voicePartyUri) replaces the default URI.

internal enum MicState { None, Live, Muted, Partial }

// One active capture endpoint. IsDefault: the default for the Console or Communications role;
// IsCommunications: the Communications default (the one voice calls use).
internal sealed record MicEndpoint(string Id, bool Muted, bool IsDefault, bool IsCommunications = false);

// What Core remembers between restarts (%APPDATA%\GamingMode\mic-mute.json).
internal sealed record MicMuteState
{
	public bool CoreMuted { get; init; }
	// Endpoint id -> its own mute before Core muted it.
	public Dictionary<string, bool> Before { get; init; } = new(StringComparer.Ordinal);
	// After an unmute: endpoints that were absent then, id -> the mute to give back when they reappear.
	public Dictionary<string, bool> Pending { get; init; } = new(StringComparer.Ordinal);

	// Records equal by content (the agent saves only on a change).
	public bool Equals(MicMuteState? other) =>
		other is not null && CoreMuted == other.CoreMuted && SameMap(Before, other.Before) && SameMap(Pending, other.Pending);

	public override int GetHashCode() => HashCode.Combine(CoreMuted, Before.Count, Pending.Count);

	private static bool SameMap(Dictionary<string, bool> left, Dictionary<string, bool> right) =>
		left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out bool value) && value == pair.Value);

	// Nothing to remember: the file can go.
	public bool IsEmpty => !CoreMuted && Pending.Count == 0;
}

internal sealed record MicChange(string Id, bool Mute);

internal sealed record MicPlan(MicMuteState Next, IReadOnlyList<MicChange> Changes, string? Log);

internal static class MicMutePolicy
{
	internal const int MaxRemembered = 16;

	internal static MicState Observe(IReadOnlyList<MicEndpoint> endpoints)
	{
		var defaults = Distinct(endpoints).Where(endpoint => endpoint.IsDefault).ToList();
		if (defaults.Count == 0) return MicState.None;
		if (defaults.All(endpoint => endpoint.Muted)) return MicState.Muted;
		return defaults.Any(endpoint => endpoint.Muted) ? MicState.Partial : MicState.Live;
	}

	// Which way the button goes: the Communications default decides when the defaults disagree
	// (calls use it); without one, partial counts as live, so the button mutes everything.
	internal static bool ActsMuted(IReadOnlyList<MicEndpoint> endpoints)
	{
		MicState state = Observe(endpoints);
		if (state != MicState.Partial) return state == MicState.Muted;
		MicEndpoint? communications = Distinct(endpoints).FirstOrDefault(endpoint => endpoint.IsCommunications);
		return communications?.Muted ?? false;
	}

	internal static string Code(MicState state) => state switch { MicState.Muted => "muted", MicState.Live => "live", MicState.Partial => "partial", _ => "none" };

	// "muted" or "live" for the Communications default; null when there is none.
	internal static string? CommunicationsCode(IReadOnlyList<MicEndpoint> endpoints) =>
		Distinct(endpoints).FirstOrDefault(endpoint => endpoint.IsCommunications) is { } communications ? communications.Muted ? "muted" : "live" : null;

	internal static MicPlan Mute(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		var before = new Dictionary<string, bool>(state.CoreMuted ? state.Before : new Dictionary<string, bool>(), StringComparer.Ordinal);
		// A pending endpoint (absent since the last unmute) keeps its own value as the one to restore.
		foreach ((string id, bool wasMuted) in state.Pending) before.TryAdd(id, wasMuted);
		var changes = new List<MicChange>();
		foreach (MicEndpoint endpoint in Distinct(endpoints).Where(endpoint => endpoint.IsDefault))
		{
			if (!before.ContainsKey(endpoint.Id)) before[endpoint.Id] = endpoint.Muted;
			if (!endpoint.Muted) changes.Add(new MicChange(endpoint.Id, true));
		}
		if (before.Count == 0) return new MicPlan(state, changes, null); // no microphone: nothing to remember
		return new MicPlan(new MicMuteState { CoreMuted = true, Before = Trim(before, endpoints) }, changes, null);
	}

	// Core's mute on: each recorded endpoint that is present goes back to its own value; an absent one
	// stays recorded as pending and is restored when it reappears (Reconcile). Endpoints Core never
	// touched keep theirs. Core's mute off (the microphone was muted elsewhere): "unmute" means a live
	// microphone, so every muted default is unmuted.
	internal static MicPlan Unmute(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		var present = Distinct(endpoints).ToDictionary(endpoint => endpoint.Id, StringComparer.Ordinal);
		var changes = new List<MicChange>();
		var pending = new Dictionary<string, bool>(StringComparer.Ordinal);
		if (state.CoreMuted)
		{
			foreach ((string id, bool wasMuted) in state.Before)
			{
				if (present.TryGetValue(id, out MicEndpoint? endpoint)) { if (endpoint.Muted != wasMuted) changes.Add(new MicChange(id, wasMuted)); }
				else pending[id] = wasMuted;
			}
		}
		else
		{
			foreach (MicEndpoint endpoint in present.Values.Where(endpoint => endpoint.IsDefault && endpoint.Muted)) changes.Add(new MicChange(endpoint.Id, false));
		}
		// Older pending records stay until their endpoint comes back.
		foreach ((string id, bool wasMuted) in state.Pending)
		{
			if (pending.ContainsKey(id)) continue;
			if (present.TryGetValue(id, out MicEndpoint? endpoint)) { if (endpoint.Muted != wasMuted && !changes.Any(change => change.Id == id)) changes.Add(new MicChange(id, wasMuted)); }
			else pending[id] = wasMuted;
		}
		string? log = pending.Count > 0 ? $"{pending.Count} microphone(s) Core had muted are unplugged; each gets its own setting back when it returns." : null;
		return new MicPlan(new MicMuteState { Pending = TrimPending(pending) }, changes, log);
	}

	internal static MicPlan Toggle(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints) =>
		ActsMuted(endpoints) ? Unmute(state, endpoints) : Mute(state, endpoints);

	// Run on a default-device change, on resume and every few seconds while Core's mute is on or an
	// endpoint is pending.
	internal static MicPlan Reconcile(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		if (!state.CoreMuted) return RestorePending(state, endpoints);
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

	// Core's mute off: a pending endpoint that is back gets its own mute and its record is dropped.
	private static MicPlan RestorePending(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints)
	{
		if (state.Pending.Count == 0) return new MicPlan(state, Array.Empty<MicChange>(), null);
		var present = Distinct(endpoints).ToDictionary(endpoint => endpoint.Id, StringComparer.Ordinal);
		var changes = new List<MicChange>();
		var pending = new Dictionary<string, bool>(StringComparer.Ordinal);
		foreach ((string id, bool wasMuted) in state.Pending)
		{
			if (!present.TryGetValue(id, out MicEndpoint? endpoint)) { pending[id] = wasMuted; continue; }
			if (endpoint.Muted != wasMuted) changes.Add(new MicChange(id, wasMuted));
		}
		if (pending.Count == state.Pending.Count) return new MicPlan(state, changes, null);
		return new MicPlan(state with { Pending = pending }, changes, "A microphone Core had muted is back; it has its own setting again.");
	}

	// A pending record goes only once its endpoint really has its setting back: when Windows refuses
	// the change (failedIds), the record stays pending for the next check.
	internal static MicMuteState KeepUnrestored(MicMuteState planned, MicMuteState previous, IEnumerable<string> failedIds)
	{
		var failed = failedIds.Where(id => previous.Pending.ContainsKey(id) && !planned.Pending.ContainsKey(id)).ToList();
		if (failed.Count == 0 || planned.CoreMuted) return planned;
		var pending = new Dictionary<string, bool>(planned.Pending, StringComparer.Ordinal);
		foreach (string id in failed) pending[id] = previous.Pending[id];
		return planned with { Pending = pending };
	}

	private static IEnumerable<MicEndpoint> Distinct(IEnumerable<MicEndpoint> endpoints) =>
		endpoints.Where(endpoint => !string.IsNullOrEmpty(endpoint.Id))
			.GroupBy(endpoint => endpoint.Id, StringComparer.Ordinal)
			.Select(group => group.First() with { IsDefault = group.Any(endpoint => endpoint.IsDefault), IsCommunications = group.Any(endpoint => endpoint.IsCommunications) });

	private static Dictionary<string, bool> TrimPending(Dictionary<string, bool> pending) =>
		pending.Count <= MaxRemembered ? pending : pending.Take(MaxRemembered).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

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
			return state with
			{
				Before = new Dictionary<string, bool>(state.Before ?? new Dictionary<string, bool>(), StringComparer.Ordinal),
				Pending = new Dictionary<string, bool>(state.Pending ?? new Dictionary<string, bool>(), StringComparer.Ordinal),
			};
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new MicMuteState(); }
	}

	internal static void Save(string path, MicMuteState state)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		if (state.IsEmpty) { try { File.Delete(path); } catch (IOException) { } return; }
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
	// Any game, detected or not. Null: Core cannot tell, which counts as a game running.
	public bool? GameRunning { get; init; }
	// Core opened the bar moments ago (it may still be animating in, not yet reporting open).
	public bool RecentlySent { get; init; }
	// Config-only override (gaming.voicePartyUri), for a widget URI found to work on this PC.
	public string? OverrideUri { get; init; }
}

internal static class PartyLaunchPolicy
{
	// Microsoft's documented Game Bar URI template (learn.microsoft.com/gaming/game-bar/api/xgb-widgetcontrol):
	// ms-gamebar:[//coreCommand/]..., where the "launch" core command opens Game Bar normally.
	internal const string DefaultUri = "ms-gamebar://launch/";
	internal static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(2);

	internal static PartyLaunchChoice Choose(PartyLaunchInputs input)
	{
		if (input.GameBarInstalled == false) return new(PartyLaunchMethod.Unavailable, "notInstalled");
		// Nothing at all is sent while a game protected by kernel anti-cheat runs.
		if (input.ProtectedGame) return new(PartyLaunchMethod.Unavailable, "protectedGame");
		// Win+G toggles, and a second launch right after the first could close the bar again.
		if (input.GameBarInteractive == true) return new(PartyLaunchMethod.AlreadyOpen, "alreadyOpen");
		if (input.RecentlySent) return new(PartyLaunchMethod.AlreadyOpen, "cooldown");
		bool configured = ValidGameBarUri(input.OverrideUri);
		string uri = configured ? input.OverrideUri!.Trim() : DefaultUri;
		if (configured) return new(PartyLaunchMethod.Uri, "override", uri);
		// A synthetic keystroke never goes into a game, detected by anti-cheat safe mode or not.
		if (input.GameRunning != false) return new(PartyLaunchMethod.Uri, input.GameRunning is null ? "gameUnknown" : "gameRunning", uri);
		// An unknown bar state could be an open bar that does not have the input: Win+G would close it.
		if (input.GameBarInteractive is null) return new(PartyLaunchMethod.Uri, "stateUnknown", uri);
		return new(PartyLaunchMethod.WinG, "shortcut");
	}

	// The URI could not be launched. Win+G only when it would have been chosen anyway: no game for sure
	// and the bar's state known; otherwise Core says the Game Bar could not be opened.
	internal static PartyLaunchChoice AfterUriFailure(PartyLaunchInputs input) =>
		input.GameRunning == false && input.GameBarInteractive == false && !input.ProtectedGame
			? new(PartyLaunchMethod.WinG, "uriFailed")
			: new(PartyLaunchMethod.Unavailable, "uriFailed");

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
