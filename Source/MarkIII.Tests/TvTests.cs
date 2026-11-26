using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamingMode.Models;
using GamingMode.Services;

// Eternal Core Mark III tests: TV protocol, TV controller against a fake TV.
static class TvTests
{
	internal static async Task Run(Action<bool, string> Check)
	{
		await Task.CompletedTask;
		// TV protocol
		{
			Check(LgWebOsProtocol.TryParseMac("a8:23:fe:01:02:03", out var mac) && LgWebOsProtocol.FormatMac(mac) == "A8:23:FE:01:02:03", "MAC with colons parses and formats");
			Check(LgWebOsProtocol.TryParseMac("A8-23-FE-01-02-03", out _) && LgWebOsProtocol.TryParseMac("a823.fe01.0203", out _) && LgWebOsProtocol.TryParseMac("A823FE010203", out _), "MAC dash, dot and bare forms parse");
			Check(!LgWebOsProtocol.TryParseMac("", out _) && !LgWebOsProtocol.TryParseMac("A8:23:FE:01:02", out _) && !LgWebOsProtocol.TryParseMac("zz:23:fe:01:02:03", out _)
				&& !LgWebOsProtocol.TryParseMac("00:00:00:00:00:00", out _) && !LgWebOsProtocol.TryParseMac("ff:ff:ff:ff:ff:ff", out _), "invalid, zero and broadcast MACs are refused");
			byte[] packet = LgWebOsProtocol.MagicPacket(mac);
			bool body = true;
			for (int i = 0; i < 16; i++) body &= packet.Skip(6 + i * 6).Take(6).SequenceEqual(mac);
			Check(packet.Length == 102 && packet.Take(6).All(b => b == 0xFF) && body, "Wake-on-LAN packet: 6 x FF then the MAC 16 times");

			Check(LgWebOsProtocol.NormalizeInput("HDMI 2") == "HDMI_2" && LgWebOsProtocol.NormalizeInput("hdmi2") == "HDMI_2"
				&& LgWebOsProtocol.NormalizeInput("HDMI_2") == "HDMI_2" && LgWebOsProtocol.NormalizeInput("2") == "HDMI_2" && LgWebOsProtocol.NormalizeInput("HDMI-4") == "HDMI_4", "HDMI input names normalise");
			Check(LgWebOsProtocol.NormalizeInput("HDMI 5") is null && LgWebOsProtocol.NormalizeInput("AV1") is null && LgWebOsProtocol.NormalizeInput(null) is null, "unknown inputs are refused");
			Check(LgWebOsProtocol.InputAppId("HDMI_2") == "com.webos.app.hdmi2", "HDMI 2 is shown by com.webos.app.hdmi2");

			Check(LgWebOsProtocol.ShouldTurnOff("com.webos.app.hdmi2", "HDMI_2") && LgWebOsProtocol.ShouldTurnOff("COM.WEBOS.APP.HDMI2", "hdmi 2"), "off: TV showing Core's HDMI 2");
			foreach (string? other in new[] { "netflix", "com.webos.app.hdmi1", "com.webos.app.livetv", "youtube.leanback.v4", "", null, "com.webos.app.hdmi22" })
				Check(!LgWebOsProtocol.ShouldTurnOff(other, "HDMI_2"), $"left on: TV showing \"{other ?? "null"}\"");

			using (var withKey = JsonDocument.Parse(LgWebOsProtocol.RegisterMessage("register_1", "secret-key")))
			using (var withoutKey = JsonDocument.Parse(LgWebOsProtocol.RegisterMessage("register_2", null)))
			{
				var payload = withKey.RootElement.GetProperty("payload");
				Check(withKey.RootElement.GetProperty("type").GetString() == "register" && payload.GetProperty("client-key").GetString() == "secret-key"
					&& payload.GetProperty("pairingType").GetString() == "PROMPT", "register carries the stored key and asks for a prompt");
				var manifest = payload.GetProperty("manifest");
				Check(manifest.GetProperty("signatures")[0].GetProperty("signature").GetString()!.StartsWith("eyJhbGdvcml0aG0i", StringComparison.Ordinal)
					&& manifest.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == "CONTROL_INPUT_TV")
					&& manifest.GetProperty("permissions").EnumerateArray().Any(p => p.GetString() == "READ_RUNNING_APPS")
					&& manifest.GetProperty("signed").GetProperty("localizedAppNames").GetProperty("ko-KR").GetString() == "리모컨 앱", "register sends LG's signed manifest with input and app-status permissions");
				Check(!withoutKey.RootElement.GetProperty("payload").TryGetProperty("client-key", out _), "first pairing sends no key");
			}
			using (var input = JsonDocument.Parse(LgWebOsProtocol.SwitchInputMessage("core_1", "hdmi 2")))
				Check(input.RootElement.GetProperty("uri").GetString() == "ssap://tv/switchInput" && input.RootElement.GetProperty("payload").GetProperty("inputId").GetString() == "HDMI_2"
					&& input.RootElement.GetProperty("id").GetString() == "core_1", "switch input request");
			using (var off = JsonDocument.Parse(LgWebOsProtocol.TurnOffMessage("core_2")))
				Check(off.RootElement.GetProperty("uri").GetString() == "ssap://system/turnOff" && off.RootElement.GetProperty("type").GetString() == "request", "turn off request");
			using (var fg = JsonDocument.Parse(LgWebOsProtocol.ForegroundAppMessage("core_3")))
				Check(fg.RootElement.GetProperty("uri").GetString() == "ssap://com.webos.applicationManager/getForegroundAppInfo", "foreground app request");

			var registered = LgWebOsProtocol.ParseReply("""{"type":"registered","id":"register_0","payload":{"client-key":"abc123"}}""");
			var prompt = LgWebOsProtocol.ParseReply("""{"type":"response","id":"register_0","payload":{"pairingType":"PROMPT","returnValue":true}}""");
			var rejected = LgWebOsProtocol.ParseReply("""{"type":"error","id":"register_0","error":"403 User rejected pairing","payload":{}}""");
			var foreground = LgWebOsProtocol.ParseReply("""{"type":"response","id":"core_3","payload":{"returnValue":true,"appId":"com.webos.app.hdmi2","processId":"","windowId":""}}""");
			Check(registered.Type == "registered" && registered.ClientKey == "abc123", "registered reply yields the key");
			Check(prompt.PairingType == "PROMPT" && prompt.ReturnValue == true, "prompt reply is recognised");
			Check(rejected.Type == "error" && rejected.Error!.Contains("403"), "rejected pairing is an error");
			Check(foreground.AppId == "com.webos.app.hdmi2" && foreground.Id == "core_3", "foreground reply yields the app id");
			var power = LgWebOsProtocol.ParseReply("""{"type":"response","id":"core_4","payload":{"returnValue":true,"state":"Active Standby","processing":"Request Active Standby"}}""");
			Check(power.PowerState == "Active Standby" && LgWebOsProtocol.TvInUse(power.PowerState) == false, "power state reply: Quick Start+ standby is not in use");
			Check(LgWebOsProtocol.TvInUse("Active") == true && LgWebOsProtocol.TvInUse("Screen Off") == true && LgWebOsProtocol.TvInUse("Suspend") == false
				&& LgWebOsProtocol.TvInUse(null) is null && LgWebOsProtocol.TvInUse("Something New") is null, "power states map to in use / not in use / unknown");
			Check(LgWebOsProtocol.ParseReply("not json").Type == "invalid", "garbage replies do not throw");

			string search = Encoding.ASCII.GetString(LgWebOsProtocol.SsdpSearchRequest());
			Check(search.StartsWith("M-SEARCH * HTTP/1.1\r\n") && search.Contains("ST: urn:lge-com:service:webos-second-screen:1\r\n") && search.EndsWith("\r\n\r\n"), "SSDP search for webOS second screen");
			var tv = LgWebOsProtocol.ParseSsdpReply("HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=1800\r\nST: urn:lge-com:service:webos-second-screen:1\r\nUSN: uuid:x::urn:lge-com:service:webos-second-screen:1\r\nDLNADeviceName.lge.com: %5BLG%5D%20webOS%20TV%20OLED55C9\r\n\r\n", "192.168.1.50");
			Check(tv is { Address: "192.168.1.50", Name: "[LG] webOS TV OLED55C9" }, "SSDP reply from a C9 is found with its name");
			Check(LgWebOsProtocol.ParseSsdpReply("HTTP/1.1 200 OK\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\nSERVER: Linux UPnP\r\n\r\n", "192.168.1.9") is null, "other UPnP devices are ignored");
		}
		// TV controller against a fake TV. Budgets are generous: on Windows a refused loopback
		// connection (the fake TV has no port 3001) takes about two seconds before the fallback.
		{
			await using var fake = new FakeTv();
			var settings = new TvSettings { Enabled = true, Address = "127.0.0.1", MacAddress = "A8:23:FE:01:02:03", CoreInput = "HDMI_2" };
			var pairing = new MemoryPairing();
			var log = new List<string>();
			var controller = new TvController(() => settings, pairing, line => { lock (log) log.Add(line); }, _ => new byte[] { 1, 2, 3, 4, 5, 6 }, fake.Port);

			bool prompted = false;
			var paired = await controller.PairAsync(() => prompted = true, CancellationToken.None);
			Check(paired.Outcome == TvOutcome.Done && prompted && pairing.ClientKey == FakeTv.Key, "pairing waits for the prompt and stores the key");
			Check(paired.MacAddress is null, "a saved MAC is not replaced by the network lookup");
			settings.MacAddress = null;
			var pairedAgain = await controller.PairAsync(null, CancellationToken.None);
			Check(pairedAgain.MacAddress == "01:02:03:04:05:06", "pairing reads the MAC from the network when none is saved");
			settings.MacAddress = "A8:23:FE:01:02:03";

			fake.Foreground = "netflix";
			var keep = await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(keep.Outcome == TvOutcome.SkippedOtherInput && !fake.TurnedOff, "sleep: TV showing Netflix stays on");
			fake.Foreground = "com.webos.app.hdmi1";
			keep = await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(keep.Outcome == TvOutcome.SkippedOtherInput && !fake.TurnedOff, "sleep: TV on another HDMI stays on");
			fake.Foreground = "com.webos.app.hdmi2";
			var off = await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(off.Outcome == TvOutcome.Done && fake.TurnedOff, "sleep: TV showing Core's HDMI 2 is turned off");

			fake.TurnedOff = false; fake.Foreground = "youtube.leanback.v4"; fake.PowerState = "Active";
			var wake = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(wake.Outcome == TvOutcome.LeftOnInUse && fake.SwitchedTo is null, "wake: a TV already on (YouTube) is left alone");
			fake.PowerState = "Screen Off";
			wake = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(wake.Outcome == TvOutcome.LeftOnInUse && fake.SwitchedTo is null, "wake: a TV playing sound with its screen off is left alone");
			fake.PowerState = "Active Standby";
			wake = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(wake.Outcome == TvOutcome.Done && fake.SwitchedTo == "HDMI_2", "wake: a TV in standby is switched to Core's HDMI 2");
			fake.SwitchedTo = null; fake.Foreground = "youtube.leanback.v4"; fake.PowerState = null;
			wake = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(wake.Outcome == TvOutcome.Done && fake.SwitchedTo == "HDMI_2", "wake: an unknown power state counts as off");
			fake.PowerState = "Active Standby";
			fake.SwitchedTo = null; fake.Foreground = "com.webos.app.hdmi2";
			wake = await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(wake.Outcome == TvOutcome.Done && fake.SwitchedTo is null, "wake: no switch when the TV already shows Core");

			settings.TurnOffWithCore = false;
			fake.TurnedOff = false;
			Check((await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None)).Outcome == TvOutcome.Disabled && !fake.TurnedOff, "turn-off setting off: nothing is sent");
			settings.TurnOffWithCore = true; settings.Enabled = false;
			Check((await controller.WakeAndSwitchAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).Outcome == TvOutcome.Disabled, "TV control off: wake does nothing");
			settings.Enabled = true;

			fake.AcceptKey = false;
			var stale = await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
			Check(stale.Outcome == TvOutcome.NotPaired && !fake.TurnedOff, "a key the TV no longer accepts is reported as not paired");
			fake.AcceptKey = true;

			pairing.Clear();
			Check((await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(10), CancellationToken.None)).Outcome == TvOutcome.NotPaired, "unpaired: nothing is sent");
			settings.Address = "127.0.0.2";
			pairing.Save(FakeTv.Key, null);
			var unreachable = await controller.TurnOffIfShowingCoreAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
			Check(unreachable.Outcome is TvOutcome.Unreachable, "an unreachable TV is reported, not thrown");
			lock (log) Check(log.Count > 0 && log.All(line => !line.Contains(FakeTv.Key, StringComparison.Ordinal)), "the client key never appears in the log");
		}
	}
}

internal sealed class MemoryPairing : ITvPairingStore
{
	public string? ClientKey { get; private set; }
	public string? CertificateSha256 { get; private set; }
	public void Save(string clientKey, string? certificateSha256) { ClientKey = clientKey; CertificateSha256 = certificateSha256; }
	public void Clear() { ClientKey = null; CertificateSha256 = null; }
}

internal sealed class FakeTv : IAsyncDisposable
{
	internal const string Key = "0123456789abcdef0123456789abcdef";
	private readonly HttpListener _listener = new();
	private readonly CancellationTokenSource _stop = new();
	private readonly Task _loop;
	internal int Port { get; }
	internal volatile string Foreground = "com.webos.app.hdmi2";
	internal volatile bool TurnedOff;
	internal volatile string? SwitchedTo;
	internal volatile bool AcceptKey = true;
	internal volatile string? PowerState = "Active Standby";

	internal FakeTv()
	{
		for (int attempt = 0; ; attempt++)
		{
			int port = Random.Shared.Next(20000, 60000);
			_listener.Prefixes.Clear();
			_listener.Prefixes.Add($"http://127.0.0.1:{port}/");
			try { _listener.Start(); Port = port; break; }
			catch (HttpListenerException) when (attempt < 20) { }
		}
		_loop = Task.Run(LoopAsync);
	}

	private async Task LoopAsync()
	{
		while (!_stop.IsCancellationRequested)
		{
			HttpListenerContext context;
			try { context = await _listener.GetContextAsync(); } catch { return; }
			_ = Task.Run(() => ServeAsync(context));
		}
	}

	private async Task ServeAsync(HttpListenerContext context)
	{
		if (!context.Request.IsWebSocketRequest) { context.Response.StatusCode = 400; context.Response.Close(); return; }
		WebSocket socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
		var buffer = new byte[65536];
		try
		{
			while (socket.State == WebSocketState.Open)
			{
				var result = await socket.ReceiveAsync(buffer, _stop.Token);
				if (result.MessageType == WebSocketMessageType.Close) break;
				var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!.AsObject();
				string id = message["id"]!.GetValue<string>();
				string type = message["type"]!.GetValue<string>();
				if (type == "register")
				{
					string? key = message["payload"]?["client-key"]?.GetValue<string>();
					if (key == Key && AcceptKey) { await Send(socket, new JsonObject { ["type"] = "registered", ["id"] = id, ["payload"] = new JsonObject { ["client-key"] = Key } }); continue; }
					await Send(socket, new JsonObject { ["type"] = "response", ["id"] = id, ["payload"] = new JsonObject { ["pairingType"] = "PROMPT", ["returnValue"] = true } });
					if (key is null)
					{
						await Task.Delay(150);
						await Send(socket, new JsonObject { ["type"] = "registered", ["id"] = id, ["payload"] = new JsonObject { ["client-key"] = Key } });
					}
					continue;
				}
				string uri = message["uri"]!.GetValue<string>();
				JsonObject payload = new() { ["returnValue"] = true };
				if (uri == "ssap://com.webos.applicationManager/getForegroundAppInfo") payload["appId"] = Foreground;
				else if (uri == "ssap://com.webos.service.tvpower/power/getPowerState")
				{
					if (PowerState is null) { await Send(socket, new JsonObject { ["type"] = "error", ["id"] = id, ["error"] = "404 no such service or method", ["payload"] = new JsonObject() }); continue; }
					payload["state"] = PowerState;
				}
				else if (uri == "ssap://system/turnOff") TurnedOff = true;
				else if (uri == "ssap://tv/switchInput") { SwitchedTo = message["payload"]!["inputId"]!.GetValue<string>(); Foreground = "com.webos.app.hdmi" + SwitchedTo[^1]; }
				await Send(socket, new JsonObject { ["type"] = "response", ["id"] = id, ["payload"] = payload });
			}
		}
		catch { }
	}

	private static Task Send(WebSocket socket, JsonObject message) =>
		socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

	public async ValueTask DisposeAsync()
	{
		_stop.Cancel();
		try { _listener.Stop(); } catch { }
		try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
		_listener.Close();
	}
}
