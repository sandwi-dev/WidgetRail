using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyExtensionManagementAsync(BrowserSurface page, Action<bool, string> check)
    {
        var package = FrontendArguments.Value(Environment.GetCommandLineArgs(), "--extension-folder")!;
        var manager = browserOwner!.Extensions;
        await SelectAsync("webapp-b", enterWidget: true);
        await WebAppUntil(() => browserOwner.PermissionTargets("webapp-b").Length != 0);
        await SelectAsync(page.WidgetId, enterWidget: true);
        await WebAppUntil(() => page.CanCompleteSiteDecision);
        await using (var import = await manager.PrepareAsync(package, lifetime.Token))
            await manager.InstallAsync(import, null, false, lifetime.Token);
        var install = (await manager.Store.ReadAsync(lifetime.Token)).Library.Single();
        check(!install.EnabledByDefault && install.Overrides.Count == 0 && !install.EnabledFor(page.ProfileName), "New import is disabled everywhere without automatic profile overrides");
        await manager.SetPolicyAsync(install.Slot, null, page.ProfileName, ExtensionOverride.On, lifetime.Token);
        await ShowTrayMenuAsync(catalogItems.Single(widget => widget.Id == page.WidgetId));
        await WebAppUntil(() => trayMenu is not null);
        var preferences = trayMenu!.Flyout.Items.OfType<MenuFlyoutSubItem>().Single(item => item.Text == "Preferences");
        check(preferences.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Extensions") &&
            preferences.Items.OfType<MenuFlyoutItem>().Any(item => item.Text == "Website settings"),
            "Extensions and Website settings are sibling entries under Preferences");
        CloseTrayMenu(restoreFocus: false);
        await WebAppUntil(() => page.CanCompleteSiteDecision);
        var managing = ManageWidgetExtensionsAsync(page.WidgetId);
        var list = await Dialog("Extensions");
        await Focused(list, install.Slot);
        check(true, "Extension list focuses its first extension");
        await CapturePopupDecisionAsync(list, "extensions-list");
        await Select(list, install.Slot);
        var details = await Dialog(install.Manifest.Name);
        await Focused(details, "policy");
        await CapturePopupDecisionAsync(details, "extensions-details");
        await Select(details, "policy");
        await Select(await Dialog("This profile"), "off");
        details = await Dialog(install.Manifest.Name);
        check(!(await page.NativeProfile!.GetBrowserExtensionsAsync()).Single(e => e.Id == install.Id).IsEnabled, "Controller action disables native extension");
        await Select(details, "policy");
        await Select(await Dialog("This profile"), "on");
        details = await Dialog(install.Manifest.Name);
        check((await page.NativeProfile!.GetBrowserExtensionsAsync()).Single(e => e.Id == install.Id).IsEnabled, "Controller action enables native extension");
        await Select(details, "remove");
        var confirmation = await Dialog("Remove extension everywhere?");
        await WebAppUntil(() => FindControl<Button>(confirmation, "Host.Choice.Close")?.FocusState == FocusState.Keyboard);
        check(true, "Removing an extension requires explicit confirmation with safe Cancel focus");
        await Press(ControllerButton.B);
        await Dialog(install.Manifest.Name);
        await Press(ControllerButton.B); await Dialog("Extensions");
        await Press(ControllerButton.B); await managing.WaitAsync(TimeSpan.FromSeconds(5));
        check((await page.NativeProfile!.GetBrowserExtensionsAsync()).Any(e => e.Id == install.Id), "Cancel leaves extension installed");
        check(activeWidget == page.WidgetId, "Tray extension management stays in the current widget");
        await VerifyWebsiteSettingsAsync(page, check);
        // Exercise the real tray admission boundary: dialog teardown restores
        // tray ownership, so widget input is initially disabled.
        SetInteractive(false);
        await WebAppUntil(() => !interactive && surface?.CurrentBinding is not null);
        managing = ManageWidgetExtensionsAsync(page.WidgetId);
        await Select(await Dialog("Extensions"), install.Slot);
        await Select(await Dialog(install.Manifest.Name), "options");
        await managing;
        await WebAppUntil(() => page.PinnablePopup is { IsReady: true, IsNavigationInProgress: false, CurrentUrl: { } url } && url.StartsWith("chrome-extension://", StringComparison.Ordinal));
        check(true, "Tray Extension options opens for the current initialized profile");
        var options = page.PinnablePopup!;
        check(await options.EvaluateFixtureAsync("typeof chrome.runtime?.id==='string' && document.body.innerText.includes('Extension options')") == "true",
            "Tray options loads the actual extension document with its runtime identity");
        await PinBrowserPopupAsync(page.WidgetId, expectedChild: options);
        await EnterPinnedAsync();
        await WebAppUntil(() => PinnedInputActive && options.CanCompleteSiteDecision);
        await options.OpenExtensionOptionsAsync(install.Installation);
        await Select(await Dialog("Replace existing page?"), "widget");
        await WebAppUntil(() => pinned is null && page.PinnablePopup is { IsReady: true, IsNavigationInProgress: false } next && !ReferenceEquals(next, options) && next.CanCompleteSiteDecision);
        check(!options.IsReady, "Replacing pinned extension options uses shared pin handoff and retires the old page");
        await page.ClosePopupAsync();
        check(page.IsReady && page.CurrentUrl?.StartsWith("https://shell.fixture.invalid", StringComparison.Ordinal) == true,
            "Closing replacement options preserves the parent website");

        await SelectAsync("settings", enterWidget: true);
        await WebAppUntil(() => surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId == "settings-root");
        await SettingsAction("open.extensions");
        await WebAppUntil(() => surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId == "extensions.library");
        check(!HostChoiceActive, "Advanced Extensions opens as a real Settings page");
        var result = FrontendArguments.Value(Environment.GetCommandLineArgs(), "--validate-webapp-shell")!;
        await Task.Delay(500); // Capture settled page transitions, not the outgoing snapshot.
        await CaptureSettingsListAsync(surface!, Path.ChangeExtension(result, ".library.png"));
        await SettingsAction("extensions.open." + install.Slot);
        await WebAppUntil(() => surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId == "extensions.details");
        check(Nodes().All(node => node.ActionId?.StartsWith("host.extensions.options.", StringComparison.Ordinal) != true),
            "Settings has no cross-widget options action; configuration stays in the current-page tray flow");
        await SettingsAction("host.extensions.default." + install.Slot + ".on");
        await WebAppUntil(() => Nodes().Any(n => n.ActionId == "host.extensions.default." + install.Slot + ".off" && n.IsBusy != true));
        var saved = (await manager.Store.ReadAsync(lifetime.Token)).Library.Single();
        check(saved.EnabledByDefault && saved.Overrides[page.ProfileName] == ExtensionOverride.On,
            "Settings default switch persists without erasing the tray override");
        await Task.Delay(500);
        await CaptureSettingsListAsync(surface!, Path.ChangeExtension(result, ".details.png"));
        var wideWidth = surface!.Width;
        surface.Width = 520;
        await Task.Delay(500);
        await CaptureSettingsListAsync(surface, Path.ChangeExtension(result, ".details-narrow.png"));
        var updateZip = FindControl<Control>(surface, "Widget.extensions.update-zip")!;
        updateZip.Focus(FocusState.Keyboard);
        updateZip.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        await Task.Delay(150);
        surface.MoveFocus(FocusNavigationDirection.Down);
        await WebAppUntil(() => FindControl<Control>(surface, "Widget.extensions.remove")?.FocusState == FocusState.Keyboard);
        check(true, "Narrow action grid keeps its wrapped Remove button reachable with Down");
        await Task.Delay(200);
        await CaptureSettingsListAsync(surface, Path.ChangeExtension(result, ".details-narrow-actions.png"));
        surface.Width = wideWidth;
        await Override("default");
        check(!(await manager.Store.ReadAsync(lifetime.Token)).Library.Single().Overrides.ContainsKey(page.ProfileName),
            "Settings Default choice removes the explicit profile override");
        await SettingsAction("host.extensions.default." + install.Slot + ".off");
        await WebAppUntil(() => Nodes().Any(n => n.ActionId == "host.extensions.default." + install.Slot + ".on" && n.IsBusy != true));
        check(!(await page.NativeProfile!.GetBrowserExtensionsAsync()).Single(e => e.Id == install.Id).IsEnabled,
            "Inherited profile follows the default switch through real Settings commands");
        await Override("on");
        await Press(ControllerButton.B);
        await WebAppUntil(() => surface?.CurrentBinding?.Frame.Snapshot.ActiveInputScopeId == "extensions.library");
        check(!HostChoiceActive, "Controller Back returns from extension details to its Settings library");
        await Task.Delay(500);
        var singleton = ShowHostPreferenceAsync("One preference", "", (HostDialogChoice[])[new("one", "Only choice")], lifetime.Token);
        var singleDialog = await Dialog("One preference");
        await Focused(singleDialog, "one");
        check(string.IsNullOrEmpty(singleDialog.PrimaryButtonText), "Single preference stays in the list with initial focus");
        await Press(ControllerButton.B); await singleton;
        await SelectAsync(page.WidgetId, enterWidget: true);
        await WebAppUntil(() => page.IsBrowsing);
        await PinAsync(page.WidgetId, WidgetRail.WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
        await EnterPinnedAsync();
        await WebAppUntil(() => PinnedInputActive && page.CanCompleteSiteDecision && ReferenceEquals(page.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot));
        managing = ManageBrowserExtensionsAsync(page);
        list = await Dialog("Extensions"); await Focused(list, install.Slot);
        check(ReferenceEquals(list.XamlRoot, pinned!.Window.AutomationRoot.XamlRoot), "Pinned browser keeps its extension dialog in the pin");
        await Press(ControllerButton.B); await managing;
        await UnpinAsync(save: false);

        IEnumerable<ViewNode> Nodes() => surface?.CurrentBinding is { } binding ? Walk(binding.Frame.Snapshot.Root) : [];
        static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
        async Task SettingsAction(string action)
        {
            var node = Nodes().Single(n => n.ActionId == action);
            await InvokeAsync(new(surface!.CurrentBinding!.Frame, new(action, node.Id, ControllerButton.A,
                InputScopeId: surface.CurrentBinding.Frame.Snapshot.ActiveInputScopeId)));
        }
        async Task Override(string value)
        {
            await InvokeAsync(new(surface!.CurrentBinding!.Frame, new("host.extensions.override." + install.Slot + "." + page.ProfileName + "." + value,
                "extensions.override." + page.ProfileName, ControllerButton.A, InputScopeId: "extensions.details")));
            await WebAppUntil(() => Nodes().All(n => n.Id != "extensions.progress"));
        }

        async Task Press(ControllerButton button)
        {
            var before = hostChoiceDialog;
            Receive(new ControllerFrame { Connected = 1 }); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released);
            if (button == ControllerButton.B && before is not null) await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, before));
        }
        async Task<HostChoiceDialog> Dialog(string title)
        { await WebAppUntil(() => hostChoiceDialog is { IsOpenForValidation: true } d && (string?)d.Title == title); return hostChoiceDialog!; }
        Task Focused(HostChoiceDialog dialog, string id) => WebAppUntil(() => FocusManager.GetFocusedElement(dialog.XamlRoot) is ListViewItem { Tag: string value } && value == id);
        async Task Select(HostChoiceDialog dialog, string id)
        {
            await WebAppUntil(() => FindControl<ListView>(dialog, "Host.Choice.Options") is { IsLoaded: true } ||
                (!string.IsNullOrEmpty(dialog.PrimaryButtonText) && FindControl<Button>(dialog, "Host.Choice.Primary") is { IsLoaded: true }));
            var list = FindControl<ListView>(dialog, "Host.Choice.Options");
            if (list is not null)
            {
                var item = list.Items.OfType<ListViewItem>().Single(i => (string?)i.Tag == id);
                list.ScrollIntoView(item); item.Focus(FocusState.Keyboard);
            }
            else FindControl<Button>(dialog, "Host.Choice.Primary")!.Focus(FocusState.Keyboard);
            await Press(ControllerButton.A); await WebAppUntil(() => !ReferenceEquals(hostChoiceDialog, dialog));
        }
    }
}
