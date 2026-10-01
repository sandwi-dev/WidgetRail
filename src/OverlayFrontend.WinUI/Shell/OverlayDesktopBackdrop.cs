using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>A nonactivating native WinUI scrim window behind the overlay, never an input owner.</summary>
internal sealed partial class OverlayDesktopBackdrop : IDisposable
{
    private Window? window;
    private SolidColorBrush? brush;
    private nint handle;
    private bool disposed;
    private Grid? root;
    internal UIElement MotionLayer { get { EnsureWindow(); return root!; } }
    internal event Action? DismissRequested;
    internal bool IsVisible => window?.AppWindow.IsVisible == true;
    internal nint Handle => handle;
    internal Color PaintedColor => brush?.Color ?? default;

    internal void Apply(nint overlay, RectInt32 bounds, Color color, double opacity, bool visible)
    {
        if (disposed) return;
        if (!visible || opacity <= 0) { window?.AppWindow.Hide(); return; }
        EnsureWindow();
        // Native host treats the backdrop as one opaque RGB brush with independent
        // window opacity. Authored brush alpha is not multiplied into that setting.
        color.A = (byte)Math.Clamp(Math.Round(Math.Clamp(opacity, 0, .8) * 255), 0, 255);
        brush!.Color = color;
        if (window!.AppWindow.Position.X != bounds.X || window.AppWindow.Position.Y != bounds.Y ||
            window.AppWindow.Size.Width != bounds.Width || window.AppWindow.Size.Height != bounds.Height)
            window.AppWindow.MoveAndResize(bounds);
        if (!IsVisible) window.AppWindow.Show(activateWindow: false);
        OverlayWindowFrame.SuppressBorder(handle);
        const uint gwHwndPrevious = 3;
        if (GetWindow(handle, gwHwndPrevious) != overlay && SetWindowPos(handle, overlay, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            window.AppWindow.Hide();
            throw new Win32Exception(error, "Backdrop z order could not be established.");
        }
    }

    private void EnsureWindow()
    {
        if (window is not null) return;
        window = new Window { Title = "WidgetRail backdrop", SystemBackdrop = new WinUIEx.TransparentTintBackdrop(), ExtendsContentIntoTitleBar = true };
        handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        window.AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)window.AppWindow.Presenter;
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        OverlayWindowFrame.SuppressBorder(handle);
        const int extendedStyle = -20;
        var before = GetWindowLongPtrW(handle, extendedStyle);
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtrW(handle, extendedStyle, before | 0x08000000 | 0x00000080) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        brush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        root = new Grid { Background = brush };
        AutomationProperties.SetAccessibilityView(root, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        root.PointerPressed += (_, args) => { args.Handled = true; DismissRequested?.Invoke(); };
        window.Content = root;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        window?.AppWindow.Hide(); window?.Close(); window = null; brush = null; handle = 0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowLongPtrW(nint window, int index);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindow(nint window, uint command);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetWindowLongPtrW(nint window, int index, nint value);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
