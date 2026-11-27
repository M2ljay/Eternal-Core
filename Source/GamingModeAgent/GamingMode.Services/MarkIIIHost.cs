using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Win32;
using Playhub.Shared;

namespace GamingMode.Services;

// Eternal Core Mark III in the agent: TV control, Steam updates (soak, gate, smoke test), the Decky health
// check, Core Repair's actions, the wake sequence and their HTTP endpoints. Core Health (the
// Quick Access health section and its notices) is in MarkIIIHost.Health.cs; Core Voice (microphone
// mute and the Xbox party button) in MarkIIIHost.Voice.cs; Stream mode (the TV left alone while the PC
// streams to another device) in MarkIIIHost.Stream.cs; the console guards in MarkIIIHost.Console.cs.
//
// Fail-safe by construction: every feature runs beside Gaming Mode, never in front of it.
// Each loop iteration, power event and endpoint catches its own failures, logs them and
// carries on; none of them can stop Steam from starting or the agent from running.
internal sealed partial class MarkIIIHost : IDisposable
{
	private readonly AppPaths _paths;
	private readonly JsonStore _store;
	private readonly ModeManager _manager;
	private readonly ProcessTools _processTools;
	private readonly AntiCheatProtectionMonitor _antiCheat;
	private readonly FileLogger _logger;
	private readonly DpapiTvPairingStore _pairing;
	private readonly TvController _tv;
	private readonly DeckyHeartbeatMonitor _decky = new();
	private readonly SteamHost _steamHost;
	private readonly string _holdPath, _healthPath, _toastsPath, _updateNightPath;
	private bool? _toastsQuiet;
	private readonly object _sync = new();
	private readonly SemaphoreSlim _job = new(1, 1);
	private readonly DateTimeOffset _started = DateTimeOffset.Now;
	private JobState _jobState = new(false, null, null, null, null);
	private DateTimeOffset _quietSince = DateTimeOffset.Now;
	private bool _subscribed;

	private sealed record JobState(bool Busy, string? Action, string? Stage, string? MessageKey, string? Detail);

	internal MarkIIIHost(AppPaths paths, JsonStore store, ModeManager manager, ProcessTools processTools,
		AntiCheatProtectionMonitor antiCheat, FileLogger logger)
	{
		_paths = paths; _store = store; _manager = manager; _processTools = processTools; _antiCheat = antiCheat; _logger = logger;
		_pairing = new DpapiTvPairingStore(paths.ConfigDirectory, logger);
		_tv = new TvController(() => _store.LoadConfig().Gaming.Tv ?? new TvSettings(), _pairing, logger.Info, NetworkMac.Resolve, hold: StreamHold);
		_holdPath = Path.Combine(paths.ConfigDirectory, "steam-update-hold.json");
		_healthPath = Path.Combine(paths.ConfigDirectory, "core-health.json");
		_toastsPath = Path.Combine(paths.ConfigDirectory, "quiet-toasts.json");
		_updateNightPath = Path.Combine(paths.ConfigDirectory, "update-night.json");
		_healthAlertsPath = Path.Combine(paths.ConfigDirectory, "core-health-alerts.json");
		_steamHost = new SteamHost(this);
		LoadHealth();
	}

	// ---------- power-on (called before the agent's services exist) ----------

	// Wake the TV and switch it to Core while Gaming Mode starts. The returned task gates
	// the boot sequence's start (at most BootSequencePolicy.MaxStartDelay); nothing waits
	// for it otherwise.
	internal static Task WakeTvForBootAsync(ModeConfig config, AppPaths paths, FileLogger logger)
	{
		TvSettings tv = config.Gaming.Tv ?? new TvSettings();
		if (!tv.Enabled || !tv.TurnOnWithCore) return Task.CompletedTask;
		return Task.Run(async () =>
		{
			try
			{
				// Stream mode: "Keep TV out of it", or a stream the agent can already see (or, after going
				// down mid-stream, a short wait for it to come back). Checked again before the input switch.
				(string? held, Func<string?> hold) = await StreamBootHoldAsync(paths, logger).ConfigureAwait(false);
				if (held is not null) { logger.Info($"TV at power-on: Stream mode ({held}); nothing sent to the TV."); return; }
				var controller = new TvController(() => tv, new DpapiTvPairingStore(paths.ConfigDirectory, logger), logger.Info, NetworkMac.Resolve, hold: hold);
				TvResult result = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(40), CancellationToken.None).ConfigureAwait(false);
				logger.Info($"TV at power-on: {result.Outcome}.");
				// The picture appears a moment after the input switch.
				if (result.Outcome == TvOutcome.Done) await Task.Delay(1200).ConfigureAwait(false);
			}
			catch (Exception error) { logger.Error("TV wake at power-on failed; Gaming Mode continues.", error); }
		});
	}

	// ---------- lifetime ----------

	internal void Start(CancellationToken stopping)
	{
		try { StartVoice(); }
		catch (Exception error) { _logger.Error("Voice: could not start; the microphone mute and party button are unavailable.", error); }
		try { StartStream(); }
		catch (Exception error) { _logger.Error("Stream mode: could not start; the TV is handled as usual until the next check.", error); }
		try { StartBatchTwo(stopping); }
		catch (Exception error) { _logger.Error("Batch 2: could not start; Home Assistant, automations and sleep after streaming are off until the next start.", error); }
		try { StartConsole(stopping); }
		catch (Exception error) { _logger.Error("Console guards: could not start; their Health rows still read, launcher pop-ups are left alone.", error); }
		try { StartEmulators(stopping); }
		catch (Exception error) { _logger.Error("Emulators and game exit: could not start; their Health rows still read.", error); }
		_ = Task.Run(() => LoopAsync(stopping));
		// SystemEvents needs a thread it can own: subscribing from a pool (MTA) thread makes
		// it create its own hidden window and message loop.
		_ = Task.Run(() =>
		{
			try
			{
				SystemEvents.PowerModeChanged += OnPowerModeChanged;
				SystemEvents.SessionEnding += OnSessionEnding;
				lock (_sync) _subscribed = true;
				_logger.Info("Mark III: power events armed (TV, wake sequence).");
			}
			catch (Exception error) { _logger.Error("Mark III: power events are unavailable; TV and wake sequence follow only the boot.", error); }
		});
	}

	public void Dispose()
	{
		StopVoice();
		StopConsole();
		try { CancelListening("the agent stopped"); _jarvisClient.Dispose(); } catch { }
		lock (_sync)
		{
			if (!_subscribed) return;
			_subscribed = false;
		}
		try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
	}

	private async Task LoopAsync(CancellationToken stopping)
	{
		int tick = 0;
		while (!stopping.IsCancellationRequested)
		{
			try { await Task.Delay(TimeSpan.FromSeconds(5), stopping).ConfigureAwait(false); }
			catch (OperationCanceledException) { return; }
			try { CheckDecky(); }
			catch (Exception error) { _logger.Error("Mark III: Decky health check failed this round.", error); }
			try { CheckToasts(); }
			catch (Exception error) { _logger.Error("Mark III: Windows banner switch failed this round.", error); }
			try { CheckVoice(); }
			catch (Exception error) { _logger.Error("Voice: microphone check failed this round.", error); }
			try { CheckStream(); }
			catch (Exception error) { _logger.Error("Stream mode: check failed this round.", error); }
			try { await CheckBatchTwoAsync().ConfigureAwait(false); }
			catch (Exception error) { _logger.Error("Batch 2: check failed this round.", error); }
			try { CheckJarvis(); }
			catch (Exception error) { _logger.Error("Jarvis: check failed this round.", error); }
			if (++tick % 12 != 0) continue; // once a minute
			try { await CheckSteamHoldAsync().ConfigureAwait(false); }
			catch (Exception error) { _logger.Error("Mark III: Steam update hold check failed this round.", error); }
			try { await CheckUpdateNightAsync().ConfigureAwait(false); }
			catch (Exception error) { _logger.Error("Mark III: update night check failed this round.", error); }
			try { CheckWindDown(); }
			catch (Exception error) { _logger.Error("Wind-down: check failed this round.", error); }
			if (tick % 60 != 0) continue; // every five minutes
			try { await CheckHealthAlertsAsync().ConfigureAwait(false); }
			catch (Exception error) { _logger.Error("Core Health: alert check failed this round.", error); }
		}
	}

	// ---------- shared observations ----------

	private bool InGamingMode() => _store.LoadState().CurrentMode == ModeKind.Gaming;

	private string? SteamRoot() => SteamInstallationPath.FindRoot(_store.LoadConfig().Gaming.SteamPath);

	private string? SteamVersion() => SteamUpdateHoldPolicy.ReadClientVersion(SteamRoot());

	private bool GameRunning() => _antiCheat.Engaged || RunningAppId() != 0;

	private static uint RunningAppId()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			return key?.GetValue("RunningAppID") switch { int value => unchecked((uint)value), long value => (uint)value, _ => 0u };
		}
		catch { return 0; }
	}

	private static int? SteamProcessId()
	{
		Process[] processes = Process.GetProcessesByName("steam");
		try
		{
			using Process current = Process.GetCurrentProcess();
			int? id = null;
			foreach (Process process in processes)
			{
				try { if (process.SessionId == current.SessionId && !process.HasExited && (id is null || process.Id < id)) id = process.Id; }
				catch (InvalidOperationException) { }
			}
			return id;
		}
		finally { foreach (Process process in processes) process.Dispose(); }
	}

	private static bool SteamRunning()
	{
		foreach (string name in new[] { "steam", "steamwebhelper" })
		{
			Process[] processes = Process.GetProcessesByName(name);
			try { if (processes.Length > 0) return true; }
			finally { foreach (Process process in processes) process.Dispose(); }
		}
		return false;
	}

	private bool PluginsExpected()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		if (!gaming.DeckyRequired) return false;
		try { return (!string.IsNullOrWhiteSpace(gaming.DeckyPath) && File.Exists(gaming.DeckyPath)) || _processTools.GetDeckyFallbackPaths().Any(File.Exists); }
		catch { return false; }
	}

	private string Language() => _store.LoadConfig().Language ?? "auto";

	// argument fills the text's {0}; extra is a sentence added after it (the plugins that reported errors).
	private void Notice(string key, string? argument = null, string? extra = null)
	{
		if (!InGamingMode() || GameRunning()) { _logger.Info($"Core notice \"{key}\" not shown: not in Big Picture or a game is running."); return; }
		// Streaming to another device: the notice would land on a TV nobody is watching (or someone else is).
		if (StreamHoldsNotices($"Core notice \"{key}\"")) return;
		string text = argument is null ? CoreText.Get(Language(), key) : CoreText.Format(Language(), key, argument);
		if (!string.IsNullOrEmpty(extra)) text += " " + extra;
		CoreNoticeOverlay.Show(text, TimeSpan.FromSeconds(10), () => InGamingMode() && !GameRunning(), _logger);
	}

	// ---------- quiet Windows (couch hardening) ----------

	// Windows banners off while Gaming Mode runs (when the setting is on), the user's own
	// setting back for the desktop. Applied only when the wanted state changes.
	private void CheckToasts()
	{
		bool quiet = InGamingMode() && _store.LoadConfig().Gaming.QuietWindowsToasts;
		if (_toastsQuiet == quiet) return;
		if (CouchHardening.SetGamingToasts(new CouchRegistryWindows(), quiet, _toastsPath))
			_logger.Info(quiet ? "Windows banners off for Gaming Mode." : "Windows banners back to the user's setting.");
		_toastsQuiet = quiet;
	}

	// Restart-pending markers Windows Update and servicing leave in HKLM (readable without admin).
	private static bool WindowsRestartPending()
	{
		foreach (string key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired",
			@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending" })
		{
			try
			{
				using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
				using RegistryKey? found = root.OpenSubKey(key);
				if (found is not null) return true;
			}
			catch { }
		}
		return false;
	}

	private async Task CheckUpdateNightAsync()
	{
		DateTimeOffset now = DateTimeOffset.Now;
		UpdateNightState state = UpdateNightPolicy.Observe(UpdateNightPolicy.Load(_updateNightPath), now);
		switch (UpdateNightPolicy.Decide(state, now, GameRunning()))
		{
			case UpdateNightAction.Restart:
				// Batch 1: Ludusavi backs up every game's saves first (time-boxed, once a night; a failure never stops the restart).
				await BackupSavesAsync("update night").ConfigureAwait(false);
				// A game started during the backup: the restart waits (Decide retries until 7:00).
				if (GameRunning()) { _logger.Info("Update night: a game started during the save backup; the restart waits."); return; }
				now = DateTimeOffset.Now;
				UpdateNightPolicy.Save(_updateNightPath, state with { ScheduledFor = null, LastNight = now });
				// Console guards: a staged firmware update with BitLocker on would ask for the recovery key.
				SuspendBitLockerForFirmware("update night");
				_logger.Info("Update night: restarting into Gaming Mode for Windows updates.");
				_manager.RestartInMode(ModeKind.Gaming);
				return;
			case UpdateNightAction.GiveUp:
				_logger.Info("Update night: a game was still running at 7:00; the restart waits for the next offer.");
				state = state with { ScheduledFor = null };
				break;
		}
		UpdateNightPolicy.Save(_updateNightPath, state);
	}

	private object UpdateNightView()
	{
		UpdateNightState state = UpdateNightPolicy.Load(_updateNightPath);
		bool pending = WindowsRestartPending();
		return new { offer = UpdateNightPolicy.ShouldOffer(UpdateNightPolicy.Observe(state, DateTimeOffset.Now), pending, DateTimeOffset.Now), pending,
			scheduledFor = state.ScheduledFor?.ToString("o"), hour = UpdateNightPolicy.RestartHour };
	}

	// ---------- Decky health ----------

	private void CheckDecky()
	{
		bool gaming = InGamingMode();
		int? steam = gaming ? SteamProcessId() : null;
		bool bigPicture = steam is not null && SteamFullscreenDetector.IsBigPictureUp();
		string? version = steam is not null ? SteamVersion() : null;
		DeckyVerdict before = _decky.Verdict;
		bool notice = _decky.Observe(new DeckyObservation(DateTimeOffset.Now, gaming, PluginsExpected(), steam, bigPicture, GameRunning(), version));
		if (_decky.Verdict != before && _decky.Verdict is DeckyVerdict.Healthy or DeckyVerdict.NotLoaded)
			_logger.Info($"Decky health: {_decky.Verdict} (Steam client {version ?? "unknown"}).");
		if (!notice) return;
		if (!_store.LoadConfig().Gaming.DeckyHealthNoticeEnabled) { _logger.Info("Decky UI did not load; the notice is turned off in settings."); return; }
		// Batch 1 (B11): name the plugins whose logs reported errors lately.
		string? failing = null;
		try { failing = FailingPluginsSentence(); }
		catch (Exception error) { _logger.Error("Decky health: plugin logs could not be read for the notice.", error); }
		Notice(_decky.AfterSteamUpdate(version) ? "notice.pluginsAfterUpdate" : "notice.plugins", extra: failing);
	}

	private void Heartbeat()
	{
		bool first = _decky.LastHeartbeat is null;
		string? version = SteamVersion();
		_decky.Heartbeat(DateTimeOffset.Now, version);
		if (first) { _logger.Info($"Decky heartbeat received: Core's Big Picture plugin is loaded (Steam client {version ?? "unknown"})."); SaveHealth(); }
	}

	private async Task<bool> WaitForHeartbeatAsync(DateTimeOffset since, TimeSpan timeout, CancellationToken cancellation)
	{
		DateTimeOffset giveUp = DateTimeOffset.Now + timeout;
		while (DateTimeOffset.Now < giveUp)
		{
			if (_decky.HealthySince(since)) return true;
			await Task.Delay(1000, cancellation).ConfigureAwait(false);
		}
		return _decky.HealthySince(since);
	}

	private void LoadHealth()
	{
		try
		{
			if (!File.Exists(_healthPath)) return;
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_healthPath));
			if (document.RootElement.TryGetProperty("dismissedForSteamVersion", out JsonElement dismissed) && dismissed.ValueKind == JsonValueKind.String)
				_decky.DismissedForSteamVersion = dismissed.GetString();
			if (document.RootElement.TryGetProperty("healthySteamVersion", out JsonElement healthy) && healthy.ValueKind == JsonValueKind.String)
				_decky.RestoreHealthyVersion(healthy.GetString());
		}
		catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
	}

	private void SaveHealth()
	{
		try
		{
			string temporary = _healthPath + ".tmp";
			File.WriteAllText(temporary, JsonSerializer.Serialize(new { dismissedForSteamVersion = _decky.DismissedForSteamVersion, healthySteamVersion = _decky.HealthySteamVersion }));
			File.Move(temporary, _healthPath, true);
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
	}

	// ---------- Steam updates (SteamUpdateHold.cs, docs/eternal/STEAM-UPDATES.md) ----------

	private static readonly HttpClient ManifestClient = new() { Timeout = TimeSpan.FromSeconds(10) };
	private static readonly TimeSpan PendingCheckEvery = TimeSpan.FromHours(6), SteamKindFor = TimeSpan.FromMinutes(10), PluginSnapshotEvery = TimeSpan.FromMinutes(15);
	private static readonly Lazy<SteamCompatibility?> Compatibility = new(LoadCompatibility);
	private (DateTimeOffset At, SteamClientKindReading Reading)? _steamKind;
	private DateTimeOffset _pluginSnapshotAt = DateTimeOffset.MinValue;
	private IReadOnlyList<string>? _pluginSnapshot;
	private volatile bool _coreUpdatingSteam;

	// The known-good file shipped inside the agent. Missing or broken: the gate cannot tell, and says so.
	private static SteamCompatibility? LoadCompatibility()
	{
		try
		{
			using Stream? stream = typeof(MarkIIIHost).Assembly.GetManifestResourceStream("SteamCompatibility.json");
			if (stream is null) return null;
			using var reader = new StreamReader(stream);
			return SteamCompatibility.Parse(reader.ReadToEnd());
		}
		catch (Exception error) when (error is IOException or InvalidOperationException) { return null; }
	}

	// Installed Decky: present at all, and its PluginLoader product version when readable.
	private (bool Installed, Version? Version) InstalledDecky()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		string?[] candidates;
		try { candidates = new string?[] { gaming.DeckyPath }.Concat(_processTools.GetDeckyFallbackPaths()).ToArray(); }
		catch { candidates = new string?[] { gaming.DeckyPath }; }
		bool installed = false;
		foreach (string? path in candidates)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
				installed = true;
				if (DeckySteamPairing.TryParseDeckyVersion(FileVersionInfo.GetVersionInfo(path).ProductVersion, out Version version) && version.Major > 0)
					return (true, version);
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
		}
		return (installed, null);
	}

	// Which UI the running Steam loads, asked at most every SteamKindFor (each ask opens a debug session).
	private async Task<SteamClientKindReading> SteamKindAsync(bool fresh = false)
	{
		if (!fresh && _steamKind is { } cached && DateTimeOffset.Now - cached.At < SteamKindFor) return cached.Reading;
		SteamClientKindReading reading = SteamProcessId() is null ? new(SteamUiKind.Unknown, "Steam is not running") : await SteamClientKindProbe.DetectAsync().ConfigureAwait(false);
		_steamKind = (DateTimeOffset.Now, reading);
		return reading;
	}

	// The pending client version: Valve's published manifest for this PC's channel, fetched at most every
	// PendingCheckEvery. Null when not fetched this round or when the fetch failed (the policy keeps what it knew).
	private async Task<string?> PendingVersionAsync(string root, SteamHoldState state, DateTimeOffset now)
	{
		if (state.PendingCheckedAt is { } last && now - last < PendingCheckEvery && now >= last) return null;
		Uri url = SteamUpdateHoldPolicy.PublishedManifest(SteamUpdateHoldPolicy.ReadBetaName(root));
		try
		{
			using HttpResponseMessage response = await ManifestClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 256 * 1024) { _logger.Info($"Steam updates: Valve's client manifest answered {(int)response.StatusCode}; pending version unknown this round."); return ""; }
			string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			string? version = text.Length <= 256 * 1024 ? SteamUpdateHoldPolicy.ParseClientManifestVersion(text) : null;
			if (version is null) _logger.Info("Steam updates: Valve's client manifest had no version; pending version unknown this round.");
			return version ?? "";
		}
		catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidOperationException)
		{
			_logger.Info($"Steam updates: Valve's client manifest could not be read ({error.GetType().Name}); pending version unknown this round.");
			return "";
		}
	}

	private async Task<SteamGateVerdict> GateAsync(SteamHoldState state, bool freshProbe = false)
	{
		(bool installed, Version? decky) = InstalledDecky();
		SteamClientKindReading steam = installed ? await SteamKindAsync(freshProbe).ConfigureAwait(false) : new(SteamUiKind.Unknown, "Decky not installed");
		SteamGateVerdict gate = SteamReleaseGate.Evaluate(Compatibility.Value, installed, decky, state.InstalledVersion ?? SteamVersion(), steam.Kind, state.PendingVersion);
		_logger.Info($"Steam update gate: {gate.Result} ({gate.Reason}). Installed Decky {(installed ? decky?.ToString() ?? "unknown version" : "none")}, "
			+ $"bundled {Compatibility.Value?.BundledDecky ?? "unknown"}, Steam {state.InstalledVersion ?? "unknown"} running {steam.Kind} ({steam.Detail}), pending {state.PendingVersion ?? "unknown"}.");
		return gate;
	}

	private string Fingerprint(SteamHoldState state) => SteamReleaseGate.Fingerprint(Compatibility.Value, InstalledDecky().Version, state.PendingVersion);

	private int SoakDays() => SteamUpdateHoldPolicy.SoakDays(_store.LoadConfig().Gaming.SteamUpdateSoakDays);

	// Decky's own list of loaded plugins, read through Steam's debug port. Null: no answer.
	private static async Task<IReadOnlyList<string>?> LoadedPluginsAsync()
	{
		SteamEvaluation answer = await SteamClientKindProbe.EvaluateAsync(DeckySteamPairing.LoadedPluginsExpression).ConfigureAwait(false);
		return DeckySteamPairing.ParseLoadedPlugins(answer.Value);
	}

	// While plugins work, remember which ones Decky loaded: the smoke test after an update that happened
	// by itself expects them back.
	private async Task RefreshPluginSnapshotAsync(DateTimeOffset now)
	{
		if (now - _pluginSnapshotAt < PluginSnapshotEvery || _decky.Verdict != DeckyVerdict.Healthy) return;
		_pluginSnapshotAt = now;
		if (await LoadedPluginsAsync().ConfigureAwait(false) is { } loaded) _pluginSnapshot = loaded;
	}

	private SmokeExpectations Expectations(IReadOnlyList<string>? plugins) =>
		PluginsExpected() && _decky.EverHealthy ? new SmokeExpectations(true, plugins ?? _pluginSnapshot ?? Array.Empty<string>()) : SmokeExpectations.None;

	private async Task CheckSteamHoldAsync()
	{
		if (GameRunning()) _quietSince = DateTimeOffset.Now;
		string? root = SteamRoot();
		if (root is null) return;
		DateTimeOffset now = DateTimeOffset.Now;
		bool holdOn = SteamUpdateHoldPolicy.IsHoldFilePresent(root);
		SteamHoldState state = SteamUpdateHoldPolicy.Load(_holdPath);
		string? fetched = holdOn ? await PendingVersionAsync(root, state, now).ConfigureAwait(false) : null;
		bool updating = _coreUpdatingSteam;
		SteamHoldObservation seen = SteamUpdateHoldPolicy.Observe(state, holdOn, SteamVersion(), string.IsNullOrEmpty(fetched) ? null : fetched, now, updating);
		SteamHoldState next = seen.Next;
		// A failed fetch is tried again in an hour, a good one in PendingCheckEvery.
		if (fetched is not null) next = next with { PendingCheckedAt = fetched.Length > 0 ? now : now - PendingCheckEvery + TimeSpan.FromHours(1) };
		if (next.PendingVersion != state.PendingVersion && next.PendingVersion is not null)
			_logger.Info($"Steam updates: Steam {next.PendingVersion} is pending (installed {next.InstalledVersion ?? "unknown"}); day one of its soak is the night of {next.PendingDayOne:yyyy-MM-dd}.");
		if (seen.UpdatedDespiteHold)
		{
			_logger.Info($"Steam updated from {state.InstalledVersion} to {next.InstalledVersion} although its updates were blocked (steam.cfg present). Running the smoke test.");
			// Not started (another job runs): keep the old version so the next round tries again.
			if (!TryRunJob("verify-steam", automatic: true)) next = next with { InstalledVersion = state.InstalledVersion };
		}
		try { await RefreshPluginSnapshotAsync(now).ConfigureAwait(false); }
		catch (Exception error) { _logger.Error("Steam updates: Decky's plugin list could not be read this round.", error); }
		SteamHoldView view = SteamUpdateHoldPolicy.View(next, holdOn, SoakDays(), Fingerprint(next), now);
		if (view.Phase is SteamHoldPhase.Due or SteamHoldPhase.Postponed && SteamUpdateHoldPolicy.IsUpdateNight(now) && !SteamUpdateHoldPolicy.DecidedTonight(next, now) && !updating)
		{
			DateTimeOffset quiet = _quietSince > _started ? _quietSince : _started;
			bool idle = SteamUpdateHoldPolicy.IsIdleMoment(InGamingMode(), SteamProcessId() is not null, RunningAppId() != 0, _antiCheat.Engaged, quiet, now);
			if (idle)
			{
				SteamGateVerdict gate = await GateAsync(next, freshProbe: true).ConfigureAwait(false);
				switch (SteamUpdateHoldPolicy.DecideNight(view, next, idle, gate, now))
				{
					case SteamNightAction.Postpone:
						next = SteamUpdateHoldPolicy.Postponed(next, gate.Reason, now);
						_logger.Info($"Steam updates: Steam {next.PendingVersion} postponed ({gate.Reason}); asked again next update night.");
						break;
					case SteamNightAction.Update:
						_logger.Info($"Steam updates: Steam {next.PendingVersion} has soaked {SoakDays()} days and the gate passes: updating now.");
						if (TryRunJob("update-steam", automatic: true)) next = next with { LastNightDecision = now };
						break;
				}
			}
		}
		if (next != state) SteamUpdateHoldPolicy.Save(_holdPath, next);
	}

	// The answer to "Update Steam now": go, or which confirmation is still needed.
	private async Task<(SteamManualAnswer Answer, SteamGateVerdict Gate)> ManualAsync(int confirmations)
	{
		SteamGateVerdict gate = await GateAsync(SteamUpdateHoldPolicy.Load(_holdPath), freshProbe: true).ConfigureAwait(false);
		return (SteamManualPolicy.Decide(gate, confirmations), gate);
	}

	private async Task<IResult> UpdateSteamNowAsync(HttpContext context)
	{
		int confirmations = int.TryParse(context.Request.Query["confirm"].ToString(), out int count) ? Math.Clamp(count, 0, 2) : 0;
		(SteamManualAnswer answer, SteamGateVerdict gate) = await ManualAsync(confirmations).ConfigureAwait(false);
		if (answer != SteamManualAnswer.Go)
			return Results.Json(new { ok = false, confirm = SteamManualPolicy.Code(answer), reason = gate.Reason });
		_logger.Info($"Steam updates: \"Update Steam now\" ({gate.Reason}, {confirmations} confirmation(s)).");
		return TryRunJob("update-steam") ? Results.Json(new { ok = true, reason = gate.Reason }, statusCode: 202) : Results.Json(new { ok = false, busy = true }, statusCode: 409);
	}

	// Runs inside the job: the update (automatic on update night, or "Update Steam now") or the check of
	// an update that happened by itself; the outcome goes into the hold state and a Core notice.
	private async Task<(string Key, string? Detail)> SteamUpdateJobAsync(bool verifyOnly, bool automatic, CancellationToken cancellation)
	{
		SteamHoldState before = SteamUpdateHoldPolicy.Load(_holdPath);
		string fingerprint = Fingerprint(before);
		SteamUpdateResult result;
		_coreUpdatingSteam = true;
		try
		{
			using (_manager.BeginSteamMaintenance())
				result = verifyOnly
					? await SteamUpdateJob.VerifyAsync(_steamHost, Expectations(null), cancellation).ConfigureAwait(false)
					: await SteamUpdateJob.RunAsync(_steamHost, cancellation).ConfigureAwait(false);
		}
		finally { _coreUpdatingSteam = false; }
		string? version = SteamVersion();
		_logger.Info($"Steam {(verifyOnly ? "update check" : "update")}: {result.Outcome}{(result.Smoke.Ok ? "" : $", failed check {SmokeTestPolicy.Code(result.Smoke.Failed)}{(result.Smoke.Detail is null ? "" : " (" + result.Smoke.Detail + ")")}")} (client {version ?? "unknown"}).");
		DateTimeOffset now = DateTimeOffset.Now;
		switch (result.Outcome)
		{
			case SteamUpdateOutcome.Updated:
				SaveHold(state => SteamUpdateHoldPolicy.Updated(state, version, now, automatic && !verifyOnly));
				if (!verifyOnly) Notice("repair.steamUpdated");
				return (verifyOnly ? "repair.done" : "repair.steamUpdated", null);
			case SteamUpdateOutcome.RolledBack:
			case SteamUpdateOutcome.RollbackFailed:
				bool restored = result.Outcome == SteamUpdateOutcome.RolledBack;
				SaveHold(state => SteamUpdateHoldPolicy.Failed(state, result.Smoke, fingerprint, restored, SteamVersion(), now, automatic && !verifyOnly));
				string key = SmokeTestPolicy.NoticeKey(result.Smoke.Failed, restored);
				Notice(key);
				return (key, result.Smoke.Detail);
			case SteamUpdateOutcome.NotWhilePlaying: return ("repair.notWhilePlaying", null);
			default: return ("repair.failed", result.Outcome.ToString());
		}
	}

	// ---------- Core Repair actions (one at a time) ----------

	// "verify-steam" is internal (an update that happened by itself); the endpoint does not accept it.
	private static readonly string[] Actions = { "restart-plugins", "reenable-access", "update-decky", "rollback-steam", "update-steam", "verify-steam", "continue" };
	private static readonly string[] RequestableActions = { "restart-plugins", "reenable-access", "update-decky", "rollback-steam", "update-steam", "continue" };

	private bool TryRunJob(string action, bool automatic = false)
	{
		if (!Actions.Contains(action)) return false;
		if (!_job.Wait(0)) return false;
		lock (_sync) _jobState = new(true, action, null, null, null);
		_ = Task.Run(async () =>
		{
			(string Key, string? Detail) result = ("repair.failed", "unexpected error");
			try { result = await RunActionAsync(action, automatic).ConfigureAwait(false); }
			catch (Exception error) { _logger.Error($"Core Repair \"{action}\" failed.", error); result = ("repair.failed", error.Message); }
			finally
			{
				lock (_sync) _jobState = new(false, action, null, result.Key, result.Detail);
				_job.Release();
				_logger.Info($"Core Repair \"{action}\" finished: {result.Key}{(result.Detail is null ? "" : " (" + result.Detail + ")")}.");
			}
		});
		return true;
	}

	private void Stage(string stage) { lock (_sync) _jobState = _jobState with { Stage = stage }; }

	// Steam counts Core Repair itself as a running game while its window is open. Actions
	// that restart Steam close that window first; give Steam a moment to notice.
	private async Task WaitForNoGameAsync(TimeSpan timeout)
	{
		DateTime giveUp = DateTime.UtcNow + timeout;
		while (GameRunning() && DateTime.UtcNow < giveUp) await Task.Delay(500).ConfigureAwait(false);
	}

	private async Task<(string Key, string? Detail)> RunActionAsync(string action, bool automatic)
	{
		var cancellation = CancellationToken.None;
		switch (action)
		{
			case "restart-plugins":
			{
				Stage("restarting");
				DateTimeOffset since = DateTimeOffset.Now;
				await _manager.RestartDeckyAsync().ConfigureAwait(false);
				return (await WaitForHeartbeatAsync(since, TimeSpan.FromSeconds(75), cancellation).ConfigureAwait(false) ? "repair.fixed" : "repair.stillBroken", null);
			}
			case "reenable-access":
			{
				await WaitForNoGameAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
				if (GameRunning()) return ("repair.notWhilePlaying", null);
				string? root = SteamRoot();
				if (root is null) return ("repair.failed", "Steam folder not found");
				string marker = Path.Combine(root, ".cef-enable-remote-debugging");
				if (!File.Exists(marker)) File.WriteAllText(marker, string.Empty);
				Stage("restarting-steam");
				DateTimeOffset since = DateTimeOffset.Now;
				using (_manager.BeginSteamMaintenance())
				{
					var restarted = await _manager.RestartSteamAsync().ConfigureAwait(false);
					if (!restarted.Ok) return ("repair.failed", restarted.Message);
				}
				bool fixedNow = await WaitForHeartbeatAsync(since, TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
				Notice(fixedNow ? "repair.fixed" : "repair.stillBroken");
				return (fixedNow ? "repair.fixed" : "repair.stillBroken", null);
			}
			case "update-decky":
			{
				Stage("updating-decky");
				string? exe = FindCoreAppExe();
				if (exe is null) return ("repair.failed", "Core app not found");
				Guid request = Guid.NewGuid();
				string receipt = Path.Combine(global::Playhub.Services.AppPaths.LocalDataRoot, "CoreRepair", request.ToString("N") + ".json");
				DateTimeOffset since = DateTimeOffset.Now;
				using (Process.Start(new ProcessStartInfo(exe, "decky-update " + request.ToString("N")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)! })) { }
				DateTime giveUp = DateTime.UtcNow.AddMinutes(6);
				while (!File.Exists(receipt) && DateTime.UtcNow < giveUp) await Task.Delay(1000).ConfigureAwait(false);
				if (!File.Exists(receipt)) return ("repair.failed", "Decky update timed out");
				bool ok = false; string? message = null;
				try
				{
					using JsonDocument document = JsonDocument.Parse(File.ReadAllText(receipt));
					ok = document.RootElement.TryGetProperty("ok", out JsonElement okValue) && okValue.ValueKind == JsonValueKind.True;
					message = document.RootElement.TryGetProperty("message", out JsonElement text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
				}
				finally { try { File.Delete(receipt); } catch { } }
				if (!ok) return ("repair.failed", message ?? "Decky update failed");
				return (await WaitForHeartbeatAsync(since, TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false) ? "repair.fixed" : "repair.stillBroken", null);
			}
			case "rollback-steam":
			{
				await WaitForNoGameAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
				DateTimeOffset since = DateTimeOffset.Now;
				SteamRollbackOutcome outcome;
				using (_manager.BeginSteamMaintenance()) outcome = await SteamUpdateJob.RollbackAsync(_steamHost, cancellation).ConfigureAwait(false);
				_logger.Info($"Steam rollback: {outcome}.");
				switch (outcome)
				{
					case SteamRollbackOutcome.NoBackup: return ("repair.noBackup", null);
					case SteamRollbackOutcome.NotWhilePlaying: return ("repair.notWhilePlaying", null);
					case SteamRollbackOutcome.RolledBack:
						SaveHold(state => state with { RollbackSuggested = false, InstalledVersion = SteamVersion() ?? state.InstalledVersion, LastOutcome = "rolled-back" });
						bool fixedNow = await WaitForHeartbeatAsync(since, TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
						Notice(fixedNow ? "repair.fixed" : "repair.stillBroken");
						return (fixedNow ? "repair.fixed" : "repair.stillBroken", null);
					default: return ("repair.failed", outcome.ToString());
				}
			}
			case "update-steam":
			case "verify-steam":
			{
				await WaitForNoGameAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
				if (GameRunning()) return ("repair.notWhilePlaying", null);
				if (automatic && action == "update-steam")
				{
					// Batch 1: Ludusavi backs up every game's saves first (time-boxed, once a night; a failure never stops the update).
					await BackupSavesAsync("Steam update").ConfigureAwait(false);
					if (GameRunning()) return ("repair.notWhilePlaying", null);
					Notice("notice.steamUpdating");
				}
				return await SteamUpdateJobAsync(action == "verify-steam", automatic, cancellation).ConfigureAwait(false);
			}
			case "continue":
			{
				_decky.DismissedForSteamVersion = SteamVersion() ?? "unknown";
				SaveHealth();
				return ("repair.continued", null);
			}
		}
		return ("repair.failed", "unknown action");
	}

	private void SaveHold(Func<SteamHoldState, SteamHoldState> change)
	{
		try { SteamUpdateHoldPolicy.Save(_holdPath, change(SteamUpdateHoldPolicy.Load(_holdPath))); }
		catch (Exception error) { _logger.Error("Steam update hold state could not be saved.", error); }
	}

	private static string? FindCoreAppExe()
	{
		try
		{
			using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playhub");
			if (key?.GetValue("InstallLocation") is string location && File.Exists(Path.Combine(location, "Playhub.exe")))
				return Path.Combine(location, "Playhub.exe");
		}
		catch { }
		string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playhub", "Playhub.exe");
		return File.Exists(fallback) ? fallback : null;
	}

	// ---------- Steam host for the update and rollback jobs ----------

	private sealed class SteamHost : ISteamUpdateHost
	{
		private readonly MarkIIIHost _owner;
		private string? _savedHold;
		internal SteamHost(MarkIIIHost owner) => _owner = owner;

		public bool GameRunning() => _owner.GameRunning();
		public DateTimeOffset Now() => DateTimeOffset.Now;
		public void Stage(SteamUpdateStage stage) { _owner.Stage(stage.ToString()); _owner._logger.Info($"Steam update job: {stage}."); }
		// Read while Steam still runs: the plugins Decky has loaded now are the ones expected back.
		public async Task<SmokeExpectations> ExpectationsAsync(CancellationToken cancellation)
		{
			IReadOnlyList<string>? loaded = _owner._decky.Verdict == DeckyVerdict.Healthy ? await LoadedPluginsAsync().ConfigureAwait(false) : null;
			SmokeExpectations expected = _owner.Expectations(loaded);
			_owner._logger.Info($"Steam update smoke test expects: {(expected.PluginsMatter ? "Big Picture, Decky's loader, Core's plugin and " + (expected.Plugins.Count == 0 ? "no listed plugins" : string.Join(", ", expected.Plugins)) : "Big Picture only (plugins did not work before)")}.");
			return expected;
		}

		// Polls every few seconds until each check passes or the limit ends. Big Picture counts only in
		// Gaming Mode (a desktop update starts Steam's normal window); Decky's loader is checked as Core's
		// Decky installer checks it (port 1337 has a listener); Core's plugin by a heartbeat since `since`.
		public async Task<SmokeResult> SmokeTestAsync(DateTimeOffset since, TimeSpan limit, SmokeExpectations expected, CancellationToken cancellation)
		{
			DateTimeOffset giveUp = DateTimeOffset.Now + limit;
			while (true)
			{
				bool steam = SteamProcessId() is not null;
				bool bigPicture = steam && (!_owner.InGamingMode() || SteamFullscreenDetector.IsBigPictureUp());
				bool loader = false;
				try { loader = DeckyStartupGuard.ListenerOwner(1337) is not null; } catch (Exception error) { _owner._logger.Error("Smoke test: Decky's port could not be checked.", error); }
				IReadOnlyList<string>? loaded = expected.PluginsMatter && expected.Plugins.Count > 0 && bigPicture && loader ? await LoadedPluginsAsync().ConfigureAwait(false) : null;
				SmokeResult result = SmokeTestPolicy.Judge(bigPicture, loader, expected, loaded, _owner._decky.HealthySince(since));
				if (result.Ok || DateTimeOffset.Now >= giveUp) return result;
				await Task.Delay(TimeSpan.FromSeconds(5), cancellation).ConfigureAwait(false);
			}
		}
		public Task RestartPluginsAsync(CancellationToken cancellation) => _owner._manager.RestartDeckyAsync();
		private static string BackupsRoot => global::Playhub.Services.AppPaths.SteamClientBackupsRoot;
		public bool HasClientBackup() => SteamClientBackup.Newest(BackupsRoot) is not null;

		public async Task<bool> StopSteamAsync(CancellationToken cancellation)
		{
			if (!SteamRunning()) return true;
			string? root = _owner.SteamRoot();
			try
			{
				if (root is not null && File.Exists(Path.Combine(root, "steam.exe")))
					using (Process.Start(new ProcessStartInfo(Path.Combine(root, "steam.exe"), "-shutdown") { UseShellExecute = false, CreateNoWindow = true })) { }
			}
			catch (Exception error) { _owner._logger.Error("Steam -shutdown could not be sent.", error); }
			for (int i = 0; i < 90 && SteamRunning(); i++) await Task.Delay(500, cancellation).ConfigureAwait(false);
			if (!SteamRunning()) return true;
			_owner._logger.Info("Steam did not close within 45 s; ending its processes.");
			foreach (string name in new[] { "steam", "steamwebhelper" })
			{
				Process[] processes = Process.GetProcessesByName(name);
				foreach (Process process in processes)
				{
					try { process.Kill(entireProcessTree: true); } catch { }
					process.Dispose();
				}
			}
			for (int i = 0; i < 20 && SteamRunning(); i++) await Task.Delay(500, cancellation).ConfigureAwait(false);
			return !SteamRunning();
		}

		public async Task<bool> StartSteamAsync(CancellationToken cancellation)
		{
			var result = await _owner._manager.RestartSteamAsync().ConfigureAwait(false);
			return result.Ok;
		}

		public Task<bool> BackupClientAsync(CancellationToken cancellation) => Task.Run(() =>
		{
			string? root = _owner.SteamRoot();
			if (root is null) return false;
			var made = SteamClientBackup.Create(root, BackupsRoot, _owner.SteamVersion(), DateTimeOffset.Now, cancellation);
			_owner._logger.Info(made.Ok ? $"Steam client backed up: {made.Files} files ({made.Skipped} skipped) in {made.Folder}." : $"Steam client backup failed: {made.Error}.");
			if (made.Ok) SteamClientBackup.Prune(BackupsRoot, keep: 2);
			return made.Ok;
		}, cancellation);

		public Task<bool> RestoreNewestBackupAsync(CancellationToken cancellation) => Task.Run(() =>
		{
			string? root = _owner.SteamRoot();
			string? backup = SteamClientBackup.Newest(BackupsRoot);
			if (root is null || backup is null) return false;
			var restored = SteamClientBackup.Restore(backup, root, cancellation);
			_owner._logger.Info(restored.Ok ? $"Steam client restored from {backup} ({restored.Files} files)." : $"Steam client restore failed: {restored.Error}.");
			return restored.Ok;
		}, cancellation);

		public bool RemoveHold()
		{
			string? root = _owner.SteamRoot();
			if (root is null) return false;
			string path = Path.Combine(root, SteamUpdateHoldPolicy.HoldFileName);
			try
			{
				if (!File.Exists(path)) return true;
				_savedHold = File.ReadAllText(path); // a hand-edited steam.cfg comes back as it was
				File.Delete(path);
				return true;
			}
			catch (Exception error) { _owner._logger.Error("steam.cfg could not be removed for the update.", error); return false; }
		}

		public bool ApplyHold()
		{
			string? root = _owner.SteamRoot();
			if (root is null) return false;
			try
			{
				string content = string.IsNullOrWhiteSpace(_savedHold) || !_savedHold.Contains("BootStrapperInhibitAll", StringComparison.OrdinalIgnoreCase)
					? SteamUpdateHoldPolicy.HoldFileContent : _savedHold;
				File.WriteAllText(Path.Combine(root, SteamUpdateHoldPolicy.HoldFileName), content);
				return true;
			}
			catch (Exception error) { _owner._logger.Error("steam.cfg could not be written back; Steam may update itself at its next start.", error); return false; }
		}
	}

	// ---------- power events ----------

	private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
	{
		try
		{
			if (e.Mode == PowerModes.Suspend)
			{
				// Jarvis: the session (history, screen answer, any recording) is forgotten when the PC sleeps.
				try { ClearJarvis("the PC went to sleep"); } catch (Exception error) { _logger.Error("Jarvis: could not clear the session before sleep.", error); }
				bool forStream = _sleepingForStream;
				if (!forStream) { try { RememberStreamAtSleep(); } catch (Exception error) { _logger.Error("Stream mode: could not note the stream before sleep.", error); } }
				// Batch 2: Home Assistant hears "asleep" (bounded, the sleep does not wait for the broker).
				try { AnnounceSleepAsync().Wait(TimeSpan.FromMilliseconds(1200)); } catch (Exception error) { _logger.Error("Home Assistant: could not announce the sleep.", error); }
				// Sleep after streaming: the TV belongs to whoever watches it; leave it as it is.
				if (forStream) _logger.Info("TV on sleep: left alone (sleep after streaming).");
				else TurnTvOffIfShowingCore("sleep", TimeSpan.FromMilliseconds(1800));
			}
			else if (e.Mode == PowerModes.Resume) OnResume();
		}
		catch (Exception error) { _logger.Error("Mark III power event failed.", error); }
	}

	private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
	{
		try { RememberStreamAtSleep(); } catch (Exception error) { _logger.Error("Stream mode: could not note the stream before shutdown.", error); }
		try { AnnounceSleepAsync().Wait(TimeSpan.FromMilliseconds(1200)); } catch (Exception error) { _logger.Error("Home Assistant: could not announce the shutdown.", error); }
		try { TurnTvOffIfShowingCore(e.Reason == SessionEndReasons.SystemShutdown ? "shutdown" : "sign-out", TimeSpan.FromMilliseconds(2500)); }
		catch (Exception error) { _logger.Error("Mark III shutdown handling failed.", error); }
	}

	// Windows waits only briefly on sleep and shutdown, so this is bounded and synchronous.
	private void TurnTvOffIfShowingCore(string why, TimeSpan budget)
	{
		TvSettings tv = _store.LoadConfig().Gaming.Tv ?? new TvSettings();
		if (!tv.Enabled || !tv.TurnOffWithCore) return;
		try
		{
			Task<TvResult> off = _tv.TurnOffIfShowingCoreAsync(budget, CancellationToken.None);
			TvResult result = off.Wait(budget + TimeSpan.FromMilliseconds(300)) ? off.Result : new TvResult(TvOutcome.Unreachable);
			_logger.Info($"TV on {why}: {result.Outcome}.");
		}
		catch (Exception error) { _logger.Error($"TV on {why} failed; left as it is.", error); }
	}

	// Windows raises Resume for a wake the user caused (PBT_APMRESUMESUSPEND), not for an
	// unattended wake timer, so a 3 a.m. maintenance wake does not turn the TV on.
	private void OnResume()
	{
		ModeConfig config = _store.LoadConfig();
		bool gaming = InGamingMode();
		_logger.Info($"Mark III: resumed from sleep (Gaming Mode: {gaming}).");
		try { ReconcileMic("resume"); }
		catch (Exception error) { _logger.Error("Voice: microphone check after resume failed.", error); }
		try { OnResumeBatchTwo(); }
		catch (Exception error) { _logger.Error("Batch 2: resume handling failed.", error); }
		bool streamedAtSleep;
		lock (_streamSync) streamedAtSleep = _streamSettings.StreamingAtSleep;
		if (!gaming) { ClearStreamAtSleep(); return; }
		StreamState streamNow = CheckStream();
		NoteWakeTvOutcome(null);
		TvSettings tv = config.Gaming.Tv ?? new TvSettings();
		// Batch 2: a "wake -> wake TV" automation turns on this same path (one wake, one cooldown; CoreAutomations.cs).
		bool wakeTv = tv.Enabled && CoreAutomations.WakeTvOnResume(tv.TurnOnWithCore, Rules());
		if (wakeTv) NoteTvWake();
		Task tvReady = wakeTv
			? Task.Run(async () =>
			{
				// Stream mode: a stream running now, or one that ended with the sleep and comes back within
				// the grace period, keeps the TV as it is (the hold inside TvController checks again).
				string? held = await StreamWakeHoldAsync().ConfigureAwait(false);
				if (held is not null) { _logger.Info($"TV on wake: Stream mode ({held}); nothing sent to the TV."); return; }
				TvResult result = await _tv.WakeAndSwitchAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
				_logger.Info($"TV on wake: {result.Outcome}.");
				NoteWakeTvOutcome(result.Outcome);
				if (result.Outcome == TvOutcome.Done) await Task.Delay(1200).ConfigureAwait(false);
			})
			: Task.CompletedTask;
		// Console guards: after the TV wake, check Big Picture, the TV input and the network (once each).
		try { StartWakeCheck(tvReady); }
		catch (Exception error) { _logger.Error("Console guards: wake check could not start.", error); }
		// No TV wake to make the stream decision: the note from before the sleep has done its job.
		if (!wakeTv) ClearStreamAtSleep();
		var decision = BootSequencePolicy.Decide(BootSequenceTrigger.Wake, config.Gaming.Splash.BootSequenceEnabled, config.Gaming.Splash.BootSoundEnabled,
			config.Gaming.Splash.Enabled, gaming, RunningAppId() != 0, _antiCheat.Engaged);
		if (decision.Play && (streamNow.Active || streamedAtSleep))
			decision = decision with { Play = false, Reason = streamNow.Active ? "Stream mode is on" : "the PC slept while streaming" };
		if (!decision.Play) { _logger.Info($"Wake sequence skipped: {decision.Reason}."); return; }
		// A separate splash service: the boot splash's foreground guard belongs to the boot.
		var splash = new SplashScreenService(_logger);
		splash.ShowWakeSequence(decision.Sound, tvReady);
		_ = Task.Run(async () =>
		{
			await Task.Delay(BootSequencePolicy.HardLimit + TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			splash.Dispose();
		});
	}

	// ---------- HTTP ----------

	private static bool Local(HttpContext context) => context.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip);

	private object RepairState()
	{
		JobState job; lock (_sync) job = _jobState;
		string? root = SteamRoot();
		SteamHoldState hold = SteamUpdateHoldPolicy.Load(_holdPath);
		int soak = SoakDays();
		SteamHoldView view = SteamUpdateHoldPolicy.View(hold, SteamUpdateHoldPolicy.IsHoldFilePresent(root), soak, Fingerprint(hold), DateTimeOffset.Now);
		return new
		{
			busy = job.Busy, action = job.Action, stage = job.Stage, messageKey = job.MessageKey, detail = job.Detail,
			rollbackSuggested = hold.RollbackSuggested, hasBackup = SteamClientBackup.Newest(global::Playhub.Services.AppPaths.SteamClientBackupsRoot) is not null,
			holdPhase = view.Phase.ToString(), daysLeft = view.DaysLeft, soakDays = soak, soakEnds = view.SoakEnds?.ToString("yyyy-MM-dd"), attention = view.Attention,
			steamVersion = SteamVersion(), pendingVersion = hold.PendingVersion, postponedSince = hold.PostponedSince, postponedReason = hold.PostponedReason,
			failedCheck = hold.FailedCheck, failedDetail = hold.FailedDetail, lastUpdateAt = hold.LastUpdateAt, lastOutcome = hold.LastOutcome,
			plugins = _decky.Verdict.ToString(), lastHeartbeat = _decky.EverHealthy ? _decky.LastHeartbeat : null,
		};
	}

	private object TvState()
	{
		TvSettings tv = _store.LoadConfig().Gaming.Tv ?? new TvSettings();
		// Never the key itself: only whether one is stored.
		return new { enabled = tv.Enabled, paired = _pairing.ClientKey is not null, address = tv.Address, macAddress = tv.MacAddress, coreInput = TvController.CoreInput(tv) };
	}

	private static IResult Tv(TvResult result) => Results.Json(new { ok = result.Ok, outcome = result.Outcome.ToString(), macAddress = result.MacAddress });

	internal void Map(WebApplication app)
	{
		app.MapPost("/decky/heartbeat", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			Heartbeat();
			// Batch 2: seconds since Core's plugin last saw Steam controller input (sleep after streaming).
			NoteSteamInput(context.Request.Query["inputAgo"].ToString());
			// Batch 1: a waiting wind-down reminder rides on the answer once (shown with Steam's toaster, over games).
			return Results.Json(new { ok = true, toast = TakeToast() });
		}));
		app.MapGet("/decky/health", (Func<HttpContext, IResult>)(context =>
			Local(context) ? Results.Json(new { verdict = _decky.Verdict.ToString(), everHealthy = _decky.EverHealthy, lastHeartbeat = _decky.LastHeartbeat }) : Results.StatusCode(403)));
		app.MapGet("/core/repair/shortcut", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string exe = Environment.ProcessPath ?? "";
			return Results.Json(new { name = "Core Repair", exe, startDir = Path.GetDirectoryName(exe) ?? "", launchOptions = "repair" });
		}));
		app.MapPost("/core/repair/run", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string action = context.Request.Query["action"].ToString();
			if (!RequestableActions.Contains(action)) return Results.Json(new { ok = false, error = "unknown action" }, statusCode: 400);
			// Core Repair's "Update Steam now" goes through the same gate and confirmations as the app's.
			if (action == "update-steam") return await UpdateSteamNowAsync(context).ConfigureAwait(false);
			return TryRunJob(action) ? Results.Json(new { ok = true, started = action }, statusCode: 202) : Results.Json(new { ok = false, busy = true }, statusCode: 409);
		}));
		app.MapGet("/core/repair/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(RepairState()) : Results.StatusCode(403)));
		app.MapGet("/steam/update/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(RepairState()) : Results.StatusCode(403)));
		app.MapPost("/steam/update/now", (Func<HttpContext, Task<IResult>>)(async context =>
			Local(context) ? await UpdateSteamNowAsync(context).ConfigureAwait(false) : Results.StatusCode(403)));

		app.MapGet("/windows/update-night", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(UpdateNightView()) : Results.StatusCode(403)));
		app.MapPost("/windows/update-night/schedule", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			DateTimeOffset now = DateTimeOffset.Now;
			UpdateNightState state = UpdateNightPolicy.Observe(UpdateNightPolicy.Load(_updateNightPath), now) with { ScheduledFor = UpdateNightPolicy.NextRestart(now) };
			UpdateNightPolicy.Save(_updateNightPath, state);
			_logger.Info($"Update night scheduled for {state.ScheduledFor:yyyy-MM-dd HH:mm}.");
			return Results.Json(UpdateNightView());
		}));
		app.MapPost("/windows/update-night/cancel", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			UpdateNightPolicy.Save(_updateNightPath, UpdateNightPolicy.Load(_updateNightPath) with { ScheduledFor = null });
			_logger.Info("Update night cancelled.");
			return Results.Json(UpdateNightView());
		}));
		app.MapGet("/tv/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(TvState()) : Results.StatusCode(403)));
		app.MapPost("/tv/discover", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			var found = await _tv.DiscoverAsync(context.RequestAborted).ConfigureAwait(false);
			return Results.Json(new { ok = true, tvs = found.Select(tv => new { address = tv.Address, name = tv.Name }).ToArray() });
		}));
		app.MapPost("/tv/pair", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			return Tv(await _tv.PairAsync(() => _logger.Info("TV: pairing prompt is on the TV; accept it with the remote."), context.RequestAborted).ConfigureAwait(false));
		}));
		app.MapPost("/tv/unpair", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			_pairing.Clear();
			_logger.Info("TV: pairing removed.");
			return Results.Json(new { ok = true });
		}));
		app.MapPost("/tv/on", (Func<HttpContext, Task<IResult>>)(async context =>
			Local(context) ? Tv(await _tv.TurnOnAsync(context.RequestAborted).ConfigureAwait(false)) : Results.StatusCode(403)));
		app.MapPost("/tv/input", (Func<HttpContext, Task<IResult>>)(async context =>
			Local(context) ? Tv(await _tv.SwitchCoreInputAsync(context.RequestAborted).ConfigureAwait(false)) : Results.StatusCode(403)));
		app.MapPost("/tv/off", (Func<HttpContext, Task<IResult>>)(async context =>
			Local(context) ? Tv(await _tv.TurnOffNowAsync(context.RequestAborted).ConfigureAwait(false)) : Results.StatusCode(403)));
		MapHealth(app);
		MapVoice(app);
		MapStream(app);
		MapConsole(app);
		MapBatch(app);
		MapBatchTwo(app);
		MapJarvis(app);
		MapEmulators(app);
	}
}
