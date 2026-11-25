using Playhub.Models;
using Playhub.Services;
using Playhub.Shared;
using System.Text.Json;

// Eternal Core: the user's own plugin forks must never be offered, or overwritten by, an upstream
// store "update". Fixtures copy the real forks' manifests (plugin.json / package.json shapes).
internal static class ForkProtectionTests
{
    private const string Collector = "M2ljay/Eternal-Collector";
    private const string Forge = "M2ljay/Eternal-Forge";
    private const string Mix = "M2ljay/Eternal-Mix";
    private const string Assemble = "M2ljay/Eternal-Assemble";

    internal static async Task RunAll(RemotePluginCatalog catalog, Func<string, Func<Task>, Task> run)
    {
        PluginCatalogService.ProtectedForkOverride = null;
        var rommDock = Entry(catalog, "Crimson3076/romm-dock");
        var nexus = Entry(catalog, "RedRanger14/decky-nexus");
        var discord = Entry(catalog, "LoZazaMastro/Playhub-Discord");
        var nowPlaying = Entry(catalog, "LoZazaMastro/Now-Playing");
        var tender = Entry(catalog, "danielcopper/romm-tender");

        await run("Fork list: bundled JSON parses and names the four forks", () =>
        {
            var forks = PluginForkProtection.Forks;
            Check(forks.Select(f => f.Repository).OrderBy(r => r, StringComparer.Ordinal)
                    .SequenceEqual(new[] { Assemble, Collector, Forge, Mix }.OrderBy(r => r, StringComparer.Ordinal)),
                "Unexpected protected fork list: " + string.Join(", ", forks.Select(f => f.Repository)));
            Check(forks.Single(f => f.Repository == Collector).Supersedes.Contains("Crimson3076/romm-dock") &&
                forks.Single(f => f.Repository == Forge).Supersedes.Contains("RedRanger14/decky-nexus") &&
                forks.Single(f => f.Repository == Assemble).Supersedes.Contains("LoZazaMastro/Playhub-Discord"),
                "A fork lost the upstream repository it replaces.");
            return Task.CompletedTask;
        });

        await run("Fork list: every superseded repository and folder exists in the shipped catalogue", () =>
        {
            foreach (var fork in PluginForkProtection.Forks)
            foreach (var upstream in fork.Supersedes)
            {
                var entry = catalog.Plugins.SingleOrDefault(p => p.Repository.Equals(upstream, StringComparison.OrdinalIgnoreCase));
                Check(entry is not null, $"{fork.Id}: {upstream} is not in catalog/plugins.json (stale protection entry).");
            }
            return Task.CompletedTask;
        });

        await run("Fork list: malformed documents are rejected", () =>
        {
            foreach (var bad in new[]
            {
                "{\"schemaVersion\":2,\"forks\":[]}",
                "{\"schemaVersion\":1,\"forks\":[{\"id\":\"x\",\"repository\":\"not a slug\",\"folders\":[\"x\"]}]}",
                "{\"schemaVersion\":1,\"forks\":[{\"id\":\"x\",\"repository\":\"a/b\"}]}",
                "{\"schemaVersion\":1,\"forks\":[{\"id\":\"x\",\"repository\":\"a/b\",\"folders\":[\"x\"]},{\"id\":\"x\",\"repository\":\"a/c\",\"folders\":[\"y\"]}]}"
            })
            {
                var rejected = false;
                try { PluginForkProtection.Parse(System.Text.Encoding.UTF8.GetBytes(bad)); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Accepted malformed fork list: " + bad);
            }
            return Task.CompletedTask;
        });

        await run("The Collector (romm-dock 0.33.0-win.N) is never offered Crimson's 0.33.0 or 0.34.0", async () =>
        {
            foreach (var version in new[] { rommDock.Version, "0.34.0" })
            {
                using var files = new ForkFixture();
                files.Plugin("romm-dock", new { name = "RomM-Dock", version = "0.33.0-win.17", author = "Crimson3076 (fork of Tender by danielcopper)",
                    publish = new { image = "https://raw.githubusercontent.com/Crimson3076/romm-dock/main/assets/store_image.png" } },
                    new { name = "romm-dock", version = "0.33.0-win.17" });
                var loaded = await files.Load(catalog, rommDock with { Version = version }, tender);
                ExpectProtected(loaded, "romm-dock", Collector, "0.33.0-win.17");
                Check(!loaded.Any(p => p.RepositorySlug == rommDock.Repository),
                    "The upstream RomM-Dock entry stayed installable over the fork's folder.");
                Check(loaded.Any(p => p.RepositorySlug == tender.Repository && !p.IsInstalled),
                    "An unrelated catalogue entry was hidden.");
            }
        });

        await run("A stale Playhub marker from an earlier upstream install cannot unprotect The Collector", async () =>
        {
            using var files = new ForkFixture();
            var folder = files.Plugin("romm-dock", new { name = "RomM-Dock", version = "0.33.0-win.17" },
                new { name = "romm-dock", version = "0.33.0-win.17" });
            File.WriteAllText(Path.Combine(folder, ".playhub-release.json"), JsonSerializer.Serialize(new
            {
                repositorySlug = rommDock.Repository, version = "0.33.0",
                releaseZipUrl = rommDock.CatalogReleaseUrl
            }));
            var loaded = await files.Load(catalog, rommDock with { Version = "0.34.0" });
            var fork = ExpectProtected(loaded, "romm-dock", Collector, "0.33.0-win.17");
            Check(string.IsNullOrEmpty(fork.CatalogReleaseZipUrl) && string.IsNullOrEmpty(fork.ReleaseZipUrl),
                "The fork kept the upstream release ZIP from the stale marker.");
        });

        await run("Forge (Nexus Mods 1.21.32-anygame.N) is never offered decky-nexus 1.21.40", async () =>
        {
            using var files = new ForkFixture();
            files.Plugin("Nexus Mods", new { name = "Nexus Mods", author = "Michael Finney" },
                new { name = "decky-nexus", version = "1.21.32-anygame.57" });
            var loaded = await files.Load(catalog, nexus);
            ExpectProtected(loaded, "Nexus Mods", Forge, "1.21.32-anygame.57");
            Check(!loaded.Any(p => p.RepositorySlug == nexus.Repository), "Decky Nexus stayed installable beside Forge.");
        });

        await run("Eternal-Assemble (Discord, upstream manifests) is never offered Playhub-Discord 1.0.4", async () =>
        {
            using var files = new ForkFixture();
            files.Plugin("Discord", new { name = "Discord", author = "LoZazaMastro, based on Steamcord by Necrosiak" },
                new { name = "playhub-discord", version = "1.0.3",
                    repository = new { type = "git", url = "git+https://github.com/LoZazaMastro/Playhub-Discord.git" },
                    homepage = "https://github.com/LoZazaMastro/Playhub-Discord" });
            var loaded = await files.Load(catalog, discord with { Version = "1.0.4" });
            ExpectProtected(loaded, "Discord", Assemble, "1.0.3");
            Check(!loaded.Any(p => p.RepositorySlug == discord.Repository), "Playhub-Discord stayed installable over the fork.");
        });

        await run("Awesome Mix and a leftover Now Playing Plus are protected; upstream Now Playing stays listed", async () =>
        {
            using var files = new ForkFixture();
            files.Plugin("Awesome Mix", new { name = "Awesome Mix", author = "M2ljay (based on Now Playing by LoZazaMastro)" },
                new { name = "eternal-mix", version = "1.0.0", repository = new { type = "git", url = "https://github.com/M2ljay/Eternal-Mix" } });
            files.Plugin("Now Playing Plus", new { name = "Now Playing Plus", author = "LoZazaMastro" },
                new { name = "now-playing-plus", version = "2.5.2" });
            var loaded = await files.Load(catalog, nowPlaying with { Version = "2.6.0" });
            ExpectProtected(loaded, "Awesome Mix", Mix, "1.0.0");
            ExpectProtected(loaded, "Now Playing Plus", Mix, "2.5.2");
            var upstream = loaded.Single(p => p.RepositorySlug == nowPlaying.Repository);
            Check(!upstream.IsInstalled && !upstream.HasUpdate, "Upstream Now Playing claimed a fork.");
        });

        await run("Upstream RomM-Dock in romm-dock (no -win. version) still gets its updates", async () =>
        {
            using var files = new ForkFixture();
            files.Plugin("romm-dock", new { name = "RomM-Dock", version = "0.32.0" }, new { name = "romm-dock", version = "0.32.0" });
            var loaded = await files.Load(catalog, rommDock);
            var actual = loaded.Single(p => p.IsInstalled);
            Check(actual.RepositorySlug == rommDock.Repository && actual.HasUpdate && !actual.IsProtectedFork,
                "A genuine upstream RomM-Dock install was treated as a fork.");
        });

        await run("Upstream decky-nexus in its own folder still gets its updates", async () =>
        {
            using var files = new ForkFixture();
            files.Plugin("decky-nexus", new { name = "Nexus Mods" }, new { name = "decky-nexus", version = "1.21.30" });
            var loaded = await files.Load(catalog, nexus);
            var actual = loaded.Single(p => p.IsInstalled);
            Check(actual.RepositorySlug == nexus.Repository && actual.HasUpdate && !actual.IsProtectedFork,
                "A genuine upstream decky-nexus install was treated as a fork.");
        });

        await run("Fork identity by declared repository works in any folder", () =>
        {
            using var files = new ForkFixture();
            var folder = files.Plugin("The Collector", new { name = "The Collector", version = "1.0.0" },
                new { name = "the-collector", version = "1.0.0", repository = "https://github.com/M2ljay/Eternal-Collector" });
            Check(PluginForkProtection.Identify(folder)?.Fork.Repository == Collector, "Declared fork repository was not recognised.");
            var other = files.Plugin("Weather", new { name = "Weather", version = "2.1.1" }, new { name = "weather", version = "2.1.1" });
            Check(PluginForkProtection.Identify(other) is null, "An unrelated plugin was treated as a fork.");
            Check(PluginForkProtection.Identify(Path.Combine(files.Installed, "missing")) is null, "A missing folder was treated as a fork.");
            return Task.CompletedTask;
        });
    }

    private static DeckyPluginInfo ExpectProtected(IReadOnlyList<DeckyPluginInfo> loaded, string folder, string repository, string version)
    {
        var matches = loaded.Where(p => p.IsInstalled && Path.GetFileName(p.InstalledFolder) == folder).ToArray();
        Check(matches.Length == 1, $"{folder}: expected one installed entry, found {matches.Length}.");
        var fork = matches[0];
        Check(!loaded.Any(p => p.IsInstalled && p.HasUpdate),
            "An update is offered: " + string.Join(", ", loaded.Where(p => p.HasUpdate).Select(p => $"{p.Name} {p.InstalledVersion} -> {p.Version}")));
        Check(fork.IsProtectedFork && fork.RepositorySlug == repository && fork.InstalledVersion == version && fork.Version == version,
            $"{folder}: got {fork.RepositorySlug} [{fork.CatalogSource}] {fork.InstalledVersion}/{fork.Version}, protected={fork.IsProtectedFork}.");
        return fork;
    }

    private static RemotePluginCatalogEntry Entry(RemotePluginCatalog catalog, string repository) =>
        catalog.Plugins.Single(p => p.Repository == repository);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ForkFixture : IDisposable
    {
        private static readonly string Parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Playhub.ForkProtection.Tests"));
        private readonly string root = Path.Combine(Parent, Guid.NewGuid().ToString("N"));
        internal string Bundled => Path.Combine(root, "bundled");
        internal string Installed => Path.Combine(root, "installed");

        internal ForkFixture()
        {
            Directory.CreateDirectory(Bundled);
            Directory.CreateDirectory(Installed);
        }

        internal string Plugin(string folder, object plugin, object package)
        {
            var path = Path.Combine(Installed, folder);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "plugin.json"), JsonSerializer.Serialize(plugin));
            File.WriteAllText(Path.Combine(path, "package.json"), JsonSerializer.Serialize(package));
            return path;
        }

        internal Task<IReadOnlyList<DeckyPluginInfo>> Load(RemotePluginCatalog catalog, params RemotePluginCatalogEntry[] entries) =>
            new PluginCatalogService().LoadAsync(Bundled, Installed, catalog with { Plugins = entries });

        public void Dispose()
        {
            if (!Path.GetFullPath(root).StartsWith(Parent + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe fixture cleanup path.");
            Directory.Delete(root, recursive: true);
        }
    }
}
