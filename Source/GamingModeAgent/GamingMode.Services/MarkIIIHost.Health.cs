using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Services;
using Playhub.Shared;

namespace GamingMode.Services;

// Core Health in the agent: gathers the observations CoreHealthPolicy judges, serves them to the
// Quick Access tab (GET /health) and shows a quiet Core notice for serious problems.
//
// Cost: the server's status file is read at most once a minute (4 s time box: an offline server
// must not stall Quick Access), the TV is probed with a plain TCP connect (no wake packet, no
// command) at most once a minute, and Core's version comes from the agent's existing cached update
// check. Notices are Core's own over Big Picture (CoreNoticeOverlay), never Windows toasts, so
// Quiet Windows' banner switch does not hide them; like every Core notice, never during a game.
internal sealed partial class MarkIIIHost
{
	private readonly string _healthAlertsPath;
	private readonly object _healthSync = new();
	private (DateTimeOffset At, string? Name, ServerStatusRead Read, ServerStatus? Status)? _serverCache;
	private Task<(ServerStatusRead, ServerStatus?)>? _serverReading;
	private (DateTimeOffset At, string? Address, bool? Reachable)? _tvCache;
	private static readonly TimeSpan ServerCacheFor = TimeSpan.FromMinutes(1), ServerReadBudget = TimeSpan.FromSeconds(4);
	private static readonly Regex HealthId = new("^[a-z-]{1,40}$", RegexOptions.CultureInvariant);

	// ---------- observations ----------

	private async Task<(ServerStatusRead Read, ServerStatus? Status)> ReadServerAsync(string? name, bool fresh)
	{
		Task<(ServerStatusRead, ServerStatus?)> reading;
		lock (_healthSync)
		{
			if (!fresh && _serverCache is { } cached && cached.Name == name && DateTimeOffset.Now - cached.At < ServerCacheFor) return (cached.Read, cached.Status);
			if (_serverReading is null || _serverReading.IsCompleted)
			{
				_serverReading = Task.Run(() => ReadServerFile(name));
				_ = _serverReading.ContinueWith(task =>
				{
					if (task.IsCompletedSuccessfully) lock (_healthSync) _serverCache = (DateTimeOffset.Now, name, task.Result.Item1, task.Result.Item2);
				}, TaskScheduler.Default);
			}
			reading = _serverReading;
		}
		if (await Task.WhenAny(reading, Task.Delay(ServerReadBudget)).ConfigureAwait(false) == reading && reading.IsCompletedSuccessfully)
			return reading.Result;
		// Still waiting on the network: an earlier answer for the same server is better than none.
		lock (_healthSync) if (_serverCache is { } cached && cached.Name == name) return (cached.Read, cached.Status);
		return (ServerStatusRead.Unreachable, null);
	}

	private static readonly HttpClient StatusClient = new() { Timeout = TimeSpan.FromSeconds(4) };

	private static async Task<(ServerStatusRead, ServerStatus?)> ReadServerFile(string? name)
	{
		Uri? url = CoreHealthPolicy.StatusUrl(name);
		if (url is null) return (ServerStatusRead.NotConfigured, null);
		try
		{
			using HttpResponseMessage response = await StatusClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
			if (response.StatusCode == HttpStatusCode.NotFound) return (ServerStatusRead.Missing, null);
			if (!response.IsSuccessStatusCode) return (ServerStatusRead.Unreadable, null);
			if (response.Content.Headers.ContentLength > 256 * 1024) return (ServerStatusRead.Unreadable, null);
			string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (json.Length > 256 * 1024) return (ServerStatusRead.Unreadable, null);
			ServerStatus? status = CoreHealthPolicy.Parse(json);
			return status is null ? (ServerStatusRead.Unreadable, null) : (ServerStatusRead.Ok, status);
		}
		catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { return (ServerStatusRead.Unreachable, null); }
	}

	// Port 3001 (wss) or 3000 (ws) answering means the TV's webOS service is up. A connect, nothing sent.
	private async Task<bool?> TvReachableAsync(TvSettings tv)
	{
		string? address = tv.Address?.Trim();
		if (string.IsNullOrEmpty(address)) return null;
		lock (_healthSync) if (_tvCache is { } cached && cached.Address == address && DateTimeOffset.Now - cached.At < ServerCacheFor) return cached.Reachable;
		bool reachable = false;
		foreach (int port in new[] { 3001, 3000 })
		{
			try
			{
				using var client = new TcpClient();
				using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
				await client.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
				reachable = true;
				break;
			}
			catch (Exception error) when (error is SocketException or OperationCanceledException or ArgumentException) { }
		}
		lock (_healthSync) _tvCache = (DateTimeOffset.Now, address, reachable);
		return reachable;
	}

	private async Task<HealthReport> GatherHealthAsync(bool fresh)
	{
		ModeConfig config = _store.LoadConfig();
		GamingSettings gaming = config.Gaming;
		DateTimeOffset now = DateTimeOffset.Now;
		(ServerStatusRead read, ServerStatus? status) = await ReadServerAsync(gaming.CoreHealthServer, fresh).ConfigureAwait(false);
		UpdateNightState night = UpdateNightPolicy.Observe(UpdateNightPolicy.Load(_updateNightPath), now);
		bool pending = WindowsRestartPending();
		TvSettings tv = gaming.Tv ?? new TvSettings();
		bool paired = _pairing.ClientKey is not null;
		// Stream mode: not even a plain connect reaches the TV while it is held.
		var stream = StreamHealth();
		bool? tvReachable = tv.Enabled && paired && !stream.Active ? await TvReachableAsync(tv).ConfigureAwait(false) : null;
		string? systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
		long? free = null, total = null;
		try { if (systemDrive is not null) { var info = new DriveInfo(systemDrive); free = info.AvailableFreeSpace; total = info.TotalSize; } }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
		(bool known, PlayhubUpdateService.UpdateInfo? update) = PlayhubUpdateNotice.Shared?.Peek() ?? (false, (PlayhubUpdateService.UpdateInfo?)null);
		bool? updateAvailable = known && update is not null ? update.IsNewer && !string.IsNullOrWhiteSpace(update.DownloadUrl) : null;
		(string? State, string? CallsMicName, string? CallsMicStatus) mic = MicHealth();
		return CoreHealthPolicy.Evaluate(new CoreHealthInputs
		{
			Now = now, ServerName = gaming.CoreHealthServer, ServerRead = read, Server = status,
			RestartPending = pending, UpdateNightOffer = UpdateNightPolicy.ShouldOffer(night, pending, now), UpdateNightScheduledFor = night.ScheduledFor,
			TvEnabled = tv.Enabled, TvPaired = paired, TvReachable = tvReachable,
			PcDrive = systemDrive, PcFreeBytes = free, PcTotalBytes = total,
			CoreVersion = EternalRelease.Version, LatestCoreVersion = known ? update?.LatestVersion : null, CoreUpdateAvailable = updateAvailable,
			Microphone = mic.State, CallsMicName = mic.CallsMicName, CallsMicStatus = mic.CallsMicStatus,
			StreamActive = stream.Active, StreamForced = stream.Forced, StreamDetected = stream.Detected, StreamSource = stream.Source, StreamDevice = stream.Device,
			StreamVirtualDisplay = stream.VirtualDisplay, TvHeld = stream.Active,
		});
	}

	// ---------- alerts ----------

	private async Task CheckHealthAlertsAsync()
	{
		HealthReport report = await GatherHealthAsync(fresh: true).ConfigureAwait(false);
		DateTimeOffset now = DateTimeOffset.Now;
		Dictionary<string, HealthAlertEntry> before = HealthAlertPolicy.Load(_healthAlertsPath);
		Dictionary<string, HealthAlertEntry> ledger = HealthAlertPolicy.Track(report.Problems, before);
		IReadOnlyList<HealthProblem> due = HealthAlertPolicy.Due(report.Problems, ledger, now);
		// While streaming the notice waits (not marked shown), so it appears once Core is back on the TV.
		if (due.Count > 0 && _store.LoadConfig().Gaming.CoreHealthAlertsEnabled && InGamingMode() && !GameRunning() && !StreamHoldsNotices("Core Health notice"))
		{
			string key = HealthAlertPolicy.NoticeKey(due)!;
			_logger.Info($"Core Health notice: {string.Join(", ", due.Select(problem => problem.Id))}.");
			Notice(key);
			ledger = HealthAlertPolicy.MarkShown(ledger, due, now);
		}
		if (!SameLedger(before, ledger)) HealthAlertPolicy.Save(_healthAlertsPath, ledger);
	}

	private static bool SameLedger(IReadOnlyDictionary<string, HealthAlertEntry> a, IReadOnlyDictionary<string, HealthAlertEntry> b) =>
		a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out HealthAlertEntry? other) && other == pair.Value);

	// ---------- HTTP ----------

	private object HealthView(HealthReport report)
	{
		Dictionary<string, HealthAlertEntry> ledger = HealthAlertPolicy.Track(report.Problems, HealthAlertPolicy.Load(_healthAlertsPath));
		return new
		{
			attention = report.Attention,
			alertsEnabled = _store.LoadConfig().Gaming.CoreHealthAlertsEnabled,
			rows = report.Rows.Select(row => new { id = row.Id, level = CoreHealthPolicy.Code(row.Level), state = row.State, data = row.Data }).ToArray(),
			alerts = report.Problems.Select(problem => new
			{
				id = problem.Id, rowId = problem.RowId,
				dismissed = ledger.TryGetValue(problem.Id, out HealthAlertEntry? entry) && entry.DismissedFingerprint == problem.Fingerprint,
			}).ToArray(),
		};
	}

	private void MapHealth(WebApplication app)
	{
		app.MapGet("/health", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			return Results.Json(HealthView(await GatherHealthAsync(fresh: false).ConfigureAwait(false)));
		}));
		app.MapPost("/health/dismiss", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			string id = context.Request.Query["id"].ToString();
			if (!HealthId.IsMatch(id)) return Results.Json(new { ok = false }, statusCode: 400);
			HealthReport report = await GatherHealthAsync(fresh: false).ConfigureAwait(false);
			Dictionary<string, HealthAlertEntry> ledger = HealthAlertPolicy.Dismiss(HealthAlertPolicy.Track(report.Problems, HealthAlertPolicy.Load(_healthAlertsPath)), id);
			HealthAlertPolicy.Save(_healthAlertsPath, ledger);
			_logger.Info($"Core Health: alert \"{id}\" dismissed until it changes.");
			return Results.Json(HealthView(report));
		}));
	}
}
