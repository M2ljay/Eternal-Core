using Playhub.Shared;

namespace Playhub.Services;

internal static class PlayhubUpdatePolicy
{
#if PLAYHUB_UPDATE_PREVIEW
    internal const bool IsPreview = true;
#else
    internal const bool IsPreview = false;
#endif

    internal static string Repository(string configured) =>
        IsPreview ? "LoZazaMastro/Playhub" : ResolveConfigured(configured);

    // Eternal Core: every Playhub 3.0 install persisted upstream's repository in
    // settings.json, so that value (or none) means the fork's releases. Any other
    // repository was configured deliberately and is kept.
    private static string ResolveConfigured(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ||
        string.Equals(configured.Trim().Trim('/'), EternalRelease.UpstreamUpdateRepository, StringComparison.OrdinalIgnoreCase)
            ? EternalRelease.UpdateRepository
            : configured.Trim();

    internal static string? ReleaseTag => IsPreview ? "v1.2.1" : null;

    internal static bool ShouldOffer(PlayhubUpdateService.UpdateInfo? info) =>
        info is not null && (IsPreview
            ? info.LatestVersion == "1.2.1" && !string.IsNullOrWhiteSpace(info.DownloadUrl)
            : info.IsNewer);
}
