using System.Text.RegularExpressions;
using GamingMode.Services;

// Eternal Core Mark III tests: Steam update hold policy, Steam update job, Steam client backup.
static class SteamHoldTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		// Steam update hold policy
		{
			var t0 = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
			var off = SteamUpdateHoldPolicy.Observe(new SteamHoldState(), holdFilePresent: false, "100", t0);
			Check(off.Phase == SteamHoldPhase.Off && off.Next.HeldSince is null, "no steam.cfg: no hold");
			var start = SteamUpdateHoldPolicy.Observe(new SteamHoldState(), true, "100", t0);
			Check(start.Phase == SteamHoldPhase.Holding && start.Next.HeldSince == t0 && start.Next.HeldVersion == "100" && start.DaysLeft == 30, "hold starts when steam.cfg is first seen");
			var day29 = SteamUpdateHoldPolicy.Observe(start.Next, true, "100", t0.AddDays(29.9));
			Check(day29.Phase == SteamHoldPhase.Holding && day29.DaysLeft == 1, "day 29: still holding, one day left");
			var day30 = SteamUpdateHoldPolicy.Observe(start.Next, true, "100", t0.AddDays(30));
			Check(day30.Phase == SteamHoldPhase.Expired && day30.DaysLeft == 0, "day 30: hold expired");
			var bypass = SteamUpdateHoldPolicy.Observe(start.Next, true, "200", t0.AddDays(10));
			Check(bypass.UpdatedDespiteHold && bypass.Next.HeldSince == t0.AddDays(10) && bypass.Next.HeldVersion == "200" && bypass.Phase == SteamHoldPhase.Holding, "Steam updated despite steam.cfg: the hold restarts from the new version");
			var unreadable = SteamUpdateHoldPolicy.Observe(start.Next, true, null, t0.AddDays(5));
			Check(!unreadable.UpdatedDespiteHold && unreadable.Next.HeldVersion == "100", "an unreadable version is not a Steam update");
			var back = SteamUpdateHoldPolicy.Observe(start.Next, true, "100", t0.AddDays(-3));
			Check(back.Phase == SteamHoldPhase.Holding && back.DaysLeft == 30, "a clock moved back never expires the hold");
			var released = SteamUpdateHoldPolicy.Observe(day30.Next with { ExpiryNotified = true }, false, "100", t0.AddDays(31));
			Check(released.Phase == SteamHoldPhase.Off && !released.Next.ExpiryNotified && released.Next.HeldSince is null, "turning the hold off clears it");

			Check(SteamUpdateHoldPolicy.IsIdleMoment(true, true, false, false, t0, t0.AddMinutes(10)), "idle: Gaming Mode, Steam up, no game for 10 minutes");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(true, true, true, false, t0, t0.AddHours(3)), "never idle while a game runs");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(true, true, false, true, t0, t0.AddHours(3)), "never idle while a protected game runs");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(true, true, false, false, t0, t0.AddMinutes(9)), "not idle before 10 quiet minutes");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(false, true, false, false, t0, t0.AddHours(3)), "not idle in Desktop Mode");

			Check(SteamUpdateHoldPolicy.ParseClientManifestVersion("\"steam_client_win64\"\n{\n\t\"version\"\t\t\"1763083880\"\n\t\"bootstrapperversion\"\t\t\"1\"\n}") == "1763083880", "client manifest version is read");
			Check(SteamUpdateHoldPolicy.ParseClientManifestVersion("garbage") is null, "no version in a broken manifest");

			string root = Directory.CreateTempSubdirectory("core-steam-").FullName;
			Directory.CreateDirectory(Path.Combine(root, "package"));
			File.WriteAllText(Path.Combine(root, "package", "steam_client_win64.manifest"), "\"steam_client_win64\"\n{\n\t\"version\"\t\t\"42\"\n}");
			Check(SteamUpdateHoldPolicy.ReadClientVersion(root) == "42" && !SteamUpdateHoldPolicy.IsHoldFilePresent(root), "version read from the Steam folder; no hold file yet");
			string statePath = Path.Combine(root, "hold.json");
			SteamUpdateHoldPolicy.Save(statePath, start.Next with { RollbackSuggested = true });
			var loaded = SteamUpdateHoldPolicy.Load(statePath);
			Check(loaded.HeldSince == t0 && loaded.HeldVersion == "100" && loaded.RollbackSuggested, "hold state round-trips through its file");
			File.WriteAllText(statePath, "{ broken");
			Check(SteamUpdateHoldPolicy.Load(statePath).HeldSince is null, "a broken state file starts fresh");
			Directory.Delete(root, true);
		}
		// Steam update job
		{
			var happy = new FakeSteamHost();
			Check(await SteamUpdateJob.RunAsync(happy, CancellationToken.None) == SteamUpdateOutcome.Updated, "update: healthy plugins afterwards");
			Check(string.Join(",", happy.Calls) == "stop,backup,remove-hold,start,wait,apply-hold", "update order: stop, back up, release hold, start, check plugins, hold again");

			var playing = new FakeSteamHost { Playing = true };
			Check(await SteamUpdateJob.RunAsync(playing, CancellationToken.None) == SteamUpdateOutcome.NotWhilePlaying && playing.Calls.Count == 0, "never updates during a game");

			var noBackup = new FakeSteamHost { BackupOk = false };
			Check(await SteamUpdateJob.RunAsync(noBackup, CancellationToken.None) == SteamUpdateOutcome.BackupFailed
				&& string.Join(",", noBackup.Calls) == "stop,backup,start", "no backup: Steam comes back on the held version, the hold is never released");

			var stubborn = new FakeSteamHost { StopOk = false };
			Check(await SteamUpdateJob.RunAsync(stubborn, CancellationToken.None) == SteamUpdateOutcome.SteamWouldNotClose && !stubborn.Calls.Contains("remove-hold"), "Steam that will not close is left as it was");

			var broken = new FakeSteamHost { PluginsHealthy = new Queue<bool>(new[] { false, false }) };
			Check(await SteamUpdateJob.RunAsync(broken, CancellationToken.None) == SteamUpdateOutcome.UpdatedPluginsBroken
				&& string.Join(",", broken.Calls) == "stop,backup,remove-hold,start,wait,apply-hold,restart-plugins,wait", "broken plugins: restart them once, then report for rollback");
			var recovered = new FakeSteamHost { PluginsHealthy = new Queue<bool>(new[] { false, true }) };
			Check(await SteamUpdateJob.RunAsync(recovered, CancellationToken.None) == SteamUpdateOutcome.Updated, "plugins that come back after a restart count as updated");
			var neverHad = new FakeSteamHost { WereHealthy = false, PluginsHealthy = new Queue<bool>(new[] { false }) };
			Check(await SteamUpdateJob.RunAsync(neverHad, CancellationToken.None) == SteamUpdateOutcome.Updated && !neverHad.Calls.Contains("restart-plugins"),
				"plugins that were already broken are not blamed on the update");
			var noStart = new FakeSteamHost { StartOk = false };
			Check(await SteamUpdateJob.RunAsync(noStart, CancellationToken.None) == SteamUpdateOutcome.SteamDidNotStart && noStart.Calls.Last() == "apply-hold", "Steam that does not start: the hold goes back");

			var rollback = new FakeSteamHost();
			Check(await SteamUpdateJob.RollbackAsync(rollback, CancellationToken.None) == SteamRollbackOutcome.RolledBack
				&& string.Join(",", rollback.Calls) == "stop,apply-hold,restore,start", "rollback: stop, hold first, restore, start");
			Check(await SteamUpdateJob.RollbackAsync(new FakeSteamHost { HasBackup = false }, CancellationToken.None) == SteamRollbackOutcome.NoBackup, "rollback without a backup says so");
			Check(await SteamUpdateJob.RollbackAsync(new FakeSteamHost { Playing = true }, CancellationToken.None) == SteamRollbackOutcome.NotWhilePlaying, "no rollback during a game");
			var failedRestore = new FakeSteamHost { RestoreOk = false };
			Check(await SteamUpdateJob.RollbackAsync(failedRestore, CancellationToken.None) == SteamRollbackOutcome.RestoreFailed && failedRestore.Calls.Last() == "start", "a failed restore still starts Steam");
		}
		// Steam client backup
		{
			string steam = Directory.CreateTempSubdirectory("core-steam-client-").FullName;
			string backups = Directory.CreateTempSubdirectory("core-steam-backups-").FullName;
			void Write(string relative, string text) { string path = Path.Combine(steam, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
			Write("steam.exe", "v1"); Write("steamui/index.html", "ui1"); Write("package/steam_client_win64.manifest", "m1"); Write("steam.cfg", "hold");
			Write("userdata/1/config.vdf", "user"); Write("steamapps/common/Game/game.exe", "game"); Write("config/loginusers.vdf", "login"); Write("logs/x.log", "log"); Write("crash.dmp", "dump");
			var made = SteamClientBackup.Create(steam, backups, "1763083880", DateTimeOffset.Now, CancellationToken.None);
			Check(made.Ok && SteamClientBackup.LooksLikeBackup(made.Folder!), "backup made with the app's marker");
			string[] inBackup = Directory.EnumerateFiles(made.Folder!, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(made.Folder!, f).Replace('\\', '/')).ToArray();
			Check(inBackup.Contains("steam.exe") && inBackup.Contains("steamui/index.html") && inBackup.Contains("package/steam_client_win64.manifest"), "backup holds the client");
			Check(!inBackup.Any(f => f.StartsWith("userdata") || f.StartsWith("steamapps") || f.StartsWith("config") || f.StartsWith("logs") || f == "steam.cfg" || f.EndsWith(".dmp")),
				"backup never holds user data, games, login config, logs, dumps or steam.cfg");
			Check(SteamClientBackup.MadeByCore(made.Folder!), "the agent's backups are marked as its own");
			Write("steam.exe", "v2"); Write("steamui/index.html", "ui2"); Write("steamui/new.js", "new");
			var restored = SteamClientBackup.Restore(made.Folder!, steam, CancellationToken.None);
			Check(restored.Ok && File.ReadAllText(Path.Combine(steam, "steam.exe")) == "v1" && File.ReadAllText(Path.Combine(steam, "steamui/index.html")) == "ui1", "restore puts the backed-up client back");
			Check(File.Exists(Path.Combine(steam, "steamui/new.js")) && File.ReadAllText(Path.Combine(steam, "userdata/1/config.vdf")) == "user" && File.Exists(Path.Combine(steam, "steam.cfg")),
				"restore deletes nothing and leaves user data and the hold alone");
			Check(!File.Exists(Path.Combine(steam, SteamClientBackup.Marker)), "the marker is not copied into Steam");

			string user = Path.Combine(backups, "PlayhubSteamClient-20200101-000000");
			Directory.CreateDirectory(user); File.WriteAllText(Path.Combine(user, "steam.exe"), "u"); File.WriteAllText(Path.Combine(user, SteamClientBackup.Marker), "{\"kind\":\"steam-client\"}");
			Directory.SetCreationTimeUtc(user, DateTime.UtcNow.AddYears(-5));
			for (int i = 0; i < 3; i++)
			{
				var extra = SteamClientBackup.Create(steam, backups, "v", DateTimeOffset.Now.AddSeconds(i + 1), CancellationToken.None);
				Directory.SetCreationTimeUtc(extra.Folder!, DateTime.UtcNow.AddMinutes(i + 1));
			}
			int removed = SteamClientBackup.Prune(backups, keep: 2);
			var left = Directory.GetDirectories(backups).Where(SteamClientBackup.LooksLikeBackup).ToArray();
			Check(removed == 2 && left.Length == 3 && left.Contains(user), "pruning keeps the newest two of Core's backups and never the user's own");
			Check(SteamClientBackup.Newest(backups) is { } newest && SteamClientBackup.MadeByCore(newest), "newest backup is found for rollback");

			// The exclusion lists must stay the same as the app's ExtraService (same backups, both ways).
			string? source = FindUp("Source/Playhub/Services/ExtraService.cs");
			Check(source is not null, "ExtraService.cs found for the exclusion comparison");
			string text = File.ReadAllText(source!);
			string Block(string name) => Regex.Match(text, name + @"\s*=\s*(?:new\s*\[\]\s*)?\{(?<body>[^}]*)\}").Groups["body"].Value;
			string[] Quoted(string block) => Regex.Matches(block, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
			Check(Quoted(Block("SteamClientExcludedFolders")).SequenceEqual(SteamClientBackup.ExcludedFolders.OrderBy(v => v, StringComparer.Ordinal))
				&& Quoted(Block("SteamClientExcludedExtensions")).SequenceEqual(SteamClientBackup.ExcludedExtensions.OrderBy(v => v, StringComparer.Ordinal))
				&& Quoted(Block("SteamClientExcludedRootFiles")).SequenceEqual(SteamClientBackup.ExcludedRootFiles.OrderBy(v => v, StringComparer.Ordinal))
				&& text.Contains("\"" + SteamClientBackup.Marker + "\"") && text.Contains("\"PlayhubSteamClient-\""), "agent and app back up exactly the same files with the same marker");
			Directory.Delete(steam, true); Directory.Delete(backups, true);
		}
	}

	static string? FindUp(string relative)
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			string candidate = Path.Combine(dir.FullName, relative);
			if (File.Exists(candidate)) return candidate;
		}
		return null;
	}
}

internal sealed class FakeSteamHost : ISteamUpdateHost
{
	internal readonly List<string> Calls = new();
	internal bool Playing, StopOk = true, StartOk = true, BackupOk = true, RestoreOk = true, HasBackup = true, WereHealthy = true;
	internal Queue<bool> PluginsHealthy = new(new[] { true });
	public bool GameRunning() => Playing;
	public Task<bool> StopSteamAsync(CancellationToken cancellation) { Calls.Add("stop"); return Task.FromResult(StopOk); }
	public Task<bool> StartSteamAsync(CancellationToken cancellation) { Calls.Add("start"); return Task.FromResult(StartOk); }
	public Task<bool> BackupClientAsync(CancellationToken cancellation) { Calls.Add("backup"); return Task.FromResult(BackupOk); }
	public bool HasClientBackup() => HasBackup;
	public Task<bool> RestoreNewestBackupAsync(CancellationToken cancellation) { Calls.Add("restore"); return Task.FromResult(RestoreOk); }
	public bool RemoveHold() { Calls.Add("remove-hold"); return true; }
	public bool ApplyHold() { Calls.Add("apply-hold"); return true; }
	public bool PluginsWereHealthy() => WereHealthy;
	public Task<bool> WaitForPluginsAsync(DateTimeOffset since, TimeSpan timeout, CancellationToken cancellation)
	{ Calls.Add("wait"); return Task.FromResult(PluginsHealthy.Count > 0 ? PluginsHealthy.Dequeue() : true); }
	public Task RestartPluginsAsync(CancellationToken cancellation) { Calls.Add("restart-plugins"); return Task.CompletedTask; }
	public DateTimeOffset Now() => DateTimeOffset.UnixEpoch;
	public void Stage(SteamUpdateStage stage) { }
}
