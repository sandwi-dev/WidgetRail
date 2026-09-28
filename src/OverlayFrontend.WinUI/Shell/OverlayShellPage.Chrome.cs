using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly ShellControllerGuide trayGuide = new();
    private ShellChromeStyles? chromeStyles;

    private void InitializeTrayGuide()
    {
        TrayHelp.Content = trayGuide;
        trayGuide.Changed += () => Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(Tray, trayGuide.HelpText);
    }

    internal void InitializeShellChrome()
    {
        chromeStyles = new();
        chromeStyles.Register(this, "body");
        chromeStyles.Register(StatusChrome, "panel");
        chromeStyles.Register(Status, "status");
        chromeStyles.Register(Retry, "body");
        chromeStyles.Attach(Tray, paintBackground: false);
        chromeStyles.Register(TrayPrevious, "tray-item");
        chromeStyles.Register(TrayNext, "tray-item");
        foreach (var element in trayGuide.Typography) chromeStyles.Register(element, element is FontIcon ? "controller-glyph" : "hint");
        RefreshShellChrome();
    }

    internal void RefreshShellChrome()
    {
        chromeStyles?.Update(ShellPalette, Appearance, systemUi.AnimationsEnabled);
        RefreshSystemStatusAppearance();
    }

    internal void DisposeShellChrome() { chromeStyles?.Dispose(); chromeStyles = null; trayGuide.Dispose(); }
}
