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
    private bool chooserHidePending;
    private long chooserHideVersion;

    private void ToggleOverlay()
    {
        if (chooserHidePending) { ShowOverlay(); return; }
        if (overlayRequestedVisible && AppWindow.IsVisible &&
            ((input?.IsForeground ?? (RootFrame.Content as OverlayShellPage)?.HasForeground) == true || nativeBrowserDialog?.HasForeground == true)) { HideOverlay(); return; }
        ShowOverlay();
    }

    internal void ShowOverlay()
    {
        if (cleanupStarted) return;
        CancelTaskHandoff();
        if (input?.PrepareShow() == false) return;
        chooserHidePending = false; ++chooserHideVersion;
        var entering = !overlayRequestedVisible;
        overlayRequestedVisible = true;
        if (entering)
        {
            ++overlayVisibilityVersion;
            EnsureOverlayMotion();
            overlayOpenPending = true;
        }
        ShellRoot.IsHitTestVisible = true;
        if (RootFrame.Content is Shell.OverlayShellPage page)
        {
            placementWindow = input?.PlacementWindow ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
            QueueDisplayRefresh();
            page.SetVisible(true);
        }
        AppWindow.Show();
        nativeBrowserDialog?.SetHostVisibility(true);
        Activate();
        input?.AcquireForeground();
        if (entering && Input.PlatformInputPump.IsForegroundProcess())
        {
            try { mouseSession?.Begin(); }
            catch (Exception error)
            {
                mouseSession?.End();
                Diagnostics.FrontendFailureLog.Current.Write("overlay-mouse", error);
            }
        }
        StartInput();
        QueueOverlayOpen();
    }

    private void HideOverlay()
    {
        if (cleanupStarted || !overlayRequestedVisible) return;
        if (DeferHideForChooser(immediate: false)) return;
        mouseSession?.End();
        nativeBrowserDialog?.SetHostVisibility(false);
        if (overlayOpenPending && overlayMotion?.Playback is not { IsCompleted: false })
        { HideOverlayImmediately(); return; }
        overlayRequestedVisible = false;
        overlayOpenPending = false;
        var version = ++overlayVisibilityVersion;
        ShellRoot.IsHitTestVisible = false;
        // Logical visibility and input retire now; native pixels survive only
        // until this short exit completes. No widget action waits on animation.
        (RootFrame.Content as Shell.OverlayShellPage)?.SetVisible(false, retainExitPresentation: true);
        input?.SetVisible(false);
        _ = PlayOverlayVisibilityAsync(version, opening: false);
    }

    private bool DeferHideForChooser(bool immediate)
    {
        if (cleanupStarted || nativeBrowserDialog is not { Active: true } chooser) return false;
        if (chooserHidePending) return true;
        chooserHidePending = true;
        var version = ++chooserHideVersion;
        _ = CompleteAsync();
        return true;
        async Task CompleteAsync()
        {
            await chooser.CloseBeforeHideAsync();
            if (cleanupStarted || !chooserHidePending || version != chooserHideVersion) return;
            chooserHidePending = false;
            if (immediate) HideOverlayImmediately(); else HideOverlay();
        }
    }

    private void DismissForExternalForeground(nint observed)
    {
        // Recheck at the consumer: an obsolete observation must never hide a
        // newly reopened overlay, or a transfer to one of our own pinned HWNDs.
        if (cleanupStarted || !AppWindow.IsVisible || input?.IsCurrentExternalForeground(observed) != true) return;
        input.TraceInput("Hiding overlay after confirmed external foreground change");
        HideOverlayImmediately();
    }

    private void ApplyOverlayPlacement(AppearanceSettings appearance) => QueueOverlayPlacement();

    private void InitializeOverlaySizing(OverlayShellPage page)
    {
        placementWindow = input?.PlacementWindow ?? WinRT.Interop.WindowNative.GetWindowHandle(this);
        page.BridgeReady += QueueDisplayRefresh;
        page.SizingChanged += QueueOverlayPlacement;
        RootFrame.SizeChanged += (_, _) => QueueOverlayPlacement();
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
        var display = OverlayDisplayArea.Resolve(AppWindow.Id, AppWindow.Id);
        if (display is null) return;
        var current = display.DisplayId.Value;
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
        var display = OverlayDisplayArea.Resolve(Win32Interop.GetWindowIdFromWindow(target), AppWindow.Id);
        // Preserve the last valid placement if Windows is between display
        // configurations. Display/XamlRoot notifications will request it again.
        if (display is null) return;
        var work = display.WorkArea;
        // The work-area shell stays stationary across widget sizes. Native Grid
        // owns its chrome and the content surface resolves within the space above it.
        ScaleRoot.InterfaceScale = appearance.InterfaceScale;
        var placement = new Placement { X = work.X, Y = work.Y, Width = work.Width, Height = work.Height };
        placementDisplay = display.DisplayId.Value;
        if (AppWindow.Position.X == placement.X && AppWindow.Position.Y == placement.Y &&
            AppWindow.Size.Width == placement.Width && AppWindow.Size.Height == placement.Height)
        {
            // The remembered foreground HWND chooses only the monitor. Its DPI
            // awareness must not decide our layout scale. Wait for our own XAML
            // viewport, including its native DPI transition on monitor changes.
            if (ScaleRoot.ActualWidth > 0 && ScaleRoot.ActualHeight > 0)
                page.ConfigureProductionViewport(new(ScaleRoot.ActualWidth / appearance.InterfaceScale,
                    ScaleRoot.ActualHeight / appearance.InterfaceScale));
            return;
        }
        applyingPlacement = true;
        try { AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, placement.Width, placement.Height)); }
        finally { applyingPlacement = false; }
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint window);
}
