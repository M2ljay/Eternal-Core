using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

// Eternal Core Mark III: "hold Steam updates until you choose" (pure policy and the update
// job's orchestration; Windows wiring is in SteamUpdateHoldService.cs).
//
// The mechanism is Playhub's existing one: steam.cfg with BootStrapperInhibitAll=enable in
// the Steam folder stops the client's self-update at launch. Valve does not document it;
// it is community knowledge and not confirmed for the 64-bit client that shipped in
// December 2025. So nothing here depends on it working:
// - while held, the client version is watched; if Steam updates anyway, the hold restarts
//   from the new version and the Decky health check (DeckyHeartbeat) judges the result;
// - holding never stops Steam from starting, and a broken plugin layer never stops Core.
//
// A hold lasts at most MaxHoldDays. After that Core says so once and updates at the next
// idle moment (Gaming Mode, Steam up, no game for IdleMinutes), never during a game.
internal sealed record SteamHoldState
{
	public DateTimeOffset? HeldSince { get; init; }
	public string? HeldVersion { get; init; }
	public string? KnownGoodVersion { get; init; }
	public DateTimeOffset? LastUpdateAt { get; init; }
	public string? LastOutcome { get; init; }
	public bool ExpiryNotified { get; init; }
	// The last update left plugins broken: Core Repair offers the rollback first.
	public bool RollbackSuggested { get; init; }
}

internal enum SteamHoldPhase { Off, Holding, Expired }

internal readonly record struct SteamHoldObservation(SteamHoldState Next, SteamHoldPhase Phase, int DaysLeft, bool UpdatedDespiteHold);

internal static class SteamUpdateHoldPolicy
{
	internal const int MaxHoldDays = 30;
	internal const int IdleMinutes = 10;
	internal const string HoldFileName = "steam.cfg";
	internal const string HoldFileContent = "BootStrapperInhibitAll=enable\r\n";

	internal static SteamHoldObservation Observe(SteamHoldState state, bool holdFilePresent, string? clientVersion, DateTimeOffset now)
	{
		if (!holdFilePresent)
		{
			// Hold switched off (or the file was removed by a Steam reinstall): nothing to
			// count. The known-good version is kept for the health check and the log.
			SteamHoldState off = state.HeldSince is null && !state.ExpiryNotified ? state
				: state with { HeldSince = null, HeldVersion = null, ExpiryNotified = false };
			return new(off, SteamHoldPhase.Off, 0, false);
		}
		SteamHoldState next = state;
		bool bypassed = false;
		if (next.HeldSince is null)
			next = next with { HeldSince = now, HeldVersion = clientVersion, ExpiryNotified = false };
		else if (!string.IsNullOrEmpty(clientVersion) && !string.IsNullOrEmpty(next.HeldVersion)
			&& !string.Equals(clientVersion, next.HeldVersion, StringComparison.Ordinal))
		{
			// Steam updated although steam.cfg was present: the block did not hold. Start a
			// fresh hold from what is installed now; the health check decides if it is good.
			bypassed = true;
			next = next with { HeldSince = now, HeldVersion = clientVersion, ExpiryNotified = false };
		}
		else if (string.IsNullOrEmpty(next.HeldVersion) && !string.IsNullOrEmpty(clientVersion))
			next = next with { HeldVersion = clientVersion };
		TimeSpan held = now - next.HeldSince!.Value;
		if (held < TimeSpan.Zero) held = TimeSpan.Zero; // clock moved back: never expire early
		int daysLeft = Math.Max(0, MaxHoldDays - (int)Math.Floor(held.TotalDays));
		return new(next, held >= TimeSpan.FromDays(MaxHoldDays) ? SteamHoldPhase.Expired : SteamHoldPhase.Holding, daysLeft, bypassed);
	}

	// Idle: Gaming Mode, Steam running, no game (or protected game) for IdleMinutes. The
	// clock starts at agent start too, so a fresh boot does not update straight away.
	internal static bool IsIdleMoment(bool gamingMode, bool steamRunning, bool gameRunning, bool protectedGame,
		DateTimeOffset quietSince, DateTimeOffset now) =>
		gamingMode && steamRunning && !gameRunning && !protectedGame && now - quietSince >= TimeSpan.FromMinutes(IdleMinutes);

	private static readonly Regex ManifestVersion = new("\"version\"\\s+\"(?<value>[0-9]{1,20})\"", RegexOptions.CultureInvariant);

	// Steam\package\steam_client_win64.manifest (or _win32): "version"  "1763083880".
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
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, SteamHoldState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}

// ---------- the update and rollback jobs ----------

internal enum SteamUpdateStage { Checking, ClosingSteam, BackingUp, Updating, CheckingPlugins, RestartingPlugins, Finished }

internal enum SteamUpdateOutcome { Updated, UpdatedPluginsBroken, NotWhilePlaying, SteamWouldNotClose, BackupFailed, SteamDidNotStart }

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
	// Plugins count only if they worked before: a Decky that was already broken (or is not
	// installed) is not the update's fault and must not trigger rollback advice.
	bool PluginsWereHealthy();
	Task<bool> WaitForPluginsAsync(DateTimeOffset since, TimeSpan timeout, CancellationToken cancellation);
	Task RestartPluginsAsync(CancellationToken cancellation);
	DateTimeOffset Now();
	void Stage(SteamUpdateStage stage);
}

internal static class SteamUpdateJob
{
	internal static readonly TimeSpan UpdateWindow = TimeSpan.FromMinutes(5);
	internal static readonly TimeSpan RecheckWindow = TimeSpan.FromMinutes(2);

	internal static async Task<SteamUpdateOutcome> RunAsync(ISteamUpdateHost host, CancellationToken cancellation)
	{
		host.Stage(SteamUpdateStage.Checking);
		if (host.GameRunning()) return SteamUpdateOutcome.NotWhilePlaying;
		bool pluginsMatter = host.PluginsWereHealthy();
		host.Stage(SteamUpdateStage.ClosingSteam);
		if (!await host.StopSteamAsync(cancellation).ConfigureAwait(false))
		{
			await host.StartSteamAsync(cancellation).ConfigureAwait(false);
			return SteamUpdateOutcome.SteamWouldNotClose;
		}
		host.Stage(SteamUpdateStage.BackingUp);
		// Never update without a way back.
		if (!await host.BackupClientAsync(cancellation).ConfigureAwait(false))
		{
			await host.StartSteamAsync(cancellation).ConfigureAwait(false);
			return SteamUpdateOutcome.BackupFailed;
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
			return SteamUpdateOutcome.SteamDidNotStart;
		}
		host.Stage(SteamUpdateStage.CheckingPlugins);
		// The hold goes back only after this wait: the bootstrapper reads steam.cfg when it
		// starts, and an update restarts Steam once, inside this window.
		bool healthy = await host.WaitForPluginsAsync(started, UpdateWindow, cancellation).ConfigureAwait(false);
		host.ApplyHold();
		if (!healthy && pluginsMatter)
		{
			host.Stage(SteamUpdateStage.RestartingPlugins);
			DateTimeOffset restarted = host.Now();
			await host.RestartPluginsAsync(cancellation).ConfigureAwait(false);
			healthy = await host.WaitForPluginsAsync(restarted, RecheckWindow, cancellation).ConfigureAwait(false);
		}
		host.Stage(SteamUpdateStage.Finished);
		return healthy || !pluginsMatter ? SteamUpdateOutcome.Updated : SteamUpdateOutcome.UpdatedPluginsBroken;
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
