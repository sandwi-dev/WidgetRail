using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Synthetic desktop input for native routing checks; never controller evidence.</summary>
internal static partial class NativeContextProbeInput
{
    internal static async Task RightClickAsync(FrameworkElement target)
    {
        var window = DemandOwnForeground();
        var bounds = target.TransformToVisual(null).TransformBounds(new(0, 0, target.ActualWidth, target.ActualHeight));
        var scale = target.XamlRoot.RasterizationScale;
        var point = new NativePoint { X = (int)((bounds.Left + bounds.Width / 2) * scale), Y = (int)((bounds.Top + bounds.Height / 2) * scale) };
        if (ClientToScreen(window, ref point) == 0 || GetCursorPos(out var previous) == 0 || SetCursorPos(point.X, point.Y) == 0)
            throw new InvalidOperationException("Native context pointer could not be positioned.");
        try { SendMouse(); await Task.Delay(150); }
        finally { _ = SetCursorPos(previous.X, previous.Y); }
    }
    internal static void Keyboard(bool shiftF10) => SendKeyboard(shiftF10 ? VirtualKey.F10 : VirtualKey.Application, shiftF10);
    internal static void Escape() => SendKeyboard(VirtualKey.Escape, false);
    private static unsafe void SendKeyboard(VirtualKey key, bool shiftF10)
    {
        DemandOwnForeground();
        var inputs = stackalloc NativeInput[4];
        var count = 0;
        if (shiftF10) inputs[count++] = Key(VirtualKey.Shift, false);
        inputs[count++] = Key(key, false); inputs[count++] = Key(key, true);
        if (shiftF10) inputs[count++] = Key(VirtualKey.Shift, true);
        if (SendInput((uint)count, inputs, sizeof(NativeInput)) != count)
            throw new InvalidOperationException("Native context keyboard injection failed.");
    }
    private static NativeInput Key(VirtualKey key, bool up) => new() { Type = 1,
        Data = new() { Keyboard = new() { Key = (ushort)key, Flags = up ? 2u : 0 } } };
    private static unsafe void SendMouse()
    {
        DemandOwnForeground();
        var inputs = stackalloc NativeInput[2];
        inputs[0] = new() { Data = new() { Mouse = new() { Flags = 0x0008 } } };
        inputs[1] = new() { Data = new() { Mouse = new() { Flags = 0x0010 } } };
        if (SendInput(2, inputs, sizeof(NativeInput)) != 2) throw new InvalidOperationException("Native context mouse injection failed.");
    }
    private static nint DemandOwnForeground()
    {
        var window = GetForegroundWindow(); GetWindowThreadProcessId(window, out var process);
        if (process != Environment.ProcessId) throw new InvalidOperationException("Context input probe requires its own foreground window.");
        return window;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public NativeUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct NativeUnion
    { [FieldOffset(0)] public NativeMouse Mouse; [FieldOffset(0)] public NativeKeyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeKeyboard { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [LibraryImport("user32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, NativeInput* inputs, int size);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int GetCursorPos(out NativePoint point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int SetCursorPos(int x, int y);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static partial int ClientToScreen(nint window, ref NativePoint point);
}
