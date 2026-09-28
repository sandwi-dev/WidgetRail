using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WindowsDisplayProvider;
using Windows.Graphics;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private bool placementQueued;
    private bool resolvingDisplay;
    private bool displayDirty;
    private bool applyingPlacement;
    private nint placementWindow;
    private ulong? placementDisplay;
    private XamlRoot? observedXamlRoot;

    private void ToggleOverlay()
    {
        if (AppWindow.IsVisible && input?.IsForeground == true) { HideOverlay(); return; }
        input?.PrepareShow();
        if (RootFrame.Content is Shell.OverlayShellPage page)
        {
            placementWindow = input?.PlacementWindow ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
            QueueDisplayRefresh();
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

    private void ApplyOverlayPlacement(AppearanceSettings appearance) => QueueOverlayPlacement();

    private void InitializeOverlaySizing(OverlayShellPage page)
    {
        placementWindow = input?.PlacementWindow ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
        page.BridgeReady += QueueDisplayRefresh;
        page.SizingChanged += QueueOverlayPlacement;
        page.Loaded += (_, _) =>
        {
            observedXamlRoot = ShellRoot.XamlRoot;
            if (observedXamlRoot is not null) observedXamlRoot.Changed += OverlayXamlRootChanged;
            QueueDisplayRefresh();
        };
        AppWindow.Changed += OverlayWindowChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OverlayDisplaySettingsChanged;
    }

    private void RetireOverlaySizing()
    {
        if (observedXamlRoot is not null) observedXamlRoot.Changed -= OverlayXamlRootChanged;
        AppWindow.Changed -= OverlayWindowChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OverlayDisplaySettingsChanged;
    }

    private void OverlayDisplaySettingsChanged(object? sender, EventArgs args) => DispatcherQueue.TryEnqueue(() => QueueDisplayRefresh());
    private void OverlayXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => QueueOverlayPlacement();
    private void OverlayWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (applyingPlacement || cleanupStarted || RootFrame.Content is not OverlayShellPage || !args.DidPositionChange) return;
        var current = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).DisplayId.Value;
        if (current == placementDisplay) return;
        placementWindow = WinRT.Interop.WindowNative.GetWindowHandle(this);
        QueueDisplayRefresh();
    }

    private void QueueDisplayRefresh()
    {
        if (cleanupStarted) return;
        displayDirty = true;
        if (resolvingDisplay) return;
        _ = RefreshDisplayAsync();
    }

    private async Task RefreshDisplayAsync()
    {
        resolvingDisplay = true;
        try
        {
            while (displayDirty && !cleanupStarted && RootFrame.Content is OverlayShellPage page)
            {
                displayDirty = false;
                var target = placementWindow;
                var context = await Task.Run(() => WindowDisplayContext.Read(target));
                if (cleanupStarted) return;
                if (target != placementWindow) { displayDirty = true; continue; }
                await page.SetDisplayAsync(context);
                QueueOverlayPlacement();
            }
        }
        catch (Exception error) { (RootFrame.Content as OverlayShellPage)?.ReportFailure(error); }
        finally { resolvingDisplay = false; }
    }

    private void QueueOverlayPlacement()
    {
        if (placementQueued || cleanupStarted) return;
        placementQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            placementQueued = false;
            if (cleanupStarted || RootFrame.Content is not OverlayShellPage page) return;
            try { ResolveOverlayPlacement(page); }
            catch (Exception error) { page.ReportFailure(error); }
        })) placementQueued = false;
    }

    private void ResolveOverlayPlacement(OverlayShellPage page)
    {
        var appearance = page.Appearance;
        var target = placementWindow != 0 ? placementWindow : WinRT.Interop.WindowNative.GetWindowHandle(this);
        var display = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(target), DisplayAreaFallback.Nearest);
        var work = display.WorkArea;
        var dpi = GetDpiForWindow(target);
        if (dpi == 0) dpi = 96;
        var measuredScale = ScaleRoot.InterfaceScale;
        // Work-area margins use monitor DIPs, not the user's UI zoom. The native
        // placement adapter applies the same margins to the final physical HWND.
        var pixelScale = dpi / 96d * appearance.InterfaceScale;
        var available = new SurfaceExtent(Math.Max(1, work.Width - 48 * dpi / 96d) / pixelScale,
            Math.Max(1, work.Height - 56 * dpi / 96d) / pixelScale);
        // Use actual native chrome after layout, with the original shell reserve
        // until the first native measurement exists. No widget geometry is copied.
        var chrome = page.ContentWidth > 0 && page.ContentHeight > 0 && ScaleRoot.ActualWidth > 0
            ? new SurfaceExtent(Math.Max(0, ScaleRoot.ActualWidth / measuredScale - page.ContentWidth),
                Math.Max(0, ScaleRoot.ActualHeight / measuredScale - page.ContentHeight))
            : new SurfaceExtent(72, 178);
        ScaleRoot.InterfaceScale = appearance.InterfaceScale;
        var size = OverlaySurfaceSizing.Resolve(page.SurfaceHints, available, chrome, appearance.TextScale, page.MeasureContent);
        page.RecordSizing(available, chrome, size);
        var request = PlacementInput.Create();
        request.WorkLeft = work.X;
        request.WorkTop = work.Y;
        request.WorkRight = work.X + work.Width;
        request.WorkBottom = work.Y + work.Height;
        request.Dpi = dpi;
        request.DesiredWidthDip = (float)Math.Max(1, size.Width * appearance.InterfaceScale);
        request.DesiredHeightDip = (float)Math.Max(1, size.Height * appearance.InterfaceScale);
        var placement = Placement.Create();
        // ComputePlacement is a pure adapter operation and does not create a
        // hardware reader, including --shell-no-controller automated runs.
        var native = new OverlayPlatformNative();
        var status = native.ComputePlacement(request, ref placement, out var present);
        if (status != PlatformStatus.Ok || present == 0) return;
        var inset = (int)Math.Round(24 * dpi / 96d);
        if (appearance.OverlayPosition == OverlayPosition.BottomLeft) placement.X = Math.Min(work.X + inset, work.X + work.Width - placement.Width);
        else if (appearance.OverlayPosition == OverlayPosition.BottomRight) placement.X = Math.Max(work.X, work.X + work.Width - placement.Width - inset);
        placementDisplay = display.DisplayId.Value;
        if (AppWindow.Position.X == placement.X && AppWindow.Position.Y == placement.Y &&
            AppWindow.Size.Width == placement.Width && AppWindow.Size.Height == placement.Height) return;
        applyingPlacement = true;
        try { AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height)); }
        finally { applyingPlacement = false; }
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint window);
}
