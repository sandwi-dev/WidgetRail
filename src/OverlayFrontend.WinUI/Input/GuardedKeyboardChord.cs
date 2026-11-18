using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal enum KeyboardChordResult { Requested, KeyboardBusy, InputRejected }

/// <summary>Host-owned shortcuts only. Callers own foreground, authority and gesture release.</summary>
internal static partial class GuardedKeyboardChord
{
    private static readonly int[] modifiers = [0x10, 0x11, 0x12, 0x5B, 0x5C];

    internal static bool KeyboardBusy(ReadOnlySpan<ushort> keys) => KeyboardBusy(keys, IsDown);

    internal static KeyboardChordResult Request(ReadOnlySpan<ushort> keys) => Request(keys, IsDown,
        events => SendInput((uint)events.Length, events, Marshal.SizeOf<NativeInput>()));

    internal static KeyboardChordResult Request(ReadOnlySpan<ushort> keys,
        Func<int, bool> isDown, Func<NativeInput[], uint> send)
    {
        if (keys.Length is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(keys));
        for (var i = 0; i < keys.Length; i++)
            if (keys[i] == 0 || keys[..i].Contains(keys[i]))
                throw new ArgumentException("Shortcut keys must be nonzero and unique.", nameof(keys));

        // Never change the meaning of a physically held modifier or release it.
        if (KeyboardBusy(keys, isDown)) return KeyboardChordResult.KeyboardBusy;
        var events = new NativeInput[keys.Length * 2];
        for (var i = 0; i < keys.Length; i++)
        {
            events[i] = Key(keys[i]);
            events[events.Length - i - 1] = Key(keys[i], up: true);
        }
        var sent = send(events);
        if (sent == events.Length) return KeyboardChordResult.Requested;

        // SendInput accepts an ordered prefix. Release only injected downs whose
        // matching ups were not accepted; never retry the shortcut itself.
        var outstanding = new List<NativeInput>();
        for (var i = keys.Length - 1; i >= 0; i--)
            if (i < sent && events.Length - i - 1 >= sent)
                outstanding.Add(Key(keys[i], up: true));
        if (outstanding.Count != 0) _ = send(outstanding.ToArray());
        return KeyboardChordResult.InputRejected;
    }

    private static bool KeyboardBusy(ReadOnlySpan<ushort> keys, Func<int, bool> isDown)
    {
        if (modifiers.Any(isDown)) return true;
        foreach (var key in keys) if (isDown(key)) return true;
        return false;
    }

    private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    private static NativeInput Key(ushort key, bool up = false) => new()
    {
        Type = 1,
        Data = new() { Keyboard = new() { VirtualKey = key,
            Flags = (key is 0x5B or 0x5C ? 1u : 0u) | (up ? 2u : 0u) } }
    };

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
