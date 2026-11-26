using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Playhub.Shared;

// The real registry behind CouchHardening (64-bit view, so policies land where Windows
// reads them even from a 32-bit process).
[SupportedOSPlatform("windows")]
internal sealed class CouchRegistryWindows : ICouchRegistry
{
    private static RegistryKey Root(bool machine) =>
        RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

    public object? Read(bool machine, string key, string name)
    {
        using RegistryKey root = Root(machine);
        using RegistryKey? sub = root.OpenSubKey(key);
        return sub?.GetValue(name) switch { int number => number, string text => text, _ => null };
    }

    public void Write(bool machine, string key, string name, object value)
    {
        using RegistryKey root = Root(machine);
        using RegistryKey sub = root.CreateSubKey(key, writable: true);
        if (value is int number) sub.SetValue(name, number, RegistryValueKind.DWord);
        else sub.SetValue(name, value.ToString() ?? "", RegistryValueKind.String);
    }

    public void Delete(bool machine, string key, string name)
    {
        using RegistryKey root = Root(machine);
        using RegistryKey? sub = root.OpenSubKey(key, writable: true);
        sub?.DeleteValue(name, throwOnMissingValue: false);
    }
}
