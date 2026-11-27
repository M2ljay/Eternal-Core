using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Playhub.Shared;

// Eternal Core batch 2 (B17, docs/eternal/BATCH-2.md): simple "when this happens, do that" rules, in the spirit of
// Docky (MIT, github.com/datbird/docky: tasks and actions on triggers). Ideas only; no code was taken. Shared by the
// desktop app (the editor in MainWindow.Batch2.cs), the agent (MarkIIIHost.Automations.cs runs them) and the tests.
//
// Stored in config.json (gaming.automations). Unknown triggers and actions are dropped, so an older Core never runs
// something it does not understand. Waking the TV on resume stays where it was (Core > Gaming Mode > LG webOS >
// "Turn on at PC start"): a "wake -> wake TV" rule turns that same path on instead of sending a second wake, and
// every TV wake, whichever rule or path asks for it, goes through one cooldown so the TV is never woken twice.
public sealed class CoreAutomationRule
{
	public string Id { get; set; } = "";
	public bool Enabled { get; set; }
	public string Trigger { get; set; } = "";
	public List<string> Actions { get; set; } = new();
	// The Home Assistant event name for the "ha-event" action (letters, digits and "-").
	public string? Event { get; set; }

	public CoreAutomationRule Copy() => new() { Id = Id, Enabled = Enabled, Trigger = Trigger, Actions = new List<string>(Actions), Event = Event };
}

public static class CoreAutomations
{
	public const string ControllerConnected = "controller-connected", Wake = "wake", GameStart = "game-start", GameStop = "game-stop", StreamOn = "stream-on", StreamOff = "stream-off";
	public const string TvWake = "tv-wake", DisplayRestore = "display-restore", SoundRestore = "sound-restore", StreamStart = "stream-start", StreamStop = "stream-stop", HaEvent = "ha-event";
	public const int MaxRules = 20;

	public static readonly string[] Triggers = { ControllerConnected, Wake, GameStart, GameStop, StreamOn, StreamOff };
	public static readonly string[] Actions = { TvWake, DisplayRestore, SoundRestore, StreamStart, StreamStop, HaEvent };
	// A TV wake would undo the point of these: a stream starting means the TV belongs to someone else.
	private static readonly Dictionary<string, string[]> Forbidden = new(StringComparer.Ordinal) { [StreamOn] = new[] { TvWake } };
	private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant);
	private static readonly Regex EventPattern = new("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant);

	// Shipped rules: only "a controller connects -> wake the TV and switch to Core" is on.
	public static List<CoreAutomationRule> Defaults() => new()
	{
		new() { Id = "controller-tv", Enabled = true, Trigger = ControllerConnected, Actions = { TvWake } },
		new() { Id = "wake-profiles", Enabled = false, Trigger = Wake, Actions = { DisplayRestore, SoundRestore } },
		new() { Id = "stream-off-profiles", Enabled = false, Trigger = StreamOff, Actions = { DisplayRestore, SoundRestore } },
		new() { Id = "game-start-ha", Enabled = false, Trigger = GameStart, Actions = { HaEvent }, Event = "game-start" },
		new() { Id = "game-stop-ha", Enabled = false, Trigger = GameStop, Actions = { HaEvent }, Event = "game-stop" },
		new() { Id = "stream-on-ha", Enabled = false, Trigger = StreamOn, Actions = { HaEvent }, Event = "stream-on" },
	};

	// What Core runs: null or an empty list is the defaults; otherwise valid rules only, actions de-duplicated and in
	// a fixed order, stream-start and stream-stop never together, at most MaxRules, unique ids.
	public static List<CoreAutomationRule> Normalize(IEnumerable<CoreAutomationRule?>? saved)
	{
		List<CoreAutomationRule?> source = saved?.ToList() ?? new List<CoreAutomationRule?>();
		if (source.Count == 0) return Defaults();
		var result = new List<CoreAutomationRule>();
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (CoreAutomationRule? rule in source)
		{
			if (rule is null || !Triggers.Contains(rule.Trigger)) continue;
			string[] forbidden = Forbidden.TryGetValue(rule.Trigger, out string[]? no) ? no : Array.Empty<string>();
			List<string> actions = Actions.Where(action => (rule.Actions ?? new List<string>()).Contains(action) && !forbidden.Contains(action)).ToList();
			if (actions.Contains(StreamStart) && actions.Contains(StreamStop)) actions.Remove(StreamStop);
			string? eventName = CleanEvent(rule.Event);
			if (actions.Contains(HaEvent) && eventName is null) eventName = rule.Trigger;
			if (!actions.Contains(HaEvent)) eventName = null;
			if (actions.Count == 0) continue;
			string id = rule.Id is { } given && IdPattern.IsMatch(given) && !ids.Contains(given) ? given : NewId(ids, rule.Trigger);
			ids.Add(id);
			result.Add(new CoreAutomationRule { Id = id, Enabled = rule.Enabled, Trigger = rule.Trigger, Actions = actions, Event = eventName });
			if (result.Count == MaxRules) break;
		}
		return result;
	}

	public static string? CleanEvent(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return null;
		string value = name.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');
		return EventPattern.IsMatch(value) ? value : null;
	}

	public static string NewId(ISet<string> taken, string trigger)
	{
		for (int index = 1; ; index++)
		{
			string id = $"{trigger}-{index}";
			if (!taken.Contains(id)) return id;
		}
	}

	public static IReadOnlyList<CoreAutomationRule> Matching(IEnumerable<CoreAutomationRule> rules, string trigger) =>
		rules.Where(rule => rule.Enabled && rule.Trigger == trigger).ToList();

	// The events Home Assistant may receive (the "Automation" event entity's types).
	public static IReadOnlyList<string> EventTypes(IEnumerable<CoreAutomationRule> rules) =>
		rules.Where(rule => rule.Actions.Contains(HaEvent) && rule.Event is not null).Select(rule => rule.Event!).Distinct(StringComparer.Ordinal).ToList();

	// TV-on-wake (TvSettings.TurnOnWithCore) or an enabled "wake -> wake TV" rule: the resume path wakes the TV.
	public static bool WakeTvOnResume(bool turnOnWithCore, IEnumerable<CoreAutomationRule> rules) =>
		turnOnWithCore || Matching(rules, Wake).Any(rule => rule.Actions.Contains(TvWake));

	// One line for the agent log, in English.
	public static string Describe(CoreAutomationRule rule) =>
		$"when {rule.Trigger.Replace('-', ' ')}: {string.Join(", ", rule.Actions.Select(action => action == HaEvent ? $"Home Assistant event \"{rule.Event}\"" : action.Replace('-', ' ')))}";
}

// Every TV wake goes through this: a wake (or the boot) and a controller connecting a few seconds later ask once.
public static class TvWakeCoordinator
{
	public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(90);

	public static bool ShouldSend(DateTimeOffset? lastWakeAt, DateTimeOffset now) =>
		lastWakeAt is not { } last || now - last >= Cooldown || now < last;
}

// Controllers appearing in Windows' device list. The first look only learns the list; a device that comes back within
// Flap (a Bluetooth pad reconnecting) or during a quiet period (agent start, the first minute after a wake, when
// Windows re-enumerates everything) is not "a controller connected".
public sealed record ControllerWatchState(IReadOnlyCollection<string>? Known, IReadOnlyDictionary<string, DateTimeOffset> LastSeen)
{
	public static readonly ControllerWatchState Empty = new(null, new Dictionary<string, DateTimeOffset>());
}

public static class ControllerWatch
{
	public static readonly TimeSpan Flap = TimeSpan.FromMinutes(2);

	public static (IReadOnlyList<string> Connected, ControllerWatchState Next) Observe(ControllerWatchState state, IReadOnlyCollection<string> present, DateTimeOffset now, bool quiet)
	{
		var keys = new HashSet<string>(present.Where(key => !string.IsNullOrEmpty(key)), StringComparer.OrdinalIgnoreCase);
		var lastSeen = new Dictionary<string, DateTimeOffset>(state.LastSeen, StringComparer.OrdinalIgnoreCase);
		var connected = new List<string>();
		if (state.Known is { } known && !quiet)
		{
			var before = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
			foreach (string key in keys)
				if (!before.Contains(key) && !(lastSeen.TryGetValue(key, out DateTimeOffset seen) && now - seen < Flap && now >= seen)) connected.Add(key);
		}
		foreach (string key in state.Known ?? Array.Empty<string>()) if (!keys.Contains(key)) lastSeen[key] = now;
		foreach (string key in lastSeen.Keys.ToList()) if (now - lastSeen[key] > Flap) lastSeen.Remove(key);
		return (connected, new ControllerWatchState(keys.ToList(), lastSeen));
	}
}
