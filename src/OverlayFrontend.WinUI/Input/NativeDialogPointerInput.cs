using System.Runtime.InteropServices;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>A virtual controller pointer; native mouse messages never move the physical mouse.</summary>
internal sealed partial class NativeDialogPointerInput(Action<string> trace) : IDisposable
{
    private readonly ControllerPointerMotion motion = new();
    private readonly RightStickScroll scroll = new();
    private readonly NativeControllerCursor visual = new();
    private Point position;
    private nint pressedWindow;
    private bool controllerVisible;
    private Point desktopPosition;
    private int desktopButtons;
    private bool desktopPositionKnown;
    private double remainderX, remainderY, wheelX, wheelY;
    internal (int X, int Y, bool Visible) Position => (position.X, position.Y, controllerVisible);
#if ENABLE_WIDGET_VALIDATION
    internal string Status { get; private set; } = "idle";
#endif
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
        position = new() { X = bounds.Left + (bounds.Right - bounds.Left) / 2, Y = bounds.Top + (bounds.Bottom - bounds.Top) / 2 };
        desktopPositionKnown = GetCursorPos(out desktopPosition) != 0;
        desktopButtons = MouseButtons(); controllerVisible = true;
    }
    internal void ObservePhysicalInput()
    {
        // GetLastInputInfo includes keyboard/gamepad-related input, not just mouse
        // input. Using it here cancelled A-down before Chromium received A-up.
        // Posted controller messages never change the system cursor/button state.
        var known = GetCursorPos(out var current) != 0;
        var buttons = MouseButtons();
        var moved = known && desktopPositionKnown && (current.X != desktopPosition.X || current.Y != desktopPosition.Y);
        var changed = buttons != desktopButtons;
        desktopPosition = current; desktopPositionKnown = known; desktopButtons = buttons;
        if (moved || changed) Reset(moved ? "mouse-moved" : "mouse-button");
    }
    internal void Paint(uint dpi, ControllerPointerAppearance appearance)
    {
        if (controllerVisible) visual.Show(position.X, position.Y, dpi, appearance);
        else visual.Hide();
    }
    internal bool Receive(ControllerFrame frame, Bounds bounds, Func<bool> ownsForeground, Func<Point, nint> resolveWindow)
    {
        if (frame.Connected == 0 || frame.Primed != 0 || !bounds.Valid || !ownsForeground()) { Reset("input-unavailable"); return false; }
        ObservePhysicalInput();
        var delta = motion.Sample(frame.State.LeftThumbX, frame.State.LeftThumbY, Environment.TickCount64);
        var wheel = scroll.Sample(frame.State.RightThumbX, frame.State.RightThumbY, Environment.TickCount64);
        var activity = delta != default || wheel != default || (frame.PressedButtons & 0x1000) != 0;
        if (activity) controllerVisible = true;
        remainderX += delta.X * (bounds.Right - bounds.Left); remainderY += delta.Y * (bounds.Bottom - bounds.Top);
        var dx = (int)remainderX; var dy = (int)remainderY; remainderX -= dx; remainderY -= dy;
        position.X = Math.Clamp(position.X + dx, bounds.Left, bounds.Right - 1);
        position.Y = Math.Clamp(position.Y + dy, bounds.Top, bounds.Bottom - 1);
        var hit = resolveWindow(position);
        var inside = bounds.Contains(position) && hit != 0;
#if ENABLE_WIDGET_VALIDATION
        Status = $"inside={inside} cursor={position.X},{position.Y} bounds={bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom} held={pressedWindow != 0}";
#endif
        if (!inside) { CancelPress(); return false; }
        var target = pressedWindow != 0 ? pressedWindow : hit;
        if (activity && ownsForeground()) SendClient(target, 0x0200, pressedWindow != 0 ? 1u : 0u, position);
        if (pressedWindow != 0 && ((frame.ReleasedButtons & 0x1000) != 0 || (frame.State.Buttons & 0x1000) == 0))
        {
            var delivered = SendClient(pressedWindow, 0x0202, 0, position);
            trace($"native-pointer release posted={delivered}"); pressedWindow = 0; return true;
        }
        if ((frame.PressedButtons & 0x1000) != 0 && pressedWindow == 0 && !PhysicalModifiersHeld() && (GetAsyncKeyState(1) & 0x8000) == 0 && ownsForeground())
        {
            if (SendClient(hit, 0x0201, 1, position)) { pressedWindow = hit; trace("native-pointer press"); }
        }
        wheelX += wheel.X; wheelY -= wheel.Y;
        var horizontal = (int)wheelX; var vertical = (int)wheelY; wheelX -= horizontal; wheelY -= vertical;
        if (!PhysicalModifiersHeld() && ownsForeground())
        {
            if (vertical != 0) SendWheel(hit, 0x020A, vertical);
            if (horizontal != 0) SendWheel(hit, 0x020E, horizontal);
        }
        return false;
    }
    internal void ReleaseInput()
    {
        CancelPress(); motion.Reset(); scroll.Reset();
        remainderX = remainderY = wheelX = wheelY = 0;
    }
    internal void Reset(string reason = "lifecycle")
    {
        if (controllerVisible || pressedWindow != 0) trace($"native-pointer reset reason={reason} held={pressedWindow != 0}");
        ReleaseInput(); visual.Hide(); controllerVisible = false;
    }
    private void CancelPress()
    {
        if (pressedWindow == 0) return;
        trace("native-pointer cancelled-press");
        // Cancel the control's capture before an out-of-control release.
        // No synthetic button is held in the global Windows input state.
        _ = PostMessageW(pressedWindow, 0x001F, 0, 0);
        _ = PostMessageW(pressedWindow, 0x0202, 0, Pack(-1, -1));
        pressedWindow = 0;
    }
    private static bool SendClient(nint window, uint message, nuint flags, Point point)
    {
        if (ScreenToClient(window, ref point) == 0) return false;
        return PostMessageW(window, message, flags, Pack(point.X, point.Y)) != 0;
    }
    private void SendWheel(nint window, uint message, int delta) =>
        _ = PostMessageW(window, message, (nuint)(unchecked((uint)(ushort)(short)Math.Clamp(delta, short.MinValue, short.MaxValue)) << 16), Pack(position.X, position.Y));
    private static nint Pack(int x, int y) => unchecked((nint)((uint)(ushort)x | ((uint)(ushort)y << 16)));
    private static int MouseButtons()
    {
        var result = 0;
        // Only the current-down bits are reliable. The low "pressed since last
        // query" bits can be consumed by another application's polling.
        ReadOnlySpan<int> keys = [1, 2, 4, 5, 6];
        foreach (var key in keys)
            if ((GetAsyncKeyState(key) & 0x8000) != 0) result |= 1 << key;
        return result;
    }
    public void Dispose() { Reset(); visual.Dispose(); }
    private static bool PhysicalModifiersHeld() =>
        (GetAsyncKeyState(0x10) & 0x8000) != 0 || (GetAsyncKeyState(0x11) & 0x8000) != 0 ||
        (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetCursorPos(out Point point);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ScreenToClient(nint window, ref Point point);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int PostMessageW(nint window, uint message, nuint wparam, nint lparam);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetAsyncKeyState(int key);
}
