// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Playhub.Shared;

// Rebrand phase 2: the file and folder names the user sees say Core. Each old Playhub name
// keeps working wherever an older component, an existing install or a running process may
// still use it. This file holds only file-system rules (no registry, no processes), so the
// decisions run in Source/CoreNames.Tests on Linux. docs/eternal/REBRAND.md lists every name
// with its migration rule.
internal static class CoreNames
{
    internal const string FolderName = "Core";
    internal const string LegacyFolderName = "Playhub";

    internal const string AppExe = "Core.exe";
    internal const string LegacyAppExe = "Playhub.exe";
    internal const string DeckyHostExe = "Core-DeckyHost.exe";
    internal const string LegacyDeckyHostExe = "Playhub-DeckyHost.exe";
    internal const string StoreHostExe = "Core.StoreHost.exe";
    internal const string LegacyStoreHostExe = "Playhub.StoreHost.exe";
    internal const string UninstallerExe = "unins-core.exe";
    internal const string LegacyUninstallerExe = "unins-playhub.exe";

    // Current name first: lookups prefer it, and fall back to the old one.
    internal static readonly string[] AppExecutables = [AppExe, LegacyAppExe];
    internal static readonly string[] DeckyHostExecutables = [DeckyHostExe, LegacyDeckyHostExe];
    internal static readonly string[] StoreHostExecutables = [StoreHostExe, LegacyStoreHostExe];

    internal static string ProcessName(string executable) => Path.GetFileNameWithoutExtension(executable);
    internal static string[] ProcessNames(IEnumerable<string> executables) => executables.Select(ProcessName).ToArray();

    internal static bool IsAppExecutable(string? path) => HasFileName(path, AppExecutables);
    internal static bool IsDeckyHostExecutable(string? path) => HasFileName(path, DeckyHostExecutables);
    internal static bool IsStoreHostExecutable(string? path) => HasFileName(path, StoreHostExecutables);

    // "Core" is a common word, so a process called Core is ours only when its image is one of
    // the expected names inside the expected folder.
    internal static bool IsOwnedImage(string? image, string directory, IEnumerable<string> executables)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(image) || string.IsNullOrWhiteSpace(directory)) return false;
            string full = Path.GetFullPath(image);
            string folder = Path.GetFullPath(directory).TrimEnd('\\', '/');
            return HasFileName(full, executables)
                && string.Equals(Path.GetDirectoryName(full)?.TrimEnd('\\', '/'), folder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    // Files a 3.4 update leaves behind under their old names; removed once the new ones exist.
    // Paths are relative to the install folder. Only entries whose renamed successor is present
    // are deleted (RetiredFilesToDelete), so a partial payload never removes a working copy.
    internal static readonly (string Retired, string Successor)[] RetiredInstallFiles =
    [
        ("Playhub.exe", AppExe),
        ("Playhub.dll", "Core.dll"),
        ("Playhub.pri", "Core.pri"),
        ("Playhub.deps.json", "Core.deps.json"),
        ("Playhub.runtimeconfig.json", "Core.runtimeconfig.json"),
        (LegacyUninstallerExe, UninstallerExe),
        ("Assets/DeckyRuntime/" + LegacyDeckyHostExe, "Assets/DeckyRuntime/" + DeckyHostExe),
        ("StoreLibrary/" + LegacyStoreHostExe, "StoreLibrary/" + StoreHostExe),
        ("StoreLibrary/Playhub.StoreHost.dll", "StoreLibrary/Core.StoreHost.dll"),
        ("StoreLibrary/Playhub.StoreHost.deps.json", "StoreLibrary/Core.StoreHost.deps.json"),
        ("StoreLibrary/Playhub.StoreHost.runtimeconfig.json", "StoreLibrary/Core.StoreHost.runtimeconfig.json"),
    ];

    internal static IReadOnlyList<string> RetiredFilesToDelete(string installDirectory, Func<string, bool> fileExists)
    {
        var result = new List<string>();
        foreach (var (retired, successor) in RetiredInstallFiles)
        {
            string old = Path.Combine(installDirectory, retired.Replace('/', Path.DirectorySeparatorChar));
            string current = Path.Combine(installDirectory, successor.Replace('/', Path.DirectorySeparatorChar));
            if (fileExists(old) && fileExists(current)) result.Add(old);
        }
        return result;
    }

    // ---------------------------------------------------------------- install folder

    internal static string DefaultInstallDir(string localAppData) => Path.Combine(localAppData, "Programs", FolderName);
    internal static string LegacyDefaultInstallDir(string localAppData) => Path.Combine(localAppData, "Programs", LegacyFolderName);

    // An existing install is updated where it is: Steam keeps the absolute path of
    // Playhub.GameSession.exe in every shortcut Core created for an imported game, and Steam's
    // shortcut identity (artwork, collections) derives from that path, so moving the folder
    // would break the library. Only a new install goes to Programs\Core.
    internal static string ChooseInstallDir(string? registered, string localAppData, Func<string, bool> isInstall)
    {
        if (!string.IsNullOrWhiteSpace(registered) && Path.IsPathFullyQualified(registered))
            return Path.GetFullPath(registered);
        string legacy = LegacyDefaultInstallDir(localAppData);
        return isInstall(legacy) ? legacy : DefaultInstallDir(localAppData);
    }

    // Default folder for lookups when Installed apps has no location: Programs\Core if it
    // exists, else an existing Programs\Playhub, else Programs\Core.
    internal static string ExistingDefaultInstallDir(string localAppData, Func<string, bool> directoryExists)
    {
        string core = DefaultInstallDir(localAppData), legacy = LegacyDefaultInstallDir(localAppData);
        return directoryExists(core) || !directoryExists(legacy) ? core : legacy;
    }

    internal static bool LooksLikeInstall(string directory, Func<string, bool> fileExists) =>
        AppExecutables.Any(name => fileExists(Path.Combine(directory, name)));

    // Install folders to look in from outside the install (agent, plugin, helpers): the folder
    // registered in Installed apps, then Programs\Core, then Programs\Playhub.
    internal static IReadOnlyList<string> InstallDirCandidates(string? registered, string localAppData)
    {
        var result = new List<string>();
        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
            string full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!result.Contains(full, StringComparer.OrdinalIgnoreCase)) result.Add(full);
        }
        Add(registered);
        Add(DefaultInstallDir(localAppData));
        Add(LegacyDefaultInstallDir(localAppData));
        return result;
    }

    internal static string? FindAppExecutable(string? registered, string localAppData, Func<string, bool> fileExists)
    {
        foreach (string directory in InstallDirCandidates(registered, localAppData))
            foreach (string name in AppExecutables)
            {
                string candidate = Path.Combine(directory, name);
                if (fileExists(candidate)) return candidate;
            }
        return null;
    }

    // Folder name for an install location the user picks in setup: a generic folder gets a
    // Core subfolder; a folder already named Core or Playhub is used as it is.
    internal static string InstallDirForChoice(string chosen)
    {
        string trimmed = chosen.TrimEnd('\\', '/');
        string leaf = Path.GetFileName(trimmed);
        return leaf.Equals(FolderName, StringComparison.OrdinalIgnoreCase) || leaf.Equals(LegacyFolderName, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : Path.Combine(trimmed, FolderName);
    }

    // ---------------------------------------------------------------- data folders

    // Written inside %APPDATA%\Core and %LOCALAPPDATA%\Core when the copy from Playhub is
    // complete. Until it exists every component keeps using the Playhub folder, so components
    // of different versions never split data between the two.
    internal const string DataMarkerFile = ".core-migrated.json";
    private const string StagingName = ".Core-migration";

    // Moved instead of copied: Steam artwork and Steam client backups can take gigabytes, and
    // both are found by scanning their folder, never by a stored path. Everything else is
    // copied (caches included: imported games may point at a cached cover by its path), and
    // the old folder keeps it unchanged as the backup.
    internal static readonly string[] MovedDataFolders = ["backups"];

    // Not carried over: scratch that the new components recreate. "downloads" also holds the
    // running setup during an update. The store maintenance lease is per update; a copied one
    // would keep the store paused until it expires.
    internal static readonly string[] SkippedDataEntries =
        ["downloads", "DeckyRuntime", "DiagnosticsRequests", "StoreLibrary/maintenance-v1.json"];

    // A failed copy below these folders is logged, not fatal (a log file held open).
    internal static readonly string[] BestEffortDataFolders = ["logs", "diagnostics"];

    internal static string RoamingDataRoot() =>
        DataRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal static string LocalDataRoot() =>
        DataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string DataRoot(string parent)
    {
        string current = Path.Combine(parent, FolderName);
        string legacy = Path.Combine(parent, LegacyFolderName);
        return File.Exists(Path.Combine(current, DataMarkerFile)) || !Directory.Exists(legacy) ? current : legacy;
    }

    internal sealed record DataMigrationResult(string Root, bool Migrated, string? Problem);

    internal static DataMigrationResult MigrateDataRoots(Action<string>? log = null)
    {
        var roaming = MigrateDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), log);
        var local = MigrateDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), log);
        return new(local.Root, roaming.Migrated || local.Migrated, roaming.Problem ?? local.Problem);
    }

    // One-time copy of <parent>\Playhub to <parent>\Core. The copy is built in a staging folder
    // that already holds the marker, then renamed, so <parent>\Core either does not exist or is
    // complete. A <parent>\Core that exists without the marker belongs to someone else: it is
    // left alone and Playhub stays in use. Safe to call on every start.
    internal static DataMigrationResult MigrateDataRoot(string parent, Action<string>? log = null)
    {
        string current = Path.Combine(parent, FolderName);
        string legacy = Path.Combine(parent, LegacyFolderName);
        string staging = Path.Combine(parent, StagingName);
        try
        {
            if (File.Exists(Path.Combine(current, DataMarkerFile))) return new(current, false, null);
            if (!Directory.Exists(legacy)) return new(current, false, null);
            if (IsLink(legacy)) return Refuse(legacy, "legacy_is_link", log);
            if (Directory.Exists(current) || File.Exists(current)) return Refuse(legacy, "current_exists", log);
            if (File.Exists(staging) || Directory.Exists(staging) && IsLink(staging)) return Refuse(legacy, "staging_invalid", log);

            Directory.CreateDirectory(staging);
            var failures = new List<string>();
            CopyTree(legacy, staging, "", failures, log);
            if (failures.Count > 0) return Refuse(legacy, "copy_failed: " + failures[0], log);

            var moved = new List<string>();
            foreach (string name in MovedDataFolders)
            {
                string source = Path.Combine(legacy, name);
                if (!Directory.Exists(source) || IsLink(source)) continue;
                MoveTree(source, Path.Combine(staging, name), log);
                moved.Add(name);
            }

            // Written by hand: the trimmed Decky host links this file and cannot use reflection JSON.
            File.WriteAllText(Path.Combine(staging, DataMarkerFile), "{\n"
                + "  \"schema\": 1,\n"
                + "  \"from\": " + JsonString(legacy) + ",\n"
                + "  \"migratedAt\": " + JsonString(DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)) + ",\n"
                + "  \"moved\": [" + string.Join(", ", moved.Select(JsonString)) + "],\n"
                + "  \"note\": \"Copied from the Playhub folder, which is kept unchanged as a backup. The moved folders are no longer in it.\"\n"
                + "}\n");
            Directory.Move(staging, current);
            log?.Invoke($"Data folder migrated: {legacy} -> {current} (moved: {string.Join(", ", moved)}).");
            return new(current, true, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Refuse(legacy, error.GetType().Name + ": " + error.Message, log);
        }
    }

    private static DataMigrationResult Refuse(string legacy, string problem, Action<string>? log)
    {
        log?.Invoke($"Data folder migration skipped for {legacy}: {problem}. Playhub stays in use.");
        return new(legacy, false, problem);
    }

    private static bool Matches(string relative, IEnumerable<string> entries) =>
        entries.Any(entry => relative.Equals(entry, StringComparison.OrdinalIgnoreCase));

    private static void CopyTree(string source, string destination, string relative, List<string> failures, Action<string>? log)
    {
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            string name = Path.GetFileName(entry);
            string childRelative = relative.Length == 0 ? name : relative + "/" + name;
            if (Matches(childRelative, SkippedDataEntries) || relative.Length == 0 && Matches(name, MovedDataFolders)) continue;
            if (IsLink(entry)) { log?.Invoke("Data migration: link not followed: " + childRelative); continue; }
            string target = Path.Combine(destination, name);
            if (Directory.Exists(entry)) { CopyTree(entry, target, childRelative, failures, log); continue; }
            try { File.Copy(entry, target, overwrite: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                string top = childRelative.Split('/')[0];
                if (Matches(top, BestEffortDataFolders)) log?.Invoke($"Data migration: {childRelative} not copied ({error.Message}).");
                else failures.Add(childRelative + " (" + error.Message + ")");
            }
        }
    }

    // Directory.Move when possible (same volume, nothing open); otherwise file by file, leaving
    // anything in use where it is.
    private static void MoveTree(string source, string destination, Action<string>? log)
    {
        if (!Directory.Exists(destination))
        {
            try { Directory.Move(source, destination); return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { log?.Invoke($"Data migration: {source} moved file by file ({error.Message})."); }
        }
        Directory.CreateDirectory(destination);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            if (IsLink(entry)) continue;
            string target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) { MoveTree(entry, target, log); continue; }
            try { if (!File.Exists(target)) File.Move(entry, target); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { log?.Invoke($"Data migration: {entry} left in place ({error.Message})."); }
        }
        try { if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string JsonString(string value)
    {
        var text = new StringBuilder("\"");
        foreach (char c in value)
            text.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' => "\\u" + ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        return text.Append('"').ToString();
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool HasFileName(string? path, IEnumerable<string> names)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string name = Path.GetFileName(path.Trim().Trim('"').Replace('/', '\\').Split('\\').Last());
        return names.Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
