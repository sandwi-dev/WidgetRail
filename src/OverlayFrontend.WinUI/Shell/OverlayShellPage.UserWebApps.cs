using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private UserWebAppEditor? webAppEditor;
    private bool webAppEditorPending;
    private UserWebAppStore WebApps => new(new PlatformSettingsPaths(options.SettingsRoot));
    private UserWebAppIconCache? webAppIcons;
    private void ApplyUserWebAppIcon(WidgetPackageIconView view, WidgetRail.WidgetBridge.BridgeWidgetDescriptor descriptor)
    {
        if (!descriptor.IsUserWebApp) return;
        var cache = webAppIcons ??= new(WebApps);
        _ = view.SetUserIconAsync($"{descriptor.Id}|{descriptor.PresentationGeneration}|{cache.Version(descriptor.Id)}", cache.GetAsync(descriptor.Id));
    }
    private async Task SaveUserWebAppIconAsync(string id, string url, byte[] bytes)
    {
        await UserWebAppIconCache.ValidateAsync(bytes);
        if (!await WebApps.SaveIconAsync(id, url, bytes, lifetime.Token)) return;
        (webAppIcons ??= new(WebApps)).Invalidate(id);
        foreach (var tile in statusTiles)
            if (tile.Descriptor?.Id == id) tile.SetItem(tile.Descriptor);
        RefreshRadialChooser();
    }

    private async Task AddBrowserWebAppAsync(string url, string title)
    {
        if (PinnedInputActive) await ReturnIntentToMainAsync(lifetime.Token);
        await EditUserWebAppAsync(url: url, title: title);
    }

    private async Task ManageUserWebAppsAsync()
    {
        var apps = await WebApps.ReadAsync(lifetime.Token);
        var id = await ShowHostChoiceAsync("Your webapps", apps.Count == 0 ? "Add a webapp to keep a website in your rail." : "Choose a webapp to rename or remove.",
            apps.Select(app => new HostDialogChoice(app.Id, app.Name, Value: (app.Enabled ? "" : "Disabled · ") + new Uri(app.Url).Host)).ToArray(), lifetime.Token, configure: dialog => dialog.ConfigurePreferenceList(closeLabel:"Back"));
        if (id is not null) await EditUserWebAppAsync(id);
    }

    private async Task EditUserWebAppAsync(string? id = null, string? url = null, string? title = null)
    {
        if (webAppEditorPending || webAppEditor is not null || HostChoiceActive || LocalInstallActive || SystemFilePickerOpen || retired || !visible || !foreground || owner is null) return;
        var store = WebApps;
        var version = selectionVersion;
        UserWebApp? existing;
        webAppEditorPending = true;
        try { existing = id is null ? null : (await store.ReadAsync(lifetime.Token)).SingleOrDefault(app => app.Id == id); }
        finally { webAppEditorPending = false; }
        if (retired || !visible || !foreground || selectionVersion != version) return;
        if (id is not null && existing is null) return;
        var originalSurface = surface;
        var originalFocus = FocusManager.GetFocusedElement(XamlRoot) as Control;
        originalSurface?.SetAutomaticFocusEnabled(false);
        originalSurface?.SetPresentationInputEnabled(false);
        shellOwnedReleases.Add(ControllerButton.A);
        ResetInputPresentation();
        UserWebApp? saved = null;
        try
        {
            using var editor = new UserWebAppEditor(store, originalSurface ?? (FrameworkElement)this, existing, url, title);
            webAppEditor = editor;
            editor.Changed += PublishControllerGuide;
#if ENABLE_WIDGET_VALIDATION
            AttachValidationInputReplay(editor.InputRoot);
            editor.Changed += () => { if (editor.Keyboard is { } keyboard) AttachValidationInputReplay(keyboard); };
#endif
            PublishControllerGuide();
            saved = await editor.ShowAsync();
            if (existing is not null && saved is not null && saved.Url != existing.Url)
            {
                webAppIcons?.Invalidate(existing.Id);
                foreach (var tile in statusTiles) if (tile.Descriptor?.Id == existing.Id) tile.SetItem(tile.Descriptor);
                RefreshRadialChooser();
            }
        }
        finally
        {
            webAppEditor = null; ResetInputPresentation(); PublishControllerGuide();
            if (!retired && visible && foreground && ReferenceEquals(surface, originalSurface))
            {
                originalSurface?.SetPresentationInputEnabled(!switching && activeWidget == requestedWidget);
                originalSurface?.SetAutomaticFocusEnabled(MainFocusEnabled);
                if (originalFocus is { IsLoaded: true, IsEnabled: true }) originalFocus.Focus(FocusState.Keyboard);
                else QueueEntryFocus();
            }
        }
        if (saved is not null && existing is null && !retired && visible)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                while (!deadline.IsCancellationRequested)
                {
                    var catalog = await owner.Session.ListWidgetsAsync(deadline.Token);
                    if (catalog.Widgets.Any(app => app.Id == saved.Id)) { SetCatalog(catalog); await SelectAsync(saved.Id, true); return; }
                    await Task.Delay(100, deadline.Token);
                }
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            { await ShowHostChoiceAsync("Webapp saved", "Your webapp will appear in the rail when the catalog finishes refreshing.", [], lifetime.Token); }
        }
    }

    private bool ReceiveWebAppEditor(ControllerFrame frame)
    {
        if (webAppEditor is not { } editor) return false;
        heldAction.Reset(); rightStick.Reset();
        foreach (var (mask, button) in Buttons)
            if ((frame.ReleasedButtons & mask) != 0) shellOwnedReleases.Remove(button);
        if (!visible || !foreground || frame.Connected == 0 || frame.Primed != 0) { editor.ControllerArmed = false; return true; }
        if (frame.State.Buttons == 0 && frame.State.LeftTrigger == 0 && frame.State.RightTrigger == 0) editor.ControllerArmed = true;
        if (!editor.ControllerArmed) return true;
        editor.SampleScroll(frame.State.RightThumbX,frame.State.RightThumbY,Environment.TickCount64);
        var direction = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        var next = direction.Direction switch
        {
            NavigationDirection.Left => FocusNavigationDirection.Left, NavigationDirection.Right => FocusNavigationDirection.Right,
            NavigationDirection.Up => FocusNavigationDirection.Up, NavigationDirection.Down => FocusNavigationDirection.Down,
            _ => FocusNavigationDirection.None,
        };
        if (direction.Phase != NavigationPhase.None && next != FocusNavigationDirection.None) editor.MoveFocus(next);
        foreach (var (mask, button) in Buttons)
        {
            if ((frame.PressedButtons & mask) != 0) { shellOwnedReleases.Add(button); editor.Handle(button, ControllerEventPhase.Pressed); }
            if ((frame.ReleasedButtons & mask) != 0) editor.Handle(button, ControllerEventPhase.Released);
        }
        if (frame.LeftTriggerPressed != 0) editor.Handle(ControllerButton.LeftTrigger, ControllerEventPhase.Pressed);
        if (frame.RightTriggerPressed != 0) editor.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Pressed);
        if (frame.LeftTriggerReleased != 0) editor.Handle(ControllerButton.LeftTrigger, ControllerEventPhase.Released);
        if (frame.RightTriggerReleased != 0) editor.Handle(ControllerButton.RightTrigger, ControllerEventPhase.Released);
        editor.Keyboard?.SampleHeldButtons(frame.State.Buttons, frame.PressedButtons, Environment.TickCount64);
        return true;
    }
}
