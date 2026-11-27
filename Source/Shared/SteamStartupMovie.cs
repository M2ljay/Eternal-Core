using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Playhub.Shared;

// Eternal Core: "Skip Steam's startup animation". Big Picture plays a startup movie each time
// it opens (Settings > Customization > Startup Movie). Steam has no documented switch or
// launch option to turn it off, so Core selects its own movie instead: 0.3 s of pure black
// (GamingModeAgent/Assets/core-skip-startup.webm, VP8/Opus, 1280x720), which leaves Core's
// boot sequence or transition handing straight to Big Picture.
//
// Where Steam keeps the choice (community-verified, see docs/eternal/MARK-III-GUIDE.md, "Steam's startup animation"):
// <Steam>/config/config.vdf, InstallConfigStore > Customization > StartupMovie, with
// "MovieID" ("0" for a local file or the default; a Points Shop item number otherwise),
// "LocalPath" (relative to <Steam>/config, "" for the default) and "Shuffle". Older clients
// read InstallConfigStore > Software > Valve > Steam > StartupMovie; Core updates that one
// too when it exists, and never creates it.
//
// Reversible: the selection Core replaced is recorded, and put back when the option is
// turned off or Core is uninstalled, unless the user has picked another movie in Steam
// since (then Core leaves their pick alone). Steam rewrites config.vdf from memory, so Core
// only touches it while Steam is not running. Edits are made in place on the text, keeping
// every other byte, and written atomically.
internal sealed record StartupMovieSection(bool Present, string? MovieId, string? LocalPath, string? Shuffle);

internal sealed record StartupMovieRecord(StartupMovieSection Customization, bool CustomizationParentPresent, StartupMovieSection Legacy);

internal enum StartupMovieOutcome
{
	Unchanged, Selected, Kept, Reselected, Restored, ResetToDefault, LeftUsersPick, NothingToRestore,
	SteamRunning, NoSteamConfig, Unreadable, Failed
}

internal readonly record struct StartupMovieSync(string Text, StartupMovieRecord? Record, StartupMovieOutcome Outcome);

internal static class SteamStartupMovie
{
	internal const string FileName = "core_skip_startup.webm";
	internal const string CoreLocalPath = "/uioverrides/movies/" + FileName;
	internal const string RecordFileName = "steam-startup-movie.json";

	private static readonly string[] CustomizationPath = { "InstallConfigStore", "Customization", "StartupMovie" };
	private static readonly string[] LegacyPath = { "InstallConfigStore", "Software", "Valve", "Steam", "StartupMovie" };
	private static readonly string[] Keys = { "MovieID", "LocalPath", "Shuffle" };

	// ---------- decisions (pure) ----------

	// skip: the setting. record: what Core replaced, if it replaced anything. Returns the new
	// config text, the record to keep (null = delete it) and what happened.
	internal static StartupMovieSync Sync(string text, StartupMovieRecord? record, bool skip)
	{
		var tree = Vdf.Parse(text);
		if (tree is null || Vdf.Find(tree, "InstallConfigStore") is null) return new(text, record, StartupMovieOutcome.Unreadable);
		bool ours = IsCoreSelected(text);
		if (skip)
		{
			if (ours) return new(text, record, StartupMovieOutcome.Kept);
			// Either the first time, or the user picked another movie in Steam since: remember
			// what is selected now, so turning the option off brings back their latest pick.
			var previous = Read(text);
			string? selected = Select(text);
			return selected is null ? new(text, record, StartupMovieOutcome.Failed)
				: new(selected, previous, record is null ? StartupMovieOutcome.Selected : StartupMovieOutcome.Reselected);
		}
		if (!ours) return new(text, null, record is null ? StartupMovieOutcome.NothingToRestore : StartupMovieOutcome.LeftUsersPick);
		string? restored = Restore(text, record);
		return restored is null ? new(text, record, StartupMovieOutcome.Failed)
			: new(restored, null, record is null ? StartupMovieOutcome.ResetToDefault : StartupMovieOutcome.Restored);
	}

	internal static bool IsCoreSelected(string text)
	{
		var tree = Vdf.Parse(text);
		if (tree is null) return false;
		var movie = Vdf.FindPath(tree, CustomizationPath) ?? Vdf.FindPath(tree, LegacyPath);
		return movie is not null && string.Equals(Vdf.Scalar(movie, "LocalPath"), CoreLocalPath, StringComparison.OrdinalIgnoreCase);
	}

	internal static StartupMovieRecord Read(string text)
	{
		var tree = Vdf.Parse(text) ?? new List<Vdf.Node>();
		static StartupMovieSection Section(Vdf.Node? node) => node is null
			? new(false, null, null, null)
			: new(true, Vdf.Scalar(node, "MovieID"), Vdf.Scalar(node, "LocalPath"), Vdf.Scalar(node, "Shuffle"));
		return new(Section(Vdf.FindPath(tree, CustomizationPath)), Vdf.FindPath(tree, CustomizationPath[..2]) is not null, Section(Vdf.FindPath(tree, LegacyPath)));
	}

	// Core's movie in the current section (created if missing) and in the older one if present.
	// Shuffle would ignore the selection, so it is turned off while Core's movie is selected.
	internal static string? Select(string text)
	{
		string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		var tree = Vdf.Parse(text);
		var root = tree is null ? null : Vdf.Find(tree, "InstallConfigStore");
		if (root is null) return null;
		if (Vdf.Find(root.Children, "Customization") is null)
			text = Vdf.InsertObject(text, root, "Customization", newline);
		root = Vdf.FindPath(Vdf.Parse(text)!, CustomizationPath[..1])!;
		var customization = Vdf.Find(root.Children, "Customization")!;
		if (Vdf.Find(customization.Children, "StartupMovie") is null)
			text = Vdf.InsertObject(text, customization, "StartupMovie", newline);
		foreach (var path in new[] { CustomizationPath, LegacyPath })
		{
			if (Vdf.FindPath(Vdf.Parse(text)!, path) is null) continue;
			text = Vdf.SetScalar(text, path, "MovieID", "0", newline);
			text = Vdf.SetScalar(text, path, "LocalPath", CoreLocalPath, newline);
			if (Vdf.Scalar(Vdf.FindPath(Vdf.Parse(text)!, path)!, "Shuffle") is not null)
				text = Vdf.SetScalar(text, path, "Shuffle", "0", newline);
		}
		return text;
	}

	// Puts back what Core replaced. Without a record (lost), Steam's default: MovieID "0", LocalPath "".
	internal static string? Restore(string text, StartupMovieRecord? record)
	{
		string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		if (Vdf.Parse(text) is null) return null;
		foreach (var (path, before) in new[] { (CustomizationPath, record?.Customization), (LegacyPath, record?.Legacy) })
		{
			var node = Vdf.FindPath(Vdf.Parse(text)!, path);
			if (node is null || !string.Equals(Vdf.Scalar(node, "LocalPath"), CoreLocalPath, StringComparison.OrdinalIgnoreCase)) continue;
			if (before is null)
			{
				text = Vdf.SetScalar(text, path, "MovieID", "0", newline);
				text = Vdf.SetScalar(text, path, "LocalPath", "", newline);
				continue;
			}
			if (!before.Present)
			{
				text = Vdf.RemoveNode(text, Vdf.FindPath(Vdf.Parse(text)!, path)!);
				if (path == CustomizationPath && record is { CustomizationParentPresent: false }
					&& Vdf.FindPath(Vdf.Parse(text)!, CustomizationPath[..2]) is { Children.Count: 0 } parent)
					text = Vdf.RemoveNode(text, parent);
				continue;
			}
			string?[] values = { before.MovieId, before.LocalPath, before.Shuffle };
			for (int i = 0; i < Keys.Length; i++)
				text = values[i] is { } value ? Vdf.SetScalar(text, path, Keys[i], value, newline) : Vdf.RemoveScalar(text, path, Keys[i]);
		}
		return text;
	}

	// ---------- files ----------

	internal static string ConfigPath(string steamRoot) => Path.Combine(steamRoot, "config", "config.vdf");
	internal static string MoviePath(string steamRoot) => Path.Combine(steamRoot, "config", "uioverrides", "movies", FileName);

	// Applies the setting to an installed Steam. openMovie gives the embedded black movie (only
	// needed when skip is on). Never throws.
	internal static StartupMovieOutcome Apply(string steamRoot, string recordPath, bool skip, bool steamRunning, Func<Stream?>? openMovie)
	{
		try
		{
			if (steamRunning) return StartupMovieOutcome.SteamRunning;
			string config = ConfigPath(steamRoot);
			if (!File.Exists(config)) return StartupMovieOutcome.NoSteamConfig;
			if (skip && !InstallMovie(steamRoot, openMovie)) return StartupMovieOutcome.Failed;
			string text = File.ReadAllText(config);
			var result = Sync(text, LoadRecord(recordPath), skip);
			if (result.Outcome is StartupMovieOutcome.Unreadable or StartupMovieOutcome.Failed) return result.Outcome;
			if (!string.Equals(result.Text, text, StringComparison.Ordinal)) WriteAtomically(config, result.Text);
			SaveRecord(recordPath, result.Record);
			if (!skip && !IsCoreSelected(result.Text)) TryDelete(MoviePath(steamRoot));
			return result.Outcome;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
		{
			return StartupMovieOutcome.Failed;
		}
	}

	private static bool InstallMovie(string steamRoot, Func<Stream?>? openMovie)
	{
		using var source = openMovie?.Invoke();
		if (source is null) return false;
		using var buffer = new MemoryStream();
		source.CopyTo(buffer);
		byte[] bytes = buffer.ToArray();
		string path = MoviePath(steamRoot);
		if (File.Exists(path) && new FileInfo(path).Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return true;
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		string staged = path + ".core-staged";
		File.WriteAllBytes(staged, bytes);
		File.Move(staged, path, overwrite: true);
		return true;
	}

	private static void WriteAtomically(string path, string text)
	{
		string staged = path + ".core-staged";
		File.WriteAllText(staged, text, new UTF8Encoding(false));
		File.Move(staged, path, overwrite: true);
	}

	private static void TryDelete(string path)
	{
		try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
	}

	internal static StartupMovieRecord? LoadRecord(string path)
	{
		if (!File.Exists(path)) return null;
		try { return JsonSerializer.Deserialize<StartupMovieRecord>(File.ReadAllText(path)); }
		catch (JsonException) { return null; }
	}

	private static void SaveRecord(string path, StartupMovieRecord? record)
	{
		if (record is null) { TryDelete(path); return; }
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		WriteAtomically(path, JsonSerializer.Serialize(record));
	}

	// ---------- a small Valve KeyValues (VDF) reader and in-place editor ----------

	internal static class Vdf
	{
		internal sealed class Node
		{
			internal string Key = "";
			internal int KeyStart;
			internal bool IsObject;
			internal int ValueStart, ValueEnd; // scalar: the text between the quotes
			internal int Open, Close;          // object: the braces
			internal string? Value;
			internal readonly List<Node> Children = new();
		}

		// Null when the text is not well-formed: then Core leaves the file alone.
		internal static List<Node>? Parse(string text)
		{
			var top = new List<Node>();
			var stack = new Stack<Node>();
			int i = 0;
			while (true)
			{
				var key = Next(text, ref i);
				if (key is null) return stack.Count == 0 ? top : null;
				if (key.Value.Kind == '}')
				{
					if (stack.Count == 0) return null;
					stack.Pop().Close = key.Value.Start;
					continue;
				}
				if (key.Value.Kind != '"') return null;
				var value = Next(text, ref i);
				if (value is null) return null;
				var node = new Node { Key = key.Value.Text, KeyStart = key.Value.Start - 1 };
				if (value.Value.Kind == '{') { node.IsObject = true; node.Open = value.Value.Start; }
				else if (value.Value.Kind == '"') { node.ValueStart = value.Value.Start; node.ValueEnd = value.Value.End; node.Value = value.Value.Text; }
				else return null;
				(stack.Count == 0 ? top : stack.Peek().Children).Add(node);
				if (node.IsObject) stack.Push(node);
			}
		}

		private readonly record struct Token(char Kind, int Start, int End, string Text);

		private static Token? Next(string text, ref int i)
		{
			while (i < text.Length)
			{
				char c = text[i];
				if (char.IsWhiteSpace(c)) { i++; continue; }
				if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
				if (c is '{' or '}') { i++; return new Token(c, i - 1, i, c.ToString()); }
				if (c == '[') { while (i < text.Length && text[i] != ']' && text[i] != '\n') i++; i++; continue; } // conditionals: ignored
				if (c == '"')
				{
					int start = ++i;
					while (i < text.Length && text[i] != '"') i += text[i] == '\\' ? 2 : 1;
					if (i >= text.Length) return null;
					int end = i++;
					return new Token('"', start, end, text[start..end]);
				}
				return new Token('?', i, i, "");
			}
			return null;
		}

		internal static Node? Find(IEnumerable<Node> nodes, string key)
		{
			foreach (var node in nodes) if (string.Equals(node.Key, key, StringComparison.OrdinalIgnoreCase)) return node;
			return null;
		}

		internal static Node? FindPath(List<Node> top, string[] path)
		{
			IEnumerable<Node> level = top;
			Node? node = null;
			foreach (string key in path)
			{
				node = Find(level, key);
				if (node is not { IsObject: true }) return null;
				level = node.Children;
			}
			return node;
		}

		internal static string? Scalar(Node node, string key)
		{
			foreach (var child in node.Children)
				if (!child.IsObject && string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase)) return child.Value;
			return null;
		}

		private static int LineStart(string text, int index) => text.LastIndexOf('\n', Math.Max(0, index - 1)) is var n && n >= 0 && n < index ? n + 1 : 0;

		private static string Indent(string text, int index)
		{
			int start = LineStart(text, index);
			int end = start;
			while (end < text.Length && text[end] is ' ' or '\t') end++;
			return text[start..end];
		}

		// Sets key in the object at path (which must exist), adding it as the last line if absent.
		internal static string SetScalar(string text, string[] path, string key, string value, string newline)
		{
			var node = FindPath(Parse(text)!, path)!;
			foreach (var child in node.Children)
				if (!child.IsObject && string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
					return text[..child.ValueStart] + value + text[child.ValueEnd..];
			int at = LineStart(text, node.Close);
			string indent = Indent(text, node.Close) + "\t";
			return text[..at] + indent + "\"" + key + "\"\t\t\"" + value + "\"" + newline + text[at..];
		}

		internal static string RemoveScalar(string text, string[] path, string key)
		{
			var node = FindPath(Parse(text)!, path)!;
			foreach (var child in node.Children)
				if (!child.IsObject && string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase))
					return RemoveLines(text, child.KeyStart, child.ValueEnd + 1);
			return text;
		}

		internal static string InsertObject(string text, Node parent, string key, string newline)
		{
			int at = LineStart(text, parent.Close);
			string indent = Indent(text, parent.Close) + "\t";
			return text[..at] + indent + "\"" + key + "\"" + newline + indent + "{" + newline + indent + "}" + newline + text[at..];
		}

		internal static string RemoveNode(string text, Node node) => RemoveLines(text, node.KeyStart, node.IsObject ? node.Close + 1 : node.ValueEnd + 1);

		// Removes [start, end) together with the whitespace of its lines when nothing else is on them.
		private static string RemoveLines(string text, int start, int end)
		{
			int from = LineStart(text, start);
			if (text[from..start].Trim().Length != 0) from = start;
			int to = end;
			while (to < text.Length && text[to] is ' ' or '\t') to++;
			if (to < text.Length && text[to] == '\r') to++;
			if (to < text.Length && text[to] == '\n') to++;
			else to = end;
			return text[..from] + text[to..];
		}
	}
}
