using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core: per-device game settings while streaming (docs/eternal/CORE-STREAM-MODE.md, "Per-device game
// settings"). Apollo had a Profile Manager that saved and restored chosen files per game per client; Vibepollo
// does not carry it (no such feature in its source or docs, checked 2026-11-27), so Core does the same,
// tightly: for each game you list, the files you list are snapshotted for the device that was playing and
// restored for the device that starts, when Stream mode switches device (the TV counts as a device). Never
// while that game runs (the switch waits until it exits), every overwritten file is backed up first, only
// regular files of at most 8 MB inside your own user folders. Pure: settings and observations in, a plan out.

internal sealed record StreamProfileGame
{
	public string Name { get; init; } = "";
	// The game's executable name ("Cyberpunk2077.exe"), used only to know whether it runs.
	public string Process { get; init; } = "";
	public List<string> Files { get; init; } = new();
}

internal sealed record StreamProfileSettings
{
	public bool Enabled { get; init; } = true;
	public List<StreamProfileGame> Games { get; init; } = new();
}

internal sealed record StreamProfileState
{
	// The device whose settings the files hold now ("tv" when nobody streams).
	public string? Current { get; init; }
	// A switch that waits for a game to exit.
	public string? PendingTo { get; init; }
	public DateTimeOffset? LastSwitchAt { get; init; }
	public string? LastResult { get; init; }
}

internal sealed record StreamProfileAction(string Game, string File, string Kind, string Client);

internal static class StreamProfilePolicy
{
	internal const long MaxFileBytes = 8L * 1024 * 1024;
	internal const string Tv = "tv";

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static T Load<T>(string path) where T : new()
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T() : new T(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new T(); }
	}

	internal static void Save<T>(string path, T value)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(value, Json));
		File.Move(temporary, path, true);
	}

	// A device's folder name: the stream's device name (Apollo's/Vibepollo's client name, Steam Remote Play's
	// device) cleaned to letters, digits, dash and underscore; "tv" without a stream.
	internal static string ClientKey(bool streaming, string? device, string? source)
	{
		if (!streaming) return Tv;
		string raw = string.IsNullOrWhiteSpace(device) ? source ?? "stream" : device;
		string key = Regex.Replace(raw.Trim().ToLowerInvariant(), "[^a-z0-9_-]+", "-").Trim('-');
		if (key.Length == 0) key = "stream";
		return key.Length > 40 ? key[..40] : key;
	}

	// Only files inside the user's own folders: a listed path that leaves them, or names a folder, is ignored.
	internal static bool Allowed(string path, IReadOnlyList<string> userRoots)
	{
		if (string.IsNullOrWhiteSpace(path) || path.Contains("..", StringComparison.Ordinal)) return false;
		string full = path.Replace('/', '\\');
		return userRoots.Any(root => root.Length > 3 && full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
	}

	// What to do when the device changes from `from` to `to`: snapshot each listed file for `from`, then restore
	// `to`'s copy when it has one (a device seen for the first time keeps the files as they are). Nothing when
	// the device did not change. A running game postpones the whole switch.
	internal static (IReadOnlyList<StreamProfileAction> Actions, string? Waiting) Plan(StreamProfileSettings settings, string? from, string to,
		Func<string, bool> gameRunning, Func<string, string, string, bool> hasSnapshot, IReadOnlyList<string> userRoots)
	{
		if (!settings.Enabled || from is null || from == to) return (Array.Empty<StreamProfileAction>(), null);
		var actions = new List<StreamProfileAction>();
		foreach (StreamProfileGame game in settings.Games.Where(game => game.Name.Length > 0))
		{
			if (!string.IsNullOrWhiteSpace(game.Process) && gameRunning(game.Process)) return (Array.Empty<StreamProfileAction>(), game.Name);
			foreach (string file in game.Files.Where(file => Allowed(file, userRoots)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				actions.Add(new StreamProfileAction(game.Name, file, "snapshot", from));
				if (hasSnapshot(game.Name, to, file)) actions.Add(new StreamProfileAction(game.Name, file, "restore", to));
			}
		}
		return (actions, null);
	}

	// Where a game's copy of one file lives for one device: a stable name from the file path.
	internal static string SnapshotName(string file) =>
		Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.ToLowerInvariant())))[..16] + "-" + Regex.Replace(Path.GetFileName(file), "[^A-Za-z0-9._-]", "_");

	internal static string GameFolder(string game) => Regex.Replace(game.Trim(), "[^A-Za-z0-9 _-]", "_").Trim();
}

// ---------- back paddles: L4 is A, R4 is B ----------

internal static class BackPaddlePolicy
{
	// Steam Input layouts (VDF): the back grips are "button_back_left" (L4), "button_back_right" (R4),
	// "button_back_left_upper" (L5) and "button_back_right_upper" (R5) inside a "group" whose bindings read
	// "xinput_button A". Returns the first binding found for a source, or null.
	internal static string? Binding(string vdf, string input)
	{
		string text = vdf.Replace("\r\n", "\n", StringComparison.Ordinal);
		int at = text.IndexOf("\"" + input + "\"", StringComparison.Ordinal);
		if (at < 0) return null;
		// The source's block runs until the next input source ("button_...", "left_trackpad"...) or the end.
		int next = Regex.Match(text[(at + input.Length + 2)..], "\"(button_|left_|right_|dpad|joystick|trigger_|gyro)", RegexOptions.CultureInvariant) is { Success: true } following ? at + input.Length + 2 + following.Index : text.Length;
		Match binding = Regex.Match(text[at..next], "\"binding\"\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);
		return binding.Success ? binding.Groups[1].Value.Trim() : null;
	}

	internal static bool IsButton(string? binding, string button) =>
		binding is not null && Regex.IsMatch(binding, @"^(xinput_button|key_press)?\s*" + button + @"(\s*,.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
		&& binding.StartsWith("xinput_button", StringComparison.OrdinalIgnoreCase);

	// One layout's verdict: "a-b" (L4 = A and R4 = B), "unbound" or "other".
	internal static string Verdict(string vdf)
	{
		string? l4 = Binding(vdf, "button_back_left"), r4 = Binding(vdf, "button_back_right");
		if (IsButton(l4, "A") && IsButton(r4, "B")) return "a-b";
		return l4 is null && r4 is null ? "unbound" : "other";
	}

	// The Health row: the Big Picture and Desktop layouts Core found, and whether each maps L4 to A and R4 to B.
	internal static HealthRow Row(IReadOnlyList<(string Layout, string Verdict)> layouts)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["layouts"] = layouts.Select(layout => new Dictionary<string, object?> { ["layout"] = layout.Layout, ["verdict"] = layout.Verdict }).ToArray(),
			["actions"] = new[] { "paddles-setup" },
		};
		if (layouts.Count == 0) return new HealthRow("back-paddles", HealthLevel.Unknown, "unknown", data);
		if (layouts.All(layout => layout.Verdict == "a-b")) return new HealthRow("back-paddles", HealthLevel.Ok, "in-place", data);
		data["fix"] = "paddles-setup";
		return new HealthRow("back-paddles", HealthLevel.Info, "not-set", data);
	}
}
