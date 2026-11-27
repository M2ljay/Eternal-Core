using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Playhub.Shared;

namespace GamingMode.Services;

// Eternal Core: when Steam may update (pure policy, the release gate, the smoke test and the
// update job's orchestration; Windows wiring is in MarkIIIHost). Policy: docs/eternal/STEAM-UPDATES.md.
//
// The block is Playhub's existing one: steam.cfg with BootStrapperInhibitAll=enable in the Steam
// folder stops the client's self-update at launch. Valve does not document it, and it is not
// confirmed for the 64-bit client, so nothing here depends on it working: the installed client
// version is watched, and an update that happens anyway gets the same smoke test and rollback.
//
// 1. Soak per version. The first time Core sees a pending Steam client version, that update night
//    is day one. Steam may update only once that version has waited SoakDays (10 by default, config
//    "SteamUpdateSoakDays"), on an update night (UpdateNight.cs: 05:00-07:00, idle, no game). A newer
//    pending version restarts its own count.
// 2. Gate before updating (SteamReleaseGate): the shipped SteamCompatibility.json and the running
//    client's kind must show that the pending client is one the installed Decky supports. "No" or
//    "can't tell" postpones; the gate is asked again every update night. Core Health shows the
//    postponement, amber once it is PostponeAttentionDays past the soak.
// 3. Safety net (SteamUpdateJob): backup first, update, a time-boxed smoke test (Steam and Big
//    Picture, Decky's loader, every plugin that loaded before, Core's own plugin). A failure rolls
//    back to the backup, holds again and raises a Core notice naming the check. A failed update is
//    not retried until the compatibility file, the bundled or installed Decky, or the pending
//    version changes (the fingerprint).
// 4. "Update Steam now" (desktop app, Quick Access, Core Repair) uses the same safety net. It may
//    override "can't tell" with one confirmation; "known incompatible" needs a second one.
// 5. An update that happened despite the block runs the same smoke test, with rollback.
internal sealed record SteamHoldState
{
	// The version watch: the installed client version last seen.
	public string? InstalledVersion { get; init; }
	// The last version that passed its smoke test (for the log and Core Repair).
	public string? KnownGoodVersion { get; init; }
	// The pending client version Valve offers, and the update night on which Core first saw it (day one).
	public string? PendingVersion { get; init; }
	public DateOnly? PendingDayOne { get; init; }
	public DateTimeOffset? PendingCheckedAt { get; init; }
	// The gate said no (or could not tell) on an update night: since when, and why (a gate reason code).
	public DateTimeOffset? PostponedSince { get; init; }
	public string? PostponedReason { get; init; }
	// The last update night on which the agent decided (one decision per night).
	public DateTimeOffset? LastNightDecision { get; init; }
	public DateTimeOffset? LastUpdateAt { get; init; }
	public string? LastOutcome { get; init; }
	// The last update failed its smoke test: which check, what exactly, and the fingerprint it failed
	// under. Automatic retries wait for a different fingerprint.
	public string? FailedCheck { get; init; }
	public string? FailedDetail { get; init; }
	public string? FailedFingerprint { get; init; }
	// The failed update could not be rolled back: Core Repair offers the rollback first.
	public bool RollbackSuggested { get; init; }
}

internal enum SteamHoldPhase { Off, Current, Soaking, Due, Postponed, WaitingForChange }

internal readonly record struct SteamHoldObservation(SteamHoldState Next, bool UpdatedDespiteHold);

internal readonly record struct SteamHoldView(SteamHoldPhase Phase, int DaysLeft, DateOnly? SoakEnds, bool Attention);

// ---------- release gate ----------

internal enum SteamGateResult { Pass, CantTell, Incompatible }

// Reason codes (Core Health, the app and the log): "ok", "no-decky", "no-compatibility-file",
// "decky-unknown", "file-disagrees", "pending-unknown", "pending-unsupported".
internal readonly record struct SteamGateVerdict(SteamGateResult Result, string Reason);

internal static class SteamReleaseGate
{
	// deckyInstalled false: no plugin layer to break. installedDecky null: Decky present but its version
	// unreadable, so the bundled one is assumed. runningClient: what the CEF probe saw in the running Steam.
	internal static SteamGateVerdict Evaluate(SteamCompatibility? compatibility, bool deckyInstalled, Version? installedDecky,
		string? installedSteam, SteamUiKind runningClient, string? pendingSteam)
	{
		if (!deckyInstalled) return new(SteamGateResult.Pass, "no-decky");
		if (compatibility is null) return new(SteamGateResult.CantTell, "no-compatibility-file");
		SteamUiKind decky = installedDecky is not null ? compatibility.ClientForDecky(installedDecky) : compatibility.BundledDeckyClient;
		if (decky == SteamUiKind.Unknown) return new(SteamGateResult.CantTell, "decky-unknown");
		// The detection checks the file: if the running client is not what the file says this version
		// is, the file cannot be trusted for the pending one either.
		SteamUiKind listed = compatibility.ClientForSteam(installedSteam);
		if (runningClient != SteamUiKind.Unknown && listed != SteamUiKind.Unknown && runningClient != listed)
			return new(SteamGateResult.CantTell, "file-disagrees");
		return DeckySteamPairing.Judge(decky, compatibility.ClientForSteam(pendingSteam)) switch
		{
			PairingResult.Compatible => new(SteamGateResult.Pass, "ok"),
			PairingResult.Incompatible => new(SteamGateResult.Incompatible, "pending-unsupported"),
			_ => new(SteamGateResult.CantTell, "pending-unknown"),
		};
	}

	// A failed update is retried only once one of these changes: the compatibility file (which names the
	// bundled Decky), the installed Decky, or the pending Steam version.
	internal static string Fingerprint(SteamCompatibility? compatibility, Version? installedDecky, string? pendingSteam) =>
		$"{compatibility?.Fingerprint ?? "none"}|{installedDecky?.ToString() ?? "none"}|{pendingSteam ?? "none"}";
}

// ---------- manual "Update Steam now" ----------

internal enum SteamManualAnswer { Go, ConfirmCantTell, ConfirmIncompatible, ConfirmIncompatibleAgain }

internal static class SteamManualPolicy
{
	// confirmations: how many times the user has confirmed (0 = first press).
	internal static SteamManualAnswer Decide(SteamGateVerdict gate, int confirmations) => gate.Result switch
	{
		SteamGateResult.Pass => SteamManualAnswer.Go,
		SteamGateResult.CantTell => confirmations >= 1 ? SteamManualAnswer.Go : SteamManualAnswer.ConfirmCantTell,
		_ => confirmations >= 2 ? SteamManualAnswer.Go : confirmations == 1 ? SteamManualAnswer.ConfirmIncompatibleAgain : SteamManualAnswer.ConfirmIncompatible,
	};

	internal static string Code(SteamManualAnswer answer) => answer switch
	{
		SteamManualAnswer.ConfirmCantTell => "confirm-cant-tell",
		SteamManualAnswer.ConfirmIncompatible => "confirm-incompatible",
		SteamManualAnswer.ConfirmIncompatibleAgain => "confirm-incompatible-again",
		_ => "go",
	};
}

// ---------- automatic update on update night ----------

internal enum SteamNightAction { None, Postpone, Update }

internal static class SteamUpdateHoldPolicy
{
	internal const int DefaultSoakDays = 10, MinSoakDays = 1, MaxSoakDays = 60;
	internal const int PostponeAttentionDays = 21;
	internal const int IdleMinutes = 10;
	internal const string HoldFileName = "steam.cfg";
	internal const string HoldFileContent = "BootStrapperInhibitAll=enable\r\n";

	internal static int SoakDays(int? configured) => configured is int days ? Math.Clamp(days, MinSoakDays, MaxSoakDays) : DefaultSoakDays;

	// The update night a moment belongs to, named by the morning it ends on: until 07:00 it is tonight's
	// (this morning's) window, after that the next one. "That night is day one" uses this.
	internal static DateOnly NightOf(DateTimeOffset now)
	{
		var day = DateOnly.FromDateTime(now.DateTime);
		return now.Hour < UpdateNightPolicy.GiveUpHour ? day : day.AddDays(1);
	}

	internal static bool IsUpdateNight(DateTimeOffset now) => now.Hour >= UpdateNightPolicy.RestartHour && now.Hour < UpdateNightPolicy.GiveUpHour;

	internal static bool DecidedTonight(SteamHoldState state, DateTimeOffset now) =>
		state.LastNightDecision is { } last && IsUpdateNight(last) && NightOf(last) == NightOf(now);

	// Steam client versions are build numbers: a pending version not newer than the installed one is no update.
	internal static bool IsNewer(string? candidate, string? installed)
	{
		if (string.IsNullOrEmpty(candidate)) return false;
		if (string.IsNullOrEmpty(installed)) return true;
		if (ulong.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out ulong a)
			&& ulong.TryParse(installed, NumberStyles.None, CultureInfo.InvariantCulture, out ulong b)) return a > b;
		return !string.Equals(candidate, installed, StringComparison.Ordinal);
	}

	private static SteamHoldState ClearPending(SteamHoldState state) =>
		state with { PendingVersion = null, PendingDayOne = null, PostponedSince = null, PostponedReason = null };

	// installed/pending null: not readable this round (keep what is known). coreUpdating: Core itself is
	// changing the client right now, so a new installed version is not "despite the block".
	internal static SteamHoldObservation Observe(SteamHoldState state, bool holdOn, string? installed, string? pending, DateTimeOffset now, bool coreUpdating = false)
	{
		SteamHoldState next = state;
		bool bypassed = false;
		if (!string.IsNullOrEmpty(installed))
		{
			if (holdOn && !coreUpdating && !string.IsNullOrEmpty(state.InstalledVersion) && !string.Equals(installed, state.InstalledVersion, StringComparison.Ordinal))
				bypassed = true;
			if (!string.Equals(installed, next.InstalledVersion, StringComparison.Ordinal)) next = next with { InstalledVersion = installed };
		}
		string? current = next.InstalledVersion;
		if (!string.IsNullOrEmpty(pending))
		{
			if (!IsNewer(pending, current)) { if (next.PendingVersion is not null) next = ClearPending(next); }
			else if (!string.Equals(pending, next.PendingVersion, StringComparison.Ordinal))
				// A new pending version: its own soak starts tonight, and an earlier postponement no longer applies.
				next = next with { PendingVersion = pending, PendingDayOne = NightOf(now), PostponedSince = null, PostponedReason = null };
		}
		else if (next.PendingVersion is not null && !string.IsNullOrEmpty(current) && !IsNewer(next.PendingVersion, current))
			next = ClearPending(next); // the installed client caught up (an update, by Core or by Steam)
		return new(next, bypassed);
	}

	internal static SteamHoldView View(SteamHoldState state, bool holdOn, int soakDays, string fingerprint, DateTimeOffset now)
	{
		if (!holdOn) return new(SteamHoldPhase.Off, 0, null, false);
		if (state.PendingVersion is null || state.PendingDayOne is not { } dayOne) return new(SteamHoldPhase.Current, 0, null, false);
		DateOnly soakEnds = dayOne.AddDays(soakDays);
		DateOnly night = NightOf(now);
		if (night < soakEnds) return new(SteamHoldPhase.Soaking, soakEnds.DayNumber - night.DayNumber, soakEnds, false);
		bool attention = night.DayNumber - soakEnds.DayNumber >= PostponeAttentionDays;
		if (state.FailedFingerprint is not null && state.FailedFingerprint == fingerprint) return new(SteamHoldPhase.WaitingForChange, 0, soakEnds, attention);
		if (state.PostponedSince is not null) return new(SteamHoldPhase.Postponed, 0, soakEnds, attention);
		return new(SteamHoldPhase.Due, 0, soakEnds, false);
	}

	// Once per update night, only after the soak, only while idle (retried through the night until 07:00).
	internal static SteamNightAction DecideNight(SteamHoldView view, SteamHoldState state, bool idle, SteamGateVerdict gate, DateTimeOffset now)
	{
		if (view.Phase is not (SteamHoldPhase.Due or SteamHoldPhase.Postponed)) return SteamNightAction.None;
		if (!IsUpdateNight(now) || DecidedTonight(state, now) || !idle) return SteamNightAction.None;
		return gate.Result == SteamGateResult.Pass ? SteamNightAction.Update : SteamNightAction.Postpone;
	}

	internal static SteamHoldState Postponed(SteamHoldState state, string reason, DateTimeOffset now) =>
		state with { PostponedSince = state.PostponedSince ?? now, PostponedReason = reason, LastNightDecision = now };

	// After an update that passed its smoke test.
	internal static SteamHoldState Updated(SteamHoldState state, string? version, DateTimeOffset now, bool nightly)
	{
		SteamHoldState next = state with
		{
			InstalledVersion = version ?? state.InstalledVersion, KnownGoodVersion = version ?? state.KnownGoodVersion, LastUpdateAt = now, LastOutcome = "updated",
			FailedCheck = null, FailedDetail = null, FailedFingerprint = null, RollbackSuggested = false,
			LastNightDecision = nightly ? now : state.LastNightDecision,
		};
		return IsNewer(next.PendingVersion, next.InstalledVersion) ? next : ClearPending(next);
	}

	// After a smoke-test failure: rolled back (or not), held again, and remembered under its fingerprint.
	internal static SteamHoldState Failed(SteamHoldState state, SmokeResult smoke, string fingerprint, bool restored, string? installedNow, DateTimeOffset now, bool nightly) => state with
	{
		InstalledVersion = installedNow ?? state.InstalledVersion, LastUpdateAt = now, LastOutcome = restored ? "rolled-back" : "rollback-failed",
		FailedCheck = SmokeTestPolicy.Code(smoke.Failed), FailedDetail = smoke.Detail, FailedFingerprint = fingerprint, RollbackSuggested = !restored,
		LastNightDecision = nightly ? now : state.LastNightDecision,
	};

	// Idle: Gaming Mode, Steam running, no game (or protected game) for IdleMinutes. The
	// clock starts at agent start too, so a fresh boot does not update straight away.
	internal static bool IsIdleMoment(bool gamingMode, bool steamRunning, bool gameRunning, bool protectedGame,
		DateTimeOffset quietSince, DateTimeOffset now) =>
		gamingMode && steamRunning && !gameRunning && !protectedGame && now - quietSince >= TimeSpan.FromMinutes(IdleMinutes);

	private static readonly Regex ManifestVersion = new("\"version\"\\s+\"(?<value>[0-9]{1,20})\"", RegexOptions.CultureInvariant);

	// Steam\package\steam_client_win64.manifest (or _win32), and Valve's published copy: "version"  "1763083880".
	internal static string? ParseClientManifestVersion(string? text)
	{
		Match match = ManifestVersion.Match(text ?? "");
		return match.Success ? match.Groups["value"].Value : null;
	}

	internal static string? ReadClientVersion(string? steamRoot)
	{
		if (string.IsNullOrWhiteSpace(steamRoot)) return null;
		foreach (string name in new[] { "steam_client_win64.manifest", "steam_client_win32.manifest" })
		{
			try
			{
				string path = Path.Combine(steamRoot, "package", name);
				if (File.Exists(path) && ParseClientManifestVersion(File.ReadAllText(path)) is { } version) return version;
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		}
		return null;
	}

	private static readonly Regex BetaName = new(@"\A[A-Za-z0-9_]{1,64}\z", RegexOptions.CultureInvariant);

	// Where Valve publishes the client manifest the bootstrapper would download: steam_client_win64 for
	// the stable channel, steam_client_<beta>_win64 for an opted-in beta (Steam\package\beta). Unverified
	// against the 64-bit bootstrapper; a failed fetch only means "pending version unknown".
	internal static Uri PublishedManifest(string? betaName)
	{
		string beta = (betaName ?? "").Trim();
		string name = beta.Length > 0 && BetaName.IsMatch(beta) ? $"steam_client_{beta}_win64" : "steam_client_win64";
		return new Uri("https://media.steampowered.com/client/" + name);
	}

	internal static string? ReadBetaName(string? steamRoot)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(steamRoot)) return null;
			string path = Path.Combine(steamRoot, "package", "beta");
			return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
	}

	internal static bool IsHoldFilePresent(string? steamRoot)
	{
		try { return !string.IsNullOrWhiteSpace(steamRoot) && File.Exists(Path.Combine(steamRoot, HoldFileName)); }
		catch { return false; }
	}

	// ---------- state file ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static SteamHoldState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<SteamHoldState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return new(); }
	}

	internal static void Save(string path, SteamHoldState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}

// ---------- smoke test ----------

internal enum SmokeCheck { None, BigPicture, DeckyLoader, Plugins, CorePlugin }

internal readonly record struct SmokeResult(SmokeCheck Failed, string? Detail)
{
	internal bool Ok => Failed == SmokeCheck.None;
	internal static SmokeResult Passed => new(SmokeCheck.None, null);
}

// What must work after the update: nothing of the plugin layer if it did not work before (a Decky that
// was already broken, or none, is not the update's fault), otherwise the loader, Core's own plugin and
// every plugin Decky had loaded before (a plugin the user disabled is not expected back).
internal sealed record SmokeExpectations(bool PluginsMatter, IReadOnlyCollection<string> Plugins)
{
	internal static SmokeExpectations None { get; } = new(false, Array.Empty<string>());
}

internal static class SmokeTestPolicy
{
	internal static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);

	// In order: Steam with Big Picture, Decky's loader answering, every expected plugin loaded as Decky
	// reports it, Core's own plugin answering.
	internal static SmokeResult Judge(bool bigPicture, bool deckyLoader, SmokeExpectations expected, IReadOnlyCollection<string>? loadedPlugins, bool corePlugin)
	{
		if (!bigPicture) return new(SmokeCheck.BigPicture, null);
		if (!expected.PluginsMatter) return SmokeResult.Passed;
		if (!deckyLoader) return new(SmokeCheck.DeckyLoader, null);
		if (expected.Plugins.Count > 0)
		{
			if (loadedPlugins is null) return new(SmokeCheck.Plugins, "Decky's plugin list could not be read");
			var loaded = new HashSet<string>(loadedPlugins, StringComparer.OrdinalIgnoreCase);
			string[] missing = expected.Plugins.Where(name => !loaded.Contains(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
			if (missing.Length > 0) return new(SmokeCheck.Plugins, string.Join(", ", missing));
		}
		return corePlugin ? SmokeResult.Passed : new(SmokeCheck.CorePlugin, null);
	}

	internal static string Code(SmokeCheck check) => check switch
	{
		SmokeCheck.BigPicture => "big-picture",
		SmokeCheck.DeckyLoader => "decky-loader",
		SmokeCheck.Plugins => "plugins",
		SmokeCheck.CorePlugin => "core-plugin",
		_ => "none",
	};

	// The Core notice that names what failed (CoreText keys).
	internal static string NoticeKey(SmokeCheck check, bool restored) => !restored ? "notice.steamRollbackFailed" : check switch
	{
		SmokeCheck.BigPicture => "notice.steamRolledBack.bigPicture",
		SmokeCheck.DeckyLoader => "notice.steamRolledBack.deckyLoader",
		SmokeCheck.Plugins => "notice.steamRolledBack.plugins",
		_ => "notice.steamRolledBack.corePlugin",
	};
}

// ---------- the update, verify and rollback jobs ----------

internal enum SteamUpdateStage { Checking, ClosingSteam, BackingUp, Updating, CheckingPlugins, RestartingPlugins, RollingBack, Finished }

internal enum SteamUpdateOutcome { Updated, RolledBack, RollbackFailed, NotWhilePlaying, SteamWouldNotClose, BackupFailed, SteamDidNotStart }

internal readonly record struct SteamUpdateResult(SteamUpdateOutcome Outcome, SmokeResult Smoke);

internal enum SteamRollbackOutcome { RolledBack, NoBackup, NotWhilePlaying, SteamWouldNotClose, RestoreFailed, SteamDidNotStart }

internal interface ISteamUpdateHost
{
	bool GameRunning();
	Task<bool> StopSteamAsync(CancellationToken cancellation);
	Task<bool> StartSteamAsync(CancellationToken cancellation);
	Task<bool> BackupClientAsync(CancellationToken cancellation);
	bool HasClientBackup();
	Task<bool> RestoreNewestBackupAsync(CancellationToken cancellation);
	bool RemoveHold();
	bool ApplyHold();
	// Read while Steam still runs, before anything changes (SmokeExpectations).
	Task<SmokeExpectations> ExpectationsAsync(CancellationToken cancellation);
	// Polls until every check passes or the time limit ends; returns the first check still failing.
	Task<SmokeResult> SmokeTestAsync(DateTimeOffset since, TimeSpan limit, SmokeExpectations expected, CancellationToken cancellation);
	Task RestartPluginsAsync(CancellationToken cancellation);
	DateTimeOffset Now();
	void Stage(SteamUpdateStage stage);
}

internal static class SteamUpdateJob
{
	internal static readonly TimeSpan UpdateWindow = SmokeTestPolicy.Limit;
	internal static readonly TimeSpan RecheckWindow = TimeSpan.FromMinutes(2);

	internal static async Task<SteamUpdateResult> RunAsync(ISteamUpdateHost host, CancellationToken cancellation)
	{
		host.Stage(SteamUpdateStage.Checking);
		if (host.GameRunning()) return new(SteamUpdateOutcome.NotWhilePlaying, SmokeResult.Passed);
		SmokeExpectations expected = await host.ExpectationsAsync(cancellation).ConfigureAwait(false);
		host.Stage(SteamUpdateStage.ClosingSteam);
		if (!await host.StopSteamAsync(cancellation).ConfigureAwait(false))
		{
			await host.StartSteamAsync(cancellation).ConfigureAwait(false);
			return new(SteamUpdateOutcome.SteamWouldNotClose, SmokeResult.Passed);
		}
		host.Stage(SteamUpdateStage.BackingUp);
		// Never update without a way back.
		if (!await host.BackupClientAsync(cancellation).ConfigureAwait(false))
		{
			await host.StartSteamAsync(cancellation).ConfigureAwait(false);
			return new(SteamUpdateOutcome.BackupFailed, SmokeResult.Passed);
		}
		host.Stage(SteamUpdateStage.Updating);
		host.RemoveHold();
		DateTimeOffset started = host.Now();
		bool steamStarted;
		try { steamStarted = await host.StartSteamAsync(cancellation).ConfigureAwait(false); }
		catch { host.ApplyHold(); throw; }
		if (!steamStarted)
		{
			host.ApplyHold();
			return new(SteamUpdateOutcome.SteamDidNotStart, SmokeResult.Passed);
		}
		host.Stage(SteamUpdateStage.CheckingPlugins);
		// The hold goes back only after this wait: the bootstrapper reads steam.cfg when it
		// starts, and an update restarts Steam once, inside this window.
		SmokeResult smoke = await host.SmokeTestAsync(started, UpdateWindow, expected, cancellation).ConfigureAwait(false);
		host.ApplyHold();
		smoke = await RetryOnceAsync(host, smoke, expected, cancellation).ConfigureAwait(false);
		if (smoke.Ok) { host.Stage(SteamUpdateStage.Finished); return new(SteamUpdateOutcome.Updated, smoke); }
		bool restored = await RestoreAsync(host, cancellation).ConfigureAwait(false);
		return new(restored ? SteamUpdateOutcome.RolledBack : SteamUpdateOutcome.RollbackFailed, smoke);
	}

	// Steam updated by itself despite the hold (version watch): the same smoke test, and the same
	// rollback to the newest backup when it fails and a backup exists.
	internal static async Task<SteamUpdateResult> VerifyAsync(ISteamUpdateHost host, SmokeExpectations expected, CancellationToken cancellation)
	{
		host.Stage(SteamUpdateStage.CheckingPlugins);
		SmokeResult smoke = await host.SmokeTestAsync(host.Now(), RecheckWindow, expected, cancellation).ConfigureAwait(false);
		smoke = await RetryOnceAsync(host, smoke, expected, cancellation).ConfigureAwait(false);
		if (smoke.Ok) { host.Stage(SteamUpdateStage.Finished); return new(SteamUpdateOutcome.Updated, smoke); }
		if (host.GameRunning()) return new(SteamUpdateOutcome.NotWhilePlaying, smoke);
		if (!host.HasClientBackup()) { host.Stage(SteamUpdateStage.Finished); return new(SteamUpdateOutcome.RollbackFailed, smoke); }
		bool restored = await RestoreAsync(host, cancellation).ConfigureAwait(false);
		return new(restored ? SteamUpdateOutcome.RolledBack : SteamUpdateOutcome.RollbackFailed, smoke);
	}

	// One plugin restart before giving up: a loader that came up before Steam's UI often recovers.
	private static async Task<SmokeResult> RetryOnceAsync(ISteamUpdateHost host, SmokeResult smoke, SmokeExpectations expected, CancellationToken cancellation)
	{
		if (smoke.Ok || !expected.PluginsMatter || smoke.Failed == SmokeCheck.BigPicture) return smoke;
		host.Stage(SteamUpdateStage.RestartingPlugins);
		DateTimeOffset restarted = host.Now();
		await host.RestartPluginsAsync(cancellation).ConfigureAwait(false);
		return await host.SmokeTestAsync(restarted, RecheckWindow, expected, cancellation).ConfigureAwait(false);
	}

	private static async Task<bool> RestoreAsync(ISteamUpdateHost host, CancellationToken cancellation)
	{
		host.Stage(SteamUpdateStage.RollingBack);
		bool stopped = await host.StopSteamAsync(cancellation).ConfigureAwait(false);
		// Hold first: a restored client must not update itself straight back.
		host.ApplyHold();
		bool restored = stopped && await host.RestoreNewestBackupAsync(cancellation).ConfigureAwait(false);
		await host.StartSteamAsync(cancellation).ConfigureAwait(false);
		host.Stage(SteamUpdateStage.Finished);
		return restored;
	}

	internal static async Task<SteamRollbackOutcome> RollbackAsync(ISteamUpdateHost host, CancellationToken cancellation)
	{
		if (host.GameRunning()) return SteamRollbackOutcome.NotWhilePlaying;
		if (!host.HasClientBackup()) return SteamRollbackOutcome.NoBackup;
		host.Stage(SteamUpdateStage.ClosingSteam);
		if (!await host.StopSteamAsync(cancellation).ConfigureAwait(false))
		{
			await host.StartSteamAsync(cancellation).ConfigureAwait(false);
			return SteamRollbackOutcome.SteamWouldNotClose;
		}
		// Hold first: a restored client must not update itself straight back.
		host.ApplyHold();
		bool restored = await host.RestoreNewestBackupAsync(cancellation).ConfigureAwait(false);
		bool started = await host.StartSteamAsync(cancellation).ConfigureAwait(false);
		host.Stage(SteamUpdateStage.Finished);
		if (!restored) return SteamRollbackOutcome.RestoreFailed;
		return started ? SteamRollbackOutcome.RolledBack : SteamRollbackOutcome.SteamDidNotStart;
	}
}
