using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamingMode.Services;

// Eternal Core batch 2 (B8, docs/eternal/BATCH-2.md): Core's state in Home Assistant over MQTT, with MQTT discovery so
// the entities appear by themselves under one device ("Core (<PC name>)"). Pure: topics, discovery payloads, the
// state message, command parsing and the Health row. The connection is MqttWire.cs; the wiring and the Windows
// probes are MarkIIIHost.Bridge.cs.
//
// Published (retained): core/<node>/state (one JSON with every value), core/<node>/asleep (ON/OFF, no availability so
// it survives the PC going away), core/<node>/availability (online, offline as the last will), core/<node>/event
// (automation events, not retained). Subscribed: core/<node>/cmd/+ (wake_tv, sleep, keep_tv_out),
// homeassistant/status (re-announce after Home Assistant restarts) and aincrad/sleep_mode (Aincrad's sleep mode,
// published by the core_pc.yaml package). "Update Steam now" is deliberately not a command: it stays local.
internal sealed record HaState
{
	public bool Game { get; init; }
	public string? GameName { get; init; }
	public uint GameAppId { get; init; }
	// "playing", "paused", "open" (the app runs, nothing plays) or "off".
	public string Media { get; init; } = "off";
	public string? MediaApp { get; init; }
	public bool Idle { get; init; }
	public bool StreamMode { get; init; }
	public string? StreamDevice { get; init; }
	public bool KeepTvOut { get; init; }
	public bool Vr { get; init; }
	public bool GamingMode { get; init; }
}

internal enum HaCommandKind { WakeTv, Sleep, KeepTvOut }

internal sealed record HaCommand(HaCommandKind Kind, bool On = false);

internal sealed record HaBridgeStatus(bool Enabled, bool Configured, bool Connected, string? Host, string? Failure, DateTimeOffset? Since, bool PasswordSaved);

internal static class HomeAssistantBridgePolicy
{
	internal const string DiscoveryPrefix = "homeassistant";
	internal const string SleepModeTopic = "aincrad/sleep_mode";
	internal const string HaStatusTopic = "homeassistant/status";
	internal const int DefaultPort = 1883;
	internal const string DefaultHost = "serverv3";
	internal const ushort KeepAliveSeconds = 30;
	internal static readonly TimeSpan[] Backoff = { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5) };
	private static readonly Regex EventName = new("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant);
	private static readonly Regex HostName = new(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant);

	// The node id in topics and unique ids: the PC's name, lower case, letters, digits, "-" and "_" only.
	internal static string NodeId(string? machineName)
	{
		var builder = new StringBuilder();
		foreach (char character in (machineName ?? "").Trim().ToLowerInvariant())
		{
			if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_') builder.Append(character);
			else if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
			if (builder.Length == 32) break;
		}
		string id = builder.ToString().Trim('_');
		return id.Length == 0 ? "core" : id;
	}

	internal static string Base(string node) => "core/" + node;
	internal static string StateTopic(string node) => Base(node) + "/state";
	internal static string AsleepTopic(string node) => Base(node) + "/asleep";
	internal static string AvailabilityTopic(string node) => Base(node) + "/availability";
	internal static string EventTopic(string node) => Base(node) + "/event";
	internal static string CommandFilter(string node) => Base(node) + "/cmd/+";

	// A host name or an IP address, nothing else (no scheme, no user:password@, no path). Port separately.
	internal static string? CleanHost(string? host)
	{
		if (string.IsNullOrWhiteSpace(host)) return null;
		string value = host.Trim();
		if (IPAddress.TryParse(value.Trim('[', ']'), out IPAddress? address)) return address.ToString();
		return HostName.IsMatch(value) ? value : null;
	}

	internal static int CleanPort(int port) => port is >= 1 and <= 65535 ? port : DefaultPort;

	// MQTT 3.1.1 limits client ids to 23 characters for every broker to accept them.
	internal static string ClientId(string node) => ("core-" + node).Length <= 23 ? "core-" + node : ("core-" + node)[..23];

	internal static string? CleanEvent(string? name) => name is not null && EventName.IsMatch(name.Trim()) ? name.Trim() : null;

	// ---------- discovery ----------

	// Every entity's config message (topic, retained JSON). eventTypes: the automations' Home Assistant events.
	internal static IReadOnlyList<(string Topic, string Payload)> Discovery(string node, string deviceName, string version, IReadOnlyList<string> eventTypes)
	{
		var list = new List<(string, string)>();
		void Add(string component, string id, string name, Action<Utf8JsonWriter> fill, bool available = true)
		{
			using var stream = new MemoryStream();
			using (var json = new Utf8JsonWriter(stream))
			{
				json.WriteStartObject();
				json.WriteString("name", name);
				json.WriteString("unique_id", $"core_{node}_{id}");
				json.WriteString("object_id", $"core_{node}_{id}");
				if (available)
				{
					json.WriteString("availability_topic", AvailabilityTopic(node));
					json.WriteString("payload_available", "online");
					json.WriteString("payload_not_available", "offline");
				}
				fill(json);
				json.WriteStartObject("device");
				json.WriteStartArray("identifiers"); json.WriteStringValue("core_" + node); json.WriteEndArray();
				json.WriteString("name", deviceName);
				json.WriteString("manufacturer", "Eternal");
				json.WriteString("model", "Core");
				json.WriteString("sw_version", version);
				json.WriteEndObject();
				json.WriteEndObject();
			}
			list.Add(($"{DiscoveryPrefix}/{component}/core_{node}/{id}/config", Encoding.UTF8.GetString(stream.ToArray())));
		}
		void Binary(string id, string name, string field, string icon, string? deviceClass = null) => Add("binary_sensor", id, name, json =>
		{
			json.WriteString("state_topic", StateTopic(node));
			json.WriteString("value_template", "{{ value_json." + field + " }}");
			json.WriteString("payload_on", "ON"); json.WriteString("payload_off", "OFF");
			json.WriteString("icon", icon);
			if (deviceClass is not null) json.WriteString("device_class", deviceClass);
		});
		void Text(string id, string name, string field, string icon) => Add("sensor", id, name, json =>
		{
			json.WriteString("state_topic", StateTopic(node));
			json.WriteString("value_template", "{{ value_json." + field + " }}");
			json.WriteString("icon", icon);
		});

		Binary("game", "Game running", "game", "mdi:controller", "running");
		Add("sensor", "game_name", "Game", json =>
		{
			json.WriteString("state_topic", StateTopic(node));
			json.WriteString("value_template", "{{ value_json.game_name }}");
			json.WriteString("json_attributes_topic", StateTopic(node));
			json.WriteString("json_attributes_template", "{{ {'app_id': value_json.app_id} | tojson }}");
			json.WriteString("icon", "mdi:gamepad-variant");
		});
		Add("sensor", "media", "Media", json =>
		{
			json.WriteString("state_topic", StateTopic(node));
			json.WriteString("value_template", "{{ value_json.media }}");
			json.WriteString("device_class", "enum");
			json.WriteStartArray("options");
			foreach (string option in new[] { "playing", "paused", "open", "off" }) json.WriteStringValue(option);
			json.WriteEndArray();
			json.WriteString("json_attributes_topic", StateTopic(node));
			json.WriteString("json_attributes_template", "{{ {'app': value_json.media_app} | tojson }}");
			json.WriteString("icon", "mdi:movie-open-play");
		});
		Binary("media_playing", "Movie or show playing", "media_playing", "mdi:play-circle");
		Binary("idle", "Idle", "idle", "mdi:sleep-off");
		Add("binary_sensor", "asleep", "Asleep", json =>
		{
			json.WriteString("state_topic", AsleepTopic(node));
			json.WriteString("payload_on", "ON"); json.WriteString("payload_off", "OFF");
			json.WriteString("icon", "mdi:power-sleep");
		}, available: false);
		Binary("stream_mode", "Stream mode", "stream_mode", "mdi:cast-connected");
		Text("stream_device", "Streaming to", "stream_device", "mdi:cellphone-link");
		Binary("vr", "VR (SteamVR)", "vr", "mdi:virtual-reality");
		Binary("gaming_mode", "Gaming Mode", "gaming_mode", "mdi:television-play");
		Add("button", "wake_tv", "Wake TV", json =>
		{
			json.WriteString("command_topic", Base(node) + "/cmd/wake_tv");
			json.WriteString("payload_press", "PRESS");
			json.WriteString("icon", "mdi:television");
		});
		Add("switch", "keep_tv_out", "Stream mode (keep TV out)", json =>
		{
			json.WriteString("command_topic", Base(node) + "/cmd/keep_tv_out");
			json.WriteString("state_topic", StateTopic(node));
			json.WriteString("value_template", "{{ value_json.keep_tv_out }}");
			json.WriteString("payload_on", "ON"); json.WriteString("payload_off", "OFF");
			json.WriteString("icon", "mdi:television-off");
		});
		Add("button", "sleep", "Sleep PC", json =>
		{
			json.WriteString("command_topic", Base(node) + "/cmd/sleep");
			json.WriteString("payload_press", "PRESS");
			json.WriteString("icon", "mdi:power-sleep");
		});
		Add("event", "automation", "Automation", json =>
		{
			json.WriteString("state_topic", EventTopic(node));
			json.WriteStartArray("event_types");
			foreach (string type in eventTypes.Select(CleanEvent).OfType<string>().Distinct().DefaultIfEmpty("core")) json.WriteStringValue(type);
			json.WriteEndArray();
			json.WriteString("icon", "mdi:robot");
		});
		return list;
	}

	// ---------- state ----------

	internal static string StatePayload(HaState state)
	{
		static string OnOff(bool value) => value ? "ON" : "OFF";
		using var stream = new MemoryStream();
		using (var json = new Utf8JsonWriter(stream))
		{
			json.WriteStartObject();
			json.WriteString("game", OnOff(state.Game));
			json.WriteString("game_name", state.Game ? Clean(state.GameName) ?? (state.GameAppId != 0 ? "App " + state.GameAppId.ToString(CultureInfo.InvariantCulture) : "Game") : "none");
			if (state.Game && state.GameAppId != 0) json.WriteNumber("app_id", state.GameAppId); else json.WriteNull("app_id");
			json.WriteString("media", state.Media is "playing" or "paused" or "open" ? state.Media : "off");
			json.WriteString("media_app", Clean(state.MediaApp) ?? "none");
			json.WriteString("media_playing", OnOff(state.Media == "playing"));
			json.WriteString("idle", OnOff(state.Idle));
			json.WriteString("stream_mode", OnOff(state.StreamMode));
			json.WriteString("stream_device", state.StreamMode ? Clean(state.StreamDevice) ?? "unknown" : "none");
			json.WriteString("keep_tv_out", OnOff(state.KeepTvOut));
			json.WriteString("vr", OnOff(state.Vr));
			json.WriteString("gaming_mode", OnOff(state.GamingMode));
			json.WriteEndObject();
		}
		return Encoding.UTF8.GetString(stream.ToArray());
	}

	internal static string EventPayload(string type) => JsonSerializer.Serialize(new Dictionary<string, string> { ["event_type"] = CleanEvent(type) ?? "core" });

	// Idle: in Gaming Mode with nothing going on (no game, nothing playing, no stream, no VR).
	internal static bool IsIdle(HaState state) => state.GamingMode && !state.Game && state.Media != "playing" && !state.StreamMode && !state.Vr;

	private static string? Clean(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var builder = new StringBuilder();
		foreach (char character in value.Trim()) { if (!char.IsControl(character)) builder.Append(character); if (builder.Length == 80) break; }
		return builder.Length == 0 ? null : builder.ToString();
	}

	// ---------- commands ----------

	// Only the three commands Core offers; anything else (including an "update Steam" someone adds by hand) is null.
	internal static HaCommand? ParseCommand(string node, string topic, string payload)
	{
		string prefix = Base(node) + "/cmd/";
		if (!topic.StartsWith(prefix, StringComparison.Ordinal)) return null;
		string name = topic[prefix.Length..], value = payload.Trim().ToUpperInvariant();
		return name switch
		{
			"wake_tv" when value == "PRESS" => new HaCommand(HaCommandKind.WakeTv),
			"sleep" when value == "PRESS" => new HaCommand(HaCommandKind.Sleep),
			"keep_tv_out" when value is "ON" or "OFF" => new HaCommand(HaCommandKind.KeepTvOut, value == "ON"),
			_ => null,
		};
	}

	// Aincrad's sleep mode, as the core_pc.yaml package publishes it (the input_boolean's state).
	internal static bool? ParseSleepMode(string payload) => payload.Trim().ToLowerInvariant() switch
	{
		"on" or "true" or "1" => true,
		"off" or "false" or "0" => false,
		_ => null,
	};

	// "Sleep PC" from Home Assistant: only when nobody is playing, here or over a stream. Null means go ahead.
	internal static string? SleepRefusal(bool gameRunning, bool streaming, bool coreBusy) =>
		streaming ? "a stream is running" : gameRunning ? "a game is running" : coreBusy ? "Core is updating" : null;

	// ---------- Core Health row ----------

	internal static HealthRow Row(HaBridgeStatus status)
	{
		var data = new Dictionary<string, object?>(StringComparer.Ordinal) { ["host"] = status.Host, ["failure"] = status.Failure, ["since"] = status.Since };
		if (!status.Enabled) return new HealthRow("home-assistant", HealthLevel.Info, "off", data);
		if (!status.Configured) return new HealthRow("home-assistant", HealthLevel.Info, "not-configured", data);
		if (status.Connected) return new HealthRow("home-assistant", HealthLevel.Ok, "connected", data);
		string state = status.Failure switch
		{
			"user name or password refused" or "not authorised" => "refused",
			null => "connecting",
			_ => "unreachable",
		};
		return new HealthRow("home-assistant", HealthLevel.Info, state, data);
	}
}
