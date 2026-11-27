using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Playhub.Shared;

// Eternal Core: Jarvis's secrets (docs/eternal/JARVIS.md): the Home Assistant long-lived access token, the user's own
// Anthropic API key and the optional OpenAI-compatible key for cloud Whisper. The desktop app writes them, the agent
// reads them. Stored only in %APPDATA%\GamingMode\jarvis-secrets.json, each value sealed with DPAPI for the current
// Windows user (CoreSecretStore's sealing), never in config.json, a log or an HTTP answer. Another user's or another PC's
// file cannot be opened and reads as "not saved".
public static class JarvisSecretStore
{
	public const string FileName = "jarvis-secrets.json";
	public const string HaToken = "haToken", AnthropicKey = "anthropicKey", OpenAiKey = "openAiKey";
	private static readonly string[] Names = { HaToken, AnthropicKey, OpenAiKey };
	private static readonly object Sync = new();

	// Which secrets are saved (names only; the values are not unsealed).
	public static IReadOnlyDictionary<string, bool> Saved(string directory)
	{
		var result = new Dictionary<string, bool>(StringComparer.Ordinal);
		JsonObject? file = Load(directory);
		foreach (string name in Names) result[name] = file?[name] is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0;
		return result;
	}

	public static bool Has(string directory, string name) => Saved(directory).TryGetValue(name, out bool saved) && saved;

	// The secret, or null when none is saved or it cannot be unsealed. Never throws.
	public static string? Read(string directory, string name)
	{
		try
		{
			if (Load(directory)?[name] is not JsonValue value || !value.TryGetValue(out string? sealedValue) || sealedValue.Length == 0) return null;
			string secret = Encoding.UTF8.GetString(CoreSecretStore.Unseal(Convert.FromBase64String(sealedValue)));
			return secret.Length == 0 ? null : secret;
		}
		catch { return null; }
	}

	// An empty value removes that secret; the file goes when nothing is left.
	public static void Save(string directory, string name, string? secret)
	{
		if (Array.IndexOf(Names, name) < 0) throw new ArgumentException("Unknown secret name.", nameof(name));
		lock (Sync)
		{
			string path = Path.Combine(directory, FileName);
			JsonObject file = Load(directory) ?? new JsonObject();
			string? clean = secret?.Trim();
			if (string.IsNullOrEmpty(clean)) file.Remove(name);
			else file[name] = Convert.ToBase64String(CoreSecretStore.Seal(Encoding.UTF8.GetBytes(clean), "Core Jarvis"));
			if (file.Count == 0) { if (File.Exists(path)) File.Delete(path); return; }
			Directory.CreateDirectory(directory);
			string temporary = path + ".tmp";
			File.WriteAllText(temporary, file.ToJsonString());
			File.Move(temporary, path, true);
		}
	}

	private static JsonObject? Load(string directory)
	{
		try
		{
			string path = Path.Combine(directory, FileName);
			return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
	}
}
