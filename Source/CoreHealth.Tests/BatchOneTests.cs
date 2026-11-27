// Batch 1 tests (Linux-runnable): the update-night save backup (finding Ludusavi like Forge, reading its --api
// answer, once a night, the Health row), Decky plugin logs (what is read, what counts as an error, the one masked
// line, the row and its notice) and the bedtime wind-down reminders. docs/eternal/BATCH-1.md.
using GamingMode.Services;

static class BatchOneTests
{
	internal static void Run(Action<bool, string> Check)
	{
		DateTimeOffset now = new(2026, 11, 26, 20, 0, 0, TimeSpan.FromHours(-5));
		SaveBackup(Check, now);
		DeckyLogs(Check, now);
		WindDown(Check);
	}

	private static void SaveBackup(Action<bool, string> Check, DateTimeOffset now)
	{
		const string local = @"C:\Users\Vonn\AppData\Local";
		var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
		{
			["LOCALAPPDATA"] = local, ["ProgramFiles"] = @"C:\Program Files", ["USERPROFILE"] = @"C:\Users\Vonn", ["ProgramData"] = @"C:\ProgramData",
			["PATH"] = string.Join(Path.PathSeparator, "Windows", "\"Tools\""), // ";" on Windows
		};
		string package = Path.Combine(local, "Microsoft", "WinGet", "Packages", "mtkennerly.ludusavi_Microsoft.Winget.Source_8wekyb3d8bbwe");
		IEnumerable<string> Directories(string folder, string pattern) =>
			folder == Path.Combine(local, "Microsoft", "WinGet", "Packages") && pattern == SaveBackupPolicy.WingetId + "_*" ? new[] { package } : Array.Empty<string>();
		string? Env(string name) => env.TryGetValue(name, out string? value) ? value : null;
		IReadOnlyList<string> candidates = SaveBackupPolicy.CandidatePaths(Env, Directories);
		Check(candidates[0] == Path.Combine(local, "Microsoft", "WinGet", "Links", "ludusavi.exe") && candidates.Contains(Path.Combine(package, "ludusavi.exe"))
			&& candidates.Contains(Path.Combine(@"C:\Users\Vonn", "scoop", "apps", "ludusavi", "current", "ludusavi.exe")) && candidates.Contains(Path.Combine(@"C:\ProgramData", "scoop", "shims", "ludusavi.exe")),
			"save backup: winget Links and Packages, Scoop apps and shims, like Forge");
		Check(SaveBackupPolicy.Find(Env, path => path == Path.Combine("Tools", "ludusavi.exe") || path == Path.Combine(package, "ludusavi.exe"), Directories) == Path.Combine("Tools", "ludusavi.exe"),
			"save backup: PATH first");
		Check(SaveBackupPolicy.Find(Env, path => path == Path.Combine(package, "ludusavi.exe"), Directories) == Path.Combine(package, "ludusavi.exe"), "save backup: a winget portable package is found");
		Check(SaveBackupPolicy.Find(Env, _ => false, Directories) is null, "save backup: not installed");
		Check(SaveBackupPolicy.Find(Env, _ => throw new UnauthorizedAccessException(), (_, _) => throw new IOException()) is null, "save backup: unreadable folders are not installed, not an error");
		Check(SaveBackupPolicy.Arguments.SequenceEqual(new[] { "backup", "--force", "--api" }), "save backup: ludusavi backup --force --api");

		var never = new SaveBackupState();
		Check(SaveBackupPolicy.ShouldRun(true, true, never, now) && !SaveBackupPolicy.ShouldRun(false, true, never, now) && !SaveBackupPolicy.ShouldRun(true, false, never, now),
			"save backup: only with the setting on and Ludusavi installed");
		Check(!SaveBackupPolicy.ShouldRun(true, true, never with { LastRunAt = now.AddHours(-2), LastResult = "failed" }, now)
			&& SaveBackupPolicy.ShouldRun(true, true, never with { LastRunAt = now.AddHours(-8), LastResult = "ok" }, now),
			"save backup: once a night (the restart and a Steam update share it, a failure is not repeated)");

		const string answer = "{\"errors\":{},\"overall\":{\"totalGames\":312,\"totalBytes\":1,\"processedGames\":41,\"processedBytes\":734003200,\"changedGames\":{}},\"games\":{\"Hades\":{\"decision\":\"Processed\",\"files\":{}}}}";
		Check(SaveBackupPolicy.Judge(0, answer, false) == new SaveBackupOutcome("ok", null, 41, 734003200), "save backup: --api answer read (games and bytes)");
		Check(SaveBackupPolicy.Judge(0, "WARN manifest\n" + answer, false).Result == "ok", "save backup: a warning before the JSON is tolerated");
		Check(SaveBackupPolicy.Judge(1, answer.Replace("\"errors\":{}", "\"errors\":{\"someGamesFailed\":true}"), false) is { Result: "failed", Reason: "some-games-failed", Games: 41 },
			"save backup: some games failed");
		Check(SaveBackupPolicy.Judge(3, answer, false) is { Result: "failed", Reason: "exit-3" }, "save backup: a bad exit code fails");
		Check(SaveBackupPolicy.Judge(0, "", false) is { Result: "failed", Reason: "no-answer" } && SaveBackupPolicy.Judge(0, "{broken", false).Result == "failed", "save backup: no answer fails");
		Check(SaveBackupPolicy.Judge(-1, null, true) is { Result: "timed-out", Reason: "time-limit" }, "save backup: the time limit");

		SaveBackupState ran = SaveBackupPolicy.Record(never, SaveBackupPolicy.Judge(0, answer, false), "update night", now.AddHours(-15));
		Check(SaveBackupPolicy.Row(true, true, ran, now) is { Id: "save-backup", Level: HealthLevel.Ok, State: "ok" } row && row.Data["recent"] is true && (int)row.Data["games"]! == 41, "save backup row: last night OK");
		Check(SaveBackupPolicy.Row(true, true, ran with { LastResult = "timed-out" }, now) is { Level: HealthLevel.Attention, State: "failed" }, "save backup row: failed or timed out is crimson");
		Check(SaveBackupPolicy.Row(true, false, ran, now) is { Level: HealthLevel.Info, State: "not-installed" }, "save backup row: Ludusavi not installed");
		Check(SaveBackupPolicy.Row(false, true, ran, now) is { Level: HealthLevel.Info, State: "off" }, "save backup row: turned off");
		Check(SaveBackupPolicy.Row(true, true, never, now) is { Level: HealthLevel.Ok, State: "never" }, "save backup row: no update night yet");
		Check(SaveBackupPolicy.Row(true, true, ran with { LastRunAt = now.AddDays(-3) }, now).Data["recent"] is false, "save backup row: an old result is not 'last night'");

		var inputs = new CoreHealthInputs { Now = now, ServerRead = ServerStatusRead.NotConfigured, CoreVersion = "3.3.0", SaveBackup = new SaveBackupInputs(true, true, ran with { LastResult = "failed" }) };
		HealthReport report = CoreHealthPolicy.Evaluate(inputs);
		var ids = report.Rows.Select(item => item.Id).ToList();
		Check(ids.IndexOf("save-backup") == ids.IndexOf("update-night") + 1 && report.Problems.All(problem => problem.RowId != "save-backup"),
			"save backup row: after update night, no notice (the update went ahead)");

		string file = Path.Combine(Path.GetTempPath(), "save-backup-" + Guid.NewGuid().ToString("N") + ".json");
		try
		{
			SaveBackupPolicy.Save(file, ran);
			Check(SaveBackupPolicy.Load(file) == ran, "save backup: state round-trips");
			File.WriteAllText(file, "{broken");
			Check(SaveBackupPolicy.Load(file) == new SaveBackupState(), "save backup: a broken state starts fresh");
		}
		finally { File.Delete(file); }
	}

	private static void DeckyLogs(Action<bool, string> Check, DateTimeOffset now)
	{
		const string log = @"C:\Users\Vonn\homebrew\logs\Forge\2026-11-26 19.00.00.log";
		var first = new DeckyLogState();
		Check(DeckyLogPolicy.ReadPlan(first, log, 5000, now) is null, "logs: the very first look only measures (old errors are never reported)");
		var scanned = DeckyLogPolicy.Finish(DeckyLogPolicy.Measured(first, log, 5000, now.AddMinutes(-1)), now.AddMinutes(-1), new[] { log });
		Check(scanned.LastScan == now.AddMinutes(-1) && scanned.Files[log].Offset == 5000, "logs: offset remembered");
		Check(DeckyLogPolicy.ReadPlan(scanned, log, 5000, now) is null, "logs: nothing new, nothing read");
		Check(DeckyLogPolicy.ReadPlan(scanned, log, 6200, now) == (5000L, 1200L), "logs: only what was added");
		Check(DeckyLogPolicy.ReadPlan(scanned, log, 5000 + 10 * DeckyLogPolicy.MaxRead, now) is { } capped && capped.Count == DeckyLogPolicy.MaxRead, "logs: capped per round (tail)");
		Check(DeckyLogPolicy.ReadPlan(scanned, log, 300, now) == (0L, 300L), "logs: a shorter file is a new file");
		const string fresh = @"C:\Users\Vonn\homebrew\logs\Forge\2026-11-26 20.00.00.log";
		Check(DeckyLogPolicy.ReadPlan(scanned, fresh, 900, now) == (0L, 900L) && DeckyLogPolicy.ReadPlan(scanned, fresh, 900, now.AddMinutes(-5)) is null,
			"logs: a file that appeared after the last look is read from the start, an older unknown one only measured");
		Check(DeckyLogPolicy.CompleteLines("one\ntwo\npart"u8) == 8 && DeckyLogPolicy.CompleteLines("partial"u8) == 0, "logs: whole lines only");

		string decky = "[2026-11-26 19:59:58,120][INFO]: Forge started, no error so far\n"
			+ "[2026-11-26 19:59:59,001][ERROR]: Upload failed for https://api.example.com/v1?token=abc123def456ghi789jkl012mno&x=1\n"
			+ "[2026-11-26 20:00:01,300][ERROR]: Backup crashed\n"
			+ "Traceback (most recent call last):\n  File \"C:\\Users\\Vonn\\homebrew\\plugins\\Forge\\main.py\", line 12, in run\n    data[\"name\"]\n"
			+ "KeyError: 'name'\n"
			+ "[2026-11-26 20:00:02,000][INFO]: carrying on\n";
		DeckyLogScan scan = DeckyLogPolicy.Scan(decky);
		Check(scan.Errors == 2 && scan.Line == "KeyError: 'name'", "logs: ERROR lines counted, logger.exception's traceback is the same error, its last line kept");
		Check(DeckyLogPolicy.Scan("Traceback (most recent call last):\n  File \"x.py\", line 1\nValueError: bad value\n") == new DeckyLogScan(1, "ValueError: bad value"), "logs: a bare traceback counts");
		Check(DeckyLogPolicy.Scan("[INFO]: an error page was skipped\n[WARNING]: slow\n").Errors == 0, "logs: INFO and WARNING lines are not errors");
		Check(DeckyLogPolicy.Scan("2026-11-26 20:00:00 - forge - CRITICAL - disk gone\n") == new DeckyLogScan(1, "disk gone"), "logs: Python's default format too");
		Check(DeckyLogPolicy.Scan("[ERROR]: Upload failed for https://api.example.com/v1?token=abc123def456ghi789jkl012mno&x=1\n").Line == "Upload failed for https://api.example.com/v1?token=***&x=1",
			"logs: a token in a URL is masked");

		foreach ((string raw, string secret) in new[]
		{
			("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl", "eyJ"),
			("api_key=sk_live_51HxAbCdEfGh0123456789", "sk_live"),
			("{\"password\": \"hunter2\"}", "hunter2"),
			("login https://vonn:Sup3rS3cret@nexus.example/api", "Sup3rS3cret"),
			("session cookie=abcDEF123456 sent", "abcDEF123456"),
			("hash 0123456789abcdef0123456789abcdef mismatch", "0123456789abcdef0123456789abcdef"),
			(@"cannot open C:\Users\Vonn\Saves\slot1.sav", @"\Vonn\"),
			("RomM token: Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MA== rejected", "Zm9vYmFyYmF6cXV4MTIzNDU2Nzg5MA"),
		})
		{
			string clean = DeckyLogPolicy.Sanitize(raw);
			Check(!clean.Contains(secret, StringComparison.Ordinal), $"logs: masked: {clean}");
		}
		Check(DeckyLogPolicy.Sanitize("Steam 1764000000 app 1145360 failed: not found") == "Steam 1764000000 app 1145360 failed: not found", "logs: ordinary numbers and words stay readable");
		string longLine = DeckyLogPolicy.Sanitize("x\u0007y " + new string('a', 400));
		Check(longLine.Length <= DeckyLogPolicy.MaxLine && longLine.EndsWith("…") && !longLine.Contains('\u0007'), "logs: one short line without control characters");

		DeckyLogState state = DeckyLogPolicy.Found(scanned, "Forge", scan, now);
		state = DeckyLogPolicy.Found(state, "gaming-mode", new DeckyLogScan(1, "boom"), now.AddMinutes(-2));
		Check(DeckyLogPolicy.Failing(state, now).SequenceEqual(new[] { "Forge", "gaming-mode" }) && state.Plugins["Forge"].Count == 2, "logs: failing plugins, newest first");
		Check(DeckyLogPolicy.Found(state, "Forge", new DeckyLogScan(0, null), now) == state, "logs: no errors, no change");
		Check(DeckyLogPolicy.Failing(state, now.AddHours(7)).Count == 0, "logs: quiet for six hours is healthy again");
		Check(DeckyLogPolicy.DisplayName("gaming-mode") == "Core" && DeckyLogPolicy.DisplayName("Awesome Mix") == "Awesome Mix", "logs: Core's own plugin is called Core");
		Check(DeckyLogPolicy.Names(new[] { "Forge" }) == "Forge" && DeckyLogPolicy.Names(new[] { "Forge", "gaming-mode", "A", "B", "C" }) == "Forge, Core, A +2", "logs: names for the notice");
		DeckyLogState finished = DeckyLogPolicy.Finish(state, now.AddDays(8), Array.Empty<string>());
		Check(finished.Files.Count == 0 && finished.Plugins.Count == 0, "logs: gone files and week-old errors are forgotten");

		var problems = new List<HealthProblem>();
		HealthRow row = DeckyLogPolicy.Row("Healthy", DeckyLogPolicy.Found(scanned, "Forge", scan, now), now, problems);
		Check(row is { Id: "decky-plugins", Level: HealthLevel.Attention, State: "errors" } && row.Data["plugins"] is string[] { Length: 1 } names && names[0] == "Forge"
			&& (string?)row.Data["line"] == "KeyError: 'name'", "logs row: Decky: Forge is reporting errors (one masked line)");
		Check(problems.Single() is { Id: "decky-errors", NoticeKey: "health.deckyErrorsOne", Argument: "Forge" }, "logs row: one notice naming the plugin");
		Check(HealthAlertPolicy.NoticeKey(problems) == "health.deckyErrorsOne", "logs row: its own notice text");
		var many = new List<HealthProblem>();
		DeckyLogPolicy.Row("Healthy", state, now, many);
		var reordered = new List<HealthProblem>();
		DeckyLogPolicy.Row("Healthy", DeckyLogPolicy.Found(DeckyLogPolicy.Found(scanned, "gaming-mode", new DeckyLogScan(1, null), now), "Forge", scan, now.AddMinutes(1)), now.AddMinutes(1), reordered);
		Check(many.Single() is { NoticeKey: "health.deckyErrorsMany", Argument: "Forge, Core" } && many[0].Fingerprint == reordered[0].Fingerprint,
			"logs row: several plugins; the alert stays the same while the set does");
		var none = new List<HealthProblem>();
		Check(DeckyLogPolicy.Row("Healthy", scanned, now, none) is { Level: HealthLevel.Ok, State: "healthy" } && none.Count == 0, "logs row: loaded and quiet");
		Check(DeckyLogPolicy.Row("NotLoaded", scanned, now, none) is { Level: HealthLevel.Attention, State: "not-loaded" }, "logs row: not loaded");
		Check(DeckyLogPolicy.Row("Waiting", scanned, now, none).Level == HealthLevel.Info && DeckyLogPolicy.Row("NotApplicable", scanned, now, none).State == "not-applicable", "logs row: waiting, or not in Gaming Mode");

		var inputs = new CoreHealthInputs { Now = now, ServerRead = ServerStatusRead.NotConfigured, CoreVersion = "3.3.0", DeckyPlugins = new DeckyPluginInputs("Healthy", DeckyLogPolicy.Found(scanned, "Forge", scan, now)) };
		HealthReport report = CoreHealthPolicy.Evaluate(inputs);
		Check(report.Rows.Any(item => item.Id == "decky-plugins") && report.Problems.Any(problem => problem.Id == "decky-errors") && report.Attention >= 1, "logs row: part of Core Health");

		string file = Path.Combine(Path.GetTempPath(), "decky-logs-" + Guid.NewGuid().ToString("N") + ".json");
		try
		{
			DeckyLogPolicy.Save(file, state);
			DeckyLogState loaded = DeckyLogPolicy.Load(file);
			Check(loaded.Files[log.ToUpperInvariant()].Offset == 5000 && loaded.Plugins["forge"].Count == 2, "logs: state round-trips (paths and names ignore case)");
			File.WriteAllText(file, "[]");
			Check(DeckyLogPolicy.Load(file).Files.Count == 0, "logs: a broken state starts fresh");
		}
		finally { File.Delete(file); }
	}

	private static void WindDown(Action<bool, string> Check)
	{
		TimeSpan offset = TimeSpan.FromHours(-5);
		DateTimeOffset At(int hour, int minute, int second = 0, int day = 27) => new(2026, 11, day, hour, minute, second, offset);
		Check(WindDownPolicy.Times(null, null).SequenceEqual(new[] { new TimeSpan(3, 30, 0), new TimeSpan(3, 50, 0) }), "wind-down: 3:30 and 3:50 for a 4:00 bedtime by default");
		Check(WindDownPolicy.Bedtime("4") == TimeSpan.FromHours(4) && WindDownPolicy.Bedtime("23:30") == new TimeSpan(23, 30, 0) && WindDownPolicy.Bedtime("4:5") == TimeSpan.FromHours(4)
			&& WindDownPolicy.Bedtime("25:00") == TimeSpan.FromHours(4), "wind-down: bedtime parsing");
		Check(WindDownPolicy.ParseMinutes("30, 10").SequenceEqual(new[] { 30, 10 }) && WindDownPolicy.ParseMinutes("10;45").SequenceEqual(new[] { 45, 10 })
			&& WindDownPolicy.ParseMinutes("abc").SequenceEqual(WindDownPolicy.DefaultMinutes) && WindDownPolicy.ParseMinutes("200, 0, 15").SequenceEqual(new[] { 15 })
			&& WindDownPolicy.ParseMinutes("5,10,15,20,25,30").Count == WindDownPolicy.MaxReminders, "wind-down: reminder minutes parsing");

		var empty = new WindDownState();
		WindDownInputs Input(DateTimeOffset now, bool game = true) => new() { Now = now, Enabled = true, Bedtime = "04:00", MinutesBefore = new[] { 30, 10 }, GameRunning = game };
		WindDownDecision show = WindDownPolicy.Decide(Input(At(3, 50, 30)), empty);
		Check(show is { Action: WindDownAction.Show, MinutesLeft: 10, Quiet: false } && show.Slot == At(3, 50) && WindDownPolicy.Clock(show.Slot!.Value) == "3:50", "wind-down: 3:50 while a game runs");
		Check(WindDownPolicy.Decide(Input(At(3, 31)), empty).Slot == At(3, 30), "wind-down: 3:30");
		Check(WindDownPolicy.Decide(Input(At(3, 45)), empty).Action == WindDownAction.Nothing, "wind-down: nothing between reminders");
		Check(WindDownPolicy.Decide(Input(At(3, 52), game: false), empty) is { Action: WindDownAction.Nothing, Reason: "no game running" }, "wind-down: skipped when idle (no game)");
		Check(WindDownPolicy.Decide(Input(At(3, 55)), empty).Action == WindDownAction.Show, "wind-down: a game started a few minutes later still hears it");
		Check(WindDownPolicy.Decide(Input(At(3, 50, 30)), WindDownPolicy.Given(empty, At(3, 50))).Action == WindDownAction.Nothing, "wind-down: once per night");
		Check(WindDownPolicy.Decide(Input(At(3, 50, 30), game: true) with { Enabled = false }, empty).Action == WindDownAction.Nothing, "wind-down: off switch");
		Check(WindDownPolicy.Decide(Input(At(3, 50, 30)) with { StreamOrVr = true }, empty) is { Action: WindDownAction.Show, Quiet: true }, "wind-down: Stream mode or VR still notified, quietly");
		Check(WindDownPolicy.Decide(Input(At(3, 50, 30)) with { SleepMode = true }, empty).Action == WindDownAction.Skip, "wind-down: the sleep-mode hook skips");
		Check(WindDownPolicy.Decide(Input(At(3, 36)) with { MinutesBefore = new[] { 30, 25 } }, empty).Slot == At(3, 35), "wind-down: two due at once, the latest is said");
		Check(WindDownPolicy.Decide(Input(At(23, 45, 0, 26)) with { Bedtime = "00:10", MinutesBefore = new[] { 30 } }, empty).Slot == At(23, 40, 0, 26), "wind-down: a bedtime just after midnight");
		Check(WindDownPolicy.Decide(Input(At(0, 5)), empty).Action == WindDownAction.Nothing, "wind-down: nothing hours before");
		WindDownState kept = empty;
		for (int day = 1; day <= 20; day++) kept = WindDownPolicy.Given(kept, new DateTimeOffset(2026, 11, day, 3, 50, 0, offset));
		Check(kept.Given.Count <= 8 && kept.Given.Last() == new DateTimeOffset(2026, 11, 20, 3, 50, 0, offset), "wind-down: the state stays small");
		string file = Path.Combine(Path.GetTempPath(), "wind-down-" + Guid.NewGuid().ToString("N") + ".json");
		try
		{
			WindDownPolicy.Save(file, WindDownPolicy.Given(empty, At(3, 50)));
			Check(WindDownPolicy.Decide(Input(At(3, 50, 30)), WindDownPolicy.Load(file)).Action == WindDownAction.Nothing, "wind-down: the state file remembers tonight");
			File.WriteAllText(file, "nope");
			Check(WindDownPolicy.Load(file).Given.Count == 0, "wind-down: a broken state starts fresh");
		}
		finally { File.Delete(file); }
	}
}
