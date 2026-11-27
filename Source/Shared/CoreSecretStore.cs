using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Playhub.Shared;

// Eternal Core batch 2: the Home Assistant (MQTT) password. The desktop app writes it, the Gaming Mode agent reads it.
// Stored only in %APPDATA%\GamingMode\home-assistant-secret.json, sealed with DPAPI for the current Windows user
// (CryptProtectData, the same way as the TV pairing key), never in config.json, a log or an HTTP answer. Another
// user's or another PC's file cannot be opened and reads as "no password".
public static class CoreSecretStore
{
	public const string FileName = "home-assistant-secret.json";

	public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingMode");

	public static bool HasPassword(string directory)
	{
		try { return File.Exists(Path.Combine(directory, FileName)); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
	}

	// The password, or null when none is saved or it cannot be unsealed. Never throws.
	public static string? ReadPassword(string directory)
	{
		try
		{
			string path = Path.Combine(directory, FileName);
			if (!File.Exists(path)) return null;
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			if (!document.RootElement.TryGetProperty("password", out JsonElement value) || value.ValueKind != JsonValueKind.String) return null;
			return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value.GetString()!), protect: false));
		}
		catch { return null; }
	}

	// An empty password removes the file.
	public static void SavePassword(string directory, string? password)
	{
		string path = Path.Combine(directory, FileName);
		if (string.IsNullOrEmpty(password)) { if (File.Exists(path)) File.Delete(path); return; }
		Directory.CreateDirectory(directory);
		string sealedValue = Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(password), protect: true));
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(new { password = sealedValue }));
		File.Move(temporary, path, true);
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DataBlob { public int Size; public nint Data; }

	private const int UiForbidden = 0x1;

	// Jarvis's secrets (JarvisSecretStore.cs) use the same sealing, with their own description.
	internal static byte[] Seal(byte[] data, string description) => Transform(data, protect: true, description);

	internal static byte[] Unseal(byte[] data) => Transform(data, protect: false);

	private static byte[] Transform(byte[] data, bool protect, string description = "Core Home Assistant")
	{
		var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
		var input = new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
		var output = new DataBlob();
		try
		{
			bool ok = protect
				? CryptProtectData(ref input, description, 0, 0, 0, UiForbidden, ref output)
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
