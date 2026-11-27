// Jarvis tests (Linux-runnable, docs/eternal/JARVIS.md): settings cleaning, combo parsing, the session history, the
// security guard, request building for Home Assistant and the Claude API, answer parsing against fakes (an HTTP handler
// standing in for Home Assistant and the Claude API), the spoiler flow, the speech routes and audio conversion, the
// Wyoming framing against a fake Wyoming server on loopback, the microphone choice, the screenshot size and the Health row.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GamingMode.Services;
using Playhub.Shared;

static class JarvisTests
{
	internal static void Run(Action<bool, string> Check)
	{
		Config(Check);
		Combos(Check);
		History(Check);
		Guard(Check);
		Requests(Check);
		FakeHomeAssistant(Check).GetAwaiter().GetResult();
		FakeClaude(Check).GetAwaiter().GetResult();
		Spoiler(Check);
		Speech(Check);
		WyomingFraming(Check).GetAwaiter().GetResult();
		FakeWyomingServer(Check).GetAwaiter().GetResult();
		Mic(Check);
		ScreenAndHealth(Check);
	}

	private static void Config(Action<bool, string> Check)
	{
		Check(JarvisConfig.HaUrl(null) == "http://serverv3:8123" && JarvisConfig.HaUrl(" https://serverv3.tail1234.ts.net/ ") == "https://serverv3.tail1234.ts.net", "jarvis: Home Assistant URL default and trailing slash");
		Check(JarvisConfig.CleanBaseUrl("http://user:pw@serverv3:8123") is null && JarvisConfig.CleanBaseUrl("ftp://serverv3") is null && JarvisConfig.CleanBaseUrl("http://serverv3:8123/api") is null && JarvisConfig.CleanBaseUrl("http://serverv3:8123/?x=1") is null, "jarvis: user info, other schemes, paths and queries refused");
		Check(JarvisConfig.CleanBaseUrl("https://api.openai.com/v1/", allowPath: true) == "https://api.openai.com/v1", "jarvis: a path where one is allowed");
		Check(JarvisConfig.CleanEntity("conversation.jarvis", "conversation") == "conversation.jarvis" && JarvisConfig.CleanEntity("tts.piper", "conversation") is null && JarvisConfig.CleanEntity("conversation.ja rvis", "conversation") is null, "jarvis: entity ids by domain");
		Check(JarvisConfig.Model(null) == "claude-haiku-5-5" && JarvisConfig.Model("claude-sonnet-5-5") == "claude-sonnet-5-5" && JarvisConfig.Model("bad model!") == "claude-haiku-5-5", "jarvis: model default is claude-haiku-5-5");
		Check(JarvisConfig.CleanBackend("claude") == "claude" && JarvisConfig.CleanBackend("anything") == "home-assistant", "jarvis: backend defaults to Home Assistant");
		Check(JarvisConfig.CleanText("  hi\u0007 there\r\nsir\t ") == "hi there \nsir" && JarvisConfig.CleanText(new string('a', 3000)).Length == JarvisConfig.MaxPromptChars, "jarvis: prompt text cleaned and capped");
	}

	private static void Combos(Action<bool, string> Check)
	{
		JarvisCombo? combo = JarvisCombos.Parse("r5 + l5");
		Check(combo is { Text: "L5+R5", Lower: (1UL << 15) | (1UL << 16), Upper: 0 }, "combo: L5+R5 normalised, lower word bits 15 and 16");
		combo = JarvisCombos.Parse("L4+R4");
		Check(combo is { Lower: 0, Upper: (1UL << 9) | (1UL << 10) }, "combo: L4+R4 in the upper word (bits 9 and 10)");
		Check(JarvisCombos.Parse("LB+RB+View")?.Text == "L1+R1+SELECT", "combo: Xbox names as aliases");
		Check(JarvisCombos.Parse("A") is null && JarvisCombos.Parse("A+B+X+Y+L1") is null && JarvisCombos.Parse("A+A") is null && JarvisCombos.Parse("STEAM+A") is null && JarvisCombos.Parse("QAM+L4") is null && JarvisCombos.Parse("L9+R9") is null, "combo: one button, five buttons, repeats, Steam's own buttons and unknown names refused");
		Check(JarvisCombos.Clean("", "L5+R5") is null && JarvisCombos.Clean("junk", "L5+R5") == "L5+R5" && JarvisCombos.Clean(null, "L4+R4") == "L4+R4", "combo: empty is off, junk falls back to the default");
		Check(JarvisCombos.Overlap(JarvisCombos.Parse("L4+R4")!, JarvisCombos.Parse("L4+R4+A")!) && !JarvisCombos.Overlap(JarvisCombos.Parse("L4+R4")!, JarvisCombos.Parse("L4+L5")!), "combo: containment is a clash, sharing one button is not");
		JarvisCombosView view = JarvisCombosPolicy.Clean(null, null, null);
		Check(view == new JarvisCombosView("L5+R5", "L4+R4", "L4+L5"), "combo: defaults on the four grips, no clash");
		view = JarvisCombosPolicy.Clean("L4+R4", "L4+R4+A", "");
		Check(view == new JarvisCombosView("L4+R4", null, null), "combo: a clashing combo is turned off, an empty one stays off");
	}

	private static void History(Action<bool, string> Check)
	{
		var history = new JarvisHistory();
		DateTimeOffset now = new(2026, 11, 26, 23, 0, 0, TimeSpan.Zero);
		for (int i = 0; i < 12; i++) history.Add(new JarvisTurn("user", "q" + i), new JarvisTurn("assistant", "a" + i), i == 0 ? "conv-1" : null, now.AddMinutes(i));
		IReadOnlyList<JarvisTurn> turns = history.Turns(now.AddMinutes(12));
		Check(turns.Count == JarvisHistory.MaxTurns * 2 && turns[0].Text == "q4" && turns[^1].Text == "a11", "history: only the last 8 turns kept");
		Check(history.CurrentConversationId(now.AddMinutes(12)) == "conv-1", "history: Home Assistant's conversation id kept");
		Check(history.Turns(now.AddMinutes(11) + JarvisHistory.IdleExpiry + TimeSpan.FromSeconds(1)).Count == 0 && history.ConversationId is null, "history: forgotten after half an hour of quiet");
		history.Add(new JarvisTurn("user", "q"), new JarvisTurn("assistant", "a"), "conv-2", now);
		history.Clear();
		Check(history.Count == 0 && history.ConversationId is null, "history: cleared on request");
	}

	private static void Guard(Action<bool, string> Check)
	{
		string ha = JarvisConfig.BackendHomeAssistant, claude = JarvisConfig.BackendClaude;
		foreach (string text in new[] { "unlock the front door", "Open the garage", "disarm the alarm", "arm the security system", "Lock everything", "call request_unlock", "sblocca la serratura", "Garagentor öffnen" })
			Check(JarvisGuard.IsSecurityRequest(text, ha), $"guard: refused for Home Assistant: {text}");
		Check(!JarvisGuard.IsSecurityRequest("turn the living room down to 30%", ha) && !JarvisGuard.IsSecurityRequest("what's on my calendar tomorrow?", ha), "guard: ordinary house requests pass");
		Check(JarvisGuard.IsSecurityRequest("how do I unlock the next level", ha) && !JarvisGuard.IsSecurityRequest("how do I unlock the next level", claude), "guard: game talk passes only on the Claude backend, which controls nothing");
		Check(JarvisGuard.IsSecurityRequest("unlock the front door", claude), "guard: house security refused on the Claude backend too");
		Check(JarvisGuard.Refusal.Contains("phone", StringComparison.Ordinal), "guard: the refusal points to the phone");
	}

	private static void Requests(Action<bool, string> Check)
	{
		JsonNode ha = JsonNode.Parse(JarvisRequests.HaConversation("lights down", "conversation.jarvis", "abc"))!;
		Check((string?)ha["text"] == "lights down" && (string?)ha["agent_id"] == "conversation.jarvis" && (string?)ha["conversation_id"] == "abc" && (string?)ha["language"] == "en", "request: Home Assistant conversation body");
		Check(JsonNode.Parse(JarvisRequests.HaConversation("hi", null, null))!.AsObject().Count == 2, "request: no agent or conversation id when none");
		var history = new List<JarvisTurn> { new("assistant", "stray"), new("user", "hello"), new("assistant", "Good evening, sir.") };
		byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xD9 };
		JsonNode claude = JsonNode.Parse(JarvisRequests.Claude("claude-haiku-5-5", JarvisPrompts.Persona, history, "what now?", jpeg, JarvisRequests.ScreenMaxTokens))!;
		JsonArray messages = claude["messages"]!.AsArray();
		Check((string?)claude["model"] == "claude-haiku-5-5" && (int?)claude["max_tokens"] == 600 && ((string?)claude["system"])!.Contains("Mr. Vonn", StringComparison.Ordinal), "request: Claude body with the persona as system");
		Check(messages.Count == 3 && (string?)messages[0]!["role"] == "user" && (string?)messages[1]!["role"] == "assistant", "request: history starts with the user (a leading assistant turn dropped)");
		JsonArray content = messages[2]!["content"]!.AsArray();
		Check((string?)content[0]!["type"] == "image" && (string?)content[0]!["source"]!["media_type"] == "image/jpeg" && (string?)content[0]!["source"]!["data"] == Convert.ToBase64String(jpeg) && (string?)content[1]!["text"] == "what now?", "request: image block first, then the question");
		Check(JarvisRequests.SttHeader() == "format=wav; codec=pcm; sample_rate=16000; bit_rate=16; channel=1; language=en", "request: Home Assistant speech-to-text header");
		Check(JarvisRequests.TtsPath("{\"url\":\"http://x/api/tts_proxy/abc.wav\",\"path\":\"/api/tts_proxy/abc.wav\"}") == "/api/tts_proxy/abc.wav" && JarvisRequests.TtsPath("{\"path\":\"/api/../secret\"}") is null && JarvisRequests.TtsPath("{\"path\":\"http://evil/x\"}") is null, "request: only a tts_proxy path is followed");
		Check(JarvisPrompts.Persona.Contains("cannot unlock doors", StringComparison.Ordinal) && JarvisPrompts.ScreenInstructions(true).Contains("NUDGE:", StringComparison.Ordinal) && !JarvisPrompts.ScreenInstructions(false).Contains("NUDGE:", StringComparison.Ordinal), "prompts: persona keeps the safety rule; the nudge only in spoiler-safe mode");
	}

	// An HttpMessageHandler standing in for Home Assistant and the Claude API: records requests, answers from a script.
	private sealed class FakeServer : HttpMessageHandler
	{
		internal readonly List<(HttpMethod Method, string Url, string? Authorization, string? ApiKey, string? Speech, string Body)> Seen = new();
		internal Func<HttpRequestMessage, (HttpStatusCode, string)> Answer = _ => (HttpStatusCode.OK, "{}");
		internal bool Fail;
		internal TimeSpan Delay = TimeSpan.Zero;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
			Seen.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(),
				request.Headers.TryGetValues("x-api-key", out var keys) ? keys.First() : null,
				request.Headers.TryGetValues("X-Speech-Content", out var speech) ? speech.First() : null, body));
			if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
			if (Fail) throw new HttpRequestException("refused");
			(HttpStatusCode status, string text) = Answer(request);
			return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
		}
	}

	private static async Task FakeHomeAssistant(Action<bool, string> Check)
	{
		var fake = new FakeServer();
		using var client = new JarvisClient(fake);
		fake.Answer = _ => (HttpStatusCode.OK, "{\"response\":{\"speech\":{\"plain\":{\"speech\":\"Done. Living room at thirty percent.\",\"extra_data\":null}},\"card\":{},\"language\":\"en\",\"response_type\":\"action_done\",\"data\":{\"targets\":[],\"success\":[],\"failed\":[]}},\"conversation_id\":\"01JABC\",\"continue_conversation\":false}");
		JarvisReply reply = await client.HaConverseAsync("http://serverv3:8123", "tok-123", "conversation.jarvis", "living room to 30%", null, TimeSpan.FromSeconds(5));
		var seen = fake.Seen[^1];
		Check(reply is { Ok: true, Text: "Done. Living room at thirty percent.", ConversationId: "01JABC" }, "ha fake: speech and conversation id parsed");
		Check(seen.Method == HttpMethod.Post && seen.Url == "http://serverv3:8123/api/conversation/process" && seen.Authorization == "Bearer tok-123" && seen.Body.Contains("\"agent_id\":\"conversation.jarvis\"", StringComparison.Ordinal), "ha fake: POST /api/conversation/process with the bearer token and the agent");
		fake.Answer = _ => (HttpStatusCode.OK, "{\"response\":{\"speech\":{\"plain\":{\"speech\":\"Sorry, I couldn't understand that\"}},\"response_type\":\"error\",\"data\":{\"code\":\"no_intent_match\"}},\"conversation_id\":\"01JABC\"}");
		reply = await client.HaConverseAsync("http://serverv3:8123", "tok", null, "blah", "01JABC", TimeSpan.FromSeconds(5));
		Check(reply is { Ok: false, Error: "agent-error", Text: "Sorry, I couldn't understand that" } && fake.Seen[^1].Body.Contains("\"conversation_id\":\"01JABC\"", StringComparison.Ordinal), "ha fake: an error answer keeps its speech, the conversation continues");
		fake.Answer = _ => (HttpStatusCode.Unauthorized, "401: Unauthorized");
		Check((await client.HaConverseAsync("http://serverv3:8123", "bad", null, "hi", null, TimeSpan.FromSeconds(5))).Error == "token-refused", "ha fake: 401 is a refused token");
		fake.Answer = _ => (HttpStatusCode.OK, "not json");
		Check((await client.HaConverseAsync("http://serverv3:8123", "tok", null, "hi", null, TimeSpan.FromSeconds(5))).Error == "bad-answer", "ha fake: garbage is a bad answer, not a crash");
		fake.Fail = true;
		Check((await client.HaConverseAsync("http://serverv3:8123", "tok", null, "hi", null, TimeSpan.FromSeconds(5))).Error == "unreachable", "ha fake: connection refused is unreachable");
		fake.Fail = false;
		fake.Delay = TimeSpan.FromSeconds(2);
		Check((await client.HaConverseAsync("http://serverv3:8123", "tok", null, "hi", null, TimeSpan.FromMilliseconds(150))).Error == "timeout", "ha fake: time-boxed");
		fake.Delay = TimeSpan.Zero;
		// Test connection: /api/ then the agent's state.
		fake.Answer = request => request.RequestUri!.AbsolutePath == "/api/" ? (HttpStatusCode.OK, "{\"message\":\"API running.\"}") : (HttpStatusCode.NotFound, "{\"message\":\"Entity not found.\"}");
		Check((await client.HaTestAsync("http://serverv3:8123", "tok", "conversation.jarvis", TimeSpan.FromSeconds(5))).Error == "agent-missing", "ha fake: test finds a missing agent");
		Check(fake.Seen[^1].Url == "http://serverv3:8123/api/states/conversation.jarvis", "ha fake: test reads the agent's state (free, no conversation)");
		fake.Answer = request => (HttpStatusCode.OK, request.RequestUri!.AbsolutePath == "/api/" ? "{\"message\":\"API running.\"}" : "{\"state\":\"unknown\"}");
		Check((await client.HaTestAsync("http://serverv3:8123", "tok", "conversation.jarvis", TimeSpan.FromSeconds(5))).Ok, "ha fake: test passes");
		// Speech to text through Home Assistant.
		fake.Answer = _ => (HttpStatusCode.OK, "{\"text\":\" what's the weather \",\"result\":\"success\"}");
		reply = await client.HaTranscribeAsync("http://serverv3:8123", "tok", "stt.faster_whisper", new byte[3200], TimeSpan.FromSeconds(5));
		Check(reply is { Ok: true, Text: "what's the weather" } && fake.Seen[^1].Url == "http://serverv3:8123/api/stt/stt.faster_whisper" && fake.Seen[^1].Speech == JarvisRequests.SttHeader(), "ha fake: speech-to-text with the X-Speech-Content header");
		fake.Answer = _ => (HttpStatusCode.OK, "{\"text\":null,\"result\":\"error\"}");
		Check((await client.HaTranscribeAsync("http://serverv3:8123", "tok", "stt.faster_whisper", new byte[2], TimeSpan.FromSeconds(5))).Error == "not-understood", "ha fake: speech-to-text error result");
		// Text to speech: tts_get_url, then the audio from the path on Core's own base URL.
		fake.Answer = request => request.RequestUri!.AbsolutePath == "/api/tts_get_url"
			? (HttpStatusCode.OK, "{\"url\":\"http://homeassistant.local:8123/api/tts_proxy/x.wav\",\"path\":\"/api/tts_proxy/x.wav\"}")
			: (HttpStatusCode.OK, "RIFFWAVE");
		(byte[]? audio, string? error) = await client.HaSpeakAsync("http://serverv3:8123", "tok", "tts.piper", "Good evening, sir.", TimeSpan.FromSeconds(5));
		Check(audio is not null && error is null && fake.Seen[^1].Url == "http://serverv3:8123/api/tts_proxy/x.wav" && fake.Seen[^2].Body.Contains("\"engine_id\":\"tts.piper\"", StringComparison.Ordinal), "ha fake: Piper through tts_get_url, audio fetched from Core's base URL");
	}

	private static async Task FakeClaude(Action<bool, string> Check)
	{
		var fake = new FakeServer();
		using var client = new JarvisClient(fake);
		fake.Answer = _ => (HttpStatusCode.OK, "{\"id\":\"msg_1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Good evening, \"},{\"type\":\"text\",\"text\":\"sir.\"}],\"stop_reason\":\"end_turn\"}");
		JarvisReply reply = await client.ClaudeAsync("sk-ant-test", JarvisRequests.Claude("claude-haiku-5-5", JarvisPrompts.Persona, Array.Empty<JarvisTurn>(), "hello"), TimeSpan.FromSeconds(5));
		var seen = fake.Seen[^1];
		Check(reply is { Ok: true, Text: "Good evening, sir." }, "claude fake: text blocks joined");
		Check(seen.Url == JarvisRequests.AnthropicUrl && seen.ApiKey == "sk-ant-test" && seen.Authorization is null, "claude fake: x-api-key header, no bearer");
		fake.Answer = _ => (HttpStatusCode.Unauthorized, "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}");
		Check((await client.ClaudeAsync("bad", "{}", TimeSpan.FromSeconds(5))).Error == "key-refused", "claude fake: a refused key");
		fake.Answer = _ => (HttpStatusCode.TooManyRequests, "{\"type\":\"error\",\"error\":{\"type\":\"rate_limit_error\",\"message\":\"slow down\"}}");
		Check((await client.ClaudeAsync("k", "{}", TimeSpan.FromSeconds(5))).Error == "rate-limited", "claude fake: rate limited");
		fake.Answer = _ => ((HttpStatusCode)529, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}");
		Check((await client.ClaudeAsync("k", "{}", TimeSpan.FromSeconds(5))).Error == "overloaded", "claude fake: overloaded");
		fake.Answer = request => request.RequestUri!.AbsolutePath == "/v1/models/claude-haiku-5-5" ? (HttpStatusCode.OK, "{\"id\":\"claude-haiku-5-5\",\"type\":\"model\"}") : (HttpStatusCode.NotFound, "{\"type\":\"error\",\"error\":{\"type\":\"not_found_error\"}}");
		Check((await client.ClaudeTestAsync("k", "claude-haiku-5-5", TimeSpan.FromSeconds(5))).Ok && fake.Seen[^1].Method == HttpMethod.Get, "claude fake: test reads the model (no billed request)");
		Check((await client.ClaudeTestAsync("k", "claude-nope", TimeSpan.FromSeconds(5))).Error == "model-unknown", "claude fake: unknown model");
		fake.Answer = _ => (HttpStatusCode.OK, "{\"text\":\"type this\"}");
		reply = await client.OpenAiTranscribeAsync("https://api.openai.com/v1", "sk-openai", JarvisAudio.Wav(new byte[3200]), TimeSpan.FromSeconds(5));
		Check(reply is { Ok: true, Text: "type this" } && fake.Seen[^1].Url == "https://api.openai.com/v1/audio/transcriptions" && fake.Seen[^1].Authorization == "Bearer sk-openai", "openai fake: cloud Whisper transcription");
	}

	private static void Spoiler(Action<bool, string> Check)
	{
		JarvisScreenAnswer answer = JarvisSpoiler.Parse("NUDGE: Look up; the way on is not on the ground.\nFULL: Climb the vines on the left wall to reach the ledge, then pull the lever.", true);
		Check(answer.Nudge == "Look up; the way on is not on the ground." && answer.Full.StartsWith("Climb the vines", StringComparison.Ordinal), "spoiler: nudge and full answer split");
		(string shown, bool more) = JarvisSpoiler.First(answer);
		Check(shown == answer.Nudge && more, "spoiler: the nudge first, More for the rest");
		answer = JarvisSpoiler.Parse("Climb the vines on the left wall.", true);
		Check(answer.Nudge == JarvisSpoiler.HiddenNudge && answer.Full == "Climb the vines on the left wall.", "spoiler: an answer without the form never shows its text first");
		answer = JarvisSpoiler.Parse("NUDGE: x\nFULL: Climb.", false);
		(shown, more) = JarvisSpoiler.First(answer);
		Check(answer.Nudge is null && shown == "Climb." && !more, "spoiler: off shows the full answer at once");
		answer = JarvisSpoiler.Parse("full: lower case works too", false);
		Check(answer.Full == "lower case works too", "spoiler: markers in any case");
	}

	private static void Speech(Action<bool, string> Check)
	{
		Check(JarvisSpeechRoutes.Plan(true, true, true, false).SequenceEqual(new[] { "wyoming", "home-assistant" }), "speech: Aincrad's Whisper first, then through Home Assistant; cloud only when chosen");
		Check(JarvisSpeechRoutes.Plan(true, false, true, true).SequenceEqual(new[] { "wyoming", "openai" }) && JarvisSpeechRoutes.Plan(false, false, false, true).Count == 0, "speech: cloud Whisper only with its key");
		// 48 kHz stereo float, a 1 kHz tone at half scale: 16 kHz mono 16-bit with the same level.
		int frames = 4800;
		var capture = new byte[frames * 2 * 4];
		for (int frame = 0; frame < frames; frame++)
		{
			float value = 0.5f * MathF.Sin(2 * MathF.PI * 1000 * frame / 48000f);
			BitConverter.GetBytes(value).CopyTo(capture, frame * 8);
			BitConverter.GetBytes(value).CopyTo(capture, frame * 8 + 4);
		}
		short[] mono = JarvisAudio.ToMono16k(capture, 48000, 2, 32, true);
		Check(mono.Length == 1600 && mono.Max() is > 15000 and < 17000, "audio: 48 kHz stereo float to 16 kHz mono, level kept");
		short[] pcm16 = JarvisAudio.ToMono16k(new byte[] { 0x00, 0x40, 0x00, 0x40 }, 16000, 1, 16, false);
		Check(pcm16.Length == 2 && pcm16[0] == 16384, "audio: 16-bit passes through at 16 kHz");
		Check(JarvisAudio.ToMono16k(new byte[10], 0, 1, 16, false).Length == 0 && JarvisAudio.ToMono16k(new byte[10], 16000, 1, 24, false).Length == 0, "audio: unusable formats give nothing");
		Check(!JarvisAudio.IsSpeech(new short[16000]) && !JarvisAudio.IsSpeech(mono.Take(100).ToArray()) && JarvisAudio.IsSpeech(Enumerable.Repeat(mono, 4).SelectMany(x => x).ToArray()), "audio: silence and a blip are not speech");
		Check(JarvisListenPolicy.AfterRecording(new short[100]) == "too-short" && JarvisListenPolicy.AfterRecording(new short[16000]) == "silent", "listen: too short, silent");
		byte[] wav = JarvisAudio.Wav(new byte[] { 1, 2, 3, 4 });
		Check(wav.Length == 48 && Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt32(wav, 40) == 4, "audio: WAV header");
	}

	private static async Task WyomingFraming(Action<bool, string> Check)
	{
		byte[] frame = WyomingWire.Frame("audio-chunk", WyomingWire.Format(16000, 2, 1), new byte[] { 1, 2, 3 });
		int newline = Array.IndexOf(frame, (byte)'\n');
		JsonNode header = JsonNode.Parse(Encoding.UTF8.GetString(frame, 0, newline))!;
		Check((string?)header["type"] == "audio-chunk" && (int?)header["payload_length"] == 3 && (int?)header["data"]!["rate"] == 16000 && frame.Length == newline + 4, "wyoming: header line, inline data, payload after the new line");
		Check(!Encoding.UTF8.GetString(WyomingWire.Frame("audio-stop")).Contains("payload_length", StringComparison.Ordinal), "wyoming: no payload length without a payload");
		List<byte[]> frames = WyomingWire.TranscribeFrames(new byte[WyomingWire.ChunkSamples * 2 * 2 + 10]).ToList();
		Check(frames.Count == 6 && Encoding.UTF8.GetString(frames[0]).Contains("\"transcribe\"", StringComparison.Ordinal) && Encoding.UTF8.GetString(frames[^1]).Contains("\"audio-stop\"", StringComparison.Ordinal), "wyoming: transcribe, audio-start, three chunks, audio-stop");
		// data_length: extra JSON after the header, merged into data.
		var stream = new MemoryStream();
		byte[] extra = Encoding.UTF8.GetBytes("{\"text\":\"hello there\"}");
		stream.Write(Encoding.UTF8.GetBytes($"{{\"type\":\"transcript\",\"data\":{{\"language\":\"en\"}},\"data_length\":{extra.Length}}}\n"));
		stream.Write(extra);
		stream.Position = 0;
		WyomingEvent? item = await WyomingWire.ReadAsync(stream, CancellationToken.None);
		Check(item is { Type: "transcript" } && (string?)item.Data["text"] == "hello there" && (string?)item.Data["language"] == "en", "wyoming: data_length data merged with inline data");
		Check(await WyomingWire.ReadAsync(stream, CancellationToken.None) is null, "wyoming: end of stream");
		bool threw = false;
		try { await WyomingWire.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("{\"type\":\"x\",\"payload_length\":99999999999}\n")), CancellationToken.None); }
		catch (InvalidDataException) { threw = true; }
		Check(threw, "wyoming: an oversized length is refused");
	}

	// A fake Whisper and a fake Piper on loopback, speaking Wyoming.
	private static async Task FakeWyomingServer(Action<bool, string> Check)
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		var received = new List<string>();
		int audioBytes = 0;
		Task server = Task.Run(async () =>
		{
			for (int round = 0; round < 2; round++)
			{
				using TcpClient client = await listener.AcceptTcpClientAsync();
				using NetworkStream stream = client.GetStream();
				while (await WyomingWire.ReadAsync(stream, CancellationToken.None) is { } item)
				{
					received.Add(item.Type);
					if (item.Type == "audio-chunk") audioBytes += item.Payload.Length;
					if (item.Type == "audio-stop")
					{
						// Real servers may send data separately (data_length): do that here.
						byte[] data = Encoding.UTF8.GetBytes("{\"text\":\" Turn it up \"}");
						await stream.WriteAsync(Encoding.UTF8.GetBytes($"{{\"type\":\"transcript\",\"data_length\":{data.Length},\"version\":\"1.5.2\"}}\n"));
						await stream.WriteAsync(data);
						break;
					}
					if (item.Type == "synthesize")
					{
						await stream.WriteAsync(WyomingWire.Frame("audio-start", WyomingWire.Format(22050, 2, 1)));
						await stream.WriteAsync(WyomingWire.Frame("audio-chunk", WyomingWire.Format(22050, 2, 1), new byte[] { 9, 9, 9, 9 }));
						await stream.WriteAsync(WyomingWire.Frame("audio-stop"));
						break;
					}
				}
			}
		});
		byte[] pcm = new byte[16000 * 2];
		(string? text, string? error) = await WyomingClient.TranscribeAsync("127.0.0.1", port, pcm, TimeSpan.FromSeconds(5), CancellationToken.None);
		Check(text == "Turn it up" && error is null, "wyoming server: transcript returned");
		Check(received.Take(2).SequenceEqual(new[] { "transcribe", "audio-start" }) && received.Contains("audio-stop") && audioBytes == pcm.Length, "wyoming server: every byte of audio arrived, in order");
		WyomingAudio? audio = await WyomingClient.SynthesizeAsync("127.0.0.1", port, "Good evening, sir.", "en_GB-alan-medium", TimeSpan.FromSeconds(5), CancellationToken.None);
		Check(audio is { Rate: 22050, Width: 2, Channels: 1 } && audio.Pcm.Length == 4, "wyoming server: Piper audio returned");
		await server;
		listener.Stop();
		(text, error) = await WyomingClient.TranscribeAsync("127.0.0.1", port, pcm, TimeSpan.FromSeconds(3), CancellationToken.None);
		Check(text is null && error is "unreachable" or "timeout", "wyoming server: a closed port fails soft");
		// A server that accepts and never answers: the deadline wins.
		var silent = new TcpListener(IPAddress.Loopback, 0);
		silent.Start();
		int silentPort = ((IPEndPoint)silent.LocalEndpoint).Port;
		(text, error) = await WyomingClient.TranscribeAsync("127.0.0.1", silentPort, pcm, TimeSpan.FromMilliseconds(300), CancellationToken.None);
		Check(text is null && error == "timeout", "wyoming server: time-boxed");
		silent.Stop();
	}

	private static void Mic(Action<bool, string> Check)
	{
		var dji = new JarvisMic("dji", false, true, false, "DJI Mic 3");
		var webcam = new JarvisMic("cam", false, false, true, "Webcam");
		JarvisMicChoice choice = JarvisMicPolicy.Choose(new[] { webcam, dji }, false, false, "dji", false);
		Check(choice is { Id: "dji", Refusal: null }, "mic: Core Voice's calls mic first");
		choice = JarvisMicPolicy.Choose(new[] { webcam, dji with { IsCommunications = false } }, false, false, "gone", false);
		Check(choice.Id == "cam", "mic: the calls mic not connected: Windows' default");
		Check(JarvisMicPolicy.Choose(new[] { webcam, dji }, false, true, "dji", false).Refusal == "muted", "mic: never while Core's mute (everything) is on");
		Check(JarvisMicPolicy.Choose(new[] { webcam with { Muted = true }, dji }, false, false, "dji", false).Refusal == "muted", "mic: a default muted outside Core under 'everything' refuses");
		choice = JarvisMicPolicy.Choose(new[] { webcam, dji with { Muted = true } }, true, true, "dji", false);
		Check(choice.Id == "cam", "mic: 'calls only' muted: the games-and-apps mic records");
		var both = new JarvisMic("dji", true, true, true, "DJI Mic 3");
		Check(JarvisMicPolicy.Choose(new[] { both }, true, true, "dji", false).Refusal == "muted", "mic: 'calls only' with one device for everything: refused");
		Check(JarvisMicPolicy.Choose(Array.Empty<JarvisMic>(), false, false, null, false).Refusal == "no-mic", "mic: no microphone");
		Check(JarvisMicPolicy.Choose(new[] { dji }, false, false, "dji", true).PartyHears, "mic: an open party hears it");
	}

	private static void ScreenAndHealth(Action<bool, string> Check)
	{
		Check(JarvisScreenSize.Fit(3840, 2160) == (1280, 720) && JarvisScreenSize.Fit(1280, 800) == (1280, 800) && JarvisScreenSize.Fit(1080, 2400) == (576, 1280) && JarvisScreenSize.Fit(0, 5) == (0, 0), "screen: long edge 1280, never enlarged");
		Check(JarvisScreenSize.VisualTokens(1280, 720) == 46 * 26, "screen: about 1,200 visual tokens per screenshot");
		HealthRow row = JarvisHealth.Row(true, "home-assistant", true, false, null, true, "http://serverv3:8123");
		Check(row is { Id: "jarvis", Level: HealthLevel.Ok, State: "connected" } && (string?)row.Data["host"] == "serverv3" && row.Data["screenHelper"] is false, "health: connected through Home Assistant");
		Check(JarvisHealth.Row(true, "home-assistant", false, true, null, false, "http://serverv3:8123").State == "not-set-up", "health: no token is not set up");
		Check(JarvisHealth.Row(true, "home-assistant", true, false, "unreachable", true, "http://serverv3:8123") is { State: "ha-unreachable", Level: HealthLevel.Info }, "health: Home Assistant unreachable (information)");
		Check(JarvisHealth.Row(true, "claude", false, true, "key-refused", false, "") .State == "key-refused" && JarvisHealth.Row(true, "claude", false, true, null, false, "").State == "ready", "health: the direct Claude backend");
		Check(JarvisHealth.Row(false, "home-assistant", true, true, null, true, "").State == "off", "health: off");
		Check(JarvisHealth.Row(true, "home-assistant", true, false, "agent-missing", false, "http://serverv3:8123").State == "agent-missing", "health: the agent not found by the free check");
		Check(CoreHealthPolicy.RowOrder.Contains("jarvis") && Array.IndexOf(CoreHealthPolicy.RowOrder, "jarvis") == Array.IndexOf(CoreHealthPolicy.RowOrder, "home-assistant") + 1, "health: the Jarvis row follows Home Assistant");
		// The secret store never answers with a value it cannot read (no file: nothing saved).
		string directory = Path.Combine(Path.GetTempPath(), "core-jarvis-" + Guid.NewGuid().ToString("N"));
		Check(!JarvisSecretStore.Has(directory, JarvisSecretStore.HaToken) && JarvisSecretStore.Read(directory, JarvisSecretStore.AnthropicKey) is null, "secrets: nothing saved reads as nothing");
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, JarvisSecretStore.FileName), "{\"haToken\":\"not-dpapi\"}");
		Check(JarvisSecretStore.Has(directory, JarvisSecretStore.HaToken) && JarvisSecretStore.Read(directory, JarvisSecretStore.HaToken) is null, "secrets: an unreadable (foreign) value reads as nothing, never throws");
		Directory.Delete(directory, true);
	}
}
