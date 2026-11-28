using System;
using System.Runtime.InteropServices;

namespace GamingMode.Services;

// Eternal Core 3.3.11: the Steam controller's trackpads as a mouse in Steam's menus. The Core plugin reads the pads
// inside Steam (Steam owns the controller) and sends batched pointer deltas and pad clicks here
// (POST /input/pointer, token-checked); this moves the Windows pointer with SendInput. The plugin sends nothing while a
// game runs, and the agent refuses while anti-cheat safe mode is engaged.
internal static class MenuPointerInput
{
    internal const int MaxDelta = 4000;
    internal static readonly string[] Buttons = ["left-down", "left-up", "right-down", "right-up"];

    internal static (int dx, int dy, string[] buttons)? Parse(string? dx, string? dy, string? buttons)
    {
        if (!int.TryParse(string.IsNullOrEmpty(dx) ? "0" : dx, out int x) || !int.TryParse(string.IsNullOrEmpty(dy) ? "0" : dy, out int y)) return null;
        if (Math.Abs(x) > MaxDelta || Math.Abs(y) > MaxDelta) return null;
        string[] list = string.IsNullOrEmpty(buttons) ? [] : buttons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (list.Length > 8) return null;
        foreach (string item in list) if (Array.IndexOf(Buttons, item) < 0) return null;
        return (x, y, list);
    }

    internal static bool Send(int dx, int dy, string[] buttons)
    {
        var inputs = new System.Collections.Generic.List<Input>();
        if (dx != 0 || dy != 0) inputs.Add(Mouse(dx, dy, 0x0001));
        foreach (string button in buttons)
        {
            uint flag = button switch { "left-down" => 0x0002, "left-up" => 0x0004, "right-down" => 0x0008, "right-up" => 0x0010, _ => 0 };
            if (flag != 0) inputs.Add(Mouse(0, 0, flag));
        }
        if (inputs.Count == 0) return true;
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) == inputs.Count;
    }

    private static Input Mouse(int dx, int dy, uint flags) => new() { Type = 0, Data = new() { Mouse = new() { X = dx, Y = dy, Flags = flags } } };
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public Data Data; }
    [StructLayout(LayoutKind.Explicit)] private struct Data
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nint Extra; }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
