using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;

namespace GamingMode.Services;

// Eternal Core: games are exited through Steam, and Steam's Exit Game closes the whole game.
// Steam on Windows ends what it started: for a non-Steam shortcut that is the program the shortcut names. A
// wrapper (a .bat/.ps1 such as EmuDeck's launcher scripts, which wait with Start-Process -Wait, or a launcher
// exe) can leave the real emulator running after Steam ends the wrapper, and a wrapper that exits at once
// makes Steam think the game is over while it still runs, so Steam no longer offers Exit Game.
// Core follows each launch's process tree (children of Steam started after the launch, their children while
// they live, and a catalogue emulator started during the launch) and, when Steam reports the game ended or the
// user chose Exit Game, closes what is left: politely first (WM_CLOSE), then terminated after a timeout. Only
// processes of that launch, never Steam, SteamVR, store launchers (their own rule below) or anything that was
// running before. When Steam lost track of a game that is still on screen, Core does not close it: it offers
// its own Exit in Quick Access and says so in Health. Pure: process snapshots in, decisions out.
// docs/eternal/CONSOLE-GUARDS.md ("Exiting games").

internal sealed record ProcessSeen(int Pid, int ParentPid, string Name, string? Path, DateTimeOffset Started, bool HasWindow);

internal sealed record TrackedProcess(int Pid, string Name, DateTimeOffset Started);

internal sealed record GameSession
{
	public uint AppId { get; init; }
	public string? Title { get; init; }
	public DateTimeOffset StartedAt { get; init; }
	public List<TrackedProcess> Tree { get; init; } = new();
	// Store launchers running when the game started (they are never closed for this game).
	public List<string> LaunchersBefore { get; init; } = new();
	// Store launchers that appeared during the game.
	public List<string> LaunchersStarted { get; init; } = new();
	public bool ExitChosen { get; init; }
	public bool SteamReportedEnd { get; init; }
}

internal enum LeftoverAction { Close, Leave }

internal sealed record LeftoverDecision(LeftoverAction Action, string Reason, IReadOnlyList<TrackedProcess> Processes);

internal static class GameExitPolicy
{
	internal static readonly TimeSpan GracefulTimeout = TimeSpan.FromSeconds(8);

	// Steam's own processes and SteamVR are never part of a game's tree.
	private static readonly HashSet<string> SteamOwn = new(StringComparer.OrdinalIgnoreCase)
	{
		"steam", "steamwebhelper", "steamerrorreporter", "steamerrorreporter64", "gameoverlayui", "gameoverlayui64", "steamservice", "streaming_client",
		"steamtours", "vrserver", "vrmonitor", "vrcompositor", "vrdashboard", "vrwebhelper", "vrstartup", "vrserverhelper", "crashhandler", "crashhandler64",
		"conhost", "werfault", "wermgr", "dwm", "explorer",
	};

	internal static string Bare(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

	internal static bool IsSteamOwn(string name) => SteamOwn.Contains(Bare(name));

	// New members of a running launch's tree from one snapshot. A process joins when its parent is a live
	// member (matched by pid and an earlier start time, so a reused pid never counts), when Steam itself
	// started it after the launch, or when it is a catalogue emulator started after the launch.
	internal static GameSession Observe(GameSession session, IReadOnlyList<ProcessSeen> snapshot, int steamPid)
	{
		var tree = session.Tree.Where(member => snapshot.Any(process => process.Pid == member.Pid && process.Started == member.Started)).ToList();
		bool changed = true;
		while (changed)
		{
			changed = false;
			foreach (ProcessSeen process in snapshot)
			{
				if (process.Started < session.StartedAt.AddSeconds(-2) || IsSteamOwn(process.Name) || LauncherCatalog.ByProcess(process.Name) is not null) continue;
				if (tree.Any(member => member.Pid == process.Pid && member.Started == process.Started)) continue;
				ProcessSeen? parent = snapshot.FirstOrDefault(other => other.Pid == process.ParentPid && other.Started <= process.Started);
				bool childOfTree = parent is not null && tree.Any(member => member.Pid == parent.Pid && member.Started == parent.Started);
				bool childOfSteam = steamPid > 0 && process.ParentPid == steamPid && parent is not null;
				bool emulator = EmulatorHealthPolicy.ByExe(process.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process.Name : process.Name + ".exe") is not null;
				if (childOfTree || childOfSteam || emulator)
				{
					tree.Add(new TrackedProcess(process.Pid, process.Name, process.Started));
					changed = true;
				}
			}
		}
		return session with { Tree = tree };
	}

	// What to do with the processes still alive when the launch ended. Exit Game chosen: close them all. Steam
	// reported the end on its own: windowless leftovers (helpers, a wrapper's child with no window) are closed;
	// a leftover with a window is the game still on screen, which Steam lost track of: left for the user, who
	// gets Core's Exit.
	internal static LeftoverDecision Decide(GameSession session, IReadOnlyList<ProcessSeen> snapshot)
	{
		var alive = session.Tree.Where(member => snapshot.Any(process => process.Pid == member.Pid && process.Started == member.Started)).ToList();
		if (alive.Count == 0) return new LeftoverDecision(LeftoverAction.Leave, "nothing-left", alive);
		if (session.ExitChosen) return new LeftoverDecision(LeftoverAction.Close, "exit-chosen", alive);
		var windowed = alive.Where(member => snapshot.Any(process => process.Pid == member.Pid && process.HasWindow)).ToList();
		if (windowed.Count > 0) return new LeftoverDecision(LeftoverAction.Leave, "steam-lost-track", alive);
		return new LeftoverDecision(LeftoverAction.Close, "steam-ended", alive);
	}

	// Close the leaves first (the emulator before its wrapper), so a wrapper cannot restart what it waits on.
	internal static IReadOnlyList<TrackedProcess> CloseOrder(IReadOnlyList<TrackedProcess> processes, IReadOnlyList<ProcessSeen> snapshot)
	{
		int Depth(TrackedProcess member)
		{
			int depth = 0;
			ProcessSeen? current = snapshot.FirstOrDefault(process => process.Pid == member.Pid);
			var seen = new HashSet<int>();
			while (current is not null && seen.Add(current.Pid) && processes.Any(other => other.Pid == current.ParentPid))
			{
				depth++;
				int parent = current.ParentPid;
				current = snapshot.FirstOrDefault(process => process.Pid == parent);
			}
			return depth;
		}
		return processes.OrderByDescending(Depth).ThenBy(member => member.Pid).ToList();
	}

	// The "Exiting games" Health row.
	internal static HealthRow Row(GameExitRecord? last, string? untrackedTitle, DateTimeOffset now)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["title"] = last?.Title, ["at"] = last?.At, ["closed"] = last?.Closed, ["left"] = last?.Left, ["reason"] = last?.Reason, ["untracked"] = untrackedTitle,
		};
		if (untrackedTitle is not null)
		{
			data["fix"] = "game-exit-now";
			return new HealthRow("game-exit", HealthLevel.Attention, "untracked", data);
		}
		if (last is null || now - last.At > TimeSpan.FromHours(12)) return new HealthRow("game-exit", HealthLevel.Ok, "watching", data);
		if (last.Left > 0) return new HealthRow("game-exit", HealthLevel.Attention, "did-not-exit", data);
		return new HealthRow("game-exit", HealthLevel.Ok, last.Closed > 0 ? "cleaned" : "clean", data);
	}
}

internal sealed record GameExitRecord(string? Title, DateTimeOffset At, int Closed, int Left, string Reason, IReadOnlyList<string> Names);

// ---------- store launchers: close them after the game they were started for ----------

internal sealed record LauncherDef(string Id, string Name, IReadOnlyList<string> Processes);

internal static class LauncherCatalog
{
	// The process that stays when each launcher sits in the tray (its helpers are children of it).
	internal static readonly IReadOnlyList<LauncherDef> All = new List<LauncherDef>
	{
		new("epic", "Epic Games Launcher", new[] { "EpicGamesLauncher" }),
		new("ea", "EA app", new[] { "EADesktop" }),
		new("ubisoft", "Ubisoft Connect", new[] { "upc", "UbisoftConnect" }),
		new("battlenet", "Battle.net", new[] { "Battle.net" }),
		new("gog", "GOG Galaxy", new[] { "GalaxyClient" }),
		new("rockstar", "Rockstar Games Launcher", new[] { "LauncherPatcher", "RockstarLauncher" }),
		new("amazon", "Amazon Games", new[] { "Amazon Games" }),
	};

	internal static LauncherDef? ByProcess(string? name) => name is null ? null
		: All.FirstOrDefault(launcher => launcher.Processes.Any(process => string.Equals(process, GameExitPolicy.Bare(name), StringComparison.OrdinalIgnoreCase)));

	internal static LauncherDef? Find(string id) => All.FirstOrDefault(launcher => launcher.Id == id);

	// Per-launcher switches (Quick Access > Core and the desktop app); every launcher is on unless switched off.
	internal static bool Enabled(IReadOnlyDictionary<string, bool>? switches, string id) => switches is null || !switches.TryGetValue(id, out bool on) || on;
}

internal enum LauncherCloseAction { Leave, Wait, Close }

internal sealed record LauncherCloseInput(bool Enabled, bool WasRunningBefore, bool StartedDuringGame, bool OtherGameRunning, bool InstallerRunning,
	long WrittenBytesDuringGrace, TimeSpan SinceGameEnded);

internal static class LauncherClosePolicy
{
	// After the game ends Core waits this long, watching the launcher's disk writes; a download or an update
	// writes far more than this in that time. Launchers do not report a download to other programs, so this is
	// the signal Core can see. The close is always polite (WM_CLOSE to its windows); Core never terminates a
	// launcher.
	internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(90);
	internal const long BusyBytes = 32L * 1024 * 1024;

	internal static LauncherCloseAction Decide(LauncherCloseInput input)
	{
		if (!input.Enabled || input.WasRunningBefore || !input.StartedDuringGame || input.OtherGameRunning) return LauncherCloseAction.Leave;
		if (input.InstallerRunning) return LauncherCloseAction.Leave;
		if (input.SinceGameEnded < Grace) return input.WrittenBytesDuringGrace >= BusyBytes ? LauncherCloseAction.Leave : LauncherCloseAction.Wait;
		return input.WrittenBytesDuringGrace >= BusyBytes ? LauncherCloseAction.Leave : LauncherCloseAction.Close;
	}
}

// ---------- Core's memory: %APPDATA%\GamingMode\game-exit.json ----------

internal sealed record GameExitState
{
	public GameExitRecord? Last { get; init; }
	public List<DateTimeOffset> LaunchersClosed { get; init; } = new();
	public string? LastLauncherClosed { get; init; }
}

internal static class GameExitStore
{
	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static GameExitState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<GameExitState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, GameExitState state)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}

	internal static int ClosedToday(GameExitState state, DateTimeOffset now) => state.LaunchersClosed.Count(at => at.Date == now.Date && at <= now);
}
