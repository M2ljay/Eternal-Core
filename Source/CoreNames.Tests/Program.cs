// Rebrand phase 2: the pure rules behind Core's renamed files and folders.
using Playhub.Shared;

var failures = 0;
void Test(string name, Action body)
{
    try { body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }

string NewRoot()
{
    string root = Path.Combine(Path.GetTempPath(), "core-names-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
}
void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

Test("data root: a fresh profile uses Core", () =>
{
    string parent = NewRoot();
    Require(CoreNames.DataRoot(parent) == Path.Combine(parent, "Core"), "fresh profile should use Core");
    var result = CoreNames.MigrateDataRoot(parent);
    Require(!result.Migrated && result.Problem is null && result.Root == Path.Combine(parent, "Core"), "nothing to migrate");
});

Test("data root: Playhub stays in use until the marker exists", () =>
{
    string parent = NewRoot();
    Write(Path.Combine(parent, "Playhub", "settings.json"), "{}");
    Require(CoreNames.DataRoot(parent) == Path.Combine(parent, "Playhub"), "no marker: Playhub");
    Directory.CreateDirectory(Path.Combine(parent, "Core"));
    Require(CoreNames.DataRoot(parent) == Path.Combine(parent, "Playhub"), "Core without marker: still Playhub");
    Write(Path.Combine(parent, "Core", CoreNames.DataMarkerFile), "{}");
    Require(CoreNames.DataRoot(parent) == Path.Combine(parent, "Core"), "marker: Core");
});

Test("data migration: copy, move the large folders, skip scratch, keep the old folder", () =>
{
    string parent = NewRoot();
    string legacy = Path.Combine(parent, "Playhub"), current = Path.Combine(parent, "Core");
    Write(Path.Combine(legacy, "settings.json"), "{\"Language\":\"it\"}");
    Write(Path.Combine(legacy, "StoreLibrary", "steam-library-v1.json"), "owned");
    Write(Path.Combine(legacy, "StoreLibrary", "maintenance-v1.json"), "lease");
    Write(Path.Combine(legacy, "backups", "SteamArtwork", "a.png"), "art");
    Write(Path.Combine(legacy, "cache", "covers", "b.jpg"), "cover");
    Write(Path.Combine(legacy, "downloads", "updates", "Core-Setup.exe"), "running setup");
    Write(Path.Combine(legacy, "DeckyRuntime", "abc", "x.dll"), "scratch");
    Write(Path.Combine(legacy, "logs", "session.log"), "log");

    var result = CoreNames.MigrateDataRoot(parent);
    Require(result.Migrated && result.Problem is null && result.Root == current, "migrated: " + result.Problem);
    using (var marker = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(current, CoreNames.DataMarkerFile))))
        Require(marker.RootElement.GetProperty("from").GetString() == legacy
            && marker.RootElement.GetProperty("moved").EnumerateArray().Select(e => e.GetString()).SequenceEqual(new[] { "backups" }), "marker records the source and the moved folders");
    Require(File.ReadAllText(Path.Combine(current, "settings.json")) == "{\"Language\":\"it\"}", "settings copied");
    Require(File.ReadAllText(Path.Combine(legacy, "settings.json")) == "{\"Language\":\"it\"}", "old settings kept as backup");
    Require(File.Exists(Path.Combine(current, "StoreLibrary", "steam-library-v1.json")), "store registry copied");
    Require(File.Exists(Path.Combine(legacy, "StoreLibrary", "steam-library-v1.json")), "old store registry kept");
    Require(!File.Exists(Path.Combine(current, "StoreLibrary", "maintenance-v1.json")), "update lease not copied");
    Require(File.Exists(Path.Combine(current, "backups", "SteamArtwork", "a.png")) && !Directory.Exists(Path.Combine(legacy, "backups")), "backups moved");
    Require(File.Exists(Path.Combine(current, "cache", "covers", "b.jpg")) && File.Exists(Path.Combine(legacy, "cache", "covers", "b.jpg")), "cache copied, old kept");
    Require(!Directory.Exists(Path.Combine(current, "downloads")) && File.Exists(Path.Combine(legacy, "downloads", "updates", "Core-Setup.exe")), "downloads left alone");
    Require(!Directory.Exists(Path.Combine(current, "DeckyRuntime")), "Decky scratch not copied");
    Require(File.Exists(Path.Combine(current, "logs", "session.log")), "logs copied");
    Require(!Directory.Exists(Path.Combine(parent, ".Core-migration")), "no staging left");
    Require(CoreNames.DataRoot(parent) == current, "Core in use afterwards");

    File.WriteAllText(Path.Combine(current, "settings.json"), "new");
    var again = CoreNames.MigrateDataRoot(parent);
    Require(!again.Migrated && again.Root == current && File.ReadAllText(Path.Combine(current, "settings.json")) == "new", "second run is a no-op");
});

Test("data migration: a Core folder that is not ours is left alone", () =>
{
    string parent = NewRoot();
    Write(Path.Combine(parent, "Playhub", "settings.json"), "{}");
    Write(Path.Combine(parent, "Core", "other-app.txt"), "someone else");
    var lines = new List<string>();
    var result = CoreNames.MigrateDataRoot(parent, lines.Add);
    Require(!result.Migrated && result.Problem == "current_exists" && result.Root == Path.Combine(parent, "Playhub"), "refused");
    Require(Directory.GetFileSystemEntries(Path.Combine(parent, "Core")).Length == 1, "foreign folder untouched");
    Require(lines.Count == 1 && lines[0].Contains("Playhub stays in use"), "refusal logged");
    Require(CoreNames.DataRoot(parent) == Path.Combine(parent, "Playhub"), "Playhub stays in use");
});

Test("data migration: an interrupted staging copy resumes", () =>
{
    string parent = NewRoot();
    Write(Path.Combine(parent, "Playhub", "settings.json"), "{}");
    Write(Path.Combine(parent, "Playhub", "backups", "SteamClient", "c.txt"), "backup");
    Write(Path.Combine(parent, ".Core-migration", "settings.json"), "partial");
    Write(Path.Combine(parent, ".Core-migration", "backups", "SteamArtwork", "a.png"), "moved earlier");
    var result = CoreNames.MigrateDataRoot(parent);
    Require(result.Migrated, "resumed: " + result.Problem);
    string current = Path.Combine(parent, "Core");
    Require(File.ReadAllText(Path.Combine(current, "settings.json")) == "{}", "copy refreshed from the old folder");
    Require(File.Exists(Path.Combine(current, "backups", "SteamArtwork", "a.png")) && File.Exists(Path.Combine(current, "backups", "SteamClient", "c.txt")), "moved folders merged");
});

Test("install folder: existing installs stay, new ones go to Programs\\Core", () =>
{
    string local = NewRoot();
    string legacy = CoreNames.LegacyDefaultInstallDir(local), core = CoreNames.DefaultInstallDir(local);
    Require(core == Path.Combine(local, "Programs", "Core") && legacy == Path.Combine(local, "Programs", "Playhub"), "default names");
    bool IsInstall(string dir) => CoreNames.LooksLikeInstall(dir, File.Exists);
    Require(CoreNames.ChooseInstallDir(null, local, IsInstall) == core, "new install: Core");
    Write(Path.Combine(legacy, "Playhub.exe"), "app");
    Require(CoreNames.ChooseInstallDir(null, local, IsInstall) == legacy, "unregistered old install is updated in place");
    string custom = Path.Combine(local, "Games", "Playhub");
    Require(CoreNames.ChooseInstallDir(custom, local, IsInstall) == custom, "registered folder wins");
    Require(CoreNames.ChooseInstallDir("relative\\path", local, IsInstall) == legacy, "a relative registry value is ignored");
});

Test("install folder: lookups try the registered folder, then Core, then Playhub; Core.exe first", () =>
{
    string local = NewRoot();
    string legacy = CoreNames.LegacyDefaultInstallDir(local), core = CoreNames.DefaultInstallDir(local);
    string custom = Path.Combine(local, "Custom");
    var candidates = CoreNames.InstallDirCandidates(custom, local);
    Require(candidates.SequenceEqual(new[] { custom, core, legacy }), "order: " + string.Join(" | ", candidates));
    Require(CoreNames.InstallDirCandidates(legacy + Path.DirectorySeparatorChar, local).SequenceEqual(new[] { legacy, core }), "deduplicated");
    Require(CoreNames.FindAppExecutable(custom, local, File.Exists) is null, "nothing installed");
    Write(Path.Combine(legacy, "Playhub.exe"), "old");
    Require(CoreNames.FindAppExecutable(custom, local, File.Exists) == Path.Combine(legacy, "Playhub.exe"), "old install found");
    Write(Path.Combine(legacy, "Core.exe"), "new");
    Require(CoreNames.FindAppExecutable(custom, local, File.Exists) == Path.Combine(legacy, "Core.exe"), "Core.exe preferred");
    Write(Path.Combine(core, "Core.exe"), "fresh");
    Require(CoreNames.FindAppExecutable(null, local, File.Exists) == Path.Combine(core, "Core.exe"), "Programs\\Core before Programs\\Playhub");
});

Test("install folder: the unregistered default prefers an existing Core folder", () =>
{
    string local = NewRoot();
    string legacy = CoreNames.LegacyDefaultInstallDir(local), core = CoreNames.DefaultInstallDir(local);
    Require(CoreNames.ExistingDefaultInstallDir(local, Directory.Exists) == core, "nothing: Core");
    Directory.CreateDirectory(legacy);
    Require(CoreNames.ExistingDefaultInstallDir(local, Directory.Exists) == legacy, "only Playhub: Playhub");
    Directory.CreateDirectory(core);
    Require(CoreNames.ExistingDefaultInstallDir(local, Directory.Exists) == core, "both: Core");
});

Test("install folder: setup's folder picker", () =>
{
    string root = NewRoot();
    Require(CoreNames.InstallDirForChoice(Path.Combine(root, "Games")) == Path.Combine(root, "Games", "Core"), "generic folder gets Core");
    Require(CoreNames.InstallDirForChoice(Path.Combine(root, "Core") + Path.DirectorySeparatorChar) == Path.Combine(root, "Core"), "Core folder used as is");
    Require(CoreNames.InstallDirForChoice(Path.Combine(root, "playhub")) == Path.Combine(root, "playhub"), "old Playhub folder used as is");
});

Test("executables: old and new names are both recognised", () =>
{
    Require(CoreNames.IsAppExecutable(@"C:\Users\v\AppData\Local\Programs\Playhub\Core.exe"), "Core.exe");
    Require(CoreNames.IsAppExecutable("\"C:\\X\\PLAYHUB.EXE\""), "Playhub.exe, quoted, any case");
    Require(!CoreNames.IsAppExecutable(@"C:\X\Core2.exe") && !CoreNames.IsAppExecutable(null), "other names rejected");
    Require(CoreNames.IsDeckyHostExecutable("Core-DeckyHost.exe") && CoreNames.IsDeckyHostExecutable("Playhub-DeckyHost.exe"), "Decky host");
    Require(CoreNames.IsStoreHostExecutable("Core.StoreHost.exe") && CoreNames.IsStoreHostExecutable("Playhub.StoreHost.exe"), "store host");
    Require(CoreNames.ProcessNames(CoreNames.AppExecutables).SequenceEqual(new[] { "Core", "Playhub" }), "process names, Core first");
});

Test("executables: a process called Core is ours only inside the install folder", () =>
{
    string install = NewRoot(), elsewhere = NewRoot();
    Require(CoreNames.IsOwnedImage(Path.Combine(install, "Core.exe"), install, CoreNames.AppExecutables), "ours");
    Require(CoreNames.IsOwnedImage(Path.Combine(install, "Playhub.exe"), install + Path.DirectorySeparatorChar, CoreNames.AppExecutables), "old name, ours");
    Require(!CoreNames.IsOwnedImage(Path.Combine(elsewhere, "Core.exe"), install, CoreNames.AppExecutables), "another app's Core.exe");
    Require(!CoreNames.IsOwnedImage(Path.Combine(install, "sub", "Core.exe"), install, CoreNames.AppExecutables), "subfolder is not the install");
    Require(!CoreNames.IsOwnedImage(null, install, CoreNames.AppExecutables), "unknown image");
});

Test("update: old files are removed only once their successor exists", () =>
{
    string install = NewRoot();
    Write(Path.Combine(install, "Playhub.exe"), "old");
    Write(Path.Combine(install, "Playhub.dll"), "old");
    Write(Path.Combine(install, "unins-playhub.exe"), "old");
    Write(Path.Combine(install, "StoreLibrary", "Playhub.StoreHost.exe"), "old");
    Require(CoreNames.RetiredFilesToDelete(install, File.Exists).Count == 0, "no successor yet: nothing removed");
    Write(Path.Combine(install, "Core.exe"), "new");
    Write(Path.Combine(install, "Core.dll"), "new");
    Write(Path.Combine(install, "StoreLibrary", "Core.StoreHost.exe"), "new");
    var retired = CoreNames.RetiredFilesToDelete(install, File.Exists).Select(path => Path.GetRelativePath(install, path).Replace('\\', '/')).ToArray();
    Require(retired.SequenceEqual(new[] { "Playhub.exe", "Playhub.dll", "StoreLibrary/Playhub.StoreHost.exe" }), "retired: " + string.Join(", ", retired));
});

Console.WriteLine(failures == 0 ? "All Core name checks passed." : $"{failures} Core name check(s) failed.");
return failures == 0 ? 0 : 1;
