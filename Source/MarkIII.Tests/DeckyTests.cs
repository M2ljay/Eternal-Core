using System.Text.RegularExpressions;
using GamingMode.Services;

// Eternal Core Mark III tests: Decky heartbeat health, agent text.
static class DeckyTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		// Decky heartbeat health
		{
			var t0 = new DateTimeOffset(2026, 11, 25, 20, 0, 0, TimeSpan.Zero);
			DeckyObservation O(int seconds, bool bp = true, bool game = false, int? pid = 100, bool gaming = true, bool expected = true, string? version = "1") =>
				new(t0.AddSeconds(seconds), gaming, expected, pid, bp, game, version);

			var healthy = new DeckyHeartbeatMonitor();
			healthy.Observe(O(0));
			healthy.Heartbeat(t0.AddSeconds(20), "1");
			Check(!healthy.Observe(O(95)) && healthy.Verdict == DeckyVerdict.Healthy, "heartbeat after Big Picture is up: healthy, no notice");
			healthy.Heartbeat(t0.AddSeconds(110), "1");
			Check(!healthy.Observe(O(130)) && healthy.Verdict == DeckyVerdict.Healthy && healthy.EverHealthy && healthy.HealthySince(t0.AddSeconds(100)), "regular heartbeats stay healthy");

			var missing = new DeckyHeartbeatMonitor();
			Check(!missing.Observe(O(0, bp: false)) && missing.Verdict == DeckyVerdict.Waiting, "before Big Picture: waiting");
			Check(!missing.Observe(O(10)) && !missing.Observe(O(80)) && missing.Verdict == DeckyVerdict.Waiting, "up to 90 s after Big Picture: still waiting");
			Check(missing.Observe(O(101)) && missing.Verdict == DeckyVerdict.NotLoaded, "no heartbeat 90 s after Big Picture: one notice");
			Check(!missing.Observe(O(200)) && !missing.Observe(O(900)), "the notice shows once per Steam session");
			Check(missing.Observe(O(1100, pid: 200)) == false && missing.Observe(O(1200, pid: 200)), "a new Steam session can show it again");

			var stopped = new DeckyHeartbeatMonitor();
			stopped.Observe(O(0)); stopped.Heartbeat(t0.AddSeconds(5), "1");
			Check(!stopped.Observe(O(60)) && stopped.Verdict == DeckyVerdict.Healthy, "healthy while heartbeats arrive");
			Check(stopped.Observe(O(100)) && stopped.Verdict == DeckyVerdict.NotLoaded, "heartbeats stopping for 90 s: notice");

			var gaming = new DeckyHeartbeatMonitor();
			gaming.Observe(O(0));
			Check(!gaming.Observe(O(120, game: true)) && gaming.Verdict == DeckyVerdict.NotLoaded, "no notice over a game");
			Check(gaming.Observe(O(400)), "the notice waits until the game is closed");

			var desktop = new DeckyHeartbeatMonitor();
			Check(!desktop.Observe(O(0, gaming: false)) && !desktop.Observe(O(500, gaming: false)) && desktop.Verdict == DeckyVerdict.NotApplicable, "Desktop Mode: no check");
			var noDecky = new DeckyHeartbeatMonitor();
			Check(!noDecky.Observe(O(0, expected: false)) && !noDecky.Observe(O(500, expected: false)), "Decky not installed or not started: no notice");

			var dismissed = new DeckyHeartbeatMonitor { DismissedForSteamVersion = "1" };
			dismissed.Observe(O(0));
			Check(!dismissed.Observe(O(200)) && dismissed.Verdict == DeckyVerdict.NotLoaded, "\"continue without plugins\": quiet for this Steam version");
			Check(dismissed.Observe(O(300, pid: 300, version: "2")) == false && dismissed.Observe(O(500, pid: 300, version: "2")), "a new Steam version brings the notice back");

			var versions = new DeckyHeartbeatMonitor();
			versions.Heartbeat(t0, "1");
			Check(versions.AfterSteamUpdate("2") && !versions.AfterSteamUpdate("1") && !new DeckyHeartbeatMonitor().AfterSteamUpdate("2"), "the notice says \"after a Steam update\" only when the version changed");
		}
		// agent text
		{
			static string[] Tokens(string value) => Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
			foreach (var (key, values) in CoreText.Entries)
				Check(values.Length == 12 && values.All(v => !string.IsNullOrWhiteSpace(v)) && values.All(v => Tokens(v).SequenceEqual(Tokens(values[1]))), $"agent text \"{key}\" exists in 12 languages");
			Check(CoreText.Get("it", "repair.close") == "Chiudi" && CoreText.Get("ja", "repair.close") == "閉じる" && CoreText.Get("xx", "repair.close") == "Close" && CoreText.Get("pt-BR", "repair.close") == "Fechar", "language lookup and English fallback");
			Check(CoreText.Format("en", "repair.failed", "disk full") == "That didn't work: disk full", "formatted text");
		}
	}
}
