using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace Playhub.Shared;

// The Windows half of BitLockerGuard.cs. The app calls Check/Install/Remove from its elevated instance
// ("console-guard ..." after one Windows prompt on the desktop); the Gaming Mode agent calls IsInstalled
// and Run, which need no administrator rights. Every call fails soft: false or null, never an exception.
[SupportedOSPlatform("windows")]
internal static class BitLockerGuardWindows
{
	private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

	internal static string SystemDrive => BitLockerGuard.NormalizeDrive(Path.GetPathRoot(Environment.SystemDirectory)) ?? "C:";

	private static (int Code, string Output) Execute(string exe, string arguments, TimeSpan timeout)
	{
		try
		{
			using var process = new Process
			{
				StartInfo = new ProcessStartInfo(Path.Combine(System32, exe), arguments)
				{
					UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
					StandardOutputEncoding = Encoding.UTF8,
				}
			};
			process.Start();
			var errors = process.StandardError.ReadToEndAsync();
			var reading = process.StandardOutput.ReadToEndAsync();
			if (!process.WaitForExit((int)timeout.TotalMilliseconds)) { try { process.Kill(true); } catch { } return (-1, ""); }
			string output = reading.Wait(TimeSpan.FromSeconds(5)) ? reading.Result : "";
			errors.Wait(TimeSpan.FromSeconds(1));
			return (process.ExitCode, output.Length > 64 * 1024 ? output[..(64 * 1024)] : output);
		}
		catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return (-1, ""); }
	}

	// Elevated. Writes only protector type numbers and a time (BitLockerGuard.CheckScript).
	internal static BitLockerGuard.CheckResult? Check(string outputPath)
	{
		string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + BitLockerGuard.EncodedCommand(BitLockerGuard.CheckScript(SystemDrive));
		(int code, string output) = Execute(@"WindowsPowerShell\v1.0\powershell.exe", arguments, TimeSpan.FromSeconds(60));
		if (code != 0) return null;
		BitLockerGuard.CheckResult? result = BitLockerGuard.ParseScriptOutput(output, DateTimeOffset.Now);
		if (result is not null) BitLockerGuard.Save(outputPath, result);
		return result;
	}

	// Elevated. schtasks reads the XML as UTF-16 with a byte order mark.
	internal static bool Install(string userSid)
	{
		if (!BitLockerGuard.IsUserSid(userSid)) return false;
		string file = Path.Combine(Path.GetTempPath(), "core-bitlocker-guard-" + Guid.NewGuid().ToString("N") + ".xml");
		try
		{
			File.WriteAllText(file, BitLockerGuard.TaskXml(userSid, SystemDrive, System32), new UnicodeEncoding(false, true));
			return Execute("schtasks.exe", $"/Create /TN \"{BitLockerGuard.TaskName}\" /XML \"{file}\" /F", TimeSpan.FromSeconds(30)).Code == 0;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
		finally { try { File.Delete(file); } catch { } }
	}

	// Elevated.
	internal static bool Remove() =>
		Execute("schtasks.exe", $"/Delete /TN \"{BitLockerGuard.TaskName}\" /F", TimeSpan.FromSeconds(30)).Code == 0 || !IsInstalled();

	// Not elevated: the task's security descriptor lets this user read and run it.
	internal static bool IsInstalled() => Execute("schtasks.exe", $"/Query /TN \"{BitLockerGuard.TaskName}\"", TimeSpan.FromSeconds(15)).Code == 0;

	internal static bool Run() => Execute("schtasks.exe", $"/Run /TN \"{BitLockerGuard.TaskName}\"", TimeSpan.FromSeconds(15)).Code == 0;
}
