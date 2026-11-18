using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class NativeContextProbeInput
{
    internal static Task LeftClickAsync(FrameworkElement target, Point point, Action? whilePressed = null) =>
        PointerGestureAsync(target, point, point, whilePressed);

    internal static async Task PointerGestureAsync(FrameworkElement target, Point start, Point end,
        Action? whilePressed = null, bool returnToStart = false)
    {
        var window = DemandOwnForeground();
        if (GetCursorPos(out var previous) == 0) throw new InvalidOperationException("Pointer position unavailable.");
        NativePoint ScreenPoint(Point point)
        {
            var client = target.TransformToVisual(null).TransformPoint(point);
            var size = target.XamlRoot.Size;
            if (client.X < 0 || client.Y < 0 || client.X >= size.Width || client.Y >= size.Height)
                throw new InvalidOperationException("Native pointer target lies outside the fixture's client area.");
            var scale = target.XamlRoot.RasterizationScale;
            var screen = new NativePoint { X = (int)(client.X * scale), Y = (int)(client.Y * scale) };
            if (ClientToScreen(window, ref screen) == 0) throw new InvalidOperationException("Pointer coordinates unavailable.");
            return screen;
        }
        var startScreen = ScreenPoint(start);
        var endScreen = ScreenPoint(end);
        void Move(NativePoint screen)
        {
            DemandOwnForeground();
            SendPointerMove(screen);
        }
        var held = false;
        try
        {
            Move(startScreen); await Task.Delay(40);
            SendLeftButton(up: false); held = true; await Task.Delay(60);
            whilePressed?.Invoke();
            Move(endScreen); await Task.Delay(60);
            if (returnToStart) { Move(startScreen); await Task.Delay(60); }
            SendLeftButton(up: true); held = false;
            await Task.Delay(100);
        }
        finally
        {
            // Always release our injected press, including a failed assertion in
            // the mid-gesture callback. Never leave a system mouse button held.
            if (held) SendLeftButton(up: true);
            _ = SetCursorPos(previous.X, previous.Y);
        }
    }

    internal static async Task HoldSliderDragAsync(Microsoft.UI.Xaml.Controls.Slider slider, Func<Task> heldCheck, Func<Task> releasedCheck)
    {
        var window = DemandOwnForeground();
        if (GetCursorPos(out var previous) == 0) throw new InvalidOperationException("Pointer position unavailable.");
        slider.UpdateLayout();
        if (!slider.IsLoaded || slider.ActualWidth <= 0 || slider.ActualHeight <= 0) throw new InvalidOperationException("Slider is not laid out.");
        // In-process automation peers can report an empty rectangle for portal
        // descendants. Use their native XAML-root transform, then verify the
        // actual injected press reaches this slider before asserting a drag.
        var point = slider.TransformToVisual(slider.XamlRoot.Content).TransformPoint(new(slider.ActualWidth / 2, slider.ActualHeight / 2));
        var scale = slider.XamlRoot.RasterizationScale;
        var start = new NativePoint { X = (int)(point.X * scale), Y = (int)(point.Y * scale) };
        if (ClientToScreen(window, ref start) == 0) throw new InvalidOperationException("Slider coordinates unavailable.");
        var held = false; var receivedPress = false;
        Microsoft.UI.Xaml.Input.PointerEventHandler pressed = (_,_) => receivedPress = true;
        slider.AddHandler(UIElement.PointerPressedEvent, pressed, true);
        try
        {
            await ContinuePointerMoveAsync(start, previous);
            SendLeftButton(false); held = true; await Task.Delay(80);
            if (!receivedPress) throw new InvalidOperationException("Injected pointer press did not reach the intended slider.");
            SendPointerMove(new() { X = start.X + 60, Y = start.Y });
            await heldCheck();
            SendLeftButton(true); held = false; await Task.Delay(200);
            SendPointerMove(new() { X = start.X - 60, Y = start.Y });
            await releasedCheck();
        }
        finally { if (held) SendLeftButton(true); slider.RemoveHandler(UIElement.PointerPressedEvent, pressed); SendPointerMove(previous); }
    }

    private static unsafe void SendLeftButton(bool up)
    {
        var input = new NativeInput { Data = new() { Mouse = new() { Flags = up ? 0x0004u : 0x0002u } } };
        if (SendInput(1, &input, sizeof(NativeInput)) != 1) throw new InvalidOperationException("Native modal pointer injection failed.");
    }

    private static unsafe void SendPointerMove(NativePoint point)
    {
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77);
        var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
        if (width <= 0 || height <= 0) throw new InvalidOperationException("Virtual desktop dimensions unavailable.");
        // SetCursorPos relocates the cursor but is not a dependable source of
        // WinUI pointer-move events during a captured gesture. Inject actual,
        // uncoalesced motion so this fixture tests the production drag route.
        var input = new NativeInput { Data = new() { Mouse = new()
        {
            X = (int)(((long)point.X - left) * 65536 / width),
            Y = (int)(((long)point.Y - top) * 65536 / height),
            Flags = 0x0001 | 0x2000 | 0x4000 | 0x8000,
        } } };
        if (SendInput(1, &input, sizeof(NativeInput)) != 1) throw new InvalidOperationException("Native modal pointer motion failed.");
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial int GetSystemMetrics(int index);
}
