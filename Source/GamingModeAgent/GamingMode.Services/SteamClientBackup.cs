using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace GamingMode.Services;

// Eternal Core Mark III: the agent's copy of the app's Steam client backup
// (Playhub/Services/ExtraService.cs, "Backup della versione di Steam").
//
// Same folder (%LOCALAPPDATA%\Core\backups\SteamClient, Playhub before the data move), same layout
// (PlayhubSteamClient-<stamp>\...) and same marker file, so the app's "Restore Steam
// version" lists backups made here and the agent can roll back to one made there. Same
// exclusions: only the program, never userdata, games, login config or caches, and never
// steam.cfg (the update hold is managed on its own). Keep the lists in step with
// ExtraService; MarkIII.Tests checks they still match.
internal static class SteamClientBackup
{
	internal const string Marker = "playhub-steam-client.json";
	internal const string FolderPrefix = "PlayhubSteamClient-";
	internal const string CreatedByCore = "core-steam-update";

	internal static readonly string[] ExcludedFolders =
	{
		"userdata", "steamapps", "config", "steam",
		"appcache", "depotcache", "logs", "dumps", "music", "workshop", "temp", "backups"
	};
	internal static readonly string[] ExcludedExtensions = { ".old", ".crash", ".log", ".dmp" };
	internal static readonly string[] ExcludedRootFiles = { "steam.cfg" };

	internal readonly record struct Result(bool Ok, string? Folder, int Files, int Skipped, string? Error);

	internal static bool LooksLikeBackup(string folder)
	{
		try { return Directory.Exists(folder) && File.Exists(Path.Combine(folder, "steam.exe")) && File.Exists(Path.Combine(folder, Marker)); }
		catch { return false; }
	}

	internal static string? Newest(string backupsRoot)
	{
		try
		{
			return Directory.Exists(backupsRoot)
				? Directory.GetDirectories(backupsRoot).Where(LooksLikeBackup).OrderByDescending(Directory.GetCreationTimeUtc)
					.ThenByDescending(path => Path.GetFileName(path), StringComparer.Ordinal).FirstOrDefault()
				: null;
		}
		catch { return null; }
	}

	internal static IEnumerable<string> ClientFiles(string steamRoot)
	{
		string[] entries;
		try { entries = Directory.GetFileSystemEntries(steamRoot); }
		catch { yield break; }
		foreach (string entry in entries)
		{
			string name = Path.GetFileName(entry);
			if (Directory.Exists(entry))
			{
				if (ExcludedFolders.Contains(name, StringComparer.OrdinalIgnoreCase) || IsLink(entry)) continue;
				foreach (string file in Walk(entry)) yield return file;
			}
			else if (!ExcludedRootFiles.Contains(name, StringComparer.OrdinalIgnoreCase) && !Excluded(entry) && !IsLink(entry))
				yield return entry;
		}
	}

	internal static Result Create(string steamRoot, string backupsRoot, string? version, DateTimeOffset now, CancellationToken cancellation)
	{
		if (!File.Exists(Path.Combine(steamRoot, "steam.exe"))) return new(false, null, 0, 0, "steam.exe not found");
		List<string> files = ClientFiles(steamRoot).ToList();
		long total = files.Sum(Length);
		if (!HasRoomFor(backupsRoot, total)) return new(false, null, 0, 0, "not enough disk space");
		string folder = Path.Combine(backupsRoot, FolderPrefix + now.LocalDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
		if (Directory.Exists(folder)) folder += "-" + Guid.NewGuid().ToString("N")[..6];
		int copied = 0, skipped = 0;
		try
		{
			Directory.CreateDirectory(folder);
			foreach (string file in files)
			{
				cancellation.ThrowIfCancellationRequested();
				try
				{
					string target = Path.Combine(folder, Path.GetRelativePath(steamRoot, file));
					Directory.CreateDirectory(Path.GetDirectoryName(target)!);
					File.Copy(file, target, overwrite: true);
					copied++;
				}
				catch (Exception error) when (error is IOException or UnauthorizedAccessException && !IsDiskFull(error)) { skipped++; }
			}
			// steam.exe is what makes a backup usable; without it there is nothing to restore.
			if (copied == 0 || !File.Exists(Path.Combine(folder, "steam.exe"))) { TryDelete(folder); return new(false, null, copied, skipped, "steam.exe could not be copied"); }
			File.WriteAllText(Path.Combine(folder, Marker), JsonSerializer.Serialize(new
			{
				kind = "steam-client",
				version = version ?? "",
				createdAt = now.ToString("o"),
				createdBy = CreatedByCore,
				source = steamRoot,
				files = files.Count,
				bytes = total
			}));
			return new(true, folder, copied, skipped, null);
		}
		catch (OperationCanceledException) { TryDelete(folder); throw; }
		catch (Exception error) { TryDelete(folder); return new(false, null, copied, skipped, error.Message); }
	}

	// Overwrites the client files from the backup. Files the backup does not have are left
	// alone: deleting extra files is the quickest way to make an install unrecoverable.
	internal static Result Restore(string backupFolder, string steamRoot, CancellationToken cancellation)
	{
		if (!LooksLikeBackup(backupFolder)) return new(false, backupFolder, 0, 0, "not a Steam client backup");
		int copied = 0;
		try
		{
			foreach (string file in Directory.EnumerateFiles(backupFolder, "*", SearchOption.AllDirectories))
			{
				cancellation.ThrowIfCancellationRequested();
				if (string.Equals(Path.GetFileName(file), Marker, StringComparison.OrdinalIgnoreCase)) continue;
				string target = Path.Combine(steamRoot, Path.GetRelativePath(backupFolder, file));
				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				File.Copy(file, target, overwrite: true);
				copied++;
			}
			return new(true, backupFolder, copied, 0, null);
		}
		catch (OperationCanceledException) { throw; }
		catch (Exception error) { return new(false, backupFolder, copied, 0, error.Message); }
	}

	// Keeps the newest `keep` backups this agent made; the user's own backups (made from
	// the app) are never deleted.
	internal static int Prune(string backupsRoot, int keep)
	{
		int removed = 0;
		try
		{
			if (!Directory.Exists(backupsRoot)) return 0;
			foreach (string folder in Directory.GetDirectories(backupsRoot).Where(LooksLikeBackup).Where(MadeByCore)
				.OrderByDescending(Directory.GetCreationTimeUtc).ThenByDescending(path => Path.GetFileName(path), StringComparer.Ordinal).Skip(Math.Max(1, keep)))
			{
				TryDelete(folder);
				if (!Directory.Exists(folder)) removed++;
			}
		}
		catch { }
		return removed;
	}

	internal static bool MadeByCore(string folder)
	{
		try
		{
			using JsonDocument marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, Marker)));
			return marker.RootElement.TryGetProperty("createdBy", out JsonElement by) && by.ValueKind == JsonValueKind.String
				&& by.GetString() == CreatedByCore;
		}
		catch { return false; }
	}

	private static IEnumerable<string> Walk(string root)
	{
		var pending = new Stack<string>();
		pending.Push(root);
		while (pending.Count > 0)
		{
			string folder = pending.Pop();
			string[] directories, files;
			try { directories = Directory.GetDirectories(folder); files = Directory.GetFiles(folder); }
			catch { continue; }
			foreach (string directory in directories) if (!IsLink(directory)) pending.Push(directory);
			foreach (string file in files) if (!IsLink(file) && !Excluded(file)) yield return file;
		}
	}

	private static bool Excluded(string file) => ExcludedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

	private static bool IsLink(string path)
	{
		try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
		catch { return true; }
	}

	private static long Length(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }

	private static bool HasRoomFor(string path, long bytes)
	{
		try
		{
			Directory.CreateDirectory(path);
			string? root = Path.GetPathRoot(Path.GetFullPath(path));
			return root is null || new DriveInfo(root).AvailableFreeSpace > bytes + 256L * 1024 * 1024;
		}
		catch { return true; }
	}

	private static bool IsDiskFull(Exception error) => error is IOException && (error.HResult & 0xFFFF) is 0x27 or 0x70;

	private static void TryDelete(string path) { try { Directory.Delete(path, recursive: true); } catch { } }
}
