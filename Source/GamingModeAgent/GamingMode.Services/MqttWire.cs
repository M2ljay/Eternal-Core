using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

// Eternal Core batch 2 (docs/eternal/BATCH-2.md): the smallest MQTT 3.1.1 client the Home Assistant bridge needs.
// CONNECT (user name, password, a retained last will), PUBLISH at QoS 0 (retained state and discovery), SUBSCRIBE
// at QoS 0 (commands), PINGREQ and DISCONNECT; it understands CONNACK, SUBACK, PINGRESP and incoming PUBLISH at
// QoS 0 or 1 (answered with PUBACK). Written here instead of adding MQTTnet: the agent ships as one self-contained
// file without NuGet packages, the Linux test projects build offline, and this is all the bridge uses. Spec:
// MQTT Version 3.1.1, OASIS Standard (2014), sections 2 and 3.
internal sealed record MqttPacket(int Type, int Flags, byte[] Body);

internal sealed record MqttMessage(string Topic, byte[] Payload, bool Retain, int Qos, ushort PacketId)
{
	internal string Text => Encoding.UTF8.GetString(Payload);
}

internal sealed class MqttProtocolException : Exception
{
	internal MqttProtocolException(string message) : base(message) { }
}

internal static class MqttWire
{
	internal const int Connect = 1, ConnAck = 2, Publish = 3, PubAck = 4, Subscribe = 8, SubAck = 9, PingReq = 12, PingResp = 13, Disconnect = 14;
	// Far more than any message the bridge sends or expects; a larger packet means a confused broker or a wrong port.
	internal const int MaxPacket = 256 * 1024;

	internal static byte[] ConnectPacket(string clientId, string? user, string? password, ushort keepAliveSeconds,
		string? willTopic, string? willPayload, bool willRetain)
	{
		var body = new List<byte>();
		AddString(body, "MQTT");
		body.Add(4); // protocol level 3.1.1
		byte flags = 0x02; // clean session
		if (willTopic is not null) flags |= (byte)(0x04 | (willRetain ? 0x20 : 0));
		bool hasUser = !string.IsNullOrEmpty(user), hasPassword = hasUser && password is not null;
		if (hasUser) flags |= 0x80;
		if (hasPassword) flags |= 0x40;
		body.Add(flags);
		body.Add((byte)(keepAliveSeconds >> 8));
		body.Add((byte)keepAliveSeconds);
		AddString(body, clientId);
		if (willTopic is not null)
		{
			AddString(body, willTopic);
			AddBytes(body, Encoding.UTF8.GetBytes(willPayload ?? ""));
		}
		if (hasUser) AddString(body, user!);
		if (hasPassword) AddBytes(body, Encoding.UTF8.GetBytes(password!));
		return Frame(Connect << 4, body);
	}

	internal static byte[] PublishPacket(string topic, byte[] payload, bool retain)
	{
		ValidateTopic(topic, filter: false);
		var body = new List<byte>(topic.Length + payload.Length + 4);
		AddString(body, topic);
		body.AddRange(payload);
		return Frame((Publish << 4) | (retain ? 1 : 0), body);
	}

	internal static byte[] PublishPacket(string topic, string payload, bool retain) => PublishPacket(topic, Encoding.UTF8.GetBytes(payload), retain);

	internal static byte[] SubscribePacket(ushort packetId, IReadOnlyList<string> filters)
	{
		if (packetId == 0) throw new ArgumentOutOfRangeException(nameof(packetId));
		if (filters.Count == 0) throw new ArgumentException("At least one topic filter.", nameof(filters));
		var body = new List<byte> { (byte)(packetId >> 8), (byte)packetId };
		foreach (string filter in filters)
		{
			ValidateTopic(filter, filter: true);
			AddString(body, filter);
			body.Add(0); // QoS 0
		}
		return Frame((Subscribe << 4) | 0x02, body);
	}

	internal static byte[] PubAckPacket(ushort packetId) => new byte[] { PubAck << 4, 2, (byte)(packetId >> 8), (byte)packetId };
	internal static byte[] PingReqPacket() => new byte[] { PingReq << 4, 0 };
	internal static byte[] DisconnectPacket() => new byte[] { Disconnect << 4, 0 };

	// One whole packet from the start of buffer, or null when more bytes are needed. Throws MqttProtocolException
	// for something that cannot be MQTT (a length over MaxPacket or a five-byte length field).
	internal static MqttPacket? TryRead(ReadOnlySpan<byte> buffer, out int consumed)
	{
		consumed = 0;
		if (buffer.Length < 2) return null;
		int length = 0, multiplier = 1, index = 1;
		while (true)
		{
			if (index >= buffer.Length) return null;
			if (index > 4) throw new MqttProtocolException("remaining length longer than four bytes");
			byte digit = buffer[index++];
			length += (digit & 0x7F) * multiplier;
			if ((digit & 0x80) == 0) break;
			multiplier *= 128;
		}
		if (length > MaxPacket) throw new MqttProtocolException($"packet of {length} bytes");
		if (buffer.Length < index + length) return null;
		consumed = index + length;
		return new MqttPacket(buffer[0] >> 4, buffer[0] & 0x0F, buffer.Slice(index, length).ToArray());
	}

	// CONNACK's return code: 0 accepted, 1 protocol version, 2 client id, 3 server unavailable, 4 bad user name or
	// password, 5 not authorised.
	internal static int ConnAckCode(MqttPacket packet)
	{
		if (packet.Type != ConnAck || packet.Body.Length != 2) throw new MqttProtocolException("expected CONNACK");
		return packet.Body[1];
	}

	internal static MqttMessage ReadPublish(MqttPacket packet)
	{
		if (packet.Type != Publish) throw new MqttProtocolException("expected PUBLISH");
		int qos = (packet.Flags >> 1) & 0x03;
		if (qos == 3) throw new MqttProtocolException("QoS 3");
		byte[] body = packet.Body;
		if (body.Length < 2) throw new MqttProtocolException("short PUBLISH");
		int topicLength = BinaryPrimitives.ReadUInt16BigEndian(body);
		int offset = 2 + topicLength;
		if (offset > body.Length) throw new MqttProtocolException("short PUBLISH topic");
		string topic = Encoding.UTF8.GetString(body, 2, topicLength);
		ushort id = 0;
		if (qos > 0)
		{
			if (offset + 2 > body.Length) throw new MqttProtocolException("short PUBLISH id");
			id = BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(offset));
			offset += 2;
		}
		return new MqttMessage(topic, body[offset..], (packet.Flags & 1) == 1, qos, id);
	}

	internal static string Describe(int connAckCode) => connAckCode switch
	{
		0 => "accepted",
		1 => "protocol version refused",
		2 => "client id refused",
		3 => "broker unavailable",
		4 => "user name or password refused",
		5 => "not authorised",
		_ => "refused (" + connAckCode + ")",
	};

	// Topic names carry no wildcards; filters may use + and # (# only last). No empty topic, no NUL.
	internal static void ValidateTopic(string topic, bool filter)
	{
		if (string.IsNullOrEmpty(topic) || Encoding.UTF8.GetByteCount(topic) > 65535 || topic.Contains('\0')) throw new ArgumentException("Invalid MQTT topic.", nameof(topic));
		if (!filter && (topic.Contains('+') || topic.Contains('#'))) throw new ArgumentException("Wildcards in an MQTT topic name.", nameof(topic));
		if (filter)
		{
			int hash = topic.IndexOf('#');
			if (hash >= 0 && (hash != topic.Length - 1 || (hash > 0 && topic[hash - 1] != '/'))) throw new ArgumentException("Misplaced # in an MQTT filter.", nameof(topic));
		}
	}

	// MQTT topic filter matching (3.1.1 section 4.7), for routing incoming messages.
	internal static bool Matches(string filter, string topic)
	{
		string[] f = filter.Split('/'), t = topic.Split('/');
		for (int index = 0; index < f.Length; index++)
		{
			if (f[index] == "#") return true;
			if (index >= t.Length) return false;
			if (f[index] != "+" && f[index] != t[index]) return false;
		}
		return f.Length == t.Length;
	}

	private static void AddString(List<byte> body, string value) => AddBytes(body, Encoding.UTF8.GetBytes(value));

	private static void AddBytes(List<byte> body, byte[] value)
	{
		if (value.Length > 65535) throw new ArgumentException("MQTT string longer than 65535 bytes.");
		body.Add((byte)(value.Length >> 8));
		body.Add((byte)value.Length);
		body.AddRange(value);
	}

	private static byte[] Frame(int header, List<byte> body)
	{
		if (body.Count > MaxPacket) throw new ArgumentException("MQTT packet too large.");
		var packet = new List<byte>(body.Count + 5) { (byte)header };
		int length = body.Count;
		do
		{
			byte digit = (byte)(length % 128);
			length /= 128;
			if (length > 0) digit |= 0x80;
			packet.Add(digit);
		} while (length > 0);
		packet.AddRange(body);
		return packet.ToArray();
	}
}

// One connection to the broker. ConnectAsync performs CONNECT/CONNACK; RunAsync then reads until the connection
// drops (or cancellation), answers QoS 1 messages, sends PINGREQ every half keep-alive and gives up when nothing
// arrives for 1.5 keep-alives. Writes are serialised. Never logs payloads: the caller decides what to log, and the
// password never leaves ConnectPacket.
internal sealed class MqttConnection : IAsyncDisposable
{
	private readonly TcpClient _client;
	private readonly Stream _stream;
	private readonly SemaphoreSlim _write = new(1, 1);
	private readonly TimeSpan _keepAlive;
	private readonly byte[] _buffer = new byte[16 * 1024];
	private byte[] _pending = Array.Empty<byte>();
	private ushort _nextId;
	private DateTimeOffset _lastReceived = DateTimeOffset.UtcNow;

	private MqttConnection(TcpClient client, Stream stream, TimeSpan keepAlive)
	{
		_client = client; _stream = stream; _keepAlive = keepAlive;
	}

	internal sealed record Options(string Host, int Port, string ClientId, string? User, string? Password, ushort KeepAliveSeconds,
		string? WillTopic, string? WillPayload, bool WillRetain);

	// The connection, or the CONNACK code / failure that refused it. Never throws for network trouble.
	internal static async Task<(MqttConnection? Connection, int? Code, string? Failure)> ConnectAsync(Options options, TimeSpan timeout, CancellationToken cancellation)
	{
		var client = new TcpClient { NoDelay = true };
		try
		{
			using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
			limit.CancelAfter(timeout);
			await client.ConnectAsync(options.Host, options.Port, limit.Token).ConfigureAwait(false);
			Stream stream = client.GetStream();
			var connection = new MqttConnection(client, stream, TimeSpan.FromSeconds(Math.Max((int)options.KeepAliveSeconds, 5)));
			await connection.SendAsync(MqttWire.ConnectPacket(options.ClientId, options.User, options.Password, options.KeepAliveSeconds,
				options.WillTopic, options.WillPayload, options.WillRetain), limit.Token).ConfigureAwait(false);
			MqttPacket? first = await connection.ReadPacketAsync(limit.Token).ConfigureAwait(false);
			if (first is null) { client.Dispose(); return (null, null, "closed"); }
			int code = MqttWire.ConnAckCode(first);
			if (code != 0) { client.Dispose(); return (null, code, MqttWire.Describe(code)); }
			return (connection, 0, null);
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { client.Dispose(); return (null, null, "timeout"); }
		catch (SocketException error) { client.Dispose(); return (null, null, "socket " + error.SocketErrorCode); }
		catch (IOException) { client.Dispose(); return (null, null, "closed"); }
		catch (MqttProtocolException error) { client.Dispose(); return (null, null, "not MQTT (" + error.Message + ")"); }
		catch (ArgumentException) { client.Dispose(); return (null, null, "bad address"); }
		catch { client.Dispose(); throw; }
	}

	internal Task PublishAsync(string topic, string payload, bool retain, CancellationToken cancellation) =>
		SendAsync(MqttWire.PublishPacket(topic, payload, retain), cancellation);

	internal Task SubscribeAsync(IReadOnlyList<string> filters, CancellationToken cancellation)
	{
		_nextId = (ushort)(_nextId == ushort.MaxValue ? 1 : _nextId + 1);
		return SendAsync(MqttWire.SubscribePacket(_nextId, filters), cancellation);
	}

	internal async Task DisconnectAsync()
	{
		try
		{
			using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			await SendAsync(MqttWire.DisconnectPacket(), limit.Token).ConfigureAwait(false);
		}
		catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
	}

	// Reads until the broker closes the connection, the keep-alive runs out or cancellation. onMessage gets each
	// incoming PUBLISH; its exceptions are the caller's (they end the connection).
	internal async Task<string> RunAsync(Func<MqttMessage, Task> onMessage, CancellationToken cancellation)
	{
		using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		Task pinger = PingLoopAsync(stop.Token);
		try
		{
			while (!stop.IsCancellationRequested)
			{
				MqttPacket? packet = await ReadPacketAsync(stop.Token).ConfigureAwait(false);
				if (packet is null) return "closed by the broker";
				switch (packet.Type)
				{
					case MqttWire.Publish:
						MqttMessage message = MqttWire.ReadPublish(packet);
						if (message.Qos == 1) await SendAsync(MqttWire.PubAckPacket(message.PacketId), stop.Token).ConfigureAwait(false);
						if (message.Qos <= 1) await onMessage(message).ConfigureAwait(false);
						break;
					case MqttWire.SubAck or MqttWire.PingResp or MqttWire.PubAck:
						break;
					default:
						return "unexpected packet " + packet.Type;
				}
			}
			return "stopped";
		}
		catch (OperationCanceledException) { return cancellation.IsCancellationRequested ? "stopped" : "keep-alive ran out"; }
		catch (Exception error) when (error is IOException or SocketException or ObjectDisposedException) { return "connection lost"; }
		catch (MqttProtocolException error) { return "protocol error (" + error.Message + ")"; }
		finally
		{
			stop.Cancel();
			try { await pinger.ConfigureAwait(false); } catch { }
		}
	}

	private async Task PingLoopAsync(CancellationToken cancellation)
	{
		TimeSpan every = TimeSpan.FromTicks(_keepAlive.Ticks / 2);
		while (!cancellation.IsCancellationRequested)
		{
			await Task.Delay(every, cancellation).ConfigureAwait(false);
			if (DateTimeOffset.UtcNow - _lastReceived > TimeSpan.FromTicks(_keepAlive.Ticks * 3 / 2)) { _client.Dispose(); return; }
			await SendAsync(MqttWire.PingReqPacket(), cancellation).ConfigureAwait(false);
		}
	}

	private async Task SendAsync(byte[] packet, CancellationToken cancellation)
	{
		await _write.WaitAsync(cancellation).ConfigureAwait(false);
		try
		{
			await _stream.WriteAsync(packet, cancellation).ConfigureAwait(false);
			await _stream.FlushAsync(cancellation).ConfigureAwait(false);
		}
		finally { _write.Release(); }
	}

	private async Task<MqttPacket?> ReadPacketAsync(CancellationToken cancellation)
	{
		while (true)
		{
			MqttPacket? packet = MqttWire.TryRead(_pending, out int consumed);
			if (packet is not null)
			{
				_pending = _pending[consumed..];
				_lastReceived = DateTimeOffset.UtcNow;
				return packet;
			}
			int read = await _stream.ReadAsync(_buffer, cancellation).ConfigureAwait(false);
			if (read <= 0) return null;
			byte[] joined = new byte[_pending.Length + read];
			_pending.CopyTo(joined, 0);
			Array.Copy(_buffer, 0, joined, _pending.Length, read);
			_pending = joined;
		}
	}

	public ValueTask DisposeAsync()
	{
		_client.Dispose();
		_write.Dispose();
		return ValueTask.CompletedTask;
	}
}
