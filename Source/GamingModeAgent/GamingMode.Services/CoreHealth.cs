using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core: "Core Health". One place that says whether the living-room setup is fine: the
// home server's nightly backup and health (read from http://<server>:8123/local/core-status.json, written
// by the Aincrad repo's nightly scripts/backup.sh), Windows restarts and update night, the TV, the PC's system drive
// and Core's own version. Pure: observations in, rows and problems out. The agent gathers the
// observations (MarkIIIHost) and the Quick Access tab renders the rows (src/HealthSection.tsx).
internal enum ServerStatusRead { NotConfigured, Unreachable, Missing, Unreadable, Ok }

internal enum HealthLevel { Ok, Attention, Info, Unknown }

internal sealed record ServerCategory(bool Ok, long Files, long Bytes);

// core-status.json, schema 1. Unknown fields are ignored and missing ones are null, so a newer
// server script (schema > 1 adds fields) still reads.
internal sealed record ServerStatus
{
	public int Schema { get; init; }
	public DateTimeOffset? WrittenAt { get; init; }
	public string? Server { get; init; }
	public string? BackupResult { get; init; }
	public DateTimeOffset? BackupStartedAt { get; init; }
	public DateTimeOffset? BackupFinishedAt { get; init; }
	public string? BackupMessage { get; init; }
	public long? UploadedBytes { get; init; }
	public int ConsecutiveFailures { get; init; }
	public DateTimeOffset? LastSuccessAt { get; init; }
	public IReadOnlyDictionary<string, ServerCategory> Categories { get; init; } = new Dictionary<string, ServerCategory>();
	public IReadOnlyList<string> SkippedPlatforms { get; init; } = Array.Empty<string>();
	public long? PoolUsedBytes { get; init; }
	public long? PoolTotalBytes { get; init; }
	public DateTimeOffset? RestoreAt { get; init; }
	public string? RestoreResult { get; init; }
	public string? RestoreMessage { get; init; }
	public string? DriveLabel { get; init; }
	public string? DriveHealth { get; init; }
	public long? DriveFreeBytes { get; init; }
	public long? DriveTotalBytes { get; init; }
	public string? DriveMessage { get; init; }
	public bool? RommReachable { get; init; }
	public DateTimeOffset? RommCheckedAt { get; init; }
	public string? RommMessage { get; init; }
}

internal sealed record CoreHealthInputs
{
	public DateTimeOffset Now { get; init; }
	public string? ServerName { get; init; }
	public ServerStatusRead ServerRead { get; init; }
	public ServerStatus? Server { get; init; }
	public bool RestartPending { get; init; }
	public bool UpdateNightOffer { get; init; }
	public DateTimeOffset? UpdateNightScheduledFor { get; init; }
	public bool TvEnabled { get; init; }
	public bool TvPaired { get; init; }
	public bool? TvReachable { get; init; }
	public string? PcDrive { get; init; }
	public long? PcFreeBytes { get; init; }
	public long? PcTotalBytes { get; init; }
	public string CoreVersion { get; init; } = "";
	public string? LatestCoreVersion { get; init; }
	public bool? CoreUpdateAvailable { get; init; }
	// Core Voice: "muted", "live", "partial" (some default microphones muted) or "none" (no
	// microphone); null when Windows could not say.
	public string? Microphone { get; init; }
	// The calls mic chosen in Core > Voice: its name and "active", "absent", "changed" or "failed";
	// null when none is chosen. Information only: an absent calls mic is never a problem (it often
	// lives on another PC), so it adds no attention and no notice.
	public string? CallsMicName { get; init; }
	public string? CallsMicStatus { get; init; }
	// Stream mode (CoreStreamMode.cs): on, forced by "Keep TV out of it", seen streaming; the source code
	// ("steam", "steamvr", "sunshine", "apollo") and the device when known; Apollo's virtual display when
	// Apollo runs. Information only. While on, the TV is not probed (TvHeld): nothing is sent to it.
	public bool StreamActive { get; init; }
	public bool StreamForced { get; init; }
	public bool StreamDetected { get; init; }
	public string? StreamSource { get; init; }
	public string? StreamDevice { get; init; }
	public bool? StreamVirtualDisplay { get; init; }
	public bool TvHeld { get; init; }
}

internal sealed record HealthRow(string Id, HealthLevel Level, string State, IReadOnlyDictionary<string, object?> Data);

// A problem that may raise a notice. The fingerprint changes when the problem changes in a way the
// user should hear about again (a new failure streak, a worse drive state, a new stale file).
internal sealed record HealthProblem(string Id, string RowId, string Fingerprint, string NoticeKey);

internal sealed record HealthReport(int Attention, IReadOnlyList<HealthRow> Rows, IReadOnlyList<HealthProblem> Problems);

internal static class CoreHealthPolicy
{
	internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(36);
	internal static readonly TimeSpan RestoreOverdueAfter = TimeSpan.FromDays(45);
	internal const int FailuresBeforeAlert = 2;
	internal const double PcMinFreeFraction = 0.10;
	internal const long PcMinFreeBytes = 20L * 1000 * 1000 * 1000;

	// The order the Quick Access section shows the rows in.
	internal static readonly string[] RowOrder = { "server-backup", "restore-check", "server-drive", "romm", "windows-restart", "update-night", "tv", "streaming", "microphone", "pc-disk", "core-update" };

	// The home server running Aincrad: "serverv3", "192.168.1.109", "serverv3:8123" or a full http(s) URL.
	// Its nightly backup writes core-status.json into Home Assistant's www/ folder, served without a
	// login at /local/. A leading "\\\\" from the old file-share setting is tolerated.
	internal const int HomeAssistantPort = 8123;
	internal const string StatusUrlPath = "/local/core-status.json";

	internal static Uri? StatusUrl(string? server)
	{
		if (string.IsNullOrWhiteSpace(server)) return null;
		string value = server.Trim();
		if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			value = value.TrimStart('\\', '/').Split('\\', '/')[0];
			if (value.Length == 0) return null;
			value = "http://" + value;
			if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? bare) || bare.UserInfo.Length > 0) return null;
			return new UriBuilder(bare.Scheme, bare.Host, bare.IsDefaultPort ? HomeAssistantPort : bare.Port, StatusUrlPath).Uri;
		}
		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url) || url.UserInfo.Length > 0 || url.Host.Length == 0) return null;
		if (url.AbsolutePath is "" or "/") return new UriBuilder(url.Scheme, url.Host, url.Port, StatusUrlPath).Uri;
		return url;
	}

	// ---------- core-status.json ----------

	internal static ServerStatus? Parse(string json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || Int(root, "schema") is not int schema || schema < 1) return null;
			JsonElement backup = Obj(root, "backup"), drive = Obj(root, "drive"), romm = Obj(root, "romm");
			JsonElement pool = Obj(backup, "pool"), restore = Obj(backup, "restore_check"), categories = Obj(backup, "categories");
			var parsedCategories = new Dictionary<string, ServerCategory>(StringComparer.Ordinal);
			if (categories.ValueKind == JsonValueKind.Object)
				foreach (JsonProperty category in categories.EnumerateObject())
					if (category.Value.ValueKind == JsonValueKind.Object)
						parsedCategories[category.Name] = new ServerCategory(Bool(category.Value, "ok") == true, Long(category.Value, "files") ?? 0, Long(category.Value, "bytes") ?? 0);
			var skipped = new List<string>();
			if (backup.ValueKind == JsonValueKind.Object && backup.TryGetProperty("roms_skipped_platforms", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
				foreach (JsonElement item in list.EnumerateArray())
					if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 and <= 40 } name) skipped.Add(name);
			return new ServerStatus
			{
				Schema = schema,
				WrittenAt = Time(root, "written_at"),
				Server = Text(root, "server"),
				BackupResult = Text(backup, "result"),
				BackupStartedAt = Time(backup, "started_at"),
				BackupFinishedAt = Time(backup, "finished_at"),
				BackupMessage = Text(backup, "message"),
				UploadedBytes = Long(backup, "uploaded_bytes"),
				ConsecutiveFailures = Int(backup, "consecutive_failures") ?? 0,
				LastSuccessAt = Time(backup, "last_success_at"),
				Categories = parsedCategories,
				SkippedPlatforms = skipped,
				PoolUsedBytes = Long(pool, "used_bytes"),
				PoolTotalBytes = Long(pool, "total_bytes"),
				RestoreAt = Time(restore, "at"),
				RestoreResult = Text(restore, "result"),
				RestoreMessage = Text(restore, "message"),
				DriveLabel = Text(drive, "label"),
				DriveHealth = Text(drive, "health"),
				DriveFreeBytes = Long(drive, "free_bytes"),
				DriveTotalBytes = Long(drive, "total_bytes"),
				DriveMessage = Text(drive, "message"),
				RommReachable = Bool(romm, "reachable"),
				RommCheckedAt = Time(romm, "checked_at"),
				RommMessage = Text(romm, "message"),
			};
		}
		catch (JsonException) { return null; }
	}

	private static JsonElement Obj(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object ? value : default;

	private static string? Text(JsonElement parent, string name)
	{
		if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;
		string text = value.GetString() ?? "";
		return text.Length > 400 ? text[..400] + "…" : text;
	}

	private static long? Long(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : null;

	private static int? Int(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;

	private static bool? Bool(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

	private static DateTimeOffset? Time(JsonElement parent, string name) =>
		Text(parent, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset time) ? time : null;

	// ---------- verdict ----------

	internal static HealthReport Evaluate(CoreHealthInputs input)
	{
		var rows = new List<HealthRow>();
		var problems = new List<HealthProblem>();
		ServerStatus? server = input.Server;
		ServerStatusRead read = input.ServerRead;
		if (read == ServerStatusRead.Ok && (server is null || server.WrittenAt is null)) read = ServerStatusRead.Unreadable;
		bool stale = read == ServerStatusRead.Ok && input.Now - server!.WrittenAt!.Value > StaleAfter;
		var source = new Dictionary<string, object?> { ["server"] = input.ServerName, ["read"] = Code(read), ["stale"] = stale, ["writtenAt"] = server?.WrittenAt };

		// Server backup.
		if (read != ServerStatusRead.Ok)
		{
			HealthLevel level = read == ServerStatusRead.NotConfigured ? HealthLevel.Info : HealthLevel.Attention;
			rows.Add(new HealthRow("server-backup", level, Code(read), source));
		}
		else
		{
			var data = new Dictionary<string, object?>(source)
			{
				["result"] = server!.BackupResult, ["lastSuccessAt"] = server.LastSuccessAt, ["finishedAt"] = server.BackupFinishedAt,
				["consecutiveFailures"] = server.ConsecutiveFailures, ["skippedPlatforms"] = server.SkippedPlatforms, ["message"] = server.BackupMessage,
				["uploadedBytes"] = server.UploadedBytes, ["poolUsedBytes"] = server.PoolUsedBytes, ["poolTotalBytes"] = server.PoolTotalBytes,
				["categories"] = server.Categories.ToDictionary(pair => pair.Key, pair => (object?)new Dictionary<string, object?> { ["ok"] = pair.Value.Ok, ["files"] = pair.Value.Files, ["bytes"] = pair.Value.Bytes }),
			};
			if (stale)
			{
				rows.Add(new HealthRow("server-backup", HealthLevel.Attention, "stale", data));
				problems.Add(new HealthProblem("status-stale", "server-backup", "stale|" + Stamp(server.WrittenAt), "health.statusStale"));
			}
			else
			{
				(HealthLevel level, string state) = server.BackupResult switch
				{
					"ok" => (HealthLevel.Ok, "ok"),
					"partial" => (HealthLevel.Attention, "partial"),
					"failed" => (HealthLevel.Attention, "failed"),
					"never" => (HealthLevel.Info, "never"),
					_ => (HealthLevel.Unknown, "unknown"),
				};
				rows.Add(new HealthRow("server-backup", level, state, data));
			}
			if (server.ConsecutiveFailures >= FailuresBeforeAlert)
				problems.Add(new HealthProblem("backup-failing", "server-backup", "backup|" + Stamp(server.LastSuccessAt), "health.backupFailing"));
		}

		// Restore check, server drive, RomM: only from a fresh status file.
		if (read != ServerStatusRead.Ok)
		{
			foreach (string id in new[] { "restore-check", "server-drive", "romm" }) rows.Add(new HealthRow(id, HealthLevel.Unknown, Code(read), source));
		}
		else
		{
			var restore = new Dictionary<string, object?>(source) { ["result"] = server!.RestoreResult, ["at"] = server.RestoreAt, ["message"] = server.RestoreMessage };
			(HealthLevel level, string state) restoreVerdict = server.RestoreResult switch
			{
				"failed" => (HealthLevel.Attention, "failed"),
				"ok" when server.RestoreAt is { } at && input.Now - at > RestoreOverdueAfter => (HealthLevel.Attention, "overdue"),
				"ok" => (HealthLevel.Ok, "ok"),
				"never" or null => (HealthLevel.Info, "never"),
				_ => (HealthLevel.Unknown, "unknown"),
			};
			rows.Add(new HealthRow("restore-check", stale ? HealthLevel.Unknown : restoreVerdict.level, stale ? "stale" : restoreVerdict.state, restore));

			var drive = new Dictionary<string, object?>(source) { ["label"] = server.DriveLabel, ["health"] = server.DriveHealth, ["freeBytes"] = server.DriveFreeBytes, ["totalBytes"] = server.DriveTotalBytes, ["message"] = server.DriveMessage };
			(HealthLevel level, string state) driveVerdict = server.DriveHealth switch
			{
				"healthy" => (HealthLevel.Ok, "healthy"),
				"warning" => (HealthLevel.Attention, "warning"),
				"unhealthy" => (HealthLevel.Attention, "unhealthy"),
				_ => (HealthLevel.Unknown, "unknown"),
			};
			rows.Add(new HealthRow("server-drive", stale ? HealthLevel.Unknown : driveVerdict.level, stale ? "stale" : driveVerdict.state, drive));
			if (server.DriveHealth is "warning" or "unhealthy")
				problems.Add(new HealthProblem("server-drive", "server-drive", "drive|" + server.DriveHealth, "health.driveWarning"));

			var romm = new Dictionary<string, object?>(source) { ["reachable"] = server.RommReachable, ["checkedAt"] = server.RommCheckedAt, ["message"] = server.RommMessage };
			(HealthLevel level, string state) rommVerdict = server.RommReachable switch
			{
				true => (HealthLevel.Ok, "reachable"),
				false => (HealthLevel.Attention, "unreachable"),
				null => (HealthLevel.Unknown, "unknown"),
			};
			rows.Add(new HealthRow("romm", stale ? HealthLevel.Unknown : rommVerdict.level, stale ? "stale" : rommVerdict.state, romm));
		}

		// Windows restart: information; update night carries the call to action.
		rows.Add(new HealthRow("windows-restart", input.RestartPending ? HealthLevel.Info : HealthLevel.Ok, input.RestartPending ? "pending" : "none",
			new Dictionary<string, object?> { ["pending"] = input.RestartPending }));
		var night = new Dictionary<string, object?> { ["scheduledFor"] = input.UpdateNightScheduledFor, ["offer"] = input.UpdateNightOffer, ["hour"] = UpdateNightPolicy.RestartHour };
		if (input.UpdateNightScheduledFor is not null) rows.Add(new HealthRow("update-night", HealthLevel.Ok, "scheduled", night));
		else if (input.UpdateNightOffer) rows.Add(new HealthRow("update-night", HealthLevel.Attention, "waiting", night));
		else rows.Add(new HealthRow("update-night", HealthLevel.Ok, "nothing", night));

		// TV: read only; never woken for this.
		var tv = new Dictionary<string, object?> { ["enabled"] = input.TvEnabled, ["paired"] = input.TvPaired, ["reachable"] = input.TvReachable };
		if (!input.TvEnabled) rows.Add(new HealthRow("tv", HealthLevel.Info, "off", tv));
		else if (!input.TvPaired) rows.Add(new HealthRow("tv", HealthLevel.Attention, "not-paired", tv));
		else if (input.TvHeld) rows.Add(new HealthRow("tv", HealthLevel.Info, "stream-mode", tv));
		else if (input.TvReachable == true) rows.Add(new HealthRow("tv", HealthLevel.Ok, "reachable", tv));
		else if (input.TvReachable == false) rows.Add(new HealthRow("tv", HealthLevel.Attention, "unreachable", tv));
		else rows.Add(new HealthRow("tv", HealthLevel.Unknown, "unknown", tv));

		// Streaming: information only ("idle", "streaming" to a device, or "kept-out" by the override).
		var stream = new Dictionary<string, object?> { ["source"] = input.StreamDetected ? input.StreamSource : null, ["device"] = input.StreamDevice, ["forced"] = input.StreamForced, ["virtualDisplay"] = input.StreamVirtualDisplay };
		rows.Add(!input.StreamActive ? new HealthRow("streaming", HealthLevel.Ok, "idle", stream)
			: input.StreamDetected ? new HealthRow("streaming", HealthLevel.Info, "streaming", stream)
			: new HealthRow("streaming", HealthLevel.Info, "kept-out", stream));

		// Microphone: information only. Muted is a choice, not a problem (the plugin shows it in crimson).
		// Partly muted is worth a look: a call may use the muted one while the badge's other mic is live.
		// The calls mic's name and connected/absent ride along as data; they never change the level.
		var mic = new Dictionary<string, object?> { ["state"] = input.Microphone, ["callsMic"] = input.CallsMicName, ["callsMicStatus"] = input.CallsMicStatus };
		rows.Add(input.Microphone switch
		{
			"muted" => new HealthRow("microphone", HealthLevel.Info, "muted", mic),
			"partial" => new HealthRow("microphone", HealthLevel.Attention, "partial", mic),
			"live" => new HealthRow("microphone", HealthLevel.Ok, "live", mic),
			"none" => new HealthRow("microphone", HealthLevel.Info, "none", mic),
			_ => new HealthRow("microphone", HealthLevel.Unknown, "unknown", mic),
		});

		// PC system drive.
		var pc = new Dictionary<string, object?> { ["drive"] = input.PcDrive, ["freeBytes"] = input.PcFreeBytes, ["totalBytes"] = input.PcTotalBytes };
		if (input.PcFreeBytes is not long free || input.PcTotalBytes is not long total || total <= 0) rows.Add(new HealthRow("pc-disk", HealthLevel.Unknown, "unknown", pc));
		else if (free < PcMinFreeBytes || free < total * PcMinFreeFraction) rows.Add(new HealthRow("pc-disk", HealthLevel.Attention, "low", pc));
		else rows.Add(new HealthRow("pc-disk", HealthLevel.Ok, "ok", pc));

		// Core's version against the last cached release check.
		var core = new Dictionary<string, object?> { ["current"] = input.CoreVersion, ["latest"] = input.LatestCoreVersion };
		rows.Add(input.CoreUpdateAvailable switch
		{
			true => new HealthRow("core-update", HealthLevel.Info, "available", core),
			false => new HealthRow("core-update", HealthLevel.Ok, "current", core),
			null => new HealthRow("core-update", HealthLevel.Unknown, "unknown", core),
		});

		return new HealthReport(rows.Count(row => row.Level == HealthLevel.Attention), rows, problems);
	}

	internal static string Code(ServerStatusRead read) => read switch
	{
		ServerStatusRead.NotConfigured => "not-configured",
		ServerStatusRead.Unreachable => "unreachable",
		ServerStatusRead.Missing => "missing",
		ServerStatusRead.Unreadable => "unreadable",
		_ => "ok",
	};

	internal static string Code(HealthLevel level) => level switch
	{
		HealthLevel.Ok => "ok",
		HealthLevel.Attention => "attention",
		HealthLevel.Info => "info",
		_ => "unknown",
	};

	private static string Stamp(DateTimeOffset? time) => time?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? "never";
}

// ---------- alerts: at most once a day per problem; a dismissed problem returns only if it changes ----------

internal sealed record HealthAlertEntry
{
	public string Fingerprint { get; init; } = "";
	public DateTimeOffset? LastShown { get; init; }
	public string? DismissedFingerprint { get; init; }
}

internal static class HealthAlertPolicy
{
	internal static readonly TimeSpan Repeat = TimeSpan.FromHours(24);

	// The ledger keeps only problems that exist now: a problem that clears and comes back is new.
	internal static Dictionary<string, HealthAlertEntry> Track(IReadOnlyList<HealthProblem> problems, IReadOnlyDictionary<string, HealthAlertEntry> ledger)
	{
		var next = new Dictionary<string, HealthAlertEntry>(StringComparer.Ordinal);
		foreach (HealthProblem problem in problems)
		{
			HealthAlertEntry entry = ledger.TryGetValue(problem.Id, out HealthAlertEntry? known) ? known : new HealthAlertEntry { Fingerprint = problem.Fingerprint };
			if (entry.Fingerprint != problem.Fingerprint) entry = entry with { Fingerprint = problem.Fingerprint, LastShown = null };
			next[problem.Id] = entry;
		}
		return next;
	}

	internal static IReadOnlyList<HealthProblem> Due(IReadOnlyList<HealthProblem> problems, IReadOnlyDictionary<string, HealthAlertEntry> tracked, DateTimeOffset now) =>
		problems.Where(problem => tracked.TryGetValue(problem.Id, out HealthAlertEntry? entry)
			&& entry.DismissedFingerprint != problem.Fingerprint
			&& (entry.LastShown is null || now - entry.LastShown.Value >= Repeat || entry.LastShown.Value > now)).ToList();

	internal static Dictionary<string, HealthAlertEntry> MarkShown(IReadOnlyDictionary<string, HealthAlertEntry> tracked, IEnumerable<HealthProblem> shown, DateTimeOffset now)
	{
		var next = new Dictionary<string, HealthAlertEntry>(tracked, StringComparer.Ordinal);
		foreach (HealthProblem problem in shown)
			if (next.TryGetValue(problem.Id, out HealthAlertEntry? entry)) next[problem.Id] = entry with { LastShown = now };
		return next;
	}

	internal static Dictionary<string, HealthAlertEntry> Dismiss(IReadOnlyDictionary<string, HealthAlertEntry> tracked, string id)
	{
		var next = new Dictionary<string, HealthAlertEntry>(tracked, StringComparer.Ordinal);
		if (next.TryGetValue(id, out HealthAlertEntry? entry)) next[id] = entry with { DismissedFingerprint = entry.Fingerprint };
		return next;
	}

	// One notice for everything due: the problem's own text, or a summary for several.
	internal static string? NoticeKey(IReadOnlyList<HealthProblem> due) => due.Count switch
	{
		0 => null,
		1 => due[0].NoticeKey,
		_ => "health.several",
	};

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static Dictionary<string, HealthAlertEntry> Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new(StringComparer.Ordinal);
			var loaded = JsonSerializer.Deserialize<Dictionary<string, HealthAlertEntry>>(File.ReadAllText(path), Json);
			return loaded is null ? new(StringComparer.Ordinal) : new(loaded, StringComparer.Ordinal);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(StringComparer.Ordinal); }
	}

	internal static void Save(string path, IReadOnlyDictionary<string, HealthAlertEntry> ledger)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, Json));
		File.Move(temporary, path, true);
	}
}
