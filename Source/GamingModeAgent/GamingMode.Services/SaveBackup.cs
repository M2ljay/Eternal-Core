using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core batch 1: the update-night save backup. Before Core's update-night restart and before
// an automatic Steam update, Core asks Ludusavi (https://github.com/mtkennerly/ludusavi, MIT, installed
// separately, for example with `winget install -e --id mtkennerly.ludusavi`) to back up every game's
// saves: `ludusavi backup --force --api`. Optional and fail-soft: no Ludusavi, no backup; a failure or
// a timeout never blocks the restart or the update, it only shows in Core Health ("Save backup").
// Ludusavi is found the way the Forge plugin finds it: PATH first, then winget's Links and Packages
// folders (per user and machine-wide), then Scoop's app and shim folders. Pure here; the process run
// is in MarkIIIHost.Batch.cs. See docs/eternal/BATCH-1.md.
internal sealed record SaveBackupState
{
	public DateTimeOffset? LastRunAt { get; init; }
	// "ok", "failed", "timed-out" or "not-installed".
	public string? LastResult { get; init; }
	// Why it failed, in one line (never Ludusavi's raw output).
	public string? LastReason { get; init; }
	public int Games { get; init; }
	public long Bytes { get; init; }
	// "update-night" or "steam-update".
	public string? Trigger { get; init; }
}

internal sealed record SaveBackupOutcome(string Result, string? Reason, int Games, long Bytes);

// What Core Health needs for the "Save backup" row.
internal sealed record SaveBackupInputs(bool Enabled, bool Installed, SaveBackupState State);

internal static class SaveBackupPolicy
{
	internal const string WingetId = "mtkennerly.ludusavi";
	internal static readonly TimeSpan Limit = TimeSpan.FromMinutes(10);
	// One backup covers the night: the update-night restart and a Steam update a few hours apart share it.
	internal static readonly TimeSpan CoversFor = TimeSpan.FromHours(6);
	// "Last night" in the Health row: an older result is still shown, with its date.
	internal static readonly TimeSpan RecentFor = TimeSpan.FromHours(36);

	internal static readonly string[] Arguments = { "backup", "--force", "--api" };

	// Where winget and Scoop put ludusavi.exe, for a process whose PATH predates the install (winget adds
	// its Links folder to the user's PATH, which a running agent does not see). Same order as Forge.
	internal static IReadOnlyList<string> CandidatePaths(Func<string, string?> env, Func<string, string, IEnumerable<string>> directories)
	{
		const string exe = "ludusavi.exe";
		var paths = new List<string>();
		foreach (string? root in new[] { Combine(env("LOCALAPPDATA"), "Microsoft", "WinGet"), Combine(env("ProgramFiles"), "WinGet") })
		{
			if (root is null) continue;
			paths.Add(Path.Combine(root, "Links", exe));
			// Portable packages unpack to Packages\<id>_<source>\ (the zip's root, or one folder below).
			string packages = Path.Combine(root, "Packages");
			IEnumerable<string> found;
			try { found = directories(packages, WingetId + "_*").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { found = Array.Empty<string>(); }
			foreach (string package in found)
			{
				paths.Add(Path.Combine(package, exe));
				IEnumerable<string> inner;
				try { inner = directories(package, "*").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList(); }
				catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { inner = Array.Empty<string>(); }
				foreach (string folder in inner) paths.Add(Path.Combine(folder, exe));
			}
		}
		string? profile = env("USERPROFILE");
		string? programData = env("ProgramData");
		foreach (string? root in new[] { env("SCOOP") ?? Combine(profile, "scoop"), env("SCOOP_GLOBAL") ?? Combine(programData, "scoop") })
		{
			if (root is null) continue;
			paths.Add(Path.Combine(root, "apps", "ludusavi", "current", exe));
			paths.Add(Path.Combine(root, "shims", exe));
		}
		return paths;
	}

	// ludusavi.exe on PATH, then the candidates. Null: not installed.
	internal static string? Find(Func<string, string?> env, Func<string, bool> exists, Func<string, string, IEnumerable<string>> directories)
	{
		foreach (string folder in (env("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string candidate;
			try { candidate = Path.Combine(folder.Trim('"'), "ludusavi.exe"); }
			catch (ArgumentException) { continue; }
			if (Safe(exists, candidate)) return candidate;
		}
		foreach (string candidate in CandidatePaths(env, directories))
			if (Safe(exists, candidate)) return candidate;
		return null;
	}

	private static bool Safe(Func<string, bool> exists, string path)
	{
		try { return exists(path); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
	}

	private static string? Combine(string? root, params string[] parts) =>
		string.IsNullOrWhiteSpace(root) ? null : Path.Combine(new[] { root }.Concat(parts).ToArray());

	// Back up now? Only with the setting on and Ludusavi installed, and once a night: a run in the last few hours
	// (good or not) covers tonight, so a failed run is not repeated before the restart or the Steam update.
	internal static bool ShouldRun(bool enabled, bool installed, SaveBackupState state, DateTimeOffset now) =>
		enabled && installed && !(state.LastRunAt is { } last && now >= last && now - last < CoversFor);

	// Ludusavi's --api answer: {"errors": {...}, "overall": {"totalGames", "processedGames", "processedBytes", ...},
	// "games": {...}}. ok also when some cloud step failed (the local copy is made); failed when a game's files
	// failed or the exit code says so. Its docs say stdout can be blank on some errors.
	internal static SaveBackupOutcome Judge(int exitCode, string? output, bool timedOut)
	{
		if (timedOut) return new("timed-out", "time-limit", 0, 0);
		string text = (output ?? "").Trim();
		int start = text.IndexOf('{');
		if (start < 0) return new("failed", exitCode == 0 ? "no-answer" : "exit-" + exitCode, 0, 0);
		try
		{
			using JsonDocument document = JsonDocument.Parse(text[start..]);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return new("failed", "no-answer", 0, 0);
			int games = 0; long bytes = 0;
			if (root.TryGetProperty("overall", out JsonElement overall) && overall.ValueKind == JsonValueKind.Object)
			{
				if (overall.TryGetProperty("processedGames", out JsonElement processed) && processed.TryGetInt32(out int count)) games = count;
				if (overall.TryGetProperty("processedBytes", out JsonElement size) && size.TryGetInt64(out long total)) bytes = total;
			}
			bool someFailed = root.TryGetProperty("errors", out JsonElement errors) && errors.ValueKind == JsonValueKind.Object
				&& errors.TryGetProperty("someGamesFailed", out JsonElement failed) && failed.ValueKind == JsonValueKind.True;
			if (someFailed) return new("failed", "some-games-failed", games, bytes);
			if (exitCode != 0) return new("failed", "exit-" + exitCode, games, bytes);
			return new("ok", null, games, bytes);
		}
		catch (JsonException) { return new("failed", "no-answer", 0, 0); }
	}

	internal static SaveBackupState Record(SaveBackupState state, SaveBackupOutcome outcome, string trigger, DateTimeOffset now) => state with
	{
		LastRunAt = now, LastResult = outcome.Result, LastReason = outcome.Reason, Games = outcome.Games, Bytes = outcome.Bytes, Trigger = trigger,
	};

	// ---------- Core Health row ----------

	// "Save backup: last night OK / failed / Ludusavi not installed". Failed is attention (the row turns crimson)
	// but raises no notice: the update went ahead and the next night tries again.
	internal static HealthRow Row(bool enabled, bool installed, SaveBackupState state, DateTimeOffset now)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["at"] = state.LastRunAt, ["reason"] = state.LastReason, ["games"] = state.Games, ["bytes"] = state.Bytes, ["trigger"] = state.Trigger,
			["recent"] = state.LastRunAt is { } at && now - at < RecentFor && now >= at,
		};
		if (!installed) return new HealthRow("save-backup", HealthLevel.Info, "not-installed", data);
		if (!enabled) return new HealthRow("save-backup", HealthLevel.Info, "off", data);
		return state.LastResult switch
		{
			"ok" => new HealthRow("save-backup", HealthLevel.Ok, "ok", data),
			"failed" or "timed-out" => new HealthRow("save-backup", HealthLevel.Attention, "failed", data),
			_ => new HealthRow("save-backup", HealthLevel.Ok, "never", data),
		};
	}

	// ---------- state file ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static SaveBackupState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<SaveBackupState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, SaveBackupState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}
