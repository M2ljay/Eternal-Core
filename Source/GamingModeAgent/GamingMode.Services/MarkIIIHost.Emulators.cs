using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GamingMode.Services;

// 3.3.5 in the agent: emulator health and the shared hotkeys (EmulatorHealth.cs), emulator updates
// (EmulatorUpdates.cs), exiting games through Steam and closing store launchers (GameExit.cs), per-device game
// settings while streaming and the back-paddle check (StreamProfiles.cs). Windows calls: Emulators.Windows.cs.
// docs/eternal/CONSOLE-GUARDS.md, EMULATOR-UPDATES.md, CORE-STREAM-MODE.md.
//
// Cost: the game watch reads the kernel's process list every 2 s (no process is opened); emulator rows are read
// at most once a minute on a pool thread with a 2.5 s budget for Quick Access; GitHub is asked at most every
// 6 hours per emulator.
internal sealed partial class MarkIIIHost
{
	private string _emuStatePath = "", _emuUpdatesPath = "", _gameExitPath = "", _emuBackupRoot = "", _profilesPath = "", _profileStatePath = "", _profileStore = "";
	private readonly object _emuSync = new();
	private GameSession? _session;
	private readonly HashSet<(int Pid, DateTimeOffset Started)> _seenRoots = new();
	private (uint AppId, string? Title, bool Running, DateTimeOffset At)? _pluginLifetime;
	private string? _untrackedTitle;
	private List<TrackedProcess>? _untrackedTree;
	private readonly List<(LauncherDef Launcher, DateTimeOffset EndedAt, long WrittenAtEnd)> _launcherWatch = new();
	private (DateTimeOffset At, IReadOnlyList<HealthRow> Rows)? _emuRows;
	private Task<IReadOnlyList<HealthRow>>? _emuReading;
	private int _emuJob, _exitJob;
	private DateTimeOffset _lastHotkeyRound, _lastUpdateRound;

	private static string ActionEmulator(string id, string prefix) => id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : "";

	internal static bool EmulatorActionAllowed(string id) =>
		id is "game-exit-now" or "paddles-setup"
		|| (EmulatorCatalog.Find(ActionEmulator(id, "emu-fix-")) is not null)
		|| (EmulatorCatalog.Find(ActionEmulator(id, "emu-hotkeys-")) is not null)
		|| (EmulatorUpdateSources.Find(ActionEmulator(id, "emu-update-")) is not null);

	// ---------- lifetime ----------

	private void StartEmulators(CancellationToken stopping)
	{
		_emuStatePath = Path.Combine(_paths.ConfigDirectory, "emulators.json");
		_emuUpdatesPath = Path.Combine(_paths.ConfigDirectory, "emulator-updates.json");
		_gameExitPath = Path.Combine(_paths.ConfigDirectory, "game-exit.json");
		_emuBackupRoot = Path.Combine(_paths.ConfigDirectory, "emulator-backups");
		_profilesPath = Path.Combine(_paths.ConfigDirectory, "stream-profiles.json");
		_profileStatePath = Path.Combine(_paths.ConfigDirectory, "stream-profiles-state.json");
		_profileStore = Path.Combine(_paths.ConfigDirectory, "stream-profiles");
		_ = Task.Run(async () =>
		{
			while (!stopping.IsCancellationRequested)
			{
				try { await Task.Delay(TimeSpan.FromSeconds(2), stopping).ConfigureAwait(false); }
				catch (OperationCanceledException) { return; }
				try { WatchGames(); }
				catch (Exception error) { _logger.Error("Game exit: watch failed this round.", error); }
				DateTimeOffset now = DateTimeOffset.Now;
				if (now - _lastHotkeyRound > TimeSpan.FromMinutes(1))
				{
					_lastHotkeyRound = now;
					try { HotkeyRound(); } catch (Exception error) { _logger.Error("Emulators: hotkey check failed this round.", error); }
					try { ProfileRound(); } catch (Exception error) { _logger.Error("Stream profiles: check failed this round.", error); }
				}
				if (now - _lastUpdateRound > TimeSpan.FromMinutes(30))
				{
					_lastUpdateRound = now;
					try { await UpdateRoundAsync(stopping).ConfigureAwait(false); }
					catch (Exception error) when (error is not OperationCanceledException) { _logger.Error("Emulator updates: check failed this round.", error); }
				}
			}
		});
	}

	// ---------- exiting games ----------

	private void WatchGames()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		var snapshot = ProcessSnapshot.Read();
		if (snapshot.Count == 0) return;
		var processes = snapshot.Select(item => item.Process).ToList();
		int steamPid = processes.FirstOrDefault(process => string.Equals(process.Name, "steam.exe", StringComparison.OrdinalIgnoreCase))?.Pid ?? 0;
		DateTimeOffset now = DateTimeOffset.Now;
		long Written(LauncherDef launcher) => snapshot.Where(item => LauncherCatalog.ByProcess(item.Process.Name)?.Id == launcher.Id).Sum(item => item.WrittenBytes);
		var launchersNow = processes.Select(process => LauncherCatalog.ByProcess(process.Name)).Where(launcher => launcher is not null).Select(launcher => launcher!.Id).Distinct().ToList();
		GameSession? ended = null;
		lock (_emuSync)
		{
			// A wrapper that left the game on screen: forget it once its processes are gone.
			if (_untrackedTree is not null && !_untrackedTree.Any(member => processes.Any(process => process.Pid == member.Pid && process.Started == member.Started)))
			{
				_untrackedTree = null;
				_untrackedTitle = null;
			}
			if (_session is null && steamPid > 0)
			{
				ProcessSeen? root = processes.FirstOrDefault(process => process.ParentPid == steamPid && !GameExitPolicy.IsSteamOwn(process.Name) && LauncherCatalog.ByProcess(process.Name) is null
					&& now - process.Started < TimeSpan.FromSeconds(15) && !_seenRoots.Contains((process.Pid, process.Started)));
				if (root is not null)
				{
					if (_seenRoots.Count > 500) _seenRoots.Clear();
					_seenRoots.Add((root.Pid, root.Started));
					string? title = _pluginLifetime is { Running: true } lifetime && now - lifetime.At < TimeSpan.FromSeconds(30) ? lifetime.Title : null;
					_session = new GameSession { AppId = _pluginLifetime?.AppId ?? RunningAppId(), Title = title ?? GameExitPolicy.Bare(root.Name), StartedAt = root.Started, LaunchersBefore = launchersNow };
					_logger.Info($"Game exit: watching the launch of {_session.Title}.");
				}
			}
			if (_session is not null)
			{
				_session = GameExitPolicy.Observe(_session, processes, steamPid);
				var started = launchersNow.Where(id => !_session.LaunchersBefore.Contains(id) && !_session.LaunchersStarted.Contains(id)).ToList();
				if (started.Count > 0) _session = _session with { LaunchersStarted = _session.LaunchersStarted.Concat(started).ToList() };
				bool pluginSaysEnded = _pluginLifetime is { Running: false } report && report.At > _session.StartedAt;
				bool rootsGone = !_session.Tree.Any(member => processes.Any(process => process.Pid == member.Pid && process.Started == member.Started && process.ParentPid == steamPid));
				bool pluginAlive = _decky.Verdict == DeckyVerdict.Healthy;
				bool steamEnded = pluginSaysEnded || (!pluginAlive && rootsGone && RunningAppId() == 0 && now - _session.StartedAt > TimeSpan.FromSeconds(5)) || steamPid == 0;
				if (_session.ExitChosen || steamEnded)
				{
					ended = _session with { SteamReportedEnd = steamEnded };
					_session = null;
					_pluginLifetime = null;
				}
			}
			if (ended is not null)
				foreach (string id in ended.LaunchersStarted.Where(id => !ended.LaunchersBefore.Contains(id)))
					if (LauncherCatalog.Find(id) is { } launcher) _launcherWatch.Add((launcher, now, Written(launcher)));
		}
		if (ended is not null) EndSession(ended, processes, gaming);
		// Store launchers started for a game that has ended.
		List<(LauncherDef Launcher, DateTimeOffset EndedAt, long WrittenAtEnd)> watch;
		lock (_emuSync) watch = _launcherWatch.ToList();
		foreach (var item in watch)
		{
			bool running = launchersNow.Contains(item.Launcher.Id);
			bool otherGame;
			lock (_emuSync) otherGame = _session is not null;
			var input = new LauncherCloseInput(LauncherCatalog.Enabled(gaming.AutoCloseLaunchers, item.Launcher.Id), false, true, otherGame,
				LauncherUpdatePolicy.Pending(Array.Empty<string>(), processes.Select(process => GameExitPolicy.Bare(process.Name))).Contains(item.Launcher.Name),
				Math.Max(0, Written(item.Launcher) - item.WrittenAtEnd), now - item.EndedAt);
			LauncherCloseAction action = running ? LauncherClosePolicy.Decide(input) : LauncherCloseAction.Leave;
			if (action == LauncherCloseAction.Wait) continue;
			lock (_emuSync) _launcherWatch.Remove(item);
			if (action != LauncherCloseAction.Close) continue;
			int sent = processes.Where(process => LauncherCatalog.ByProcess(process.Name)?.Id == item.Launcher.Id).Sum(process => ProcessSnapshot.PostClose(process.Pid));
			_logger.Info($"Store launchers: {item.Launcher.Name} was started for the game that ended and stayed quiet for {LauncherClosePolicy.Grace.TotalSeconds:0} s; asked it to close ({sent} windows). Never forced.");
			SaveGameExit(state => state with { LaunchersClosed = state.LaunchersClosed.Where(at => at.Date == now.Date).Append(now).ToList(), LastLauncherClosed = item.Launcher.Name });
		}
	}

	private void EndSession(GameSession session, IReadOnlyList<ProcessSeen> processes, GamingSettings gaming)
	{
		LeftoverDecision decision = GameExitPolicy.Decide(session, processes);
		if (decision.Action == LeftoverAction.Leave)
		{
			if (decision.Reason == "steam-lost-track")
			{
				lock (_emuSync) { _untrackedTitle = session.Title; _untrackedTree = decision.Processes.ToList(); }
				_logger.Info($"Game exit: Steam stopped tracking {session.Title} while it is still on screen ({string.Join(", ", decision.Processes.Select(member => member.Name))}); left running. Core's Exit is offered in Quick Access.");
			}
			return;
		}
		if (!gaming.GameExitCleanup || _antiCheat.Engaged)
		{
			_logger.Info($"Game exit: {decision.Processes.Count} process(es) of {session.Title} still run; left ({(_antiCheat.Engaged ? "anti-cheat safe mode" : "cleanup off")}).");
			return;
		}
		CloseTree(session.Title, decision.Processes, decision.Reason, processes);
	}

	private void CloseTree(string? title, IReadOnlyList<TrackedProcess> tree, string reason, IReadOnlyList<ProcessSeen> processes)
	{
		if (Interlocked.Exchange(ref _exitJob, 1) == 1) return;
		_ = Task.Run(async () =>
		{
			try
			{
				IReadOnlyList<TrackedProcess> order = GameExitPolicy.CloseOrder(tree, processes);
				foreach (TrackedProcess member in order) ProcessSnapshot.PostClose(member.Pid);
				DateTimeOffset until = DateTimeOffset.Now + GameExitPolicy.GracefulTimeout;
				while (DateTimeOffset.Now < until && order.Any(member => ProcessSnapshot.Alive(member.Pid, member.Started))) await Task.Delay(500).ConfigureAwait(false);
				int terminated = 0;
				foreach (TrackedProcess member in order.Where(member => ProcessSnapshot.Alive(member.Pid, member.Started)))
					if (ProcessSnapshot.Terminate(member.Pid, member.Started)) terminated++;
				await Task.Delay(1000).ConfigureAwait(false);
				var left = order.Where(member => ProcessSnapshot.Alive(member.Pid, member.Started)).ToList();
				_logger.Info($"Game exit: {title}: {order.Count - left.Count} leftover process(es) closed ({terminated} had to be ended after {GameExitPolicy.GracefulTimeout.TotalSeconds:0} s){(left.Count > 0 ? "; still running: " + string.Join(", ", left.Select(member => member.Name)) : "")}. Reason: {reason}.");
				var record = new GameExitRecord(title, DateTimeOffset.Now, order.Count - left.Count, left.Count, reason, order.Select(member => member.Name).Distinct().ToList());
				SaveGameExit(state => state with { Last = record });
				lock (_emuSync) { _emuRows = null; if (left.Count == 0) { _untrackedTree = null; _untrackedTitle = null; } }
				if (left.Count > 0) await CheckHealthAlertsAsync().ConfigureAwait(false);
			}
			catch (Exception error) { _logger.Error("Game exit: closing the leftovers failed; nothing else changed.", error); }
			finally { Interlocked.Exchange(ref _exitJob, 0); }
		});
	}

	private void SaveGameExit(Func<GameExitState, GameExitState> change)
	{
		lock (_emuSync)
		{
			try { GameExitStore.Save(_gameExitPath, change(GameExitStore.Load(_gameExitPath))); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.Error("Game exit: state could not be saved.", error); }
		}
	}

	// ---------- emulator rows ----------

	private IReadOnlyList<HealthRow> ReadEmulatorRows()
	{
		var rows = new List<HealthRow>();
		DateTimeOffset now = DateTimeOffset.Now;
		GameExitState exit = GameExitStore.Load(_gameExitPath);
		string? untracked;
		lock (_emuSync) untracked = _untrackedTitle;
		rows.Add(GameExitPolicy.Row(exit.Last, untracked, now));
		rows.Add(BackPaddlePolicy.Row(EmulatorProbes.PaddleLayouts(SteamRoot())));
		IReadOnlyDictionary<string, string> located = EmulatorProbes.Locate();
		if (located.Count == 0) return rows;
		GamingSettings gaming = _store.LoadConfig().Gaming;
		EmulatorUpdateState updates = EmulatorUpdatePolicy.Load(_emuUpdatesPath);
		int soak = Math.Clamp(gaming.EmulatorUpdateSoakDays, 1, 60);
		rows.Add(EmulatorUpdatePolicy.Row(EmulatorCatalog.All.Where(def => located.ContainsKey(def.Id)).Select(def =>
		{
			updates.Emulators.TryGetValue(def.Id, out EmulatorUpdateEntry? entry);
			return (def.Id, def.Name, EmulatorUpdatePolicy.Phase(def.Id, true, entry, soak, now), entry);
		}).ToList(), soak, now));
		IReadOnlyDictionary<string, string> emudeck = EmulatorProbes.EmuDeckSettings();
		foreach (EmulatorDef def in EmulatorCatalog.All.Where(def => located.ContainsKey(def.Id)))
		{
			try { rows.AddRange(EmulatorHealthPolicy.Rows(EmulatorProbes.Observe(def, located[def.Id], emudeck))); }
			catch (Exception error) { _logger.Error($"Emulators: {def.Name} could not be read this round.", error); }
		}
		return rows;
	}

	private async Task<IReadOnlyList<HealthRow>?> EmulatorRowsAsync(bool fresh)
	{
		Task<IReadOnlyList<HealthRow>> reading;
		lock (_emuSync)
		{
			if (!fresh && _emuRows is { } cached && DateTimeOffset.Now - cached.At < TimeSpan.FromMinutes(1)) return cached.Rows;
			if (_emuReading is null || _emuReading.IsCompleted)
			{
				_emuReading = Task.Run(ReadEmulatorRows);
				_ = _emuReading.ContinueWith(task => { if (task.IsCompletedSuccessfully) lock (_emuSync) _emuRows = (DateTimeOffset.Now, task.Result); }, TaskScheduler.Default);
			}
			reading = _emuReading;
			if (!fresh && _emuRows is { } older) return older.Rows;
		}
		if (await Task.WhenAny(reading, Task.Delay(2500)).ConfigureAwait(false) == reading && reading.IsCompletedSuccessfully) return reading.Result;
		lock (_emuSync) return _emuRows?.Rows;
	}

	// ---------- the shared hotkeys: first run, a new emulator, after an update ----------

	private void HotkeyRound()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		if (!gaming.EmulatorHotkeysEnabled || GameRunning()) return;
		IReadOnlyDictionary<string, string> located = EmulatorProbes.Locate();
		if (located.Count == 0) return;
		EmulatorState state = EmulatorStateStore.Load(_emuStatePath);
		bool firstRun = state.FirstRunAt is null;
		IReadOnlyDictionary<string, string> emudeck = EmulatorProbes.EmuDeckSettings();
		var done = new List<string>();
		foreach ((string id, string exe) in located)
		{
			EmulatorDef def = EmulatorCatalog.Find(id)!;
			if (!EmulatorStateStore.ShouldAutoApply(state, id, firstRun)) continue;
			EmulatorObservation observation = EmulatorProbes.Observe(def, exe, emudeck);
			HotkeyVerdict verdict = EmulatorHealthPolicy.Hotkeys(observation);
			// An emulator that has not written its settings yet keeps its place in the queue until it has.
			if (verdict.State == "no-config" || observation.Running) continue;
			if (verdict.State == "drifted")
			{
				(string result, _) = ApplyHotkeys(def, exe, observation);
				_logger.Info($"Emulators: shared hotkeys applied to {def.Name} automatically ({(firstRun ? "Core's first run" : state.Known.Contains(id) ? "after an update" : "a new emulator")}): {result}.");
			}
			done.Add(id);
		}
		var known = state.Known.Union(located.Keys).Distinct().ToList();
		EmulatorStateStore.Save(_emuStatePath, state with { Known = known, UpdatedSinceHotkeys = state.UpdatedSinceHotkeys.Except(done).ToList(), FirstRunAt = state.FirstRunAt ?? DateTimeOffset.Now });
		if (done.Count > 0) lock (_emuSync) _emuRows = null;
	}

	private (string Result, List<string> Written) ApplyHotkeys(EmulatorDef def, string exe, EmulatorObservation observation) =>
		EmulatorProbes.Write(def, exe, EmulatorHealthPolicy.ApplyHotkeys(observation), observation.Files, _emuBackupRoot, _logger);

	// ---------- one-tap actions (POST /health/action?id=) ----------

	private async Task<(bool Ok, string Result)> RunEmulatorActionAsync(string id)
	{
		if (id == "game-exit-now")
		{
			List<TrackedProcess>? tree;
			string? title;
			lock (_emuSync) { tree = _untrackedTree?.ToList() ?? _session?.Tree.ToList(); title = _untrackedTitle ?? _session?.Title; }
			if (tree is null || tree.Count == 0) return (false, "nothing-running");
			if (_antiCheat.Engaged) return (false, "anti-cheat");
			CloseTree(title, tree, "core-exit", ProcessSnapshot.Read().Select(item => item.Process).ToList());
			return (true, "closing");
		}
		if (id == "paddles-setup")
		{
			// Steam's own layout editor for Big Picture, driven with the controller.
			return _processTools.OpenUri("steam://controllerconfig/413090") ? (true, "opened") : (false, "failed");
		}
		IReadOnlyDictionary<string, string> located = EmulatorProbes.Locate();
		string emulator = id.StartsWith("emu-fix-", StringComparison.Ordinal) ? id[8..] : id.StartsWith("emu-hotkeys-", StringComparison.Ordinal) ? id[12..] : id[11..];
		if (!located.TryGetValue(emulator, out string? exe) || EmulatorCatalog.Find(emulator) is not { } def) return (false, "not-installed");
		if (id.StartsWith("emu-update-", StringComparison.Ordinal)) return await RunEmulatorUpdateAsync(emulator, manual: true, CancellationToken.None).ConfigureAwait(false);
		EmulatorObservation observation = EmulatorProbes.Observe(def, exe, EmulatorProbes.EmuDeckSettings());
		if (observation.Running) return (false, "not-while-running");
		(string result, List<string> written) = id.StartsWith("emu-fix-", StringComparison.Ordinal)
			? EmulatorProbes.Write(def, exe, EmulatorHealthPolicy.ApplySettings(observation), observation.Files, _emuBackupRoot, _logger)
			: ApplyHotkeys(def, exe, observation);
		lock (_emuSync) _emuRows = null;
		return (result == "ok", result == "ok" ? (written.Count == 0 ? "no-change" : "fixed") : result);
	}

	// ---------- emulator updates ----------

	private async Task UpdateRoundAsync(CancellationToken stopping)
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		if (!gaming.EmulatorUpdatesEnabled) return;
		IReadOnlyDictionary<string, string> located = EmulatorProbes.Locate();
		EmulatorUpdateState state = EmulatorUpdatePolicy.Load(_emuUpdatesPath);
		DateTimeOffset now = DateTimeOffset.Now;
		bool changed = false;
		foreach (UpdateSource source in EmulatorUpdateSources.All.Where(source => located.ContainsKey(source.Emulator)))
		{
			state.Emulators.TryGetValue(source.Emulator, out EmulatorUpdateEntry? entry);
			entry ??= new EmulatorUpdateEntry();
			if (entry.LastCheck is { } last && now - last < EmulatorUpdatePolicy.CheckEvery) continue;
			ReleaseInfo? release = null;
			try { release = await EmulatorUpdater.LatestAsync(source, stopping).ConfigureAwait(false); }
			catch (Exception error) when (error is System.Net.Http.HttpRequestException or TaskCanceledException) { }
			if (release is null) { state.Emulators[source.Emulator] = entry with { LastCheck = now }; changed = true; continue; }
			ReleaseAsset? asset = EmulatorUpdateSources.PickAsset(source, release);
			ReleaseAsset? checksum = asset is null ? null : EmulatorUpdateSources.ChecksumAsset(release, asset);
			EmulatorUpdateEntry next = EmulatorUpdatePolicy.Observe(entry, source, release, asset, checksum, now);
			if (next.Pending is { } pending && entry.Pending?.Version != pending.Version)
				_logger.Info($"Emulator updates: {source.Emulator} {pending.Version} is out; it soaks {gaming.EmulatorUpdateSoakDays} days ({pending.Verification}).");
			state.Emulators[source.Emulator] = next;
			changed = true;
		}
		if (changed) EmulatorUpdatePolicy.Save(_emuUpdatesPath, state);
		foreach (UpdateSource source in EmulatorUpdateSources.All.Where(source => located.ContainsKey(source.Emulator)))
		{
			state.Emulators.TryGetValue(source.Emulator, out EmulatorUpdateEntry? entry);
			EmulatorUpdatePhase phase = EmulatorUpdatePolicy.Phase(source.Emulator, true, entry, gaming.EmulatorUpdateSoakDays, now);
			if (EmulatorUpdatePolicy.MayRunAutomatically(phase, GameRunning(), StreamHealth().Active, EmulatorProbes.Running(EmulatorCatalog.Find(source.Emulator)!), now))
				await RunEmulatorUpdateAsync(source.Emulator, manual: false, stopping).ConfigureAwait(false);
		}
	}

	private async Task<(bool Ok, string Result)> RunEmulatorUpdateAsync(string emulator, bool manual, CancellationToken cancellation)
	{
		if (Interlocked.Exchange(ref _emuJob, 1) == 1) return (false, "busy");
		string work = Path.Combine(Path.GetTempPath(), "Core-emulator-update", emulator + "-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture));
		try
		{
			UpdateSource source = EmulatorUpdateSources.Find(emulator)!;
			EmulatorDef def = EmulatorCatalog.Find(emulator)!;
			if (!EmulatorProbes.Locate().TryGetValue(emulator, out string? exe)) return (false, "not-installed");
			EmulatorUpdateState state = EmulatorUpdatePolicy.Load(_emuUpdatesPath);
			state.Emulators.TryGetValue(emulator, out EmulatorUpdateEntry? entry);
			GamingSettings gaming = _store.LoadConfig().Gaming;
			EmulatorUpdatePhase phase = EmulatorUpdatePolicy.Phase(emulator, true, entry, gaming.EmulatorUpdateSoakDays, DateTimeOffset.Now);
			if (manual && EmulatorUpdatePolicy.RefuseManual(phase, EmulatorProbes.Running(def)) is { } refusal) return (false, refusal);
			if (entry?.Pending is not { } pending) return (false, "current");
			if (EmulatorProbes.Running(def)) return (false, "not-while-running");
			string folder = Path.GetDirectoryName(exe)!;
			_logger.Info($"Emulator updates: {def.Name} {entry.Installed} -> {pending.Version} ({(manual ? "Update now" : "update night")}).");
			string archive = Path.Combine(work, pending.Asset);
			string sha = await EmulatorUpdater.DownloadAsync(pending.Url, archive, pending.Size, cancellation).ConfigureAwait(false);
			if (pending.GitHubSha256 is { } github && github != sha) return Fail(emulator, pending.Version, "hash-mismatch", false);
			if (pending.Verification == UpdateVerification.Project)
			{
				string? sums = pending.ChecksumUrl is null ? null : await EmulatorUpdater.TextAsync(pending.ChecksumUrl, cancellation).ConfigureAwait(false);
				if (sums is null || EmulatorUpdateSources.ChecksumFor(sums, pending.Asset, pending.ChecksumPerAsset) != sha) return Fail(emulator, pending.Version, "checksum-mismatch", false);
			}
			string extracted = Path.Combine(work, "x");
			if (!EmulatorUpdater.Extract(archive, extracted, _logger)) return Fail(emulator, pending.Version, "unpack-failed", false);
			var files = Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(extracted, path).Replace('\\', '/')).ToList();
			var plan = EmulatorUpdatePolicy.PlanOverlay(source, files);
			if (plan.Count == 0) return Fail(emulator, pending.Version, "empty-archive", false);
			// Back up the program files first; no backup, no update.
			string backup = Path.Combine(_emuBackupRoot, emulator, "update-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
			List<string> program = EmulatorUpdater.ProgramFiles(source, folder);
			foreach (string relative in program) EmulatorUpdater.Copy(Path.Combine(folder, relative), Path.Combine(backup, relative));
			var created = plan.Where(item => !File.Exists(Path.Combine(folder, item.To))).Select(item => item.To).ToList();
			string? failure = null;
			try { foreach ((string from, string to) in plan) EmulatorUpdater.Copy(Path.Combine(extracted, from), Path.Combine(folder, to)); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { failure = "copy-failed"; _logger.Error($"Emulator updates: {def.Name}: copying the new files failed.", error); }
			failure ??= EmulatorUpdater.SmokeTest(exe);
			if (failure is null)
			{
				SaveUpdates(emulator, current => EmulatorUpdatePolicy.Succeeded(current, pending.Version, DateTimeOffset.Now));
				EmulatorState emuState = EmulatorStateStore.Load(_emuStatePath);
				EmulatorStateStore.Save(_emuStatePath, emuState with { UpdatedSinceHotkeys = emuState.UpdatedSinceHotkeys.Append(emulator).Distinct().ToList() });
				_logger.Info($"Emulator updates: {def.Name} {pending.Version} passed its smoke test. Backup kept at {backup}.");
				_lastHotkeyRound = DateTimeOffset.MinValue;
				lock (_emuSync) _emuRows = null;
				return (true, "updated");
			}
			// Roll back: the program files come back from the backup, files the update added go away.
			foreach (string relative in program) EmulatorUpdater.Copy(Path.Combine(backup, relative), Path.Combine(folder, relative));
			foreach (string relative in created) try { File.Delete(Path.Combine(folder, relative)); } catch (IOException) { }
			_logger.Info($"Emulator updates: {def.Name} {pending.Version} failed its check ({failure}); rolled back to the backup.");
			Notice("notice.emulatorRolledBack", def.Name);
			return Fail(emulator, pending.Version, failure, true);
		}
		catch (Exception error) when (error is not OperationCanceledException)
		{
			_logger.Error($"Emulator updates: {emulator} update failed before anything was replaced, or while restoring; see the lines above.", error);
			return (false, "failed");
		}
		finally
		{
			try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
			Interlocked.Exchange(ref _emuJob, 0);
		}
	}

	private (bool, string) Fail(string emulator, string version, string reason, bool rolledBack)
	{
		SaveUpdates(emulator, current => EmulatorUpdatePolicy.Failed(current, version, reason, rolledBack));
		lock (_emuSync) _emuRows = null;
		return (false, (rolledBack ? "rolled-back:" : "failed:") + reason);
	}

	private void SaveUpdates(string emulator, Func<EmulatorUpdateEntry, EmulatorUpdateEntry> change)
	{
		if (emulator.Length == 0) return;
		lock (_emuSync)
		{
			EmulatorUpdateState state = EmulatorUpdatePolicy.Load(_emuUpdatesPath);
			state.Emulators.TryGetValue(emulator, out EmulatorUpdateEntry? entry);
			state.Emulators[emulator] = change(entry ?? new EmulatorUpdateEntry());
			try { EmulatorUpdatePolicy.Save(_emuUpdatesPath, state); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.Error("Emulator updates: state could not be saved.", error); }
		}
	}

	// ---------- per-device game settings while streaming ----------

	private void ProfileRound()
	{
		StreamProfileSettings settings = StreamProfilePolicy.Load<StreamProfileSettings>(_profilesPath);
		if (!settings.Enabled || settings.Games.Count == 0) return;
		var stream = StreamHealth();
		string to = StreamProfilePolicy.ClientKey(stream.Active && !stream.Forced, stream.Device, stream.Source);
		StreamProfileState state = StreamProfilePolicy.Load<StreamProfileState>(_profileStatePath);
		if (state.Current is null) { StreamProfilePolicy.Save(_profileStatePath, state with { Current = to }); return; }
		if (state.Current == to && state.PendingTo is null) return;
		string[] roots = { EmulatorProbes.UserProfile };
		string Snapshot(string game, string client, string file) => Path.Combine(_profileStore, StreamProfilePolicy.GameFolder(game), client, StreamProfilePolicy.SnapshotName(file));
		var (actions, waiting) = StreamProfilePolicy.Plan(settings, state.Current, to, process => EmulatorProbes.AnyProcess(Path.GetFileNameWithoutExtension(process)),
			(game, client, file) => File.Exists(Snapshot(game, client, file)), roots);
		if (waiting is not null)
		{
			if (state.PendingTo != to) _logger.Info($"Stream profiles: {waiting} is running; its settings switch to {to} when it exits.");
			StreamProfilePolicy.Save(_profileStatePath, state with { PendingTo = to });
			return;
		}
		string backups = Path.Combine(_profileStore, "backups", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
		int done = 0;
		foreach (StreamProfileAction action in actions)
		{
			try
			{
				var info = new FileInfo(action.File);
				string snapshot = Snapshot(action.Game, action.Client, action.File);
				if (action.Kind == "snapshot")
				{
					if (!info.Exists || info.Length > StreamProfilePolicy.MaxFileBytes) continue;
					EmulatorUpdater.Copy(action.File, snapshot);
				}
				else
				{
					if (new FileInfo(snapshot) is not { Exists: true, Length: <= StreamProfilePolicy.MaxFileBytes }) continue;
					if (info.Exists) EmulatorUpdater.Copy(action.File, Path.Combine(backups, StreamProfilePolicy.GameFolder(action.Game), StreamProfilePolicy.SnapshotName(action.File)));
					EmulatorUpdater.Copy(snapshot, action.File);
				}
				done++;
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.Error($"Stream profiles: {action.Kind} of a {action.Game} file failed; that file is left as it is.", error); }
		}
		_logger.Info($"Stream profiles: game settings switched from {state.Current} to {to} ({done} file operations; overwritten files backed up under {backups}).");
		StreamProfilePolicy.Save(_profileStatePath, new StreamProfileState { Current = to, LastSwitchAt = DateTimeOffset.Now, LastResult = done.ToString(System.Globalization.CultureInfo.InvariantCulture) });
	}

	// ---------- HTTP ----------

	private object EmulatorStateView()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		StreamProfileSettings profiles = StreamProfilePolicy.Load<StreamProfileSettings>(_profilesPath);
		StreamProfileState profileState = StreamProfilePolicy.Load<StreamProfileState>(_profileStatePath);
		string? untracked;
		lock (_emuSync) untracked = _untrackedTitle;
		var clients = Directory.Exists(_profileStore)
			? Directory.EnumerateDirectories(_profileStore).Where(path => !Path.GetFileName(path).Equals("backups", StringComparison.OrdinalIgnoreCase))
				.SelectMany(Directory.EnumerateDirectories).Select(Path.GetFileName).Distinct().Take(16).ToArray()
			: Array.Empty<string?>();
		return new
		{
			hotkeys = EmulatorCatalog.HotkeyTable.Select(row => new { function = row.Function, buttons = row.Buttons }).ToArray(),
			launchers = LauncherCatalog.All.Select(launcher => new { id = launcher.Id, name = launcher.Name, on = LauncherCatalog.Enabled(gaming.AutoCloseLaunchers, launcher.Id) }).ToArray(),
			untracked,
			profiles = new { games = profiles.Games.Select(game => game.Name).Take(32).ToArray(), clients, current = profileState.Current, pending = profileState.PendingTo },
		};
	}

	private void MapEmulators(WebApplication app)
	{
		app.MapGet("/emulators/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(EmulatorStateView()) : Results.StatusCode(403)));
		// Core's Big Picture plugin: Steam's app lifetime events and the user's Exit Game (SteamClient.Apps.TerminateApp).
		app.MapPost("/game/lifetime", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			uint.TryParse(context.Request.Query["appId"].ToString(), out uint appId);
			bool running = context.Request.Query["running"].ToString() == "true";
			string title = new string(context.Request.Query["title"].ToString().Where(character => !char.IsControl(character)).Take(64).ToArray());
			lock (_emuSync) _pluginLifetime = (appId, title.Length == 0 ? null : title, running, DateTimeOffset.Now);
			return Results.Json(new { ok = true });
		}));
		app.MapPost("/game/exit-chosen", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			bool known;
			lock (_emuSync)
			{
				known = _session is not null || _untrackedTree is not null;
				if (_session is not null) _session = _session with { ExitChosen = true };
			}
			_logger.Info("Game exit: Exit Game chosen in Steam; Core closes what the launch leaves running.");
			return Results.Json(new { ok = known });
		}));
		app.MapPost("/launchers/auto-close", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string id = context.Request.Query["id"].ToString();
			if (LauncherCatalog.Find(id) is null) return Results.Json(new { ok = false }, statusCode: 400);
			bool on = context.Request.Query["on"].ToString() == "true";
			ModeConfig config = _store.LoadConfig();
			var switches = new Dictionary<string, bool>(config.Gaming.AutoCloseLaunchers ?? new Dictionary<string, bool>()) { [id] = on };
			config.Gaming.AutoCloseLaunchers = switches;
			_store.SaveConfig(config);
			return Results.Json(EmulatorStateView());
		}));
		// The desktop app: the once-per-version confirmation for a download only GitHub's own digest vouches for.
		app.MapPost("/emulators/update/confirm", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string id = context.Request.Query["id"].ToString(), version = context.Request.Query["version"].ToString();
			if (EmulatorUpdateSources.Find(id) is null || version.Length is 0 or > 80) return Results.Json(new { ok = false }, statusCode: 400);
			SaveUpdates(id, entry => entry.Pending?.Version == version ? EmulatorUpdatePolicy.Confirm(entry, version) : entry);
			_logger.Info($"Emulator updates: {id} {version} confirmed on the desktop.");
			lock (_emuSync) _emuRows = null;
			return Results.Json(new { ok = true });
		}));
		app.MapGet("/emulators/updates", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(EmulatorUpdatePolicy.Load(_emuUpdatesPath)) : Results.StatusCode(403)));
	}
}
