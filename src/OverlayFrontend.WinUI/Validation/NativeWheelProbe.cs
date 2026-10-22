using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Opt-in fixture input only. Never used by a shipping controller route.</summary>
internal sealed partial class NativeWheelProbe : IDisposable
{
    private readonly nint window;
    private readonly NativePoint point;
    private readonly NativePoint previous;
    private bool movedCursor;
    internal NativeWheelProbe(FrameworkElement target)
    {
        window = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
        DemandOwned(window);
        var bounds = target.TransformToVisual(null).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
        var scale = target.XamlRoot.RasterizationScale;
        point = new() { X = (int)((bounds.X + bounds.Width / 2) * scale), Y = (int)((bounds.Y + bounds.Height / 2) * scale) };
        if (ClientToScreen(window, ref point) == 0 || GetCursorPos(out previous) == 0)
            throw new InvalidOperationException("Cannot locate the fixture viewport.");
    }

    internal object Position => new { point.X, point.Y, window = window.ToInt64() };

    internal void MoveToTarget()
    {
        DemandOwned(window);
        if (GetForegroundWindow() != window) throw new InvalidOperationException("Wheel injection lost its fixture foreground.");
        if (movedCursor && GetCursorPos(out var current) != 0 && (current.X != point.X || current.Y != point.Y))
            throw new InvalidOperationException("User moved the pointer; stopping the wheel fixture.");
        if (SetCursorPos(point.X, point.Y) == 0) throw new InvalidOperationException("Cannot target fixture pointer.");
        movedCursor = true;
        DemandOwned(WindowFromPoint(point));
    }

    internal unsafe void Send(int delta)
    {
        MoveToTarget();
        var input = new NativeInput { Data = new() { Mouse = new() { Data = unchecked((uint)delta), Flags = 0x0800 } } };
        if (SendInput(1, &input, sizeof(NativeInput)) != 1) throw new InvalidOperationException("Fixture wheel injection failed.");
    }

    public void Dispose()
    {
        // Do not undo a real pointer move if the user returned during the probe.
        if (movedCursor && GetCursorPos(out var current) != 0 && current.X == point.X && current.Y == point.Y)
            _ = SetCursorPos(previous.X, previous.Y);
    }

    private static void DemandOwned(nint handle)
    {
        GetWindowThreadProcessId(handle, out var process);
        if (handle == 0 || process != Environment.ProcessId)
            throw new InvalidOperationException("Wheel probe cannot send to another process.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public NativeUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct NativeUnion
    { [FieldOffset(0)] public NativeMouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMouse
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WindowFromPoint(NativePoint point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ClientToScreen(nint window, ref NativePoint point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetCursorPos(out NativePoint point);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetCursorPos(int x, int y);
    [LibraryImport("user32.dll", SetLastError = true), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, NativeInput* inputs, int size);
}
