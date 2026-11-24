using GamingMode.Services;

// Anti-cheat safe mode: detection policy, folder probe and pause/resume orchestration.
// Console test exe, like the other agent checks: `dotnet run -c Release`, exit code 0 = pass.

int failures = 0, passed = 0;
void Test(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }

var on = AntiCheatSettings.Defaults;
AntiCheatObservation Seen(IEnumerable<AntiCheatService>? services = null, IEnumerable<string>? processes = null,
    IEnumerable<string>? folders = null, IEnumerable<string>? executables = null, bool gameRunning = false)
    => new((services ?? []).ToArray(), (processes ?? []).ToArray(), (folders ?? []).ToArray(), (executables ?? []).ToArray(), gameRunning);

// A fake file system: folder -> entries. Records every folder that was listed.
var listed = new List<string>();
Func<string, IReadOnlyCollection<string>?> Disk(Dictionary<string, string[]> tree) => folder =>
{
    listed.Add(folder);
    return tree.TryGetValue(folder, out var entries) ? entries : Array.Empty<string>();
};
var empty = Disk(new());

// ---------- Services ----------

Test("disabled master switch never protects, even with an anti-cheat service running", () =>
{
    var verdict = AntiCheatProtectionPolicy.Evaluate(on with { Enabled = false },
        Seen([new("EasyAntiCheat_EOS", AntiCheatServiceStart.Manual)], gameRunning: true), empty);
    Require(!verdict.Protected, "protected while disabled");
});

Test("an on-demand anti-cheat service that is running protects and names itself", () =>
{
    var verdict = AntiCheatProtectionPolicy.Evaluate(on, Seen([new("atvi-randgrid_sr", AntiCheatServiceStart.Manual)]), empty);
    Require(verdict.Protected, "not protected");
    Require(verdict.Reason.Contains("atvi-randgrid_sr") && verdict.Reason.Contains("Ricochet"), "reason: " + verdict.Reason);
});

Test("service names match case-insensitively", () =>
    Require(AntiCheatProtectionPolicy.Evaluate(on, Seen([new("beservice", AntiCheatServiceStart.Manual)]), empty).Protected, "BEService not matched"));

foreach (var start in new[] { AntiCheatServiceStart.Boot, AntiCheatServiceStart.System, AntiCheatServiceStart.Automatic })
    Test($"a {start}-start service runs from Windows start-up and is not a game signal", () =>
        Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen([new("vgk", start)]), empty).Protected, "permanent service engaged protection"));

Test("an unknown start type still counts, to stay safe", () =>
    Require(AntiCheatProtectionPolicy.Evaluate(on, Seen([new("vgc", AntiCheatServiceStart.Unknown)]), empty).Protected, "unknown start ignored"));

Test("a service that is not in the catalogue is ignored", () =>
    Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen([new("Spooler", AntiCheatServiceStart.Manual)]), empty).Protected, "unrelated service matched"));

Test("every requested anti-cheat family is in the service catalogue", () =>
{
    foreach (var name in new[] { "EasyAntiCheat", "EasyAntiCheat_EOS", "BEService", "vgc", "vgk", "FACEIT", "FACEITService",
        "EAAntiCheatService", "atvi-randgrid_sr", "mhyprot2", "npggsvc", "GameGuard", "xhunter1" })
        Require(AntiCheatCatalog.Services.ContainsKey(name), name + " missing");
});

// ---------- Always-protected executables ----------

Test("the always-protected list matches process names with or without .exe, path or case", () =>
{
    foreach (var entry in new[] { "cod.exe", "COD", "\"D:\\Games\\Call of Duty\\cod.exe\"", " cod.EXE " })
    {
        var verdict = AntiCheatProtectionPolicy.Evaluate(on with { ProtectedExecutables = [entry] }, Seen(processes: ["explorer", "cod"]), empty);
        Require(verdict.Protected && verdict.Reason.Contains("cod.exe"), $"'{entry}' did not match: {verdict.Reason}");
    }
});

Test("the always-protected list does not match a different process", () =>
    Require(!AntiCheatProtectionPolicy.Evaluate(on with { ProtectedExecutables = ["cod.exe"] }, Seen(processes: ["codex", "notcod"]), empty).Protected,
        "partial name matched"));

Test("the settings box parses one executable per line, commas or semicolons, without duplicates", () =>
{
    var parsed = AntiCheatProtectionPolicy.ParseExecutableList("cod.exe\r\n\r\nValorant.exe, r5apex.exe;COD.EXE\n  \"bf6.exe\"  ");
    Require(parsed.SequenceEqual(new[] { "cod.exe", "Valorant.exe", "r5apex.exe", "bf6.exe" }), string.Join("|", parsed));
    Require(AntiCheatProtectionPolicy.ParseExecutableList(null).Count == 0 && AntiCheatProtectionPolicy.ParseExecutableList(" \n.exe\n").Count == 0,
        "empty input produced entries");
});

// ---------- Anti-cheat files in the game's folder ----------

Test("a Steam game's install folder with EasyAntiCheat protects", () =>
{
    var disk = Disk(new() { [@"D:\SteamLibrary\steamapps\common\Elden Ring"] = ["eldenring.exe", "EasyAntiCheat", "start_protected_game.exe"] });
    var verdict = AntiCheatProtectionPolicy.Evaluate(on, Seen(folders: [@"D:\SteamLibrary\steamapps\common\Elden Ring"]), disk);
    Require(verdict.Protected && verdict.Reason.Contains("Easy Anti-Cheat") && verdict.Reason.Contains("Elden Ring"), verdict.Reason);
});

Test("randgrid.sys in Call of Duty HQ marks Ricochet", () =>
{
    var disk = Disk(new() { [@"C:\Program Files (x86)\Steam\steamapps\common\Call of Duty HQ"] = ["cod.exe", "randgrid.sys", "bootstrapper.log"] });
    var verdict = AntiCheatProtectionPolicy.Evaluate(on, Seen(folders: [@"C:\Program Files (x86)\Steam\steamapps\common\Call of Duty HQ"]), disk);
    Require(verdict.Protected && verdict.Reason.Contains("Ricochet"), verdict.Reason);
});

Test("a marker two folders above the executable is found (bin\\x64\\game.exe)", () =>
{
    var disk = Disk(new() { [@"E:\Games\Hunt"] = ["BattlEye", "bin"] });
    var verdict = AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"E:\Games\Hunt\bin\x64\HuntGame.exe"]), disk);
    Require(verdict.Protected && verdict.Reason.Contains("BattlEye") && verdict.Reason.Contains("HuntGame.exe"), verdict.Reason);
});

Test("forward slashes and quotes in an image path are handled", () =>
{
    var disk = Disk(new() { [@"E:\Games\Hunt"] = ["BattlEye"] });
    Require(AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: ["\"E:/Games/Hunt/bin/HuntGame.exe\""]), disk).Protected, "not normalized");
});

Test("the walk stops at a library folder, so a neighbouring game's anti-cheat never matches", () =>
{
    listed.Clear();
    var disk = Disk(new() { [@"D:\SteamLibrary\steamapps\common"] = ["EasyAntiCheat"], [@"D:\SteamLibrary\steamapps\common\Other Game"] = ["EasyAntiCheat"] });
    var verdict = AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"D:\SteamLibrary\steamapps\common\Indie\Indie.exe"]), disk);
    Require(!verdict.Protected, verdict.Reason);
    Require(listed.SequenceEqual(new[] { @"D:\SteamLibrary\steamapps\common\Indie" }), "listed: " + string.Join(" | ", listed));
});

Test("drive roots and system folders are never listed", () =>
{
    listed.Clear();
    var disk = Disk(new() { [@"C:\"] = ["EasyAntiCheat"], [@"C:\Windows"] = ["BattlEye"] });
    Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"C:\game.exe", @"C:\Windows\System32\notepad.exe"], folders: [@"C:\"]), disk).Protected,
        "root or system folder matched");
    Require(listed.Count == 0, "listed: " + string.Join(" | ", listed));
});

Test("the walk goes at most three levels above the executable's folder", () =>
{
    var disk = Disk(new() { [@"E:\Lib\Game"] = ["EasyAntiCheat"] });
    Require(AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"E:\Lib\Game\a\b\c\game.exe"]), disk).Protected, "three levels up missed");
    Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"E:\Lib\Game\a\b\c\d\game.exe"]), disk).Protected, "four levels up matched");
});

Test("a folder that cannot be listed proves nothing and does not throw", () =>
{
    Func<string, IReadOnlyCollection<string>?> broken = _ => throw new UnauthorizedAccessException();
    Func<string, IReadOnlyCollection<string>?> missing = _ => null;
    Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"E:\Games\X\x.exe"], folders: [@"E:\Games\X"]), broken).Protected, "broken lister");
    Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"E:\Games\X\x.exe"]), missing).Protected, "missing folder");
});

Test("a game without anti-cheat files is not protected by default", () =>
{
    var disk = Disk(new() { [@"E:\Games\Celeste"] = ["Celeste.exe", "Content"] });
    Require(!AntiCheatProtectionPolicy.Evaluate(on, Seen(executables: [@"E:\Games\Celeste\Celeste.exe"], gameRunning: true), disk).Protected, "plain game protected");
});

// ---------- Treat every game as protected ----------

Test("\"Treat every game as protected\" protects any running game, and only while one runs", () =>
{
    var every = on with { TreatEveryGameAsProtected = true };
    Require(AntiCheatProtectionPolicy.Evaluate(every, Seen(gameRunning: true), empty).Protected, "running game not protected");
    Require(!AntiCheatProtectionPolicy.Evaluate(every, Seen(gameRunning: false), empty).Protected, "protected with no game");
    Require(!AntiCheatProtectionPolicy.Evaluate(every with { Enabled = false }, Seen(gameRunning: true), empty).Protected, "master switch ignored");
});

Test("a specific signal is reported before the catch-all", () =>
{
    var verdict = AntiCheatProtectionPolicy.Evaluate(on with { TreatEveryGameAsProtected = true },
        Seen([new("EasyAntiCheat", AntiCheatServiceStart.Manual)], gameRunning: true), empty);
    Require(verdict.Reason.Contains("EasyAntiCheat"), verdict.Reason);
});

// ---------- Paths and Steam manifests ----------

Test("Windows path arithmetic works the same on Linux", () =>
{
    Require(WindowsPath.Parent(@"C:\Games\X\x.exe") == @"C:\Games\X", "parent");
    Require(WindowsPath.Parent(@"C:\Games") == @"C:\", "parent of top folder");
    Require(WindowsPath.Parent(@"C:\") is null && WindowsPath.Parent("C:") is null, "root has no parent");
    Require(WindowsPath.Leaf(@"C:\Games\X\") == "X" && WindowsPath.Normalize("C:/Games/X/") == @"C:\Games\X", "leaf/normalize");
    Require(WindowsPath.Parent(@"\\server\share\Game\g.exe") == @"\\server\share\Game", "UNC parent");
});

Test("Steam library folders and installdir are read from Steam's text manifests", () =>
{
    const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
    var libraries = SteamLibraryText.LibraryFolders(vdf);
    Require(libraries.SequenceEqual(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }), string.Join("|", libraries));
    Require(SteamLibraryText.InstallDir("\"AppState\"\n{\n\t\"appid\"\t\t\"1938090\"\n\t\"installdir\"\t\t\"Call of Duty HQ\"\n}") == "Call of Duty HQ", "installdir");
    Require(SteamLibraryText.InstallDir("\"installdir\" \"..\\\\..\\\\Windows\"") is null && SteamLibraryText.InstallDir("\"installdir\" \"..\"") is null
        && SteamLibraryText.InstallDir("") is null, "unsafe installdir accepted");
});

// ---------- Pause / resume orchestration ----------

(AntiCheatProtectionOrchestrator Orchestrator, List<string> Events, List<string> Log) Harness(int quiet = 3, string? failing = null)
{
    var events = new List<string>();
    var log = new List<string>();
    AntiCheatProtectionOrchestrator? orchestrator = null;
    IAntiCheatPausable Feature(string key, string name) => new AntiCheatPausable(key, name,
        () => { events.Add($"pause {name} engaged={orchestrator!.Engaged}"); if (name == failing) throw new InvalidOperationException("boom"); },
        () => events.Add($"resume {name} engaged={orchestrator!.Engaged}"));
    orchestrator = new AntiCheatProtectionOrchestrator(
        new[] { Feature("qos", "game power QoS"), Feature("window", "borderless/foreground handling"), Feature("volume", "volume-key hook"),
            Feature("cursor", "cursor auto-hide") },
        log.Add, quiet);
    return (orchestrator, events, log);
}
var protectedGame = new AntiCheatVerdict(true, "anti-cheat service EasyAntiCheat (Easy Anti-Cheat) is running");

Test("engaging pauses all four features once, after Engaged is already true", () =>
{
    var (o, events, log) = Harness();
    o.Observe(protectedGame);
    o.Observe(protectedGame);
    Require(o.Engaged, "not engaged");
    Require(events.SequenceEqual(new[] { "pause game power QoS engaged=True", "pause borderless/foreground handling engaged=True",
        "pause volume-key hook engaged=True", "pause cursor auto-hide engaged=True" }), string.Join(" | ", events));
    Require(log.Count == 1 && log[0].Contains("engaged") && log[0].Contains("EasyAntiCheat"), string.Join(" | ", log));
});

Test("release waits for consecutive quiet samples and resumes in reverse order", () =>
{
    var (o, events, log) = Harness();
    o.Observe(protectedGame);
    events.Clear();
    o.Observe(AntiCheatVerdict.None);
    o.Observe(AntiCheatVerdict.None);
    Require(o.Engaged && events.Count == 0, "released too early");
    o.Observe(AntiCheatVerdict.None);
    Require(!o.Engaged, "still engaged");
    Require(events.SequenceEqual(new[] { "resume cursor auto-hide engaged=True", "resume volume-key hook engaged=True",
        "resume borderless/foreground handling engaged=True", "resume game power QoS engaged=True" }), string.Join(" | ", events));
    Require(log.Last().Contains("released") && log.Last().Contains("EasyAntiCheat"), log.Last());
});

Test("a launcher handing over to the game (one quiet sample) does not flap the features", () =>
{
    var (o, events, _) = Harness();
    o.Observe(protectedGame);
    o.Observe(AntiCheatVerdict.None);
    o.Observe(AntiCheatVerdict.None);
    o.Observe(protectedGame with { Reason = "randgrid.sys (Ricochet (Call of Duty)) found in the running game's folder X" });
    o.Observe(AntiCheatVerdict.None);
    o.Observe(AntiCheatVerdict.None);
    Require(o.Engaged && events.Count(e => e.StartsWith("pause")) == 4 && !events.Any(e => e.StartsWith("resume")), string.Join(" | ", events));
});

Test("a change of reason while engaged is logged once, without pausing again", () =>
{
    var (o, events, log) = Harness();
    o.Observe(protectedGame);
    var other = new AntiCheatVerdict(true, "cod.exe is on the \"always protected\" list");
    o.Observe(other);
    o.Observe(other);
    Require(events.Count == 4 && log.Count == 2 && log[1].Contains("still engaged") && log[1].Contains("cod.exe"), string.Join(" | ", log));
});

Test("nothing happens while no protected game is seen", () =>
{
    var (o, events, log) = Harness();
    for (int i = 0; i < 10; i++) o.Observe(AntiCheatVerdict.None);
    Require(!o.Engaged && events.Count == 0 && log.Count == 0, "acted without a protected game");
});

Test("a feature that fails to pause is logged and the others still pause", () =>
{
    var (o, events, log) = Harness(failing: "borderless/foreground handling");
    o.Observe(protectedGame);
    Require(events.Count == 4 && o.Engaged, string.Join(" | ", events));
    Require(log.Any(line => line.Contains("could not pause borderless/foreground handling") && line.Contains("boom")), string.Join(" | ", log));
});

Test("an explicit release resumes at once, and a second release does nothing", () =>
{
    var (o, events, log) = Harness();
    o.Observe(protectedGame);
    events.Clear();
    o.Release("test");
    o.Release("test again");
    Require(!o.Engaged && events.Count(e => e.StartsWith("resume")) == 4 && log.Count(line => line.Contains("released")) == 1, string.Join(" | ", log));
});

Test("protection engages again for the next protected game", () =>
{
    var (o, events, _) = Harness(quiet: 1);
    o.Observe(protectedGame);
    o.Observe(AntiCheatVerdict.None);
    o.Observe(protectedGame);
    Require(o.Engaged && events.Count(e => e.StartsWith("pause")) == 8 && events.Count(e => e.StartsWith("resume")) == 4, string.Join(" | ", events));
});

Test("per-feature pause state is visible while engaged and cleared after release", () =>
{
    var (o, _, _) = Harness(quiet: 1);
    Require(!o.IsPaused("qos"), "paused before engagement");
    o.Observe(protectedGame);
    Require(o.IsPaused("qos") && o.IsPaused("VOLUME") && !o.IsPaused("unknown"), "pause state missing");
    o.Observe(AntiCheatVerdict.None);
    Require(!o.IsPaused("qos") && !o.Engaged, "pause state survived release");
});

Test("bisecting: a configured subset pauses and resumes only those features", () =>
{
    var (o, events, log) = Harness(quiet: 1);
    o.SetPausedFeatures(new[] { "volume", " Cursor " });
    o.Observe(protectedGame);
    Require(events.SequenceEqual(new[] { "pause volume-key hook engaged=True", "pause cursor auto-hide engaged=True" }), string.Join(" | ", events));
    Require(!o.IsPaused("qos") && o.IsPaused("cursor"), "subset pause state wrong");
    Require(log[0].Contains("Pausing: volume-key hook, cursor auto-hide."), log[0]);
    o.SetPausedFeatures(null); // a change applies from the next engagement, never to the features already paused
    o.Observe(AntiCheatVerdict.None);
    Require(events.Skip(2).SequenceEqual(new[] { "resume cursor auto-hide engaged=True", "resume volume-key hook engaged=True" }), string.Join(" | ", events));
});

Test("bisecting: unknown or empty feature keys fall back to pausing all four", () =>
{
    foreach (var keys in new[] { new[] { "keyboard" }, Array.Empty<string>() })
    {
        var (o, events, _) = Harness();
        o.SetPausedFeatures(keys);
        o.Observe(protectedGame);
        Require(events.Count == 4, $"[{string.Join(",", keys)}] paused {events.Count}");
    }
});

Console.WriteLine(failures == 0 ? $"ALL PASS ({passed})" : $"{failures} FAILED, {passed} passed");
return failures == 0 ? 0 : 1;
