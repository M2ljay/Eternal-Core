using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GamingMode.Services;

// The Windows half of 3.3.5's emulator health, emulator updates, game exit and store launchers (rules in
// EmulatorHealth.cs, EmulatorUpdates.cs, GameExit.cs, StreamProfiles.cs). Nothing here opens a game process
// while it runs: the process list comes from NtQuerySystemInformation, windows from EnumWindows, and a
// process is opened only to terminate a leftover of a launch Steam already ended. Settings files are read as
// text and never logged; credentials in them are tested for presence by the pure parsers only.

internal static class EmulatorProbes
{
	internal static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
	internal static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
	// EmuDeck for Windows: vars.ps1 $emusPath, and settings.ps1 with the BIOS and tools roots.
	internal static string EmusPath => Path.Combine(AppData, "EmuDeck", "Emulators");
	internal static string SettingsPs1 => Path.Combine(AppData, "EmuDeck", "settings.ps1");
	private const long MaxConfigBytes = 4L * 1024 * 1024;

	internal static IReadOnlyDictionary<string, string> EmuDeckSettings()
	{
		try { return File.Exists(SettingsPs1) ? EmulatorHealthPolicy.ParseEmuDeckSettings(File.ReadAllText(SettingsPs1)) : new Dictionary<string, string>(); }
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new Dictionary<string, string>(); }
	}

	// Each catalogue emulator's exe: the one EmuDeck's launcher script names, else the EmuDeck folder layout.
	internal static IReadOnlyDictionary<string, string> Locate()
	{
		var found = new Dictionary<string, string>(StringComparer.Ordinal);
		IReadOnlyDictionary<string, string> settings = EmuDeckSettings();
		if (settings.TryGetValue("toolsPath", out string? tools) && Directory.Exists(Path.Combine(tools, "launchers")))
		{
			try
			{
				foreach (string script in Directory.EnumerateFiles(Path.Combine(tools, "launchers"), "*.ps1").Take(100))
				{
					string? exe = EmulatorHealthPolicy.LauncherEmulatorFile(ReadSmall(script) ?? "", AppData, UserProfile);
					if (exe is not null && EmulatorHealthPolicy.ByExe(exe) is { } def && File.Exists(exe)) found.TryAdd(def.Id, exe);
				}
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		}
		foreach (EmulatorDef def in EmulatorCatalog.All)
		{
			if (found.ContainsKey(def.Id)) continue;
			foreach (string exe in def.Exes)
			{
				string path = Path.Combine(EmusPath, def.Folder, exe);
				if (File.Exists(path)) { found[def.Id] = path; break; }
			}
		}
		return found;
	}

	internal static string? ReadSmall(string path)
	{
		try
		{
			var info = new FileInfo(path);
			if (!info.Exists || info.Length > MaxConfigBytes) return null;
			return DecodeText(File.ReadAllBytes(path)).Text;
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
	}

	internal static (string Text, bool Bom) DecodeText(byte[] bytes)
	{
		bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
		return (Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
	}

	internal static bool Running(EmulatorDef def) => def.Exes.Any(exe => AnyProcess(Path.GetFileNameWithoutExtension(exe)));

	internal static bool AnyProcess(string name)
	{
		Process[] processes = Array.Empty<Process>();
		try { processes = Process.GetProcessesByName(name); return processes.Length > 0; }
		catch (InvalidOperationException) { return false; }
		finally { foreach (Process process in processes) process.Dispose(); }
	}

	// The version: the exe's own version resource (no process is started), else what EmuDeck recorded.
	internal static string? Version(EmulatorDef def, string exe)
	{
		try
		{
			FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
			string? version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
			if (!string.IsNullOrWhiteSpace(version) && version.Trim() != "0.0.0.0") return version.Trim().Length > 40 ? version.Trim()[..40] : version.Trim();
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException or FileNotFoundException) { }
		try
		{
			string json = Path.Combine(AppData, "emudeck", "emu_versions.json");
			if (ReadSmall(json) is { } text)
			{
				using var document = System.Text.Json.JsonDocument.Parse(text);
				foreach (var property in document.RootElement.EnumerateObject())
					if (property.Name.Replace("_", "", StringComparison.Ordinal).Contains(def.Id, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
						return property.Value.GetString();
			}
		}
		catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException) { }
		return null;
	}

	// The observation for one emulator: its settings files as text, the PPSSPP token file's size (never its
	// contents), BIOS verdicts and whether it runs.
	internal static EmulatorObservation Observe(EmulatorDef def, string exe, IReadOnlyDictionary<string, string> emudeck)
	{
		string folder = Path.GetDirectoryName(exe)!;
		var files = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		void Add(string relative) { if (!files.ContainsKey(relative)) files[relative] = ReadSmall(Path.Combine(folder, relative.Replace('/', '\\'))); }
		foreach (SettingCheck check in def.Settings) Add(check.Where.File);
		if (def.Ra is { } ra) { Add(ra.Enabled.File); Add(ra.User.File); foreach (ConfigKey key in ra.Token) Add(key.File); }
		foreach (HotkeyWrite write in EmulatorCatalog.Hotkeys(def.Id).Writes) Add(write.Where.File);
		bool? token = null;
		if (def.Ra?.TokenFile is { } tokenFile)
		{
			try { var info = new FileInfo(Path.Combine(folder, tokenFile.Replace('/', '\\'))); token = info.Exists && info.Length > 0; }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { token = null; }
		}
		return new EmulatorObservation
		{
			Def = def, Exe = exe, Version = Version(def, exe), Running = Running(def), Files = files, TokenFileNonEmpty = token,
			Bios = Bios(def, folder, emudeck, files),
		};
	}

	private static IReadOnlyDictionary<string, string> Bios(EmulatorDef def, string folder, IReadOnlyDictionary<string, string> emudeck, IReadOnlyDictionary<string, string?> files)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		List<(string Name, string RelativeFolder, Func<string?> Md5)>? biosFiles = null;
		foreach (BiosSpec spec in def.Bios)
		{
			if (spec.InEmulator)
			{
				string name = spec.Names[0];
				if (name.StartsWith("toml:", StringComparison.Ordinal))
				{
					// xemu: the file its own xemu.toml names must exist.
					string[] parts = name[5..].Split('/');
					string? path = ConfigText.Read(files.TryGetValue("xemu.toml", out string? toml) ? toml : null, new ConfigKey("xemu.toml", ConfigFormat.Toml, parts[0], parts[1]));
					result[spec.System] = string.IsNullOrWhiteSpace(path) ? "missing" : File.Exists(Path.IsPathRooted(path) ? path : Path.Combine(folder, path)) ? "ok" : "missing";
					continue;
				}
				string full = Path.Combine(folder, name.Replace('/', '\\'));
				bool present = spec.Directory ? Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() : File.Exists(full);
				result[spec.System] = present ? "ok" : "missing";
				continue;
			}
			if (!emudeck.TryGetValue("biosPath", out string? root) || !Directory.Exists(root)) { result[spec.System] = "unknown"; continue; }
			biosFiles ??= ListBios(root);
			result[spec.System] = EmulatorHealthPolicy.BiosState(spec, biosFiles);
		}
		return result;
	}

	private static List<(string Name, string RelativeFolder, Func<string?> Md5)> ListBios(string root)
	{
		var list = new List<(string, string, Func<string?>)>();
		try
		{
			foreach (string path in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }).Take(3000))
			{
				string relative = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
				string captured = path;
				string? cached = null;
				bool done = false;
				list.Add((Path.GetFileName(path), relative == "." ? "" : relative.Replace('\\', '/'), () =>
				{
					if (done) return cached;
					done = true;
					try
					{
						var info = new FileInfo(captured);
						if (info.Length > EmulatorHealthPolicy.MaxBiosHashBytes || info.Length < 1024) return null;
						using FileStream stream = File.OpenRead(captured);
						cached = Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
					}
					catch (Exception error) when (error is IOException or UnauthorizedAccessException) { cached = null; }
					return cached;
				}));
			}
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		return list;
	}

	// Writes the new text of each file under the fix guard: the file is read again just before writing and must
	// be exactly what the plan was made from, the emulator must not run, and a backup comes first. Returns the
	// result code ("ok" or the refusal) and the files written.
	internal static (string Result, List<string> Written) Write(EmulatorDef def, string exe, IReadOnlyDictionary<string, string?> plan, IReadOnlyDictionary<string, string?> readTexts, string backupRoot, FileLogger logger)
	{
		var written = new List<string>();
		string folder = Path.GetDirectoryName(exe)!;
		DateTimeOffset at = DateTimeOffset.Now;
		foreach ((string relative, string? next) in plan)
		{
			string path = Path.Combine(folder, relative.Replace('/', '\\'));
			byte[] bytes;
			try { bytes = File.ReadAllBytes(path); }
			catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return ("unreadable", written); }
			(string current, bool bom) = DecodeText(bytes);
			readTexts.TryGetValue(relative, out string? read);
			FixRefusal refusal = EmulatorFixGuard.Decide(Running(def), ConfigText.Hash(read), ConfigText.Hash(current), next, current);
			if (refusal == FixRefusal.NoChange) continue;
			if (refusal != FixRefusal.None) return (EmulatorFixGuard.Code(refusal), written);
			string backup = EmulatorFixGuard.BackupPath(backupRoot, def.Id, at, relative);
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
				File.WriteAllBytes(backup, bytes);
				byte[] output = Encoding.UTF8.GetBytes(next!);
				if (bom) output = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(output).ToArray();
				string temporary = path + ".core-tmp";
				File.WriteAllBytes(temporary, output);
				File.Move(temporary, path, true);
				written.Add(relative);
				logger.Info($"Emulators: {def.Name}: {relative} updated (backup: {backup}).");
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException)
			{
				logger.Error($"Emulators: {def.Name}: {relative} could not be written; the backup is at {backup}.", error);
				return ("write-failed", written);
			}
		}
		return ("ok", written);
	}

	// ---------- back paddles: Steam Input layouts (read-only) ----------

	// Steam keeps the Desktop (app 413080) and Big Picture (413090) controller layouts under
	// Steam\steamapps\common\Steam Controller Configs\<account>\config\<app>\*.vdf. Read only; Core never writes them.
	internal static IReadOnlyList<(string Layout, string Verdict)> PaddleLayouts(string? steamRoot)
	{
		var layouts = new List<(string, string)>();
		if (steamRoot is null) return layouts;
		string configs = Path.Combine(steamRoot, "steamapps", "common", "Steam Controller Configs");
		try
		{
			if (!Directory.Exists(configs)) return layouts;
			foreach (string account in Directory.EnumerateDirectories(configs).Take(8))
				foreach ((string folderName, string layout) in new[] { ("413090", "big-picture"), ("413080", "desktop") })
				{
					string folder = Path.Combine(account, "config", folderName);
					if (!Directory.Exists(folder)) continue;
					string? vdf = Directory.EnumerateFiles(folder, "*.vdf").Select(ReadSmall).FirstOrDefault(text => text is not null);
					if (vdf is not null) layouts.Add((layout, BackPaddlePolicy.Verdict(vdf)));
				}
		}
		catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
		return layouts;
	}
}

// ---------- processes, without opening them ----------

internal static class ProcessSnapshot
{
	private const int SystemProcessInformation = 5;

	// Every process with its parent, start time, image name, disk writes so far and whether it owns a visible,
	// game-sized window. NtQuerySystemInformation reads the kernel's list; no process is opened.
	internal static IReadOnlyList<(ProcessSeen Process, long WrittenBytes)> Read()
	{
		var list = new List<(ProcessSeen, long)>();
		if (IntPtr.Size != 8) return list;
		HashSet<int> windowed = WindowedProcesses();
		int size = 1 << 20;
		for (int attempt = 0; attempt < 4; attempt++)
		{
			nint buffer = Marshal.AllocHGlobal(size);
			try
			{
				int status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out int needed);
				if (status == unchecked((int)0xC0000004)) { size = Math.Max(size * 2, needed + 65536); continue; }
				if (status != 0) return list;
				int offset = 0;
				while (true)
				{
					nint entry = buffer + offset;
					int next = Marshal.ReadInt32(entry, 0);
					long created = Marshal.ReadInt64(entry, 32);
					int nameLength = Marshal.ReadInt16(entry, 56) & 0xFFFF;
					nint nameBuffer = Marshal.ReadIntPtr(entry, 64);
					int pid = (int)Marshal.ReadIntPtr(entry, 80);
					int parent = (int)Marshal.ReadIntPtr(entry, 88);
					long written = Marshal.ReadInt64(entry, 240);
					string name = nameBuffer != 0 && nameLength > 0 ? Marshal.PtrToStringUni(nameBuffer, nameLength / 2) : "";
					if (pid > 4 && name.Length > 0)
					{
						DateTimeOffset started = created > 0 ? DateTimeOffset.FromFileTime(created) : DateTimeOffset.MinValue;
						list.Add((new ProcessSeen(pid, parent, name, null, started, windowed.Contains(pid)), written));
					}
					if (next == 0) break;
					offset += next;
				}
				return list;
			}
			finally { Marshal.FreeHGlobal(buffer); }
		}
		return list;
	}

	private static HashSet<int> WindowedProcesses()
	{
		var result = new HashSet<int>();
		EnumWindows((window, _) =>
		{
			if (IsWindowVisible(window) && GetWindowRect(window, out Rect rect) && rect.Right - rect.Left >= 320 && rect.Bottom - rect.Top >= 240)
			{
				GetWindowThreadProcessId(window, out uint pid);
				result.Add((int)pid);
			}
			return true;
		}, 0);
		return result;
	}

	// The polite close: WM_CLOSE to every top-level window of the process (an emulator saves its settings and
	// exits; a tray launcher closes its window and, for most, quits).
	internal static int PostClose(int pid)
	{
		int sent = 0;
		EnumWindows((window, _) =>
		{
			GetWindowThreadProcessId(window, out uint owner);
			if (owner == pid && PostMessage(window, 0x0010 /* WM_CLOSE */, 0, 0)) sent++;
			return true;
		}, 0);
		return sent;
	}

	// The last step for a leftover of an ended launch: opened with PROCESS_TERMINATE only, and only when its
	// start time still matches (a reused pid is never touched).
	internal static bool Terminate(int pid, DateTimeOffset started)
	{
		if (!Read().Any(item => item.Process.Pid == pid && item.Process.Started == started)) return false;
		nint handle = OpenProcess(0x0001 /* PROCESS_TERMINATE */, false, pid);
		if (handle == 0) return false;
		try { return TerminateProcess(handle, 1); }
		finally { CloseHandle(handle); }
	}

	internal static bool Alive(int pid, DateTimeOffset started) => Read().Any(item => item.Process.Pid == pid && item.Process.Started == started);

	private delegate bool EnumWindowsProc(nint window, nint parameter);
	[StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
	[DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int informationClass, nint information, int length, out int returned);
	[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
	[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
	[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rect);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
	[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
	[DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, int pid);
	[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint handle, uint exitCode);
	[DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
}

// ---------- emulator updates: download, verify, unpack, back up, overlay, smoke test, roll back ----------

internal static class EmulatorUpdater
{
	private static readonly HttpClient Http = CreateClient();

	private static HttpClient CreateClient()
	{
		var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd("Core-emulator-updates/1");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return client;
	}

	internal static async Task<ReleaseInfo?> LatestAsync(UpdateSource source, CancellationToken cancellation)
	{
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		timeout.CancelAfter(TimeSpan.FromSeconds(20));
		using HttpResponseMessage response = await Http.GetAsync("https://api.github.com/repos/" + source.Repository + "/releases/latest", timeout.Token).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode) return null;
		string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
		return json.Length > 2_000_000 ? null : EmulatorUpdateSources.ParseRelease(json);
	}

	internal static async Task<string?> TextAsync(string url, CancellationToken cancellation)
	{
		if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) return null;
		using HttpResponseMessage response = await Http.GetAsync(url, cancellation).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_000_000) return null;
		return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
	}

	// Downloads to a file and returns its SHA-256 (lower-case hex).
	internal static async Task<string> DownloadAsync(string url, string target, long expectedSize, CancellationToken cancellation)
	{
		if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only GitHub release downloads are allowed.");
		using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
		response.EnsureSuccessStatusCode();
		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		await using (Stream input = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false))
		await using (FileStream output = File.Create(target))
			await input.CopyToAsync(output, cancellation).ConfigureAwait(false);
		if (expectedSize > 0 && new FileInfo(target).Length != expectedSize) throw new InvalidDataException("The download's size does not match the release.");
		await using FileStream stream = File.OpenRead(target);
		return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false)).ToLowerInvariant();
	}

	// Unpacks with EmuDeck's own 7-Zip when it is there (it unpacks both .7z and .zip), else Windows' tar
	// (libarchive; .zip always, .7z on current Windows 11). The output folder must be empty and new.
	internal static bool Extract(string archive, string folder, FileLogger logger)
	{
		Directory.CreateDirectory(folder);
		string sevenZip = Path.Combine(EmulatorProbes.AppData, "EmuDeck", "backend", "wintools", "7z.exe");
		(string File, string Arguments) command = File.Exists(sevenZip)
			? (sevenZip, $"x -y -o\"{folder}\" \"{archive}\"")
			: (Path.Combine(Environment.SystemDirectory, "tar.exe"), $"-xf \"{archive}\" -C \"{folder}\"");
		try
		{
			using Process? process = Process.Start(new ProcessStartInfo(command.File, command.Arguments) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
			if (process is null) return false;
			if (!process.WaitForExit(600_000)) { try { process.Kill(); } catch { } return false; }
			return process.ExitCode == 0 && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any();
		}
		catch (Exception error) { logger.Error($"Emulator updates: {Path.GetFileName(command.File)} could not unpack the download.", error); return false; }
	}

	// Program files only: everything in the emulator folder outside its user-data paths.
	internal static List<string> ProgramFiles(UpdateSource source, string folder) =>
		Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
			.Where(relative => !EmulatorUpdatePolicy.IsProtected(source, relative)).ToList();

	internal static void Copy(string from, string to)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(to)!);
		File.Copy(from, to, true);
	}

	internal static string? SmokeTest(string exe)
	{
		bool exists = File.Exists(exe);
		bool pe = false;
		try { if (exists) { using FileStream stream = File.OpenRead(exe); pe = stream.ReadByte() == 'M' && stream.ReadByte() == 'Z'; } }
		catch (IOException) { }
		if (!exists || !pe) return EmulatorUpdatePolicy.SmokeFailure(exists, pe, false, null, false);
		try
		{
			using Process? process = Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Minimized });
			if (process is null) return EmulatorUpdatePolicy.SmokeFailure(true, true, false, null, false);
			bool exited = process.WaitForExit((int)EmulatorUpdatePolicy.SmokeAlive.TotalMilliseconds);
			int? code = exited ? process.ExitCode : null;
			if (!exited)
			{
				try { process.CloseMainWindow(); } catch { }
				if (!process.WaitForExit(5000)) try { process.Kill(); } catch { }
			}
			return EmulatorUpdatePolicy.SmokeFailure(true, true, true, code, !exited);
		}
		catch (Exception) { return EmulatorUpdatePolicy.SmokeFailure(true, true, false, null, false); }
	}
}
