using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Shared;

// Eternal Core: Jarvis's network calls (docs/eternal/JARVIS.md), shared by the agent and the desktop app's "Test
// connection". Every call has its own deadline and fails soft: a JarvisReply with an error code ("unreachable",
// "timeout", "token-refused", ...), never an exception. The token and keys go only into the Authorization / x-api-key
// header of the request they belong to; request and answer bodies are never logged or kept.
public sealed class JarvisClient : IDisposable
{
	private const int MaxAnswer = 256 * 1024;
	private const int MaxAudio = 8 * 1024 * 1024;
	private readonly HttpClient _http;
	private readonly string _anthropicUrl;

	public JarvisClient(HttpMessageHandler? handler = null, string anthropicMessagesUrl = JarvisRequests.AnthropicUrl)
	{
		_http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
		_http.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // each call has its own deadline
		_anthropicUrl = anthropicMessagesUrl;
	}

	public void Dispose() => _http.Dispose();

	// ---------- Home Assistant ----------

	public async Task<JarvisReply> HaConverseAsync(string baseUrl, string token, string? agentId, string text, string? conversationId, TimeSpan timeout, CancellationToken cancellation = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/conversation/process")
		{
			Content = new StringContent(JarvisRequests.HaConversation(text, agentId, conversationId), Encoding.UTF8, "application/json"),
		};
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		(int status, string? body, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
		return error is not null ? new JarvisReply(false, null, null, error) : JarvisRequests.ParseHaConversation(status, body);
	}

	// "API running." with the token, then the agent entity exists. Costs nothing (no conversation is started).
	public async Task<JarvisReply> HaTestAsync(string baseUrl, string token, string? agentId, TimeSpan timeout, CancellationToken cancellation = default)
	{
		using (var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/"))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			(int status, string? body, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
			if (error is not null) return new JarvisReply(false, null, null, error);
			if (status is 401 or 403) return new JarvisReply(false, null, null, "token-refused", status);
			if (status != 200 || body is null || !body.Contains("API running", StringComparison.Ordinal)) return new JarvisReply(false, null, null, "not-home-assistant", status);
		}
		if (string.IsNullOrEmpty(agentId)) return new JarvisReply(true, "ok", null, null, 200);
		using (var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/states/" + Uri.EscapeDataString(agentId)))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			(int status, _, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
			if (error is not null) return new JarvisReply(false, null, null, error);
			if (status == 404) return new JarvisReply(false, null, null, "agent-missing", status);
			return status == 200 ? new JarvisReply(true, "ok", null, null, status) : new JarvisReply(false, null, null, "refused", status);
		}
	}

	// Aincrad's Whisper through Home Assistant (POST /api/stt/<entity>), for when its Wyoming port is not reachable.
	public async Task<JarvisReply> HaTranscribeAsync(string baseUrl, string token, string sttEntity, byte[] pcm16k, TimeSpan timeout, CancellationToken cancellation = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/stt/" + Uri.EscapeDataString(sttEntity)) { Content = new ByteArrayContent(pcm16k) };
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		request.Headers.TryAddWithoutValidation("X-Speech-Content", JarvisRequests.SttHeader());
		request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
		(int status, string? body, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
		return error is not null ? new JarvisReply(false, null, null, error) : JarvisRequests.ParseTranscript(status, body);
	}

	// Aincrad's Piper through Home Assistant: tts_get_url, then the audio from the returned path (WAV or MP3).
	public async Task<(byte[]? Audio, string? Error)> HaSpeakAsync(string baseUrl, string token, string ttsEntity, string text, TimeSpan timeout, CancellationToken cancellation = default)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		limit.CancelAfter(timeout);
		string? path;
		using (var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/tts_get_url") { Content = new StringContent(JarvisRequests.TtsRequest(ttsEntity, text), Encoding.UTF8, "application/json") })
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			(int status, string? body, string? error) = await SendTextAsync(request, timeout, limit.Token).ConfigureAwait(false);
			if (error is not null) return (null, error);
			if (status is 401 or 403) return (null, "token-refused");
			path = status == 200 ? JarvisRequests.TtsPath(body) : null;
			if (path is null) return (null, "refused");
		}
		using (var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path))
		{
			try
			{
				using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
				if (!response.IsSuccessStatusCode) return (null, "refused");
				byte[]? audio = await ReadCappedAsync(response, MaxAudio, limit.Token).ConfigureAwait(false);
				return audio is { Length: > 0 } ? (audio, null) : (null, "empty");
			}
			catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return (null, "timeout"); }
			catch (HttpRequestException) { return (null, "unreachable"); }
		}
	}

	// ---------- the Claude API (the user's own key) ----------

	public async Task<JarvisReply> ClaudeAsync(string key, string body, TimeSpan timeout, CancellationToken cancellation = default)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, _anthropicUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
		request.Headers.TryAddWithoutValidation("x-api-key", key);
		request.Headers.TryAddWithoutValidation("anthropic-version", JarvisRequests.AnthropicVersion);
		(int status, string? answer, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
		return error is not null ? new JarvisReply(false, null, null, error) : JarvisRequests.ParseClaude(status, answer);
	}

	// GET /v1/models/<model>: checks the key and the model without a billed request.
	public async Task<JarvisReply> ClaudeTestAsync(string key, string model, TimeSpan timeout, CancellationToken cancellation = default)
	{
		string url = _anthropicUrl.Replace("/v1/messages", "/v1/models/", StringComparison.Ordinal) + Uri.EscapeDataString(model);
		using var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.TryAddWithoutValidation("x-api-key", key);
		request.Headers.TryAddWithoutValidation("anthropic-version", JarvisRequests.AnthropicVersion);
		(int status, string? answer, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
		if (error is not null) return new JarvisReply(false, null, null, error);
		if (status == 200) return new JarvisReply(true, "ok", null, null, status);
		JarvisReply parsed = JarvisRequests.ParseClaude(status, answer);
		return parsed with { Ok = false, Text = null };
	}

	// ---------- OpenAI-compatible cloud Whisper (optional, only with its key) ----------

	public async Task<JarvisReply> OpenAiTranscribeAsync(string baseUrl, string key, byte[] wav, TimeSpan timeout, CancellationToken cancellation = default)
	{
		var form = new MultipartFormDataContent();
		var file = new ByteArrayContent(wav);
		file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
		form.Add(file, "file", "speech.wav");
		form.Add(new StringContent("whisper-1"), "model");
		form.Add(new StringContent("en"), "language");
		form.Add(new StringContent("json"), "response_format");
		using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/audio/transcriptions") { Content = form };
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
		(int status, string? body, string? error) = await SendTextAsync(request, timeout, cancellation).ConfigureAwait(false);
		if (error is not null) return new JarvisReply(false, null, null, error);
		if (status is 401 or 403) return new JarvisReply(false, null, null, "key-refused", status);
		return JarvisRequests.ParseTranscript(status, body);
	}

	// ---------- plumbing ----------

	private async Task<(int Status, string? Body, string? Error)> SendTextAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellation)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		limit.CancelAfter(timeout);
		try
		{
			using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false);
			byte[]? bytes = await ReadCappedAsync(response, MaxAnswer, limit.Token).ConfigureAwait(false);
			return ((int)response.StatusCode, bytes is null ? null : Encoding.UTF8.GetString(bytes), bytes is null ? "too-large" : null);
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return (0, null, "timeout"); }
		catch (OperationCanceledException) { return (0, null, "cancelled"); }
		catch (HttpRequestException) { return (0, null, "unreachable"); }
		catch (IOException) { return (0, null, "unreachable"); }
	}

	private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, int maximum, CancellationToken cancellation)
	{
		await using Stream stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
		var buffer = new MemoryStream();
		var chunk = new byte[16384];
		while (true)
		{
			int read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false);
			if (read == 0) return buffer.ToArray();
			if (buffer.Length + read > maximum) return null;
			buffer.Write(chunk, 0, read);
		}
	}
}
