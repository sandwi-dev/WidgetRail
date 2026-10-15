namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal bool OwnsBrowserUiProcess(uint process) => !retired && visible && browserOwner?.OwnsNativeUiProcess(process) == true;
    private async Task ManageBrowserPermissionsAsync(WidgetRail.WidgetBridge.BridgeWidgetDescriptor widget)
    {
        if (retired || !visible || !foreground || browserOwner is not { } browsers) return;
        var pages = browsers.PermissionTargets(widget.Id);
        if (pages.Length == 0) return;
        var index = 0;
        if (pages.Length > 1)
        {
            var choices = pages.Select((page, i) => new HostDialogChoice(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                page.PermissionTargetLabel)).ToArray();
            var selected = await ShowHostChoiceAsync("Website settings", "Choose a website and profile.", choices, lifetime.Token);
            if (!int.TryParse(selected, out index) || index < 0 || index >= pages.Length) return;
        }
        if (!browsers.PermissionTargets(widget.Id).Contains(pages[index])) return;
        await pages[index].ShowSettingsFromHostAsync((_, title, message, choices, token, focusFirstChoice) =>
            ShowHostChoiceAsync(title, message, choices, token, configure: dialog => dialog.FocusFirstOptionOnOpen = focusFirstChoice));
    }
    private void RefreshWebappStatus()
    {
        if (retired) return;
        RefreshNotificationBell();
    }

    private async Task StopBrowserWidgetAsync(WidgetRail.WidgetBridge.BridgeWidgetDescriptor widget)
    {
        if (browserOwner?.HasRunningSession(widget.Id) != true) return;
        if (await ShowHostChoiceAsync("Stop webapp session?", $"{widget.Name}\n\nThis ends its calls and connections. Saved login data is kept.",
            (HostDialogChoice[])[new("stop", "Stop session")], lifetime.Token) == "stop")
            await browserOwner.StopWidgetSessionsAsync(widget.Id);
    }

    private Task<string?> ShowBrowserChoiceAsync(Browser.BrowserSurface browser, string title, string message,
        IReadOnlyList<HostDialogChoice> choices, CancellationToken token, bool focusFirstChoice = false) =>
        ShowBrowserChoiceCoreAsync(browser, title, message, choices, token, dialog => dialog.FocusFirstOptionOnOpen = focusFirstChoice);

    private Task<string?> ShowBrowserChoiceCoreAsync(Browser.BrowserSurface browser, string title, string message,
        IReadOnlyList<HostDialogChoice> choices, CancellationToken token, Action<HostChoiceDialog>? configure, bool directDecisionButtons = false)
    {
        if (!browser.CanCompleteSiteDecision || retired || !visible) return Task.FromResult<string?>(null);
        var pin = pinned is { } current && ReferenceEquals(browser.XamlRoot, current.Window.AutomationRoot.XamlRoot) ? current : null;
        if (pin is null && !ReferenceEquals(browser.XamlRoot, XamlRoot)) return Task.FromResult<string?>(null);
        return ShowHostChoiceAsync(title, message, choices, token, pinnedOwner: pin, configure: configure, directDecisionButtons: directDecisionButtons);
    }
}
