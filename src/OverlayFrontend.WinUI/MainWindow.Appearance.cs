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
    private readonly Windows.UI.ViewManagement.UISettings shellUi = new();
    private readonly SolidColorBrush shellForeground = new();

    private void InitializeShellAppearance(OverlayShellPage page)
    {
        // The host-owned fill belongs to the widget surface. An outer opaque card
        // would defeat a transparent declaration even with a transparent inner panel.
        ShellCard.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ShellCard.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
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
        Closed += (_, _) => { desktopBackdrop.Dispose(); desktopBackdrop = null; };
    }

    private void RefreshShellAppearance()
    {
        if (cleanupStarted || desktopBackdrop is null || RootFrame.Content is not OverlayShellPage page) return;
        var policy = OverlayAppearancePolicy.Resolve(page.Appearance, page.SurfaceAppearanceWidgetId,
            page.SurfaceHints?.Appearance ?? WidgetSurfaceAppearance.Theme, themeSettings.HighContrast, backdropAvailable);
        var shellColor = policy.HighContrast
            ? shellUi.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : Microsoft.UI.Colors.Transparent;
        if (ShellCard.Background is SolidColorBrush shellBrush) shellBrush.Color = shellColor;
        shellForeground.Color = shellUi.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
        if (policy.HighContrast) page.Foreground = shellForeground;
        else page.ClearValue(Control.ForegroundProperty);
        foreach (var child in ShellHeader.Children)
        {
            if (child is TextBlock title)
            {
                if (policy.HighContrast) title.Foreground = shellForeground;
                else title.ClearValue(TextBlock.ForegroundProperty);
            }
            else if (child is Control control)
            {
                if (policy.HighContrast) control.Foreground = shellForeground;
                else control.ClearValue(Control.ForegroundProperty);
            }
        }
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
