using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using EternalTests;
using Playhub.Services;
using Playhub.Shared;

var failures = 0;
async Task Test(string name, Func<Task> body)
{
    try { await body(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
}
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }

var installer = new byte[70000];
installer[0] = (byte)'M'; installer[1] = (byte)'Z';
var digest = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
const string Fork = "M2ljay/Eternal-Core";

string Release(string tag, string assetName, string? assetDigest) => JsonSerializer.Serialize(new
{
    tag_name = tag,
    html_url = $"https://github.com/{Fork}/releases/tag/{tag}",
    body = "notes",
    assets = new[]
    {
        new { name = assetName, browser_download_url = $"https://github.com/{Fork}/releases/download/{tag}/{assetName}", size = (long)installer.Length, digest = assetDigest }
    }
});

var requested = new List<Uri>();
void Serve(string tag, string assetName = "Playhub-Setup.exe", string? assetDigest = null)
{
    assetDigest ??= "sha256:" + digest;
    State.Reply = request =>
    {
        requested.Add(request.RequestUri!);
        var path = request.RequestUri!.AbsolutePath;
        if (request.RequestUri.Host == "api.github.com" && path == $"/repos/{Fork}/releases/latest")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release(tag, assetName, assetDigest)) });
        if (request.RequestUri.Host == "github.com" && path == $"/{Fork}/releases/download/{tag}/{assetName}")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(installer) });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    };
}

try
{
    await Test("one version source: constants, display name and compiled assembly version agree", () =>
    {
        var version = Version.Parse(EternalRelease.Version);
        Require(version.Major == 3 && version.Minor >= 1, "fork releases must sort above upstream 3.0.0");
        Require(EternalRelease.AssemblyVersion == EternalRelease.Version + ".0", "assembly version drifted");
        Require(EternalRelease.DisplayVersion == EternalRelease.MarkName + " (" + EternalRelease.Version + ")", "display version drifted");
        Require(EternalRelease.MarkName.StartsWith("Mark ", StringComparison.Ordinal), "Mark name expected");
        Require(typeof(EternalRelease).Assembly.GetName().Version == Version.Parse(EternalRelease.AssemblyVersion),
            "EternalRelease.props did not apply the constant to <Version>");
        Require(EternalRelease.UpdateRepository == Fork, "update repository is not the fork");
        return Task.CompletedTask;
    });

    await Test("rebrand: the user sees Core by Eternal; upgrade identifiers keep Playhub", () =>
    {
        Require(EternalRelease.DisplayName == "Core", "display name");
        Require(EternalRelease.MakerName == "Eternal", "maker name");
        Require(EternalRelease.UpstreamUpdateRepository == "LoZazaMastro/Playhub", "legacy update repository must stay mapped");
        var props = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "EternalRelease.props"));
        Require(props.Contains("const string DisplayName = \"([^\"]+)\";"), "EternalRelease.props no longer reads DisplayName for the file metadata");
        return Task.CompletedTask;
    });

    await Test("persisted upstream or empty repository resolves to the fork; custom ones are kept", () =>
    {
        Require(!PlayhubUpdatePolicy.IsPreview, "test expects the public build");
        foreach (var legacy in new[] { "LoZazaMastro/Playhub", "lozazamastro/playhub", " LoZazaMastro/Playhub/ ", "", "  " })
            Require(PlayhubUpdatePolicy.Repository(legacy) == Fork, $"'{legacy}' did not resolve to the fork");
        Require(PlayhubUpdatePolicy.Repository("someone/else") == "someone/else", "custom repository overridden");
        Require(PlayhubUpdatePolicy.Repository("LoZazaMastro/Playhub-Artworks") == "LoZazaMastro/Playhub-Artworks", "prefix match is too loose");
        return Task.CompletedTask;
    });

    await Test("an installed upstream 3.0.0 sees the fork release as newer, from the fork's API", async () =>
    {
        Serve("v" + EternalRelease.Version);
        requested.Clear();
        var info = await new PlayhubUpdateService().CheckAsync(PlayhubUpdatePolicy.Repository("LoZazaMastro/Playhub"), "3.0.0");
        Require(requested.Count == 1 && requested[0].AbsoluteUri == $"https://api.github.com/repos/{Fork}/releases/latest", "wrong endpoint");
        Require(info is { IsNewer: true, AssetName: "Playhub-Setup.exe" } && info.LatestVersion == EternalRelease.Version, "fork release not offered");
        Require(PlayhubUpdatePolicy.ShouldOffer(info), "policy refused a newer release");
    });

    await Test("the running Mark is not re-offered; the next Mark is", async () =>
    {
        Serve("v" + EternalRelease.Version);
        Require((await new PlayhubUpdateService().CheckAsync(Fork, EternalRelease.Version))?.IsNewer == false, "same version offered again");
        var next = new Version(EternalRelease.Version);
        var nextTag = $"v{next.Major}.{next.Minor + 1}.0";
        Serve(nextTag);
        Require((await new PlayhubUpdateService().CheckAsync(Fork, EternalRelease.Version))?.IsNewer == true, "next Mark not offered");
    });

    await Test("a non-numeric tag is never offered", async () =>
    {
        Serve("mark-ii");
        Require((await new PlayhubUpdateService().CheckAsync(Fork, "3.0.0"))?.IsNewer != true, "non-numeric tag offered");
    });

    await Test("download keeps SHA-256 verification against the release digest", async () =>
    {
        Serve("v" + EternalRelease.Version);
        var service = new PlayhubUpdateService();
        var info = await service.CheckAsync(Fork, "3.0.0");
        var path = await service.DownloadInstallerAsync(info!);
        Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(installer), "downloaded bytes differ");

        Serve("v" + EternalRelease.Version, assetDigest: "sha256:" + new string('0', 64));
        var tampered = await service.CheckAsync(Fork, "3.0.0");
        try { await service.DownloadInstallerAsync(tampered!); throw new Exception("digest mismatch accepted"); }
        catch (InvalidDataException) { }
    });
}
finally
{
    try { Directory.Delete(State.Root, true); } catch { }
}

Console.WriteLine(failures == 0 ? "ALL PASS" : failures + " FAILED");
return failures == 0 ? 0 : 1;
