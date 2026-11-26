using System;

namespace GamingMode.Services;

// Eternal Core Mark III: "did Decky's UI actually load into Steam?" (pure state machine).
//
// Decky's own watchdogs (Playhub.DeckyHost) see the loader process and its HTTP server.
// They cannot see the usual failure after a Steam client update: the loader runs, but its
// UI never appears inside Steam. Core's own Decky plugin proves the whole chain instead:
// its frontend (running inside Steam) asks its Python backend (running inside Decky) to
// POST /decky/heartbeat to the agent every HeartbeatSeconds.
//
// Verdict: Big Picture has been up for NoticeAfter and no heartbeat arrived since it came
// up, or heartbeats stopped for NoticeAfter, means "plugins not loaded". The agent then
// shows one calm, non-interactive notice per Steam session, never while a game runs.
// Decky is an extra: nothing here changes what Steam or Gaming Mode do.
internal enum DeckyVerdict { NotApplicable, Waiting, Healthy, NotLoaded }

internal readonly record struct DeckyObservation(
	DateTimeOffset Now,
	bool GamingMode,
	bool PluginsExpected,          // Decky installed and "start Decky" on
	int? SteamProcessId,           // the Steam session; a new id is a new session
	bool BigPictureUp,
	bool GameRunning,              // any game, or anti-cheat safe mode engaged
	string? SteamVersion);

internal sealed class DeckyHeartbeatMonitor
{
	internal static readonly TimeSpan NoticeAfter = TimeSpan.FromSeconds(90);
	internal const int HeartbeatSeconds = 30;

	private int? _session;
	private DateTimeOffset? _bigPictureSince;
	private bool _noticeShown;

	internal DateTimeOffset? LastHeartbeat { get; private set; }
	internal string? HealthySteamVersion { get; private set; }
	internal bool EverHealthy { get; private set; }
	// "Continue without plugins" in Core Repair: quiet until Steam's version changes.
	internal string? DismissedForSteamVersion { get; set; }
	internal DeckyVerdict Verdict { get; private set; } = DeckyVerdict.NotApplicable;

	internal void Heartbeat(DateTimeOffset now, string? steamVersion)
	{
		LastHeartbeat = now;
		EverHealthy = true;
		if (!string.IsNullOrEmpty(steamVersion)) HealthySteamVersion = steamVersion;
	}

	// From the agent's last run: plugins worked with this Steam version before.
	internal void RestoreHealthyVersion(string? steamVersion)
	{
		if (string.IsNullOrEmpty(steamVersion)) return;
		HealthySteamVersion = steamVersion;
		EverHealthy = true;
	}

	internal bool HealthySince(DateTimeOffset since) => LastHeartbeat is { } beat && beat >= since;

	// Returns true when the notice should be shown now (at most once per Steam session).
	internal bool Observe(DeckyObservation o)
	{
		if (o.SteamProcessId != _session)
		{
			_session = o.SteamProcessId;
			_bigPictureSince = null;
			_noticeShown = false;
		}
		if (!o.GamingMode || !o.PluginsExpected || o.SteamProcessId is null)
		{
			_bigPictureSince = null;
			Verdict = DeckyVerdict.NotApplicable;
			return false;
		}
		if (!o.BigPictureUp && _bigPictureSince is null) { Verdict = DeckyVerdict.Waiting; return false; }
		_bigPictureSince ??= o.Now;

		bool beatSinceUp = LastHeartbeat is { } beat && beat >= _bigPictureSince.Value - TimeSpan.FromSeconds(HeartbeatSeconds);
		bool stale = LastHeartbeat is { } last && o.Now - last >= NoticeAfter;
		if (beatSinceUp && !stale) Verdict = DeckyVerdict.Healthy;
		else if (o.Now - _bigPictureSince.Value < NoticeAfter) Verdict = DeckyVerdict.Waiting;
		else Verdict = DeckyVerdict.NotLoaded;

		if (Verdict != DeckyVerdict.NotLoaded || _noticeShown || o.GameRunning) return false;
		if (!string.IsNullOrEmpty(DismissedForSteamVersion) && string.Equals(DismissedForSteamVersion, o.SteamVersion, StringComparison.Ordinal))
			return false;
		_noticeShown = true;
		return true;
	}

	// The notice can say "after a Steam update" when the version changed since the last
	// healthy heartbeat.
	internal bool AfterSteamUpdate(string? currentSteamVersion) =>
		!string.IsNullOrEmpty(HealthySteamVersion) && !string.IsNullOrEmpty(currentSteamVersion)
		&& !string.Equals(HealthySteamVersion, currentSteamVersion, StringComparison.Ordinal);
}
