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

    private void InitializeShellAppearance(OverlayShellPage page)
    {
        // Only the widget surface owns a panel fill; production has no outer card.
        page.InitializeShellChrome();
        desktopBackdrop = new(); desktopBackdrop.DismissRequested += HideOverlay;
        page.SurfaceAppearanceChanged += RefreshShellAppearance;
        page.AppearanceLoaded += settings => { RefreshShellAppearance(); _ = page.RefreshShellPaletteAsync(); };
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
        OverlaySurfacePaint.Apply(page.SurfaceBackground, policy, page.ShellPalette);
        var color = OverlaySurfacePaint.Background(page.ShellPalette, "backdrop", Microsoft.UI.Colors.Black);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).OuterBounds;
        try { desktopBackdrop.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), area, color,
            page.Appearance.BackdropOpacity, AppWindow.IsVisible); }
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
