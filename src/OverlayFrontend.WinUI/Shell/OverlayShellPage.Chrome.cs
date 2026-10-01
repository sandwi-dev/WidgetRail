using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly ShellControllerGuide trayGuide = new();
    private ShellChromeStyles? chromeStyles;
    private readonly NativePopupTheme popupTheme = new();

    private void InitializeTrayGuide()
    {
        TrayHelp.Content = trayGuide;
        trayGuide.Changed += () => Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(Tray, trayGuide.HelpText);
    }

    internal void InitializeShellChrome()
    {
        chromeStyles = new();
        popupTheme.Attach(this);
        chromeStyles.Register(this, "body");
        chromeStyles.Register(StatusChrome, "panel");
        chromeStyles.Register(Status, "status");
        chromeStyles.Register(Retry, "body");
        fullscreenView.RegisterChrome(chromeStyles);
        // Horizontal icon rails use the established bottom selection mark in
        // their content; retain native item controls without a second left bar.
        Tray.Resources["ListViewItemSelectionIndicatorVisualEnabled"] = false;
        chromeStyles.Attach(Tray, paintBackground: false);
        chromeStyles.Register(TrayPrevious, "tray-item");
        chromeStyles.Register(TrayNext, "tray-item");
        foreach (var element in trayGuide.Typography) chromeStyles.Register(element, element is FontIcon ? "controller-glyph" : "hint");
        RefreshShellChrome();
    }

    internal void RefreshShellChrome()
    {
        chromeStyles?.Update(ShellPalette, Appearance, systemUi.AnimationsEnabled);
        popupTheme.Update(ShellPalette, Appearance);
        trayGuide.ApplyAppearance(ShellPalette, Appearance, systemUi.AnimationsEnabled);
        RefreshSystemStatusAppearance();
        openingIndicator?.ApplyAppearance(ShellPalette, Appearance, systemUi.AnimationsEnabled);
        RefreshStartupPresentation();
        RefreshRadialChooser();
    }

    internal void DisposeShellChrome() { WaitForGuideLayout(null); chromeStyles?.Dispose(); chromeStyles = null; trayGuide.Dispose(); radialView?.Dispose(); }
}
