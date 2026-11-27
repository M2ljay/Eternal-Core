using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core: emulator updates with the Steam-update safety net (docs/eternal/EMULATOR-UPDATES.md). New
// releases are read from each emulator's official GitHub project only (the same projects EmuDeck for Windows
// downloads from), soak 10 days per version, run on an update night while idle, and go through a backup of
// the program files, an overlay that never touches user data, a smoke test and an automatic rollback.
// Pure: release JSON, file lists and observations in, decisions out. The Windows half is
// EmulatorUpdates.Windows.cs, the wiring MarkIIIHost.Emulators.cs.
//
// Why not EmuDeck's own updater: EmuDeck asks before each update in its launcher script (isLatestVersionGH,
// helperFunctions.ps1) and installs with <Emu>_install, which is not built to run unattended (winget prompts,
// progress messages, git resets of its backend) and offers no backup or rollback. Core reuses its sources,
// folder layout and file renames instead, and leaves EmuDeck's "Manage Emulators" for the rest.

// One emulator's official release source. AssetPattern/Exclude pick the Windows build (EmuDeck's own
// getReleaseURLGH filters); Renames repeats EmuDeck's post-extract renames.
internal sealed record UpdateSource(string Emulator, string Repository, string AssetPattern, string? Exclude, IReadOnlyDictionary<string, string> Renames, IReadOnlyList<string> Protected);

internal sealed record ReleaseAsset(string Name, string Url, long Size, string? Digest);

internal sealed record ReleaseInfo(string Tag, DateTimeOffset? Published, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets);

internal static class EmulatorUpdateSources
{
	private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

	// Program-relative paths that hold user data in EmuDeck's portable layouts (configs\*, the *_init
	// functions). An archive entry under one of them is skipped, and they are never backed up or restored:
	// only program files move.
	internal static readonly IReadOnlyList<UpdateSource> All = new List<UpdateSource>
	{
		new("pcsx2", "PCSX2/pcsx2", @"windows.*\.7z$", "symbols", new Dictionary<string, string> { ["pcsx2-qt.exe"] = "pcsx2-qtx64.exe" },
			new[] { "inis", "memcards", "sstates", "bios", "cache", "snaps", "covers", "logs", "cheats", "textures", "gamesettings", "inputprofiles", "videos", "portable.ini", "portable.txt" }),
		new("duckstation", "stenzek/duckstation", @"windows-x64.*\.zip$", "symbols", None,
			new[] { "settings.ini", "portable.txt", "memcards", "savestates", "bios", "cache", "covers", "screenshots", "inputprofiles", "gamesettings", "cheats", "dump", "shaders", "textures" }),
		new("azahar", "azahar-emu/azahar", @"windows-msvc.*\.zip$", null, None, new[] { "user" }),
		new("rpcs3", "RPCS3/rpcs3-binaries-win", @"\.7z$", null, None,
			new[] { "config", "config.yml", "vfs.yml", "games.yml", "dev_hdd0", "dev_hdd1", "dev_flash", "dev_flash2", "dev_flash3", "dev_usb000", "dev_bdvd", "cache", "games", "savestates", "patches", "captures", "GuiConfigs", "logs" }),
		new("xenia", "xenia-canary/xenia-canary", @"windows.*\.7z$", null, None,
			new[] { "portable.txt", "content", "cache", "cache0", "cache1", "xenia-canary.config.toml", "patches", "screenshots" }),
		new("shadps4", "shadps4-emu/shadps4-qtlauncher", @"win64.*\.zip$", null, None, new[] { "user" }),
		new("cemu", "cemu-project/Cemu", @"windows-x64.*\.zip$", null, None,
			new[] { "mlc01", "settings.xml", "keys.txt", "controllerProfiles", "gameProfiles", "graphicPacks", "shaderCache", "portable" }),
		new("xemu", "xemu-project/xemu", @"windows-x86_64.*\.zip$", "pdb|dbg", None, new[] { "xemu.toml", "eeprom.bin", "xbox_hdd.qcow2" }),
	};

	// Emulators EmuDeck takes from elsewhere (buildbot, their own sites, a non-GitHub forge): Core does not
	// update them; EmuDeck's Manage Emulators does.
	internal static readonly IReadOnlyDictionary<string, string> NotOffered = new Dictionary<string, string>
	{
		["retroarch"] = "buildbot.libretro.com", ["dolphin"] = "dolphin-emu.org", ["ppsspp"] = "ppsspp.org", ["ryujinx"] = "git.ryujinx.app",
	};

	internal static UpdateSource? Find(string emulator) => All.FirstOrDefault(source => source.Emulator == emulator);

	// GitHub's /releases/latest answer (the newest release that is neither a draft nor a prerelease).
	internal static ReleaseInfo? ParseRelease(string json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out JsonElement tag) || tag.ValueKind != JsonValueKind.String) return null;
			if (root.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True) return null;
			DateTimeOffset? published = root.TryGetProperty("published_at", out JsonElement at) && at.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : null;
			bool prerelease = root.TryGetProperty("prerelease", out JsonElement pre) && pre.ValueKind == JsonValueKind.True;
			var assets = new List<ReleaseAsset>();
			if (root.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
				foreach (JsonElement asset in list.EnumerateArray())
				{
					string? name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
					string? url = asset.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() : null;
					long size = asset.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long value) ? value : 0;
					string? digest = asset.TryGetProperty("digest", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
					if (name is not null && url is not null) assets.Add(new ReleaseAsset(name, url, size, digest));
				}
			return new ReleaseInfo(tag.GetString()!, published, prerelease, assets);
		}
		catch (JsonException) { return null; }
	}

	internal static ReleaseAsset? PickAsset(UpdateSource source, ReleaseInfo release)
	{
		var pattern = new Regex(source.AssetPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		Regex? exclude = source.Exclude is null ? null : new Regex(source.Exclude, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		// Only downloads from GitHub's own release host for this repository.
		string prefix = "https://github.com/" + source.Repository + "/releases/download/";
		return release.Assets.FirstOrDefault(asset => pattern.IsMatch(asset.Name) && (exclude is null || !exclude.IsMatch(asset.Name))
			&& asset.Url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && asset.Size > 0);
	}

	// A checksum file the project itself publishes beside the asset (SHA256SUMS, <asset>.sha256...).
	internal static ReleaseAsset? ChecksumAsset(ReleaseInfo release, ReleaseAsset asset) =>
		release.Assets.FirstOrDefault(other => string.Equals(other.Name, asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(other.Name, asset.Name + ".sha256sum", StringComparison.OrdinalIgnoreCase))
		?? release.Assets.FirstOrDefault(other => other.Name.ToLowerInvariant() is "sha256sums" or "sha256sums.txt" or "checksums.txt" or "sha256sum.txt");

	// The SHA-256 a checksum file gives for a file name ("<hex>  <name>" or "<hex> *<name>", or a lone hash in a
	// per-asset file).
	internal static string? ChecksumFor(string checksumText, string assetName, bool perAsset)
	{
		foreach (string raw in checksumText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
		{
			Match match = Regex.Match(raw.Trim(), @"^([0-9a-fA-F]{64})(?:\s+\*?(.+))?$", RegexOptions.CultureInvariant);
			if (!match.Success) continue;
			string name = match.Groups[2].Value.Trim();
			if ((perAsset && name.Length == 0) || string.Equals(Path.GetFileName(name), assetName, StringComparison.OrdinalIgnoreCase)) return match.Groups[1].Value.ToLowerInvariant();
		}
		return null;
	}

	// GitHub's digest field is "sha256:<hex>".
	internal static string? GitHubSha256(string? digest) =>
		digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && digest.Length == 71 ? digest[7..].ToLowerInvariant() : null;
}

// How a download is trusted. Project: the project publishes a SHA-256 for it. GitHubOnly: only GitHub's own
// digest of the uploaded file exists, which proves the bytes are the ones uploaded but not who uploaded
// them, so the user confirms that version once on the desktop. None: no hash at all (also confirmed once).
internal enum UpdateVerification { Project, GitHubOnly, None }

internal sealed record PendingEmulatorUpdate
{
	public string Version { get; init; } = "";
	public string Asset { get; init; } = "";
	public string Url { get; init; } = "";
	public long Size { get; init; }
	public string? GitHubSha256 { get; init; }
	public string? ChecksumUrl { get; init; }
	public bool ChecksumPerAsset { get; init; }
	public UpdateVerification Verification { get; init; }
	public DateTimeOffset FirstSeen { get; init; }
}

internal sealed record EmulatorUpdateEntry
{
	public string? Installed { get; init; }
	public PendingEmulatorUpdate? Pending { get; init; }
	public DateTimeOffset? LastCheck { get; init; }
	public DateTimeOffset? LastUpdateAt { get; init; }
	public string? LastResult { get; init; }
	public string? FailedVersion { get; init; }
	public List<string> Confirmed { get; init; } = new();
}

internal sealed record EmulatorUpdateState
{
	public Dictionary<string, EmulatorUpdateEntry> Emulators { get; init; } = new(StringComparer.Ordinal);
}

internal enum EmulatorUpdatePhase { NotOffered, NotInstalled, Current, Soaking, NeedsConfirmation, Due, WaitingForChange }

internal static class EmulatorUpdatePolicy
{
	internal const int DefaultSoakDays = 10;
	internal static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);
	internal static readonly TimeSpan SmokeAlive = TimeSpan.FromSeconds(6);

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

	internal static EmulatorUpdateState Load(string path)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<EmulatorUpdateState>(File.ReadAllText(path), Json) ?? new() : new(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
	}

	internal static void Save(string path, EmulatorUpdateState state)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(state, Json));
		File.Move(temporary, path, true);
	}

	// A release seen for the first time starts its own soak; a newer one restarts it. The version Core installed
	// (or the tag it recorded as current) is not pending.
	internal static EmulatorUpdateEntry Observe(EmulatorUpdateEntry entry, UpdateSource source, ReleaseInfo release, ReleaseAsset? asset, ReleaseAsset? checksum, DateTimeOffset now)
	{
		entry = entry with { LastCheck = now };
		if (asset is null || release.Prerelease) return entry with { Pending = null };
		// The first release seen for an emulator Core never updated counts as the installed one: Core cannot tell
		// EmuDeck's build from a tag, and would otherwise "update" to what is already there.
		if (entry.Installed is null) return entry with { Installed = release.Tag, Pending = null };
		if (string.Equals(entry.Installed, release.Tag, StringComparison.Ordinal)) return entry with { Pending = null };
		if (entry.Pending is { } pending && pending.Version == release.Tag) return entry;
		string? github = EmulatorUpdateSources.GitHubSha256(asset.Digest);
		var verification = checksum is not null ? UpdateVerification.Project : github is not null ? UpdateVerification.GitHubOnly : UpdateVerification.None;
		return entry with
		{
			Pending = new PendingEmulatorUpdate
			{
				Version = release.Tag, Asset = asset.Name, Url = asset.Url, Size = asset.Size, GitHubSha256 = github,
				ChecksumUrl = checksum?.Url, ChecksumPerAsset = checksum is not null && checksum.Name.StartsWith(asset.Name, StringComparison.OrdinalIgnoreCase),
				Verification = verification, FirstSeen = now,
			},
		};
	}

	internal static EmulatorUpdatePhase Phase(string emulator, bool installed, EmulatorUpdateEntry? entry, int soakDays, DateTimeOffset now)
	{
		if (EmulatorUpdateSources.Find(emulator) is null) return EmulatorUpdatePhase.NotOffered;
		if (!installed) return EmulatorUpdatePhase.NotInstalled;
		if (entry?.Pending is not { } pending) return EmulatorUpdatePhase.Current;
		if (entry.FailedVersion == pending.Version) return EmulatorUpdatePhase.WaitingForChange;
		if (pending.Verification != UpdateVerification.Project && !entry.Confirmed.Contains(pending.Version, StringComparer.Ordinal)) return EmulatorUpdatePhase.NeedsConfirmation;
		return now - pending.FirstSeen >= TimeSpan.FromDays(Math.Clamp(soakDays, 1, 60)) ? EmulatorUpdatePhase.Due : EmulatorUpdatePhase.Soaking;
	}

	internal static int DaysLeft(EmulatorUpdateEntry? entry, int soakDays, DateTimeOffset now) =>
		entry?.Pending is { } pending ? Math.Max(0, (int)Math.Ceiling((pending.FirstSeen.AddDays(soakDays) - now).TotalDays)) : 0;

	// Automatic updates only while idle: Gaming Mode or not, no game, no stream, the emulator not running, the
	// update-night window (05:00-07:00, like Steam's).
	internal static bool MayRunAutomatically(EmulatorUpdatePhase phase, bool gameRunning, bool streaming, bool emulatorRunning, DateTimeOffset now) =>
		phase == EmulatorUpdatePhase.Due && !gameRunning && !streaming && !emulatorRunning && now.Hour >= 5 && now.Hour < 7;

	// A manual "Update now" skips the soak and the no-retry rule, never the confirmation or the running check.
	internal static string? RefuseManual(EmulatorUpdatePhase phase, bool emulatorRunning) => emulatorRunning ? "not-while-running" : phase switch
	{
		EmulatorUpdatePhase.NotOffered => "not-offered",
		EmulatorUpdatePhase.NotInstalled => "not-installed",
		EmulatorUpdatePhase.Current => "current",
		EmulatorUpdatePhase.NeedsConfirmation => "needs-confirmation",
		_ => null,
	};

	// ---------- files ----------

	// Whether a program-relative path lies under one of the emulator's user-data paths.
	internal static bool IsProtected(UpdateSource source, string relative)
	{
		string first = relative.Replace('\\', '/').TrimStart('/').Split('/')[0];
		return source.Protected.Any(item => string.Equals(item, first, StringComparison.OrdinalIgnoreCase));
	}

	// Archive entries, relative to the extract folder, mapped to their place in the emulator folder: a single
	// top folder is stripped (Cemu's "Cemu_2.6/", DuckStation's builds), EmuDeck's renames applied, user data and
	// anything escaping the folder skipped.
	internal static IReadOnlyList<(string From, string To)> PlanOverlay(UpdateSource source, IReadOnlyList<string> extracted)
	{
		var files = extracted.Select(path => path.Replace('\\', '/').TrimStart('/')).Where(path => path.Length > 0).ToList();
		string? top = files.Select(path => path.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && files.All(path => path.Contains('/')) ? files[0].Split('/')[0] : null;
		var plan = new List<(string, string)>();
		foreach (string file in files)
		{
			string relative = top is null ? file : file[(top.Length + 1)..];
			if (relative.Split('/').Any(part => part is ".." or "." || part.Contains(':'))) continue;
			string name = Path.GetFileName(relative);
			if (source.Renames.TryGetValue(name, out string? renamed) && !relative.Contains('/')) relative = renamed;
			if (IsProtected(source, relative)) continue;
			plan.Add((file, relative));
		}
		return plan;
	}

	// The smoke test: the emulator's exe is there and is a PE file, and it starts and either keeps running for
	// a few seconds or exits cleanly. A crash at start is a failure.
	internal static string? SmokeFailure(bool exeExists, bool peHeader, bool started, int? exitCode, bool aliveAfter)
	{
		if (!exeExists) return "exe-missing";
		if (!peHeader) return "exe-invalid";
		if (!started) return "did-not-start";
		if (aliveAfter) return null;
		return exitCode == 0 ? null : "crashed";
	}

	internal static EmulatorUpdateEntry Succeeded(EmulatorUpdateEntry entry, string version, DateTimeOffset now) =>
		entry with { Installed = version, Pending = null, LastUpdateAt = now, LastResult = "updated", FailedVersion = null };

	internal static EmulatorUpdateEntry Failed(EmulatorUpdateEntry entry, string version, string reason, bool rolledBack) =>
		entry with { LastResult = (rolledBack ? "rolled-back:" : "failed:") + reason, FailedVersion = version };

	internal static EmulatorUpdateEntry Confirm(EmulatorUpdateEntry entry, string version) =>
		entry.Confirmed.Contains(version, StringComparer.Ordinal) ? entry : entry with { Confirmed = entry.Confirmed.Append(version).TakeLast(10).ToList() };

	// The "Emulator updates" row: information only (amber when something waits for the user or rolled back).
	internal static HealthRow Row(IReadOnlyList<(string Id, string Name, EmulatorUpdatePhase Phase, EmulatorUpdateEntry? Entry)> emulators, int soakDays, DateTimeOffset now)
	{
		var items = emulators.Where(item => item.Phase is not (EmulatorUpdatePhase.NotOffered or EmulatorUpdatePhase.NotInstalled)).Select(item => new Dictionary<string, object?>
		{
			["emulator"] = item.Id, ["name"] = item.Name, ["phase"] = item.Phase.ToString(), ["installed"] = item.Entry?.Installed,
			["pending"] = item.Entry?.Pending?.Version, ["daysLeft"] = DaysLeft(item.Entry, soakDays, now), ["verification"] = item.Entry?.Pending?.Verification.ToString(),
			["lastResult"] = item.Entry?.LastResult,
		}).ToArray();
		var data = new Dictionary<string, object?>(StringComparer.Ordinal) { ["emulators"] = items, ["soakDays"] = soakDays,
			["notOffered"] = emulators.Where(item => item.Phase == EmulatorUpdatePhase.NotOffered).Select(item => item.Name).ToArray() };
		bool needsYou = emulators.Any(item => item.Phase is EmulatorUpdatePhase.NeedsConfirmation or EmulatorUpdatePhase.WaitingForChange);
		if (needsYou) return new HealthRow("emulator-updates", HealthLevel.Info, "needs-you", data);
		if (emulators.Any(item => item.Phase is EmulatorUpdatePhase.Soaking or EmulatorUpdatePhase.Due)) return new HealthRow("emulator-updates", HealthLevel.Ok, "soaking", data);
		return new HealthRow("emulator-updates", HealthLevel.Ok, "current", data);
	}
}
