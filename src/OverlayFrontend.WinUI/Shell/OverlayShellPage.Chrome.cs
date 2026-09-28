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

    internal void InitializeShellChrome(Grid header, TextBlock title, Button close)
    {
        chromeStyles = new();
        chromeStyles.Register(this, "body");
        chromeStyles.Register(header, "tray");
        chromeStyles.Register(title, "title");
        chromeStyles.Register(close, "body");
        chromeStyles.Register(StatusChrome, "tray");
        chromeStyles.Register(Status, "status");
        chromeStyles.Register(Retry, "body");
        chromeStyles.Attach(Tray);
        chromeStyles.Register(trayGuide.BackgroundSurface, "tray");
        foreach (var element in trayGuide.Typography) chromeStyles.Register(element, element is FontIcon ? "controller-glyph" : "hint");
        RefreshShellChrome();
    }

    internal void RefreshShellChrome() => chromeStyles?.Update(ShellPalette, Appearance, systemUi.AnimationsEnabled);

    internal void DisposeShellChrome() { chromeStyles?.Dispose(); chromeStyles = null; trayGuide.Dispose(); }
}
