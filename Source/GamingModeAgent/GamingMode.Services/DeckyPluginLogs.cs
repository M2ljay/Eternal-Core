using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core batch 1 (B11): Decky health names the failing plugin. Decky writes one log per plugin start
// under %USERPROFILE%\homebrew\logs\<plugin>\*.log. Core reads only what was added since its last look
// (a remembered byte offset per file, at most MaxRead bytes per file and round, whole lines only), counts
// ERROR/CRITICAL lines and Python tracebacks, and keeps, per plugin, when it last reported one and one
// sanitised line. Core Health then says "Decky: Forge is reporting errors", and the existing "plugins did not
// load" notice names them too. Nothing else from a log reaches the TV, and the one line is masked: anything
// that looks like a token, key, password, cookie, signed URL or user folder is replaced. The idea (per-plugin
// error state from Decky's logs) comes from Decky Task Manager; no code was taken (it has no licence).
// Pure here; the file reads are in MarkIIIHost.Batch.cs. See docs/eternal/BATCH-1.md.
internal sealed record DeckyLogFile
{
	public long Offset { get; init; }
	public DateTimeOffset? LastWrite { get; init; }
}

internal sealed record DeckyPluginErrors
{
	public DateTimeOffset LastErrorAt { get; init; }
	public int Count { get; init; }
	public string? Line { get; init; }
}

internal sealed record DeckyLogState
{
	public DateTimeOffset? LastScan { get; init; }
	public Dictionary<string, DeckyLogFile> Files { get; init; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, DeckyPluginErrors> Plugins { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

internal readonly record struct DeckyLogScan(int Errors, string? Line);

// What Core Health needs for the "Decky" row: the heartbeat monitor's verdict (DeckyVerdict's name) and the log state.
internal sealed record DeckyPluginInputs(string Verdict, DeckyLogState Logs);

internal static class DeckyLogPolicy
{
	internal const long MaxRead = 256 * 1024;
	internal const int MaxPlugins = 40, FilesPerPlugin = 2, MaxLine = 120;
	internal static readonly TimeSpan RecentFor = TimeSpan.FromHours(6);
	internal static readonly TimeSpan ScanEvery = TimeSpan.FromMinutes(1);
	// A file nobody wrote for a week is not followed any more.
	internal static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(7);

	// Folder names that are Core's own plugin: shown as "Core" (its identifiers still say Playhub).
	private static readonly string[] CoreFolders = { "gaming-mode", "Playhub" };

	internal static string DisplayName(string folder)
	{
		if (CoreFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return "Core";
		string name = Regex.Replace(folder, @"[\u0000-\u001f\u007f]", "").Trim();
		return name.Length > 40 ? name[..40] : name;
	}

	// Which bytes to read from a log file: (from, count), or null for nothing new. A file seen for the first
	// time is read only when it appeared after the last scan (a new plugin start); on the very first scan
	// every file is only measured, so old errors from before Core looked are never reported.
	internal static (long From, long Count)? ReadPlan(DeckyLogState state, string path, long length, DateTimeOffset lastWrite)
	{
		if (length <= 0) return null;
		if (state.Files.TryGetValue(path, out DeckyLogFile? known))
		{
			long offset = known.Offset > length ? 0 : known.Offset; // shorter than before: a new file under the same name
			if (offset == length) return null;
			long from = Math.Max(offset, length - MaxRead);
			return (from, length - from);
		}
		if (state.LastScan is not { } last || lastWrite <= last) return null;
		long start = Math.Max(0, length - MaxRead);
		return (start, length - start);
	}

	// Whole lines only: the bytes up to and including the last newline. A line still being written is read next time.
	internal static int CompleteLines(ReadOnlySpan<byte> buffer)
	{
		int last = buffer.LastIndexOf((byte)'\n');
		return last < 0 ? 0 : last + 1;
	}

	private static readonly Regex ErrorLine = new(@"(?:\[|\b)(?:ERROR|CRITICAL|FATAL)(?:\]|:|\s+-)", RegexOptions.CultureInvariant);
	private static readonly Regex Traceback = new(@"^\s*Traceback \(most recent call last\):", RegexOptions.CultureInvariant);
	private static readonly Regex ErrorMessage = new(@"(?:\[|\b)(?:ERROR|CRITICAL|FATAL)(?:\]|:|\s+-)\s*:?\s*(.*)$", RegexOptions.CultureInvariant);
	private static readonly Regex ExceptionLine = new(@"^[A-Za-z_][\w.]*(?:Error|Exception|Exit|Interrupt|Warning)\b.*$", RegexOptions.CultureInvariant);

	// Counts error events in new log text: each ERROR/CRITICAL line, and each traceback that does not directly
	// follow one (logger.exception writes both). The line kept is the last event's message, or a traceback's
	// final "SomeError: ..." line.
	internal static DeckyLogScan Scan(string text)
	{
		int errors = 0;
		string? line = null;
		bool lastWasError = false, inTraceback = false;
		foreach (string raw in text.Split('\n'))
		{
			string current = raw.TrimEnd('\r');
			if (Traceback.IsMatch(current))
			{
				if (!lastWasError) errors++;
				inTraceback = true;
				lastWasError = false;
				continue;
			}
			if (inTraceback)
			{
				if (current.Length == 0 || char.IsWhiteSpace(current[0])) continue;
				inTraceback = false;
				if (ExceptionLine.IsMatch(current)) { line = current; lastWasError = false; continue; }
			}
			if (ErrorLine.IsMatch(current))
			{
				errors++;
				Match message = ErrorMessage.Match(current);
				string text2 = message.Success ? message.Groups[1].Value : current;
				line = text2.Trim().Length > 0 ? text2 : current;
				lastWasError = true;
				continue;
			}
			if (current.Trim().Length > 0) lastWasError = false;
		}
		return new DeckyLogScan(errors, line is null ? null : Sanitize(line));
	}

	private static readonly (Regex Pattern, string Replacement)[] Masks =
	{
		// JSON Web Tokens and "Bearer <token>".
		(new Regex(@"eyJ[\w-]{4,}\.[\w-]{4,}\.[\w-]*", RegexOptions.CultureInvariant), "***"),
		(new Regex(@"(?i)\b(bearer|basic)\s+[^\s""',;]+", RegexOptions.CultureInvariant), "$1 ***"),
		// user:password@ in a URL.
		(new Regex(@"(?i)(\w+://)[^/\s:@]+:[^/\s@]+@", RegexOptions.CultureInvariant), "$1***@"),
		// Query parameters that carry a secret.
		(new Regex(@"(?i)([?&][^=&\s]*(?:token|key|secret|sig|signature|auth|code|password|session)[^=&\s]*=)[^&\s""']+", RegexOptions.CultureInvariant), "$1***"),
		// name=value / name: value / "name": "value" for secret-looking names.
		(new Regex(@"(?i)(\b[\w-]*(?:token|secret|password|passwd|pwd|api[_-]?key|apikey|access[_-]?key|private[_-]?key|client[_-]?secret|auth|authorization|cookie|session[_-]?id|sessionid|credential)s?[""']?\s*[:=]\s*[""']?)[^\s""',;}&]+", RegexOptions.CultureInvariant), "$1***"),
		// Long opaque strings (hex or base64 with letters and digits): keys, hashes, tokens.
		(new Regex(@"(?<![\w/\\.])(?=[A-Za-z0-9+/_-]*\d)(?=[A-Za-z0-9+/_-]*[A-Za-z])[A-Za-z0-9+/_-]{24,}={0,2}", RegexOptions.CultureInvariant), "***"),
		// The Windows user folder's name.
		(new Regex(@"(?i)\b([A-Z]:[\\/]+Users[\\/]+)[^\\/\s""']+", RegexOptions.CultureInvariant), "$1…"),
		(new Regex(@"(?i)(/home/|/Users/)[^/\s""']+", RegexOptions.CultureInvariant), "$1…"),
	};

	// One line fit for the TV: no control characters, secrets masked, whitespace collapsed, at most MaxLine characters.
	internal static string Sanitize(string line)
	{
		string text = Regex.Replace(line, @"[\u0000-\u0008\u000b-\u001f\u007f]", " ");
		foreach ((Regex pattern, string replacement) in Masks) text = pattern.Replace(text, replacement);
		text = Regex.Replace(text, @"\s+", " ").Trim();
		return text.Length > MaxLine ? text[..(MaxLine - 1)].TrimEnd() + "…" : text;
	}

	internal static DeckyLogState Measured(DeckyLogState state, string path, long offset, DateTimeOffset lastWrite)
	{
		var files = new Dictionary<string, DeckyLogFile>(state.Files, StringComparer.OrdinalIgnoreCase) { [path] = new DeckyLogFile { Offset = offset, LastWrite = lastWrite } };
		return state with { Files = files };
	}

	internal static DeckyLogState Found(DeckyLogState state, string plugin, DeckyLogScan scan, DateTimeOffset now)
	{
		if (scan.Errors <= 0) return state;
		var plugins = new Dictionary<string, DeckyPluginErrors>(state.Plugins, StringComparer.OrdinalIgnoreCase);
		int count = plugins.TryGetValue(plugin, out DeckyPluginErrors? known) && now - known.LastErrorAt < RecentFor ? known.Count : 0;
		plugins[plugin] = new DeckyPluginErrors { LastErrorAt = now, Count = Math.Min(count + scan.Errors, 99999), Line = scan.Line ?? known?.Line };
		return state with { Plugins = plugins };
	}

	// After a scan: remember when, and forget files and plugins that have been quiet for long.
	internal static DeckyLogState Finish(DeckyLogState state, DateTimeOffset now, IReadOnlyCollection<string> present)
	{
		var files = state.Files.Where(pair => present.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
			&& (pair.Value.LastWrite is not { } write || now - write < ForgetAfter)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
		var plugins = state.Plugins.Where(pair => now - pair.Value.LastErrorAt < ForgetAfter).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
		return state with { LastScan = now, Files = files, Plugins = plugins };
	}

	// Plugins that reported errors within RecentFor, newest first.
	internal static IReadOnlyList<string> Failing(DeckyLogState state, DateTimeOffset now) =>
		state.Plugins.Where(pair => now >= pair.Value.LastErrorAt && now - pair.Value.LastErrorAt < RecentFor)
			.OrderByDescending(pair => pair.Value.LastErrorAt).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Select(pair => pair.Key).ToList();

	// "Forge", "Forge, Awesome Mix", "Forge, Awesome Mix, Core +2": the names for a notice. Comma-separated in
	// every language, so no word needs translating.
	internal static string Names(IReadOnlyList<string> plugins)
	{
		var shown = plugins.Take(3).Select(DisplayName).ToList();
		string list = string.Join(", ", shown);
		return plugins.Count > 3 ? list + " +" + (plugins.Count - 3) : list;
	}

	// ---------- Core Health row ----------

	// "Decky": plugins loaded (steel), waiting or not in Gaming Mode (amber), not loaded or a plugin reporting
	// errors (crimson). Errors raise one notice ("Decky: Forge is reporting errors"), at most once a day, again
	// when the set of failing plugins changes.
	internal static HealthRow Row(string verdict, DeckyLogState state, DateTimeOffset now, List<HealthProblem> problems)
	{
		IReadOnlyList<string> failing = Failing(state, now);
		DeckyPluginErrors? newest = failing.Count > 0 ? state.Plugins[failing[0]] : null;
		var data = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["verdict"] = verdict,
			["plugins"] = failing.Select(DisplayName).ToArray(),
			["errors"] = failing.Sum(name => state.Plugins[name].Count),
			["line"] = newest?.Line,
			["at"] = newest?.LastErrorAt,
		};
		if (failing.Count > 0)
			problems.Add(new HealthProblem("decky-errors", "decky-plugins", "decky|" + string.Join("|", failing.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant(),
				failing.Count == 1 ? "health.deckyErrorsOne" : "health.deckyErrorsMany", Names(failing)));
		return verdict switch
		{
			"NotLoaded" => new HealthRow("decky-plugins", HealthLevel.Attention, "not-loaded", data),
			_ when failing.Count > 0 => new HealthRow("decky-plugins", HealthLevel.Attention, "errors", data),
			"Healthy" => new HealthRow("decky-plugins", HealthLevel.Ok, "healthy", data),
			"Waiting" => new HealthRow("decky-plugins", HealthLevel.Info, "waiting", data),
			_ => new HealthRow("decky-plugins", HealthLevel.Info, "not-applicable", data),
		};
	}

	// ---------- state file ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static DeckyLogState Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new();
			DeckyLogState? loaded = JsonSerializer.Deserialize<DeckyLogState>(File.ReadAllText(path), Json);
			if (loaded is null) return new();
			return loaded with
			{
				Files = new(loaded.Files ?? new(), StringComparer.OrdinalIgnoreCase),
				Plugins = new(loaded.Plugins ?? new(), StringComparer.OrdinalIgnoreCase),
			};
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, DeckyLogState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}
