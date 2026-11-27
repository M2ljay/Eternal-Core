using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GamingMode.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Playhub.Shared;

namespace GamingMode.Services;

// Jarvis in the agent (docs/eternal/JARVIS.md): chat for Quick Access, the screen helper and push-to-talk, for Core's
// Decky plugin (src/JarvisSection.tsx, src/jarvisCombos.ts). Rules in Jarvis.cs and Shared/JarvisCore.cs; Windows calls
// in Jarvis.Windows.cs.
//
// - Chat: Home Assistant's Jarvis agent (conversation API, the user's long-lived token) by default, or the Claude API
//   with the user's own key and the Jarvis persona. A short history in memory, cleared on request, after a quiet half
//   hour, when Aincrad's sleep mode turns on and when the PC sleeps. Security actions never leave Core (JarvisGuard).
// - Screen helper: only when asked; one frame of the screen, scaled and JPEG-encoded in memory, sent with the question
//   to the Claude API (Home Assistant's conversation API takes text only), then dropped. Spoiler-safe: a nudge first.
//   The "screenshot taken" mark shows every time, Stream mode or not.
// - Push-to-talk: records from Core Voice's microphone choice, never while muted (checked before and during the
//   recording), transcribes with Aincrad's Whisper, then types the text or asks Jarvis and speaks the answer.
//
// Never logged: prompts, answers, transcripts, screenshots, the token and the keys. Log lines say what happened and how
// (backend, route, outcome), nothing of the content. Every network call is time-boxed and fails soft. Nothing here
// stops, pauses or focuses a game.
internal sealed partial class MarkIIIHost
{
	private readonly object _jarvisSync = new();
	private readonly JarvisHistory _jarvisHistory = new();
	private readonly JarvisClient _jarvisClient = new();
	private readonly SemaphoreSlim _jarvisBusy = new(1, 1);
	private string? _jarvisLastError;
	private bool _jarvisEverAnswered;
	private DateTimeOffset _jarvisProbedAt = DateTimeOffset.MinValue;
	private DateTimeOffset _wyomingDownUntil = DateTimeOffset.MinValue;
	private bool? _jarvisSleepModeWas;
	// Push-to-talk.
	private JarvisRecorder? _recorder;
	private JarvisListenMode _recordMode;
	private string? _recordMicId;
	private int _recordGeneration;
	// The screen helper's last answer (text only; the screenshot is gone by then).
	private JarvisScreenAnswer? _screenAnswer;
	private bool _screenRevealed;

	private sealed record JarvisRuntime(bool Enabled, string Backend, string HaUrl, string? HaAgent, string Model, JarvisCombosView Combos,
		bool SpoilerSafe, bool Voice, bool SpeakScreen, bool CloudWhisper, bool TapOpens, string? WyomingHost, int WyomingPort, string SttEntity, string TtsEntity, string OpenAiUrl);

	private JarvisRuntime JarvisSettings()
	{
		GamingSettings gaming = _store.LoadConfig().Gaming;
		return new JarvisRuntime(gaming.JarvisEnabled, JarvisConfig.CleanBackend(gaming.JarvisBackend), JarvisConfig.HaUrl(gaming.JarvisHaUrl),
			gaming.JarvisHaAgent is { Length: 0 } ? null : JarvisConfig.CleanEntity(gaming.JarvisHaAgent, "conversation") ?? JarvisConfig.DefaultHaAgent,
			JarvisConfig.Model(gaming.JarvisModel), JarvisCombosPolicy.Clean(gaming.JarvisDictateCombo, gaming.JarvisTalkCombo, gaming.JarvisScreenCombo),
			gaming.JarvisSpoilerSafe, gaming.JarvisVoice, gaming.JarvisSpeakScreen, gaming.JarvisCloudWhisper, gaming.JarvisTapOpensQuickAccess,
			JarvisConfig.CleanHost(gaming.JarvisWyomingHost), JarvisConfig.CleanPort(gaming.JarvisWyomingPort, JarvisConfig.DefaultWyomingPort),
			JarvisConfig.CleanEntity(gaming.JarvisSttEntity, "stt") ?? JarvisConfig.DefaultSttEntity, JarvisConfig.CleanEntity(gaming.JarvisTtsEntity, "tts") ?? JarvisConfig.DefaultTtsEntity,
			JarvisConfig.CleanBaseUrl(gaming.JarvisOpenAiUrl, allowPath: true) ?? JarvisConfig.DefaultOpenAiUrl);
	}

	private string? Secret(string name) => JarvisSecretStore.Read(_paths.ConfigDirectory, name);

	// ---------- lifetime ----------

	// Every five seconds from the Mark III loop: Aincrad's sleep mode turning on clears the session; a free status probe
	// every ten minutes keeps the Health row honest.
	private void CheckJarvis()
	{
		bool? sleepMode = SleepModeActive();
		bool? before;
		lock (_jarvisSync) { before = _jarvisSleepModeWas; _jarvisSleepModeWas = sleepMode; }
		if (sleepMode == true && before == false) ClearJarvis("Aincrad's sleep mode");
		DateTimeOffset now = DateTimeOffset.Now;
		if (now - _jarvisProbedAt < TimeSpan.FromMinutes(10)) return;
		_jarvisProbedAt = now;
		JarvisRuntime settings = JarvisSettings();
		if (!settings.Enabled) return;
		_ = Task.Run(async () =>
		{
			try
			{
				JarvisReply reply = settings.Backend == JarvisConfig.BackendClaude
					? Secret(JarvisSecretStore.AnthropicKey) is { } key ? await _jarvisClient.ClaudeTestAsync(key, settings.Model, TimeSpan.FromSeconds(6)).ConfigureAwait(false) : new JarvisReply(false, null, null, null)
					: Secret(JarvisSecretStore.HaToken) is { } token ? await _jarvisClient.HaTestAsync(settings.HaUrl, token, settings.HaAgent, TimeSpan.FromSeconds(6)).ConfigureAwait(false) : new JarvisReply(false, null, null, null);
				if (reply.Error is null && !reply.Ok) return; // not set up
				NoteJarvis(reply.Ok ? null : reply.Error, reply.Ok);
			}
			catch (Exception error) { _logger.Error("Jarvis: status probe failed.", error); }
		});
	}

	// The session is forgotten: on request, on Aincrad's sleep mode, when the PC sleeps.
	private void ClearJarvis(string why)
	{
		lock (_jarvisSync)
		{
			_jarvisHistory.Clear();
			_screenAnswer = null;
			_screenRevealed = false;
		}
		CancelListening("session cleared");
		_logger.Info($"Jarvis: session cleared ({why}).");
	}

	private void NoteJarvis(string? error, bool answered)
	{
		lock (_jarvisSync)
		{
			_jarvisLastError = error;
			if (answered) _jarvisEverAnswered = true;
		}
	}

	// ---------- chat ----------

	private async Task<(bool Ok, string? Text, string? Error, bool Refused)> AskJarvisAsync(string raw, string source)
	{
		JarvisRuntime settings = JarvisSettings();
		if (!settings.Enabled) return (false, null, "off", false);
		string text = JarvisConfig.CleanText(raw);
		if (text.Length == 0) return (false, null, "empty", false);
		if (JarvisGuard.IsSecurityRequest(text, settings.Backend))
		{
			_logger.Info($"Jarvis: a security request from {source} was refused in Core (nothing sent).");
			lock (_jarvisSync) _jarvisHistory.Add(new JarvisTurn("user", text), new JarvisTurn("assistant", JarvisGuard.Refusal), null, DateTimeOffset.Now);
			return (true, JarvisGuard.Refusal, null, true);
		}
		if (!await _jarvisBusy.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false)) return (false, null, "busy", false);
		try
		{
			JarvisReply reply;
			string? conversation;
			IReadOnlyList<JarvisTurn> turns;
			lock (_jarvisSync) { conversation = _jarvisHistory.CurrentConversationId(DateTimeOffset.Now); turns = _jarvisHistory.Turns(DateTimeOffset.Now); }
			if (settings.Backend == JarvisConfig.BackendClaude)
			{
				string? key = Secret(JarvisSecretStore.AnthropicKey);
				if (key is null) return (false, null, "not-set-up", false);
				reply = await _jarvisClient.ClaudeAsync(key, JarvisRequests.Claude(settings.Model, JarvisPrompts.Persona, turns, text), TimeSpan.FromSeconds(30)).ConfigureAwait(false);
			}
			else
			{
				string? token = Secret(JarvisSecretStore.HaToken);
				if (token is null) return (false, null, "not-set-up", false);
				reply = await _jarvisClient.HaConverseAsync(settings.HaUrl, token, settings.HaAgent, text, conversation, TimeSpan.FromSeconds(25)).ConfigureAwait(false);
			}
			bool connected = reply.Ok || reply.Error is "agent-error" or "empty" or "rate-limited" or "overloaded" or "refused";
			NoteJarvis(connected ? null : reply.Error, connected);
			_logger.Info($"Jarvis: {source} message answered by {settings.Backend} ({(reply.Ok ? "ok" : reply.Error)}).");
			string? answer = reply.Text;
			if (answer is not null)
				lock (_jarvisSync) _jarvisHistory.Add(new JarvisTurn("user", text), new JarvisTurn("assistant", answer), reply.ConversationId, DateTimeOffset.Now);
			return (answer is not null, answer, reply.Ok ? null : reply.Error, false);
		}
		finally { _jarvisBusy.Release(); }
	}

	// Aincrad's Piper through Home Assistant, then Piper over Wyoming, then Windows' own British voice.
	private async Task<string> SpeakJarvisAsync(string text)
	{
		JarvisRuntime settings = JarvisSettings();
		try
		{
			if (Secret(JarvisSecretStore.HaToken) is { } token)
			{
				(byte[]? audio, string? error) = await _jarvisClient.HaSpeakAsync(settings.HaUrl, token, settings.TtsEntity, text, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
				if (audio is not null && await JarvisSpeaker.PlayAsync(audio, audio.Length > 4 && audio[0] == (byte)'R' && audio[1] == (byte)'I' ? "audio/wav" : "audio/mpeg").ConfigureAwait(false)) return "piper";
				_logger.Info($"Jarvis: Piper through Home Assistant unavailable ({error ?? "playback failed"}); trying the next voice.");
			}
			if (settings.WyomingHost is { } host && DateTimeOffset.Now >= _wyomingDownUntil)
			{
				WyomingAudio? piper = await WyomingClient.SynthesizeAsync(host, 10200, text, null, TimeSpan.FromSeconds(8), CancellationToken.None).ConfigureAwait(false);
				if (piper is { Pcm.Length: > 0 } && await JarvisSpeaker.PlayAsync(JarvisAudio.Wav(piper.Pcm, piper.Rate, piper.Channels, piper.Width * 8), "audio/wav").ConfigureAwait(false)) return "piper-wyoming";
			}
			return await JarvisSpeaker.SpeakWindowsAsync(text).ConfigureAwait(false) ? "windows" : "none";
		}
		catch (Exception error)
		{
			_logger.Error("Jarvis: the answer could not be spoken.", error);
			return "none";
		}
	}

	// ---------- push-to-talk ----------

	// auto: tap to talk (3.3.5), the recording ends by itself on a pause (JarvisListenLevels).
	private (bool Ok, string? Error, string? Mic, bool PartyHears) StartListening(JarvisListenMode mode, bool auto = false)
	{
		JarvisRuntime settings = JarvisSettings();
		if (!settings.Enabled) return (false, "off", null, false);
		JarvisMicChoice choice = ChooseJarvisMic();
		if (choice.Refusal is not null) { _logger.Info($"Jarvis: push-to-talk refused ({choice.Refusal})."); return (false, choice.Refusal, null, false); }
		int generation;
		lock (_jarvisSync)
		{
			if (_recorder is not null) return (false, "busy", null, false);
			JarvisRecorder? recorder = JarvisRecorder.Start(choice.Id!, out string? failure, auto, level => JarvisIndicator.Level(level, _logger));
			if (recorder is null) { _logger.Info($"Jarvis: the microphone could not be opened ({failure})."); return (false, failure ?? "refused", null, false); }
			_recorder = recorder;
			_recordMode = mode;
			_recordMicId = choice.Id;
			generation = ++_recordGeneration;
		}
		JarvisIndicator.Listening(true, CoreText.Get(Language(), "jarvis.listening"), _logger);
		_logger.Info($"Jarvis: push-to-talk ({mode.ToString().ToLowerInvariant()}) recording{(choice.PartyHears ? "; an Xbox party is open and hears it" : "")}.");
		_ = Task.Run(() => WatchListeningAsync(generation));
		return (true, null, choice.Name, choice.PartyHears);
	}

	// While recording: stop at once if the microphone gets muted, and give up after the longest recording (the plugin
	// normally stops it when the combo is released).
	private async Task WatchListeningAsync(int generation)
	{
		DateTimeOffset until = DateTimeOffset.Now + JarvisAudio.MaximumRecording + TimeSpan.FromSeconds(2);
		int round = 0;
		while (DateTimeOffset.Now < until)
		{
			await Task.Delay(100).ConfigureAwait(false);
			string? micId;
			bool ended;
			lock (_jarvisSync) { if (_recorder is null || generation != _recordGeneration) return; micId = _recordMicId; ended = _recorder.Ended; }
			// Tap to talk ended on a pause: the microphone is closed, so the pill stops saying "Listening" at once.
			if (ended)
			{
				JarvisIndicator.Thinking(CoreText.Get(Language(), "jarvis.thinking"), _logger);
				_logger.Info("Jarvis: tap to talk heard a pause; recording stopped.");
				return;
			}
			if (++round % 4 != 0) continue;
			JarvisMicChoice choice = ChooseJarvisMic();
			if (choice.Refusal is not null || choice.Id != micId) { CancelListening(choice.Refusal == "muted" ? "the microphone was muted" : "the microphone changed"); return; }
		}
		CancelListening("longest recording reached");
	}

	private JarvisMicChoice ChooseJarvisMic()
	{
		var view = MicSnapshot();
		var mics = view.Endpoints.Select(endpoint => new JarvisMic(endpoint.Id, endpoint.Muted, endpoint.IsCommunications, endpoint.IsConsole, endpoint.Name)).ToList();
		MicScope scope = CallsMicPolicy.Scope(view.Settings);
		string? callsMicId = CallsMicPolicy.Find(view.Settings, view.Endpoints)?.Id;
		return JarvisMicPolicy.Choose(mics, scope == MicScope.Calls, view.CoreMuted, callsMicId, WindowsGameBar.Interactive() == true);
	}

	private void CancelListening(string why)
	{
		JarvisRecorder? recorder;
		lock (_jarvisSync) { recorder = _recorder; _recorder = null; _recordMicId = null; }
		if (recorder is null) return;
		short[] samples = recorder.Stop();
		Array.Clear(samples);
		recorder.Dispose();
		JarvisIndicator.Listening(false, "", _logger);
		_logger.Info($"Jarvis: push-to-talk cancelled ({why}); nothing was sent.");
	}

	// The pill says "Thinking..." from the end of the recording until the answer, then shows the answer card (talk) or
	// goes away.
	private async Task<object> StopListeningAsync(bool steamTyping)
	{
		object result = new { ok = false, error = "failed" };
		try { return result = await StopListeningCoreAsync(steamTyping).ConfigureAwait(false); }
		finally
		{
			string? answer = null;
			try
			{
				using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(result));
				if (document.RootElement.TryGetProperty("answer", out JsonElement value) && value.ValueKind == JsonValueKind.String) answer = value.GetString();
			}
			catch (JsonException) { }
			if (!string.IsNullOrWhiteSpace(answer)) JarvisIndicator.Answer("JARVIS", answer!, CoreText.Get(Language(), "jarvis.answerHint"), _logger);
			else JarvisIndicator.Done(_logger);
		}
	}

	private async Task<object> StopListeningCoreAsync(bool steamTyping)
	{
		JarvisRecorder? recorder;
		JarvisListenMode mode;
		lock (_jarvisSync) { recorder = _recorder; mode = _recordMode; _recorder = null; _recordMicId = null; }
		if (recorder is null) return new { ok = false, error = "not-listening" };
		short[] samples = recorder.Stop();
		recorder.Dispose();
		JarvisIndicator.Listening(false, "", _logger);
		JarvisIndicator.Thinking(CoreText.Get(Language(), "jarvis.thinking"), _logger);
		string? skip = JarvisListenPolicy.AfterRecording(samples);
		if (skip is not null) { Array.Clear(samples); return new { ok = false, error = skip }; }
		(string? text, string? route, string? failure) = await TranscribeAsync(JarvisAudio.Bytes(samples)).ConfigureAwait(false);
		Array.Clear(samples);
		if (text is null) { _logger.Info($"Jarvis: speech not transcribed ({failure})."); return new { ok = false, error = failure ?? "not-understood" }; }
		_logger.Info($"Jarvis: speech transcribed ({route}, {mode.ToString().ToLowerInvariant()}).");
		if (mode == JarvisListenMode.Dictate)
		{
			if (steamTyping) return new { ok = true, mode = "dictate", text, typed = false };
			if (_antiCheat.Engaged) return new { ok = true, mode = "dictate", text, typed = false, error = "anti-cheat" };
			bool typed = JarvisTyper.Type(text);
			return new { ok = true, mode = "dictate", text, typed, error = typed ? null : "type-refused" };
		}
		var answer = await AskJarvisAsync(text, "talk").ConfigureAwait(false);
		if (answer.Text is { } spoken && JarvisSettings().Voice) _ = Task.Run(() => SpeakJarvisAsync(spoken));
		return new { ok = answer.Ok, mode = "talk", text, answer = answer.Text, error = answer.Error, refused = answer.Refused };
	}

	// Aincrad's Whisper over Wyoming, then through Home Assistant, then (only if chosen and keyed) cloud Whisper.
	private async Task<(string? Text, string? Route, string? Error)> TranscribeAsync(byte[] pcm)
	{
		JarvisRuntime settings = JarvisSettings();
		string? token = Secret(JarvisSecretStore.HaToken);
		string? openAi = settings.CloudWhisper ? Secret(JarvisSecretStore.OpenAiKey) : null;
		string? lastError = "no-route";
		try
		{
			foreach (string route in JarvisSpeechRoutes.Plan(settings.WyomingHost is not null, token is not null, openAi is not null, settings.CloudWhisper))
			{
				switch (route)
				{
					case JarvisSpeechRoutes.Wyoming:
						if (DateTimeOffset.Now < _wyomingDownUntil) continue;
						(string? text, string? error) = await WyomingClient.TranscribeAsync(settings.WyomingHost!, settings.WyomingPort, pcm, TimeSpan.FromSeconds(12), CancellationToken.None).ConfigureAwait(false);
						if (text is { Length: > 0 }) return (text, route, null);
						if (text is not null) return (null, route, "not-understood");
						lastError = error;
						// Aincrad's compose file publishes Whisper on the server's loopback only: skip the direct route a while.
						if (error is "unreachable" or "timeout") _wyomingDownUntil = DateTimeOffset.Now + TimeSpan.FromMinutes(10);
						break;
					case JarvisSpeechRoutes.HomeAssistant:
						JarvisReply ha = await _jarvisClient.HaTranscribeAsync(settings.HaUrl, token!, settings.SttEntity, pcm, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
						if (ha.Ok) return (ha.Text, route, null);
						if (ha.Error == "not-understood") return (null, route, "not-understood");
						lastError = ha.Error;
						break;
					case JarvisSpeechRoutes.OpenAi:
						JarvisReply cloud = await _jarvisClient.OpenAiTranscribeAsync(settings.OpenAiUrl, openAi!, JarvisAudio.Wav(pcm), TimeSpan.FromSeconds(20)).ConfigureAwait(false);
						if (cloud.Ok) return (cloud.Text, route, null);
						lastError = cloud.Error;
						break;
				}
			}
		}
		catch (Exception error) { _logger.Error("Jarvis: transcription failed.", error); lastError = "failed"; }
		return (null, null, lastError);
	}

	// ---------- screen helper ----------

	private async Task<object> AskScreenAsync(string? rawQuestion)
	{
		JarvisRuntime settings = JarvisSettings();
		if (!settings.Enabled) return new { ok = false, error = "off" };
		string? key = Secret(JarvisSecretStore.AnthropicKey);
		if (key is null) return new { ok = false, error = "needs-key" };
		string question = JarvisConfig.CleanText(rawQuestion, JarvisConfig.MaxQuestionChars);
		if (question.Length == 0) question = JarvisPrompts.DefaultScreenQuestion;
		if (!await _jarvisBusy.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false)) return new { ok = false, error = "busy" };
		try
		{
			(byte[]? jpeg, string method) = await Task.Run(() => JarvisScreenGrab.CaptureAsync(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
			// Every screenshot is announced, including while Stream mode sends the picture to someone else.
			JarvisIndicator.Flash(CoreText.Get(Language(), "jarvis.screenshot"), _logger);
			if (jpeg is null) { _logger.Info("Jarvis: screen helper: Windows gave no picture."); return new { ok = false, error = "no-picture" }; }
			string body = JarvisRequests.Claude(settings.Model, JarvisPrompts.ScreenInstructions(settings.SpoilerSafe), Array.Empty<JarvisTurn>(), question, jpeg, JarvisRequests.ScreenMaxTokens);
			int size = jpeg.Length;
			Array.Clear(jpeg);
			JarvisReply reply = await _jarvisClient.ClaudeAsync(key, body, TimeSpan.FromSeconds(45)).ConfigureAwait(false);
			body = "";
			_logger.Info($"Jarvis: screen helper ({method}, {size / 1024} KB) answered ({(reply.Ok ? "ok" : reply.Error)}); the screenshot is discarded.");
			if (!reply.Ok || reply.Text is null) return new { ok = false, error = reply.Error ?? "failed" };
			JarvisScreenAnswer answer = JarvisSpoiler.Parse(reply.Text, settings.SpoilerSafe);
			(string shown, bool more) = JarvisSpoiler.First(answer);
			lock (_jarvisSync) { _screenAnswer = answer; _screenRevealed = !more; }
			if (settings.SpeakScreen) _ = Task.Run(() => SpeakJarvisAsync(shown));
			return new { ok = true, text = shown, more, spoilerSafe = settings.SpoilerSafe };
		}
		catch (Exception error)
		{
			_logger.Error("Jarvis: screen helper failed.", error);
			return new { ok = false, error = "failed" };
		}
		finally { _jarvisBusy.Release(); }
	}

	private object ScreenMore()
	{
		JarvisScreenAnswer? answer;
		lock (_jarvisSync) { answer = _screenAnswer; if (answer is not null) _screenRevealed = true; }
		if (answer is null) return new { ok = false, error = "nothing" };
		if (JarvisSettings().SpeakScreen) _ = Task.Run(() => SpeakJarvisAsync(answer.Full));
		return new { ok = true, text = answer.Full, more = false };
	}

	// ---------- Health ----------

	private HealthRow JarvisRow()
	{
		JarvisRuntime settings = JarvisSettings();
		IReadOnlyDictionary<string, bool> saved = JarvisSecretStore.Saved(_paths.ConfigDirectory);
		string? error; bool answered;
		lock (_jarvisSync) { error = _jarvisLastError; answered = _jarvisEverAnswered; }
		return JarvisHealth.Row(settings.Enabled, settings.Backend, saved[JarvisSecretStore.HaToken], saved[JarvisSecretStore.AnthropicKey], error, answered, settings.HaUrl);
	}

	// ---------- HTTP ----------

	private object JarvisState()
	{
		JarvisRuntime settings = JarvisSettings();
		IReadOnlyDictionary<string, bool> saved = JarvisSecretStore.Saved(_paths.ConfigDirectory);
		string? error; bool answered; bool listening; JarvisListenMode mode; IReadOnlyList<JarvisTurn> turns; JarvisScreenAnswer? screen; bool revealed;
		lock (_jarvisSync)
		{
			error = _jarvisLastError; answered = _jarvisEverAnswered; listening = _recorder is not null; mode = _recordMode;
			turns = _jarvisHistory.Turns(DateTimeOffset.Now); screen = _screenAnswer; revealed = _screenRevealed;
		}
		(string? shown, bool more) = screen is null ? (null, false) : revealed ? (screen.Full, false) : JarvisSpoiler.First(screen);
		return new
		{
			enabled = settings.Enabled, backend = settings.Backend,
			status = settings.Enabled ? JarvisStatus.Code(settings.Backend, saved[JarvisSecretStore.HaToken], saved[JarvisSecretStore.AnthropicKey], error, answered) : "off",
			screenHelper = saved[JarvisSecretStore.AnthropicKey], spoilerSafe = settings.SpoilerSafe, voice = settings.Voice, speakScreen = settings.SpeakScreen,
			combos = new { dictate = settings.Combos.Dictate, talk = settings.Combos.Talk, screen = settings.Combos.Screen },
			// Core UI phase 1: a short tap on the talk combo opens Jarvis's Quick Access tab (src/jarvisCombos.ts).
			tapOpens = settings.TapOpens,
			listening = new { active = listening, mode = listening ? mode.ToString().ToLowerInvariant() : null },
			history = turns.Select(turn => new { role = turn.Role, text = turn.Text }).ToArray(),
			screen = shown is null ? null : new { text = shown, more },
			busy = _jarvisBusy.CurrentCount == 0,
			antiCheat = _antiCheat.Engaged,
		};
	}

	private static async Task<JsonElement?> ReadJarvisBodyAsync(HttpContext context)
	{
		try
		{
			if (context.Request.ContentLength is > 16384) return null;
			using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
			var buffer = new char[16385];
			int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			if (read > 16384) return null;
			using JsonDocument document = JsonDocument.Parse(new string(buffer, 0, read));
			return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
		}
		catch (Exception error) when (error is JsonException or IOException or InvalidOperationException) { return null; }
	}

	private static string? BodyText(JsonElement? body, string name) =>
		body is { } value && value.TryGetProperty(name, out JsonElement field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

	private void MapJarvis(WebApplication app)
	{
		app.MapGet("/jarvis/state", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(JarvisState()) : Results.StatusCode(403)));
		app.MapPost("/jarvis/chat", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			JsonElement? body = await ReadJarvisBodyAsync(context).ConfigureAwait(false);
			string? text = BodyText(body, "text");
			if (text is null) return Results.Json(new { ok = false, error = "bad-request" }, statusCode: 400);
			bool speak = body is { } value && value.TryGetProperty("speak", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
			var answer = await AskJarvisAsync(text, "Quick Access").ConfigureAwait(false);
			if (speak && answer.Text is { } spoken && JarvisSettings().Voice) _ = Task.Run(() => SpeakJarvisAsync(spoken));
			return Results.Json(new { ok = answer.Ok, text = answer.Text, error = answer.Error, refused = answer.Refused });
		}));
		app.MapPost("/jarvis/clear", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			ClearJarvis("Quick Access");
			return Results.Json(new { ok = true });
		}));
		app.MapPost("/jarvis/listen/start", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			JarvisListenMode? mode = JarvisListenPolicy.ParseMode(context.Request.Query["mode"].ToString());
			if (mode is null) return Results.Json(new { ok = false, error = "bad-request" }, statusCode: 400);
			bool auto = context.Request.Query["auto"].ToString() == "true";
			var started = StartListening(mode.Value, auto);
			// Over a game in exclusive fullscreen the pill may not show: the plugin then uses the orb and a toast.
			bool pillHidden = started.Ok && JarvisIndicator.ExclusiveFullscreen();
			if (pillHidden) _logger.Info("Jarvis: a game runs in exclusive fullscreen; the listening pill may be hidden, the plugin shows a toast instead.");
			return Results.Json(new { ok = started.Ok, error = started.Error, mic = started.Mic, partyHears = started.PartyHears, pillHidden });
		}));
		// Tap to talk: whether the recording is still running or ended on a pause (the plugin then asks for the answer).
		app.MapGet("/jarvis/listen/state", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			bool recording, ended;
			lock (_jarvisSync) { recording = _recorder is not null && !_recorder.Ended; ended = _recorder?.Ended == true; }
			return Results.Json(new { recording, ended });
		}));
		app.MapPost("/jarvis/listen/stop", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			bool steamTyping = context.Request.Query["steamTyping"].ToString() == "true";
			return Results.Json(await StopListeningAsync(steamTyping).ConfigureAwait(false));
		}));
		app.MapPost("/jarvis/listen/cancel", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			CancelListening("released too early or cancelled");
			return Results.Json(new { ok = true });
		}));
		app.MapPost("/jarvis/screen", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			JsonElement? body = await ReadJarvisBodyAsync(context).ConfigureAwait(false);
			return Results.Json(await AskScreenAsync(BodyText(body, "question")).ConfigureAwait(false));
		}));
		app.MapPost("/jarvis/screen/more", (Func<HttpContext, IResult>)(context => Local(context) ? Results.Json(ScreenMore()) : Results.StatusCode(403)));
		// Speak a given answer (Quick Access's speaker button), through the same voices as talk mode.
		app.MapPost("/jarvis/speak", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			JsonElement? body = await ReadJarvisBodyAsync(context).ConfigureAwait(false);
			string text = JarvisConfig.CleanText(BodyText(body, "text"));
			if (text.Length == 0) return Results.Json(new { ok = false, error = "empty" });
			_ = Task.Run(() => SpeakJarvisAsync(text));
			return Results.Json(new { ok = true });
		}));
		app.MapPost("/jarvis/speak/stop", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			JarvisSpeaker.Stop();
			return Results.Json(new { ok = true });
		}));
		// Dictated text the plugin could not type through Steam: typed with SendInput, never under anti-cheat.
		app.MapPost("/jarvis/type", (Func<HttpContext, Task<IResult>>)(async context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			JsonElement? body = await ReadJarvisBodyAsync(context).ConfigureAwait(false);
			string text = JarvisConfig.CleanText(BodyText(body, "text"));
			if (text.Length == 0) return Results.Json(new { ok = false, error = "empty" });
			if (_antiCheat.Engaged) return Results.Json(new { ok = false, error = "anti-cheat" });
			return Results.Json(new { ok = JarvisTyper.Type(text) });
		}));
		// The desktop app saved new Jarvis settings or secrets: forget the old status and probe again soon.
		app.MapPost("/jarvis/reload", (Func<HttpContext, IResult>)(context =>
		{
			if (!Local(context)) return Results.StatusCode(403);
			lock (_jarvisSync) { _jarvisLastError = null; _jarvisEverAnswered = false; }
			_jarvisProbedAt = DateTimeOffset.MinValue;
			_wyomingDownUntil = DateTimeOffset.MinValue;
			return Results.Json(new { ok = true });
		}));
	}
}
