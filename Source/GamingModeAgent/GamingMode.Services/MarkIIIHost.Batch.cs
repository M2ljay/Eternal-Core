using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GamingMode.Services;

// Batch 1 in the agent (docs/eternal/BATCH-1.md): the Ludusavi save backup before update night and automatic
// Steam updates (rules in SaveBackup.cs), Decky plugin logs for Core Health and the plugins notice
// (DeckyPluginLogs.cs), the bedtime wind-down reminders (WindDown.cs) delivered as a Steam toast through the
// heartbeat's answer, and the settings the Decky plugin's Auto-Completed feature reads.
//
// Fail-soft like the rest of Mark III: every step catches its own failures and logs them. The backup has a
// time limit and its failure never stops the restart or the Steam update; the log scan reads at most
// DeckyLogPolicy.MaxRead bytes per file and once a minute; the toast is dropped when nobody picks it up.
internal sealed partial class MarkIIIHost
{
	private readonly object _batchSync = new();
	private (DateTimeOffset At, string? Path)? _ludusavi;
	private DateTimeOffset _deckyLogsAt = DateTimeOffset.MinValue;
	private DeckyLogState? _deckyLogs;
	private (string Id, string Body, bool Quiet, DateTimeOffset Expires)? _pendingToast;
	private static readonly TimeSpan LudusaviCacheFor = TimeSpan.FromMinutes(10);
	private const int MaxBackupOutput = 32 * 1024 * 1024;
	private static readonly Regex AppId = new("^[0-9]{1,10}$", RegexOptions.CultureInvariant);

	private string SaveBackupPath => Path.Combine(_paths.ConfigDirectory, "save-backup.json");
	private string DeckyLogsPath => Path.Combine(_paths.ConfigDirectory, "decky-logs.json");
	private string WindDownPath => Path.Combine(_paths.ConfigDirectory, "wind-down.json");

	// ---------- save backup (Ludusavi) ----------

	// PATH as the agent started with it plus the user's PATH as it is now (winget adds its Links folder there,
	// which a running process does not see), then winget's and Scoop's folders.
	private string? FindLudusavi()
	{
		lock (_batchSync) if (_ludusavi is { } cached && DateTimeOffset.Now - cached.At < LudusaviCacheFor) return cached.Path;
		string? found = null;
		try
		{
			found = SaveBackupPolicy.Find(name =>
			{
				if (!name.Equals("PATH", StringComparison.OrdinalIgnoreCase)) return Environment.GetEnvironmentVariable(name);
				string process = Environment.GetEnvironmentVariable("PATH") ?? "";
				string user = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "" : "";
				return process + Path.PathSeparator + user;
			}, File.Exists, (folder, pattern) => Directory.Exists(folder) ? Directory.EnumerateDirectories(folder, pattern) : Array.Empty<string>());
		}
		catch (Exception error) { _logger.Error("Save backup: Ludusavi could not be looked for.", error); }
		lock (_batchSync) _ludusavi = (DateTimeOffset.Now, found);
		return found;
	}

	// Runs `ludusavi backup --force --api` for every game, without a window, within SaveBackupPolicy.Limit.
	// Returns when it finished, failed or was stopped; never throws.
	private async Task BackupSavesAsync(string trigger)
	{
		try
		{
			DateTimeOffset now = DateTimeOffset.Now;
			GamingSettings gaming = _store.LoadConfig().Gaming;
			SaveBackupState state = SaveBackupPolicy.Load(SaveBackupPath);
			string? exe = FindLudusavi();
			if (!SaveBackupPolicy.ShouldRun(gaming.SaveBackupBeforeUpdates, exe is not null, state, now))
			{
				_logger.Info($"Save backup before {trigger}: skipped ({(!gaming.SaveBackupBeforeUpdates ? "turned off" : exe is null ? "Ludusavi is not installed" : "already ran tonight")}).");
				return;
			}
			_logger.Info($"Save backup before {trigger}: running Ludusavi ({exe}) for every game, at most {SaveBackupPolicy.Limit.TotalMinutes:0} minutes.");
			SaveBackupOutcome outcome = await RunLudusaviAsync(exe!).ConfigureAwait(false);
			SaveBackupPolicy.Save(SaveBackupPath, SaveBackupPolicy.Record(state, outcome, trigger, DateTimeOffset.Now));
			_logger.Info($"Save backup before {trigger}: {outcome.Result}{(outcome.Reason is null ? "" : " (" + outcome.Reason + ")")}, {outcome.Games} games, {outcome.Bytes} bytes. The {trigger} goes ahead either way.");
		}
		catch (Exception error) { _logger.Error($"Save backup before {trigger} failed; the {trigger} goes ahead.", error); }
	}

	private async Task<SaveBackupOutcome> RunLudusaviAsync(string exe)
	{
		var start = new ProcessStartInfo(exe)
		{
			UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
			StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
		};
		foreach (string argument in SaveBackupPolicy.Arguments) start.ArgumentList.Add(argument);
		using Process? process = Process.Start(start);
		if (process is null) return new SaveBackupOutcome("failed", "not-started", 0, 0);
		try { process.StandardInput.Close(); } catch (Exception error) when (error is IOException or InvalidOperationException) { }
		try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
		var output = new StringBuilder();
		bool truncated = false;
		Task readOutput = Task.Run(async () =>
		{
			char[] buffer = new char[16384];
			int read;
			while ((read = await process.StandardOutput.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
			{
				if (output.Length + read <= MaxBackupOutput) output.Append(buffer, 0, read);
				else truncated = true; // keep draining so Ludusavi never blocks on a full pipe
			}
		});
		Task readErrors = Task.Run(async () => { char[] buffer = new char[4096]; while (await process.StandardError.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) > 0) { } });
		using var limit = new CancellationTokenSource(SaveBackupPolicy.Limit);
		try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
		catch (OperationCanceledException)
		{
			try { process.Kill(entireProcessTree: true); } catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
			await Task.WhenAny(Task.WhenAll(readOutput, readErrors), Task.Delay(5000)).ConfigureAwait(false);
			return SaveBackupPolicy.Judge(-1, null, timedOut: true);
		}
		await Task.WhenAny(Task.WhenAll(readOutput, readErrors), Task.Delay(10000)).ConfigureAwait(false);
		// An answer too large to keep (many games): the exit code decides.
		if (truncated) return process.ExitCode == 0 ? new SaveBackupOutcome("ok", null, 0, 0) : new SaveBackupOutcome("failed", "exit-" + process.ExitCode, 0, 0);
		return SaveBackupPolicy.Judge(process.ExitCode, output.ToString(), timedOut: false);
	}

	private SaveBackupInputs SaveBackupHealth()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		return new SaveBackupInputs(gaming.SaveBackupBeforeUpdates, FindLudusavi() is not null, SaveBackupPolicy.Load(SaveBackupPath));
	}

	// ---------- Decky plugin logs ----------

	// New lines in each plugin's newest logs since the last look, at most once a minute (ScanEvery).
	private DeckyLogState DeckyLogs()
	{
		DateTimeOffset now = DateTimeOffset.Now;
		lock (_batchSync)
		{
			if (_deckyLogs is { } cached && now - _deckyLogsAt < DeckyLogPolicy.ScanEvery && now >= _deckyLogsAt) return cached;
			_deckyLogsAt = now;
		}
		DeckyLogState state = _deckyLogs ?? DeckyLogPolicy.Load(DeckyLogsPath);
		try { state = ScanDeckyLogs(state, now); }
		catch (Exception error) { _logger.Error("Decky health: plugin logs could not be read this round.", error); }
		lock (_batchSync) _deckyLogs = state;
		try { DeckyLogPolicy.Save(DeckyLogsPath, state); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _logger.Error("Decky health: log state could not be saved.", error); }
		return state;
	}

	private DeckyLogState ScanDeckyLogs(DeckyLogState state, DateTimeOffset now)
	{
		string? profile = Environment.GetEnvironmentVariable("USERPROFILE");
		var present = new List<string>();
		string root = string.IsNullOrEmpty(profile) ? "" : Path.Combine(profile, "homebrew", "logs");
		if (root.Length == 0 || !Directory.Exists(root)) return DeckyLogPolicy.Finish(state, now, present);
		var before = new HashSet<string>(DeckyLogPolicy.Failing(state, now), StringComparer.OrdinalIgnoreCase);
		foreach (DirectoryInfo folder in new DirectoryInfo(root).EnumerateDirectories().Take(DeckyLogPolicy.MaxPlugins))
		{
			IEnumerable<FileInfo> files;
			try { files = folder.EnumerateFiles("*.log").OrderByDescending(file => file.LastWriteTimeUtc).Take(DeckyLogPolicy.FilesPerPlugin).ToList(); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
			foreach (FileInfo file in files)
			{
				present.Add(file.FullName);
				DateTimeOffset written = file.LastWriteTimeUtc;
				(long From, long Count)? plan = DeckyLogPolicy.ReadPlan(state, file.FullName, file.Length, written);
				if (plan is not { } read) { state = DeckyLogPolicy.Measured(state, file.FullName, file.Length, written); continue; }
				try
				{
					using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					stream.Seek(read.From, SeekOrigin.Begin);
					byte[] buffer = new byte[read.Count];
					int total = 0, got;
					while (total < buffer.Length && (got = stream.Read(buffer, total, buffer.Length - total)) > 0) total += got;
					int whole = DeckyLogPolicy.CompleteLines(buffer.AsSpan(0, total));
					// One line longer than the whole read window: skip it rather than reading it forever.
					if (whole == 0 && total >= DeckyLogPolicy.MaxRead) whole = total;
					DeckyLogScan scan = DeckyLogPolicy.Scan(Encoding.UTF8.GetString(buffer, 0, whole));
					state = DeckyLogPolicy.Found(state, folder.Name, scan, now);
					state = DeckyLogPolicy.Measured(state, file.FullName, read.From + whole, written);
				}
				catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
			}
		}
		state = DeckyLogPolicy.Finish(state, now, present);
		foreach (string plugin in DeckyLogPolicy.Failing(state, now).Where(name => !before.Contains(name)))
			_logger.Info($"Decky health: {DeckyLogPolicy.DisplayName(plugin)} is reporting errors ({state.Plugins[plugin].Count}).");
		return state;
	}

	// The extra sentence for the "plugins did not load" notice: which plugins reported errors, if any.
	private string? FailingPluginsSentence()
	{
		IReadOnlyList<string> failing = DeckyLogPolicy.Failing(DeckyLogs(), DateTimeOffset.Now);
		return failing.Count == 0 ? null : CoreText.Format(Language(), "notice.pluginsErrors", DeckyLogPolicy.Names(failing));
	}

	// ---------- wind-down ----------

	// Hook for Home Assistant's sleep mode (input_boolean.sleep_mode): true skips the reminders. Not wired yet.
	private static bool? SleepModeActive() => null;

	private void CheckWindDown()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		if (!gaming.WindDownEnabled) return;
		DateTimeOffset now = DateTimeOffset.Now;
		WindDownState state = WindDownPolicy.Load(WindDownPath);
		var stream = StreamHealth();
		WindDownDecision decision = WindDownPolicy.Decide(new WindDownInputs
		{
			Now = now, Enabled = true, Bedtime = gaming.WindDownBedtime, MinutesBefore = WindDownPolicy.ParseMinutes(gaming.WindDownMinutes),
			GameRunning = GameRunning(), StreamOrVr = stream.Active, SleepMode = SleepModeActive(),
		}, state);
		if (decision.Slot is not { } slot || decision.Action == WindDownAction.Nothing) return;
		if (decision.Action == WindDownAction.Show)
		{
			string body = CoreText.Format(Language(), "winddown.save", WindDownPolicy.Clock(slot));
			lock (_batchSync) _pendingToast = ("winddown-" + slot.ToUnixTimeSeconds(), body, decision.Quiet, now + WindDownPolicy.Grace);
			_logger.Info($"Wind-down: {WindDownPolicy.Clock(slot)} reminder ({decision.MinutesLeft} minutes before bedtime) queued for Core's plugin, {decision.Reason}.");
		}
		else _logger.Info($"Wind-down: {WindDownPolicy.Clock(slot)} reminder skipped ({decision.Reason}).");
		WindDownPolicy.Save(WindDownPath, WindDownPolicy.Given(state, slot));
	}

	// The heartbeat's answer carries a waiting toast once; Core's plugin shows it with Steam's toaster (over games).
	private object? TakeToast()
	{
		lock (_batchSync)
		{
			if (_pendingToast is not { } toast) return null;
			_pendingToast = null;
			if (DateTimeOffset.Now > toast.Expires) { _logger.Info($"Wind-down: reminder {toast.Id} expired before Core's plugin asked for it (Decky not loaded?)."); return null; }
			_logger.Info($"Wind-down: reminder {toast.Id} handed to Core's plugin.");
			return new { id = toast.Id, title = "Core", body = toast.Body, quiet = toast.Quiet };
		}
	}

	// ---------- Auto-Completed (the Decky plugin does the work) ----------

	private object CompletedSettings()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		return new { enabled = gaming.AutoCompletedEnabled, create = gaming.AutoCompletedCreate, collection = "Completed" };
	}

	private void MapBatch(WebApplication app)
	{
		app.MapGet("/core/completed", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(CompletedSettings()) : Results.StatusCode(403)));
		app.MapPost("/core/completed/report", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string appId = context.Request.Query["appId"].ToString(), outcome = context.Request.Query["outcome"].ToString();
			if (!AppId.IsMatch(appId) || outcome is not ("added" or "created" or "missing-collection" or "failed")) return Results.Json(new { ok = false }, statusCode: 400);
			_logger.Info($"Auto-Completed: app {appId} {outcome switch { "added" => "added to \"Completed\"", "created" => "added to a new \"Completed\" collection", "missing-collection" => "is at 100% but there is no \"Completed\" collection (creating it is off)", _ => "could not be added" }}.");
			return Results.Json(new { ok = true });
		}));
	}
}
