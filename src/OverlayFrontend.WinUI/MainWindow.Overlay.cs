using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private void ToggleOverlay()
    {
        if (AppWindow.IsVisible && input?.IsForeground == true) { HideOverlay(); return; }
        input?.PrepareShow();
        if (RootFrame.Content is Shell.OverlayShellPage page)
        {
            ApplyOverlayPlacement(page.Appearance);
            page.SetVisible(true);
        }
        AppWindow.Show();
        Activate();
        input?.AcquireForeground();
        StartInput();
    }

    private void HideOverlay()
    {
        (RootFrame.Content as Shell.OverlayShellPage)?.SetVisible(false);
        input?.SetVisible(false);
        AppWindow.Hide();
    }

    private void ApplyOverlayPlacement(AppearanceSettings appearance)
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var request = PlacementInput.Create();
        request.WorkLeft = work.X;
        request.WorkTop = work.Y;
        request.WorkRight = work.X + work.Width;
        request.WorkBottom = work.Y + work.Height;
        request.Dpi = dpi;
        request.DesiredWidthDip = (float)(1200 * appearance.InterfaceScale);
        request.DesiredHeightDip = (float)(860 * appearance.InterfaceScale);
        var placement = Placement.Create();
        // ComputePlacement is a pure adapter operation and does not create a
        // hardware reader, including --shell-no-controller automated runs.
        var native = new OverlayPlatformNative();
        var status = native.ComputePlacement(request, ref placement, out var present);
        if (status != PlatformStatus.Ok || present == 0) return;
        var inset = (int)Math.Round(24 * dpi / 96d);
        if (appearance.OverlayPosition == OverlayPosition.BottomLeft) placement.X = work.X + inset;
        else if (appearance.OverlayPosition == OverlayPosition.BottomRight) placement.X = work.X + work.Width - placement.Width - inset;
        AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height));
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint window);
}
