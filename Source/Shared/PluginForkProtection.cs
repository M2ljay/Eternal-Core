using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.Shared;

// Eternal Core: plugin folders that hold one of the user's own forks. The Plugin Store must never
// treat such a folder as an upstream install: no update offer, no "Update all", no install into it.
// The list is data (ProtectedPluginForks.json, embedded as a resource); the Big Picture store reads
// the same file. See docs/eternal/ARCHITECTURE-NOTES.md, section 5.
public sealed record ProtectedPluginFork(
    string Id,
    string DisplayName,
    string Repository,
    IReadOnlyList<string> RepositoryAliases,
    IReadOnlyList<string> Supersedes,
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> PluginNames,
    IReadOnlyList<string> VersionMarkers)
{
    public IEnumerable<string> Repositories => RepositoryAliases.Prepend(Repository);
}

public sealed record ProtectedForkInstall(ProtectedPluginFork Fork, string Folder, string Name, string Version);

public static class PluginForkProtection
{
    public const string ResourceName = "Playhub.ProtectedPluginForks.json";
    private const int MaxManifestBytes = 256 * 1024;
    private static readonly Regex Slug = new(@"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant);
    private static readonly Regex GithubSlug = new(
        @"github\.com[/:](?<owner>[A-Za-z0-9_.-]{1,100})/(?<repo>[A-Za-z0-9_.-]{1,100}?)(?:\.git)?(?:[/#?].*)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Lazy<IReadOnlyList<ProtectedPluginFork>> Bundled = new(LoadBundled);

    // A missing or malformed resource is a build error, never "no forks": the caller must not
    // silently fall back to offering upstream updates.
    public static IReadOnlyList<ProtectedPluginFork> Forks => Bundled.Value;

    private static IReadOnlyList<ProtectedPluginFork> LoadBundled()
    {
        using var stream = typeof(PluginForkProtection).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray());
    }

    public static IReadOnlyList<ProtectedPluginFork> Parse(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            });
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
                schema.GetInt32() != 1 || !root.TryGetProperty("forks", out var forks) || forks.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Unsupported protected fork list.");
            var result = new List<ProtectedPluginFork>();
            foreach (var item in forks.EnumerateArray())
            {
                var fork = new ProtectedPluginFork(
                    Field(item, "id"), Field(item, "displayName"), Field(item, "repository"),
                    Strings(item, "repositoryAliases"), Strings(item, "supersedes"), Strings(item, "folders"),
                    Strings(item, "pluginNames"), Strings(item, "versionMarkers"));
                if (fork.Id.Length == 0 || !fork.Repositories.Concat(fork.Supersedes).All(Slug.IsMatch) ||
                    fork.Folders.Count + fork.PluginNames.Count == 0 ||
                    fork.Folders.Any(folder => folder.IndexOfAny(Path.GetInvalidFileNameChars().Append('/').Append('\\').ToArray()) >= 0 ||
                        folder is "." or ".."))
                    throw new InvalidDataException($"Invalid protected fork entry '{fork.Id}'.");
                if (result.Any(existing => existing.Id.Equals(fork.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"Duplicate protected fork '{fork.Id}'.");
                result.Add(fork);
            }
            return result;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Malformed protected fork list.", error);
        }
    }

    // Repositories whose store entries must not be listed while the given forks are installed:
    // the upstream they replace and the fork's own repositories (the fork is shown as installed).
    public static ISet<string> ShieldedRepositories(IEnumerable<ProtectedForkInstall> installs) =>
        installs.SelectMany(install => install.Fork.Supersedes.Concat(install.Fork.Repositories))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ProtectedForkInstall> FindInstalled(string pluginsPath, IReadOnlyList<ProtectedPluginFork>? forks = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(pluginsPath) || !Directory.Exists(pluginsPath)) return Array.Empty<ProtectedForkInstall>();
            return Directory.GetDirectories(pluginsPath)
                .Select(folder => Identify(folder, forks)).OfType<ProtectedForkInstall>().ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Array.Empty<ProtectedForkInstall>();
        }
    }

    // Reads the plugin's own manifests only. Playhub's .playhub-release.json marker is ignored on
    // purpose: a fork deployed over an earlier store install can inherit the upstream marker.
    public static ProtectedForkInstall? Identify(string folder, IReadOnlyList<ProtectedPluginFork>? forks = null)
    {
        forks ??= Forks;
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            using var plugin = ReadManifest(Path.Combine(folder, "plugin.json"));
            using var package = ReadManifest(Path.Combine(folder, "package.json"));
            var name = Text(plugin, "name");
            var version = FirstNonEmpty(Text(plugin, "version"), Text(package, "version"));
            var declared = new[]
                {
                    Text(plugin, "repository"), Text(plugin, "repository_url"), Text(plugin, "homepage"),
                    Text(Child(plugin, "publish"), "repository"), Text(package, "repository"),
                    Text(Child(package, "repository"), "url"), Text(package, "homepage")
                }
                .Select(RepositorySlug).Where(slug => slug.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var fork in forks)
            {
                var byRepository = fork.Repositories.Any(declared.Contains);
                var byIdentity = (fork.Folders.Contains(folderName, StringComparer.OrdinalIgnoreCase) ||
                        name.Length > 0 && fork.PluginNames.Contains(name, StringComparer.OrdinalIgnoreCase)) &&
                    (fork.VersionMarkers.Count == 0 ||
                        fork.VersionMarkers.Any(marker => version.Contains(marker, StringComparison.OrdinalIgnoreCase)));
                if (byRepository || byIdentity)
                    return new ProtectedForkInstall(fork, folder, name.Length > 0 ? name : folderName, version);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        return null;
    }

    public static string RepositorySlug(string value)
    {
        value = value.Trim();
        if (Slug.IsMatch(value)) return value;
        var match = GithubSlug.Match(value);
        return match.Success ? $"{match.Groups["owner"].Value}/{match.Groups["repo"].Value}" : "";
    }

    private static JsonDocument? ReadManifest(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxManifestBytes) return null;
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException) { return null; }
    }

    private static JsonElement? Child(JsonDocument? document, string property) =>
        document is not null && document.RootElement.ValueKind == JsonValueKind.Object &&
        document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string Text(JsonDocument? document, string property) =>
        document is null ? "" : Text(document.RootElement, property);

    private static string Text(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var text) &&
        text.ValueKind == JsonValueKind.String ? text.GetString()?.Trim() ?? "" : "";

    private static string Field(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";

    private static IReadOnlyList<string> Strings(JsonElement item, string property) =>
        !item.TryGetProperty(property, out var value) ? Array.Empty<string>() :
        value.ValueKind != JsonValueKind.Array ? throw new InvalidDataException($"'{property}' must be an array.") :
        value.EnumerateArray().Select(entry => entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString())
                ? entry.GetString()!.Trim() : throw new InvalidDataException($"'{property}' holds an empty or non-string value."))
            .ToArray();

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(value => value.Length > 0) ?? "";
}
