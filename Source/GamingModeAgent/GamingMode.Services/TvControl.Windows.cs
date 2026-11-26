using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GamingMode.Services;

// Eternal Core Mark III: Windows pieces of TV control.
//
// The pairing key is a secret. It is stored only in %APPDATA%\GamingMode\tv-pairing.json,
// encrypted with DPAPI for the current Windows user (CryptProtectData), and it is never
// written to a log, an HTTP response or config.json. The certificate fingerprint next to
// it is not a secret; it pins the TV's self-signed certificate (LgWebOsClient).
internal sealed class DpapiTvPairingStore : ITvPairingStore
{
	private readonly string _path;
	private readonly FileLogger _logger;
	private readonly object _sync = new();
	private string? _key;
	private string? _certificate;
	private bool _loaded;

	internal DpapiTvPairingStore(string configDirectory, FileLogger logger)
	{
		_path = Path.Combine(configDirectory, "tv-pairing.json");
		_logger = logger;
	}

	public string? ClientKey { get { Load(); lock (_sync) return _key; } }
	public string? CertificateSha256 { get { Load(); lock (_sync) return _certificate; } }

	public void Save(string clientKey, string? certificateSha256)
	{
		byte[] sealedKey = Protect(Encoding.UTF8.GetBytes(clientKey));
		string json = JsonSerializer.Serialize(new { key = Convert.ToBase64String(sealedKey), certificate = certificateSha256 });
		string temporary = _path + ".tmp";
		File.WriteAllText(temporary, json);
		File.Move(temporary, _path, true);
		lock (_sync) { _key = clientKey; _certificate = certificateSha256; _loaded = true; }
	}

	public void Clear()
	{
		try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
		lock (_sync) { _key = null; _certificate = null; _loaded = true; }
	}

	private void Load()
	{
		lock (_sync) if (_loaded) return;
		string? key = null, certificate = null;
		try
		{
			if (File.Exists(_path))
			{
				using JsonDocument document = JsonDocument.Parse(File.ReadAllText(_path));
				JsonElement root = document.RootElement;
				if (root.TryGetProperty("key", out JsonElement sealedKey) && sealedKey.ValueKind == JsonValueKind.String)
					key = Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(sealedKey.GetString()!)));
				if (root.TryGetProperty("certificate", out JsonElement cert) && cert.ValueKind == JsonValueKind.String)
					certificate = cert.GetString();
			}
		}
		catch (Exception error)
		{
			// Another user's or another PC's file cannot be decrypted: treat as unpaired.
			// The exception type only; its message could in theory carry data.
			_logger.Info("TV pairing could not be read (" + error.GetType().Name + "); pair the TV again.");
			key = null; certificate = null;
		}
		lock (_sync) { if (_loaded) return; _key = key; _certificate = certificate; _loaded = true; }
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DataBlob { public int Size; public nint Data; }

	private const int UiForbidden = 0x1;

	private static byte[] Protect(byte[] data) => Transform(data, protect: true);
	private static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

	private static byte[] Transform(byte[] data, bool protect)
	{
		var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
		var input = new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
		var output = new DataBlob();
		try
		{
			bool ok = protect
				? CryptProtectData(ref input, "Core TV pairing", 0, 0, 0, UiForbidden, ref output)
				: CryptUnprotectData(ref input, 0, 0, 0, 0, UiForbidden, ref output);
			if (!ok) throw new InvalidOperationException("DPAPI failed with error " + Marshal.GetLastWin32Error() + ".");
			var result = new byte[output.Size];
			Marshal.Copy(output.Data, result, 0, output.Size);
			return result;
		}
		finally
		{
			handle.Free();
			if (output.Data != 0) LocalFree(output.Data);
		}
	}

	[DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptProtectData(ref DataBlob data, string? description, nint entropy, nint reserved, nint prompt, int flags, ref DataBlob output);

	[DllImport("crypt32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CryptUnprotectData(ref DataBlob data, nint description, nint entropy, nint reserved, nint prompt, int flags, ref DataBlob output);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint memory);
}

internal static class NetworkMac
{
	// The TV's MAC address from the ARP table (after pairing the TV has just answered, so
	// its entry is fresh). IPv4 only; null when Windows cannot resolve it.
	internal static byte[]? Resolve(string address)
	{
		try
		{
			if (!IPAddress.TryParse(address?.Trim(), out IPAddress? ip) || ip.AddressFamily != AddressFamily.InterNetwork) return null;
			uint destination = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
			var mac = new byte[6];
			uint length = (uint)mac.Length;
			return SendARP(destination, 0, mac, ref length) == 0 && length == 6 ? mac : null;
		}
		catch { return null; }
	}

	[DllImport("iphlpapi.dll", ExactSpelling = true)]
	private static extern int SendARP(uint destination, uint source, byte[] mac, ref uint length);
}
