// Core Stream mode tests (Linux-runnable): what counts as a stream (and when it ends), the Sunshine and
// Apollo log, the wake decision, keeping the PC's sound off the TV with an exact restore, and the TV hold
// against Mark III's fake TV (nothing at all is sent while Stream mode is on).
using GamingMode.Models;
using GamingMode.Services;

int passed = 0;
void Check(bool condition, string name)
{
	if (!condition) throw new Exception("FAIL " + name);
	passed++;
	Console.WriteLine("PASS " + name);
}

DateTimeOffset now = new(2026, 11, 26, 21, 0, 0, TimeSpan.FromHours(-5));

// ---------- what counts as streaming ----------
StreamState idle = StreamModePolicy.Evaluate(Array.Empty<StreamSignal>(), false, now);
Check(!idle.Active && idle.HoldReason is null, "nothing streams: Stream mode off, TV not held");
StreamState forced = StreamModePolicy.Evaluate(Array.Empty<StreamSignal>(), true, now);
Check(forced is { Active: true, Forced: true, Detected: false } && forced.HoldReason == "\"Keep TV out of it\" is on", "Keep TV out of it: on without a stream");
StreamState steam = StreamModePolicy.Evaluate(new[] { new StreamSignal(StreamSource.SteamRemotePlay, "Steam Frame", now.AddSeconds(-20)) }, false, now);
Check(steam is { Active: true, Detected: true, Source: StreamSource.SteamRemotePlay, Device: "Steam Frame" }, "a fresh Steam report: streaming to its device");
Check(steam.HoldReason == "streaming to Steam Frame through Steam Remote Play", "hold reason names the device and the source");
Check(!StreamModePolicy.Evaluate(new[] { new StreamSignal(StreamSource.SteamRemotePlay, "Steam Frame", now.AddSeconds(-91)) }, false, now).Active,
	"a Steam report older than 90 s (plugin gone) ends it");
Check(!StreamModePolicy.Evaluate(new[] { new StreamSignal(StreamSource.SteamRemotePlay, "x", now.AddMinutes(5)) }, false, now).Active, "a report from the future is not trusted");
StreamState both = StreamModePolicy.Evaluate(new[] { new StreamSignal(StreamSource.SteamVr, null, now), new StreamSignal(StreamSource.Sunshine, "AYN Thor", now) }, true, now);
Check(both is { Source: StreamSource.Sunshine, Device: "AYN Thor", Forced: true } && both.Sources.Count == 2, "two sources: the one that names the device leads");
Check(StreamModePolicy.Evaluate(new[] { new StreamSignal(StreamSource.SteamVr, null, now) }, false, now).HoldReason == "streaming through SteamVR", "SteamVR without a device name");
Check(StreamModePolicy.CleanDevice("  ROG\u0007 Flow Z13\n ") == "ROG Flow Z13" && StreamModePolicy.CleanDevice(new string('x', 200))!.Length == 64 && StreamModePolicy.CleanDevice(" \t") is null,
	"device names: one short printable line");
Check(StreamModePolicy.Code(StreamSource.SteamRemotePlay) == "steam" && StreamModePolicy.Code(StreamSource.Apollo) == "apollo" && StreamModePolicy.Code(null) == "none", "codes for the plugin");
Check(StreamModePolicy.RouteAudioToVirtual(steam) && !StreamModePolicy.RouteAudioToVirtual(both) && !StreamModePolicy.RouteAudioToVirtual(forced),
	"sound is moved to the virtual speakers only for Steam Remote Play");
Check(StreamModePolicy.Describe(idle, steam)!.Contains("Steam Frame") && StreamModePolicy.Describe(steam, steam) is null && StreamModePolicy.Describe(steam, idle) == "Stream mode: off; the TV is Core's again.",
	"log lines on changes only");

// ---------- wake ----------
Check(StreamModePolicy.OnWake(steam, false) == StreamWakeDecision.Hold, "wake while streaming: hold");
Check(StreamModePolicy.OnWake(forced, false) == StreamWakeDecision.Hold, "wake with Keep TV out of it: hold");
Check(StreamModePolicy.OnWake(idle, true) == StreamWakeDecision.WaitForStream, "wake after sleeping mid-stream: wait for the stream");
Check(StreamModePolicy.OnWake(idle, false) == StreamWakeDecision.Proceed, "ordinary wake: the TV as usual");

// ---------- Sunshine / Apollo log ----------
SunshineLogState log = SunshineLogState.Empty;
string[] apolloSession =
{
	"[2026-01-28 16:34:21.366]: Info: Display mode for client [Steam Deck] requested to [1280x720x60]",
	"[2026-01-28 16:34:21.578]: Info: Virtual Display created at \\\\.\\DISPLAY49",
	"[2026-01-28 16:34:23.744]: Info: New streaming session started [active sessions: 1]",
	"[2026-01-28 16:34:23.934]: Info: CLIENT CONNECTED",
};
foreach (string line in apolloSession) log = SunshineLog.Apply(log, line);
Check(log is { Streaming: true, Client: "Steam Deck", VirtualDisplay: true, Display: "\\\\.\\DISPLAY49" }, "Apollo session: streaming, client name, virtual display");
log = SunshineLog.Apply(log, "[2026-01-28 16:34:57.439]: Info: Virtual Display removed successfully");
log = SunshineLog.Apply(log, "[2026-01-28 16:34:58.180]: Info: CLIENT DISCONNECTED");
Check(log is { Streaming: false, Client: null, VirtualDisplay: false }, "Apollo session end: idle, display gone");
log = SunshineLog.Apply(SunshineLogState.Empty, "[2025-01-20 21:13:24.476]: Info: New streaming session started [active sessions: 1]");
Check(!log.Streaming, "a session that has not connected yet does not count");
log = SunshineLog.Apply(log, "[2025-01-20 21:13:24.483]: Info: CLIENT CONNECTED");
Check(log.Streaming && log.Client is null, "Sunshine session: streaming, no client name");
log = SunshineLog.Apply(log, "[2025-07-28 12:36:56.292]: Info: Terminate handler called");
Check(!log.Streaming, "Sunshine shutting down: idle");
Check(!SunshineLog.Apply(SunshineLogState.Empty, "Info: CLIENT DISCONNECTED").Streaming, "a stray disconnect never goes below zero");

string folder = Path.Combine(Path.GetTempPath(), "core-stream-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
	string file = Path.Combine(folder, "sunshine.log");
	var tail = new SunshineLogTail(file);
	Check(!tail.Poll().Streaming && tail.LastWrite is null, "tail: no log file, nothing streams");
	File.WriteAllText(file, "[x]: Info: Sunshine version: v2025\n[x]: Info: Display mode for client [ROG Flow Z13] requested to [2560x1600x120]\n");
	Check(!tail.Poll().Streaming, "tail: a log without a session");
	File.AppendAllText(file, "[x]: Info: CLIENT CONN");
	Check(!tail.Poll().Streaming, "tail: half a line waits for the rest");
	File.AppendAllText(file, "ECTED\n");
	Check(tail.Poll() is { Streaming: true, Client: "ROG Flow Z13" }, "tail: the completed line counts, with the client from earlier");
	File.AppendAllText(file, "[x]: Info: CLIENT DISCONNECTED\n");
	Check(!tail.Poll().Streaming, "tail: disconnect read incrementally");
	File.AppendAllText(file, "[x]: Info: CLIENT CONNECTED\n");
	Check(tail.Poll().Streaming, "tail: a new session");
	File.WriteAllText(file, "[y]: Info: Sunshine version: v2025\n");
	Check(!tail.Poll().Streaming, "tail: a new, shorter log after a restart starts over");
	using (var writer = new StreamWriter(file, append: false))
	{
		for (int index = 0; index < 60000; index++) writer.WriteLine($"[z]: Debug: padding line {index} ................................................................................");
		writer.WriteLine("[z]: Info: CLIENT CONNECTED");
	}
	var big = new SunshineLogTail(file);
	Check(big.Poll().Streaming, "tail: a large log is read from its last 4 MB on the first look");
}
finally { try { Directory.Delete(folder, true); } catch { } }

// ---------- the PC's sound ----------
// A tiny Core Audio: endpoint id -> (name, form factor, muted); one default.
var speakers = new Dictionary<string, (string Name, int FormFactor, bool Muted)>
{
	["tv"] = ("LG TV SSCR2 (NVIDIA High Definition Audio)", StreamAudioPolicy.FormFactorDigitalDisplay, false),
	["headset"] = ("Headphones (Arctis)", 3, false),
	["steam"] = ("Speakers (Steam Streaming Speakers)", 1, false),
};
string defaultId = "tv";
List<SpeakerEndpoint> Speakers() => speakers.Select(pair => new SpeakerEndpoint(pair.Key, pair.Value.Name, pair.Value.FormFactor, pair.Key == defaultId, pair.Value.Muted)).ToList();
StreamAudioState Run(StreamAudioPlan plan)
{
	if (plan.SetDefault is { } id) defaultId = id;
	foreach (SpeakerChange change in plan.Changes) speakers[change.Id] = speakers[change.Id] with { Muted = change.Mute };
	return plan.Next;
}

// Steam Remote Play, virtual speakers present: default moves there and back.
var audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), routeToVirtual: true));
Check(defaultId == "steam" && audio is { Engaged: true, Mode: "virtual", SwitchedFrom: "tv", SwitchedTo: "steam" } && !speakers["tv"].Muted, "Steam stream: sound moves to Steam Streaming Speakers, nothing muted");
Check(StreamAudioPolicy.Engage(audio, Speakers(), true).SetDefault is null, "engaging again changes nothing");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(defaultId == "tv" && audio.IsEmpty, "stream over: the previous default is back, record cleared");

// The user picks another default meanwhile: Core respects it.
audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), true));
defaultId = "headset";
audio = Run(StreamAudioPolicy.Engage(audio, Speakers(), true));
Check(audio.DefaultChangedOutside && defaultId == "headset", "default changed outside Core while streaming: left there");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(defaultId == "headset" && audio.IsEmpty, "and not moved back afterwards");

// The stream already moved the default (Sunshine with Steam Streaming Speakers): nothing to do or undo.
defaultId = "steam";
audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), false));
Check(audio.Mode == "already" && defaultId == "steam" && speakers.Values.All(value => !value.Muted), "already on the virtual speakers: left as it is");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(defaultId == "steam" && audio.IsEmpty, "and nothing is undone");

// Sunshine/Apollo/SteamVR or forced: the TV's speaker is muted, exactly restored; the user's own mute stays.
defaultId = "tv";
speakers["tv2"] = ("Second display (HDMI)", StreamAudioPolicy.FormFactorDigitalDisplay, true);
audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), false));
Check(audio.Mode == "muted" && speakers["tv"].Muted && audio.Muted.SequenceEqual(new[] { "tv" }) && defaultId == "tv" && !speakers["headset"].Muted && !speakers["steam"].Muted,
	"mute fallback: only the live TV speaker is muted, the default is not moved");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(!speakers["tv"].Muted && speakers["tv2"].Muted && audio.IsEmpty, "released: the TV speaker live again, the one muted before stays muted");

// Virtual speakers missing while Steam streams: the mute fallback.
speakers.Remove("steam");
audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), true));
Check(audio.Mode == "muted" && speakers["tv"].Muted, "no virtual speakers: mute the TV instead");
// Unmuted outside Core: that wins.
speakers["tv"] = speakers["tv"] with { Muted = false };
audio = Run(StreamAudioPolicy.Engage(audio, Speakers(), true));
Check(!speakers["tv"].Muted && audio.Muted.Count == 0, "an unmute from outside Core is not fought");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(!speakers["tv"].Muted && audio.IsEmpty, "and nothing to restore");

// The TV's speaker disappears while muted (TV off) and comes back after the stream: unmuted then.
audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), false));
var tvMuted = speakers["tv"];
speakers.Remove("tv");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(!audio.IsEmpty && audio.Pending.SequenceEqual(new[] { "tv" }), "released with the TV speaker gone: kept as pending");
Check(StreamAudioPolicy.Release(audio, Speakers()).Changes.Count == 0, "still gone: nothing to do");
speakers["tv"] = tvMuted;
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(!speakers["tv"].Muted && audio.IsEmpty, "back: unmuted, record cleared");

// A TV speaker that appears during the stream (HDMI after a wake) is muted too.
speakers.Remove("tv");
audio = Run(StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), false));
Check(audio.Mode == "none", "no TV speaker yet: nothing to do");
speakers["tv"] = tvMuted with { Muted = false };
audio = Run(StreamAudioPolicy.Engage(audio, Speakers(), false));
Check(speakers["tv"].Muted && audio.Muted.Contains("tv"), "the TV speaker appears: muted too");
audio = Run(StreamAudioPolicy.Release(audio, Speakers()));
Check(!speakers["tv"].Muted, "and unmuted at the end");

// Windows refuses: a failed mute is not restored later, a failed unmute is retried.
var refusedPlan = StreamAudioPolicy.Engage(new StreamAudioState(), Speakers(), false);
var afterRefusal = StreamAudioPolicy.KeepFailed(refusedPlan.Next, refusedPlan.Changes.ToArray());
Check(afterRefusal.Muted.Count == 0, "a mute Windows refused leaves no record");
speakers["tv"] = speakers["tv"] with { Muted = true };
var unmuteRefused = StreamAudioPolicy.Release(new StreamAudioState { Engaged = true, Mode = "muted", Muted = new() { "tv" }, Seen = new() { "tv" } }, Speakers());
var keep = StreamAudioPolicy.KeepFailed(unmuteRefused.Next, unmuteRefused.Changes.ToArray());
Check(keep.Pending.Contains("tv") && StreamAudioPolicy.Release(keep, Speakers()).Changes.Single() == new SpeakerChange("tv", false), "an unmute Windows refused is tried again");
speakers["tv"] = speakers["tv"] with { Muted = false };

// The record survives a restart.
string record = Path.Combine(Path.GetTempPath(), "core-stream-audio-" + Guid.NewGuid().ToString("N") + ".json");
try
{
	var saved = new StreamAudioState { Engaged = true, Mode = "muted", Muted = new() { "tv" }, Seen = new() { "tv", "headset" } };
	StreamAudioState.Save(record, saved);
	Check(StreamAudioState.Load(record).SameAs(saved), "audio record: saved and loaded");
	StreamAudioState.Save(record, new StreamAudioState());
	Check(!File.Exists(record) && StreamAudioState.Load(record).IsEmpty, "an empty record removes the file");
	string settingsFile = record + ".settings";
	StreamSettings.Save(settingsFile, new StreamSettings { KeepTvOut = true });
	StreamSettings loaded = StreamSettings.Load(settingsFile);
	Check(loaded.KeepTvOut && loaded.AudioOffTv && !loaded.StreamingAtSleep, "settings: override saved, sound off TV by default");
	File.WriteAllText(settingsFile, "{ not json");
	Check(!StreamSettings.Load(settingsFile).KeepTvOut, "unreadable settings: defaults");
	File.Delete(settingsFile);
}
finally { try { File.Delete(record); } catch { } }

// ---------- the TV hold, against the fake TV ----------
{
	await using var fake = new FakeTv();
	var settings = new TvSettings { Enabled = true, Address = "127.0.0.1", MacAddress = "A8:23:FE:01:02:03", CoreInput = "HDMI_2" };
	var pairing = new MemoryPairing();
	pairing.Save(FakeTv.Key, null);
	var lines = new List<string>();
	string? hold = "streaming to Steam Frame through Steam Remote Play";
	var controller = new TvController(() => settings, pairing, line => { lock (lines) lines.Add(line); }, _ => null, fake.Port, () => hold);

	fake.PowerState = "Active Standby"; fake.Foreground = "com.webos.app.hdmi1"; fake.SwitchedTo = null; fake.TurnedOff = false;
	TvResult wake = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
	Check(wake.Outcome == TvOutcome.StreamMode && wake.Ok && fake.SwitchedTo is null, "wake while streaming: no input switch");
	lock (lines) Check(lines.Any(line => line == "TV: Stream mode (streaming to Steam Frame through Steam Remote Play); nothing sent to the TV (wake and input switch)."),
		"wake while streaming: one clear log line");
	lock (lines) Check(!lines.Any(line => line.Contains("Wake-on-LAN", StringComparison.Ordinal) || line.Contains("power state", StringComparison.Ordinal)), "wake while streaming: no Wake-on-LAN, no power query");

	fake.Foreground = "com.webos.app.hdmi2";
	Check((await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None)).Outcome == TvOutcome.StreamMode && !fake.TurnedOff, "sleep while streaming: not turned off, even on Core's input");
	Check((await controller.SwitchCoreInputAsync(CancellationToken.None)).Outcome == TvOutcome.StreamMode, "input button while streaming: held");
	Check((await controller.TurnOffNowAsync(CancellationToken.None)).Outcome == TvOutcome.StreamMode && !fake.TurnedOff, "off button while streaming: held");
	Check((await controller.TurnOnAsync(CancellationToken.None)).Outcome == TvOutcome.StreamMode, "on button while streaming: held");
	Check((await controller.PairAsync(null, CancellationToken.None)).Outcome == TvOutcome.StreamMode, "pairing while streaming: no prompt on the TV");

	hold = "\"Keep TV out of it\" is on";
	fake.Foreground = "com.webos.app.hdmi1";
	Check((await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None)).Outcome == TvOutcome.StreamMode && fake.SwitchedTo is null, "Keep TV out of it: held too");

	// A stream that starts while the TV is waking: the input switch is still skipped.
	int asked = 0;
	var late = new TvController(() => settings, pairing, line => { lock (lines) lines.Add(line); }, _ => null, fake.Port, () => ++asked >= 2 ? "streaming through Apollo" : null);
	TvResult lateWake = await late.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
	Check(lateWake.Outcome == TvOutcome.StreamMode && fake.SwitchedTo is null, "a stream starting during the wake: no input switch");

	hold = null;
	TvResult normal = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
	Check(normal.Outcome == TvOutcome.Done && fake.SwitchedTo == "HDMI_2", "stream over: the TV is woken and switched as usual");
	var throwing = new TvController(() => settings, pairing, _ => { }, _ => null, fake.Port, () => throw new InvalidOperationException("probe"));
	fake.SwitchedTo = null; fake.Foreground = "com.webos.app.hdmi1";
	Check((await throwing.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None)).Outcome == TvOutcome.StreamMode && fake.SwitchedTo is null, "a hold that cannot answer keeps the TV alone");
	lock (lines) Check(lines.All(line => !line.Contains(FakeTv.Key, StringComparison.Ordinal)), "the client key never appears in the log");
}

Console.WriteLine($"ALL PASS ({passed} checks)");
return 0;
