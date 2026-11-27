using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core batch 1 (B10): the 4 a.m. wind-down. A few calm reminders before bedtime ("It's 3:50, good
// moment to save."), by default 30 and 10 minutes before a 4:00 bedtime. Warnings only: Core never closes a
// game, never quits anything and never puts the PC to sleep for this. They go out only while a game runs
// (with nothing to save, a reminder is noise), as a Steam toast through Core's Decky plugin, because Core's
// own notice window (CoreNoticeOverlay) is for Big Picture and is never shown over a game. While Stream mode
// or SteamVR is on they still go out, quietly (no toast sound). A missed reminder stays due for Grace (a
// game started at 3:52 still hears about 3:50) and each one is given once per night.
// Hook, not integration: when something can tell that the household has gone to bed (Home Assistant's
// sleep mode, later), SleepMode = true skips the reminders. Nothing sets it yet.
// Pure here; the agent wiring is in MarkIIIHost.Batch.cs. See docs/eternal/BATCH-1.md.
internal sealed record WindDownState
{
	// When each reminder was last given (the slot's own time on its night), newest last; at most a handful.
	public List<DateTimeOffset> Given { get; init; } = new();
}

internal sealed record WindDownInputs
{
	public DateTimeOffset Now { get; init; }
	public bool Enabled { get; init; }
	public string? Bedtime { get; init; }
	public IReadOnlyList<int>? MinutesBefore { get; init; }
	public bool GameRunning { get; init; }
	public bool StreamOrVr { get; init; }
	public bool? SleepMode { get; init; }
}

internal enum WindDownAction { Nothing, Show, Skip }

internal sealed record WindDownDecision(WindDownAction Action, DateTimeOffset? Slot, int MinutesLeft, bool Quiet, string Reason);

internal static class WindDownPolicy
{
	internal static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
	internal const string DefaultBedtime = "04:00";
	internal static readonly int[] DefaultMinutes = { 30, 10 };
	internal const int MaxReminders = 4, MaxMinutesBefore = 180;

	// "04:00", "4:00", "4" -> 4:00. Anything else: the default.
	internal static TimeSpan Bedtime(string? value)
	{
		string text = (value ?? "").Trim();
		if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int hourOnly) && hourOnly is >= 0 and <= 23) return TimeSpan.FromHours(hourOnly);
		string[] parts = text.Split(':');
		if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hour) && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minute)
			&& hour is >= 0 and <= 23 && minute is >= 0 and <= 59 && parts[1].Length == 2)
			return new TimeSpan(hour, minute, 0);
		return new TimeSpan(4, 0, 0);
	}

	// "30, 10" or a list: 1 to MaxMinutesBefore minutes, distinct, at most MaxReminders, largest first. Empty: the default.
	internal static IReadOnlyList<int> Minutes(IEnumerable<int>? values)
	{
		var list = (values ?? Array.Empty<int>()).Where(value => value is >= 1 and <= MaxMinutesBefore).Distinct().OrderByDescending(value => value).Take(MaxReminders).ToList();
		return list.Count > 0 ? list : DefaultMinutes;
	}

	internal static IReadOnlyList<int> ParseMinutes(string? text) =>
		Minutes((text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0));

	// The reminder times of the day, e.g. 3:30 and 3:50 (a bedtime near midnight wraps: 23:40 for 0:10 minus 30).
	internal static IReadOnlyList<TimeSpan> Times(string? bedtime, IEnumerable<int>? minutes)
	{
		TimeSpan bed = Bedtime(bedtime);
		return Minutes(minutes).Select(value => Wrap(bed - TimeSpan.FromMinutes(value))).ToList();
	}

	private static TimeSpan Wrap(TimeSpan time)
	{
		long ticks = time.Ticks % TimeSpan.TicksPerDay;
		return TimeSpan.FromTicks(ticks < 0 ? ticks + TimeSpan.TicksPerDay : ticks);
	}

	// The latest reminder that is due now: its slot (the last occurrence of its time at or before now) is within
	// Grace and has not been given. Show while a game runs; otherwise Nothing (it may still be shown within Grace).
	internal static WindDownDecision Decide(WindDownInputs input, WindDownState state)
	{
		if (!input.Enabled) return new(WindDownAction.Nothing, null, 0, false, "off");
		DateTimeOffset now = input.Now;
		TimeSpan bed = Bedtime(input.Bedtime);
		var due = new List<(DateTimeOffset Slot, int Minutes)>();
		foreach (int minutes in Minutes(input.MinutesBefore))
		{
			TimeSpan time = Wrap(bed - TimeSpan.FromMinutes(minutes));
			var today = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset) + time;
			DateTimeOffset slot = today <= now ? today : today.AddDays(-1);
			if (now - slot < Grace && !state.Given.Contains(slot)) due.Add((slot, minutes));
		}
		if (due.Count == 0) return new(WindDownAction.Nothing, null, 0, false, "nothing due");
		(DateTimeOffset latest, int left) = due.OrderByDescending(item => item.Slot).First();
		if (input.SleepMode == true) return new(WindDownAction.Skip, latest, left, false, "sleep mode is on");
		if (!input.GameRunning) return new(WindDownAction.Nothing, latest, left, false, "no game running");
		return new(WindDownAction.Show, latest, left, input.StreamOrVr, input.StreamOrVr ? "quietly (Stream mode or VR)" : "game running");
	}

	// Mark the decision's slot (and any earlier one still within Grace) as given.
	internal static WindDownState Given(WindDownState state, DateTimeOffset slot)
	{
		var given = state.Given.Where(value => slot - value < TimeSpan.FromDays(2)).Append(slot).Distinct().OrderBy(value => value).TakeLast(8).ToList();
		return state with { Given = given };
	}

	// The time shown in the reminder ("3:50"): hours without a leading zero, 24-hour, the same in every language.
	internal static string Clock(DateTimeOffset slot) => slot.ToString("H:mm", CultureInfo.InvariantCulture);

	// ---------- state file ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static WindDownState Load(string path)
	{
		try
		{
			WindDownState? loaded = File.Exists(path) ? JsonSerializer.Deserialize<WindDownState>(File.ReadAllText(path), Json) : null;
			return loaded is null ? new() : loaded with { Given = loaded.Given ?? new() };
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, WindDownState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}
