namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private Task<string?> ShowBrowserChoiceAsync(Browser.BrowserSurface browser, string title, string message,
        IReadOnlyList<HostDialogChoice> choices, CancellationToken token)
    {
        if (!browser.CanRequestSiteDecision || retired || !visible) return Task.FromResult<string?>(null);
        var pin = pinned is { } current && ReferenceEquals(browser.XamlRoot, current.Window.AutomationRoot.XamlRoot) ? current : null;
        if (pin is null && !ReferenceEquals(browser.XamlRoot, XamlRoot)) return Task.FromResult<string?>(null);
        return ShowHostChoiceAsync(title, message, choices, token, pinnedOwner: pin);
    }
}
