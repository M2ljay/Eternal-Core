using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Playhub.Shared;

// Eternal Core Mark III: couch hardening. The Windows interruptions that can reach the TV,
// switched off with documented registry values, each one backed up first so Undo puts the
// machine back exactly as it was (a value that did not exist is deleted again).
//
// Per-user values are written by the app directly. Machine values (HKLM) need one admin
// prompt: the app restarts itself elevated with "couch-harden apply|undo <backup file>".
// Windows banners are not in this list: the agent turns them off only while Gaming Mode
// runs (GamingToasts below) and puts them back for the desktop.
internal interface ICouchRegistry
{
    // Null when the value does not exist. DWORDs come back as int, strings as string.
    object? Read(bool machine, string key, string name);
    void Write(bool machine, string key, string name, object value);
    void Delete(bool machine, string key, string name);
}

internal sealed record CouchValue(bool Machine, string Key, string Name, object Value);

internal sealed record CouchFix(string Id, string Label, bool Machine, CouchValue[] Values);

internal static class CouchHardening
{
    private const string Delivery = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Update = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private const string Power = @"SOFTWARE\Policies\Microsoft\Power\PowerSettings\";

    private static CouchValue U(string key, string name, object value) => new(false, key, name, value);
    private static CouchValue M(string key, string name, object value) => new(true, key, name, value);

    internal static readonly CouchFix[] Fixes =
    {
        new("setup-nag", "Nessuna schermata \"Completiamo la configurazione\" dopo gli aggiornamenti", false, new[]
        {
            U(@"Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0),
            U(Delivery, "SubscribedContent-310093Enabled", 0),
        }),
        new("tips", "Niente consigli, suggerimenti e pubblicità di Windows", false, new[]
        {
            U(Delivery, "SubscribedContent-338389Enabled", 0),
            U(Delivery, "SoftLandingEnabled", 0),
            U(Delivery, "SystemPaneSuggestionsEnabled", 0),
            U(Delivery, "RotatingLockScreenOverlayEnabled", 0),
        }),
        new("crash-dialogs", "Niente finestre \"Il programma ha smesso di funzionare\"", false, new[]
        {
            U(@"Software\Microsoft\Windows\Windows Error Reporting", "DontShowUI", 1),
        }),
        new("accessibility-keys", "Niente finestre Tasti permanenti, filtro e interruttore", false, new[]
        {
            U(@"Control Panel\Accessibility\StickyKeys", "Flags", "506"),
            U(@"Control Panel\Accessibility\Keyboard Response", "Flags", "122"),
            U(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58"),
        }),
        // Sound > More sound settings > Communications: "When Windows detects communications
        // activity" = Do nothing (0 mute others, 1 reduce 80%, 2 reduce 50% (default), 3 nothing).
        // An Xbox party or a Discord call then leaves the game and music at full volume.
        new("ducking", "Le chiamate vocali non abbassano il volume di giochi e musica", false, new[]
        {
            U(@"Software\Microsoft\Multimedia\Audio", "UserDuckingPreference", 3),
        }),
        new("update-restarts", "Nessun riavvio per gli aggiornamenti mentre sei connesso", true, new[]
        {
            M(Update, "SetActiveHours", 1), M(Update, "ActiveHoursStart", 11), M(Update, "ActiveHoursEnd", 5),
            M(Update + @"\AU", "NoAutoRebootWithLoggedOnUsers", 1), M(Update + @"\AU", "AUOptions", 4),
            M(Update, "SetUpdateNotificationLevel", 1), M(Update, "UpdateNotificationLevel", 1),
        }),
        new("driver-updates", "Windows Update non cambia il driver grafico", true, new[]
        {
            M(Update, "ExcludeWUDriversInQualityUpdate", 1),
        }),
        new("night-wakes", "Niente risvegli notturni per manutenzione", true, new[]
        {
            M(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "WakeUp", 0),
            M(Power + "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", "ACSettingIndex", 0),
            M(Power + "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", "DCSettingIndex", 0),
        }),
        new("wake-password", "Nessuna password al risveglio", true, new[]
        {
            M(Power + "0e796bdb-100d-47d6-a2d5-f7d2daa51f51", "ACSettingIndex", 0),
            M(Power + "0e796bdb-100d-47d6-a2d5-f7d2daa51f51", "DCSettingIndex", 0),
        }),
        new("edge-prompts", "Niente richieste di Edge al primo avvio o come browser predefinito", true, new[]
        {
            M(@"SOFTWARE\Policies\Microsoft\Edge", "HideFirstRunExperience", 1),
            M(@"SOFTWARE\Policies\Microsoft\Edge", "DefaultBrowserSettingEnabled", 0),
        }),
    };

    internal static IEnumerable<CouchValue> Values(bool machine) => Fixes.Where(fix => fix.Machine == machine).SelectMany(fix => fix.Values);

    internal static bool IsApplied(ICouchRegistry registry, CouchFix fix) => fix.Values.All(value => Same(Safe(registry, value), value.Value));

    // ---------- backup, apply, undo ----------

    internal sealed record Saved(bool Machine, string Key, string Name, string Kind, string? Text, int Number);

    // Applies the values, recording each one's state before the first change. A value
    // already in the backup keeps its original record, so applying twice and undoing
    // once still restores the machine as it was before Core.
    internal static int Apply(ICouchRegistry registry, IEnumerable<CouchValue> values, string backupPath)
    {
        List<Saved> backup = Load(backupPath);
        int changed = 0;
        foreach (CouchValue value in values)
        {
            object? before = Safe(registry, value);
            if (!backup.Any(saved => Matches(saved, value)))
                backup.Add(before switch
                {
                    null => new Saved(value.Machine, value.Key, value.Name, "absent", null, 0),
                    int number => new Saved(value.Machine, value.Key, value.Name, "dword", null, number),
                    _ => new Saved(value.Machine, value.Key, value.Name, "string", Convert.ToString(before), 0),
                });
            Store(backupPath, backup);
            if (Same(before, value.Value)) continue;
            registry.Write(value.Machine, value.Key, value.Name, value.Value);
            changed++;
        }
        return changed;
    }

    // Restores the recorded values of one hive (per-user or machine) and keeps the rest.
    internal static int Undo(ICouchRegistry registry, bool machine, string backupPath)
    {
        List<Saved> backup = Load(backupPath);
        int restored = 0;
        foreach (Saved saved in backup.Where(saved => saved.Machine == machine).ToList())
        {
            if (saved.Kind == "absent") registry.Delete(machine, saved.Key, saved.Name);
            else registry.Write(machine, saved.Key, saved.Name, saved.Kind == "dword" ? saved.Number : saved.Text ?? "");
            backup.Remove(saved);
            restored++;
        }
        if (backup.Count == 0) { try { File.Delete(backupPath); } catch (IOException) { } }
        else Store(backupPath, backup);
        return restored;
    }

    // ---------- Windows banners while Gaming Mode runs ----------

    internal static readonly CouchValue GamingToasts =
        U(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled", 0);

    // Gaming Mode: banners off (the previous value is recorded once). Desktop Mode: the
    // recorded value comes back. Returns true when something changed.
    internal static bool SetGamingToasts(ICouchRegistry registry, bool gaming, string backupPath)
    {
        if (gaming) return Apply(registry, new[] { GamingToasts }, backupPath) > 0;
        return File.Exists(backupPath) && Undo(registry, false, backupPath) > 0;
    }

    // ---------- helpers ----------

    private static object? Safe(ICouchRegistry registry, CouchValue value)
    {
        try { return registry.Read(value.Machine, value.Key, value.Name); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or System.Security.SecurityException) { return null; }
    }

    private static bool Same(object? current, object wanted) => current switch
    {
        int number => wanted is int target && number == target,
        string text => wanted is string target && string.Equals(text, target, StringComparison.Ordinal),
        _ => false,
    };

    private static bool Matches(Saved saved, CouchValue value) =>
        saved.Machine == value.Machine && string.Equals(saved.Key, value.Key, StringComparison.OrdinalIgnoreCase)
        && string.Equals(saved.Name, value.Name, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static List<Saved> Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    private static void Store(string path, List<Saved> backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(backup, Json));
        File.Move(temporary, path, true);
    }
}
