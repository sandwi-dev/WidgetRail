using System.Runtime.InteropServices;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Pointer transport for an explicitly admitted native dialog, independent of its kind.</summary>
internal sealed partial class NativeDialogPointerInput
{
    private readonly ControllerPointerMotion motion = new();
    private readonly RightStickScroll scroll = new();
    private bool pressed;
    private double remainderX, remainderY, wheelX, wheelY;
#if ENABLE_WIDGET_VALIDATION
    internal string Status { get; private set; } = "idle";
#endif

    // Physical screen coordinates, including negative coordinates on secondary monitors.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Bounds
    {
        internal int Left, Top, Right, Bottom;
        internal readonly bool Valid => Right > Left && Bottom > Top;
        internal readonly bool Contains(Point point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }

    internal void Center(Bounds bounds)
    {
        Reset();
        if (bounds.Valid) _ = SetCursorPos(bounds.Left + (bounds.Right - bounds.Left) / 2, bounds.Top + (bounds.Bottom - bounds.Top) / 2);
    }

    // True means a completed click may have closed the dialog: its owner must recheck lifetime.
    internal bool Receive(ControllerFrame frame, Bounds bounds, Func<bool> ownsForeground, Func<nint, bool> ownsPoint)
    {
        if (frame.Connected == 0 || frame.Primed != 0 || !bounds.Valid || !ownsForeground()) { Reset(); return false; }
        if (GetCursorPos(out var cursor) == 0) return false;
        var delta = motion.Sample(frame.State.LeftThumbX, frame.State.LeftThumbY, Environment.TickCount64);
        if (delta != default)
        {
            remainderX += delta.X * (bounds.Right - bounds.Left);
            remainderY += delta.Y * (bounds.Bottom - bounds.Top);
            var dx = (int)remainderX; var dy = (int)remainderY;
            remainderX -= dx; remainderY -= dy;
            cursor.X = Math.Clamp(cursor.X + dx, bounds.Left, bounds.Right - 1);
            cursor.Y = Math.Clamp(cursor.Y + dy, bounds.Top, bounds.Bottom - 1);
            if (ownsForeground()) _ = SetCursorPos(cursor.X, cursor.Y);
        }
        // Never click/scroll another window or the webpage surrounding a native modal.
        var inside = bounds.Contains(cursor) && ownsPoint(WindowFromPoint(cursor));
#if ENABLE_WIDGET_VALIDATION
        Status = $"inside={inside} cursor={cursor.X},{cursor.Y} bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} held={pressed} modifiers={PhysicalModifiersHeld()}";
#endif
        if (!inside) { Release(); scroll.Reset(); return false; }
        if (pressed && ((frame.ReleasedButtons & 0x1000) != 0 || (frame.State.Buttons & 0x1000) == 0))
        { Release(); return true; }
        if ((frame.PressedButtons & 0x1000) != 0 && !pressed && !PhysicalModifiersHeld() && (GetAsyncKeyState(1) & 0x8000) == 0 && ownsForeground())
            pressed = SendMouse(0x0002); // MOUSEEVENTF_LEFTDOWN; the release can occur on a later frame (drag).
        var wheel = scroll.Sample(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
        wheelX += wheel.X; wheelY -= wheel.Y;
        var horizontal = (int)wheelX; var vertical = (int)wheelY;
        wheelX -= horizontal; wheelY -= vertical;
        if (!PhysicalModifiersHeld() && ownsForeground())
        {
            if (vertical != 0) _ = SendMouse(0x0800, vertical);
            if (horizontal != 0) _ = SendMouse(0x1000, horizontal);
        }
        return false;
    }

    internal void Reset()
    {
        Release(); motion.Reset(); scroll.Reset();
        remainderX = remainderY = wheelX = wheelY = 0;
    }
    private void Release()
    {
        if (!pressed) return;
        // Complete only our own press, including disconnect, focus loss and teardown.
        if (SendMouse(0x0004)) pressed = false;
    }
    private static bool PhysicalModifiersHeld() =>
        (GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
    private static unsafe bool SendMouse(uint flags, int data = 0)
    {
        var input = new NativeInput { Type = 0, Mouse = new() { Flags = flags, Data = unchecked((uint)data) } };
        if (SendInput(1, &input, sizeof(NativeInput)) == 1) return true;
        Diagnostics.FrontendFailureLog.Current.Write("native-dialog-pointer", null, "Windows rejected a controller pointer event.");
        return false;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }
    // MOUSEINPUT is the largest INPUT union member; x64 alignment supplies the union offset/padding.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { internal uint Type; internal MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetCursorPos(int x, int y);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint WindowFromPoint(Point point);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, NativeInput* input, int size);
}
