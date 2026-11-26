// Core Health tests (Linux-runnable): reading core-status.json, the server path, every row's
// verdict and the notice rules (once a day per problem, dismissed until it changes).
using System.Text.Json;
using GamingMode.Services;

int passed = 0;
void Check(bool condition, string name)
{
	if (!condition) throw new Exception("FAIL " + name);
	passed++;
	Console.WriteLine("PASS " + name);
}

DateTimeOffset now = new(2026, 11, 26, 20, 0, 0, TimeSpan.FromHours(-5));
string sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "core-status.sample.json"));
HealthRow Row(HealthReport report, string id) => report.Rows.Single(row => row.Id == id);
CoreHealthInputs Base(ServerStatusRead read = ServerStatusRead.Ok, ServerStatus? server = null) => new()
{
	Now = now, ServerName = "V3", ServerRead = read, Server = server,
	TvEnabled = true, TvPaired = true, TvReachable = true,
	PcDrive = "C:\\", PcFreeBytes = 300L * 1000 * 1000 * 1000, PcTotalBytes = 1000L * 1000 * 1000 * 1000,
	CoreVersion = "3.3.0", LatestCoreVersion = "3.3.0", CoreUpdateAvailable = false,
};

// ---------- server path ----------
Check(CoreHealthPolicy.StatusUrl("serverv3")?.ToString() == "http://serverv3:8123/local/core-status.json", "url: bare server name");
Check(CoreHealthPolicy.StatusUrl("192.168.1.109")?.ToString() == "http://192.168.1.109:8123/local/core-status.json", "url: IP address");
Check(CoreHealthPolicy.StatusUrl("serverv3:9000")?.ToString() == "http://serverv3:9000/local/core-status.json", "url: custom port");
Check(CoreHealthPolicy.StatusUrl(@"\\V3\CoreStatus")?.ToString() == "http://v3:8123/local/core-status.json", "url: old file-share value still works");
Check(CoreHealthPolicy.StatusUrl("https://serverv3.tail.ts.net")?.ToString() == "https://serverv3.tail.ts.net/local/core-status.json", "url: full URL without a path");
Check(CoreHealthPolicy.StatusUrl("http://serverv3:8123/local/x.json")?.ToString() == "http://serverv3:8123/local/x.json", "url: full URL kept");
Check(CoreHealthPolicy.StatusUrl("  ") is null && CoreHealthPolicy.StatusUrl(null) is null, "url: empty is not configured");
Check(CoreHealthPolicy.StatusUrl("http://user:pw@serverv3") is null && CoreHealthPolicy.StatusUrl("bad host") is null, "url: credentials and junk refused");
ServerStatus? aincrad = CoreHealthPolicy.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "core-status.aincrad.json")));
Check(aincrad is { BackupResult: "ok", ConsecutiveFailures: 0, RommReachable: true, DriveHealth: "healthy" } && aincrad.LastSuccessAt is not null && aincrad.RestoreResult is null, "aincrad: backup.sh output parses");

// ---------- parsing (the file the PowerShell script writes) ----------
ServerStatus? status = CoreHealthPolicy.Parse(sample);
Check(status is not null && status.Schema == 1 && status.Server == "V3", "parse: schema and server");
Check(status!.BackupResult == "ok" && status.ConsecutiveFailures == 0 && status.LastSuccessAt == new DateTimeOffset(2026, 11, 26, 5, 52, 10, TimeSpan.FromHours(-5)), "parse: backup result and last success");
Check(status.SkippedPlatforms.SequenceEqual(new[] { "ps2", "ngc" }), "parse: skipped platforms");
Check(status.Categories["roms"].Bytes == 21474836480 && status.Categories["saves"].Ok, "parse: categories");
Check(status.PoolTotalBytes == 2000000000000 && status.PoolUsedBytes == 1310000000000, "parse: pool");
Check(status.RestoreResult == "ok" && status.DriveHealth == "healthy" && status.RommReachable == true, "parse: restore, drive, RomM");
Check(CoreHealthPolicy.Parse("{\"schema\":1}") is { BackupResult: null, ConsecutiveFailures: 0 }, "parse: minimal file");
Check(CoreHealthPolicy.Parse("{\"schema\":2,\"written_at\":\"2026-11-26T05:00:00Z\",\"new_thing\":{}}") is { Schema: 2 }, "parse: a newer schema still reads");
Check(CoreHealthPolicy.Parse("not json") is null && CoreHealthPolicy.Parse("{}") is null && CoreHealthPolicy.Parse("[1]") is null, "parse: garbage is unreadable");
using (JsonDocument nullPool = JsonDocument.Parse(sample.Replace("\"pool\": {", "\"pool_old\": {")))
	Check(CoreHealthPolicy.Parse(nullPool.RootElement.GetRawText())!.PoolTotalBytes is null, "parse: pool may be null");

// ---------- verdicts ----------
HealthReport report = CoreHealthPolicy.Evaluate(Base(server: status));
Check(report.Attention == 0 && report.Problems.Count == 0, "verdict: sample is all good");
Check(report.Rows.Select(row => row.Id).SequenceEqual(CoreHealthPolicy.RowOrder), "verdict: rows in display order");
Check(Row(report, "server-backup").State == "ok" && Row(report, "server-backup").Data["skippedPlatforms"] is IReadOnlyList<string> { Count: 2 }, "verdict: backup ok with skipped platforms");

report = CoreHealthPolicy.Evaluate(Base(ServerStatusRead.NotConfigured) with { ServerName = null });
Check(Row(report, "server-backup") is { Level: HealthLevel.Info, State: "not-configured" } && report.Attention == 0, "verdict: no server set is information, not a problem");
foreach (var (read, code) in new[] { (ServerStatusRead.Unreachable, "unreachable"), (ServerStatusRead.Missing, "missing"), (ServerStatusRead.Unreadable, "unreadable") })
{
	report = CoreHealthPolicy.Evaluate(Base(read));
	Check(Row(report, "server-backup") is { Level: HealthLevel.Attention } row && row.State == code && Row(report, "server-drive").Level == HealthLevel.Unknown, $"verdict: {code} is its own state");
	Check(report.Problems.Count == 0, $"verdict: {code} raises no notice (could be a sleeping server)");
}
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-37) }));
Check(Row(report, "server-backup").State == "stale" && Row(report, "romm").State == "stale" && report.Problems.Any(p => p.Id == "status-stale"), "verdict: older than 36 h is stale and alerts");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-35) }));
Check(Row(report, "server-backup").State == "ok", "verdict: 35 h old is still fresh");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-1), BackupResult = "failed", ConsecutiveFailures = 1 }));
Check(Row(report, "server-backup").State == "failed" && report.Problems.Count == 0, "verdict: one failure shows, no notice");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-1), BackupResult = "partial", ConsecutiveFailures = 2 }));
Check(report.Problems.Single().Id == "backup-failing" && report.Attention == 1, "verdict: two failures in a row alert");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-1), DriveHealth = "warning" }));
Check(Row(report, "server-drive").Level == HealthLevel.Attention && report.Problems.Single().Fingerprint == "drive|warning", "verdict: drive warning alerts");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-1), RestoreAt = now.AddDays(-50) }));
Check(Row(report, "restore-check").State == "overdue", "verdict: restore check older than 45 days is overdue");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-1), RestoreResult = "failed" }));
Check(Row(report, "restore-check").Level == HealthLevel.Attention, "verdict: failed restore check");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now.AddHours(-1), RommReachable = false }));
Check(Row(report, "romm").State == "unreachable", "verdict: RomM down");

report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { RestartPending = true });
Check(Row(report, "windows-restart") is { Level: HealthLevel.Info, State: "pending" } && Row(report, "update-night").State == "nothing", "verdict: restart pending is information");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { RestartPending = true, UpdateNightOffer = true });
Check(Row(report, "update-night") is { Level: HealthLevel.Attention, State: "waiting" } && report.Attention == 1, "verdict: an unscheduled update night needs attention once");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { UpdateNightOffer = true, UpdateNightScheduledFor = now.AddHours(9) });
Check(Row(report, "update-night").State == "scheduled" && report.Attention == 0, "verdict: scheduled update night is fine");

report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { TvEnabled = false });
Check(Row(report, "tv") is { Level: HealthLevel.Info, State: "off" }, "verdict: TV control off");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { TvPaired = false, TvReachable = null });
Check(Row(report, "tv").State == "not-paired" && report.Attention == 1, "verdict: TV not paired");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { TvReachable = false });
Check(Row(report, "tv").State == "unreachable", "verdict: TV not answering");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { PcFreeBytes = 15L * 1000 * 1000 * 1000 });
Check(Row(report, "pc-disk").State == "low", "verdict: under 20 GB free is low");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { PcFreeBytes = 90L * 1000 * 1000 * 1000 });
Check(Row(report, "pc-disk").State == "low", "verdict: under 10% free is low");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { CoreUpdateAvailable = true, LatestCoreVersion = "3.4.0" });
Check(Row(report, "core-update") is { Level: HealthLevel.Info, State: "available" } && (string?)Row(report, "core-update").Data["latest"] == "3.4.0", "verdict: Core update available");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { CoreUpdateAvailable = null, LatestCoreVersion = null });
Check(Row(report, "core-update").Level == HealthLevel.Unknown, "verdict: no cached check yet");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { Microphone = "muted" });
Check(Row(report, "microphone") is { Level: HealthLevel.Info, State: "muted" } && report.Attention == 0, "verdict: a muted microphone is shown, not counted as a problem");
Check(Row(CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { Microphone = "live" }), "microphone").Level == HealthLevel.Ok, "verdict: live microphone");
Check(Row(CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { Microphone = "none" }), "microphone").State == "none", "verdict: no microphone");
Check(Row(CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now })), "microphone").Level == HealthLevel.Unknown, "verdict: microphone unknown");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { Microphone = "partial" });
Check(Row(report, "microphone") is { Level: HealthLevel.Attention, State: "partial" } && report.Attention == 1 && report.Problems.Count == 0, "verdict: a partly muted microphone is worth a look (no notice)");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { Microphone = "live", CallsMicName = "Microphone (Wireless Microphone RX)", CallsMicStatus = "absent" });
Check(Row(report, "microphone") is { Level: HealthLevel.Ok, State: "live" } && report.Attention == 0 && report.Problems.Count == 0
	&& (string?)Row(report, "microphone").Data["callsMic"] == "Microphone (Wireless Microphone RX)" && (string?)Row(report, "microphone").Data["callsMicStatus"] == "absent",
	"verdict: an absent calls mic is information only (no attention, no notice), name and status carried");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { Microphone = "live", CallsMicName = "Microphone (Wireless Microphone RX)", CallsMicStatus = "active" });
Check(Row(report, "microphone").Level == HealthLevel.Ok && (string?)Row(report, "microphone").Data["callsMicStatus"] == "active", "verdict: connected calls mic");

// The server refuses an old Proton CLI or loses the canary: a failed night whose message Core shows.
const string tooOld = "Proton CLI too old: this proton-drive offers only --conflict-strategy, which may skip whole folders that already exist in Drive, so new saves, BIOS and ROMs would silently not upload. Nothing was uploaded. Update the Proton Drive CLI (v0.8.0 or later).";
ServerStatus refused = status with { BackupResult = "failed", BackupMessage = tooOld, ConsecutiveFailures = 1 };
report = CoreHealthPolicy.Evaluate(Base(server: refused with { WrittenAt = now }));
Check(Row(report, "server-backup") is { Level: HealthLevel.Attention, State: "failed" } row2 && (string?)row2.Data["message"] == tooOld, "verdict: Proton CLI too old is a failed backup with its message");

// ---------- Stream mode ----------
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }));
Check(Row(report, "streaming") is { Level: HealthLevel.Ok, State: "idle" }, "verdict: not streaming is idle");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { StreamActive = true, StreamDetected = true, StreamSource = "steam", StreamDevice = "Steam Frame", TvHeld = true, TvReachable = null });
Check(Row(report, "streaming") is { Level: HealthLevel.Info, State: "streaming" } streamRow && (string?)streamRow.Data["device"] == "Steam Frame" && (string?)streamRow.Data["source"] == "steam",
	"verdict: streaming to a device is information");
Check(Row(report, "tv") is { Level: HealthLevel.Info, State: "stream-mode" } && report.Attention == 0, "verdict: a held TV is not probed and not a problem");
report = CoreHealthPolicy.Evaluate(Base(server: status with { WrittenAt = now }) with { StreamActive = true, StreamForced = true, TvHeld = true });
Check(Row(report, "streaming").State == "kept-out" && Row(report, "streaming").Data["source"] is null, "verdict: Keep TV out of it without a stream");

// ---------- alerts ----------
var failing = new HealthProblem("backup-failing", "server-backup", "backup|2026-11-20T10:00:00.0000000+00:00", "health.backupFailing");
var drive = new HealthProblem("server-drive", "server-drive", "drive|warning", "health.driveWarning");
var ledger = HealthAlertPolicy.Track(new[] { failing }, new Dictionary<string, HealthAlertEntry>());
var due = HealthAlertPolicy.Due(new[] { failing }, ledger, now);
Check(due.Count == 1 && HealthAlertPolicy.NoticeKey(due) == "health.backupFailing", "alert: new problem is due");
ledger = HealthAlertPolicy.MarkShown(ledger, due, now);
Check(HealthAlertPolicy.Due(new[] { failing }, HealthAlertPolicy.Track(new[] { failing }, ledger), now.AddHours(23)).Count == 0, "alert: not again within a day");
Check(HealthAlertPolicy.Due(new[] { failing }, HealthAlertPolicy.Track(new[] { failing }, ledger), now.AddHours(24)).Count == 1, "alert: again after a day");
ledger = HealthAlertPolicy.Dismiss(HealthAlertPolicy.Track(new[] { failing }, ledger), "backup-failing");
Check(HealthAlertPolicy.Due(new[] { failing }, HealthAlertPolicy.Track(new[] { failing }, ledger), now.AddDays(5)).Count == 0, "alert: dismissed stays quiet while unchanged");
var changed = failing with { Fingerprint = "backup|2026-11-24T10:00:00.0000000+00:00" };
var tracked = HealthAlertPolicy.Track(new[] { changed }, ledger);
Check(HealthAlertPolicy.Due(new[] { changed }, tracked, now.AddDays(5)).Count == 1, "alert: dismissed problem returns when it changes");
tracked = HealthAlertPolicy.Track(Array.Empty<HealthProblem>(), tracked);
Check(tracked.Count == 0, "alert: a cleared problem leaves the ledger (it alerts again if it comes back)");
tracked = HealthAlertPolicy.Track(new[] { failing, drive }, tracked);
due = HealthAlertPolicy.Due(new[] { failing, drive }, tracked, now);
Check(due.Count == 2 && HealthAlertPolicy.NoticeKey(due) == "health.several", "alert: several problems share one notice");
var worse = drive with { Fingerprint = "drive|unhealthy" };
tracked = HealthAlertPolicy.Dismiss(HealthAlertPolicy.MarkShown(tracked, due, now), "server-drive");
Check(HealthAlertPolicy.Due(new[] { worse }, HealthAlertPolicy.Track(new[] { worse }, tracked), now.AddHours(1)).Count == 1, "alert: warning to unhealthy alerts at once");
string file = Path.Combine(Path.GetTempPath(), "core-health-alerts-" + Guid.NewGuid().ToString("N") + ".json");
try
{
	HealthAlertPolicy.Save(file, tracked);
	var loaded = HealthAlertPolicy.Load(file);
	Check(loaded.Count == tracked.Count && loaded.All(pair => tracked[pair.Key] == pair.Value), "alert: ledger round-trips through its file");
	File.WriteAllText(file, "{broken");
	Check(HealthAlertPolicy.Load(file).Count == 0, "alert: a broken ledger starts fresh");
}
finally { File.Delete(file); }

Console.WriteLine($"ALL PASS ({passed} checks)");
return 0;
