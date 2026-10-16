using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private bool extensionCommandPending;
    private BrowserSurface[] ExtensionTargets(string widgetId) => browserOwner?.PermissionTargets(widgetId)
        .Where(page => page.ExtensionsEligible).OrderBy(page => ReferenceEquals(page.XamlRoot, XamlRoot) ? 0 : 1)
        .DistinctBy(page => page.ProfileName).ToArray() ?? [];

    private async Task ManageWidgetExtensionsAsync(string widgetId)
    {
        var selection = selectionVersion;
        var pages = ExtensionTargets(widgetId);
        if (pages.Length == 0) return;
        var index = 0;
        if (pages.Length > 1)
        {
            var choice = await ShowHostPreferenceAsync("Extensions", "Choose a browser profile. Extensions apply across its websites.",
                pages.Select((page, i) => new HostDialogChoice(i.ToString(System.Globalization.CultureInfo.InvariantCulture), page.ExtensionProfile.Label)).ToArray(), lifetime.Token);
            if (!int.TryParse(choice, out index) || index < 0 || index >= pages.Length) return;
        }
        if (selection != selectionVersion || activeWidget != widgetId || !ExtensionTargets(widgetId).Contains(pages[index])) return;
        await ManageBrowserExtensionsAsync(pages[index], fromTray: true);
    }

    private async Task ManageBrowserExtensionsAsync(BrowserSurface? source = null, bool fromTray = false)
    {
        if (browserOwner is null || source is null || !source.ExtensionsEligible || retired || !visible) return;
        var manager = browserOwner.Extensions;
        var pin = !fromTray && pinned is { } current && ReferenceEquals(source.XamlRoot, current.Window.AutomationRoot.XamlRoot) ? current : null;
        if (source.ExtensionProfile.Private)
        {
            await ShowHostChoiceAsync("Extensions unavailable", "Extensions require a persistent browser profile. This page uses private storage.", [], lifetime.Token, pinnedOwner: pin);
            return;
        }
        try
        {
            await manager.InitializeAsync(lifetime.Token);
            while (!retired && visible && source.CanCompleteSiteDecision)
            {
                var catalog = await manager.Store.ReadAsync(lifetime.Token);
                var choices = catalog.Library.OrderBy(e => e.Manifest.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(e => new HostDialogChoice(e.Slot, e.Manifest.Name + " · " + (e.EnabledFor(source.ProfileName) ? "On" : "Off"))).ToList();
                choices.Add(new("zip", "Install from ZIP…")); choices.Add(new("folder", "Install from folder…"));
                var selected = await ShowHostPreferenceAsync("Extensions", "Choose an extension for this profile. Shared defaults are available in Settings → Extensions.", choices, lifetime.Token, pinnedOwner: pin);
                if (selected is null) return;
                if (selected is "zip" or "folder")
                {
                    await ImportExtensionAsync(selected == "folder", null, pin);
                    continue;
                }
                while (!retired && visible && source.CanCompleteSiteDecision)
                {
                    var entry = (await manager.Store.ReadAsync(lifetime.Token)).Library.FirstOrDefault(e => e.Slot == selected);
                    if (entry is null) break;
                    var choice = entry.Overrides.GetValueOrDefault(source.ProfileName);
                    var local = new List<HostDialogChoice> {
                        new("policy", "This profile · " + (choice == ExtensionOverride.Default ? "Default (" + (entry.EnabledByDefault ? "On" : "Off") + ")" : choice.ToString())) };
                    if (entry.EnabledFor(source.ProfileName) && entry.Manifest.OptionsPage is not null) local.Add(new("options", "Extension options"));
                    local.Add(new("zip", "Update everywhere from ZIP…")); local.Add(new("folder", "Update everywhere from folder…"));
                    local.Add(new("remove", "Remove everywhere…"));
                    var action = await ShowHostPreferenceAsync(entry.Manifest.Name, "Version " + entry.Manifest.Version, local, lifetime.Token, pinnedOwner: pin);
                    if (action is null) break;
                    if (action == "policy")
                    {
                        var policy = await ShowHostPreferenceAsync("This profile", entry.Manifest.Name,
                            (HostDialogChoice[])[new("default", "Default · " + (entry.EnabledByDefault ? "On" : "Off")), new("on", "On"), new("off", "Off")], lifetime.Token,
                            pinnedOwner: pin, currentChoiceId: choice.ToString().ToLowerInvariant());
                        if (policy is not null) await manager.SetPolicyAsync(entry.Slot, null, source.ProfileName,
                            policy == "on" ? ExtensionOverride.On : policy == "off" ? ExtensionOverride.Off : ExtensionOverride.Default, lifetime.Token);
                    }
                    else if (action == "options")
                    {
                        if (fromTray)
                        {
                            if (pinned is { } pinnedPage && ReferenceEquals(source.XamlRoot, pinnedPage.Window.AutomationRoot.XamlRoot))
                            { await EnterPinnedAsync(); if (!PinnedInputActive) return; }
                            else
                            {
                                // Dialog teardown restored tray ownership. The
                                // normal selection transition publishes widget
                                // input only after its lifecycle is ready; setting
                                // the flag alone leaves IsInteractionCurrent false.
                                await SelectAsync(source.WidgetId, enterWidget: true);
                                if (retired || !visible || !foreground || activeWidget != source.WidgetId ||
                                    !ExtensionTargets(source.WidgetId).Contains(source)) return;
                            }
                        }
                        await source.OpenExtensionOptionsAsync(entry.Installation); return;
                    }
                    else if (action is "zip" or "folder") await ImportExtensionAsync(action == "folder", entry.Slot, pin);
                    else if (action == "remove" && await ShowHostChoiceAsync("Remove extension everywhere?", entry.Manifest.Name + "\nThis removes it and its saved settings from every profile.",
                        (HostDialogChoice[])[new("remove", "Remove everywhere")], lifetime.Token, directDecisionButtons:true, pinnedOwner: pin) == "remove")
                    { await manager.RemoveAsync(entry.Slot, lifetime.Token); break; }
                }
            }
        }
        catch (OperationCanceledException) when (retired || lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("browser-extension", error);
            if (!retired && visible) await ShowHostChoiceAsync("Extension change could not finish",
                error is InvalidDataException or InvalidOperationException ? error.Message : "Check the extension package and try again.", [], lifetime.Token, pinnedOwner: pin);
        }
    }
    private async Task ImportExtensionAsync(bool folder, string? replace, PinnedSurface? pin)
    {
        var path = await PickExtensionAsync(folder, pin, lifetime.Token);
        if (path is null || !visible) return;
        var manager = browserOwner!.Extensions;
        await using var import = await manager.PrepareAsync(path, lifetime.Token);
        var manifest = import.Manifest;
        var sites = manifest.Sites.Any(s => s is "<all_urls>" or "*://*/*" or "http://*/*" or "https://*/*")
            ? "all websites in enabled profiles" : manifest.Sites.Length == 0 ? "none requested" : string.Join(", ", manifest.Sites);
        var description = manifest.Name + " · " + manifest.Version + "\n\nWebsite access: " + sites +
            "\nRequested capabilities: " + (manifest.Permissions.Length == 0 ? "none" : string.Join(", ", manifest.Permissions)) +
            (replace is not null ? "\n\nUpdates every profile using this extension. Defaults and overrides are kept." :
            "\n\nStarts disabled in every profile unless you enable the default below. You can enable individual profiles after installation.");
        HostChoiceDialog? review = null;
        if (await ShowHostChoiceAsync(replace is null ? "Install extension?" : "Update extension?", description,
            (HostDialogChoice[])[new("install", replace is null ? "Install" : "Update everywhere")], lifetime.Token, directDecisionButtons:true, pinnedOwner: pin,
            configure: dialog => { review = dialog; if (replace is null) dialog.AddRememberChoice("Enabled by default in existing and new eligible profiles"); }) == "install")
            await manager.InstallAsync(import, replace, review?.RememberChoice == true, lifetime.Token);
    }
    private async Task RefreshExtensionSettingsAsync()
    {
        if (retired || owner is null) return;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var frame = owner.Session.GetState("settings")?.LastGood;
            if (frame?.Snapshot.ActiveInputScopeId is not ("extensions.library" or "extensions.details")) return;
            try
            {
                await owner.Session.SendActionAsync(frame, new("extensions.refresh", "extensions.refresh", ControllerButton.A,
                    InputScopeId: frame.Snapshot.ActiveInputScopeId), lifetime.Token); return;
            }
            catch (WidgetRail.WidgetPresentationSession.WidgetPresentationSessionException) when (attempt < 2) { await Task.Delay(40, lifetime.Token); }
        }
    }
    private async Task HandleExtensionCommandAsync(WidgetActionRequest request)
    {
        if (extensionCommandPending || owner is null || browserOwner is null) return;
        owner.Session.ValidateBrowserExtensionManagement(request.Displayed, request.Action);
        extensionCommandPending = true;
        var manager = browserOwner.Extensions;
        try
        {
            var parts = request.Action.ActionId.Split('.');
            await manager.InitializeAsync(lifetime.Token);
            // Settings owns page composition and busy/focus state; native work
            // stays here. Its refresh command reads the atomic shared catalog.
            await owner.Session.SendActionAsync(request.Displayed, request.Action, lifetime.Token);
            if (parts[2] is "install" or "update" && parts.Length == 5)
            {
                var replace = parts[2] == "update" ? parts[4] : null;
                await ImportExtensionAsync(parts[3] == "folder", replace, null);
            }
            else if (parts[2] == "default" && parts.Length == 5)
                await manager.SetPolicyAsync(parts[3], parts[4] == "on", null, ExtensionOverride.Default, lifetime.Token);
            else if (parts[2] == "override" && parts.Length == 6)
                await manager.SetPolicyAsync(parts[3], null, parts[4], parts[5] switch { "on" => ExtensionOverride.On, "off" => ExtensionOverride.Off, _ => ExtensionOverride.Default }, lifetime.Token);
            else if (parts[2] == "remove" && parts.Length == 4)
            {
                var entry = (await manager.Store.ReadAsync(lifetime.Token)).Library.Single(e => e.Slot == parts[3]);
                if (await ShowHostChoiceAsync("Remove extension everywhere?", entry.Manifest.Name + "\nThis removes it and its saved settings from every profile.",
                    (HostDialogChoice[])[new("remove", "Remove everywhere")], lifetime.Token, directDecisionButtons:true) == "remove") await manager.RemoveAsync(entry.Slot, lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (retired || lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("browser-extension", error);
            if (!retired && visible && activeWidget == "settings") await ShowHostChoiceAsync("Extension change could not finish",
                error is InvalidDataException or InvalidOperationException ? error.Message : "Check that the package is a supported MV3 Edge/Chromium extension, then try again.", [], lifetime.Token);
        }
        finally
        {
            try { await RefreshExtensionSettingsAsync(); }
            catch (Exception error) { if (!retired) Diagnostics.FrontendFailureLog.Current.Write("extension-settings-refresh", error); }
            extensionCommandPending = false;
        }
    }
    private async Task<string?> PickExtensionAsync(bool folder, PinnedSurface? pin, CancellationToken token)
    {
        if (SystemFilePickerOpen || HostChoiceActive || LocalInstallActive || retired || !visible) return null;
        var root = pin?.Window.AutomationRoot.XamlRoot ?? XamlRoot;
        var presenter = pin?.Presenter ?? surface;
        var focus = FocusManager.GetFocusedElement(root) as Control;
        SystemFilePickerOpen = true;
        presenter?.SetAutomaticFocusEnabled(false); presenter?.SetPresentationInputEnabled(false); pin?.SetInput(false);
        shellOwnedReleases.Add(WidgetRail.WidgetProtocol.ControllerButton.A);
        ResetInputPresentation(); PublishControllerGuide();
        try
        {
            var window = root.ContentIslandEnvironment.AppWindowId;
            if (folder)
            {
                var picker = new FolderPicker(window) { CommitButtonText = "Select extension", SuggestedStartLocation = PickerLocationId.Downloads };
                return (await NativeFilePickerLifetime.RunAsync(window, () => picker.PickSingleFolderAsync().AsTask(), token))?.Path;
            }
            var filePicker = new FileOpenPicker(window) { CommitButtonText = "Select extension", SuggestedStartLocation = PickerLocationId.Downloads };
            filePicker.FileTypeFilter.Add(".zip");
            return (await NativeFilePickerLifetime.RunAsync(window, () => filePicker.PickSingleFileAsync().AsTask(), token))?.Path;
        }
        finally
        {
            SystemFilePickerOpen = false; ResetInputPresentation(); PublishControllerGuide();
            if (!retired && visible && (pin is null ? foreground && ReferenceEquals(presenter, surface) : ReferenceEquals(pin, pinned) && PinnedInputActive))
            {
                var enabled = pin is null ? MainFocusEnabled : PinnedInputActive;
                presenter?.SetAutomaticFocusEnabled(enabled); presenter?.SetPresentationInputEnabled(enabled); pin?.SetInput(enabled);
                if (enabled && focus is { IsLoaded: true, IsEnabled: true }) focus.Focus(FocusState.Keyboard);
            }
        }
    }
}
