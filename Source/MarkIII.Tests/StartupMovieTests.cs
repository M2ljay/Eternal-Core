using Playhub.Shared;

// Eternal Core tests: "Skip Steam's startup animation" (Shared/SteamStartupMovie.cs). Selecting
// Core's black movie in Steam's config.vdf, and putting the user's selection back.
static class StartupMovieTests
{
	// A config.vdf as Steam writes it, with a Points Shop movie on shuffle and the older section.
	private const string Bought = "\"InstallConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n"
		+ "\t\t\t\t\"AutoUpdateWindowEnabled\"\t\t\"0\"\n\t\t\t\t\"StartupMovie\"\n\t\t\t\t{\n\t\t\t\t\t\"MovieID\"\t\t\"2934523\"\n"
		+ "\t\t\t\t\t\"LocalPath\"\t\t\"/communityitemscache/startupmovies/2934523_ab12.webm\"\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n"
		+ "\t\"Customization\"\n\t{\n\t\t\"StartupMovie\"\n\t\t{\n\t\t\t\"MovieID\"\t\t\"2934523\"\n"
		+ "\t\t\t\"LocalPath\"\t\t\"/communityitemscache/startupmovies/2934523_ab12.webm\"\n\t\t\t\"Shuffle\"\t\t\"1\"\n\t\t}\n\t}\n"
		+ "\t\"Music\"\n\t{\n\t\t\"CrawlSteamInstallFolders\"\t\t\"1\"\n\t}\n}\n";

	// A fresh Steam: no Customization section, no older section.
	private const string Fresh = "\"InstallConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"Steam\"\n\t\t\t{\n"
		+ "\t\t\t\t\"BigPictureInForeground\"\t\t\"1\"\n\t\t\t}\n\t\t}\n\t}\n}\n";

	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		const string Ours = SteamStartupMovie.CoreLocalPath;

		// selecting and restoring a bought movie
		var on = SteamStartupMovie.Sync(Bought, null, skip: true);
		var after = SteamStartupMovie.Read(on.Text);
		Check(on.Outcome == StartupMovieOutcome.Selected && SteamStartupMovie.IsCoreSelected(on.Text), "skip on: Core's black movie is selected");
		Check(after.Customization is { MovieId: "0", LocalPath: Ours, Shuffle: "0" } && after.Legacy is { MovieId: "0", LocalPath: Ours, Shuffle: null },
			"both sections point at Core's movie, shuffle off, no shuffle key added where there was none");
		Check(on.Record is { Customization: { Present: true, MovieId: "2934523", Shuffle: "1" }, CustomizationParentPresent: true, Legacy.Present: true },
			"the replaced selection is recorded");
		Check(on.Text.Contains("\"AutoUpdateWindowEnabled\"\t\t\"0\"") && on.Text.Contains("\"CrawlSteamInstallFolders\"\t\t\"1\"")
			&& on.Text.Length - Bought.Length == 2 * (Ours.Length - "/communityitemscache/startupmovies/2934523_ab12.webm".Length) + 2 * ("0".Length - "2934523".Length),
			"nothing else in config.vdf changes");
		Check(SteamStartupMovie.Sync(on.Text, on.Record, skip: true) is { Outcome: StartupMovieOutcome.Kept } kept && kept.Text == on.Text && kept.Record == on.Record,
			"applying again changes nothing");
		var off = SteamStartupMovie.Sync(on.Text, on.Record, skip: false);
		Check(off.Outcome == StartupMovieOutcome.Restored && off.Text == Bought && off.Record is null, "skip off: the bought movie, shuffle and the older section come back byte for byte");

		// a fresh Steam: the sections Core added are taken away again
		var fresh = SteamStartupMovie.Sync(Fresh, null, skip: true);
		Check(fresh.Outcome == StartupMovieOutcome.Selected && SteamStartupMovie.Read(fresh.Text) is { Customization.LocalPath: Ours, Legacy.Present: false },
			"fresh Steam: Customization > StartupMovie is created, the older section is not");
		Check(SteamStartupMovie.Sync(fresh.Text, fresh.Record, skip: false).Text == Fresh, "fresh Steam: turning it off removes what Core added");
		string emptyCustomization = Fresh.Replace("}\n}\n", "}\n\t\"Customization\"\n\t{\n\t}\n}\n", StringComparison.Ordinal);
		var partial = SteamStartupMovie.Sync(emptyCustomization, null, skip: true);
		Check(SteamStartupMovie.Sync(partial.Text, partial.Record, skip: false).Text == emptyCustomization, "an existing Customization section stays when Core's movie goes");

		// the user's own choices win
		string picked = on.Text.Replace("\t\t\t\"MovieID\"\t\t\"0\"\n\t\t\t\"LocalPath\"\t\t\"" + Ours + "\"",
			"\t\t\t\"MovieID\"\t\t\"0\"\n\t\t\t\"LocalPath\"\t\t\"/uioverrides/movies/my_movie.webm\"", StringComparison.Ordinal);
		Check(!SteamStartupMovie.IsCoreSelected(picked), "fixture: a movie picked in Steam afterwards");
		var left = SteamStartupMovie.Sync(picked, on.Record, skip: false);
		Check(left.Outcome == StartupMovieOutcome.LeftUsersPick && left.Text == picked && left.Record is null, "skip off after picking a movie in Steam: the pick stays");
		var again = SteamStartupMovie.Sync(picked, on.Record, skip: true);
		Check(again.Outcome == StartupMovieOutcome.Reselected && again.Record!.Customization.LocalPath == "/uioverrides/movies/my_movie.webm",
			"skip still on after picking in Steam: Core's movie again, and the new pick is what comes back later");
		Check(SteamStartupMovie.Read(SteamStartupMovie.Sync(again.Text, again.Record, skip: false).Text).Customization.LocalPath == "/uioverrides/movies/my_movie.webm",
			"...and it does come back");
		Check(SteamStartupMovie.Sync(Bought, null, skip: false) is { Outcome: StartupMovieOutcome.NothingToRestore } nothing && nothing.Text == Bought,
			"skip off from the start: config.vdf is never touched");

		// recovery and robustness
		var lost = SteamStartupMovie.Sync(on.Text, null, skip: false);
		Check(lost.Outcome == StartupMovieOutcome.ResetToDefault && SteamStartupMovie.Read(lost.Text).Customization is { MovieId: "0", LocalPath: "" },
			"record lost: Steam's default movie, never a dangling path to Core's");
		Check(SteamStartupMovie.Sync("\"InstallConfigStore\"\n{\n\t\"Software\"\n", null, skip: true) is { Outcome: StartupMovieOutcome.Unreadable, Text: "\"InstallConfigStore\"\n{\n\t\"Software\"\n" },
			"a damaged config.vdf is left alone");
		string crlf = Bought.Replace("\n", "\r\n", StringComparison.Ordinal);
		var windows = SteamStartupMovie.Sync(crlf, null, skip: true);
		Check(!windows.Text.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n') && SteamStartupMovie.Sync(windows.Text, windows.Record, skip: false).Text == crlf,
			"Windows line endings are kept");
		string commented = Bought.Replace("\t\"Music\"", "\t// a comment with \"quotes\" { and a brace\n\t\"Music\"", StringComparison.Ordinal);
		Check(SteamStartupMovie.Sync(commented, null, skip: true).Outcome == StartupMovieOutcome.Selected, "comments do not confuse the reader");

		// on disk
		string root = Path.Combine(Path.GetTempPath(), "core-startup-movie-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(Path.Combine(root, "config"));
			string config = SteamStartupMovie.ConfigPath(root), record = Path.Combine(root, "state", SteamStartupMovie.RecordFileName);
			File.WriteAllText(config, Bought);
			Stream Movie() => new MemoryStream(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3 });
			Check(SteamStartupMovie.Apply(root, record, true, steamRunning: true, Movie) == StartupMovieOutcome.SteamRunning
				&& File.ReadAllText(config) == Bought && !File.Exists(SteamStartupMovie.MoviePath(root)), "never while Steam runs (it rewrites config.vdf from memory)");
			Check(SteamStartupMovie.Apply(root, record, true, false, Movie) == StartupMovieOutcome.Selected && File.Exists(SteamStartupMovie.MoviePath(root))
				&& File.Exists(record) && SteamStartupMovie.IsCoreSelected(File.ReadAllText(config)), "installs the movie, selects it and records the old choice");
			Check(SteamStartupMovie.Apply(root, record, true, false, () => null) == StartupMovieOutcome.Failed && SteamStartupMovie.IsCoreSelected(File.ReadAllText(config)),
				"without the movie asset nothing is selected anew");
			Check(SteamStartupMovie.Apply(root, record, false, false, null) == StartupMovieOutcome.Restored && File.ReadAllText(config) == Bought
				&& !File.Exists(SteamStartupMovie.MoviePath(root)) && !File.Exists(record), "turning it off restores config.vdf and removes Core's movie and record");
			Check(SteamStartupMovie.Apply(Path.Combine(root, "missing"), record, true, false, Movie) == StartupMovieOutcome.NoSteamConfig, "no Steam config: nothing to do");
		}
		finally { try { Directory.Delete(root, true); } catch (IOException) { } }
	}
}
