using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamingMode.Services;
internal static class WindowsXboxGameBar
{
    internal static GameBarGuideResult ToggleVerified(uint appId)
    {
        var deadline = Stopwatch.StartNew();
        nint foreground = OverlayWindowTools.GetForegroundWindowHandle();
        uint pid = (uint)OverlayWindowTools.WindowProcessId(foreground);
        if (pid == 0) return new(false, false, "unverifiedForeground");
        var sessions = UwpSteamSessionAssociation.FindForApp(appId);
        return GameBarInteractionGuard.Run(sessions.Count > 0,
            () => sessions.Any(s => s.GamePid == pid),
            () => Windows.Gaming.UI.GameBar.IsInputRedirected,
            () => SendVerifiedChord(foreground, deadline));
    }
    private static GameBarGuideResult SendVerifiedChord(nint foreground, Stopwatch deadline)
    {
        // Keep the verified foreground boundary adjacent to the single input chord.
        if (deadline.ElapsedMilliseconds >= 600 || OverlayWindowTools.GetForegroundWindowHandle() != foreground) return new(false, false, "foregroundChanged");
        Input[] chord = [ Key(0x5B, false), Key(0x47, false), Key(0x47, true), Key(0x5B, true) ];
        uint sent = SendInput((uint)chord.Length, chord, Marshal.SizeOf<Input>());
        if (sent != chord.Length)
        {
            Input[] release = [Key(0x47, true), Key(0x5B, true)];
            SendInput(2, release, Marshal.SizeOf<Input>());
            return new(false, false, "inputRejected");
        }
        return new(true, true, "opened");
    }
    // Core Voice (Xbox party button): the plain Win+G shortcut, with no Xbox-game session to verify.
    // The caller (PartyLaunchPolicy) sends it only when no game runs and the bar is known to be closed.
    internal static bool SendShortcut()
    {
        Input[] chord = [ Key(0x5B, false), Key(0x47, false), Key(0x47, true), Key(0x5B, true) ];
        uint sent = SendInput((uint)chord.Length, chord, Marshal.SizeOf<Input>());
        if (sent == chord.Length) return true;
        if (sent > 0)
        {
            // Win went down: release what is held. A lone Win up would open Start over Big Picture, so a
            // neutral key (VK_NONAME) is pressed first, the usual way to suppress it.
            var release = new System.Collections.Generic.List<Input>();
            if (sent == 2) release.Add(Key(0x47, true));
            release.Add(Key(0xFC, false));
            release.Add(Key(0xFC, true));
            release.Add(Key(0x5B, true));
            SendInput((uint)release.Count, release.ToArray(), Marshal.SizeOf<Input>());
        }
        return false;
    }
    private static Input Key(ushort key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Data Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Data
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
