using System.Net;
using System.Text.RegularExpressions;
using GamingMode.Services;
using Playhub.Shared;

// Eternal Core: the Decky 4 / new Steam client transition (docs/eternal/DECKY-4.md). The pairing
// rule, the Steam probe's parsing, the Decky install guard, the compatibility file and the update gate.
static class DeckyTransitionTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		// The one rule: Decky 3.x needs webpack Steam, Decky 4.x the ES-module client.
		Check(DeckySteamPairing.RequiredClient(new Version(3, 3, 1)) == SteamUiKind.Webpack
			&& DeckySteamPairing.RequiredClient(new Version(3, 99)) == SteamUiKind.Webpack, "Decky 3.x pairs with webpack Steam");
		Check(DeckySteamPairing.RequiredClient(new Version(4, 0, 0)) == SteamUiKind.EsModules
			&& DeckySteamPairing.RequiredClient(new Version(5, 1)) == SteamUiKind.EsModules, "Decky 4.x and later pair with the ES-module client");

		// Install guard verdicts.
		Check(DeckySteamPairing.CheckInstall(new Version(3, 3, 1), SteamUiKind.Webpack).Allowed, "Decky 3 on webpack Steam: allowed");
		Check(DeckySteamPairing.CheckInstall(new Version(4, 0, 0), SteamUiKind.EsModules).Allowed, "Decky 4 on the new client: allowed");
		var fourOnWebpack = DeckySteamPairing.CheckInstall(new Version(4, 0, 0), SteamUiKind.Webpack);
		Check(!fourOnWebpack.Allowed && fourOnWebpack.Reason == DeckySteamPairing.NeedsModuleClient, "Decky 4 on today's stable Steam is refused with its reason");
		var threeOnModules = DeckySteamPairing.CheckInstall(new Version(3, 3, 1), SteamUiKind.EsModules);
		Check(!threeOnModules.Allowed && threeOnModules.Reason == DeckySteamPairing.NeedsWebpackClient, "Decky 3 on the new client is refused with its reason");
		Check(DeckySteamPairing.CheckInstall(new Version(4, 0), SteamUiKind.Unknown).Allowed
			&& DeckySteamPairing.CheckInstall(new Version(3, 3, 1), SteamUiKind.Unknown).Allowed, "Steam not checkable: today's behaviour (allowed, logged by the caller)");

		// Versions as they appear in release tags and PluginLoader product versions.
		Check(DeckySteamPairing.TryParseDeckyVersion("v3.3.1", out var a) && a == new Version(3, 3, 1), "tag v3.3.1 parses");
		Check(DeckySteamPairing.TryParseDeckyVersion("4.0.0-pre1", out var b) && b.Major == 4, "pre-release version parses to its major");
		Check(DeckySteamPairing.TryParseDeckyVersion("3.3.1+playhub.reconnect.1", out var c) && c == new Version(3, 3, 1), "build metadata is ignored");
		Check(!DeckySteamPairing.TryParseDeckyVersion("", out _) && !DeckySteamPairing.TryParseDeckyVersion(null, out _)
			&& !DeckySteamPairing.TryParseDeckyVersion("latest", out _), "no version in empty or free text");

		// The probe: typeof window.webpackChunksteamui in SharedJSContext.
		Check(DeckySteamPairing.ProbeExpression == "typeof window.webpackChunksteamui", "the probe asks for the webpack chunk array");
		Check(DeckySteamPairing.ParseProbeValue("object") == SteamUiKind.Webpack && DeckySteamPairing.ParseProbeValue("function") == SteamUiKind.Webpack
			&& DeckySteamPairing.ParseProbeValue("undefined") == SteamUiKind.EsModules && DeckySteamPairing.ParseProbeValue(null) == SteamUiKind.Unknown
			&& DeckySteamPairing.ParseProbeValue("string") == SteamUiKind.Unknown, "probe answers map to client kinds");
		const string shared = "{\"id\":\"ABC\",\"type\":\"page\",\"title\":\"SharedJSContext\",\"url\":\"https://steamloopback.host/routes/\",\"webSocketDebuggerUrl\":\"ws://127.0.0.1:8080/devtools/page/ABC\"}";
		Check(DeckySteamPairing.SelectSharedContext("[" + shared + "]", 8080)?.ToString() == "ws://127.0.0.1:8080/devtools/page/ABC", "the SharedJSContext page is found");
		Check(DeckySteamPairing.SelectSharedContext("[" + shared + "," + shared.Replace("ABC", "DEF") + "]", 8080) is null, "two SharedJSContext pages: none is trusted");
		Check(DeckySteamPairing.SelectSharedContext("[" + shared.Replace("127.0.0.1", "evil.invalid") + "]", 8080) is null
			&& DeckySteamPairing.SelectSharedContext("[" + shared.Replace("steamloopback.host", "example.com") + "]", 8080) is null
			&& DeckySteamPairing.SelectSharedContext("[" + shared.Replace("/devtools/page/ABC", "/devtools/page/XYZ") + "]", 8080) is null
			&& DeckySteamPairing.SelectSharedContext("[" + shared + "]", 9222) is null, "foreign hosts, origins, ids and ports are refused");
		Check(DeckySteamPairing.SelectSharedContext("not json", 8080) is null && DeckySteamPairing.SelectSharedContext("{}", 8080) is null
			&& DeckySteamPairing.SelectSharedContext("[]", 8080) is null, "broken or empty target lists find nothing");
		Check(DeckySteamPairing.ParseEvaluateReply("{\"id\":1,\"result\":{\"result\":{\"type\":\"string\",\"value\":\"object\"}}}") == "object", "evaluate reply value is read");
		Check(DeckySteamPairing.ParseEvaluateReply("{\"id\":2,\"result\":{\"result\":{\"type\":\"string\",\"value\":\"object\"}}}") is null
			&& DeckySteamPairing.ParseEvaluateReply("{\"id\":1,\"error\":{\"message\":\"x\"}}") is null
			&& DeckySteamPairing.ParseEvaluateReply("{\"id\":1,\"result\":{\"result\":{},\"exceptionDetails\":{}}}") is null
			&& DeckySteamPairing.ParseEvaluateReply("{\"id\":1,\"result\":{\"result\":{\"type\":\"number\",\"value\":3}}}") is null
			&& DeckySteamPairing.ParseEvaluateReply("garbage") is null, "other ids, errors, page exceptions and non-strings give no answer");

		// The probe never throws: a closed port, an HTTP error and a list without SharedJSContext are Unknown.
		var refused = await SteamClientKindProbe.DetectAsync(handler: new FakeHandler(_ => throw new HttpRequestException("refused")));
		Check(refused.Kind == SteamUiKind.Unknown && refused.Detail.Contains("not running"), "Steam not running: Unknown, with a reason for the log");
		var error = await SteamClientKindProbe.DetectAsync(handler: new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));
		Check(error.Kind == SteamUiKind.Unknown, "debug port error: Unknown");
		var empty = await SteamClientKindProbe.DetectAsync(handler: new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }));
		Check(empty.Kind == SteamUiKind.Unknown && empty.Detail.Contains("SharedJSContext"), "no SharedJSContext yet: Unknown");
		var hanging = await SteamClientKindProbe.DetectAsync(handler: new FakeHandler(_ => { Thread.Sleep(50); throw new TaskCanceledException(); }), budget: TimeSpan.FromMilliseconds(10));
		Check(hanging.Kind == SteamUiKind.Unknown, "a probe past its budget: Unknown");

		// The one rule, Judge: unknown on either side is "can't tell", never "compatible".
		Check(DeckySteamPairing.Judge(SteamUiKind.Webpack, SteamUiKind.Webpack) == PairingResult.Compatible
			&& DeckySteamPairing.Judge(SteamUiKind.EsModules, SteamUiKind.EsModules) == PairingResult.Compatible, "same kind: compatible");
		Check(DeckySteamPairing.Judge(SteamUiKind.Webpack, SteamUiKind.EsModules) == PairingResult.Incompatible
			&& DeckySteamPairing.Judge(SteamUiKind.EsModules, SteamUiKind.Webpack) == PairingResult.Incompatible, "different kinds: incompatible");
		Check(DeckySteamPairing.Judge(SteamUiKind.Unknown, SteamUiKind.Webpack) == PairingResult.CantTell
			&& DeckySteamPairing.Judge(SteamUiKind.Webpack, SteamUiKind.Unknown) == PairingResult.CantTell, "either side unknown: can't tell");

		// Decky's own plugin list (the smoke test).
		Check(DeckySteamPairing.ParseLoadedPlugins("[\"Core\",\"Awesome Mix\",3]") is { Count: 2 } names && names[1] == "Awesome Mix", "Decky's loaded plugins are read");
		Check(DeckySteamPairing.ParseLoadedPlugins("null") is null && DeckySteamPairing.ParseLoadedPlugins("garbage") is null && DeckySteamPairing.ParseLoadedPlugins(null) is null,
			"no loader, no list");
		Check(DeckySteamPairing.LoadedPluginsExpression.Contains("DeckyPluginLoader") && DeckySteamPairing.LoadedPluginsExpression.Contains("JSON.stringify"), "the plugin list comes from Decky's loader as JSON");

		// The shipped compatibility file.
		string? file = FindUp("Source/Shared/SteamCompatibility.json");
		Check(file is not null, "SteamCompatibility.json found");
		SteamCompatibility? shipped = SteamCompatibility.Parse(File.ReadAllText(file!));
		Check(shipped is not null, "the shipped compatibility file parses");
		string? bundledSource = FindUp("Source/Playhub/Services/BundledDeckyWindowsRelease.cs");
		Check(bundledSource is not null && Regex.Match(File.ReadAllText(bundledSource), "BuildVersion = \"(?<v>[^\"]+)\"").Groups["v"].Value == shipped!.BundledDecky,
			"the file's bundledDecky is the Decky Core bundles (update both together: DECKY-4.md)");
		Check(shipped!.BundledDeckyClient == SteamUiKind.Webpack, "today Core bundles a Decky for webpack Steam");
		foreach (var entry in shipped.Decky)
			Check(DeckySteamPairing.TryParseDeckyVersion(entry.Key.Contains('.') ? entry.Key : entry.Key + ".0", out var listed) && DeckySteamPairing.RequiredClient(listed) == entry.Value,
				$"the file's Decky {entry.Key} entry agrees with the built-in rule");
		Check(shipped.ClientForSteam("1763083880") == SteamUiKind.Webpack, "a 2025 client is webpack");
		Check(shipped.ClientForSteam("1795046400") == SteamUiKind.Unknown, "a client built after the cut-off: can't tell until the file says");
		Check(shipped.ClientForSteam(null) == SteamUiKind.Unknown && shipped.ClientForSteam("beta") == SteamUiKind.Unknown, "no or odd version: can't tell");

		const string sample = "{\"schema\":1,\"bundledDecky\":\"4.0.0\",\"decky\":{\"4.0.0\":\"modules\",\"3\":\"webpack\",\"x\":\"modules\",\"5\":\"other\"},"
			+ "\"steamClients\":[{\"from\":\"0\",\"through\":\"100\",\"client\":\"webpack\"},{\"from\":\"200\",\"through\":\"300\",\"client\":\"modules\"},"
			+ "{\"from\":\"250\",\"through\":\"260\",\"client\":\"webpack\"},{\"from\":\"9\",\"through\":\"1\",\"client\":\"webpack\"},{\"from\":400,\"through\":\"500\",\"client\":\"webpack\"}]}";
		SteamCompatibility? four = SteamCompatibility.Parse(sample);
		Check(four is not null && four.BundledDeckyClient == SteamUiKind.EsModules && four.Decky.Count == 2, "a Decky 4 file parses; bad entries are skipped");
		Check(four!.ClientForDecky(new Version(4, 0, 0)) == SteamUiKind.EsModules && four.ClientForDecky(new Version(3, 2, 0)) == SteamUiKind.Webpack
			&& four.ClientForDecky(new Version(5, 0, 0)) == SteamUiKind.Unknown, "Decky by exact version, then by major, else unknown");
		Check(four.ClientForSteam("50") == SteamUiKind.Webpack && four.ClientForSteam("220") == SteamUiKind.EsModules && four.ClientForSteam("150") == SteamUiKind.Unknown,
			"Steam versions by range; gaps are unknown");
		Check(four.ClientForSteam("255") == SteamUiKind.Unknown, "ranges that contradict each other: can't tell");
		Check(four.ClientForSteam("450") == SteamUiKind.Unknown, "a numeric (not string) bound is refused");
		Check(SteamCompatibility.Parse(sample)!.Fingerprint == four.Fingerprint && SteamCompatibility.Parse(sample.Replace("\"300\"", "\"301\""))!.Fingerprint != four.Fingerprint,
			"the fingerprint is stable and changes with the file's content");
		Check(SteamCompatibility.Parse("{\"schema\":2,\"bundledDecky\":\"3.3.1\"}") is null && SteamCompatibility.Parse("{\"schema\":1}") is null
			&& SteamCompatibility.Parse("garbage") is null && SteamCompatibility.Parse(null) is null, "unknown schema, no bundled Decky or broken JSON: no file");

		// The install guard can use the file, with the same rule.
		Check(!DeckySteamPairing.CheckInstall(new Version(4, 0, 0), SteamUiKind.Webpack, shipped).Allowed
			&& DeckySteamPairing.CheckInstall(new Version(3, 3, 1), SteamUiKind.Webpack, shipped).Allowed, "install guard with the shipped file");

		// The release gate (SteamUpdateHold.cs).
		var three = new Version(3, 3, 1);
		Check(SteamReleaseGate.Evaluate(shipped, true, three, "1763083880", SteamUiKind.Webpack, "1790000000") == new SteamGateVerdict(SteamGateResult.Pass, "ok"),
			"Decky 3, a webpack Steam pending: pass");
		Check(SteamReleaseGate.Evaluate(shipped, true, three, "1763083880", SteamUiKind.Webpack, "1795046400") == new SteamGateVerdict(SteamGateResult.CantTell, "pending-unknown"),
			"a pending client the file does not know: can't tell");
		Check(SteamReleaseGate.Evaluate(shipped, true, three, "1763083880", SteamUiKind.Webpack, null).Reason == "pending-unknown", "no pending version known: can't tell");
		Check(SteamReleaseGate.Evaluate(four, true, three, "50", SteamUiKind.Webpack, "220") == new SteamGateVerdict(SteamGateResult.Incompatible, "pending-unsupported"),
			"Decky 3 installed, an ES-module Steam pending: known incompatible");
		Check(SteamReleaseGate.Evaluate(four, true, new Version(4, 0, 0), "220", SteamUiKind.EsModules, "230").Result == SteamGateResult.Pass, "Decky 4 on the new client: pass");
		Check(SteamReleaseGate.Evaluate(four, true, null, "220", SteamUiKind.Unknown, "230").Result == SteamGateResult.Pass, "Decky version unreadable: the bundled Decky is assumed");
		Check(SteamReleaseGate.Evaluate(null, true, three, "1", SteamUiKind.Webpack, "2").Reason == "no-compatibility-file", "no compatibility file: can't tell");
		Check(SteamReleaseGate.Evaluate(four, true, new Version(5, 0, 0), "50", SteamUiKind.Webpack, "60").Reason == "decky-unknown", "a Decky the file does not know: can't tell");
		Check(SteamReleaseGate.Evaluate(shipped, true, three, "1763083880", SteamUiKind.EsModules, "1790000000").Reason == "file-disagrees",
			"the running client is not what the file says: the file is not trusted");
		Check(SteamReleaseGate.Evaluate(null, false, null, null, SteamUiKind.Unknown, null).Result == SteamGateResult.Pass, "no Decky installed: nothing to break");
		Check(SteamReleaseGate.Fingerprint(shipped, three, "200") != SteamReleaseGate.Fingerprint(shipped, three, "300")
			&& SteamReleaseGate.Fingerprint(shipped, three, "200") != SteamReleaseGate.Fingerprint(shipped, new Version(3, 3, 2), "200")
			&& SteamReleaseGate.Fingerprint(shipped, three, "200") != SteamReleaseGate.Fingerprint(four, three, "200"),
			"the retry fingerprint changes with the pending version, the installed Decky and the compatibility file");

		// The agent embeds the file, and Core Health shows a postponement.
		string? agentProject = FindUp("Source/GamingModeAgent/GamingMode.csproj");
		Check(agentProject is not null && File.ReadAllText(agentProject).Contains("SteamCompatibility.json"), "the agent embeds SteamCompatibility.json");
	}

	static string? FindUp(string relative)
	{
		for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
		{
			string candidate = Path.Combine(dir.FullName, relative);
			if (File.Exists(candidate)) return candidate;
		}
		return null;
	}
}

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(answer(request));
}
