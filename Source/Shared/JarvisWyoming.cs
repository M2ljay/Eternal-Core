using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Shared;

// Eternal Core: the Wyoming protocol (github.com/rhasspy/wyoming), which Aincrad's Whisper (port 10300) and Piper (port
// 10200) speak. One event = one JSON header line ({"type", "data", "data_length", "payload_length", "version"} and a
// new line), then data_length bytes of extra JSON data (merged into "data"), then payload_length bytes of payload
// (audio). Speech to text: transcribe, audio-start, audio-chunk..., audio-stop, then wait for "transcript". Text to
// speech: synthesize, then audio-start, audio-chunk..., audio-stop.
//
// Every call is time-boxed by its caller's token and a deadline of its own, and fails soft (null). Nothing is logged.
public sealed record WyomingEvent(string Type, JsonObject Data, byte[] Payload);

public static class WyomingWire
{
	public const string Version = "1.5.2";
	public const int ChunkSamples = 1024;
	private const int MaxHeader = 64 * 1024;
	private const int MaxPayload = 32 * 1024 * 1024;

	public static byte[] Frame(string type, JsonObject? data = null, byte[]? payload = null)
	{
		var header = new JsonObject { ["type"] = type, ["version"] = Version };
		if (data is not null && data.Count > 0) header["data"] = data;
		if (payload is { Length: > 0 }) header["payload_length"] = payload.Length;
		byte[] line = Encoding.UTF8.GetBytes(header.ToJsonString() + "\n");
		if (payload is not { Length: > 0 }) return line;
		var frame = new byte[line.Length + payload.Length];
		line.CopyTo(frame, 0);
		payload.CopyTo(frame, line.Length);
		return frame;
	}

	public static JsonObject Format(int rate, int width, int channels) => new() { ["rate"] = rate, ["width"] = width, ["channels"] = channels };

	// The frames of one transcription request: 16 kHz mono 16-bit PCM, in chunks of ChunkSamples.
	public static IEnumerable<byte[]> TranscribeFrames(byte[] pcm, string language = "en")
	{
		yield return Frame("transcribe", new JsonObject { ["language"] = language });
		yield return Frame("audio-start", Format(JarvisAudio.SampleRate, 2, 1));
		for (int offset = 0; offset < pcm.Length; offset += ChunkSamples * 2)
		{
			int length = Math.Min(ChunkSamples * 2, pcm.Length - offset);
			var chunk = new byte[length];
			Array.Copy(pcm, offset, chunk, 0, length);
			yield return Frame("audio-chunk", Format(JarvisAudio.SampleRate, 2, 1), chunk);
		}
		yield return Frame("audio-stop");
	}

	// Reads one event; null at the end of the stream. Throws InvalidDataException on a malformed or oversized event.
	public static async Task<WyomingEvent?> ReadAsync(Stream stream, CancellationToken cancellation)
	{
		var line = new MemoryStream();
		var one = new byte[1];
		while (true)
		{
			int read = await stream.ReadAsync(one, cancellation).ConfigureAwait(false);
			if (read == 0) return line.Length == 0 ? null : throw new InvalidDataException("Wyoming: stream ended inside a header.");
			if (one[0] == (byte)'\n') break;
			if (line.Length >= MaxHeader) throw new InvalidDataException("Wyoming: header too long.");
			line.WriteByte(one[0]);
		}
		JsonObject header;
		try { header = JsonNode.Parse(line.ToArray()) as JsonObject ?? throw new InvalidDataException("Wyoming: header is not an object."); }
		catch (JsonException error) { throw new InvalidDataException("Wyoming: header is not JSON.", error); }
		string type = header["type"] is JsonValue typeValue && typeValue.TryGetValue(out string? text) ? text : throw new InvalidDataException("Wyoming: no type.");
		var data = header["data"] is JsonObject inline ? (JsonObject)inline.DeepClone() : new JsonObject();
		int dataLength = Length(header, "data_length"), payloadLength = Length(header, "payload_length");
		if (dataLength > 0)
		{
			byte[] extra = await ReadExactlyAsync(stream, dataLength, cancellation).ConfigureAwait(false);
			try
			{
				if (JsonNode.Parse(extra) is JsonObject more)
					foreach ((string key, JsonNode? value) in more) data[key] = value?.DeepClone();
			}
			catch (JsonException error) { throw new InvalidDataException("Wyoming: data is not JSON.", error); }
		}
		byte[] payload = payloadLength > 0 ? await ReadExactlyAsync(stream, payloadLength, cancellation).ConfigureAwait(false) : Array.Empty<byte>();
		return new WyomingEvent(type, data, payload);
	}

	private static int Length(JsonObject header, string name)
	{
		if (header[name] is null) return 0;
		if (header[name] is not JsonValue value || !value.TryGetValue(out long length)) throw new InvalidDataException($"Wyoming: {name} is not a number.");
		if (length < 0 || length > MaxPayload) throw new InvalidDataException($"Wyoming: {name} out of range.");
		return (int)length;
	}

	private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellation)
	{
		var buffer = new byte[length];
		int done = 0;
		while (done < length)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(done, length - done), cancellation).ConfigureAwait(false);
			if (read == 0) throw new InvalidDataException("Wyoming: stream ended inside an event.");
			done += read;
		}
		return buffer;
	}
}

public sealed record WyomingAudio(byte[] Pcm, int Rate, int Width, int Channels);

public static class WyomingClient
{
	// The transcript, "" when Whisper heard nothing, or null when the server could not be reached or failed.
	public static async Task<(string? Text, string? Error)> TranscribeAsync(string host, int port, byte[] pcm16k, TimeSpan timeout, CancellationToken cancellation, string language = "en")
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		limit.CancelAfter(timeout);
		try
		{
			using var client = new TcpClient { NoDelay = true };
			await client.ConnectAsync(host, port, limit.Token).ConfigureAwait(false);
			using NetworkStream stream = client.GetStream();
			foreach (byte[] frame in WyomingWire.TranscribeFrames(pcm16k, language))
				await stream.WriteAsync(frame, limit.Token).ConfigureAwait(false);
			await stream.FlushAsync(limit.Token).ConfigureAwait(false);
			while (await WyomingWire.ReadAsync(stream, limit.Token).ConfigureAwait(false) is { } item)
			{
				if (item.Type == "transcript")
					return (item.Data["text"] is JsonValue value && value.TryGetValue(out string? text) ? text.Trim() : "", null);
				if (item.Type == "error") return (null, "server-error");
			}
			return (null, "closed");
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return (null, "timeout"); }
		catch (SocketException) { return (null, "unreachable"); }
		catch (Exception error) when (error is IOException or InvalidDataException or ObjectDisposedException) { return (null, "bad-answer"); }
	}

	// Piper over Wyoming: the synthesized audio, or null.
	public static async Task<WyomingAudio?> SynthesizeAsync(string host, int port, string text, string? voice, TimeSpan timeout, CancellationToken cancellation)
	{
		using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		limit.CancelAfter(timeout);
		try
		{
			using var client = new TcpClient { NoDelay = true };
			await client.ConnectAsync(host, port, limit.Token).ConfigureAwait(false);
			using NetworkStream stream = client.GetStream();
			var data = new JsonObject { ["text"] = text };
			if (!string.IsNullOrEmpty(voice)) data["voice"] = new JsonObject { ["name"] = voice };
			await stream.WriteAsync(WyomingWire.Frame("synthesize", data), limit.Token).ConfigureAwait(false);
			var audio = new MemoryStream();
			int rate = 22050, width = 2, channels = 1;
			while (await WyomingWire.ReadAsync(stream, limit.Token).ConfigureAwait(false) is { } item)
			{
				if (item.Type is "audio-start" or "audio-chunk")
				{
					if (item.Data["rate"] is JsonValue r && r.TryGetValue(out int rateValue)) rate = rateValue;
					if (item.Data["width"] is JsonValue w && w.TryGetValue(out int widthValue)) width = widthValue;
					if (item.Data["channels"] is JsonValue c && c.TryGetValue(out int channelValue)) channels = channelValue;
					audio.Write(item.Payload);
				}
				else if (item.Type == "audio-stop") return new WyomingAudio(audio.ToArray(), rate, width, channels);
				else if (item.Type == "error") return null;
			}
			return null;
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return null; }
		catch (Exception error) when (error is SocketException or IOException or InvalidDataException or ObjectDisposedException) { return null; }
	}
}
