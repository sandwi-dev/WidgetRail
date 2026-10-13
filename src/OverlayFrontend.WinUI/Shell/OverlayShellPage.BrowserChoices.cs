namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private void RefreshWebappStatus()
    {
        if (retired) return;
        var count = browserOwner?.BackgroundSessionCount ?? 0;
        var failed = browserOwner?.FailedBackgroundSessionCount ?? 0;
        WebappStatus.Text = failed > 0 ? $"Webapps: {count} running · {failed} disconnected" : $"Webapps running: {count}";
        WebappStatus.Visibility = count + failed > 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(WebappStatus, browserOwner?.BackgroundSessionNames ?? "");
    }

    private async Task StopBrowserWidgetAsync(WidgetRail.WidgetBridge.BridgeWidgetDescriptor widget)
    {
        if (browserOwner?.HasRunningSession(widget.Id) != true) return;
        if (await ShowHostChoiceAsync("Stop webapp session?", $"{widget.Name}\n\nThis ends its calls and connections. Saved login data is kept.",
            (HostDialogChoice[])[new("stop", "Stop session")], lifetime.Token) == "stop")
            await browserOwner.StopWidgetSessionsAsync(widget.Id);
    }

    private Task<string?> ShowBrowserChoiceAsync(Browser.BrowserSurface browser, string title, string message,
        IReadOnlyList<HostDialogChoice> choices, CancellationToken token)
    {
        if (!browser.CanRequestSiteDecision || retired || !visible) return Task.FromResult<string?>(null);
        var pin = pinned is { } current && ReferenceEquals(browser.XamlRoot, current.Window.AutomationRoot.XamlRoot) ? current : null;
        if (pin is null && !ReferenceEquals(browser.XamlRoot, XamlRoot)) return Task.FromResult<string?>(null);
        return ShowHostChoiceAsync(title, message, choices, token, pinnedOwner: pin);
    }
}
