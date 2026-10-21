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
            var selected = await ShowHostPreferenceAsync("Website settings", "Choose a website and profile.", choices, lifetime.Token, closeLabel: "Done");
            if (!int.TryParse(selected, out index) || index < 0 || index >= pages.Length) return;
        }
        if (!browsers.PermissionTargets(widget.Id).Contains(pages[index])) return;
        await pages[index].ShowSettingsFromHostAsync((_, title, message, choices, token, focusFirstChoice, preference) =>
            ShowHostChoiceAsync(title, message, choices, token, configure: dialog => ConfigureBrowserChoice(dialog, focusFirstChoice, preference)));
    }
    private void RefreshWebappStatus()
    {
        if (retired) return;
        RefreshNotificationBell();
        RefreshUtilityNotifications();
        QueueWidgetStatusRefresh();
    }

    private Task<string?> ShowBrowserChoiceAsync(Browser.BrowserSurface browser, string title, string message,
        IReadOnlyList<HostDialogChoice> choices, CancellationToken token, bool focusFirstChoice = false, HostChoicePreference? preference = null) =>
        ShowBrowserChoiceCoreAsync(browser, title, message, choices, token, dialog => ConfigureBrowserChoice(dialog, focusFirstChoice, preference),
            directDecisionButtons: !focusFirstChoice && preference is null && choices.Count==1);

    private static void ConfigureBrowserChoice(HostChoiceDialog dialog, bool focusFirstChoice, HostChoicePreference? preference)
    {
        if (preference is not null) dialog.ConfigurePreferenceList(preference.CurrentChoiceId, preference.CloseLabel);
        else dialog.FocusFirstOptionOnOpen = focusFirstChoice;
    }

    private Task<string?> ShowBrowserChoiceCoreAsync(Browser.BrowserSurface browser, string title, string message,
        IReadOnlyList<HostDialogChoice> choices, CancellationToken token, Action<HostChoiceDialog>? configure, bool directDecisionButtons = false)
    {
        if (!browser.CanCompleteSiteDecision || retired || !visible) return Task.FromResult<string?>(null);
        var pin = pinned is { } current && ReferenceEquals(browser.XamlRoot, current.Window.AutomationRoot.XamlRoot) ? current : null;
        if (pin is null && !ReferenceEquals(browser.XamlRoot, XamlRoot)) return Task.FromResult<string?>(null);
        return ShowHostChoiceAsync(title, message, choices, token, pinnedOwner: pin, configure: configure, directDecisionButtons: directDecisionButtons);
    }
}
