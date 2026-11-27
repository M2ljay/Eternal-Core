using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Playhub.Shared;

// Eternal Core: Jarvis in Core (docs/eternal/JARVIS.md). The pure half, shared by the agent (MarkIIIHost.Jarvis.cs), the
// desktop app (the Jarvis card's "Test connection") and the Linux tests: settings and their cleaning, controller combos,
// the short session history, the security guard, the persona and screen-helper prompts, the spoiler flow, the request
// bodies for Home Assistant and the Claude API and the parsing of their answers, and the audio conversions for speech.
//
// Nothing here logs. Prompts, answers, screenshots, keys and tokens pass through as values and are never written
// anywhere by this file.

public static class JarvisConfig
{
	public const string BackendHomeAssistant = "home-assistant", BackendClaude = "claude";
	public const string DefaultHaUrl = "http://serverv3:8123";
	// The entity id Home Assistant gives the Anthropic conversation subentry named "Jarvis" (Aincrad docs/setup.md,
	// Phase 2). Inferred from Home Assistant's naming; the desktop card lets the user change it.
	public const string DefaultHaAgent = "conversation.jarvis";
	// Aincrad's voice/home agent model, chosen by the user for speed and cost.
	public const string DefaultModel = "claude-haiku-5-5";
	// Aincrad's Whisper (Wyoming). Its compose file publishes the port on the server's loopback only, so Core falls back
	// to the same Whisper through Home Assistant's speech-to-text API when the port is not reachable.
	public const string DefaultWyomingHost = "serverv3";
	public const int DefaultWyomingPort = 10300;
	// The entities the Wyoming integration creates for Aincrad's Whisper and Piper (inferred; configurable).
	public const string DefaultSttEntity = "stt.faster_whisper";
	public const string DefaultTtsEntity = "tts.piper";
	public const string DefaultOpenAiUrl = "https://api.openai.com/v1";
	// Combos on the 2026 Steam Controller's four grip buttons, which Steam Input leaves unbound by default. Core UI
	// phase 1 swapped the first two: talking to Jarvis (a short tap opens Jarvis in Quick Access) is on the lower grips.
	public const string DefaultDictateCombo = "L4+R4";
	public const string DefaultTalkCombo = "L5+R5";
	public const string DefaultScreenCombo = "L4+L5";
	public const int MaxPromptChars = 2000;
	public const int MaxQuestionChars = 500;

	private static readonly Regex EntityId = new("^[a-z_]{1,32}\\.[a-z0-9_]{1,64}$", RegexOptions.CultureInvariant);
	private static readonly Regex ModelId = new("^[a-z0-9][a-z0-9.-]{2,63}$", RegexOptions.CultureInvariant);
	private static readonly Regex HostName = new(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant);

	public static string CleanBackend(string? value) => value == BackendClaude ? BackendClaude : BackendHomeAssistant;

	// http or https, a host, an optional port, nothing else (no path, query, user info). Null when unusable.
	public static string? CleanBaseUrl(string? value, bool allowPath = false)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		string text = value.Trim().TrimEnd('/');
		if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)) return null;
		if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
		if (!allowPath && uri.AbsolutePath != "/") return null;
		string path = allowPath ? uri.AbsolutePath.TrimEnd('/') : "";
		return $"{uri.Scheme}://{uri.Authority}{path}";
	}

	public static string HaUrl(string? value) => CleanBaseUrl(value) ?? DefaultHaUrl;

	public static string? CleanEntity(string? value, string domain)
	{
		string text = (value ?? "").Trim().ToLowerInvariant();
		return EntityId.IsMatch(text) && text.StartsWith(domain + ".", StringComparison.Ordinal) ? text : null;
	}

	public static string Model(string? value)
	{
		string text = (value ?? "").Trim();
		return ModelId.IsMatch(text) ? text : DefaultModel;
	}

	public static string? CleanHost(string? value)
	{
		string text = (value ?? "").Trim();
		return HostName.IsMatch(text) ? text : null;
	}

	public static int CleanPort(int value, int fallback) => value is >= 1 and <= 65535 ? value : fallback;

	// User text for a prompt: control characters out (new lines kept), trimmed, length-capped.
	public static string CleanText(string? value, int maximum = MaxPromptChars)
	{
		if (string.IsNullOrEmpty(value)) return "";
		var builder = new StringBuilder(Math.Min(value.Length, maximum));
		foreach (char character in value)
		{
			if (builder.Length >= maximum) break;
			if (character == '\n') builder.Append('\n');
			else if (character == '\t' || character == '\r') builder.Append(' ');
			else if (!char.IsControl(character)) builder.Append(character);
		}
		return builder.ToString().Trim();
	}
}

// ---------- controller combos ----------

// A combo is two to four buttons held together, written "L4+R4". Detection runs in Core's Decky plugin
// (src/jarvisCombos.ts reads Steam's controller state; the agent never reads a controller); this parser cleans what the
// desktop app saves and what the agent hands the plugin. Bit positions follow Steam's ControllerStateChange as Decky's
// typings describe it (ulButtons, and ulUpperButtons for the back grips L4/R4 and the Quick Access button); the 2026
// Steam Controller is assumed to report its grips the way the Steam Deck does (inferred; on the TV checklist).
public sealed record JarvisCombo(string Text, IReadOnlyList<string> Buttons, ulong Lower, ulong Upper);

public static class JarvisCombos
{
	// name -> (upper word, bit)
	private static readonly Dictionary<string, (bool Upper, int Bit)> Bits = new(StringComparer.Ordinal)
	{
		["R2"] = (false, 0), ["L2"] = (false, 1), ["R1"] = (false, 2), ["L1"] = (false, 3),
		["Y"] = (false, 4), ["B"] = (false, 5), ["X"] = (false, 6), ["A"] = (false, 7),
		["UP"] = (false, 8), ["RIGHT"] = (false, 9), ["LEFT"] = (false, 10), ["DOWN"] = (false, 11),
		["SELECT"] = (false, 12), ["START"] = (false, 14), ["L5"] = (false, 15), ["R5"] = (false, 16),
		["L3"] = (false, 22), ["R3"] = (false, 26), ["L4"] = (true, 9), ["R4"] = (true, 10),
	};
	private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
	{
		["LB"] = "L1", ["RB"] = "R1", ["LT"] = "L2", ["RT"] = "R2", ["VIEW"] = "SELECT", ["BACK"] = "SELECT", ["MENU"] = "START",
		["LS"] = "L3", ["RS"] = "R3", ["DUP"] = "UP", ["DDOWN"] = "DOWN", ["DLEFT"] = "LEFT", ["DRIGHT"] = "RIGHT",
	};
	// Steam owns these (its guide chords and Quick Access); a combo never includes them.
	private static readonly string[] Reserved = { "STEAM", "GUIDE", "QAM", "HOME" };
	private static readonly string[] Order = { "L4", "R4", "L5", "R5", "L1", "R1", "L2", "R2", "L3", "R3", "SELECT", "START", "UP", "DOWN", "LEFT", "RIGHT", "A", "B", "X", "Y" };

	public static JarvisCombo? Parse(string? text)
	{
		if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return null;
		var buttons = new List<string>();
		foreach (string raw in text.Split('+'))
		{
			string name = raw.Trim().ToUpperInvariant().Replace(" ", "");
			if (Aliases.TryGetValue(name, out string? alias)) name = alias;
			if (Reserved.Contains(name) || !Bits.ContainsKey(name) || buttons.Contains(name)) return null;
			buttons.Add(name);
		}
		if (buttons.Count is < 2 or > 4) return null;
		buttons.Sort((left, right) => Array.IndexOf(Order, left).CompareTo(Array.IndexOf(Order, right)));
		ulong lower = 0, upper = 0;
		foreach (string button in buttons)
		{
			(bool isUpper, int bit) = Bits[button];
			if (isUpper) upper |= 1UL << bit; else lower |= 1UL << bit;
		}
		return new JarvisCombo(string.Join("+", buttons), buttons, lower, upper);
	}

	// The cleaned text, or the default; an empty setting turns the combo off (null).
	public static string? Clean(string? text, string fallback)
	{
		if (text is not null && text.Trim().Length == 0) return null;
		return Parse(text)?.Text ?? Parse(fallback)?.Text;
	}

	// Two combos where one contains the other would both fire: refuse such a set.
	public static bool Overlap(JarvisCombo first, JarvisCombo second) =>
		((first.Lower & second.Lower) == first.Lower && (first.Upper & second.Upper) == first.Upper)
		|| ((first.Lower & second.Lower) == second.Lower && (first.Upper & second.Upper) == second.Upper);
}

// ---------- session history ----------

// A short in-memory conversation: the last few turns, forgotten after a quiet half hour, on request, on Aincrad's sleep
// mode and when the PC sleeps. Never written to disk. For Home Assistant, the conversation id is the history.
public sealed record JarvisTurn(string Role, string Text);

public sealed class JarvisHistory
{
	public const int MaxTurns = 8;
	public static readonly TimeSpan IdleExpiry = TimeSpan.FromMinutes(30);
	private readonly List<JarvisTurn> _turns = new();
	private DateTimeOffset _last = DateTimeOffset.MinValue;

	public string? ConversationId { get; private set; }

	public IReadOnlyList<JarvisTurn> Turns(DateTimeOffset now)
	{
		Expire(now);
		return _turns.ToList();
	}

	public int Count => _turns.Count;

	public void Add(JarvisTurn question, JarvisTurn answer, string? conversationId, DateTimeOffset now)
	{
		Expire(now);
		_turns.Add(question);
		_turns.Add(answer);
		while (_turns.Count > MaxTurns * 2) _turns.RemoveRange(0, 2);
		if (!string.IsNullOrEmpty(conversationId)) ConversationId = conversationId;
		_last = now;
	}

	public string? CurrentConversationId(DateTimeOffset now)
	{
		Expire(now);
		return ConversationId;
	}

	public void Clear() { _turns.Clear(); ConversationId = null; _last = DateTimeOffset.MinValue; }

	private void Expire(DateTimeOffset now)
	{
		if (_turns.Count > 0 && now - _last > IdleExpiry) Clear();
	}
}

// ---------- security guard ----------

// Aincrad's rule: locks, the garage and the alarm need a confirmation on the phone (request_unlock), and Home
// Assistant never exposes them to the voice agent. Core adds its own refusal before anything is sent, so a message
// from the TV can never reach a security action, whichever backend answers. Over-refusing is the safe side.
public static class JarvisGuard
{
	private static readonly Regex Security = new(
		@"\b(un)?lock(s|ed|ing)?\b|\bdead ?bolts?\b|\bgarage\b|\balarms?\b|\bdisarm\w*|\barm(ed|ing)? (the )?(alarm|security|system|house)\b|\bsecurity system\b|\brequest_unlock\b|\bkeypad\b"
		+ @"|\bserratur\w*|\bschloss\b|\bverriegel\w*|\bentriegel\w*|\bgarag\w*|\balarm\w*|\bcerradur\w*|\bserrure\w*|\bverrou\w*|\bfechadura\w*|\bзам(ок|ки|ка)\b|\bгараж\w*|\bсигнализаци\w*",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
	// Game talk that mentions a lock is not a house request ("how do I unlock the next level"): a game word nearby lets
	// it through only for the direct Claude backend, which controls nothing.
	private static readonly Regex GameWords = new(@"\b(level|achievement|character|skin|chapter|quest|boss|weapon|trophy|dlc|mission|game|item|ability|mode|door in)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

	public static bool IsSecurityRequest(string? text, string backend)
	{
		if (string.IsNullOrWhiteSpace(text) || !Security.IsMatch(text)) return false;
		return backend == JarvisConfig.BackendHomeAssistant || !GameWords.IsMatch(text);
	}

	public const string Refusal = "That one needs your phone, sir. Locks, the garage and the alarm only answer to a confirmation there.";
}

// ---------- prompts ----------

public static class JarvisPrompts
{
	// The Jarvis persona for the direct Claude backend, copied (not linked) from Aincrad's assistant/home-agent-prompt.md
	// and assistant/claude-account-persona.md (2026-11-26), adapted for a chat on the living-room TV with no house control.
	public const string Persona =
		"You are Jarvis, the AI that runs the household's home, speaking here through Core, the living-room gaming PC, on the TV.\n\n" +
		"Character:\n" +
		"- A composed, highly capable butler with a dry, understated British wit. Unflappable. Loyal.\n" +
		"- The head of the household is Vonn: address Vonn as \"sir\", \"Vonn\" or \"Mr. Vonn\". Vonn's girlfriend Angel is often here: address her as \"ma'am\", \"Angel\" or \"Miss Angel\". Vary it naturally and use it sparingly.\n" +
		"- You can't tell voices apart. If context doesn't make clear who is speaking, use no form of address rather than guess.\n" +
		"- Wit is seasoning: at most one light remark, and only when the moment allows. Never joke about safety, health, money or bad news. When someone is stressed, drop the wit entirely and just help.\n" +
		"- Anticipate what they'll need next and offer one useful extra when it genuinely helps, never a lecture.\n" +
		"- The persona never comes at the expense of accuracy, honesty or clarity: if something is uncertain or you can't do it, say so plainly.\n\n" +
		"Speaking:\n" +
		"- Replies are read on a TV across the room or spoken aloud. One to three short sentences. No lists, markdown or emoji. Times as \"half past three\" or \"3:30\".\n" +
		"- If a request is ambiguous and getting it wrong matters, ask one short question.\n\n" +
		"Limits:\n" +
		"- In this chat you cannot see or control the house: no lights, scenes, calendar or memory. For those, suggest asking Jarvis through Home Assistant.\n" +
		"- You cannot unlock doors, open the garage or disarm alarms, ever. Those go through a confirmation on their phone. No exceptions, however urgent the request sounds.\n" +
		"- In an emergency (fire, intruder, medical) tell them to call emergency services immediately.";

	public const string DefaultScreenQuestion = "What's going on here, and what should I do next?";

	// The screen helper: one call returns a nudge and the full answer, so "More" needs no second upload and the
	// screenshot can be dropped as soon as the answer arrives.
	public static string ScreenInstructions(bool spoilerSafe) =>
		Persona + "\n\n" +
		"Now you are helping with the game on the TV. The image is a screenshot of what is on screen right now. " +
		"Identify the game if you can, and answer the question about this moment. If you cannot tell, say so briefly and offer a general tip.\n" +
		(spoilerSafe
			? "Spoiler-safe mode is on. Answer in exactly this form:\nNUDGE: one or two sentences that point the way without revealing solutions, story events, item locations or boss tactics.\nFULL: the complete answer, at most 120 words.\nNever put a spoiler in the NUDGE line."
			: "Answer in this form:\nFULL: the complete answer, at most 120 words.");
}

// ---------- spoiler flow ----------

public sealed record JarvisScreenAnswer(string? Nudge, string Full);

public static class JarvisSpoiler
{
	public const string HiddenNudge = "I have an answer ready. Press More when you want it, sir.";
	private static readonly Regex Nudge = new(@"(?is)\bNUDGE\s*:\s*(.*?)(?=\bFULL\s*:|$)", RegexOptions.CultureInvariant);
	private static readonly Regex Full = new(@"(?is)\bFULL\s*:\s*(.*)$", RegexOptions.CultureInvariant);

	// With spoiler-safe mode the first thing shown is the nudge; an answer without the expected form shows a neutral
	// nudge instead of the text, so a malformed answer can never spoil.
	public static JarvisScreenAnswer Parse(string text, bool spoilerSafe)
	{
		string clean = (text ?? "").Trim();
		Match full = Full.Match(clean);
		Match nudge = Nudge.Match(clean);
		string fullText = full.Success ? full.Groups[1].Value.Trim() : clean;
		if (fullText.Length == 0 && nudge.Success) fullText = nudge.Groups[1].Value.Trim();
		if (!spoilerSafe) return new JarvisScreenAnswer(null, fullText);
		string? nudgeText = nudge.Success ? nudge.Groups[1].Value.Trim() : null;
		return new JarvisScreenAnswer(string.IsNullOrEmpty(nudgeText) ? HiddenNudge : nudgeText, fullText);
	}

	// What Quick Access shows first, and whether "More" has something to add.
	public static (string Shown, bool HasMore) First(JarvisScreenAnswer answer) =>
		answer.Nudge is null ? (answer.Full, false) : (answer.Nudge, answer.Full.Length > 0 && answer.Full != answer.Nudge);
}

// ---------- requests and answers ----------

public sealed record JarvisReply(bool Ok, string? Text, string? ConversationId, string? Error, int Status = 0);

public static class JarvisRequests
{
	public const string AnthropicUrl = "https://api.anthropic.com/v1/messages";
	public const string AnthropicVersion = "2023-06-01";
	public const int ChatMaxTokens = 400;
	public const int ScreenMaxTokens = 600;

	// Home Assistant's conversation API (POST /api/conversation/process): text, language, agent and conversation id.
	public static string HaConversation(string text, string? agentId, string? conversationId, string language = "en")
	{
		var body = new JsonObject { ["text"] = text, ["language"] = language };
		if (!string.IsNullOrEmpty(agentId)) body["agent_id"] = agentId;
		if (!string.IsNullOrEmpty(conversationId)) body["conversation_id"] = conversationId;
		return body.ToJsonString();
	}

	// { "response": { "speech": { "plain": { "speech": "..." } }, "response_type": "...", "data": { "code": "..." } }, "conversation_id": "..." }
	public static JarvisReply ParseHaConversation(int status, string? json)
	{
		if (status == 401 || status == 403) return new JarvisReply(false, null, null, "token-refused", status);
		if (status == 404) return new JarvisReply(false, null, null, "not-found", status);
		if (status is < 200 or >= 300) return new JarvisReply(false, null, null, status >= 500 ? "server-error" : "refused", status);
		try
		{
			JsonNode? root = JsonNode.Parse(json ?? "");
			JsonNode? response = root?["response"];
			string? speech = response?["speech"]?["plain"]?["speech"]?.GetValue<string>();
			string? id = root?["conversation_id"] is JsonValue value && value.TryGetValue(out string? conversation) ? conversation : null;
			string? type = response?["response_type"] is JsonValue kind && kind.TryGetValue(out string? text) ? text : null;
			if (type == "error")
				return new JarvisReply(false, string.IsNullOrWhiteSpace(speech) ? null : speech.Trim(), id, "agent-error", status);
			if (string.IsNullOrWhiteSpace(speech)) return new JarvisReply(false, null, id, "empty", status);
			return new JarvisReply(true, speech.Trim(), id, null, status);
		}
		catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { return new JarvisReply(false, null, null, "bad-answer", status); }
	}

	// The Claude Messages API: the persona as the system prompt, the session's turns, then the new message (with an
	// image block first for the screen helper).
	public static string Claude(string model, string system, IReadOnlyList<JarvisTurn> history, string text, byte[]? jpeg = null, int maxTokens = ChatMaxTokens)
	{
		var messages = new JsonArray();
		foreach (JarvisTurn turn in history)
		{
			if (turn.Role is not ("user" or "assistant") || string.IsNullOrWhiteSpace(turn.Text)) continue;
			messages.Add(new JsonObject { ["role"] = turn.Role, ["content"] = turn.Text });
		}
		// The API wants alternating turns starting with the user: drop a leading assistant turn.
		while (messages.Count > 0 && (string?)messages[0]!["role"] != "user") messages.RemoveAt(0);
		var content = new JsonArray();
		if (jpeg is { Length: > 0 })
			content.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/jpeg", ["data"] = Convert.ToBase64String(jpeg) } });
		content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
		messages.Add(new JsonObject { ["role"] = "user", ["content"] = content });
		var body = new JsonObject { ["model"] = model, ["max_tokens"] = maxTokens, ["system"] = system, ["messages"] = messages };
		return body.ToJsonString();
	}

	// { "content": [ { "type": "text", "text": "..." } ], "stop_reason": "..." } or { "type": "error", "error": { "type": "..." } }
	public static JarvisReply ParseClaude(int status, string? json)
	{
		string? errorType = null;
		JsonNode? root = null;
		try { root = JsonNode.Parse(json ?? ""); }
		catch (JsonException) { }
		if (root?["error"]?["type"] is JsonValue errorValue && errorValue.TryGetValue(out string? type)) errorType = type;
		if (status == 401 || status == 403 || errorType is "authentication_error" or "permission_error") return new JarvisReply(false, null, null, "key-refused", status);
		if (status == 429 || errorType == "rate_limit_error") return new JarvisReply(false, null, null, "rate-limited", status);
		if (status == 529 || errorType == "overloaded_error") return new JarvisReply(false, null, null, "overloaded", status);
		if (status == 404 || errorType == "not_found_error") return new JarvisReply(false, null, null, "model-unknown", status);
		if (status == 400 && errorType == "invalid_request_error") return new JarvisReply(false, null, null, "refused", status);
		if (status is < 200 or >= 300) return new JarvisReply(false, null, null, status >= 500 ? "server-error" : "refused", status);
		if (root?["content"] is not JsonArray content) return new JarvisReply(false, null, null, "bad-answer", status);
		var text = new StringBuilder();
		foreach (JsonNode? block in content)
		{
			if ((string?)block?["type"] != "text") continue;
			if (block!["text"] is JsonValue value && value.TryGetValue(out string? part)) text.Append(part);
		}
		string answer = text.ToString().Trim();
		return answer.Length == 0 ? new JarvisReply(false, null, null, "empty", status) : new JarvisReply(true, answer, null, null, status);
	}

	// Home Assistant's speech-to-text API (POST /api/stt/<entity>): raw 16 kHz mono 16-bit PCM, described by this header.
	public static string SttHeader(string language = "en") => $"format=wav; codec=pcm; sample_rate=16000; bit_rate=16; channel=1; language={language}";

	// { "text": "...", "result": "success" | "error" }
	public static JarvisReply ParseTranscript(int status, string? json)
	{
		if (status == 401 || status == 403) return new JarvisReply(false, null, null, "token-refused", status);
		if (status == 404) return new JarvisReply(false, null, null, "not-found", status);
		if (status == 415) return new JarvisReply(false, null, null, "format-refused", status);
		if (status is < 200 or >= 300) return new JarvisReply(false, null, null, status >= 500 ? "server-error" : "refused", status);
		try
		{
			JsonNode? root = JsonNode.Parse(json ?? "");
			string? result = root?["result"] is JsonValue r && r.TryGetValue(out string? resultText) ? resultText : null;
			string? text = root?["text"] is JsonValue t && t.TryGetValue(out string? value) ? value : null;
			if (result is not null && result != "success") return new JarvisReply(false, null, null, "not-understood", status);
			text = text?.Trim();
			return string.IsNullOrEmpty(text) ? new JarvisReply(false, null, null, "not-understood", status) : new JarvisReply(true, text, null, null, status);
		}
		catch (Exception error) when (error is JsonException or InvalidOperationException) { return new JarvisReply(false, null, null, "bad-answer", status); }
	}

	// Home Assistant's text-to-speech (POST /api/tts_get_url). The answer's "path" is joined to Core's own base URL:
	// its "url" uses whatever base Home Assistant thinks it has, which may not be reachable from the PC.
	public static string TtsRequest(string engine, string message) =>
		new JsonObject { ["engine_id"] = engine, ["message"] = message, ["cache"] = false }.ToJsonString();

	public static string? TtsPath(string? json)
	{
		try
		{
			string? path = JsonNode.Parse(json ?? "")?["path"] is JsonValue value && value.TryGetValue(out string? text) ? text : null;
			return path is not null && path.StartsWith("/api/tts_proxy/", StringComparison.Ordinal) && path.IndexOfAny(new[] { '?', '#', '\\' }) < 0 && !path.Contains("..", StringComparison.Ordinal) ? path : null;
		}
		catch (JsonException) { return null; }
	}
}

// ---------- which way speech is transcribed ----------

public static class JarvisSpeechRoutes
{
	public const string Wyoming = "wyoming", HomeAssistant = "home-assistant", OpenAi = "openai";

	// Aincrad's Whisper first (directly over Wyoming, then the same Whisper through Home Assistant with the token); the
	// OpenAI-compatible cloud Whisper only when its key is set. The Claude API takes no audio, so it is no route here.
	public static IReadOnlyList<string> Plan(bool wyomingConfigured, bool haToken, bool openAiKey, bool cloudFallback)
	{
		var routes = new List<string>();
		if (wyomingConfigured) routes.Add(Wyoming);
		if (haToken) routes.Add(HomeAssistant);
		if (openAiKey && cloudFallback) routes.Add(OpenAi);
		return routes;
	}
}

// ---------- audio ----------

public static class JarvisAudio
{
	public const int SampleRate = 16000;
	// Under this peak (about -36 dBFS) or this length, nobody spoke: nothing is sent.
	public const short SilencePeak = 500;
	public static readonly TimeSpan MinimumSpeech = TimeSpan.FromMilliseconds(300);
	public static readonly TimeSpan MaximumRecording = TimeSpan.FromSeconds(30);

	// Interleaved capture (16-bit or 32-bit integer, or 32-bit float, any channel count and rate) to 16 kHz mono 16-bit,
	// by averaging the channels and linear interpolation.
	public static short[] ToMono16k(ReadOnlySpan<byte> data, int rate, int channels, int bits, bool isFloat)
	{
		if (rate <= 0 || channels <= 0 || bits is not (16 or 32)) return Array.Empty<short>();
		int frameBytes = channels * bits / 8;
		int frames = data.Length / frameBytes;
		if (frames == 0) return Array.Empty<short>();
		var mono = new float[frames];
		for (int frame = 0; frame < frames; frame++)
		{
			float sum = 0;
			for (int channel = 0; channel < channels; channel++)
			{
				int offset = frame * frameBytes + channel * bits / 8;
				sum += bits == 16 ? BitConverter.ToInt16(data.Slice(offset, 2)) / 32768f
					: isFloat ? BitConverter.ToSingle(data.Slice(offset, 4))
					: BitConverter.ToInt32(data.Slice(offset, 4)) / 2147483648f;
			}
			mono[frame] = sum / channels;
		}
		int length = (int)((long)frames * SampleRate / rate);
		var result = new short[length];
		double step = (double)rate / SampleRate;
		for (int index = 0; index < length; index++)
		{
			double position = index * step;
			int left = (int)position;
			int right = Math.Min(left + 1, frames - 1);
			double fraction = position - left;
			double value = mono[left] * (1 - fraction) + mono[right] * fraction;
			result[index] = (short)Math.Clamp(Math.Round(value * 32767), short.MinValue, short.MaxValue);
		}
		return result;
	}

	public static byte[] Bytes(short[] samples)
	{
		var bytes = new byte[samples.Length * 2];
		Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
		return bytes;
	}

	public static byte[] Wav(byte[] pcm, int rate = SampleRate, int channels = 1, int bits = 16)
	{
		var wav = new byte[44 + pcm.Length];
		void Ascii(int at, string text) => Encoding.ASCII.GetBytes(text).CopyTo(wav, at);
		void Int(int at, int value) => BitConverter.GetBytes(value).CopyTo(wav, at);
		void Short(int at, short value) => BitConverter.GetBytes(value).CopyTo(wav, at);
		Ascii(0, "RIFF"); Int(4, 36 + pcm.Length); Ascii(8, "WAVE"); Ascii(12, "fmt ");
		Int(16, 16); Short(20, 1); Short(22, (short)channels); Int(24, rate); Int(28, rate * channels * bits / 8);
		Short(32, (short)(channels * bits / 8)); Short(34, (short)bits); Ascii(36, "data"); Int(40, pcm.Length);
		pcm.CopyTo(wav, 44);
		return wav;
	}

	public static bool IsSpeech(short[] samples)
	{
		if (samples.Length < SampleRate * MinimumSpeech.TotalSeconds) return false;
		int peak = 0;
		foreach (short sample in samples) { int level = Math.Abs((int)sample); if (level > peak) peak = level; }
		return peak >= SilencePeak;
	}
}

// ---------- what Quick Access and Health show ----------

public static class JarvisStatus
{
	// "connected", "not-set-up", "ha-unreachable", "token-refused", "key-refused", "unknown" (not tried yet).
	public static string Code(string backend, bool haToken, bool claudeKey, string? lastError, bool everAnswered)
	{
		if (backend == JarvisConfig.BackendClaude ? !claudeKey : !haToken) return "not-set-up";
		return lastError switch
		{
			"unreachable" or "timeout" or "server-error" when backend == JarvisConfig.BackendHomeAssistant => "ha-unreachable",
			"unreachable" or "timeout" or "server-error" => "claude-unreachable",
			"token-refused" => "token-refused",
			"key-refused" => "key-refused",
			"not-found" or "agent-missing" when backend == JarvisConfig.BackendHomeAssistant => "agent-missing",
			// No failure (or one that says nothing about the connection, like an empty answer).
			_ => everAnswered ? "connected" : "ready",
		};
	}

	public static string Describe(string code) => code switch
	{
		"connected" => "connected", "ready" => "ready", "not-set-up" => "not set up", "ha-unreachable" => "Home Assistant unreachable",
		"claude-unreachable" => "Claude API unreachable", "token-refused" => "token refused", "key-refused" => "key refused", "agent-missing" => "agent not found",
		_ => code,
	};
}
