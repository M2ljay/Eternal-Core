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
// 3. Mute scope. "Mute everything" (the default) is item 1. "Mute calls only" mutes just the
//    Communications default, so the Console default (games, apps, recording) stays live. Windows
//    mutes an endpoint for every app at once, so any app recording from the calls mic is muted too.
// 4. Calls mic. The user picks a capture device to be the Communications default (optionally the
//    Console default too). Core remembers it by endpoint id and makes it the default again when it
//    reappears, unless the user chose another default in Windows meanwhile (CallsMicPolicy).

internal enum MicState { None, Live, Muted, Partial }

// Which endpoints Core's mute covers: every default (All) or only the Communications default (Calls).
internal enum MicScope { All, Calls }

// One active capture endpoint. IsDefault: the default for the Console or Communications role;
// IsCommunications: the Communications default (the one voice calls use); IsConsole: the Console
// default (games, apps). Name: Windows' friendly name, for the picker only.
internal sealed record MicEndpoint(string Id, bool Muted, bool IsDefault, bool IsCommunications = false, bool IsConsole = false, string? Name = null);

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

	// The endpoints the mute covers. Calls scope: the Communications default; when Windows reports
	// none, every default (so the button never does nothing while a microphone is live).
	internal static List<MicEndpoint> Targets(IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All)
	{
		List<MicEndpoint> present = Distinct(endpoints).ToList();
		if (scope == MicScope.Calls && present.Any(endpoint => endpoint.IsCommunications)) return present.Where(endpoint => endpoint.IsCommunications).ToList();
		return present.Where(endpoint => endpoint.IsDefault).ToList();
	}

	internal static MicState Observe(IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All)
	{
		var defaults = Targets(endpoints, scope);
		if (defaults.Count == 0) return MicState.None;
		if (defaults.All(endpoint => endpoint.Muted)) return MicState.Muted;
		return defaults.Any(endpoint => endpoint.Muted) ? MicState.Partial : MicState.Live;
	}

	// Which way the button goes: the Communications default decides when the defaults disagree
	// (calls use it); without one, partial counts as live, so the button mutes everything.
	internal static bool ActsMuted(IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All)
	{
		MicState state = Observe(endpoints, scope);
		if (state != MicState.Partial) return state == MicState.Muted;
		MicEndpoint? communications = Distinct(endpoints).FirstOrDefault(endpoint => endpoint.IsCommunications);
		return communications?.Muted ?? false;
	}

	internal static string Code(MicState state) => state switch { MicState.Muted => "muted", MicState.Live => "live", MicState.Partial => "partial", _ => "none" };

	// "muted" or "live" for the Communications default; null when there is none.
	internal static string? CommunicationsCode(IReadOnlyList<MicEndpoint> endpoints) =>
		Distinct(endpoints).FirstOrDefault(endpoint => endpoint.IsCommunications) is { } communications ? communications.Muted ? "muted" : "live" : null;

	internal static MicPlan Mute(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All)
	{
		var before = new Dictionary<string, bool>(state.CoreMuted ? state.Before : new Dictionary<string, bool>(), StringComparer.Ordinal);
		// A pending endpoint (absent since the last unmute) keeps its own value as the one to restore.
		foreach ((string id, bool wasMuted) in state.Pending) before.TryAdd(id, wasMuted);
		var changes = new List<MicChange>();
		foreach (MicEndpoint endpoint in Targets(endpoints, scope))
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
	internal static MicPlan Unmute(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All)
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
			foreach (MicEndpoint endpoint in Targets(endpoints, scope).Where(endpoint => endpoint.Muted)) changes.Add(new MicChange(endpoint.Id, false));
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

	internal static MicPlan Toggle(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All) =>
		ActsMuted(endpoints, scope) ? Unmute(state, endpoints, scope) : Mute(state, endpoints, scope);

	// Run on a default-device change, on resume and every few seconds while Core's mute is on or an
	// endpoint is pending.
	internal static MicPlan Reconcile(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints, MicScope scope = MicScope.All)
	{
		if (!state.CoreMuted) return RestorePending(state, endpoints);
		List<MicEndpoint> targets = Targets(endpoints, scope);
		MicEndpoint? unmutedElsewhere = targets.FirstOrDefault(endpoint => !endpoint.Muted && state.Before.ContainsKey(endpoint.Id));
		if (unmutedElsewhere is not null)
		{
			MicPlan restore = Unmute(state, endpoints, scope);
			return restore with { Log = "A microphone Core had muted was unmuted outside Core; Core's mute ends and the others are restored." };
		}
		List<MicEndpoint> fresh = targets.Where(endpoint => !state.Before.ContainsKey(endpoint.Id)).ToList();
		if (fresh.Count == 0) return new MicPlan(state, Array.Empty<MicChange>(), null);
		MicPlan mute = Mute(state, endpoints, scope);
		return mute with { Log = fresh.Any(endpoint => !endpoint.Muted) ? "A new default microphone appeared while Core's mute is on; muted it too." : null };
	}

	// The scope changed while Core's mute is on: as if unmuted (each recorded endpoint gets its own
	// value back, an absent one stays recorded) and muted again under the new scope, in one plan, so
	// nothing is restored twice and no record is lost. Core's mute off: nothing to do.
	internal static MicPlan ChangeScope(MicMuteState state, IReadOnlyList<MicEndpoint> endpoints, MicScope scope)
	{
		if (!state.CoreMuted) return new MicPlan(state, Array.Empty<MicChange>(), null);
		List<MicEndpoint> present = Distinct(endpoints).ToList();
		MicPlan unmute = Unmute(state, present);
		var after = present.Select(endpoint => unmute.Changes.LastOrDefault(change => change.Id == endpoint.Id) is { } change ? endpoint with { Muted = change.Mute } : endpoint).ToList();
		MicPlan mute = Mute(unmute.Next, after, scope);
		var final = after.ToDictionary(endpoint => endpoint.Id, endpoint => endpoint.Muted, StringComparer.Ordinal);
		foreach (MicChange change in mute.Changes) final[change.Id] = change.Mute;
		var changes = present.Where(endpoint => final[endpoint.Id] != endpoint.Muted).Select(endpoint => new MicChange(endpoint.Id, final[endpoint.Id])).ToList();
		return new MicPlan(mute.Next, changes, $"Mute scope is now {(scope == MicScope.Calls ? "calls only" : "everything")}; Core's mute follows it.");
	}

	internal static MicScope ParseScope(string? value) => string.Equals(value, "calls", StringComparison.OrdinalIgnoreCase) ? MicScope.Calls : MicScope.All;

	internal static string ScopeCode(MicScope scope) => scope == MicScope.Calls ? "calls" : "all";

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
			.Select(group => group.First() with
			{
				IsDefault = group.Any(endpoint => endpoint.IsDefault || endpoint.IsCommunications || endpoint.IsConsole),
				IsCommunications = group.Any(endpoint => endpoint.IsCommunications),
				IsConsole = group.Any(endpoint => endpoint.IsConsole),
			});

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

// ---------- calls mic: which capture device is the Communications default ----------

// Windows roles Core sets (ERole): Communications for calls; with "Use for everything", Console (and
// Multimedia with it, as Windows' own Sound panel does) for games and apps.
internal enum MicRole { Communications, Console }

internal sealed record DefaultChange(string Id, MicRole Role);

internal enum DefaultSetResult { Done, Refused, Unavailable }

// Setting a default device. Windows has no documented API for it; the one implementation
// (CoreVoice.Windows.cs, PolicyConfigDefaultMic) uses the undocumented IPolicyConfig COM interface,
// so it stays behind this seam and every failure comes back as a result, never an exception.
internal interface IDefaultMicSetter
{
	DefaultSetResult SetDefault(string id, MicRole role);
}

// What Core remembers (%APPDATA%\GamingMode\voice-settings.json, written only by the agent).
internal sealed record VoiceSettings
{
	public string? CallsMicId { get; init; }
	// Last friendly name seen, shown while the device is absent and used to find it again when
	// Windows gives it a new endpoint id (plugged into another USB port).
	public string? CallsMicName { get; init; }
	public bool UseForEverything { get; init; }
	// "all" (default) or "calls".
	public string MuteScope { get; init; } = "all";
	// Core's choice is in force. False once the user picked another default in Windows (respected
	// until he picks again in Core) or when Windows refused the change.
	public bool Applied { get; init; }
	// While the calls mic is away: role -> the default Windows fell back to ("" for none). Used to
	// tell Windows' own fallback from a choice the user made meanwhile, and to know it was away.
	public Dictionary<string, string> Away { get; init; } = new(StringComparer.Ordinal);
	// Windows refused to set the default the last time Core tried.
	public bool Failed { get; init; }

	public bool Equals(VoiceSettings? other) =>
		other is not null && CallsMicId == other.CallsMicId && CallsMicName == other.CallsMicName && UseForEverything == other.UseForEverything
		&& MuteScope == other.MuteScope && Applied == other.Applied && Failed == other.Failed
		&& Away.Count == other.Away.Count && Away.All(pair => other.Away.TryGetValue(pair.Key, out string? value) && value == pair.Value);

	public override int GetHashCode() => HashCode.Combine(CallsMicId, UseForEverything, MuteScope, Applied, Away.Count);
}

internal sealed record CallsMicPlan(VoiceSettings Next, IReadOnlyList<DefaultChange> Changes, string? Log);

internal static class CallsMicPolicy
{
	internal const int MaxNameLength = 120;

	// "none" (no calls mic chosen), "active" (connected and the default Core set), "absent" (not
	// connected: calls use Windows' own default; information only), "changed" (connected, but the
	// user picked another default in Windows, which Core respects), "failed" (Windows refused).
	internal static string Status(VoiceSettings settings, IReadOnlyList<MicEndpoint> endpoints)
	{
		if (string.IsNullOrEmpty(settings.CallsMicId)) return "none";
		MicEndpoint? mic = Find(settings, endpoints);
		if (mic is null) return "absent";
		if (settings.Failed) return "failed";
		return settings.Applied && Roles(settings).All(role => Holds(mic, role)) ? "active" : "changed";
	}

	internal static IReadOnlyList<MicRole> Roles(VoiceSettings settings) =>
		settings.UseForEverything ? new[] { MicRole.Communications, MicRole.Console } : new[] { MicRole.Communications };

	// By endpoint id; failing that, the one present device with the remembered name (a USB device in
	// another port gets a new endpoint id). Two devices with that name: no guess.
	internal static MicEndpoint? Find(VoiceSettings settings, IReadOnlyList<MicEndpoint> endpoints)
	{
		if (string.IsNullOrEmpty(settings.CallsMicId)) return null;
		MicEndpoint? byId = endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Id, settings.CallsMicId, StringComparison.Ordinal));
		if (byId is not null) return byId;
		if (string.IsNullOrWhiteSpace(settings.CallsMicName)) return null;
		var byName = endpoints.Where(endpoint => string.Equals(endpoint.Name, settings.CallsMicName, StringComparison.Ordinal)).GroupBy(endpoint => endpoint.Id).ToList();
		return byName.Count == 1 ? byName[0].First() : null;
	}

	// The user picks a device in Quick Access. Null when it is not connected (only connected
	// devices are offered). Picking again also clears "changed" and "failed".
	internal static CallsMicPlan? Choose(VoiceSettings settings, IReadOnlyList<MicEndpoint> endpoints, string id, bool everything)
	{
		MicEndpoint? mic = endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Id, id, StringComparison.Ordinal));
		if (mic is null) return null;
		VoiceSettings next = settings with { CallsMicId = mic.Id, CallsMicName = Name(mic) ?? (mic.Id == settings.CallsMicId ? settings.CallsMicName : null), UseForEverything = everything, Applied = true, Failed = false, Away = new(StringComparer.Ordinal) };
		var changes = Roles(next).Where(role => !Holds(mic, role)).Select(role => new DefaultChange(mic.Id, role)).ToList();
		return new CallsMicPlan(next, changes, $"Calls mic set{(everything ? " (also for everything)" : "")}.");
	}

	// Back to Windows' own choice; Windows' defaults are left as they are.
	internal static VoiceSettings Forget(VoiceSettings settings) =>
		settings with { CallsMicId = null, CallsMicName = null, UseForEverything = false, Applied = false, Failed = false, Away = new(StringComparer.Ordinal) };

	// Run on device changes, on resume and every few seconds while a calls mic is chosen.
	internal static CallsMicPlan Step(VoiceSettings settings, IReadOnlyList<MicEndpoint> endpoints)
	{
		var none = Array.Empty<DefaultChange>();
		if (string.IsNullOrEmpty(settings.CallsMicId)) return new CallsMicPlan(settings, none, null);
		MicEndpoint? mic = Find(settings, endpoints);
		IReadOnlyList<MicRole> roles = Roles(settings);
		if (mic is null)
		{
			// Away. Nothing to change (calls use Windows' own default meanwhile); only note which default
			// Windows fell back to, so a choice the user makes in Windows meanwhile can be told apart.
			if (!settings.Applied) return new CallsMicPlan(settings, none, null);
			var away = new Dictionary<string, string>(settings.Away, StringComparer.Ordinal);
			foreach (MicRole role in roles)
			{
				string key = Key(role);
				string current = DefaultFor(endpoints, role)?.Id ?? "";
				if (!away.TryGetValue(key, out string? fallback)) { away[key] = current; continue; }
				if (fallback == current) continue;
				// The fallback device is still here, yet the default moved: the user chose it in Windows.
				if (fallback.Length > 0 && endpoints.Any(endpoint => endpoint.Id == fallback))
					return new CallsMicPlan(settings with { Applied = false, Away = new(StringComparer.Ordinal) }, none,
						"The default microphone was changed in Windows while the calls mic was away; Core respects it.");
				away[key] = current; // Windows moved on by itself (its fallback went away too)
			}
			VoiceSettings noted = settings with { Away = away };
			return new CallsMicPlan(noted, none, settings.Away.Count == 0 ? "Calls mic not connected; calls use Windows' default microphone until it is back." : null);
		}
		VoiceSettings seen = settings with { CallsMicId = mic.Id, CallsMicName = Name(mic) ?? settings.CallsMicName };
		string? renamed = mic.Id != settings.CallsMicId ? "The calls mic came back with a new Windows id (another USB port?); following it by name." : null;
		var missing = roles.Where(role => !Holds(mic, role)).ToList();
		if (!settings.Applied || settings.Failed)
		{
			// Respecting Windows. If the user made it the default in Windows himself, Core's choice holds again.
			if (!settings.Failed && missing.Count == 0) return new CallsMicPlan(seen with { Applied = true }, none, "The calls mic is the default again (chosen in Windows).");
			return new CallsMicPlan(seen, none, renamed);
		}
		if (missing.Count == 0) return new CallsMicPlan(seen with { Away = new(StringComparer.Ordinal) }, none, renamed ?? (settings.Away.Count > 0 ? "The calls mic is back and still the default." : null));
		if (settings.Away.Count > 0 || renamed is not null)
			return new CallsMicPlan(seen with { Away = new(StringComparer.Ordinal) }, missing.Select(role => new DefaultChange(mic.Id, role)).ToList(),
				"The calls mic is back; making it the default again.");
		// Connected all along and no longer the default: the user (or an app) chose another in Windows.
		return new CallsMicPlan(seen with { Applied = false }, none, "The calls mic is no longer the default; it was changed in Windows. Core respects it.");
	}

	// Windows refused one or more default changes: Core stops trying (no retry loop, no notice) and
	// Quick Access says so; picking the device again retries.
	internal static VoiceSettings AfterApply(VoiceSettings planned, bool failed) => failed ? planned with { Failed = true } : planned;

	internal static MicScope Scope(VoiceSettings settings) => MicMutePolicy.ParseScope(settings.MuteScope);

	private static bool Holds(MicEndpoint mic, MicRole role) => role == MicRole.Communications ? mic.IsCommunications : mic.IsConsole;

	private static MicEndpoint? DefaultFor(IReadOnlyList<MicEndpoint> endpoints, MicRole role) =>
		endpoints.FirstOrDefault(endpoint => role == MicRole.Communications ? endpoint.IsCommunications : endpoint.IsConsole);

	private static string Key(MicRole role) => role == MicRole.Communications ? "communications" : "console";

	private static string? Name(MicEndpoint mic)
	{
		string? name = mic.Name?.Trim();
		if (string.IsNullOrEmpty(name)) return null;
		return name.Length > MaxNameLength ? name[..MaxNameLength] : name;
	}

	// ---------- persistence ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static VoiceSettings Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new VoiceSettings();
			VoiceSettings? settings = JsonSerializer.Deserialize<VoiceSettings>(File.ReadAllText(path), Json);
			if (settings is null) return new VoiceSettings();
			return settings with
			{
				MuteScope = MicMutePolicy.ScopeCode(MicMutePolicy.ParseScope(settings.MuteScope)),
				Away = new Dictionary<string, string>(settings.Away ?? new Dictionary<string, string>(), StringComparer.Ordinal),
			};
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new VoiceSettings(); }
	}

	internal static void Save(string path, VoiceSettings settings)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
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
