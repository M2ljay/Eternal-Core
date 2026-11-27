using System.Text.RegularExpressions;
using GamingMode.Services;

// Eternal Core tests: Steam update policy (soak, night decisions, failures), smoke test, manual update, update job, Steam client backup.
static class SteamHoldTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		// Steam update policy: soak per version (docs/eternal/STEAM-UPDATES.md)
		{
			const string fp = "fp";
			var noon = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
			DateTimeOffset NightAt(int day, int hour = 5) => new DateTimeOffset(2026, 11, 25, hour, 30, 0, TimeSpan.Zero).AddDays(day);
			Check(SteamUpdateHoldPolicy.NightOf(noon) == new DateOnly(2026, 11, 26) && SteamUpdateHoldPolicy.NightOf(NightAt(0)) == new DateOnly(2026, 11, 25)
				&& SteamUpdateHoldPolicy.NightOf(new DateTimeOffset(2026, 11, 25, 7, 0, 0, TimeSpan.Zero)) == new DateOnly(2026, 11, 26), "a moment belongs to the next update night (named by its morning)");
			Check(SteamUpdateHoldPolicy.SoakDays(null) == 10 && SteamUpdateHoldPolicy.SoakDays(0) == 1 && SteamUpdateHoldPolicy.SoakDays(999) == 60 && SteamUpdateHoldPolicy.SoakDays(14) == 14,
				"soak is 10 days by default; the setting is clamped to 1-60");

			var start = SteamUpdateHoldPolicy.Observe(new SteamHoldState(), true, "100", null, noon);
			Check(start.Next.InstalledVersion == "100" && start.Next.PendingVersion is null && !start.UpdatedDespiteHold, "first look: the installed version is remembered, nothing pending");
			Check(SteamUpdateHoldPolicy.View(start.Next, true, 10, fp, noon).Phase == SteamHoldPhase.Current, "nothing pending: current");
			Check(SteamUpdateHoldPolicy.View(start.Next, false, 10, fp, noon).Phase == SteamHoldPhase.Off, "block switched off: off");

			var seen = SteamUpdateHoldPolicy.Observe(start.Next, true, "100", "200", noon);
			Check(seen.Next.PendingVersion == "200" && seen.Next.PendingDayOne == new DateOnly(2026, 11, 26), "a pending version seen at noon: tonight is day one");
			var soaking = SteamUpdateHoldPolicy.View(seen.Next, true, 10, fp, NightAt(1));
			Check(soaking.Phase == SteamHoldPhase.Soaking && soaking.DaysLeft == 10 && soaking.SoakEnds == new DateOnly(2026, 12, 6), "day one: ten days to wait");
			Check(SteamUpdateHoldPolicy.View(seen.Next, true, 10, fp, NightAt(10)).Phase == SteamHoldPhase.Soaking
				&& SteamUpdateHoldPolicy.View(seen.Next, true, 10, fp, NightAt(10)).DaysLeft == 1, "day ten: still waiting");
			var due = SteamUpdateHoldPolicy.View(seen.Next, true, 10, fp, NightAt(11));
			Check(due.Phase == SteamHoldPhase.Due && !due.Attention, "after ten days the update is due");
			Check(SteamUpdateHoldPolicy.View(seen.Next, true, 3, fp, NightAt(4)).Phase == SteamHoldPhase.Due, "the soak follows the setting");

			var same = SteamUpdateHoldPolicy.Observe(seen.Next, true, "100", "200", NightAt(5));
			Check(same.Next.PendingDayOne == new DateOnly(2026, 11, 26), "the same pending version keeps its day one");
			var unknown = SteamUpdateHoldPolicy.Observe(seen.Next, true, "100", null, NightAt(5));
			Check(unknown.Next.PendingVersion == "200", "a round without a pending reading keeps what is known");
			var newer = SteamUpdateHoldPolicy.Observe(seen.Next with { PostponedSince = noon, PostponedReason = "pending-unknown" }, true, "100", "300", NightAt(8, 14));
			Check(newer.Next.PendingVersion == "300" && newer.Next.PendingDayOne == new DateOnly(2026, 12, 4) && newer.Next.PostponedSince is null,
				"a newer pending version restarts its own ten days and drops the postponement");
			var older = SteamUpdateHoldPolicy.Observe(seen.Next, true, "100", "90", NightAt(5));
			Check(older.Next.PendingVersion is null, "a published version not newer than the installed one is no update");

			// Unexpected update: the version watch.
			var bypass = SteamUpdateHoldPolicy.Observe(seen.Next, true, "200", null, NightAt(3));
			Check(bypass.UpdatedDespiteHold && bypass.Next.InstalledVersion == "200" && bypass.Next.PendingVersion is null, "Steam updated despite the block: noticed, and the pending version is gone");
			Check(!SteamUpdateHoldPolicy.Observe(seen.Next, true, "200", null, NightAt(3), coreUpdating: true).UpdatedDespiteHold, "Core's own update is not 'despite the block'");
			Check(!SteamUpdateHoldPolicy.Observe(seen.Next, false, "200", null, NightAt(3)).UpdatedDespiteHold, "with the block off, Steam updating itself is expected");
			Check(!SteamUpdateHoldPolicy.Observe(seen.Next, true, null, null, NightAt(3)).UpdatedDespiteHold, "an unreadable installed version is not an update");

			// Update night decisions.
			var pass = new SteamGateVerdict(SteamGateResult.Pass, "ok");
			var cantTell = new SteamGateVerdict(SteamGateResult.CantTell, "pending-unknown");
			var no = new SteamGateVerdict(SteamGateResult.Incompatible, "pending-unsupported");
			SteamHoldView DueAt(DateTimeOffset at, SteamHoldState s) => SteamUpdateHoldPolicy.View(s, true, 10, fp, at);
			Check(SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(11), seen.Next), seen.Next, true, pass, NightAt(11)) == SteamNightAction.Update, "due, update night, idle, gate passes: update");
			Check(SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(11, 12), seen.Next), seen.Next, true, pass, NightAt(11, 12)) == SteamNightAction.None, "never outside update night");
			Check(SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(11), seen.Next), seen.Next, false, pass, NightAt(11)) == SteamNightAction.None, "never while not idle (a game, Desktop Mode)");
			Check(SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(10), seen.Next), seen.Next, true, pass, NightAt(10)) == SteamNightAction.None, "never before the soak ends");
			Check(SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(11), seen.Next), seen.Next, true, cantTell, NightAt(11)) == SteamNightAction.Postpone
				&& SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(11), seen.Next), seen.Next, true, no, NightAt(11)) == SteamNightAction.Postpone, "gate says no or can't tell: postpone");
			var postponed = SteamUpdateHoldPolicy.Postponed(seen.Next, "pending-unknown", NightAt(11));
			Check(SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(11, 6), postponed), postponed, true, pass, NightAt(11, 6)) == SteamNightAction.None, "one decision per night");
			Check(DueAt(NightAt(12), postponed).Phase == SteamHoldPhase.Postponed
				&& SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(12), postponed), postponed, true, pass, NightAt(12)) == SteamNightAction.Update, "re-checked the next update night, and updates once the gate passes");
			var later = SteamUpdateHoldPolicy.Postponed(postponed, "pending-unsupported", NightAt(12));
			Check(later.PostponedSince == NightAt(11) && later.PostponedReason == "pending-unsupported", "the postponement keeps its start and shows the latest reason");
			Check(!DueAt(NightAt(31), later).Attention && DueAt(NightAt(32), later).Attention, "amber once postponed 21 days past the soak");

			// After an update.
			var updated = SteamUpdateHoldPolicy.Updated(postponed, "200", NightAt(12), nightly: true);
			Check(updated.PendingVersion is null && updated.PostponedSince is null && updated.KnownGoodVersion == "200" && updated.InstalledVersion == "200"
				&& updated.LastOutcome == "updated" && updated.FailedFingerprint is null, "a passing update clears the pending version and the postponement");
			Check(!SteamUpdateHoldPolicy.Observe(updated, true, "200", "200", NightAt(13)).UpdatedDespiteHold, "after Core's update the version watch starts from the new version");

			// After a failed update: no retry until the fingerprint changes.
			var smoke = new SmokeResult(SmokeCheck.Plugins, "Awesome Mix");
			var failed = SteamUpdateHoldPolicy.Failed(seen.Next, smoke, fp, restored: true, "100", NightAt(11), nightly: true);
			Check(failed.FailedCheck == "plugins" && failed.FailedDetail == "Awesome Mix" && failed.LastOutcome == "rolled-back" && !failed.RollbackSuggested && failed.PendingVersion == "200",
				"a failed update is remembered with the failing check; the version stays pending");
			Check(DueAt(NightAt(12), failed).Phase == SteamHoldPhase.WaitingForChange
				&& SteamUpdateHoldPolicy.DecideNight(DueAt(NightAt(12), failed), failed, true, pass, NightAt(12)) == SteamNightAction.None, "the same fingerprint: no automatic retry");
			var changed = SteamUpdateHoldPolicy.View(failed, true, 10, "other", NightAt(12));
			Check(changed.Phase == SteamHoldPhase.Due && SteamUpdateHoldPolicy.DecideNight(changed, failed, true, pass, NightAt(12)) == SteamNightAction.Update,
				"a new compatibility file, Decky or pending version: retried");
			Check(SteamUpdateHoldPolicy.Failed(seen.Next, smoke, fp, restored: false, null, NightAt(11), nightly: false).RollbackSuggested, "a rollback that failed: Core Repair offers it");

			Check(SteamUpdateHoldPolicy.IsNewer("200", "100") && !SteamUpdateHoldPolicy.IsNewer("100", "100") && !SteamUpdateHoldPolicy.IsNewer(null, "1") && SteamUpdateHoldPolicy.IsNewer("1", null),
				"client versions compare as numbers");
			Check(SteamUpdateHoldPolicy.IsIdleMoment(true, true, false, false, noon, noon.AddMinutes(10)), "idle: Gaming Mode, Steam up, no game for 10 minutes");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(true, true, true, false, noon, noon.AddHours(3)), "never idle while a game runs");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(true, true, false, true, noon, noon.AddHours(3)), "never idle while a protected game runs");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(true, true, false, false, noon, noon.AddMinutes(9)), "not idle before 10 quiet minutes");
			Check(!SteamUpdateHoldPolicy.IsIdleMoment(false, true, false, false, noon, noon.AddHours(3)), "not idle in Desktop Mode");

			Check(SteamUpdateHoldPolicy.PublishedManifest(null).ToString() == "https://media.steampowered.com/client/steam_client_win64"
				&& SteamUpdateHoldPolicy.PublishedManifest("publicbeta").ToString() == "https://media.steampowered.com/client/steam_client_publicbeta_win64"
				&& SteamUpdateHoldPolicy.PublishedManifest("../x").ToString() == "https://media.steampowered.com/client/steam_client_win64", "Valve's manifest for the stable channel or an opted-in beta; odd beta names ignored");
			Check(SteamUpdateHoldPolicy.ParseClientManifestVersion("\"steam_client_win64\"\n{\n\t\"version\"\t\t\"1763083880\"\n\t\"bootstrapperversion\"\t\t\"1\"\n}") == "1763083880", "client manifest version is read");
			Check(SteamUpdateHoldPolicy.ParseClientManifestVersion("garbage") is null, "no version in a broken manifest");

			string root = Directory.CreateTempSubdirectory("core-steam-").FullName;
			Directory.CreateDirectory(Path.Combine(root, "package"));
			File.WriteAllText(Path.Combine(root, "package", "steam_client_win64.manifest"), "\"steam_client_win64\"\n{\n\t\"version\"\t\t\"42\"\n}");
			Check(SteamUpdateHoldPolicy.ReadClientVersion(root) == "42" && !SteamUpdateHoldPolicy.IsHoldFilePresent(root), "version read from the Steam folder; no hold file yet");
			File.WriteAllText(Path.Combine(root, "package", "beta"), "publicbeta\n");
			Check(SteamUpdateHoldPolicy.ReadBetaName(root) == "publicbeta", "the opted-in beta is read");
			string statePath = Path.Combine(root, "hold.json");
			SteamUpdateHoldPolicy.Save(statePath, failed with { RollbackSuggested = true });
			var loaded = SteamUpdateHoldPolicy.Load(statePath);
			Check(loaded == failed with { RollbackSuggested = true }, "hold state round-trips through its file (day one included)");
			File.WriteAllText(statePath, "{\"heldSince\":\"2026-01-01T00:00:00+00:00\",\"heldVersion\":\"1\",\"expiryNotified\":true}");
			Check(SteamUpdateHoldPolicy.Load(statePath).PendingVersion is null, "a state file from the old 30-day hold loads as a fresh state");
			File.WriteAllText(statePath, "{ broken");
			Check(SteamUpdateHoldPolicy.Load(statePath).InstalledVersion is null, "a broken state file starts fresh");
			Directory.Delete(root, true);
		}
		// Smoke test verdicts
		{
			var all = new SmokeExpectations(true, new[] { "Core", "Awesome Mix" });
			Check(SmokeTestPolicy.Judge(true, true, all, new[] { "core", "Awesome Mix", "Extra" }, true).Ok, "everything up: passes (names compare without case)");
			Check(SmokeTestPolicy.Judge(false, true, all, null, true).Failed == SmokeCheck.BigPicture, "no Big Picture: fails first on Big Picture");
			Check(SmokeTestPolicy.Judge(true, false, all, null, true).Failed == SmokeCheck.DeckyLoader, "loader silent: fails on the loader");
			var missing = SmokeTestPolicy.Judge(true, true, all, new[] { "Core" }, true);
			Check(missing.Failed == SmokeCheck.Plugins && missing.Detail == "Awesome Mix", "a plugin that loaded before and not now: named");
			Check(SmokeTestPolicy.Judge(true, true, all, null, true).Failed == SmokeCheck.Plugins, "Decky's plugin list unreadable: fails on plugins");
			Check(SmokeTestPolicy.Judge(true, true, all, new[] { "Core", "Awesome Mix" }, false).Failed == SmokeCheck.CorePlugin, "Core's plugin silent: fails on Core's plugin");
			Check(SmokeTestPolicy.Judge(true, false, SmokeExpectations.None, null, false).Ok, "a plugin layer that did not work before is not the update's fault");
			Check(SmokeTestPolicy.Judge(true, true, new SmokeExpectations(true, Array.Empty<string>()), null, true).Ok, "no plugin list from before: loader and Core's plugin decide");
			Check(SmokeTestPolicy.NoticeKey(SmokeCheck.Plugins, true) == "notice.steamRolledBack.plugins" && SmokeTestPolicy.NoticeKey(SmokeCheck.BigPicture, false) == "notice.steamRollbackFailed",
				"the notice names the failed check, or the failed rollback");
			foreach (string key in new[] { "notice.steamRolledBack.bigPicture", "notice.steamRolledBack.deckyLoader", "notice.steamRolledBack.plugins", "notice.steamRolledBack.corePlugin", "notice.steamRollbackFailed" })
				Check(CoreText.Entries.TryGetValue(key, out var texts) && texts.Length == CoreText.Languages.Length && texts.All(text => !string.IsNullOrWhiteSpace(text)), $"notice {key} exists in every language");
		}
		// Manual "Update Steam now"
		{
			var pass = new SteamGateVerdict(SteamGateResult.Pass, "ok");
			var cantTell = new SteamGateVerdict(SteamGateResult.CantTell, "pending-unknown");
			var no = new SteamGateVerdict(SteamGateResult.Incompatible, "pending-unsupported");
			Check(SteamManualPolicy.Decide(pass, 0) == SteamManualAnswer.Go, "gate passes: goes at once");
			Check(SteamManualPolicy.Decide(cantTell, 0) == SteamManualAnswer.ConfirmCantTell && SteamManualPolicy.Decide(cantTell, 1) == SteamManualAnswer.Go, "can't tell: one confirmation overrides");
			Check(SteamManualPolicy.Decide(no, 0) == SteamManualAnswer.ConfirmIncompatible && SteamManualPolicy.Decide(no, 1) == SteamManualAnswer.ConfirmIncompatibleAgain
				&& SteamManualPolicy.Decide(no, 2) == SteamManualAnswer.Go, "known incompatible: needs a second confirmation");
		}
		// Steam update job
		{
			var happy = new FakeSteamHost();
			Check((await SteamUpdateJob.RunAsync(happy, CancellationToken.None)).Outcome == SteamUpdateOutcome.Updated, "update: smoke test passes");
			Check(string.Join(",", happy.Calls) == "expect,stop,backup,remove-hold,start,smoke,apply-hold", "update order: note what works, stop, back up, release hold, start, smoke test, hold again");

			var playing = new FakeSteamHost { Playing = true };
			Check((await SteamUpdateJob.RunAsync(playing, CancellationToken.None)).Outcome == SteamUpdateOutcome.NotWhilePlaying && playing.Calls.Count == 0, "never updates during a game");

			var noBackup = new FakeSteamHost { BackupOk = false };
			Check((await SteamUpdateJob.RunAsync(noBackup, CancellationToken.None)).Outcome == SteamUpdateOutcome.BackupFailed
				&& string.Join(",", noBackup.Calls) == "expect,stop,backup,start", "no backup: Steam comes back on the held version, the hold is never released");

			var stubborn = new FakeSteamHost { StopOk = false };
			Check((await SteamUpdateJob.RunAsync(stubborn, CancellationToken.None)).Outcome == SteamUpdateOutcome.SteamWouldNotClose && !stubborn.Calls.Contains("remove-hold"), "Steam that will not close is left as it was");

			var broken = new FakeSteamHost { Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.Plugins, "X"), new SmokeResult(SmokeCheck.Plugins, "X") }) };
			var brokenResult = await SteamUpdateJob.RunAsync(broken, CancellationToken.None);
			Check(brokenResult.Outcome == SteamUpdateOutcome.RolledBack && brokenResult.Smoke.Failed == SmokeCheck.Plugins
				&& string.Join(",", broken.Calls) == "expect,stop,backup,remove-hold,start,smoke,apply-hold,restart-plugins,smoke,stop,apply-hold,restore,start",
				"failed smoke test: one plugin restart, then roll back (hold first) and report the failing check");
			var recovered = new FakeSteamHost { Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.CorePlugin, null), SmokeResult.Passed }) };
			Check((await SteamUpdateJob.RunAsync(recovered, CancellationToken.None)).Outcome == SteamUpdateOutcome.Updated, "plugins that come back after a restart count as updated");
			var noPicture = new FakeSteamHost { Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.BigPicture, null) }) };
			var noPictureResult = await SteamUpdateJob.RunAsync(noPicture, CancellationToken.None);
			Check(noPictureResult.Outcome == SteamUpdateOutcome.RolledBack && !noPicture.Calls.Contains("restart-plugins"), "no Big Picture: rolled back without a plugin restart");
			var noRestore = new FakeSteamHost { RestoreOk = false, Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.BigPicture, null) }) };
			Check((await SteamUpdateJob.RunAsync(noRestore, CancellationToken.None)).Outcome == SteamUpdateOutcome.RollbackFailed && noRestore.Calls.Last() == "start", "a failed restore is reported and Steam is still started");
			var neverHad = new FakeSteamHost { Expected = SmokeExpectations.None, Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.DeckyLoader, null) }) };
			Check((await SteamUpdateJob.RunAsync(neverHad, CancellationToken.None)).Outcome == SteamUpdateOutcome.RolledBack && !neverHad.Calls.Contains("restart-plugins"),
				"without plugin expectations no plugin restart is tried");
			var noStart = new FakeSteamHost { StartOk = false };
			Check((await SteamUpdateJob.RunAsync(noStart, CancellationToken.None)).Outcome == SteamUpdateOutcome.SteamDidNotStart && noStart.Calls.Last() == "apply-hold", "Steam that does not start: the hold goes back");

			var verified = new FakeSteamHost();
			Check((await SteamUpdateJob.VerifyAsync(verified, verified.Expected, CancellationToken.None)).Outcome == SteamUpdateOutcome.Updated
				&& string.Join(",", verified.Calls) == "smoke", "unexpected update that works: only the smoke test");
			var badSurprise = new FakeSteamHost { Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.DeckyLoader, null), new SmokeResult(SmokeCheck.DeckyLoader, null) }) };
			Check((await SteamUpdateJob.VerifyAsync(badSurprise, badSurprise.Expected, CancellationToken.None)).Outcome == SteamUpdateOutcome.RolledBack
				&& string.Join(",", badSurprise.Calls) == "smoke,restart-plugins,smoke,stop,apply-hold,restore,start", "unexpected update that breaks plugins: rolled back to the newest backup");
			var noBackupSurprise = new FakeSteamHost { HasBackup = false, Smoke = new Queue<SmokeResult>(new[] { new SmokeResult(SmokeCheck.BigPicture, null) }) };
			Check((await SteamUpdateJob.VerifyAsync(noBackupSurprise, noBackupSurprise.Expected, CancellationToken.None)).Outcome == SteamUpdateOutcome.RollbackFailed
				&& !noBackupSurprise.Calls.Contains("stop"), "unexpected update without a backup: reported, Steam left running");

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
	internal bool Playing, StopOk = true, StartOk = true, BackupOk = true, RestoreOk = true, HasBackup = true;
	internal SmokeExpectations Expected = new(true, new[] { "Core" });
	internal Queue<SmokeResult> Smoke = new(new[] { SmokeResult.Passed });
	public bool GameRunning() => Playing;
	public Task<bool> StopSteamAsync(CancellationToken cancellation) { Calls.Add("stop"); return Task.FromResult(StopOk); }
	public Task<bool> StartSteamAsync(CancellationToken cancellation) { Calls.Add("start"); return Task.FromResult(StartOk); }
	public Task<bool> BackupClientAsync(CancellationToken cancellation) { Calls.Add("backup"); return Task.FromResult(BackupOk); }
	public bool HasClientBackup() => HasBackup;
	public Task<bool> RestoreNewestBackupAsync(CancellationToken cancellation) { Calls.Add("restore"); return Task.FromResult(RestoreOk); }
	public bool RemoveHold() { Calls.Add("remove-hold"); return true; }
	public bool ApplyHold() { Calls.Add("apply-hold"); return true; }
	public Task<SmokeExpectations> ExpectationsAsync(CancellationToken cancellation) { Calls.Add("expect"); return Task.FromResult(Expected); }
	public Task<SmokeResult> SmokeTestAsync(DateTimeOffset since, TimeSpan limit, SmokeExpectations expected, CancellationToken cancellation)
	{ Calls.Add("smoke"); return Task.FromResult(Smoke.Count > 0 ? Smoke.Dequeue() : SmokeResult.Passed); }
	public Task RestartPluginsAsync(CancellationToken cancellation) { Calls.Add("restart-plugins"); return Task.CompletedTask; }
	public DateTimeOffset Now() => DateTimeOffset.UnixEpoch;
	public void Stage(SteamUpdateStage stage) { }
}
