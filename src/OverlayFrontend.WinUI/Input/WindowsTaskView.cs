using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal enum WindowsTaskViewResult { Requested, KeyboardBusy, InputRejected, Cancelled }

/// <summary>Explicit shell shortcut request. Never used to obtain activation rights for another action.</summary>
internal static partial class WindowsTaskView
{
    private const ushort WindowsKey = 0x5B, TabKey = 0x09;
    private static readonly int[] guardedKeys = [0x10, 0x11, 0x12, WindowsKey, 0x5C, TabKey];
    internal static bool KeyboardBusy() => guardedKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);
    internal static WindowsTaskViewResult Open() => Request(
        key => (GetAsyncKeyState(key) & 0x8000) != 0,
        events => SendInput((uint)events.Length, events, Marshal.SizeOf<NativeInput>()));

    internal static WindowsTaskViewResult Request(Func<int, bool> isDown, Func<NativeInput[], uint> send)
    {
        // Do not change the meaning of a physically held modifier or release it.
        if (guardedKeys.Any(isDown)) return WindowsTaskViewResult.KeyboardBusy;
        var sent = send([Key(WindowsKey), Key(TabKey), Key(TabKey, up: true), Key(WindowsKey, up: true)]);
        if (sent == 4) return WindowsTaskViewResult.Requested;
        // A partial injection must not leave our keys logically down. Release only
        // keys whose down event was submitted and whose up event was not.
        if (sent == 1 || sent == 3) _ = send([Key(WindowsKey, up: true)]);
        else if (sent == 2) _ = send([Key(TabKey, up: true), Key(WindowsKey, up: true)]);
        return WindowsTaskViewResult.InputRejected;
    }

    private static NativeInput Key(ushort key, bool up = false) => new()
    { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = (key == WindowsKey ? 1u : 0u) | (up ? 2u : 0u) } } };

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeInput { internal uint Type; internal InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput { internal ushort VirtualKey, Scan; internal uint Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint ExtraInfo; }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendInput(uint count, [In] NativeInput[] inputs, int size);
}
