using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.Shared;

// Eternal Core: which Decky Loader generation can run on which Steam client.
//
// Stable Steam (2026) builds its UI with webpack; Decky 3.x finds React through the
// window.webpackChunksteamui chunk array. The Steam beta of 18-21 November 2026 loads its UI as ES
// modules instead (no chunk array, renamed CSS classes): Decky 3.x does not load there, and Decky
// 4.0 (pre-release only so far) does not load on the webpack client. Core must never pair the two
// wrongly: the Decky installer refuses a mismatch (DeckyWindowsReleaseClient), and the agent's Steam
// update gate (SteamUpdateHold.cs) lets Steam update only to a client the installed Decky supports.
//
// One rule, one place: Judge. Both the install guard and the update gate call it; everything else
// here says which client kind a Decky version or a Steam client version is (the shipped file
// SteamCompatibility.json, with RequiredClient as the built-in fallback for Decky), and parses what
// the CEF probe (SteamClientKindProbe.cs) reads. See docs/eternal/DECKY-4.md and STEAM-UPDATES.md.
public enum SteamUiKind { Unknown, Webpack, EsModules }

public enum PairingResult { Compatible, Incompatible, CantTell }

public readonly record struct DeckyPairingVerdict(bool Allowed, string? Reason);

public static class DeckySteamPairing
{
    // The first Decky major that runs on the ES-module client (and no longer on webpack Steam).
    public const int FirstModuleClientMajor = 4;

    // Evaluated in Steam's SharedJSContext: "object" on the webpack client, "undefined" on the new one.
    public const string ProbeExpression = "typeof window.webpackChunksteamui";

    // User-facing reasons (Italian source strings; the app translates them through its 12 locale files).
    public const string NeedsModuleClient =
        "Questa versione di Decky richiede il nuovo client di Steam, che su questo PC non c'è ancora. Decky resta alla versione installata.";
    public const string NeedsWebpackClient =
        "Questo Steam usa il nuovo client, che Decky 3 non sa caricare. Serve Decky 4: Decky resta alla versione installata.";

    // THE compatibility rule: the client kind a Decky supports against the kind a Steam client is.
    // Either side unknown means Core cannot tell; it never guesses "compatible".
    public static PairingResult Judge(SteamUiKind deckySupports, SteamUiKind steam)
    {
        if (deckySupports == SteamUiKind.Unknown || steam == SteamUiKind.Unknown) return PairingResult.CantTell;
        return deckySupports == steam ? PairingResult.Compatible : PairingResult.Incompatible;
    }

    // Built-in knowledge of Decky generations, for versions the shipped file does not list.
    public static SteamUiKind RequiredClient(Version decky) =>
        decky.Major >= FirstModuleClientMajor ? SteamUiKind.EsModules : SteamUiKind.Webpack;

    // The Decky install guard. Can't tell (Steam not running, debug port closed): allowed, as before,
    // and the caller logs that it could not check.
    public static DeckyPairingVerdict CheckInstall(Version decky, SteamUiKind steam, SteamCompatibility? compatibility = null)
    {
        SteamUiKind needed = compatibility?.ClientForDecky(decky) ?? RequiredClient(decky);
        return Judge(needed, steam) switch
        {
            PairingResult.Incompatible => new(false, needed == SteamUiKind.EsModules ? NeedsModuleClient : NeedsWebpackClient),
            _ => new(true, null),
        };
    }

    private static readonly Regex VersionText = new(@"\A\s*v?(?<v>[0-9]{1,6}\.[0-9]{1,6}(?:\.[0-9]{1,6}){0,2})(?:[-+].*)?\s*\z",
        RegexOptions.CultureInvariant);

    // "v3.3.1", "3.3.1", "4.0.0-pre1", "3.3.1+playhub.reconnect.1" (release tags and PE product versions).
    public static bool TryParseDeckyVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        Match match = VersionText.Match(text ?? "");
        return match.Success && Version.TryParse(match.Groups["v"].Value, out version!);
    }

    public static SteamUiKind ParseProbeValue(string? typeOfChunkArray) => typeOfChunkArray switch
    {
        "object" or "function" => SteamUiKind.Webpack,
        "undefined" => SteamUiKind.EsModules,
        _ => SteamUiKind.Unknown,
    };

    // From http://127.0.0.1:<port>/json: the one SharedJSContext page Steam serves, as a local
    // DevTools WebSocket on that port. Anything else (none, several, foreign hosts) is null.
    public static Uri? SelectSharedContext(string? targetsJson, int port)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(targetsJson ?? "");
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            Uri? found = null;
            foreach (JsonElement target in document.RootElement.EnumerateArray())
            {
                if (target.ValueKind != JsonValueKind.Object || Text(target, "type") != "page" || Text(target, "title") != "SharedJSContext") continue;
                if (!Uri.TryCreate(Text(target, "url"), UriKind.Absolute, out Uri? origin) || origin.Scheme != "https" || origin.Host != "steamloopback.host") continue;
                string id = Text(target, "id") ?? "";
                if (!Uri.TryCreate(Text(target, "webSocketDebuggerUrl"), UriKind.Absolute, out Uri? socket) || socket.Scheme != "ws"
                    || socket.Host != "127.0.0.1" || socket.Port != port || socket.UserInfo.Length != 0 || socket.Query.Length != 0
                    || id.Length == 0 || socket.AbsolutePath != "/devtools/page/" + id) continue;
                if (found is not null) return null;
                found = socket;
            }
            return found;
        }
        catch (JsonException) { return null; }
    }

    // The reply to {"id":1,"method":"Runtime.evaluate"}: the string value, or null (other id,
    // protocol error, exception in the page, non-string result).
    public static string? ParseEvaluateReply(string? replyJson, int id = 1)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(replyJson ?? "");
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out JsonElement replyId)
                || replyId.ValueKind != JsonValueKind.Number || !replyId.TryGetInt32(out int value) || value != id) return null;
            if (root.TryGetProperty("error", out _) || !root.TryGetProperty("result", out JsonElement result) || result.ValueKind != JsonValueKind.Object
                || result.TryGetProperty("exceptionDetails", out _) || !result.TryGetProperty("result", out JsonElement inner)
                || inner.ValueKind != JsonValueKind.Object || !inner.TryGetProperty("value", out JsonElement text) || text.ValueKind != JsonValueKind.String) return null;
            return text.GetString();
        }
        catch (JsonException) { return null; }
    }

    // Decky's own list of the plugins it loaded (window.DeckyPluginLoader.plugins in SharedJSContext),
    // read by the Steam update smoke test. A string, as SteamClientKindProbe.EvaluateAsync requires.
    public const string LoadedPluginsExpression =
        "(function(){try{var l=window.DeckyPluginLoader;var p=l&&l.plugins;if(!p||typeof p.length!=='number')return 'null';"
        + "return JSON.stringify(Array.prototype.map.call(p,function(x){return x&&x.name;}).filter(function(n){return typeof n==='string';}));}catch(e){return 'null';}})()";

    // ["Core", "Awesome Mix"] from the expression above; null when Decky's loader is not there.
    public static IReadOnlyList<string>? ParseLoadedPlugins(string? json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json ?? "");
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var names = new List<string>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 and <= 200 } name) names.Add(name);
            return names;
        }
        catch (JsonException) { return null; }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

// The shipped known-good file (Source/Shared/SteamCompatibility.json, embedded in the agent): which
// client kind each Decky version supports, which Steam client versions are which kind, and the
// Decky version Core bundles. Parsed here so it can be tested on Linux.
public sealed record SteamClientRange(ulong From, ulong Through, SteamUiKind Client);

public sealed record SteamCompatibility(string BundledDecky, IReadOnlyDictionary<string, SteamUiKind> Decky,
    IReadOnlyList<SteamClientRange> SteamClients, string Fingerprint)
{
    public const int Schema = 1;

    // An exact version entry ("3.3.1") first, then a major entry ("4"); otherwise Unknown.
    public SteamUiKind ClientForDecky(Version decky)
    {
        string full = decky.Build >= 0 ? $"{decky.Major}.{decky.Minor}.{decky.Build}" : $"{decky.Major}.{decky.Minor}";
        if (Decky.TryGetValue(full, out SteamUiKind exact)) return exact;
        return Decky.TryGetValue(decky.Major.ToString(CultureInfo.InvariantCulture), out SteamUiKind major) ? major : SteamUiKind.Unknown;
    }

    public SteamUiKind BundledDeckyClient =>
        DeckySteamPairing.TryParseDeckyVersion(BundledDecky, out Version version) ? ClientForDecky(version) : SteamUiKind.Unknown;

    // Steam client versions are the numbers in package\steam_client_win64.manifest ("version" "1763083880").
    // A version no range covers (or not a number) is Unknown: Core cannot tell what it is.
    public SteamUiKind ClientForSteam(string? version)
    {
        if (!ulong.TryParse(version, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)) return SteamUiKind.Unknown;
        SteamUiKind found = SteamUiKind.Unknown;
        foreach (SteamClientRange range in SteamClients)
        {
            if (value < range.From || value > range.Through) continue;
            // Two ranges that disagree about one version: the file contradicts itself, so Core cannot tell.
            if (found != SteamUiKind.Unknown && found != range.Client) return SteamUiKind.Unknown;
            found = range.Client;
        }
        return found;
    }

    public static SteamCompatibility? Parse(string? json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json ?? "");
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schema", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int number) || number != Schema) return null;
            string bundled = root.TryGetProperty("bundledDecky", out JsonElement b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            if (!DeckySteamPairing.TryParseDeckyVersion(bundled, out _)) return null;
            var decky = new Dictionary<string, SteamUiKind>(StringComparer.Ordinal);
            if (root.TryGetProperty("decky", out JsonElement entries) && entries.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty entry in entries.EnumerateObject())
                    if (Regex.IsMatch(entry.Name, @"\A[0-9]{1,6}(?:\.[0-9]{1,6}){0,2}\z") && entry.Value.ValueKind == JsonValueKind.String
                        && Kind(entry.Value.GetString()) is { } kind)
                        decky[entry.Name] = kind;
            var ranges = new List<SteamClientRange>();
            if (root.TryGetProperty("steamClients", out JsonElement clients) && clients.ValueKind == JsonValueKind.Array)
                foreach (JsonElement range in clients.EnumerateArray())
                {
                    if (range.ValueKind != JsonValueKind.Object) continue;
                    if (Number(range, "from") is not ulong from || Number(range, "through") is not ulong through || through < from) continue;
                    if (!range.TryGetProperty("client", out JsonElement client) || client.ValueKind != JsonValueKind.String || Kind(client.GetString()) is not { } kind) continue;
                    ranges.Add(new(from, through, kind));
                }
            string canonical = bundled + "|" + string.Join(",", decky.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value))
                + "|" + string.Join(",", ranges.Select(range => $"{range.From}-{range.Through}={range.Client}"));
            string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))[..16];
            return new(bundled, decky, ranges, fingerprint);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    // "from"/"through" as strings of digits, so JSON readers never round a 10-digit version.
    private static ulong? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
        && ulong.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed) ? parsed : null;

    private static SteamUiKind? Kind(string? text) => text switch
    {
        "webpack" => SteamUiKind.Webpack,
        "modules" => SteamUiKind.EsModules,
        _ => null,
    };
}
