using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Win32;
using Playhub.Shared;

namespace GamingMode.Services;

// Eternal Core Mark III in the agent: TV control, the Steam update hold, the Decky health
// check, Core Repair's actions, the wake sequence and their HTTP endpoints. Core Health (the
// Quick Access health section and its notices) is in MarkIIIHost.Health.cs; Core Voice (microphone
// mute and the Xbox party button) in MarkIIIHost.Voice.cs.
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
		_tv = new TvController(() => _store.LoadConfig().Gaming.Tv ?? new TvSettings(), _pairing, logger.Info, NetworkMac.Resolve);
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
				var controller = new TvController(() => tv, new DpapiTvPairingStore(paths.ConfigDirectory, logger), logger.Info, NetworkMac.Resolve);
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
			if (++tick % 12 != 0) continue; // once a minute
			try { CheckSteamHold(); }
			catch (Exception error) { _logger.Error("Mark III: Steam update hold check failed this round.", error); }
			try { CheckUpdateNight(); }
			catch (Exception error) { _logger.Error("Mark III: update night check failed this round.", error); }
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

	private void Notice(string key)
	{
		if (!InGamingMode() || GameRunning()) { _logger.Info($"Core notice \"{key}\" not shown: not in Big Picture or a game is running."); return; }
		CoreNoticeOverlay.Show(CoreText.Get(Language(), key), TimeSpan.FromSeconds(10), () => InGamingMode() && !GameRunning(), _logger);
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

	private void CheckUpdateNight()
	{
		DateTimeOffset now = DateTimeOffset.Now;
		UpdateNightState state = UpdateNightPolicy.Observe(UpdateNightPolicy.Load(_updateNightPath), now);
		switch (UpdateNightPolicy.Decide(state, now, GameRunning()))
		{
			case UpdateNightAction.Restart:
				UpdateNightPolicy.Save(_updateNightPath, state with { ScheduledFor = null, LastNight = now });
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
		Notice(_decky.AfterSteamUpdate(version) ? "notice.pluginsAfterUpdate" : "notice.plugins");
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

	// ---------- Steam update hold ----------

	private void CheckSteamHold()
	{
		if (GameRunning()) _quietSince = DateTimeOffset.Now;
		string? root = SteamRoot();
		if (root is null) return;
		DateTimeOffset now = DateTimeOffset.Now;
		SteamHoldState state = SteamUpdateHoldPolicy.Load(_holdPath);
		SteamHoldObservation seen = SteamUpdateHoldPolicy.Observe(state, SteamUpdateHoldPolicy.IsHoldFilePresent(root), SteamVersion(), now);
		SteamHoldState next = seen.Next;
		if (seen.UpdatedDespiteHold)
			_logger.Info($"Steam updated to {next.HeldVersion} although its updates were on hold (steam.cfg present). The hold restarts from this version; the Decky health check judges it.");
		if (seen.Phase == SteamHoldPhase.Expired && !next.ExpiryNotified)
		{
			_logger.Info($"Steam updates have been on hold for {SteamUpdateHoldPolicy.MaxHoldDays} days; Core updates Steam at the next idle moment.");
			Notice("notice.steamHoldExpired");
			next = next with { ExpiryNotified = true };
		}
		if (next != state) SteamUpdateHoldPolicy.Save(_holdPath, next);
		DateTimeOffset quiet = _quietSince > _started ? _quietSince : _started;
		if (seen.Phase == SteamHoldPhase.Expired && SteamUpdateHoldPolicy.IsIdleMoment(InGamingMode(), SteamProcessId() is not null, RunningAppId() != 0, _antiCheat.Engaged, quiet, now))
		{
			_logger.Info("Idle moment after an expired hold: updating Steam now.");
			_ = TryRunJob("update-steam", automatic: true);
		}
	}

	// ---------- Core Repair actions (one at a time) ----------

	private static readonly string[] Actions = { "restart-plugins", "reenable-access", "update-decky", "rollback-steam", "update-steam", "continue" };

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
						SaveHold(state => state with { RollbackSuggested = false, HeldSince = DateTimeOffset.Now, HeldVersion = SteamVersion(), ExpiryNotified = false, LastOutcome = "rolled-back" });
						bool fixedNow = await WaitForHeartbeatAsync(since, TimeSpan.FromMinutes(2), cancellation).ConfigureAwait(false);
						Notice(fixedNow ? "repair.fixed" : "repair.stillBroken");
						return (fixedNow ? "repair.fixed" : "repair.stillBroken", null);
					default: return ("repair.failed", outcome.ToString());
				}
			}
			case "update-steam":
			{
				await WaitForNoGameAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
				if (GameRunning()) return ("repair.notWhilePlaying", null);
				if (automatic) Notice("notice.steamUpdating");
				SteamUpdateOutcome outcome;
				using (_manager.BeginSteamMaintenance()) outcome = await SteamUpdateJob.RunAsync(_steamHost, cancellation).ConfigureAwait(false);
				_logger.Info($"Steam update: {outcome} (client {SteamVersion() ?? "unknown"}).");
				switch (outcome)
				{
					case SteamUpdateOutcome.Updated:
						SaveHold(state => state with { HeldSince = DateTimeOffset.Now, HeldVersion = SteamVersion(), KnownGoodVersion = SteamVersion(), LastUpdateAt = DateTimeOffset.Now, LastOutcome = "updated", ExpiryNotified = false, RollbackSuggested = false });
						Notice("repair.steamUpdated");
						return ("repair.steamUpdated", null);
					case SteamUpdateOutcome.UpdatedPluginsBroken:
						SaveHold(state => state with { HeldSince = DateTimeOffset.Now, HeldVersion = SteamVersion(), LastUpdateAt = DateTimeOffset.Now, LastOutcome = "plugins-broken", ExpiryNotified = false, RollbackSuggested = true });
						Notice("notice.steamUpdatedPluginsBroken");
						return ("notice.steamUpdatedPluginsBroken", null);
					case SteamUpdateOutcome.NotWhilePlaying: return ("repair.notWhilePlaying", null);
					default: return ("repair.failed", outcome.ToString());
				}
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
		public bool PluginsWereHealthy() => _owner.PluginsExpected() && _owner._decky.EverHealthy;
		public Task<bool> WaitForPluginsAsync(DateTimeOffset since, TimeSpan timeout, CancellationToken cancellation) => _owner.WaitForHeartbeatAsync(since, timeout, cancellation);
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
			if (e.Mode == PowerModes.Suspend) TurnTvOffIfShowingCore("sleep", TimeSpan.FromMilliseconds(1800));
			else if (e.Mode == PowerModes.Resume) OnResume();
		}
		catch (Exception error) { _logger.Error("Mark III power event failed.", error); }
	}

	private void OnSessionEnding(object? sender, SessionEndingEventArgs e)
	{
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
		if (!gaming) return;
		TvSettings tv = config.Gaming.Tv ?? new TvSettings();
		Task tvReady = tv.Enabled && tv.TurnOnWithCore
			? Task.Run(async () =>
			{
				TvResult result = await _tv.WakeAndSwitchAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
				_logger.Info($"TV on wake: {result.Outcome}.");
				if (result.Outcome == TvOutcome.Done) await Task.Delay(1200).ConfigureAwait(false);
			})
			: Task.CompletedTask;
		var decision = BootSequencePolicy.Decide(BootSequenceTrigger.Wake, config.Gaming.Splash.BootSequenceEnabled, config.Gaming.Splash.BootSoundEnabled,
			config.Gaming.Splash.Enabled, gaming, RunningAppId() != 0, _antiCheat.Engaged);
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
		SteamHoldObservation seen = SteamUpdateHoldPolicy.Observe(hold, SteamUpdateHoldPolicy.IsHoldFilePresent(root), SteamVersion(), DateTimeOffset.Now);
		return new
		{
			busy = job.Busy, action = job.Action, stage = job.Stage, messageKey = job.MessageKey, detail = job.Detail,
			rollbackSuggested = hold.RollbackSuggested, hasBackup = SteamClientBackup.Newest(global::Playhub.Services.AppPaths.SteamClientBackupsRoot) is not null,
			holdPhase = seen.Phase.ToString(), daysLeft = seen.DaysLeft, heldSince = seen.Next.HeldSince, steamVersion = SteamVersion(),
			lastUpdateAt = hold.LastUpdateAt, lastOutcome = hold.LastOutcome,
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
			return Results.Json(new { ok = true });
		}));
		app.MapGet("/decky/health", (Func<HttpContext, IResult>)(context =>
			Local(context) ? Results.Json(new { verdict = _decky.Verdict.ToString(), everHealthy = _decky.EverHealthy, lastHeartbeat = _decky.LastHeartbeat }) : Results.StatusCode(403)));
		app.MapGet("/core/repair/shortcut", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string exe = Environment.ProcessPath ?? "";
			return Results.Json(new { name = "Core Repair", exe, startDir = Path.GetDirectoryName(exe) ?? "", launchOptions = "repair" });
		}));
		app.MapPost("/core/repair/run", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string action = context.Request.Query["action"].ToString();
			if (!Actions.Contains(action)) return Results.Json(new { ok = false, error = "unknown action" }, statusCode: 400);
			return TryRunJob(action) ? Results.Json(new { ok = true, started = action }, statusCode: 202) : Results.Json(new { ok = false, busy = true }, statusCode: 409);
		}));
		app.MapGet("/core/repair/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(RepairState()) : Results.StatusCode(403)));
		app.MapGet("/steam/update/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(RepairState()) : Results.StatusCode(403)));
		app.MapPost("/steam/update/now", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			return TryRunJob("update-steam") ? Results.Json(new { ok = true }, statusCode: 202) : Results.Json(new { ok = false, busy = true }, statusCode: 409);
		}));

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
	}
}
