using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GamingMode.Services;

// Eternal Core Mark III: the pure half of LG webOS TV control (no sockets, no Windows).
//
// The TV (an LG C9, webOS 4.5) is controlled over the network:
// - on: a Wake-on-LAN magic packet to the TV's MAC address (needs "Turn on via Wi-Fi" /
//   "Mobile TV On" and Quick Start+ on the TV);
// - input, power off and "what is on screen": LG's SSAP JSON messages over a websocket,
//   wss://<tv>:3001 (port 3000 is the unencrypted fallback older firmware still offers).
// The first connection shows a pairing prompt on the TV; accepting it with the remote
// returns a client key that later connections present.
//
// Written for Eternal Core (MIT). The message shapes are LG's public second-screen
// protocol. The signed manifest below is LG's own remote-app manifest, a data constant
// that every open client sends so the TV grants the standard permissions; the copy here
// follows LGWebOSRemote (MIT, (c) 2017 Karl Lattimer, github.com/klattimer/LGWebOSRemote).
internal static class LgWebOsProtocol
{
	internal const int SecurePort = 3001;
	internal const int PlainPort = 3000;
	internal const int WakeOnLanPort = 9;
	internal const string DefaultCoreInput = "HDMI_2";
	internal const string SwitchInputUri = "ssap://tv/switchInput";
	internal const string ForegroundAppUri = "ssap://com.webos.applicationManager/getForegroundAppInfo";
	internal const string TurnOffUri = "ssap://system/turnOff";
	internal const string SsdpAddress = "239.255.255.250";
	internal const int SsdpPort = 1900;
	internal const string SsdpSearchTarget = "urn:lge-com:service:webos-second-screen:1";

	// ---------- MAC address and Wake-on-LAN ----------

	// Accepts AA:BB:CC:DD:EE:FF, AA-BB-CC-DD-EE-FF, AABB.CCDD.EEFF and AABBCCDDEEFF.
	internal static bool TryParseMac(string? text, out byte[] mac)
	{
		mac = Array.Empty<byte>();
		if (string.IsNullOrWhiteSpace(text)) return false;
		var hex = new StringBuilder(12);
		foreach (char c in text.Trim())
		{
			if (Uri.IsHexDigit(c)) hex.Append(c);
			else if (c is not (':' or '-' or '.' or ' ')) return false;
		}
		if (hex.Length != 12) return false;
		var bytes = new byte[6];
		for (int i = 0; i < 6; i++)
			bytes[i] = byte.Parse(hex.ToString(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		// All-zero and broadcast addresses are not a TV.
		if (Array.TrueForAll(bytes, b => b == 0) || Array.TrueForAll(bytes, b => b == 0xFF)) return false;
		mac = bytes;
		return true;
	}

	internal static string FormatMac(byte[] mac)
	{
		if (mac.Length != 6) throw new ArgumentException("A MAC address has six bytes.", nameof(mac));
		return string.Join(":", Array.ConvertAll(mac, b => b.ToString("X2", CultureInfo.InvariantCulture)));
	}

	// Six 0xFF bytes followed by the MAC sixteen times: 102 bytes.
	internal static byte[] MagicPacket(byte[] mac)
	{
		if (mac.Length != 6) throw new ArgumentException("A MAC address has six bytes.", nameof(mac));
		var packet = new byte[102];
		for (int i = 0; i < 6; i++) packet[i] = 0xFF;
		for (int repeat = 0; repeat < 16; repeat++) Buffer.BlockCopy(mac, 0, packet, 6 + repeat * 6, 6);
		return packet;
	}

	// ---------- HDMI inputs ----------

	// "HDMI 2", "hdmi2", "HDMI_2", "HDMI-2" and "2" all mean HDMI_2. webOS TVs have up to four.
	internal static string? NormalizeInput(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return null;
		string value = text.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
		if (value.StartsWith("HDMI", StringComparison.Ordinal)) value = value[4..];
		return value.Length == 1 && value[0] is >= '1' and <= '4' ? "HDMI_" + value : null;
	}

	internal static int InputNumber(string input) => NormalizeInput(input) is { } normalized ? normalized[^1] - '0' : 0;

	// webOS shows an HDMI input through an app whose id ends in the input number.
	internal static string InputAppId(string input)
	{
		int number = InputNumber(input);
		if (number == 0) throw new ArgumentException("Not an HDMI input.", nameof(input));
		return "com.webos.app.hdmi" + number.ToString(CultureInfo.InvariantCulture);
	}

	internal static bool ForegroundIsInput(string? foregroundAppId, string input)
	{
		if (string.IsNullOrWhiteSpace(foregroundAppId) || InputNumber(input) == 0) return false;
		return string.Equals(foregroundAppId.Trim(), InputAppId(input), StringComparison.OrdinalIgnoreCase);
	}

	// The off rule: Core turns the TV off only while the TV shows Core's input. Anything
	// else (an app, live TV, another HDMI, or an unknown answer) leaves the TV alone, so
	// Core going to sleep never cuts off something someone else is watching.
	internal static bool ShouldTurnOff(string? foregroundAppId, string coreInput) => ForegroundIsInput(foregroundAppId, coreInput);

	// ---------- SSAP messages ----------

	internal static string RegisterMessage(string id, string? clientKey)
	{
		var payload = new JsonObject
		{
			["forcePairing"] = false,
			["pairingType"] = "PROMPT",
			["manifest"] = JsonNode.Parse(Manifest),
		};
		if (!string.IsNullOrWhiteSpace(clientKey)) payload["client-key"] = clientKey;
		return new JsonObject { ["type"] = "register", ["id"] = id, ["payload"] = payload }.ToJsonString();
	}

	internal static string RequestMessage(string id, string uri, JsonObject? payload = null)
	{
		var message = new JsonObject { ["type"] = "request", ["id"] = id, ["uri"] = uri };
		if (payload is not null) message["payload"] = payload;
		return message.ToJsonString();
	}

	internal static string SwitchInputMessage(string id, string input) =>
		RequestMessage(id, SwitchInputUri, new JsonObject { ["inputId"] = NormalizeInput(input) ?? throw new ArgumentException("Not an HDMI input.", nameof(input)) });

	internal static string ForegroundAppMessage(string id) => RequestMessage(id, ForegroundAppUri);

	internal static string TurnOffMessage(string id) => RequestMessage(id, TurnOffUri);

	internal readonly record struct SsapReply(string Type, string? Id, string? ClientKey, string? PairingType,
		bool? ReturnValue, string? AppId, string? Error);

	internal static SsapReply ParseReply(string json)
	{
		try
		{
			using JsonDocument document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return new("invalid", null, null, null, null, null, "not an object");
			string type = String(root, "type") ?? "";
			string? id = String(root, "id");
			string? error = String(root, "error");
			string? clientKey = null, pairing = null, appId = null;
			bool? ok = null;
			if (root.TryGetProperty("payload", out JsonElement payload) && payload.ValueKind == JsonValueKind.Object)
			{
				clientKey = String(payload, "client-key");
				pairing = String(payload, "pairingType");
				appId = String(payload, "appId");
				if (payload.TryGetProperty("returnValue", out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
					ok = value.GetBoolean();
				error ??= String(payload, "errorText");
			}
			return new(type, id, clientKey, pairing, ok, appId, error);
		}
		catch (JsonException) { return new("invalid", null, null, null, null, null, "unreadable"); }
	}

	private static string? String(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	// ---------- SSDP discovery ----------

	internal static byte[] SsdpSearchRequest() => Encoding.ASCII.GetBytes(
		"M-SEARCH * HTTP/1.1\r\n" +
		"HOST: " + SsdpAddress + ":" + SsdpPort.ToString(CultureInfo.InvariantCulture) + "\r\n" +
		"MAN: \"ssdp:discover\"\r\n" +
		"MX: 2\r\n" +
		"ST: " + SsdpSearchTarget + "\r\n\r\n");

	internal readonly record struct DiscoveredTv(string Address, string Name);

	// A webOS answer names the second-screen service, or LG/webOS in SERVER. The address is
	// the sender's; the friendly name comes from LG's DLNADeviceName header when present.
	internal static DiscoveredTv? ParseSsdpReply(string reply, string senderAddress)
	{
		if (string.IsNullOrWhiteSpace(reply) || string.IsNullOrWhiteSpace(senderAddress)) return null;
		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string[] lines = reply.Replace("\r\n", "\n").Split('\n');
		if (lines.Length == 0 || !lines[0].StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)) return null;
		foreach (string line in lines)
		{
			int colon = line.IndexOf(':');
			if (colon <= 0) continue;
			headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
		}
		bool webOs = (headers.TryGetValue("ST", out string? st) && st.Contains("webos-second-screen", StringComparison.OrdinalIgnoreCase))
			|| (headers.TryGetValue("USN", out string? usn) && usn.Contains("webos-second-screen", StringComparison.OrdinalIgnoreCase))
			|| (headers.TryGetValue("SERVER", out string? server) && server.Contains("WebOS", StringComparison.OrdinalIgnoreCase));
		if (!webOs) return null;
		string name = headers.TryGetValue("DLNADeviceName.lge.com", out string? dlna) && dlna.Length > 0
			? Uri.UnescapeDataString(dlna) : "LG webOS TV";
		return new(senderAddress, name.Length > 80 ? name[..80] : name);
	}

	// ---------- Manifest ----------

	private const string Manifest = """
	{"manifestVersion":1,"appVersion":"1.1",
	 "signed":{"created":"20140509","appId":"com.lge.test","vendorId":"com.lge",
	  "localizedAppNames":{"":"LG Remote App","ko-KR":"리모컨 앱","zxx-XX":"ЛГ Rэmotэ AПП"},
	  "localizedVendorNames":{"":"LG Electronics"},
	  "permissions":["TEST_SECURE","CONTROL_INPUT_TEXT","CONTROL_MOUSE_AND_KEYBOARD","READ_INSTALLED_APPS","READ_LGE_SDX","READ_NOTIFICATIONS","SEARCH","WRITE_SETTINGS","WRITE_NOTIFICATION_ALERT","CONTROL_POWER","READ_CURRENT_CHANNEL","READ_RUNNING_APPS","READ_UPDATE_INFO","UPDATE_FROM_REMOTE_APP","READ_LGE_TV_INPUT_EVENTS","READ_TV_CURRENT_TIME"],
	  "serial":"2f930e2d2cfe083771f68e4fe7bb07"},
	 "permissions":["LAUNCH","LAUNCH_WEBAPP","APP_TO_APP","CLOSE","TEST_OPEN","TEST_PROTECTED","CONTROL_AUDIO","CONTROL_DISPLAY","CONTROL_INPUT_JOYSTICK","CONTROL_INPUT_MEDIA_RECORDING","CONTROL_INPUT_MEDIA_PLAYBACK","CONTROL_INPUT_TV","CONTROL_POWER","READ_APP_STATUS","READ_CURRENT_CHANNEL","READ_INPUT_DEVICE_LIST","READ_NETWORK_STATE","READ_RUNNING_APPS","READ_TV_CHANNEL_LIST","WRITE_NOTIFICATION_TOAST","READ_POWER_STATE","READ_COUNTRY_INFO","READ_SETTINGS","CONTROL_TV_SCREEN","CONTROL_TV_STANBY","CONTROL_FAVORITE_GROUP","CONTROL_USER_INFO","CHECK_BLUETOOTH_DEVICE","CONTROL_BLUETOOTH","CONTROL_TIMER_INFO","STB_INTERNAL_CONNECTION","CONTROL_RECORDING","READ_RECORDING_STATE","WRITE_RECORDING_LIST","READ_RECORDING_LIST","READ_RECORDING_SCHEDULE","WRITE_RECORDING_SCHEDULE","READ_STORAGE_DEVICE_LIST","READ_TV_PROGRAM_INFO","CONTROL_BOX_CHANNEL","READ_TV_ACR_AUTH_TOKEN","READ_TV_CONTENT_STATE","READ_TV_CURRENT_TIME","ADD_LAUNCHER_CHANNEL","SET_CHANNEL_SKIP","RELEASE_CHANNEL_SKIP","CONTROL_CHANNEL_BLOCK","DELETE_SELECT_CHANNEL","CONTROL_CHANNEL_GROUP","SCAN_TV_CHANNELS","CONTROL_TV_POWER","CONTROL_WOL"],
	 "signatures":[{"signatureVersion":1,"signature":"eyJhbGdvcml0aG0iOiJSU0EtU0hBMjU2Iiwia2V5SWQiOiJ0ZXN0LXNpZ25pbmctY2VydCIsInNpZ25hdHVyZVZlcnNpb24iOjF9.hrVRgjCwXVvE2OOSpDZ58hR+59aFNwYDyjQgKk3auukd7pcegmE2CzPCa0bJ0ZsRAcKkCTJrWo5iDzNhMBWRyaMOv5zWSrthlf7G128qvIlpMT0YNY+n/FaOHE73uLrS/g7swl3/qH/BGFG2Hu4RlL48eb3lLKqTt2xKHdCs6Cd4RMfJPYnzgvI4BNrFUKsjkcu+WD4OO2A27Pq1n50cMchmcaXadJhGrOqH5YmHdOCj5NSHzJYrsW0HPlpuAx/ECMeIZYDh6RMqaFM2DXzdKX9NmmyqzJ3o/0lkk/N97gfVRLW5hA29yeAwaCViZNCP8iC9aO0q9fQojoa7NQnAtw=="}]}
	""";
}
