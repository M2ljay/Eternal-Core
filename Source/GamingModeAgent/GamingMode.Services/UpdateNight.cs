using System;
using System.IO;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core Mark III: "update night". Couch hardening holds Windows Update restarts
// while someone is signed in (always, on Core), so updates need a deliberate outlet. When
// Windows says a restart is pending, or a month has passed since the last update night,
// Core's Quick Access page offers "Restart tonight at 5:00". At 5:00 the agent restarts
// into Gaming Mode, never during a game: it retries until 7:00, then gives up and asks
// again. Pure decisions here; the agent wiring is in MarkIIIHost.
internal sealed record UpdateNightState
{
	public DateTimeOffset? FirstSeen { get; init; }
	public DateTimeOffset? LastNight { get; init; }
	public DateTimeOffset? ScheduledFor { get; init; }
}

internal enum UpdateNightAction { Wait, Restart, GiveUp }

internal static class UpdateNightPolicy
{
	internal const int RestartHour = 5, GiveUpHour = 7, MonthDays = 30;

	internal static UpdateNightState Observe(UpdateNightState state, DateTimeOffset now) =>
		state.FirstSeen is null ? state with { FirstSeen = now } : state;

	internal static bool ShouldOffer(UpdateNightState state, bool restartPending, DateTimeOffset now)
	{
		if (state.ScheduledFor is not null) return false;
		if (restartPending) return true;
		DateTimeOffset since = state.LastNight ?? state.FirstSeen ?? now;
		return now - since >= TimeSpan.FromDays(MonthDays);
	}

	// The next 5:00 local time: today's if it is still ahead (1 a.m. means "in four hours"),
	// otherwise tomorrow's.
	internal static DateTimeOffset NextRestart(DateTimeOffset now)
	{
		var today = new DateTimeOffset(now.Year, now.Month, now.Day, RestartHour, 0, 0, now.Offset);
		return now < today ? today : today.AddDays(1);
	}

	internal static UpdateNightAction Decide(UpdateNightState state, DateTimeOffset now, bool gameRunning)
	{
		if (state.ScheduledFor is not { } due || now < due) return UpdateNightAction.Wait;
		if (!gameRunning) return UpdateNightAction.Restart;
		return now >= due.AddHours(GiveUpHour - RestartHour) ? UpdateNightAction.GiveUp : UpdateNightAction.Wait;
	}

	// ---------- state file ----------

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static UpdateNightState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<UpdateNightState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, UpdateNightState state)
	{
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}
}
