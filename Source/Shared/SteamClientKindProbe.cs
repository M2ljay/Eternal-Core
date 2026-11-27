using System;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Playhub.Shared;

// Eternal Core: asks the running Steam which UI it loads (DeckySteamPairing.cs), through the CEF
// debug port Core already enables for Decky (.cef-enable-remote-debugging): GET /json, the
// SharedJSContext page, one Runtime.evaluate of "typeof window.webpackChunksteamui". Read-only,
// time-boxed, loopback only; any failure is Unknown with a reason for the log, never an exception.
public readonly record struct SteamClientKindReading(SteamUiKind Kind, string Detail);

// One Runtime.evaluate in SharedJSContext: the string it returned (null when there was none) and why.
public readonly record struct SteamEvaluation(string? Value, string Detail);

public static class SteamClientKindProbe
{
    public const int DebugPort = 8080;
    private const int MaxListBytes = 512 * 1024, MaxReplyBytes = 64 * 1024;

    public static async Task<SteamClientKindReading> DetectAsync(CancellationToken cancellation = default,
        HttpMessageHandler? handler = null, TimeSpan? budget = null, int port = DebugPort)
    {
        SteamEvaluation answer = await EvaluateAsync(DeckySteamPairing.ProbeExpression, cancellation, handler, budget, port).ConfigureAwait(false);
        if (answer.Value is null) return new(SteamUiKind.Unknown, answer.Detail);
        SteamUiKind kind = DeckySteamPairing.ParseProbeValue(answer.Value);
        return new(kind, kind == SteamUiKind.Unknown ? "unexpected answer from Steam: " + answer.Value : "typeof window.webpackChunksteamui = " + answer.Value);
    }

    // The expression must produce a string (wrap others in String(...) or JSON.stringify(...)).
    public static async Task<SteamEvaluation> EvaluateAsync(string expression, CancellationToken cancellation = default,
        HttpMessageHandler? handler = null, TimeSpan? budget = null, int port = DebugPort)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(budget ?? TimeSpan.FromSeconds(4));
        string list;
        try
        {
            using var http = handler is null
                ? new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
                : new HttpClient(handler, disposeHandler: false);
            using HttpResponseMessage response = await http.GetAsync($"http://127.0.0.1:{port}/json", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(null, $"Steam's debug port answered {(int)response.StatusCode}");
            using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            list = Encoding.UTF8.GetString(await ReadBoundedAsync(stream, MaxListBytes, timeout.Token).ConfigureAwait(false));
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or InvalidOperationException)
        {
            return new(null, "Steam is not running or its debug port is closed (" + error.GetType().Name + ")");
        }
        Uri? socketUri = DeckySteamPairing.SelectSharedContext(list, port);
        if (socketUri is null) return new(null, "no single SharedJSContext page on Steam's debug port yet");
        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.Proxy = null;
            await socket.ConnectAsync(socketUri, timeout.Token).ConfigureAwait(false);
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { id = 1, method = "Runtime.evaluate",
                @params = new { expression, returnByValue = true, awaitPromise = true } });
            await socket.SendAsync(request, WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
            byte[] buffer = new byte[8192];
            while (socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (part.MessageType != WebSocketMessageType.Text) return new(null, "Steam closed the debug session");
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > MaxReplyBytes) return new(null, "Steam's reply was too large");
                } while (!part.EndOfMessage);
                string reply = Encoding.UTF8.GetString(message.ToArray());
                if (!reply.Contains("\"id\":1", StringComparison.Ordinal)) continue; // an event, not our answer
                string? value = DeckySteamPairing.ParseEvaluateReply(reply);
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false); } catch { }
                return new(value, value is null ? "Steam gave no string answer" : "answered");
            }
            return new(null, "Steam closed the debug session");
        }
        catch (Exception error) when (error is WebSocketException or IOException or OperationCanceledException or InvalidOperationException or JsonException)
        {
            return new(null, "could not ask Steam (" + error.GetType().Name + ")");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellation)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            output.Write(buffer, 0, count);
            if (output.Length > maximum) throw new IOException("Steam's target list exceeds its bound.");
        }
        return output.ToArray();
    }
}
