// Core Voice tests (Linux-runnable): the microphone mute state machine (mute, exact restore, new
// default devices, an unmute from outside Core, persistence), how Core opens the Game Bar for an
// Xbox party and hands the screen back, and the Quiet Windows ducking fix.
using GamingMode.Services;
using Playhub.Shared;

int passed = 0;
void Check(bool condition, string name)
{
	if (!condition) throw new Exception("FAIL " + name);
	passed++;
	Console.WriteLine("PASS " + name);
}

// A tiny Core Audio: endpoint id -> (muted, default).
var audio = new Dictionary<string, (bool Muted, bool Default)>();
List<MicEndpoint> Endpoints() => audio.Select(pair => new MicEndpoint(pair.Key, pair.Value.Muted, pair.Value.Default)).ToList();
MicMuteState Apply(MicPlan plan)
{
	foreach (MicChange change in plan.Changes) audio[change.Id] = (change.Mute, audio[change.Id].Default);
	return plan.Next;
}

// ---------- observe ----------
Check(MicMutePolicy.Observe(Endpoints()) == MicState.None, "no microphone: none");
audio["usb"] = (false, true);
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Live, "one live default: live");
Check(MicMutePolicy.Code(MicState.Muted) == "muted" && MicMutePolicy.Code(MicState.None) == "none", "codes for the plugin");

// ---------- mute and exact restore, console and communications differ ----------
audio.Clear();
audio["webcam"] = (false, true);   // default Console
audio["headset"] = (true, true);   // default Communications, already muted by the user
audio["spare"] = (false, false);   // not a default: never touched
var state = new MicMuteState();
MicPlan plan = MicMutePolicy.Mute(state, Endpoints());
Check(plan.Changes.Count == 1 && plan.Changes[0] == new MicChange("webcam", true), "mute changes only the live default");
state = Apply(plan);
Check(state.CoreMuted && state.Before["webcam"] == false && state.Before["headset"] == true && !state.Before.ContainsKey("spare"), "mute records each default's own value");
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Muted, "both defaults muted: muted");
Check(MicMutePolicy.Mute(state, Endpoints()).Changes.Count == 0 && MicMutePolicy.Mute(state, Endpoints()).Next.Before["webcam"] == false, "muting twice keeps the first record");
state = Apply(MicMutePolicy.Unmute(state, Endpoints()));
Check(!audio["webcam"].Muted && audio["headset"].Muted && !audio["spare"].Muted, "unmute restores exactly: the user's own mute stays");
Check(!state.CoreMuted && state.Before.Count == 0, "unmute clears Core's record");

// ---------- toggle ----------
state = Apply(MicMutePolicy.Toggle(state, Endpoints()));
Check(state.CoreMuted && MicMutePolicy.Observe(Endpoints()) == MicState.Muted, "toggle from live mutes");
state = Apply(MicMutePolicy.Toggle(state, Endpoints()));
Check(!state.CoreMuted && MicMutePolicy.Observe(Endpoints()) == MicState.Live, "toggle from muted restores");

// ---------- muted elsewhere, unmuted in Core ----------
audio["webcam"] = (true, true);
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Muted, "muted outside Core still shows muted");
state = Apply(MicMutePolicy.Toggle(new MicMuteState(), Endpoints()));
Check(!audio["webcam"].Muted && !audio["headset"].Muted && MicMutePolicy.Observe(Endpoints()) == MicState.Live, "unmute without Core's mute means a live microphone");

// ---------- a new default while muted ----------
audio.Clear();
audio["webcam"] = (false, true);
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints()));
Check(MicMutePolicy.Reconcile(state, Endpoints()).Changes.Count == 0 && MicMutePolicy.Reconcile(state, Endpoints()).Log is null, "reconcile with nothing new changes nothing");
audio["webcam"] = (true, false);
audio["headset"] = (false, true); // plugged in, becomes the default
plan = MicMutePolicy.Reconcile(state, Endpoints());
Check(plan.Changes.SequenceEqual(new[] { new MicChange("headset", true) }) && plan.Log is not null, "a new default microphone is muted too");
state = Apply(plan);
Check(state.Before["headset"] == false && state.Before["webcam"] == false, "the new one is recorded with its own value");
audio.Remove("headset"); // unplugged while muted
Check(MicMutePolicy.Observe(Endpoints()) == MicState.None, "no default left: none");
Check(MicMutePolicy.Reconcile(state, Endpoints()).Changes.Count == 0 && MicMutePolicy.Reconcile(state, Endpoints()).Next.Before.ContainsKey("headset"), "an unplugged microphone stays remembered");
audio["webcam"] = (true, true);
state = Apply(MicMutePolicy.Unmute(state, Endpoints()));
Check(!audio["webcam"].Muted && !state.CoreMuted, "unmute restores what is present and forgets the rest");

// ---------- unmuted outside Core while Core's mute is on ----------
audio.Clear();
audio["webcam"] = (false, true);
audio["headset"] = (false, true);
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints()));
audio["headset"] = (false, true); // someone unmuted it in Windows
plan = MicMutePolicy.Reconcile(state, Endpoints());
state = Apply(plan);
Check(!state.CoreMuted && !audio["webcam"].Muted && plan.Log is not null, "an unmute outside Core ends Core's mute and restores the rest (no fight)");
Check(MicMutePolicy.Reconcile(new MicMuteState(), Endpoints()).Changes.Count == 0, "reconcile does nothing without Core's mute");
Check(MicMutePolicy.Mute(new MicMuteState(), new List<MicEndpoint>()).Next.CoreMuted == false, "muting with no microphone remembers nothing");

// ---------- many devices: bounded memory ----------
var many = Enumerable.Range(0, 30).Select(i => new MicEndpoint("old" + i, false, true)).ToList();
Check(MicMutePolicy.Mute(new MicMuteState(), many).Next.Before.Count == MicMutePolicy.MaxRemembered, "at most 16 microphones remembered");

// ---------- persistence ----------
string folder = Path.Combine(Path.GetTempPath(), "core-voice-" + Guid.NewGuid().ToString("N"));
try
{
	string path = Path.Combine(folder, "mic-mute.json");
	var saved = new MicMuteState { CoreMuted = true, Before = new() { ["{0.0.1.00000000}.{abc}"] = false, ["{0.0.1.00000000}.{def}"] = true } };
	MicMutePolicy.Save(path, saved);
	MicMuteState loaded = MicMutePolicy.Load(path);
	Check(loaded.CoreMuted && loaded.Before.Count == 2 && loaded.Before["{0.0.1.00000000}.{def}"], "state survives a restart");
	MicMutePolicy.Save(path, new MicMuteState());
	Check(!File.Exists(path) && !MicMutePolicy.Load(path).CoreMuted, "unmuted: the file is removed");
	File.WriteAllText(path, "{ not json");
	Check(!MicMutePolicy.Load(path).CoreMuted, "a damaged file reads as unmuted");
}
finally { try { Directory.Delete(folder, true); } catch { } }

// ---------- Xbox party: launch choice ----------
PartyLaunchChoice Choose(PartyLaunchInputs input) => PartyLaunchPolicy.Choose(input);
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = false }) == new PartyLaunchChoice(PartyLaunchMethod.WinG, "shortcut"), "default: the Win+G shortcut");
Check(Choose(new() { GameBarInstalled = null, GameBarInteractive = null }).Method == PartyLaunchMethod.WinG, "unknown state: still tries Win+G");
Check(Choose(new() { GameBarInstalled = false }).Reason == "notInstalled", "no Game Bar: says so");
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = true }).Method == PartyLaunchMethod.AlreadyOpen, "already open: no keystroke (Win+G would close it)");
Check(Choose(new() { GameBarInstalled = true, ProtectedGame = true }).Reason == "protectedGame", "never sends keys into an anti-cheat game");
var custom = Choose(new() { GameBarInstalled = true, OverrideUri = " ms-gamebar://launch/activate/Example.Widget_0000000000000_App_Widget " });
Check(custom.Method == PartyLaunchMethod.Uri && custom.Uri!.StartsWith("ms-gamebar:", StringComparison.Ordinal), "a configured ms-gamebar: URI is used");
Check(!PartyLaunchPolicy.ValidGameBarUri("https://example.com") && !PartyLaunchPolicy.ValidGameBarUri("C:\\Windows\\notepad.exe")
	&& !PartyLaunchPolicy.ValidGameBarUri("ms-gamebar:x\" & calc") && !PartyLaunchPolicy.ValidGameBarUri(""), "anything but ms-gamebar: is ignored");
Check(Choose(new() { GameBarInstalled = true, OverrideUri = "ms-settings:privacy" }).Method == PartyLaunchMethod.WinG, "an invalid override falls back to Win+G");

// ---------- Xbox party: back to Big Picture ----------
DateTimeOffset t0 = new(2026, 11, 26, 22, 0, 0, TimeSpan.FromHours(-5));
var watch = new GameBarWatch(t0, RefocusOnClose: true);
var step = GameBarWatchPolicy.Step(watch, t0.AddSeconds(1), false, false, false);
Check(step.Action == GameBarWatchAction.None && !step.Next.SeenOpen, "waits for the bar to open");
Check(GameBarWatchPolicy.Step(watch, t0.AddSeconds(7), false, true, false).Action == GameBarWatchAction.Stop, "never opened: gives up after a few seconds");
step = GameBarWatchPolicy.Step(watch, t0.AddSeconds(1), true, false, false);
Check(step.Next.SeenOpen && step.Action == GameBarWatchAction.None, "open: keeps watching");
watch = step.Next;
Check(GameBarWatchPolicy.Step(watch, t0.AddMinutes(40), true, false, false).Action == GameBarWatchAction.None, "a long party: still watching");
Check(GameBarWatchPolicy.Step(watch, t0.AddMinutes(40), null, false, false).Action == GameBarWatchAction.None, "unknown state: keeps watching");
Check(GameBarWatchPolicy.Step(watch, t0.AddMinutes(41), false, false, false).Action == GameBarWatchAction.Refocus, "closed with Big Picture behind: bring it back");
Check(GameBarWatchPolicy.Step(watch, t0.AddMinutes(41), false, true, false).Action == GameBarWatchAction.Stop, "closed and Steam already in front: nothing to do");
Check(GameBarWatchPolicy.Step(watch, t0.AddMinutes(41), false, false, true).Action == GameBarWatchAction.Stop, "a game started meanwhile: never steal its focus");
Check(GameBarWatchPolicy.Step(watch with { RefocusOnClose = false }, t0.AddMinutes(41), false, false, false).Action == GameBarWatchAction.Stop, "opened over a game: Windows returns to the game");
Check(GameBarWatchPolicy.Step(watch, t0.AddHours(7), true, false, false).Action == GameBarWatchAction.Stop, "the watch ends after six hours");

// ---------- Quiet Windows: communications ducking ----------
var registry = new MemoryRegistry();
string backup = Path.Combine(Path.GetTempPath(), "core-ducking-" + Guid.NewGuid().ToString("N") + ".json");
CouchFix ducking = CouchHardening.Fixes.Single(fix => fix.Id == "ducking");
Check(!ducking.Machine && ducking.Values.Single() == new CouchValue(false, @"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3), "ducking fix: per user, Do nothing (3)");
registry.Write(false, @"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 1);
CouchHardening.Apply(registry, ducking.Values, backup);
Check(CouchHardening.IsApplied(registry, ducking) && (int?)registry.Read(false, @"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference") == 3, "apply sets Do nothing");
CouchHardening.Undo(registry, false, backup);
Check((int?)registry.Read(false, @"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference") == 1, "undo restores the user's choice (reduce by 80%)");
CouchHardening.Apply(registry.Cleared(), ducking.Values, backup);
CouchHardening.Undo(registry, false, backup);
Check(registry.Read(false, @"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference") is null && !File.Exists(backup), "undo removes a value Windows never had (back to its default)");
Check(CouchHardening.Values(false).Any(value => value.Name == "UserDuckingPreference") && !CouchHardening.Values(true).Any(value => value.Name == "UserDuckingPreference"), "applied with the per-user fixes, no admin prompt");

Console.WriteLine($"All {passed} Core Voice checks passed.");
return 0;

internal sealed class MemoryRegistry : ICouchRegistry
{
	private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
	private static string Id(bool machine, string key, string name) => (machine ? "HKLM\\" : "HKCU\\") + key + "|" + name;
	public object? Read(bool machine, string key, string name) => _values.TryGetValue(Id(machine, key, name), out var value) ? value : null;
	public void Write(bool machine, string key, string name, object value) => _values[Id(machine, key, name)] = value;
	public void Delete(bool machine, string key, string name) => _values.Remove(Id(machine, key, name));
	internal MemoryRegistry Cleared() { _values.Clear(); return this; }
}
