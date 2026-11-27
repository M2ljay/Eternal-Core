using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playhub.Shared;

// Eternal Core console guards: the BitLocker parts that need administrator rights, kept to two things the
// desktop app does once, behind one Windows prompt each (the TV never shows a prompt):
//
// 1. "Check BitLocker": which key protector types the system drive has and when Windows last logged a
//    recovery-key backup to the Microsoft account. Only type numbers and a time are written; never a
//    recovery key, a protector ID or anything else from the volume.
// 2. "BitLocker guard": a scheduled task, \Core\BitLocker suspend once, that runs exactly one fixed command
//    from System32 as SYSTEM: manage-bde -protectors -disable <drive> -RebootCount 1 (BitLocker suspended
//    for one restart, then back on by itself). The task's security descriptor lets this user run it, so the
//    Gaming Mode agent can start it without a prompt before a firmware update; it can do nothing else.
//    No script file is involved (a user-writable script run as SYSTEM would weaken UAC), and UAC itself is
//    never touched. Removing the guard deletes the task.
//
// Pure apart from the file read/write helpers; the Windows runner is BitLockerGuard.Windows.cs.
internal static class BitLockerGuard
{
	internal const string TaskFolder = @"\Core";
	internal const string TaskName = @"\Core\BitLocker suspend once";
	internal const string CheckFileName = "bitlocker-check.json";

	private static readonly Regex UserSid = new(@"^S-1-5-21(-\d{1,10}){3,5}$", RegexOptions.CultureInvariant);
	private static readonly Regex DriveLetter = new(@"^[A-Za-z]:$", RegexOptions.CultureInvariant);

	internal static bool IsUserSid(string? sid) => sid is not null && UserSid.IsMatch(sid);

	internal static string? NormalizeDrive(string? drive)
	{
		string value = (drive ?? "").Trim().TrimEnd('\\');
		return DriveLetter.IsMatch(value) ? value.ToUpperInvariant() : null;
	}

	internal static string SuspendArguments(string drive) => $"-protectors -disable {drive} -RebootCount 1";

	// Task Scheduler XML (schema 1.2). SYSTEM runs it; the SDDL gives SYSTEM and Administrators full access
	// and the named user read and execute, which is what "schtasks /Run" needs and nothing more.
	internal static string TaskXml(string userSid, string drive, string system32)
	{
		if (!IsUserSid(userSid)) throw new ArgumentException("Not a user SID.", nameof(userSid));
		string letter = NormalizeDrive(drive) ?? throw new ArgumentException("Not a drive letter.", nameof(drive));
		string command = Path.Combine(system32, "manage-bde.exe");
		string sddl = $"D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;{userSid})";
		return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
			+ "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
			+ "  <RegistrationInfo>\r\n"
			+ "    <Author>Core</Author>\r\n"
			+ "    <Description>Core (Eternal): suspends BitLocker on the system drive for one restart before a firmware update. Started by Core's Gaming Mode agent; does nothing else.</Description>\r\n"
			+ $"    <SecurityDescriptor>{SecurityElement.Escape(sddl)}</SecurityDescriptor>\r\n"
			+ "  </RegistrationInfo>\r\n"
			+ "  <Principals>\r\n"
			+ "    <Principal id=\"System\">\r\n"
			+ "      <UserId>S-1-5-18</UserId>\r\n"
			+ "      <RunLevel>HighestAvailable</RunLevel>\r\n"
			+ "    </Principal>\r\n"
			+ "  </Principals>\r\n"
			+ "  <Settings>\r\n"
			+ "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n"
			+ "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n"
			+ "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n"
			+ "    <AllowHardTerminate>true</AllowHardTerminate>\r\n"
			+ "    <StartWhenAvailable>false</StartWhenAvailable>\r\n"
			+ "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n"
			+ "    <Enabled>true</Enabled>\r\n"
			+ "    <Hidden>false</Hidden>\r\n"
			+ "    <ExecutionTimeLimit>PT5M</ExecutionTimeLimit>\r\n"
			+ "  </Settings>\r\n"
			+ "  <Actions Context=\"System\">\r\n"
			+ "    <Exec>\r\n"
			+ $"      <Command>{SecurityElement.Escape(command)}</Command>\r\n"
			+ $"      <Arguments>{SecurityElement.Escape(SuspendArguments(letter))}</Arguments>\r\n"
			+ "    </Exec>\r\n"
			+ "  </Actions>\r\n"
			+ "</Task>\r\n";
	}

	// The elevated check, as a PowerShell script (run with -EncodedCommand, so nothing is quoted on a
	// command line). Win32_EncryptableVolume through CIM works on every edition that has BitLocker or device
	// encryption; event 845 in BitLocker Management is Windows' "recovery information was backed up to your
	// Microsoft account". Output: one compact JSON line with type numbers only.
	internal static string CheckScript(string drive)
	{
		string letter = NormalizeDrive(drive) ?? throw new ArgumentException("Not a drive letter.", nameof(drive));
		return "$ErrorActionPreference = 'Stop'\n"
			+ "$found = $false; $status = $null; $types = @(); $backup = $null\n"
			+ "try {\n"
			+ $"  $v = Get-CimInstance -Namespace 'root/cimv2/security/microsoftvolumeencryption' -ClassName Win32_EncryptableVolume -Filter \"DriveLetter='{letter}'\"\n"
			+ "  if ($v) {\n"
			+ "    $found = $true\n"
			+ "    $status = [int](Invoke-CimMethod -InputObject $v -MethodName GetProtectionStatus).ProtectionStatus\n"
			+ "    $ids = (Invoke-CimMethod -InputObject $v -MethodName GetKeyProtectors -Arguments @{ KeyProtectorType = [uint32]0 }).VolumeKeyProtectorID\n"
			+ "    foreach ($id in @($ids)) { if ($id) { $types += [int](Invoke-CimMethod -InputObject $v -MethodName GetKeyProtectorType -Arguments @{ VolumeKeyProtectorID = $id }).KeyProtectorType } }\n"
			+ "  }\n"
			+ "} catch { }\n"
			+ "try { $e = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-BitLocker/BitLocker Management'; Id = 845 } -MaxEvents 1 -ErrorAction Stop; $backup = $e.TimeCreated.ToUniversalTime().ToString('o') } catch { }\n"
			+ "[pscustomobject]@{ found = $found; protection = $status; protectors = @($types); backupSeenAt = $backup } | ConvertTo-Json -Compress\n";
	}

	internal static string EncodedCommand(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

	internal sealed record CheckResult(DateTimeOffset CheckedAt, bool Found, int? Protection, IReadOnlyList<int> Protectors, DateTimeOffset? BackupSeenAt);

	// The script's JSON line (PowerShell 5.1 writes a one-element array as a bare number; both are read).
	internal static CheckResult? ParseScriptOutput(string output, DateTimeOffset now)
	{
		string? line = output.Split('\n').Select(text => text.Trim()).LastOrDefault(text => text.StartsWith('{'));
		if (line is null) return null;
		try
		{
			using JsonDocument document = JsonDocument.Parse(line);
			JsonElement root = document.RootElement;
			bool found = root.TryGetProperty("found", out JsonElement f) && f.ValueKind == JsonValueKind.True;
			int? protection = root.TryGetProperty("protection", out JsonElement p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int status) ? status : null;
			var protectors = new List<int>();
			if (root.TryGetProperty("protectors", out JsonElement list))
			{
				if (list.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in list.EnumerateArray())
						if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out int type) && type is >= 0 and <= 10) protectors.Add(type);
				}
				else if (list.ValueKind == JsonValueKind.Number && list.TryGetInt32(out int single) && single is >= 0 and <= 10) protectors.Add(single);
			}
			DateTimeOffset? backup = root.TryGetProperty("backupSeenAt", out JsonElement b) && b.ValueKind == JsonValueKind.String
				&& DateTimeOffset.TryParse(b.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset at) ? at : null;
			return new CheckResult(now, found, protection, protectors, backup);
		}
		catch (JsonException) { return null; }
	}

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	internal static void Save(string path, CheckResult result)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		string temporary = path + ".tmp";
		File.WriteAllText(temporary, JsonSerializer.Serialize(result, Json));
		File.Move(temporary, path, true);
	}

	internal static CheckResult? Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return null;
			CheckResult? result = JsonSerializer.Deserialize<CheckResult>(File.ReadAllText(path), Json);
			return result is null ? null : result with { Protectors = (result.Protectors ?? Array.Empty<int>()).Where(type => type is >= 0 and <= 10).ToList() };
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
	}
}
