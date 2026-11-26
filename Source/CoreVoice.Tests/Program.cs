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
string? communications = null; // the Communications default, when the test sets one
List<MicEndpoint> Endpoints() => audio.Select(pair => new MicEndpoint(pair.Key, pair.Value.Muted, pair.Value.Default, pair.Key == communications)).ToList();
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
Check(!state.CoreMuted && MicMutePolicy.Observe(Endpoints()) == MicState.Partial, "toggle from muted restores (the user's own mute on one stays: partial)");

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
plan = MicMutePolicy.Unmute(state, Endpoints());
state = Apply(plan);
Check(!audio["webcam"].Muted && !state.CoreMuted, "unmute restores what is present");
Check(state.Pending.Count == 1 && state.Pending["headset"] == false && plan.Log is not null, "an unplugged microphone keeps its record as pending");

// ---------- C1: unplugged while muted, plugged back after the unmute ----------
Check(MicMutePolicy.Reconcile(state, Endpoints()).Changes.Count == 0 && MicMutePolicy.Reconcile(state, Endpoints()).Next == state, "pending: nothing to do while it is still unplugged");
audio["headset"] = (true, true); // back, still muted by Windows, and a default again
plan = MicMutePolicy.Reconcile(state, Endpoints());
Check(plan.Changes.SequenceEqual(new[] { new MicChange("headset", false) }) && plan.Log is not null, "replugged after the unmute: gets its own (live) setting back");
Check(MicMutePolicy.KeepUnrestored(plan.Next, state, new[] { "headset" }).Pending.ContainsKey("headset"), "pending: kept when Windows refuses the restore");
state = Apply(plan);
Check(state.Pending.Count == 0 && state.IsEmpty && !audio["headset"].Muted, "pending: dropped once restored");
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Live, "both back live");

// A microphone the user had muted himself comes back muted (its own setting), not live.
audio.Clear();
audio["webcam"] = (false, true);
audio["headset"] = (true, true);
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints()));
audio.Remove("headset");
state = Apply(MicMutePolicy.Unmute(state, Endpoints()));
audio["headset"] = (false, true); // a driver reset it while unplugged
state = Apply(MicMutePolicy.Reconcile(state, Endpoints()));
Check(audio["headset"].Muted && state.Pending.Count == 0, "pending: restores the user's own mute too");

// Muting again while a record is pending keeps its own value for the next unmute.
audio.Clear();
audio["webcam"] = (false, true);
audio["headset"] = (false, true);
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints()));
audio.Remove("headset");
state = Apply(MicMutePolicy.Unmute(state, Endpoints()));
state = Apply(MicMutePolicy.Mute(state, Endpoints()));
Check(state.CoreMuted && state.Before["headset"] == false && state.Pending.Count == 0, "mute again: the pending record moves back into Core's mute");
audio["headset"] = (true, true);
state = Apply(MicMutePolicy.Unmute(state, Endpoints()));
Check(!audio["headset"].Muted && !audio["webcam"].Muted && state.IsEmpty, "and the next unmute restores it");

// Unplugged while muted, plugged back while Core's mute is still on: muted, record kept.
audio.Clear();
audio["webcam"] = (false, true);
audio["headset"] = (false, true);
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints()));
audio.Remove("headset");
state = Apply(MicMutePolicy.Reconcile(state, Endpoints()));
audio["headset"] = (true, true);
plan = MicMutePolicy.Reconcile(state, Endpoints());
Check(plan.Changes.Count == 0 && plan.Next.CoreMuted && plan.Next.Before["headset"] == false, "unplug and replug while muted: still muted, record kept");

// ---------- C1: partial ----------
audio.Clear();
audio["webcam"] = (false, true);  // Console default
audio["headset"] = (true, true);  // Communications default, muted
communications = "headset";
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Partial && MicMutePolicy.Code(MicState.Partial) == "partial", "one default muted, one live: partial");
Check(MicMutePolicy.CommunicationsCode(Endpoints()) == "muted", "the Communications default is reported");
Check(MicMutePolicy.ActsMuted(Endpoints()), "partial with the call mic muted: the button unmutes");
state = Apply(MicMutePolicy.Toggle(new MicMuteState(), Endpoints()));
Check(!audio["headset"].Muted && !audio["webcam"].Muted && MicMutePolicy.Observe(Endpoints()) == MicState.Live, "toggle from partial (call mic muted) gives a live microphone");
audio["webcam"] = (true, true);
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Partial && !MicMutePolicy.ActsMuted(Endpoints()), "partial with the call mic live: the button mutes");
state = Apply(MicMutePolicy.Toggle(new MicMuteState(), Endpoints()));
Check(audio["headset"].Muted && audio["webcam"].Muted && state.CoreMuted, "toggle from partial (call mic live) mutes both");
state = Apply(MicMutePolicy.Unmute(state, Endpoints()));
Check(audio["webcam"].Muted && !audio["headset"].Muted, "and unmuting restores each one's own setting");
communications = null;
audio["headset"] = (false, true);
Check(MicMutePolicy.Observe(Endpoints()) == MicState.Partial && !MicMutePolicy.ActsMuted(Endpoints()), "partial without a known Communications default: the button mutes");

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
	MicMutePolicy.Save(path, new MicMuteState { Pending = new() { ["{0.0.1.00000000}.{abc}"] = false } });
	loaded = MicMutePolicy.Load(path);
	Check(File.Exists(path) && !loaded.CoreMuted && loaded.Pending["{0.0.1.00000000}.{abc}"] == false, "pending records survive a restart");
	MicMutePolicy.Save(path, new MicMuteState());
	Check(!File.Exists(path) && !MicMutePolicy.Load(path).CoreMuted, "unmuted with nothing pending: the file is removed");
	File.WriteAllText(path, "{\"coreMuted\": true, \"before\": {\"a\": false}}");
	Check(MicMutePolicy.Load(path).Pending.Count == 0 && MicMutePolicy.Load(path).CoreMuted, "an older file without pending still reads");
	File.WriteAllText(path, "{ not json");
	Check(!MicMutePolicy.Load(path).CoreMuted, "a damaged file reads as unmuted");
}
finally { try { Directory.Delete(folder, true); } catch { } }

// ---------- mute scope: calls only ----------
audio.Clear();
audio["webcam"] = (false, true);   // Console default: the stream/recording keeps it
audio["dji"] = (false, true);      // Communications default: the calls mic
communications = "dji";
const MicScope Calls = MicScope.Calls;
Check(MicMutePolicy.Targets(Endpoints(), Calls).Select(endpoint => endpoint.Id).SequenceEqual(new[] { "dji" }), "calls scope: only the Communications default is a target");
state = Apply(MicMutePolicy.Toggle(new MicMuteState(), Endpoints(), Calls));
Check(audio["dji"].Muted && !audio["webcam"].Muted && state.CoreMuted && !state.Before.ContainsKey("webcam"), "calls scope: mutes the calls mic only, the Console default stays live");
Check(MicMutePolicy.Observe(Endpoints(), Calls) == MicState.Muted && MicMutePolicy.Observe(Endpoints()) == MicState.Partial, "calls scope: shows muted (not partly muted) when the calls mic is muted");
Check(MicMutePolicy.ActsMuted(Endpoints(), Calls) && MicMutePolicy.Reconcile(state, Endpoints(), Calls).Changes.Count == 0, "calls scope: the button unmutes; a live Console default is not a new default to mute");
state = Apply(MicMutePolicy.Toggle(state, Endpoints(), Calls));
Check(!audio["dji"].Muted && !audio["webcam"].Muted && state.IsEmpty, "calls scope: unmute restores the calls mic");
communications = null;
Check(MicMutePolicy.Targets(Endpoints(), Calls).Count == 2, "calls scope without a Communications default: every default (the button never does nothing)");
communications = "dji";

// C1 under the calls scope: unplugged while muted, unmuted, plugged back: its own setting returns.
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints(), Calls));
audio.Remove("dji");
communications = null; // Windows falls back
state = Apply(MicMutePolicy.Reconcile(state, Endpoints(), Calls));
Check(state.CoreMuted && audio["webcam"].Muted && state.Before["dji"] == false, "calls scope: calls mic unplugged while muted, the fallback default is muted too and the record kept");
state = Apply(MicMutePolicy.Unmute(state, Endpoints(), Calls));
Check(!audio["webcam"].Muted && state.Pending.Count == 1 && state.Pending["dji"] == false, "calls scope: unmute while unplugged keeps the record until restored");
audio["dji"] = (true, true);
communications = "dji";
plan = MicMutePolicy.Reconcile(state, Endpoints(), Calls);
state = Apply(plan);
Check(!audio["dji"].Muted && state.IsEmpty, "calls scope: plugged back, the calls mic is live again and the record goes");

// C1 under the calls scope: partial state stays exact (a muted Console mic the user had muted stays so).
audio["webcam"] = (true, true);
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints(), Calls));
state = Apply(MicMutePolicy.Unmute(state, Endpoints(), Calls));
Check(audio["webcam"].Muted && !audio["dji"].Muted && state.IsEmpty, "calls scope: never touches the Console mic, muted or not");
audio["webcam"] = (false, true);

// Scope changed while muted: one plan, exact restore, records kept.
state = Apply(MicMutePolicy.Mute(new MicMuteState(), Endpoints()));
Check(audio["webcam"].Muted && audio["dji"].Muted, "everything: both muted");
plan = MicMutePolicy.ChangeScope(state, Endpoints(), Calls);
state = Apply(plan);
Check(!audio["webcam"].Muted && audio["dji"].Muted && state.CoreMuted && !state.Before.ContainsKey("webcam") && state.Before["dji"] == false && plan.Log is not null, "everything -> calls only while muted: the Console mic gets its own setting back");
state = Apply(MicMutePolicy.ChangeScope(state, Endpoints(), MicScope.All));
Check(audio["webcam"].Muted && audio["dji"].Muted && state.Before["webcam"] == false, "calls only -> everything while muted: the Console mic is muted and recorded");
audio.Remove("dji");
communications = null;
state = Apply(MicMutePolicy.ChangeScope(state, Endpoints(), Calls));
Check(state.Before.ContainsKey("dji") && state.Before["dji"] == false, "scope change with the calls mic unplugged: its record is kept");
state = Apply(MicMutePolicy.Unmute(state, Endpoints(), Calls));
Check(!audio["webcam"].Muted && state.Pending["dji"] == false, "and unmute keeps it pending until it returns");
Check(MicMutePolicy.ChangeScope(new MicMuteState(), Endpoints(), Calls).Changes.Count == 0, "scope change without Core's mute: nothing to do");
Check(MicMutePolicy.ParseScope("calls") == Calls && MicMutePolicy.ParseScope("weird") == MicScope.All && MicMutePolicy.ScopeCode(Calls) == "calls", "scope codes; anything unknown is everything");
communications = null;

// ---------- calls mic: picking the Communications default ----------
// A tiny device list: id -> (name, Console default, Communications default).
var devices = new Dictionary<string, (string Name, bool Console, bool Comm)>();
List<MicEndpoint> Devices() => devices.Select(pair => new MicEndpoint(pair.Key, false, pair.Value.Console || pair.Value.Comm, pair.Value.Comm, pair.Value.Console, pair.Value.Name)).ToList();
void SetDefault(string id, MicRole role)
{
	foreach (string key in devices.Keys.ToList())
	{
		var value = devices[key];
		devices[key] = role == MicRole.Communications ? value with { Comm = key == id } : value with { Console = key == id };
	}
}
VoiceSettings Run(CallsMicPlan step) { foreach (DefaultChange change in step.Changes) SetDefault(change.Id, change.Role); return step.Next; }
const string Dji = "{0.0.1.00000000}.{11111111-1111-1111-1111-111111111111}";
devices["webcam"] = ("Microphone (USB Webcam)", true, true);
devices[Dji] = ("Microphone (Wireless Microphone RX)", false, false);
var voice = new VoiceSettings();
Check(CallsMicPolicy.Status(voice, Devices()) == "none" && CallsMicPolicy.Step(voice, Devices()).Changes.Count == 0, "no calls mic chosen: nothing to do");
Check(CallsMicPolicy.Choose(voice, Devices(), "missing", false) is null, "a device that is not connected cannot be picked");
CallsMicPlan pick = CallsMicPolicy.Choose(voice, Devices(), Dji, false)!;
Check(pick.Changes.SequenceEqual(new[] { new DefaultChange(Dji, MicRole.Communications) }), "pick: sets the Communications default only");
voice = Run(pick);
Check(voice.CallsMicName == "Microphone (Wireless Microphone RX)" && voice.Applied && CallsMicPolicy.Status(voice, Devices()) == "active" && devices["webcam"].Console, "pick: active, the Console default untouched");
Check(CallsMicPolicy.Step(voice, Devices()) is { Changes.Count: 0, Log: null } steady && steady.Next == voice, "steady: nothing changes, nothing logged");
voice = Run(CallsMicPolicy.Choose(voice, Devices(), Dji, true)!);
Check(devices[Dji].Console && devices[Dji].Comm && voice.UseForEverything && CallsMicPolicy.Status(voice, Devices()) == "active", "use for everything: also the Console default");
voice = Run(CallsMicPolicy.Choose(voice, Devices(), Dji, false)!);
Check(!voice.UseForEverything && devices[Dji].Console, "use for everything off: Windows' Console default is left as it is");

// Unplugged (the receiver went to the other PC): information only, Windows' fallback noted.
devices.Remove(Dji);
SetDefault("webcam", MicRole.Communications);
CallsMicPlan away = CallsMicPolicy.Step(voice, Devices());
Check(away.Changes.Count == 0 && away.Next.Away["communications"] == "webcam" && away.Log is not null, "absent: nothing changed, Windows' fallback noted, logged once");
voice = away.Next;
Check(CallsMicPolicy.Status(voice, Devices()) == "absent" && CallsMicPolicy.Step(voice, Devices()) is { Log: null, Changes.Count: 0 }, "absent: calm (no repeated log, no change)");
// Back: made the default again.
devices[Dji] = ("Microphone (Wireless Microphone RX)", false, false);
CallsMicPlan back = CallsMicPolicy.Step(voice, Devices());
Check(back.Changes.SequenceEqual(new[] { new DefaultChange(Dji, MicRole.Communications) }) && back.Next.Away.Count == 0, "back: made the Communications default again");
voice = Run(back);
Check(CallsMicPolicy.Status(voice, Devices()) == "active", "back: active");
// Back and Windows already restored it by itself: nothing to set.
devices.Remove(Dji); SetDefault("webcam", MicRole.Communications);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
devices[Dji] = ("Microphone (Wireless Microphone RX)", false, true); SetDefault(Dji, MicRole.Communications);
Check(CallsMicPolicy.Step(voice, Devices()) is { Changes.Count: 0 } restored && restored.Next.Away.Count == 0, "back and already the default: nothing to set");
voice = CallsMicPolicy.Step(voice, Devices()).Next;

// Changed in Windows while connected: respected, not fought.
SetDefault("webcam", MicRole.Communications);
CallsMicPlan changed = CallsMicPolicy.Step(voice, Devices());
Check(changed.Changes.Count == 0 && !changed.Next.Applied && changed.Log is not null, "changed in Windows while connected: respected");
voice = changed.Next;
Check(CallsMicPolicy.Status(voice, Devices()) == "changed", "status: changed");
devices.Remove(Dji);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
devices[Dji] = ("Microphone (Wireless Microphone RX)", false, false);
Check(CallsMicPolicy.Step(voice, Devices()).Changes.Count == 0, "after a change in Windows, a replug does not take the default back");
SetDefault(Dji, MicRole.Communications);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
Check(voice.Applied && CallsMicPolicy.Status(voice, Devices()) == "active", "made the default in Windows again: Core's choice holds again");

// Changed in Windows while the calls mic was away: respected when it returns.
devices["headset"] = ("Headset Microphone", false, false);
devices.Remove(Dji); SetDefault("webcam", MicRole.Communications);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
SetDefault("headset", MicRole.Communications); // the user picks the headset in Windows; the webcam is still there
CallsMicPlan whileAway = CallsMicPolicy.Step(voice, Devices());
Check(!whileAway.Next.Applied && whileAway.Log is not null, "changed in Windows while away (fallback still connected): respected");
voice = whileAway.Next;
devices[Dji] = ("Microphone (Wireless Microphone RX)", false, false);
Check(CallsMicPolicy.Step(voice, Devices()).Changes.Count == 0 && CallsMicPolicy.Status(voice, Devices()) == "changed", "and the returning calls mic does not take the default back");
devices.Remove("headset");

// Windows moving on by itself while away (its fallback unplugged too) is not a user choice.
voice = Run(CallsMicPolicy.Choose(voice, Devices(), Dji, false)!);
devices["headset"] = ("Headset Microphone", false, false);
devices.Remove(Dji); SetDefault("headset", MicRole.Communications);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
devices.Remove("headset"); SetDefault("webcam", MicRole.Communications);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
Check(voice.Applied && voice.Away["communications"] == "webcam", "Windows' own second fallback is not a user choice");
devices[Dji] = ("Microphone (Wireless Microphone RX)", false, false);
Check(CallsMicPolicy.Step(voice, Devices()).Changes.Count == 1, "and the calls mic is made the default again when back");
voice = Run(CallsMicPolicy.Step(voice, Devices()));

// Another USB port: a new endpoint id, the same name. Followed by name, only when unambiguous.
devices.Remove(Dji); SetDefault("webcam", MicRole.Communications);
voice = CallsMicPolicy.Step(voice, Devices()).Next;
const string DjiOtherPort = "{0.0.1.00000000}.{22222222-2222-2222-2222-222222222222}";
devices[DjiOtherPort] = ("Microphone (Wireless Microphone RX)", false, false);
CallsMicPlan moved = CallsMicPolicy.Step(voice, Devices());
Check(moved.Next.CallsMicId == DjiOtherPort && moved.Changes.SequenceEqual(new[] { new DefaultChange(DjiOtherPort, MicRole.Communications) }), "new id in another port: found by name, made the default");
voice = Run(moved);
devices["{0.0.1.00000000}.{33333333-3333-3333-3333-333333333333}"] = ("Microphone (Wireless Microphone RX)", false, false);
Check(CallsMicPolicy.Find(voice with { CallsMicId = "gone" }, Devices()) is null, "two devices with the remembered name: no guess");
devices.Remove("{0.0.1.00000000}.{33333333-3333-3333-3333-333333333333}");

// Windows refuses: no retry loop, says so; picking again retries.
voice = CallsMicPolicy.AfterApply(CallsMicPolicy.Choose(voice, Devices(), "webcam", false)!.Next, failed: true);
Check(voice.Failed && CallsMicPolicy.Status(voice, Devices()) == "failed" && CallsMicPolicy.Step(voice, Devices()).Changes.Count == 0, "Windows refused: status failed, no retry loop");
voice = Run(CallsMicPolicy.Choose(voice, Devices(), DjiOtherPort, false)!);
Check(!voice.Failed && CallsMicPolicy.Status(voice, Devices()) == "active", "picking again clears the failure");
Check(CallsMicPolicy.Forget(voice) is { CallsMicId: null, Applied: false } forgot && CallsMicPolicy.Status(forgot, Devices()) == "none", "forget: back to Windows' own choice");
Check(CallsMicPolicy.Step(CallsMicPolicy.Forget(voice) with { MuteScope = "calls" }, Devices()).Changes.Count == 0, "forgotten: nothing set");

// Persistence.
string voiceFolder = Path.Combine(Path.GetTempPath(), "core-voice-settings-" + Guid.NewGuid().ToString("N"));
try
{
	string voiceFile = Path.Combine(voiceFolder, "voice-settings.json");
	Check(CallsMicPolicy.Load(voiceFile) == new VoiceSettings() && new VoiceSettings().MuteScope == "all", "no file: no calls mic, mute everything");
	var saved = voice with { MuteScope = "calls", Away = new() { ["communications"] = "webcam" } };
	CallsMicPolicy.Save(voiceFile, saved);
	Check(CallsMicPolicy.Load(voiceFile) == saved, "voice settings survive a restart");
	File.WriteAllText(voiceFile, "{\"callsMicId\":\"x\",\"muteScope\":\"loud\"}");
	Check(CallsMicPolicy.Load(voiceFile) is { CallsMicId: "x", MuteScope: "all", Applied: false } && CallsMicPolicy.Load(voiceFile).Away.Count == 0, "an unknown scope reads as everything");
	File.WriteAllText(voiceFile, "nope");
	Check(CallsMicPolicy.Load(voiceFile).CallsMicId is null, "a damaged file reads as no calls mic");
}
finally { try { Directory.Delete(voiceFolder, true); } catch { } }

// ---------- Xbox party: launch choice ----------
PartyLaunchChoice Choose(PartyLaunchInputs input) => PartyLaunchPolicy.Choose(input);
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = false }) == new PartyLaunchChoice(PartyLaunchMethod.WinG, "shortcut"), "no game, bar closed: the Win+G shortcut");
var withGame = Choose(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = true });
Check(withGame.Method == PartyLaunchMethod.Uri && withGame.Reason == "gameRunning" && withGame.Uri == PartyLaunchPolicy.DefaultUri, "C2: a game running (detected or not): the ms-gamebar: URI, never Win+G");
var unknownGame = Choose(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = null });
Check(unknownGame.Method == PartyLaunchMethod.Uri && unknownGame.Reason == "gameUnknown", "C2: cannot tell whether a game runs: treated as running (URI)");
var unknownBar = Choose(new() { GameBarInstalled = null, GameBarInteractive = null, GameRunning = false });
Check(unknownBar.Method == PartyLaunchMethod.Uri && unknownBar.Reason == "stateUnknown", "bar state unknown: no toggle keystroke (it could close an open bar)");
Check(PartyLaunchPolicy.ValidGameBarUri(PartyLaunchPolicy.DefaultUri) && PartyLaunchPolicy.DefaultUri == "ms-gamebar://launch/", "the default URI is Microsoft's documented launch form");
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = false, RecentlySent = true }) == new PartyLaunchChoice(PartyLaunchMethod.AlreadyOpen, "cooldown"), "a second press right after the first sends nothing");
Check(Choose(new() { GameBarInstalled = true, ProtectedGame = true, GameRunning = true, OverrideUri = "ms-gamebar://launch/" }).Reason == "protectedGame", "anti-cheat game: nothing at all, not even the URI");
Check(PartyLaunchPolicy.AfterUriFailure(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = true }) == new PartyLaunchChoice(PartyLaunchMethod.Unavailable, "uriFailed"), "C2: URI failed with a game running: a message, no keystroke");
Check(PartyLaunchPolicy.AfterUriFailure(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = null }).Method == PartyLaunchMethod.Unavailable, "C2: URI failed, game unknown: a message, no keystroke");
Check(PartyLaunchPolicy.AfterUriFailure(new() { GameBarInstalled = true, GameBarInteractive = null, GameRunning = false }).Method == PartyLaunchMethod.Unavailable, "URI failed, bar state unknown: a message, no keystroke");
Check(PartyLaunchPolicy.AfterUriFailure(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = false }).Method == PartyLaunchMethod.WinG, "URI failed with no game and the bar closed: Win+G as before");
Check(Choose(new() { GameBarInstalled = false }).Reason == "notInstalled", "no Game Bar: says so");
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = true }).Method == PartyLaunchMethod.AlreadyOpen, "already open: no keystroke (Win+G would close it)");
Check(Choose(new() { GameBarInstalled = true, ProtectedGame = true }).Reason == "protectedGame", "never sends keys into an anti-cheat game");
var custom = Choose(new() { GameBarInstalled = true, OverrideUri = " ms-gamebar://launch/activate/Example.Widget_0000000000000_App_Widget " });
Check(custom.Method == PartyLaunchMethod.Uri && custom.Reason == "override" && custom.Uri == "ms-gamebar://launch/activate/Example.Widget_0000000000000_App_Widget", "a configured ms-gamebar: URI is used");
Check(Choose(new() { GameBarInstalled = true, GameRunning = true, OverrideUri = "ms-gamebar:activate/X_Y_Z" }).Uri == "ms-gamebar:activate/X_Y_Z", "the configured URI also replaces the default during a game");
Check(!PartyLaunchPolicy.ValidGameBarUri("https://example.com") && !PartyLaunchPolicy.ValidGameBarUri("C:\\Windows\\notepad.exe")
	&& !PartyLaunchPolicy.ValidGameBarUri("ms-gamebar:x\" & calc") && !PartyLaunchPolicy.ValidGameBarUri(""), "anything but ms-gamebar: is ignored");
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = false, OverrideUri = "ms-settings:privacy" }).Method == PartyLaunchMethod.WinG, "an invalid override is ignored (no game: Win+G)");
Check(Choose(new() { GameBarInstalled = true, GameBarInteractive = false, GameRunning = true, OverrideUri = "ms-settings:privacy" }).Uri == PartyLaunchPolicy.DefaultUri, "an invalid override is ignored (game: the default URI)");

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
