// Batch 2 tests (Linux-runnable): the MQTT wire format and a real connection to a fake broker on loopback, the Home
// Assistant bridge (topics, discovery, state, commands, the Health row), automations (defaults, cleaning, the one TV
// wake path, the controller watch), sleep after streaming, Vibepollo's logs and Wake-on-LAN readiness.
// docs/eternal/BATCH-2.md.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using GamingMode.Services;
using Playhub.Shared;

static class BatchTwoTests
{
	internal static void Run(Action<bool, string> Check)
	{
		DateTimeOffset now = new(2026, 11, 26, 23, 0, 0, TimeSpan.FromHours(-5));
		Wire(Check);
		LiveBroker(Check).GetAwaiter().GetResult();
		Bridge(Check);
		Automations(Check, now);
		StreamSleep(Check, now);
		Vibepollo(Check);
		WakeOnLan(Check);
		Health(Check, now);
	}

	private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

	private static void Wire(Action<bool, string> Check)
	{
		byte[] connect = MqttWire.ConnectPacket("core-pc", "core", "s3cret", 30, "core/pc/availability", "offline", true);
		// Fixed header 0x10, then "MQTT", level 4, flags: user + password + will retain + will + clean session = 0xE6.
		Check(connect[0] == 0x10 && Encoding.ASCII.GetString(connect, 4, 4) == "MQTT" && connect[8] == 4 && connect[9] == 0xE6 && connect[10] == 0 && connect[11] == 30, "mqtt: CONNECT header and flags");
		Check(Encoding.UTF8.GetString(connect).EndsWith("core\0\u0006s3cret", StringComparison.Ordinal), "mqtt: user name and password last, length-prefixed");
		byte[] anonymous = MqttWire.ConnectPacket("core-pc", null, "ignored", 30, null, null, false);
		Check(anonymous[9] == 0x02 && !Encoding.UTF8.GetString(anonymous).Contains("ignored"), "mqtt: no user, no password sent");
		byte[] publish = MqttWire.PublishPacket("a/b", "on", true);
		Check(Hex(publish) == "31070003612F626F6E", "mqtt: retained PUBLISH QoS 0");
		Check(Hex(MqttWire.SubscribePacket(1, new[] { "a/+" })) == "820800010003612F2B00", "mqtt: SUBSCRIBE");
		byte[] big = MqttWire.PublishPacket("t", new byte[200], false);
		Check(big[1] == 0xCB && big[2] == 0x01, "mqtt: remaining length over two bytes");
		Check(MqttWire.TryRead(big.AsSpan(0, 2), out _) is null && MqttWire.TryRead(big.AsSpan(0, 50), out _) is null, "mqtt: partial packets wait for more");
		MqttPacket? whole = MqttWire.TryRead(big, out int consumed);
		Check(whole is { Type: MqttWire.Publish } && consumed == big.Length, "mqtt: whole packet read");
		bool threw = false;
		try { MqttWire.TryRead(new byte[] { 0x30, 0xFF, 0xFF, 0xFF, 0x7F }, out _); } catch (MqttProtocolException) { threw = true; }
		Check(threw, "mqtt: an absurd length is refused");
		Check(MqttWire.ConnAckCode(new MqttPacket(MqttWire.ConnAck, 0, new byte[] { 0, 4 })) == 4 && MqttWire.Describe(4) == "user name or password refused", "mqtt: CONNACK code");
		MqttMessage qos1 = MqttWire.ReadPublish(new MqttPacket(MqttWire.Publish, 0x02, new byte[] { 0, 1, (byte)'x', 0, 7, (byte)'O', (byte)'N' }));
		Check(qos1 is { Topic: "x", Qos: 1, PacketId: 7, Text: "ON" }, "mqtt: incoming QoS 1 PUBLISH");
		Check(Hex(MqttWire.PubAckPacket(7)) == "40020007", "mqtt: PUBACK");
		Check(MqttWire.Matches("core/pc/cmd/+", "core/pc/cmd/sleep") && !MqttWire.Matches("core/pc/cmd/+", "core/pc/cmd/a/b") && MqttWire.Matches("a/#", "a/b/c"), "mqtt: filter matching");
		threw = false;
		try { MqttWire.PublishPacket("a/+", "x", false); } catch (ArgumentException) { threw = true; }
		Check(threw, "mqtt: wildcards refused in a topic name");
	}

	// A fake broker on 127.0.0.1: checks the CONNECT, answers CONNACK, takes the SUBSCRIBE, sends a command, and reads
	// what the client publishes.
	private static async Task LiveBroker(Action<bool, string> Check)
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		var received = new List<MqttPacket>();
		Task broker = Task.Run(async () =>
		{
			using TcpClient peer = await listener.AcceptTcpClientAsync();
			NetworkStream stream = peer.GetStream();
			var buffer = new List<byte>();
			byte[] chunk = new byte[4096];
			async Task<MqttPacket> Next()
			{
				while (true)
				{
					MqttPacket? packet = MqttWire.TryRead(buffer.ToArray(), out int used);
					if (packet is not null) { buffer.RemoveRange(0, used); return packet; }
					int read = await stream.ReadAsync(chunk);
					if (read <= 0) throw new IOException("closed");
					buffer.AddRange(chunk.AsSpan(0, read).ToArray());
				}
			}
			received.Add(await Next());
			await stream.WriteAsync(new byte[] { 0x20, 2, 0, 0 });
			received.Add(await Next());
			await stream.WriteAsync(new byte[] { 0x90, 3, 0, 1, 0 });
			byte[] topic = Encoding.ASCII.GetBytes("core/pc/cmd/tv");
			await stream.WriteAsync(new byte[] { 0x32, (byte)(2 + topic.Length + 2 + 2), 0, (byte)topic.Length }.Concat(topic).Concat(new byte[] { 0, 9 }).Concat(Encoding.ASCII.GetBytes("ON")).ToArray());
			received.Add(await Next()); // PUBACK
			received.Add(await Next()); // the client's PUBLISH
			received.Add(await Next()); // DISCONNECT
		});
		var options = new MqttConnection.Options("127.0.0.1", port, "core-pc", "core", "pw", 30, "core/pc/availability", "offline", true);
		(MqttConnection? connection, int? code, string? failure) = await MqttConnection.ConnectAsync(options, TimeSpan.FromSeconds(5), CancellationToken.None);
		Check(connection is not null && code == 0 && failure is null, "live: connected to the fake broker");
		await connection!.SubscribeAsync(new[] { "core/pc/cmd/+" }, CancellationToken.None);
		var got = new TaskCompletionSource<MqttMessage>();
		using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		Task<string> run = connection.RunAsync(message => { got.TrySetResult(message); return Task.CompletedTask; }, stop.Token);
		MqttMessage command = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Check(command is { Topic: "core/pc/cmd/tv", Text: "ON", Qos: 1 }, "live: a command arrives");
		await connection.PublishAsync("core/pc/state", "{\"game\":\"OFF\"}", true, CancellationToken.None);
		await connection.DisconnectAsync();
		await broker.WaitAsync(TimeSpan.FromSeconds(5));
		stop.Cancel();
		await run;
		await connection.DisposeAsync();
		listener.Stop();
		Check(received[0].Type == MqttWire.Connect && received[1].Type == MqttWire.Subscribe && received[2].Type == MqttWire.PubAck && Hex(received[2].Body) == "0009", "live: CONNECT, SUBSCRIBE, PUBACK in order");
		Check(received[3].Type == MqttWire.Publish && (received[3].Flags & 1) == 1 && MqttWire.ReadPublish(received[3]).Text == "{\"game\":\"OFF\"}" && received[4].Type == MqttWire.Disconnect, "live: retained state published, clean goodbye");

		// A refused login and a closed port fail soft.
		var refuse = new TcpListener(IPAddress.Loopback, 0);
		refuse.Start();
		int refusePort = ((IPEndPoint)refuse.LocalEndpoint).Port;
		Task refuser = Task.Run(async () => { using TcpClient peer = await refuse.AcceptTcpClientAsync(); byte[] skip = new byte[512]; await peer.GetStream().ReadAsync(skip); await peer.GetStream().WriteAsync(new byte[] { 0x20, 2, 0, 5 }); await Task.Delay(200); });
		var refused = await MqttConnection.ConnectAsync(options with { Port = refusePort }, TimeSpan.FromSeconds(5), CancellationToken.None);
		await refuser;
		refuse.Stop();
		Check(refused.Connection is null && refused.Code == 5 && refused.Failure == "not authorised", "live: a refused login is reported");
		var closed = await MqttConnection.ConnectAsync(options with { Port = refusePort }, TimeSpan.FromSeconds(3), CancellationToken.None);
		Check(closed.Connection is null && closed.Failure is not null && !closed.Failure.Contains("pw"), "live: nobody listening fails soft, no password in the reason");
	}

	private static void Bridge(Action<bool, string> Check)
	{
		Check(HomeAssistantBridgePolicy.NodeId("LIVING-ROOM PC") == "living-room_pc" && HomeAssistantBridgePolicy.NodeId("") == "core" && HomeAssistantBridgePolicy.NodeId("ÄÖ") == "core", "bridge: node id from the PC name");
		Check(HomeAssistantBridgePolicy.ClientId("living-room_pc_with_a_long_name").Length <= 23, "bridge: client id within MQTT 3.1.1's 23 characters");
		Check(HomeAssistantBridgePolicy.CleanHost(" serverv3 ") == "serverv3" && HomeAssistantBridgePolicy.CleanHost("192.168.1.109") == "192.168.1.109", "bridge: host names and addresses");
		Check(HomeAssistantBridgePolicy.CleanHost("mqtt://serverv3") is null && HomeAssistantBridgePolicy.CleanHost("user:pw@serverv3") is null && HomeAssistantBridgePolicy.CleanHost("") is null, "bridge: URLs and credentials in the host refused");
		Check(HomeAssistantBridgePolicy.CleanPort(0) == 1883 && HomeAssistantBridgePolicy.CleanPort(8883) == 8883, "bridge: port");

		var discovery = HomeAssistantBridgePolicy.Discovery("pc", "Core (PC)", "3.3.0", new[] { "game-start", "Bad Name!" });
		var topics = discovery.Select(item => item.Topic).ToList();
		Check(topics.Contains("homeassistant/binary_sensor/core_pc/game/config") && topics.Contains("homeassistant/button/core_pc/wake_tv/config")
			&& topics.Contains("homeassistant/switch/core_pc/keep_tv_out/config") && topics.Contains("homeassistant/button/core_pc/sleep/config")
			&& topics.Contains("homeassistant/sensor/core_pc/media/config") && topics.Contains("homeassistant/binary_sensor/core_pc/vr/config")
			&& topics.Contains("homeassistant/binary_sensor/core_pc/asleep/config") && topics.Contains("homeassistant/event/core_pc/automation/config"), "bridge: every entity is announced");
		Check(!discovery.Any(item => item.Topic.Contains("update") || item.Payload.Contains("update_steam")), "bridge: no Steam update entity");
		using (JsonDocument game = JsonDocument.Parse(discovery.Single(item => item.Topic.EndsWith("/game/config")).Payload))
			Check(game.RootElement.GetProperty("unique_id").GetString() == "core_pc_game" && game.RootElement.GetProperty("state_topic").GetString() == "core/pc/state"
				&& game.RootElement.GetProperty("availability_topic").GetString() == "core/pc/availability" && game.RootElement.GetProperty("device").GetProperty("manufacturer").GetString() == "Eternal", "bridge: discovery payload");
		using (JsonDocument asleep = JsonDocument.Parse(discovery.Single(item => item.Topic.EndsWith("/asleep/config")).Payload))
			Check(!asleep.RootElement.TryGetProperty("availability_topic", out _), "bridge: asleep keeps its value while the PC is offline");
		using (JsonDocument events = JsonDocument.Parse(discovery.Single(item => item.Topic.EndsWith("/automation/config")).Payload))
			Check(events.RootElement.GetProperty("event_types").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "game-start" }), "bridge: event types cleaned");

		var playing = new HaState { Game = true, GameAppId = 1145350, GameName = "Hades II", Media = "off", StreamMode = true, StreamDevice = "AYN Thor", GamingMode = true };
		using (JsonDocument state = JsonDocument.Parse(HomeAssistantBridgePolicy.StatePayload(playing with { Idle = HomeAssistantBridgePolicy.IsIdle(playing) })))
		{
			JsonElement root = state.RootElement;
			Check(root.GetProperty("game").GetString() == "ON" && root.GetProperty("game_name").GetString() == "Hades II" && root.GetProperty("app_id").GetUInt32() == 1145350
				&& root.GetProperty("stream_device").GetString() == "AYN Thor" && root.GetProperty("idle").GetString() == "OFF", "bridge: state while playing");
		}
		var idle = new HaState { GamingMode = true, Media = "open", MediaApp = "Stremio" };
		Check(HomeAssistantBridgePolicy.IsIdle(idle) && !HomeAssistantBridgePolicy.IsIdle(idle with { Media = "playing" }) && !HomeAssistantBridgePolicy.IsIdle(idle with { GamingMode = false }), "bridge: idle");
		using (JsonDocument state = JsonDocument.Parse(HomeAssistantBridgePolicy.StatePayload(idle with { Media = "playing" })))
			Check(state.RootElement.GetProperty("media_playing").GetString() == "ON" && state.RootElement.GetProperty("game_name").GetString() == "none" && state.RootElement.GetProperty("app_id").ValueKind == JsonValueKind.Null, "bridge: movie playing");

		Check(HomeAssistantBridgePolicy.ParseCommand("pc", "core/pc/cmd/wake_tv", "PRESS") == new HaCommand(HaCommandKind.WakeTv), "bridge: wake TV command");
		Check(HomeAssistantBridgePolicy.ParseCommand("pc", "core/pc/cmd/keep_tv_out", " on ") == new HaCommand(HaCommandKind.KeepTvOut, true), "bridge: Stream mode switch");
		Check(HomeAssistantBridgePolicy.ParseCommand("pc", "core/pc/cmd/update_steam", "PRESS") is null && HomeAssistantBridgePolicy.ParseCommand("pc", "core/other/cmd/sleep", "PRESS") is null
			&& HomeAssistantBridgePolicy.ParseCommand("pc", "core/pc/cmd/sleep", "NOW") is null, "bridge: unknown commands, other PCs and odd payloads ignored");
		Check(HomeAssistantBridgePolicy.ParseSleepMode("on") == true && HomeAssistantBridgePolicy.ParseSleepMode("off") == false && HomeAssistantBridgePolicy.ParseSleepMode("unavailable") is null, "bridge: Aincrad's sleep mode");
		Check(HomeAssistantBridgePolicy.SleepRefusal(false, true, false) == "a stream is running" && HomeAssistantBridgePolicy.SleepRefusal(true, false, false) == "a game is running" && HomeAssistantBridgePolicy.SleepRefusal(false, false, false) is null, "bridge: Sleep PC only when nobody plays");
		Check(HomeAssistantBridgePolicy.EventPayload("game-start") == "{\"event_type\":\"game-start\"}", "bridge: event payload");

		Check(HomeAssistantBridgePolicy.Row(new HaBridgeStatus(false, true, false, "serverv3", null, null, true)).State == "off", "bridge row: off");
		Check(HomeAssistantBridgePolicy.Row(new HaBridgeStatus(true, true, true, "serverv3", null, null, true)) is { Level: HealthLevel.Ok, State: "connected" }, "bridge row: connected in steel");
		Check(HomeAssistantBridgePolicy.Row(new HaBridgeStatus(true, true, false, "serverv3", "user name or password refused", null, true)) is { Level: HealthLevel.Info, State: "refused" }, "bridge row: refused login is amber");
		Check(HomeAssistantBridgePolicy.Row(new HaBridgeStatus(true, true, false, "serverv3", "timeout", null, true)).State == "unreachable", "bridge row: unreachable");
	}

	private static void Automations(Action<bool, string> Check, DateTimeOffset now)
	{
		List<CoreAutomationRule> defaults = CoreAutomations.Defaults();
		Check(defaults.Where(rule => rule.Enabled).Select(rule => rule.Id).SequenceEqual(new[] { "controller-tv" }) && defaults.Single(rule => rule.Enabled).Actions.SequenceEqual(new[] { CoreAutomations.TvWake }), "automations: only controller -> TV is on by default");
		Check(CoreAutomations.Normalize(null).Count == defaults.Count && CoreAutomations.Normalize(new List<CoreAutomationRule?>()).Count == defaults.Count, "automations: nothing saved means the defaults");
		var messy = new List<CoreAutomationRule?>
		{
			null,
			new() { Id = "x", Enabled = true, Trigger = "reboot", Actions = { CoreAutomations.TvWake } },
			new() { Id = "a", Enabled = true, Trigger = CoreAutomations.StreamOn, Actions = { CoreAutomations.TvWake, CoreAutomations.HaEvent } },
			new() { Id = "a", Enabled = true, Trigger = CoreAutomations.GameStop, Actions = { CoreAutomations.StreamStop, "format-disk", CoreAutomations.StreamStart, CoreAutomations.StreamStart } },
			new() { Id = "b", Enabled = true, Trigger = CoreAutomations.Wake, Actions = { "nothing" } },
			new() { Id = "c", Enabled = false, Trigger = CoreAutomations.GameStart, Actions = { CoreAutomations.HaEvent }, Event = "Lights Down" },
		};
		List<CoreAutomationRule> clean = CoreAutomations.Normalize(messy);
		Check(clean.Count == 3, "automations: unknown triggers, unknown actions and empty rules dropped");
		Check(clean[0] is { Id: "a", Trigger: "stream-on", Event: "stream-on" } && clean[0].Actions.SequenceEqual(new[] { CoreAutomations.HaEvent }), "automations: no TV wake when a stream starts; event named after the trigger");
		Check(clean[1].Id == "game-stop-1" && clean[1].Actions.SequenceEqual(new[] { CoreAutomations.StreamStart }), "automations: duplicate id renamed; start and stop never together");
		Check(clean[2].Event == "lights-down" && !clean[2].Enabled, "automations: event name cleaned, disabled kept");
		Check(CoreAutomations.EventTypes(clean).SequenceEqual(new[] { "stream-on", "lights-down" }), "automations: Home Assistant event types");
		Check(CoreAutomations.Normalize(Enumerable.Range(0, 40).Select(index => (CoreAutomationRule?)new CoreAutomationRule { Id = "r" + index, Trigger = CoreAutomations.Wake, Actions = { CoreAutomations.SoundRestore } })).Count == CoreAutomations.MaxRules, "automations: at most 20 rules");
		Check(!CoreAutomations.WakeTvOnResume(false, defaults) && CoreAutomations.WakeTvOnResume(true, defaults)
			&& CoreAutomations.WakeTvOnResume(false, new[] { new CoreAutomationRule { Enabled = true, Trigger = CoreAutomations.Wake, Actions = { CoreAutomations.TvWake } } }), "automations: wake -> TV uses the resume path");
		Check(CoreAutomations.Describe(clean[2]) == "when game start: Home Assistant event \"lights-down\"", "automations: log line");

		Check(TvWakeCoordinator.ShouldSend(null, now) && !TvWakeCoordinator.ShouldSend(now - TimeSpan.FromSeconds(20), now) && TvWakeCoordinator.ShouldSend(now - TimeSpan.FromMinutes(2), now), "TV wake: once per 90 seconds, whoever asks");

		var (first, state) = ControllerWatch.Observe(ControllerWatchState.Empty, new[] { "pad-a" }, now, quiet: false);
		Check(first.Count == 0, "controllers: the first look only learns");
		(IReadOnlyList<string> joined, state) = ControllerWatch.Observe(state, new[] { "pad-a", "steam-ctrl" }, now.AddSeconds(5), quiet: false);
		Check(joined.SequenceEqual(new[] { "steam-ctrl" }), "controllers: a new one connects");
		(_, state) = ControllerWatch.Observe(state, new[] { "pad-a" }, now.AddSeconds(10), quiet: false);
		(joined, state) = ControllerWatch.Observe(state, new[] { "pad-a", "steam-ctrl" }, now.AddSeconds(40), quiet: false);
		Check(joined.Count == 0, "controllers: a quick reconnect is not a new connection");
		(_, state) = ControllerWatch.Observe(state, new[] { "pad-a" }, now.AddSeconds(50), quiet: false);
		(joined, state) = ControllerWatch.Observe(state, new[] { "pad-a", "steam-ctrl" }, now.AddMinutes(5), quiet: false);
		Check(joined.SequenceEqual(new[] { "steam-ctrl" }), "controllers: back after a while counts again");
		(_, state) = ControllerWatch.Observe(state, new[] { "pad-a" }, now.AddMinutes(10), quiet: false);
		(joined, _) = ControllerWatch.Observe(state, new[] { "pad-a", "steam-ctrl" }, now.AddMinutes(20), quiet: true);
		Check(joined.Count == 0, "controllers: nothing during the quiet minute after a wake");
	}

	private static void StreamSleep(Action<bool, string> Check, DateTimeOffset now)
	{
		StreamSleepInputs Input(DateTimeOffset at, bool streaming = false, DateTimeOffset? input = null) => new()
		{
			Now = at, Enabled = true, DelayMinutes = 10, InGamingMode = true, Streaming = streaming, Source = streaming ? "Apollo" : null, LastLocalInput = input ?? now.AddHours(-1),
		};
		var state = new StreamSleepState();
		StreamSleepDecision decision = StreamSleepPolicy.Decide(Input(now, streaming: true), state);
		Check(decision.Action == StreamSleepAction.Nothing && decision.Next.WasStreaming, "stream sleep: streaming is noted");
		decision = StreamSleepPolicy.Decide(Input(now.AddSeconds(5)), decision.Next);
		Check(decision.Action == StreamSleepAction.Armed && decision.SleepAt == now.AddSeconds(5).AddMinutes(10) && decision.Reason.Contains("Apollo"), "stream sleep: the stream ended, the timer starts");
		StreamSleepState armed = decision.Next;
		Check(StreamSleepPolicy.Decide(Input(now.AddMinutes(5), input: now.AddSeconds(8)), armed).Action == StreamSleepAction.Nothing, "stream sleep: input right after the disconnect is the stream's own");
		Check(StreamSleepPolicy.Decide(Input(now.AddMinutes(5), input: now.AddMinutes(4)), armed) is { Action: StreamSleepAction.Disarmed, Reason: "someone is using Core here" }, "stream sleep: local input cancels");
		Check(StreamSleepPolicy.Decide(Input(now.AddMinutes(5), streaming: true), armed).Action == StreamSleepAction.Disarmed, "stream sleep: the stream coming back cancels");
		decision = StreamSleepPolicy.Decide(Input(now.AddMinutes(9).AddSeconds(10)), armed);
		Check(decision.Action == StreamSleepAction.Warn && decision.Next.Warned, "stream sleep: one minute's warning");
		Check(StreamSleepPolicy.Decide(Input(now.AddMinutes(9).AddSeconds(20)), decision.Next).Action == StreamSleepAction.Nothing, "stream sleep: warned once");
		decision = StreamSleepPolicy.Decide(Input(now.AddMinutes(10).AddSeconds(5)), decision.Next);
		Check(decision.Action == StreamSleepAction.Sleep && decision.Next.ArmedAt is null && decision.Next.LastSleepAt is not null, "stream sleep: sleeps after the delay");
		Check(StreamSleepPolicy.Decide(Input(now.AddMinutes(11)) with { Busy = true }, armed).Action == StreamSleepAction.Wait, "stream sleep: waits while Core is busy");
		Check(StreamSleepPolicy.Decide(Input(now.AddHours(2)), armed).Action == StreamSleepAction.Disarmed, "stream sleep: a stale timer is dropped");
		Check(StreamSleepPolicy.Decide(Input(now.AddMinutes(3)) with { Enabled = false }, armed).Action == StreamSleepAction.Disarmed
			&& StreamSleepPolicy.Decide(Input(now.AddMinutes(3)) with { InGamingMode = false }, armed).Action == StreamSleepAction.Disarmed, "stream sleep: off or outside Gaming Mode cancels");
		Check(StreamSleepPolicy.Decide(Input(now), new StreamSleepState()).Action == StreamSleepAction.Nothing, "stream sleep: nothing without a stream first");
		Check(StreamSleepPolicy.CleanDelay(0) == 10 && StreamSleepPolicy.CleanDelay(30) == 30 && StreamSleepPolicy.CleanDelay(500) == 10, "stream sleep: delay bounds");
		Check(StreamSleepPolicy.Row(true, 10, null, null) is { Level: HealthLevel.Ok, State: "ready" } && StreamSleepPolicy.Row(true, 10, now, null).State == "counting" && StreamSleepPolicy.Row(false, 10, null, null).State == "off", "stream sleep: Health row");
	}

	private static void Vibepollo(Action<bool, string> Check)
	{
		Check(VibepolloLogs.IsSessionLog("sunshine-20261126-230501-042.log") && !VibepolloLogs.IsSessionLog("sunshine-20261126-230501-042.log.1") && !VibepolloLogs.IsSessionLog("sunshine.log"), "vibepollo: session log names");
		DateTime t = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
		Check(VibepolloLogs.Newest(new[] { ("a/sunshine-20261125-100000-000.log", t), ("a/sunshine-20261126-100000-000.log", t.AddHours(1)), ("a/sunshine-20261126-100000-000.log.1", t.AddHours(2)) }) == "a/sunshine-20261126-100000-000.log", "vibepollo: the newest run's log");

		// A session log that rolls over in place keeps the session's state; Sunshine's own log starts over.
		string folder = Path.Combine(Path.GetTempPath(), "core-batch2-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try
		{
			string session = Path.Combine(folder, "sunshine-20261126-230501-042.log");
			File.WriteAllText(session, "﻿Info: CLIENT CONNECTED\n");
			var tail = new SunshineLogTail(session);
			Check(tail.Poll().Streaming, "vibepollo: connected");
			File.WriteAllText(session, "Info: something\n");
			Check(tail.Poll().Streaming, "vibepollo: still streaming after a roll-over");
			File.AppendAllText(session, "Info: CLIENT DISCONNECTED\n");
			Check(!tail.Poll().Streaming, "vibepollo: disconnect read from the new piece");
			string plain = Path.Combine(folder, "sunshine.log");
			File.WriteAllText(plain, "Info: CLIENT CONNECTED\nInfo: padding padding padding\n");
			var sunshine = new SunshineLogTail(plain);
			Check(sunshine.Poll().Streaming, "sunshine: connected");
			File.WriteAllText(plain, "Info: x\n");
			Check(!sunshine.Poll().Streaming, "sunshine: a new file starts over");
		}
		finally { Directory.Delete(folder, true); }
	}

	private static void WakeOnLan(Action<bool, string> Check)
	{
		var good = new NetworkAdapterWake("Realtek 2.5GbE", true, true, true, false, true, "A8:23:FE:01:02:03");
		Check(WakeOnLanPolicy.Row(new WakeOnLanInputs(new[] { good }, false)) is { Level: HealthLevel.Ok, State: "ready" } row && (string?)row.Data["mac"] == "A8:23:FE:01:02:03", "wol: ready with the address");
		HealthRow check = WakeOnLanPolicy.Row(new WakeOnLanInputs(new[] { good with { MagicPacket = false, PowerManagementOff = true } }, true));
		Check(check is { Level: HealthLevel.Info, State: "check" } && ((List<string>)check.Data["problems"]!).SequenceEqual(new[] { "magic-packet", "power-management" }) && (bool?)check.Data["fastStartup"] == true, "wol: what to check, in amber");
		Check(WakeOnLanPolicy.Row(new WakeOnLanInputs(new[] { new NetworkAdapterWake("Wi-Fi", false, true, null, null, null, null) }, null)).State == "wifi-only", "wol: Wi-Fi only");
		Check(WakeOnLanPolicy.Row(new WakeOnLanInputs(null, null)).Level == HealthLevel.Unknown, "wol: unreadable is unknown");
		Check(WakeOnLanPolicy.MagicPacket("1") == true && WakeOnLanPolicy.MagicPacket("0") == false && WakeOnLanPolicy.MagicPacket(null) is null, "wol: *WakeOnMagicPacket");
		Check(WakeOnLanPolicy.PowerManagementOff(24) == true && WakeOnLanPolicy.PowerManagementOff(0) == false && WakeOnLanPolicy.PowerManagementOff("x") is null, "wol: PnPCapabilities");
		Check(WakeOnLanPolicy.WakeArmed("Realtek 2.5GbE\r\nHID Keyboard Device\r\n").Count == 2 && WakeOnLanPolicy.WakeArmed("NONE\r\n").Count == 0, "wol: powercfg's wake-armed list");
	}

	private static void Health(Action<bool, string> Check, DateTimeOffset now)
	{
		var rows = new List<HealthRow>
		{
			HomeAssistantBridgePolicy.Row(new HaBridgeStatus(true, true, true, "serverv3", null, null, true)),
			StreamSleepPolicy.Row(true, 10, null, null),
			WakeOnLanPolicy.Row(new WakeOnLanInputs(null, null)),
		};
		HealthReport report = CoreHealthPolicy.Evaluate(new CoreHealthInputs { Now = now, ServerRead = ServerStatusRead.NotConfigured, CoreVersion = "3.3.0", BatchTwoRows = rows });
		List<string> ids = report.Rows.Select(row => row.Id).ToList();
		int streaming = ids.IndexOf("streaming");
		Check(ids.Skip(streaming + 1).Take(3).SequenceEqual(new[] { "stream-sleep", "wake-on-lan", "home-assistant" }), "health: batch 2 rows after Streaming, in order");
		Check(report.Attention == 0, "health: batch 2 rows never need attention");
	}
}
