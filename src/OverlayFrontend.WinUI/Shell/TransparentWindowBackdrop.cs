using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using WinUIEx;
using WinUIEx.Messaging;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

// Adapted from WinUIEx TransparentTintBackdrop (MIT, third_party/WinUIEx/LICENSE).
// Each HWND owns one backdrop. The compositor brush lifetime belongs to the base.
internal sealed partial class TransparentWindowBackdrop : CompositionBrushBackdrop
{
    private WindowMessageMonitor? monitor;

    protected override Windows.UI.Composition.CompositionBrush CreateBrush(Windows.UI.Composition.Compositor compositor) =>
        compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(root.ContentIslandEnvironment.AppWindowId);
        monitor = new WindowMessageMonitor(hwnd);
        monitor.WindowMessageReceived += OnMessage;
        try
        {
            ConfigureDwm(hwnd);
            base.OnTargetConnected(target, root);
            var dc = GetDC(hwnd);
            if (dc != 0)
            {
                try { Clear(hwnd, dc); }
                finally { ReleaseDC(hwnd, dc); }
            }
        }
        catch { DisconnectMonitor(); throw; }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        DisconnectMonitor();
        base.OnTargetDisconnected(target);
    }

    private void DisconnectMonitor()
    {
        if (monitor is null) return;
        monitor.WindowMessageReceived -= OnMessage;
        monitor.Dispose();
        monitor = null;
    }

    private static void ConfigureDwm(nint hwnd)
    {
        DwmExtendFrameIntoClientArea(hwnd, default);
        var region = CreateRectRgn(-2, -2, -1, -1);
        if (region == 0) return;
        try { DwmEnableBlurBehindWindow(hwnd, new BlurBehind { Flags = 3, Enabled = 1, Region = region }); }
        finally { DeleteObject(region); }
    }

    private static bool Clear(nint hwnd, nint dc) =>
        GetClientRect(hwnd, out var bounds) != 0 && FillRect(dc, bounds, GetStockObject(4 /* BLACK_BRUSH */)) != 0;

    private void OnMessage(object? sender, WindowMessageEventArgs args)
    {
        if (args.Message.MessageId == 0x0014 /* WM_ERASEBKGND */ && Clear(args.Message.Hwnd, (nint)args.Message.WParam))
        {
            args.Result = 1;
            args.Handled = true;
        }
        else if (args.Message.MessageId == 0x031e /* WM_DWMCOMPOSITIONCHANGED */)
        {
            ConfigureDwm(args.Message.Hwnd);
            args.Result = 0;
            args.Handled = true;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind { public uint Flags; public int Enabled; public nint Region; public int TransitionOnMaximized; }
    [LibraryImport("dwmapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmExtendFrameIntoClientArea(nint hwnd, in Margins margins);
    [LibraryImport("dwmapi.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmEnableBlurBehindWindow(nint hwnd, in BlurBehind blur);
    [LibraryImport("gdi32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateRectRgn(int left, int top, int right, int bottom);
    [LibraryImport("gdi32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DeleteObject(nint value);
    [LibraryImport("gdi32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetStockObject(int index);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetDC(nint hwnd);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ReleaseDC(nint hwnd, nint dc);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClientRect(nint hwnd, out Rect bounds);
    [LibraryImport("user32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FillRect(nint dc, in Rect bounds, nint brush);
}
