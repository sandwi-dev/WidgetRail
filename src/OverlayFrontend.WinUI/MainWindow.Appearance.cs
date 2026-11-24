using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI;

public sealed partial class MainWindow
{
    private OverlayDesktopBackdrop? desktopBackdrop;
    private bool backdropAvailable = true;
#if ENABLE_WIDGET_VALIDATION
    internal nint BackdropHandleForValidation => desktopBackdrop?.Handle ?? 0;
#endif

    private void InitializeShellAppearance(OverlayShellPage page)
    {
        // Only the widget surface owns a panel fill; production has no outer card.
        page.InitializeShellChrome();
        desktopBackdrop = new(); desktopBackdrop.DismissRequested += HideOverlay;
        page.SurfaceAppearanceChanged += RefreshShellAppearance;
        page.AppearanceLoaded += settings => { RefreshShellAppearance(); RefreshOverlayMotionPolicy(); _ = page.RefreshShellPaletteAsync(); };
        page.BridgeReady += () => _ = page.RefreshShellPaletteAsync();
        page.Loaded += (_, _) => RefreshShellAppearance();
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidVisibilityChange || args.DidPositionChange || args.DidSizeChange) RefreshShellAppearance();
        };
        Activated += (_, args) => { if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshShellAppearance(); };
        Closed += (_, _) => { desktopBackdrop.Dispose(); desktopBackdrop = null; page.DisposeShellChrome(); };
    }

    private void RefreshShellAppearance()
    {
        if (cleanupStarted || desktopBackdrop is null || RootFrame.Content is not OverlayShellPage page) return;
        var policy = OverlayAppearancePolicy.Resolve(page.Appearance, page.SurfaceAppearanceWidgetId,
            page.SurfaceHints?.Appearance ?? WidgetSurfaceAppearance.Theme, themeSettings.HighContrast, backdropAvailable);
        page.RefreshShellChrome();
        var allow = policy.Effective == WidgetSurfaceAppearance.Transparent;
        if (allow != ForegroundBackgroundAllowed)
        {
            ForegroundBackgroundAllowed = allow;
            // Apply the committed surface material before its first visible frame.
            // An incoming transparent surface must not fade from the outgoing widget's paint.
            ForegroundBackgroundChanged?.Invoke(false);
        }
        if (backgroundHook != 0 && foregroundReturnTarget is { } target && !BackgroundWindowVisible(target))
            EndForegroundBackground();
        OverlaySurfacePaint.Apply(page.SurfaceBackground, policy, page.ShellPalette);
        var color = OverlaySurfacePaint.Background(page.ShellPalette, "backdrop", Microsoft.UI.Colors.Black);
        var display = OverlayDisplayArea.Resolve(AppWindow.Id, AppWindow.Id);
        if (display is null) return;
        var area = display.OuterBounds;
        try
        {
            // The whole visible overlay stack belongs above ordinary apps.
            // Placing a topmost scrim below a non-topmost main HWND demotes it
            // again, so establish the upper anchor's band before ordering peers.
            if(AppWindow.IsVisible)OverlayWindowFrame.KeepTopmost(WinRT.Interop.WindowNative.GetWindowHandle(this));
            desktopBackdrop.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), area, color,
                page.Appearance.BackdropOpacity, AppWindow.IsVisible);
            page.ReconcilePeerWindowOrder(desktopBackdrop.IsVisible?desktopBackdrop.Handle:0);
        }
        catch (Exception error)
        {
            backdropAvailable = false;
            desktopBackdrop.Dispose();
            OverlaySurfacePaint.Apply(page.SurfaceBackground, OverlayAppearancePolicy.Resolve(page.Appearance,
                page.SurfaceAppearanceWidgetId, page.SurfaceHints?.Appearance ?? WidgetSurfaceAppearance.Theme,
                themeSettings.HighContrast, transparentComposition: false), page.ShellPalette);
            page.ReportFailure(error);
        }
    }
}
