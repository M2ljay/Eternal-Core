using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Shared;

namespace GamingMode.Services;

// Console guards in the agent (rules in ConsoleGuards.cs, Windows calls in ConsoleGuards.Windows.cs,
// docs/eternal/CONSOLE-GUARDS.md): gathers their observations for Core Health, runs the one-button fixes
// Quick Access offers (POST /health/action), pushes launcher pop-ups back behind Big Picture and checks
// Big Picture, the TV input and the network after a wake.
//
// Cost: the slow probes (BitLocker through the Shell, firmware, sign-in, policy, controllers, launcher
// updates, the guard task) run at most once a minute on a pool thread with a 2.5 s time box for Quick
// Access, which shows the previous answer meanwhile. Nothing here needs administrator rights, nothing
// reads a password or a key, and every registry change is recorded first (CouchHardening backups).
internal sealed partial class MarkIIIHost
{
	private string _consolePath = "";
	private readonly object _consoleSync = new();
	private sealed record ConsoleSlow(string? BitLocker, IReadOnlyList<string> Firmware, AutoSignInState? AutoSignIn, UpdatePolicyState UpdatePolicy,
		bool? PromptsOff, IReadOnlyList<ControllerSeen>? Controllers, IReadOnlyList<string> LauncherUpdates, bool GuardInstalled);
	private (DateTimeOffset At, ConsoleSlow Slow)? _consoleSlow;
	private Task<ConsoleSlow>? _consoleReading;
	private static readonly TimeSpan ConsoleCacheFor = TimeSpan.FromMinutes(1), ConsoleReadBudget = TimeSpan.FromMilliseconds(2500);
	private WindowLifecycleObserver? _foregroundWatcher;
	private bool _bigPictureWasForeground;
	private int _launcherPushBusy;
	// How the TV wake on the last resume ended (only Done means Core itself switched the TV to its input).
	private TvOutcome? _wakeTvOutcome;
	private static readonly Regex ActionId = new("^[a-z0-9-]{1,40}$", RegexOptions.CultureInvariant);

	private static string BitLockerCheckPath => Path.Combine(global::Playhub.Services.AppPaths.LocalDataRoot, BitLockerGuard.CheckFileName);
	// The same backup Core's desktop "Quiet Windows" card writes and undoes, so its Undo reverts this too.
	private static string QuietUserBackup => Path.Combine(global::Playhub.Services.AppPaths.LocalDataRoot, "couch-user.json");

	// ---------- lifetime ----------

	private void StartConsole(CancellationToken stopping)
	{
		_consolePath = Path.Combine(_paths.ConfigDirectory, "console-guards.json");
		try { _foregroundWatcher = new WindowLifecycleObserver(() => { }, OnForegroundChanged, watchLifecycle: false); }
		catch (Exception error) { _logger.Error("Console guards: foreground watch unavailable; launcher pop-ups are left alone.", error); }
		// At start, compare the display and sound with the saved normal ones once things have settled.
		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(90), stopping).ConfigureAwait(false);
				await CheckHealthAlertsAsync().ConfigureAwait(false);
			}
			catch (OperationCanceledException) { }
			catch (Exception error) { _logger.Error("Console guards: first check after start failed.", error); }
		});
	}

	private void StopConsole()
	{
		try { _foregroundWatcher?.Dispose(); } catch { }
	}

	private ConsoleGuardState LoadConsole() { lock (_consoleSync) return ConsoleGuardStore.Load(_consolePath); }

	private void SaveConsole(Func<ConsoleGuardState, ConsoleGuardState> change)
	{
		lock (_consoleSync)
		{
			try { ConsoleGuardStore.Save(_consolePath, change(ConsoleGuardStore.Load(_consolePath))); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.Error("Console guards: state could not be saved.", error); }
		}
	}

	// ---------- observations ----------

	private ConsoleSlow ReadConsoleSlow()
	{
		T Safe<T>(Func<T> read, T fallback, string what)
		{
			try { return read(); }
			catch (Exception error) { _logger.Error($"Console guards: {what} could not be read this round.", error); return fallback; }
		}
		return new ConsoleSlow(
			Safe<string?>(ConsoleProbes.BitLockerProtection, null, "BitLocker"),
			Safe<IReadOnlyList<string>>(ConsoleProbes.FirmwarePending, Array.Empty<string>(), "staged firmware"),
			Safe<AutoSignInState?>(ConsoleProbes.AutoSignIn, null, "automatic sign-in"),
			Safe<UpdatePolicyState>(ConsoleProbes.UpdatePolicy, new UpdatePolicyState(null, false, false, false, null, null), "update policy"),
			Safe<bool?>(ConsoleProbes.PromptsOff, null, "Windows setup screens"),
			Safe<IReadOnlyList<ControllerSeen>?>(WindowsControllers.Read, null, "controllers"),
			Safe<IReadOnlyList<string>>(ConsoleProbes.PendingLauncherUpdates, Array.Empty<string>(), "launcher updates"),
			Safe<bool>(BitLockerGuardWindows.IsInstalled, false, "BitLocker guard task"));
	}

	private async Task<ConsoleSlow?> ConsoleSlowAsync(bool fresh)
	{
		Task<ConsoleSlow> reading;
		lock (_consoleSync)
		{
			if (!fresh && _consoleSlow is { } cached && DateTimeOffset.Now - cached.At < ConsoleCacheFor) return cached.Slow;
			if (_consoleReading is null || _consoleReading.IsCompleted)
			{
				_consoleReading = Task.Run(() => ReadConsoleSlow());
				_ = _consoleReading.ContinueWith(task =>
				{
					if (task.IsCompletedSuccessfully) lock (_consoleSync) _consoleSlow = (DateTimeOffset.Now, task.Result);
				}, TaskScheduler.Default);
			}
			reading = _consoleReading;
			// Quick Access does not wait for a refresh when an older answer exists; it gets the new one next time.
			if (!fresh && _consoleSlow is { } older) return older.Slow;
		}
		if (await Task.WhenAny(reading, Task.Delay(ConsoleReadBudget)).ConfigureAwait(false) == reading && reading.IsCompletedSuccessfully) return reading.Result;
		lock (_consoleSync) return _consoleSlow?.Slow;
	}

	private async Task<ConsoleGuardInputs> GatherConsoleAsync(bool fresh)
	{
		ConsoleSlow? slow = await ConsoleSlowAsync(fresh).ConfigureAwait(false);
		ConsoleGuardState state = LoadConsole();
		GamingSettings gaming = _store.LoadConfig().Gaming;
		DateTimeOffset now = DateTimeOffset.Now;
		IReadOnlyList<DisplayProfile>? displays = null;
		try { displays = WindowsDisplays.Read(); } catch (Exception error) { _logger.Error("Console guards: displays could not be read.", error); }
		DisplayProfile? current = displays is null ? null : state.Display is { } saved ? ConsoleGuardPolicy.Match(saved, displays) : displays.FirstOrDefault(display => display.Primary);
		SoundDevice? sound = null;
		IReadOnlyList<string> available = Array.Empty<string>();
		try
		{
			IReadOnlyList<SpeakerEndpoint> speakers = WindowsSpeakers.Read();
			available = speakers.Select(speaker => speaker.Id).ToList();
			if (speakers.FirstOrDefault(speaker => speaker.IsDefault) is { } chosen) sound = new SoundDevice(chosen.Id, chosen.Name);
		}
		catch (Exception error) { _logger.Error("Console guards: sound devices could not be read.", error); }
		SteamSignIn? steam = null;
		try { steam = ConsoleProbes.Steam(SteamRoot(), SteamRunning()); } catch (Exception error) { _logger.Error("Console guards: Steam sign-in could not be read.", error); }
		bool? ethernet = null;
		string? adapter = null;
		try { (ethernet, adapter) = ConsoleProbes.Ethernet(); } catch (Exception error) { _logger.Error("Console guards: network could not be read.", error); }
		BitLockerGuard.CheckResult? check = BitLockerGuard.Load(BitLockerCheckPath);
		return new ConsoleGuardInputs
		{
			BitLockerProtection = slow?.BitLocker,
			BitLockerCheck = check is null ? null : new BitLockerCheck(check.CheckedAt, check.Found, check.Protection, check.Protectors, check.BackupSeenAt),
			BitLockerGuardInstalled = slow?.GuardInstalled ?? false,
			FirmwarePending = slow?.Firmware ?? Array.Empty<string>(),
			BitLockerSuspendedByCoreAt = ConsoleGuardStore.SuspendedThisBoot(state, ConsoleProbes.BootTime()),
			AutoSignIn = slow?.AutoSignIn,
			DisplaySaved = state.Display, DisplayCurrent = current, DisplayRead = displays is not null,
			SoundSaved = state.Sound, SoundCurrent = sound, SoundAvailable = available,
			Controllers = slow?.Controllers, ExpectedControllers = state.ExpectedControllers,
			Steam = steam,
			PromptsOff = slow?.PromptsOff,
			UpdatePolicy = slow?.UpdatePolicy,
			PendingLauncherUpdates = slow?.LauncherUpdates ?? Array.Empty<string>(),
			LauncherFocusEnabled = gaming.ConsoleLauncherFocusGuard, LauncherPushedToday = ConsoleGuardStore.PushesToday(state, now), LauncherLastPushed = state.LauncherLastPushed,
				LauncherClosedToday = GameExitStore.ClosedToday(GameExitStore.Load(_gameExitPath), now), LauncherLastClosed = GameExitStore.Load(_gameExitPath).LastLauncherClosed,
			WakeChecksEnabled = gaming.ConsoleWakeChecks, LastWake = state.LastWake, EthernetUp = ethernet, EthernetName = adapter,
		};
	}

	// ---------- BitLocker: one restart's suspension before a staged firmware update ----------

	// Only when BitLocker is on, firmware is staged, the desktop installed the guard task and Core has not
	// already asked since the last boot (FirmwarePolicy.ShouldSuspend). Bounded and synchronous.
	private bool SuspendBitLockerForFirmware(string why, bool force = false)
	{
		try
		{
			ConsoleGuardState state = LoadConsole();
			DateTimeOffset boot = ConsoleProbes.BootTime();
			IReadOnlyList<string> firmware = ConsoleProbes.FirmwarePending();
			bool installed = BitLockerGuardWindows.IsInstalled();
			string? protection = ConsoleProbes.BitLockerProtection();
			if (!force && !FirmwarePolicy.ShouldSuspend(protection, firmware, installed, ConsoleGuardStore.SuspendedThisBoot(state, boot))) return false;
			if (force && (!installed || protection is not ("on" or "encrypting"))) return false;
			bool started = BitLockerGuardWindows.Run();
			_logger.Info(started ? $"Console guards: BitLocker suspended for one restart ({why}; staged firmware: {string.Join(", ", firmware)})."
				: $"Console guards: the BitLocker guard task did not start ({why}). Reinstall it from Core's desktop app.");
			if (started) SaveConsole(saved => saved with { BitLockerSuspendedAt = DateTimeOffset.Now, BitLockerSuspendedBoot = boot });
			lock (_consoleSync) _consoleSlow = null;
			return started;
		}
		catch (Exception error) { _logger.Error($"Console guards: BitLocker suspension before {why} failed; nothing changed.", error); return false; }
	}

	// ---------- launcher pop-ups ----------

	// Raised on the watcher's own thread for every foreground change; cheap unless a launcher took the screen.
	private void OnForegroundChanged(nint window)
	{
		try
		{
			string? process = LauncherWindows.ProcessName(window);
			bool wasBigPicture = _bigPictureWasForeground;
			_bigPictureWasForeground = process is "steamwebhelper" or "steam" && InGamingMode();
			if (LauncherFocusPolicy.LauncherName(process) is not { } launcher) return;
			GamingSettings gaming = _store.LoadConfig().Gaming;
			ConsoleGuardState state = LoadConsole();
			DateTimeOffset now = DateTimeOffset.Now;
			LauncherFocusAction action = LauncherFocusPolicy.Decide(new LauncherFocusInput(gaming.ConsoleLauncherFocusGuard, InGamingMode(), GameRunning(), StreamHealth().Active,
				wasBigPicture, process!, state.LauncherPushes, now));
			if (action == LauncherFocusAction.Leave) return;
			if (action == LauncherFocusAction.GiveUp) { _logger.Info($"Console guards: {launcher} came back to the front three times in ten minutes; left in front (wanted)."); return; }
			if (Interlocked.Exchange(ref _launcherPushBusy, 1) == 1) return;
			_ = Task.Run(() =>
			{
				try
				{
					bool pushed = LauncherWindows.PushBack(window);
					SteamFullscreenDetector.TryFocusSteamWindow(_logger);
					_logger.Info($"Console guards: {launcher} popped over Big Picture; {(pushed ? "pushed behind it" : "could not be moved")} and Big Picture focused again. Nothing was closed.");
					SaveConsole(saved => ConsoleGuardStore.RecordPush(saved, launcher, now));
				}
				catch (Exception error) { _logger.Error("Console guards: launcher push-back failed; the window stays where it is.", error); }
				finally { Interlocked.Exchange(ref _launcherPushBusy, 0); }
			});
		}
		catch (Exception error) { _logger.Error("Console guards: foreground check failed.", error); }
	}

	// ---------- after a wake ----------

	private void StartWakeCheck(Task tvReady)
	{
		if (!_store.LoadConfig().Gaming.ConsoleWakeChecks) return;
		_ = Task.Run(async () =>
		{
			try
			{
				try { await tvReady.ConfigureAwait(false); } catch { }
				await Task.Delay(WakeCheckPolicy.Delay).ConfigureAwait(false);
				TvOutcome? outcome;
				lock (_consoleSync) outcome = _wakeTvOutcome;
				await RunWakeCheckAsync(outcome == TvOutcome.Done).ConfigureAwait(false);
				lock (_consoleSync) _consoleSlow = null;
				await CheckHealthAlertsAsync().ConfigureAwait(false);
			}
			catch (Exception error) { _logger.Error("Console guards: wake check failed; nothing changed.", error); }
		});
	}

	private void NoteWakeTvOutcome(TvOutcome? outcome) { lock (_consoleSync) _wakeTvOutcome = outcome; }

	// Big Picture in front (re-focused once), the TV on Core's input (re-sent once, only if Core switched
	// it on this wake), the Ethernet link. The result is the "After waking" row.
	private async Task RunWakeCheckAsync(bool coreSwitchedTv)
	{
		bool gaming = InGamingMode();
		if (!gaming) return;
		bool game = GameRunning();
		bool up = SteamFullscreenDetector.IsBigPictureUp();
		bool retaken = false;
		if (WakeCheckPolicy.ShouldRetakeFocus(gaming, game, up, SteamFullscreenDetector.IsForegroundSteam()))
		{
			SteamFullscreenDetector.TryFocusSteamWindow(_logger);
			retaken = true;
		}
		TvSettings tv = _store.LoadConfig().Gaming.Tv ?? new TvSettings();
		bool held = StreamHealth().Active;
		bool? onCore = tv.Enabled && !held ? await _tv.ShowsCoreInputAsync(TimeSpan.FromSeconds(4), CancellationToken.None).ConfigureAwait(false) : null;
		bool resent = false;
		if (WakeCheckPolicy.ShouldResendInput(tv.Enabled && tv.TurnOnWithCore, coreSwitchedTv, held, onCore))
		{
			TvResult result = await _tv.SwitchCoreInputAsync(CancellationToken.None).ConfigureAwait(false);
			resent = true;
			onCore = result.Outcome == TvOutcome.Done ? true : onCore;
		}
		(bool? network, string? adapter) = ConsoleProbes.Ethernet();
		var wake = new WakeCheckResult { At = DateTimeOffset.Now, BigPictureUp = game ? null : up, FocusRetaken = retaken, TvOnCore = onCore, InputResent = resent, NetworkUp = network };
		_logger.Info($"Console guards: after waking: Big Picture {(game ? "not judged (game running)" : up ? "up" : "not visible")}{(retaken ? ", focused again" : "")}; "
			+ $"TV on Core's input {(onCore is null ? "unknown" : onCore.Value ? "yes" : "no")}{(resent ? ", input switch sent again" : "")}; Ethernet {(network is null ? "not present" : network.Value ? "up" : "down")} ({adapter ?? "-"}). "
			+ "Temperatures: not read (no driver-free source).");
		SaveConsole(state => state with { LastWake = wake });
	}

	// ---------- one-button fixes (POST /health/action?id=) ----------

	private async Task<(bool Ok, string Result)> RunConsoleActionAsync(string id)
	{
		ConsoleGuardState state = LoadConsole();
		switch (id)
		{
			case "display-save":
			{
				DisplayProfile? primary = WindowsDisplays.Read()?.FirstOrDefault(display => display.Primary);
				if (primary is null) return (false, "no-display");
				SaveConsole(saved => saved with { Display = primary });
				_logger.Info($"Console guards: saved as the normal display: {primary.Monitor ?? primary.Device} {ConsoleGuardPolicy.Describe(primary)}, Windows VRR {(primary.Vrr is null ? "not readable" : primary.Vrr.Value ? "on" : "off")}.");
				return (true, "saved");
			}
			case "display-restore":
			{
				if (state.Display is not { } saved) return (false, "not-saved");
				if (GameRunning()) return (false, "not-while-playing");
				(bool mode, bool primary, bool hdr) = WindowsDisplays.Restore(saved);
				_logger.Info($"Console guards: display restore: mode {(mode ? "ok" : "failed")}, primary {(primary ? "ok" : "failed")}, HDR {(hdr ? "ok" : "failed")}.");
				return (mode && primary && hdr, mode && primary && hdr ? "restored" : "partial");
			}
			case "sound-save":
			{
				SpeakerEndpoint? chosen = WindowsSpeakers.Read().FirstOrDefault(speaker => speaker.IsDefault);
				if (chosen is null) return (false, "no-device");
				SaveConsole(saved => saved with { Sound = new SoundDevice(chosen.Id, chosen.Name) });
				_logger.Info($"Console guards: saved as the normal sound output: {chosen.Name ?? "(unnamed)"}.");
				return (true, "saved");
			}
			case "sound-restore":
			{
				if (state.Sound is not { } saved) return (false, "not-saved");
				bool done = PolicyConfigDefaultSpeaker.SetDefault(saved.Id);
				_logger.Info(done ? $"Console guards: sound output switched back to {saved.Name ?? "the saved device"}." : "Console guards: Windows refused to switch the sound output back (IPolicyConfig unavailable or refused).");
				return (done, done ? "restored" : "refused");
			}
			case "controllers-expect":
			{
				IReadOnlyList<ControllerSeen>? seen = WindowsControllers.Read();
				if (seen is null) return (false, "unknown");
				SaveConsole(saved => saved with { ExpectedControllers = seen.Where(controller => controller.Key.Length > 0).Select(controller => controller.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList() });
				return (true, "saved");
			}
			case "prompts-off":
			{
				var values = CouchHardening.Fixes.Where(fix => fix.Id is "setup-nag" or "tips").SelectMany(fix => fix.Values);
				int changed = CouchHardening.Apply(new CouchRegistryWindows(), values, QuietUserBackup);
				_logger.Info($"Console guards: Windows setup and tips screens off ({changed} values changed; recorded for Undo in Core's Quiet Windows card).");
				return (true, "applied");
			}
			case "bitlocker-suspend":
				return SuspendBitLockerForFirmware("Quick Access", force: true) ? (true, "suspended") : (false, "not-suspended");
			case "wake-check-now":
				await RunWakeCheckAsync(false).ConfigureAwait(false);
				return (true, "checked");
			case "normal-forget":
				SaveConsole(saved => saved with { Display = null, Sound = null, ExpectedControllers = new List<string>() });
				_logger.Info("Console guards: the saved display, sound output and expected controllers were forgotten.");
				return (true, "forgotten");
		}
		return (false, "unknown-action");
	}

	private void MapConsole(WebApplication app)
	{
		app.MapPost("/health/action", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string id = context.Request.Query["id"].ToString();
			if (!ActionId.IsMatch(id) || !(ConsoleGuardPolicy.Actions.Contains(id) || id == "normal-forget" || EmulatorActionAllowed(id))) return Results.Json(new { ok = false, result = "unknown-action" }, statusCode: 400);
			(bool ok, string result) = (false, "failed");
			try { (ok, result) = EmulatorActionAllowed(id) ? await RunEmulatorActionAsync(id).ConfigureAwait(false) : await RunConsoleActionAsync(id).ConfigureAwait(false); }
			catch (Exception error) { _logger.Error($"Console guards: action \"{id}\" failed; nothing else changed.", error); }
			lock (_consoleSync) _consoleSlow = null;
			HealthReport report = await GatherHealthAsync(fresh: false).ConfigureAwait(false);
			return Results.Json(new { ok, action = id, result, health = HealthView(report) });
		}));
	}
}
