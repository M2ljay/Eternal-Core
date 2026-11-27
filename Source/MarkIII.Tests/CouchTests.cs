using GamingMode.Services;
using Playhub.Shared;

// Eternal Core Mark III tests: couch hardening (backup, apply, undo, Gaming Mode banners)
// against an in-memory registry, and the update-night decisions.
static class CouchTests
{
	internal static Task Run(Action<bool, string> Check)
	{
		string folder = Path.Combine(Path.GetTempPath(), "core-couch-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		try
		{
			var registry = new MemoryRegistry();
			string user = Path.Combine(folder, "user.json"), machine = Path.Combine(folder, "machine.json"), toasts = Path.Combine(folder, "toasts.json");
			registry.Write(false, @"Control Panel\Accessibility\StickyKeys", "Flags", "510");
			registry.Write(false, @"Software\Microsoft\Windows\Windows Error Reporting", "DontShowUI", 0);
			int before = registry.Count;

			int changed = CouchHardening.Apply(registry, CouchHardening.Values(false), user);
			Check(changed > 0 && CouchHardening.Fixes.Where(f => !f.Machine).All(f => CouchHardening.IsApplied(registry, f)), "per-user fixes apply");
			Check(!registry.TouchedMachine, "per-user apply never writes the machine hive");
			Check(CouchHardening.Apply(registry, CouchHardening.Values(false), user) == 0, "applying twice changes nothing");
			CouchHardening.Undo(registry, false, user);
			Check((string?)registry.Read(false, @"Control Panel\Accessibility\StickyKeys", "Flags") == "510"
				&& (int?)registry.Read(false, @"Software\Microsoft\Windows\Windows Error Reporting", "DontShowUI") == 0, "undo restores the values that existed");
			Check(registry.Count == before && !File.Exists(user), "undo deletes the values that did not exist, and the backup");

			CouchHardening.Apply(registry, CouchHardening.Values(true), machine);
			Check(CouchHardening.Fixes.Where(f => f.Machine).All(f => CouchHardening.IsApplied(registry, f)), "machine fixes apply");
			Check((int?)registry.Read(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ActiveHoursEnd") == 5
				&& (int?)registry.Read(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ActiveHoursStart") == 11, "active hours run 11:00 to 5:00, past the 4 a.m. bedtime");
			CouchHardening.Undo(registry, true, machine);
			Check(registry.Count == before, "machine undo leaves no policy behind");

			// Console guards: the update policy that fits the edition (Home ignores the feature-update pin).
			const string version = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
			Check(CouchHardening.EditionFixes("Core", "24H2", "26100").Single() is { Id: "driver-search", Machine: true } home
				&& home.Values.Single() is { Key: @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", Name: "SearchOrderConfig", Value: 0 }, "Home: Device Installation Settings' No instead of a pin");
			Check(CouchHardening.EditionFixes("Professional", "24H2", "26100").Single() is { Id: "feature-pin" } pin
				&& pin.Values.Select(value => (value.Name, value.Value)).SequenceEqual(new (string, object)[] { ("ProductVersion", "Windows 11"), ("TargetReleaseVersion", 1), ("TargetReleaseVersionInfo", "24H2") }),
				"Pro: feature updates pinned to the installed version");
			Check(CouchHardening.EditionFixes("Professional", "22H2", "19045").Single().Values[0].Value is "Windows 10" && CouchHardening.EditionFixes("Professional", "garbage", "26100").Length == 0,
				"Windows 10 named as such; an unreadable version pins nothing");
			registry.Write(true, version, "EditionID", "Professional"); registry.Write(true, version, "DisplayVersion", "24H2"); registry.Write(true, version, "CurrentBuild", "26100");
			int withEdition = registry.Count;
			CouchHardening.Apply(registry, CouchHardening.MachineValues(registry), machine);
			Check((int?)registry.Read(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "TargetReleaseVersion") == 1
				&& CouchHardening.EditionFixes(registry).All(fix => CouchHardening.IsApplied(registry, fix)), "machine apply includes the edition's policy");
			CouchHardening.Undo(registry, true, machine);
			Check(registry.Count == withEdition && registry.Read(true, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "TargetReleaseVersionInfo") is null, "undo removes the pin too");
			Check(CouchHardening.Fixes.Single(fix => fix.Id == "setup-nag").Values.Any(value => value.Name == "ScoobeSystemSettingEnabled")
				&& CouchHardening.Fixes.Single(fix => fix.Id == "setup-nag").Values.Any(value => value.Name == "SubscribedContent-310093Enabled")
				&& CouchHardening.Fixes.Single(fix => fix.Id == "tips").Values.Count(value => value.Name.StartsWith("SubscribedContent-", StringComparison.Ordinal)) == 5,
				"setup screens: SCOOBE, the post-update welcome and the tips/suggestions content");

			registry.Write(false, CouchHardening.GamingToasts.Key, CouchHardening.GamingToasts.Name, 1);
			Check(CouchHardening.SetGamingToasts(registry, true, toasts) && (int?)registry.Read(false, CouchHardening.GamingToasts.Key, "ToastEnabled") == 0, "Gaming Mode turns Windows banners off");
			Check(!CouchHardening.SetGamingToasts(registry, true, toasts), "staying in Gaming Mode changes nothing");
			Check(CouchHardening.SetGamingToasts(registry, false, toasts) && (int?)registry.Read(false, CouchHardening.GamingToasts.Key, "ToastEnabled") == 1, "Desktop Mode puts the user's banner setting back");
			Check(!CouchHardening.SetGamingToasts(registry, false, toasts), "Desktop Mode with nothing recorded changes nothing");

			var offset = TimeSpan.FromHours(-5);
			var oneAm = new DateTimeOffset(2026, 11, 26, 1, 0, 0, offset);
			Check(UpdateNightPolicy.NextRestart(oneAm) == new DateTimeOffset(2026, 11, 26, 5, 0, 0, offset), "at 1 a.m., tonight means 5:00 today");
			Check(UpdateNightPolicy.NextRestart(oneAm.AddHours(10)) == new DateTimeOffset(2026, 11, 27, 5, 0, 0, offset), "after 5:00, it means tomorrow at 5:00");
			var state = UpdateNightPolicy.Observe(new UpdateNightState(), oneAm);
			Check(!UpdateNightPolicy.ShouldOffer(state, false, oneAm) && UpdateNightPolicy.ShouldOffer(state, true, oneAm), "offered when Windows has a restart pending");
			Check(UpdateNightPolicy.ShouldOffer(state, false, oneAm.AddDays(30)), "offered a month after the last one even with nothing pending");
			var scheduled = state with { ScheduledFor = UpdateNightPolicy.NextRestart(oneAm) };
			Check(!UpdateNightPolicy.ShouldOffer(scheduled, true, oneAm), "not offered again once scheduled");
			Check(UpdateNightPolicy.Decide(scheduled, oneAm.AddHours(3), false) == UpdateNightAction.Wait, "waits until 5:00");
			Check(UpdateNightPolicy.Decide(scheduled, oneAm.AddHours(4), false) == UpdateNightAction.Restart, "restarts at 5:00 with no game");
			Check(UpdateNightPolicy.Decide(scheduled, oneAm.AddHours(5), true) == UpdateNightAction.Wait, "never restarts during a game; keeps waiting");
			Check(UpdateNightPolicy.Decide(scheduled, oneAm.AddHours(6), true) == UpdateNightAction.GiveUp, "a game still running at 7:00: gives up and asks again");
		}
		finally { try { Directory.Delete(folder, true); } catch { } }
		return Task.CompletedTask;
	}
}

internal sealed class MemoryRegistry : ICouchRegistry
{
	private readonly Dictionary<string, object> _values = new(StringComparer.OrdinalIgnoreCase);
	internal bool TouchedMachine { get; private set; }
	internal int Count => _values.Count;
	private static string Id(bool machine, string key, string name) => (machine ? "HKLM\\" : "HKCU\\") + key + "|" + name;
	public object? Read(bool machine, string key, string name) => _values.TryGetValue(Id(machine, key, name), out var value) ? value : null;
	public void Write(bool machine, string key, string name, object value) { TouchedMachine |= machine; _values[Id(machine, key, name)] = value; }
	public void Delete(bool machine, string key, string name) { TouchedMachine |= machine; _values.Remove(Id(machine, key, name)); }
}
