using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

// Eternal Core Mark III: network half of LG webOS TV control. Plain .NET (sockets and
// ClientWebSocket), no Windows APIs, so the loopback tests run on Linux too.
//
// Secrets: the client key is only ever written into the register message. Nothing in this
// file logs or returns it except RegisterAsync's result, which TvControlService stores
// encrypted and never logs.
internal static class LgWebOsClient
{
	// Broadcast the magic packet on the usual WoL ports. A /24 directed broadcast for the
	// TV's address is added because some routers drop 255.255.255.255 on wired segments.
	internal static async Task<int> SendWakeAsync(byte[] mac, string? tvAddress, CancellationToken cancellation)
	{
		byte[] packet = LgWebOsProtocol.MagicPacket(mac);
		var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, LgWebOsProtocol.WakeOnLanPort), new(IPAddress.Broadcast, 7) };
		if (IPAddress.TryParse(tvAddress?.Trim(), out IPAddress? tv) && tv.AddressFamily == AddressFamily.InterNetwork)
		{
			byte[] bytes = tv.GetAddressBytes();
			bytes[3] = 255;
			targets.Add(new(new IPAddress(bytes), LgWebOsProtocol.WakeOnLanPort));
		}
		int sent = 0;
		using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
		for (int round = 0; round < 3; round++)
		{
			foreach (IPEndPoint target in targets)
			{
				try { await udp.SendAsync(packet, target, cancellation).ConfigureAwait(false); sent++; }
				catch (SocketException) { /* An unreachable broadcast route is not fatal: the others may work. */ }
			}
			await Task.Delay(120, cancellation).ConfigureAwait(false);
		}
		return sent;
	}

	internal static async Task<IReadOnlyList<LgWebOsProtocol.DiscoveredTv>> DiscoverAsync(TimeSpan window, CancellationToken cancellation)
	{
		var found = new Dictionary<string, LgWebOsProtocol.DiscoveredTv>(StringComparer.OrdinalIgnoreCase);
		using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
		byte[] request = LgWebOsProtocol.SsdpSearchRequest();
		var multicast = new IPEndPoint(IPAddress.Parse(LgWebOsProtocol.SsdpAddress), LgWebOsProtocol.SsdpPort);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		timeout.CancelAfter(window);
		try
		{
			for (int i = 0; i < 2; i++) await udp.SendAsync(request, multicast, timeout.Token).ConfigureAwait(false);
			while (!timeout.IsCancellationRequested)
			{
				UdpReceiveResult reply = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
				string address = reply.RemoteEndPoint.Address.ToString();
				if (LgWebOsProtocol.ParseSsdpReply(Encoding.UTF8.GetString(reply.Buffer), address) is { } tv) found[address] = tv;
			}
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
		catch (SocketException) { }
		return new List<LgWebOsProtocol.DiscoveredTv>(found.Values);
	}

	internal enum RegisterOutcome { Registered, PromptTimedOut, Rejected, Failed }

	internal readonly record struct RegisterResult(RegisterOutcome Outcome, string? ClientKey, string? Detail);

	internal sealed class Session : IAsyncDisposable
	{
		private readonly ClientWebSocket _socket;
		private int _next;
		private Session(ClientWebSocket socket, bool secure, string? certificate)
		{ _socket = socket; Secure = secure; CertificateSha256 = certificate; }

		internal bool Secure { get; }
		internal string? CertificateSha256 { get; }

		// wss://tv:3001 first. The TV's certificate is self-signed, so it is pinned instead
		// of chain-validated: pairing records its SHA-256, later sessions require the same
		// one. ws://tv:3000 is tried only when 3001 does not answer at all, never after a
		// certificate mismatch. With a pinned certificate the plain port is not used at all,
		// so the client key never travels unencrypted to a TV that has a secure port.
		internal static async Task<Session> ConnectAsync(string address, string? pinnedCertificateSha256, int? plainPort,
			TimeSpan timeout, CancellationToken cancellation)
		{
			if (!IPAddress.TryParse(address?.Trim(), out IPAddress? ip)) throw new ArgumentException("The TV address is not an IP address.", nameof(address));
			string host = ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString();
			string? seen = null;
			bool mismatch = false;
			var secure = new ClientWebSocket();
			secure.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
			secure.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
			{
				if (certificate is null) return false;
				seen = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
				mismatch = !string.IsNullOrEmpty(pinnedCertificateSha256)
					&& !string.Equals(seen, pinnedCertificateSha256, StringComparison.OrdinalIgnoreCase);
				return !mismatch;
			};
			using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
			{
				attempt.CancelAfter(timeout);
				try
				{
					await secure.ConnectAsync(new Uri($"wss://{host}:{LgWebOsProtocol.SecurePort}"), attempt.Token).ConfigureAwait(false);
					return new Session(secure, true, seen);
				}
				catch (Exception) when (!cancellation.IsCancellationRequested)
				{
					secure.Dispose();
					if (mismatch) throw new TvCertificateChangedException();
					if (!string.IsNullOrEmpty(pinnedCertificateSha256) || plainPort is null) throw;
				}
			}
			var plain = new ClientWebSocket();
			using var fallback = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
			fallback.CancelAfter(timeout);
			try
			{
				await plain.ConnectAsync(new Uri($"ws://{host}:{plainPort.Value}"), fallback.Token).ConfigureAwait(false);
				return new Session(plain, false, null);
			}
			catch { plain.Dispose(); throw; }
		}

		internal async Task<RegisterResult> RegisterAsync(string? clientKey, TimeSpan timeout, Action? onPrompt, CancellationToken cancellation)
		{
			string id = "register_" + Interlocked.Increment(ref _next).ToString(System.Globalization.CultureInfo.InvariantCulture);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
			deadline.CancelAfter(timeout);
			try
			{
				await SendAsync(LgWebOsProtocol.RegisterMessage(id, clientKey), deadline.Token).ConfigureAwait(false);
				while (true)
				{
					LgWebOsProtocol.SsapReply reply = LgWebOsProtocol.ParseReply(await ReceiveAsync(deadline.Token).ConfigureAwait(false));
					if (reply.Id != id) continue;
					if (reply.Type == "registered" && !string.IsNullOrWhiteSpace(reply.ClientKey))
						return new(RegisterOutcome.Registered, reply.ClientKey, null);
					if (reply.Type == "response" && string.Equals(reply.PairingType, "PROMPT", StringComparison.OrdinalIgnoreCase))
					{ onPrompt?.Invoke(); continue; }
					if (reply.Type == "error")
						return new(reply.Error?.Contains("403", StringComparison.Ordinal) == true ? RegisterOutcome.Rejected : RegisterOutcome.Failed, null, reply.Error);
				}
			}
			catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
			{
				return new(RegisterOutcome.PromptTimedOut, null, null);
			}
			catch (WebSocketException error) { return new(RegisterOutcome.Failed, null, error.Message); }
		}

		internal async Task<LgWebOsProtocol.SsapReply> RequestAsync(Func<string, string> message, TimeSpan timeout, CancellationToken cancellation)
		{
			string id = "core_" + Interlocked.Increment(ref _next).ToString(System.Globalization.CultureInfo.InvariantCulture);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
			deadline.CancelAfter(timeout);
			await SendAsync(message(id), deadline.Token).ConfigureAwait(false);
			while (true)
			{
				LgWebOsProtocol.SsapReply reply = LgWebOsProtocol.ParseReply(await ReceiveAsync(deadline.Token).ConfigureAwait(false));
				if (reply.Id == id && reply.Type is "response" or "error") return reply;
			}
		}

		private Task SendAsync(string text, CancellationToken cancellation) =>
			_socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellation);

		private async Task<string> ReceiveAsync(CancellationToken cancellation)
		{
			var buffer = new byte[8192];
			using var message = new System.IO.MemoryStream();
			while (true)
			{
				WebSocketReceiveResult result = await _socket.ReceiveAsync(buffer, cancellation).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("The TV closed the connection.");
				message.Write(buffer, 0, result.Count);
				if (message.Length > 1024 * 1024) throw new WebSocketException("The TV sent an oversized message.");
				if (result.EndOfMessage) return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
			}
		}

		public async ValueTask DisposeAsync()
		{
			try
			{
				if (_socket.State == WebSocketState.Open)
				{
					using var close = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
					await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", close.Token).ConfigureAwait(false);
				}
			}
			catch { /* Closing is best effort; the TV drops idle sockets itself. */ }
			_socket.Dispose();
		}
	}
}

internal sealed class TvCertificateChangedException : Exception
{
	internal TvCertificateChangedException() : base("The TV presented a different certificate than the one recorded at pairing. Pair the TV again.") { }
}
