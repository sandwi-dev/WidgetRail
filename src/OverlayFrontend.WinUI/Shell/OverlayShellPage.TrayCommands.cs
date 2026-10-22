using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly TrayHoldGesture trayHold = new();
    private readonly Input.ControllerGestureOwnership shellGestures = new();
    private bool reordering;
    private long quickActionSequence;
    private TrayMenu? trayMenu;
    private object? trayMenuRequest;
    private sealed record TrayMenu(BridgeWidgetDescriptor Owner, long Selection, MenuFlyout Flyout,
        Control Anchor, List<MenuFlyoutItem> Items) { internal NativeMenuNavigator? Navigation; internal IDisposable? Theme; }

    private void InitializeTrayCommands()
    {
        InitializeTrayGuide();
        InitializeSemanticGuide();
        Tray.ContextRequested += (sender, args) =>
        {
            var target = FindTrayWidget(args.OriginalSource as DependencyObject) ?? FocusedTrayWidget();
            if (target is null) return;
            args.Handled = true;
            _ = ShowTrayMenuAsync(target);
        };
        Tray.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.F2)
            { ToggleTrayReorder(); args.Handled = true; }
            else if (reordering && args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right)
            { MoveTrayWidget(args.Key == Windows.System.VirtualKey.Left ? -1 : 1); args.Handled = true; }
            else if (reordering && args.Key is Windows.System.VirtualKey.Escape or Windows.System.VirtualKey.Enter)
            { FinishTrayReorder(); args.Handled = true; }
            else if (!reordering && trayMenu is null && args.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right &&
                NavigateNotificationBell(args.Key == Windows.System.VirtualKey.Left ? FocusNavigationDirection.Left : FocusNavigationDirection.Right))
            { args.Handled = true; }
            else if (!reordering && trayMenu is null && args.Key == Windows.System.VirtualKey.Up && FocusedTrayWidget() is { } widget)
            { args.Handled = true; _ = SelectAsync(widget.Id); }
            else if (args.Key == Windows.System.VirtualKey.Escape && trayMenu is null)
            { _ = BackFromTrayAsync(); args.Handled = true; }
        };
    }

    private BridgeWidgetDescriptor? FindTrayWidget(DependencyObject? element)
    {
        for (; element is not null && !ReferenceEquals(element, Tray); element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem item && Tray.ItemFromContainer(item) is BridgeWidgetDescriptor descriptor) return descriptor;
        return null;
    }

    private object? TrayGestureIdentity => UtilityIconFocused && visible && foreground ? ("utility", shownUtility) : !retired && visible && foreground && !interactive && trayMenu is null &&
        FocusedTrayWidget() is { } widget ? (widget.Id, widget.InstanceId, widget.RuntimeGeneration,
            widget.PresentationGeneration, selectionVersion) : null;

    private void ResetTrayInteraction()
    {
        trayHold.Cancel();
        CloseTrayMenu(restoreFocus: false);
        FinishTrayReorder();
    }

    private void ToggleTrayReorder()
    {
        if (retired || !visible || interactive || FocusedTrayWidget() is null) return;
        reordering = !reordering;
        UpdateTrayHelp();
        if (!reordering) _ = SavePreferencesAsync();
    }
    private void FinishTrayReorder()
    {
        if (!reordering) return;
        reordering = false;
        UpdateTrayHelp();
        _ = SavePreferencesAsync();
    }
    private void UpdateTrayHelp()
    {
        RefreshRadialChooser();
        PublishControllerGuide();
        AutomationProperties.SetHelpText(Tray, trayGuide.HelpText);
        // Keep the native layout slot stable when focus enters/leaves the tray.
        TrayHelp.Visibility = Visibility.Visible;
        AutomationProperties.SetAccessibilityView(TrayHelp, AccessibilityView.Content);
    }

    private void MoveTrayWidget(int delta)
    {
        if (!reordering || FocusedTrayWidget() is not { } widget) return;
        var index = catalogItems.IndexOf(widget);
        var destination = index + delta;
        if (destination < 0 || destination >= catalogItems.Count) return;
        // Preserve the selected identity rather than following the vacated index.
        catalogItems.Move(index, destination);
        var visibleIds = catalogItems.Select(item => item.Id).ToArray();
        preferences = preferences with { Order = visibleIds.Concat(preferences.Order)
            .Distinct(StringComparer.Ordinal).Take(ShellPreferences.MaximumWidgets).ToArray() };
        Tray.SelectedItem = widget;
        RequestTrayFocus(widget);
    }

    private Task RunTrayHoldAsync(TrayHoldAction action)
    {
        if (action == TrayHoldAction.ToggleReorder) ToggleTrayReorder();
        else if (action == TrayHoldAction.OpenUtility)
        {
            FocusUtility(HasPendingNotifications ? ShellUtility.Notifications : ShellUtility.Tasks);
            EnterUtility();
        }
        return Task.CompletedTask;
    }

    private bool TrayOwnerCurrent(BridgeWidgetDescriptor captured, long selection) =>
        !retired && visible && foreground && !interactive && selectionVersion == selection &&
        requestedWidget == captured.Id && catalogItems.FirstOrDefault(item => item.Id == captured.Id) is { } current &&
        SameSurfaceOwner(captured, current) && captured.QuickActions.SequenceEqual(current.QuickActions);

    private async Task RestartWidgetAsync(BridgeWidgetDescriptor widget, long selection)
    {
        if (widget.IsStopped) { await ChangeWidgetStoppedAsync(widget.Id, stopped: false); return; }
        if (owner is null) return;
        var requestOwner = owner;
        var failureCurrent = CaptureTrayOperationGuard(widget, selection);
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (!failureCurrent()) return;
                interactionAdmission.Invalidate();
                await requestOwner.Session.RestartAsync(requestOwner.Session.GetTarget(widget.Id), lifetime.Token);
            }
            finally { transitions.Release(); }
            if (failureCurrent()) await SelectAsync(widget.Id, enterWidget: false);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "catalog_stale" or "presentation_stale" or "unknown_widget") { }
        catch (Exception error) { ReportOperationFailure(error, failureCurrent); }
    }

    private async Task InvokeTrayQuickActionAsync(BridgeWidgetDescriptor widget, BridgeQuickActionDescriptor action, long selection)
    {
        if (owner is null) return;
        var requestOwner = owner;
        var presentationCurrent = CapturePresentationFailureGuard();
        var trayCurrent = CaptureTrayOperationGuard(widget, selection);
        bool Current() => presentationCurrent() && trayCurrent();
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (!Current()) return;
                // Dashboard quick actions use Visible authority. They must not
                // move focus into the widget or synthesize an ordinary action.
                var target = requestOwner.Session.GetTarget(widget.Id);
                await requestOwner.Session.InvokeQuickActionAsync(target, action.Id, ++quickActionSequence,
                    checked(Environment.TickCount64 * 1000), lifetime.Token);
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "catalog_stale" or "presentation_stale" or "unknown_quick_action" or "unknown_widget") { }
        catch (Exception error) { ReportOperationFailure(error, Current); }
    }

    // Restart intentionally retires its own presentation binding. Keep the tray
    // command's selection/session owner without requiring the old widget frame.
    private Func<bool> CaptureTrayOperationGuard(BridgeWidgetDescriptor widget, long selection)
    {
        var requestOwner = owner;
        var visibleSession = visibleSince;
        return () => ReferenceEquals(owner, requestOwner) && visibleSince == visibleSession && TrayOwnerCurrent(widget, selection);
    }

    private async Task ShowTrayMenuAsync(BridgeWidgetDescriptor descriptor)
    {
        var request = new object();
        TrayMenu? openedMenu = null;
        Func<bool> failureCurrent = CapturePresentationFailureGuard();
        try
        {
            if (retired || !visible || owner is null) return;
            CloseTrayMenu(false);
            FinishTrayReorder(); trayHold.Cancel();
            // Selection resets tray interaction synchronously. Claim this opening
            // afterward, before awaiting, so later close/open requests retire it.
            var selectionTask = requestedWidget != descriptor.Id || interactive
                ? SelectAsync(descriptor.Id, enterWidget: false) : Task.CompletedTask;
            trayMenuRequest = request;
            var selection = selectionVersion;
            var trayCurrent = CaptureTrayOperationGuard(descriptor, selection);
            failureCurrent = () => ReferenceEquals(trayMenuRequest, request) && trayCurrent();
            await selectionTask;
            if (!failureCurrent() || (RadialOpen ? radialView?.ContextAnchor(descriptor.Id) : Tray.ContainerFromItem(descriptor)) is not Control anchor) return;
            var menu = new TrayMenu(descriptor, selection, new MenuFlyout { Placement = FlyoutPlacementMode.Top }, anchor, []);
            openedMenu = menu;
            Input.GamepadKeyBoundary.ObserveFlyout(menu.Flyout, anchor);
            trayMenu = menu;
            MenuFlyoutSubItem? pinGroup = null;
            AddPinnedCommands(descriptor, (label, id, action) =>
            {
                var group = id is "Pin.Adjust" or "Pin.Opacity" or "Pin.Interact"
                    ? pinGroup ??= Group("Manage pin", "Pin") : null;
                Add(label, id, action, id == "Pin.Interact" ? ControllerButton.View : null, group: group);
            });
            var preferencesGroup = Group("Preferences", "Preferences");
            if (browserOwner?.PermissionTargets(descriptor.Id).Length > 0)
                Add("Website settings", "Browser.Permissions", () => ManageBrowserPermissionsAsync(descriptor), group: preferencesGroup);
            if (ExtensionTargets(descriptor.Id).Length > 0)
                Add("Extensions", "Browser.Extensions", () => ManageWidgetExtensionsAsync(descriptor.Id), group: preferencesGroup);
            if (notificationCenter is not null)
                Add("Notification preferences", "Notification.Preferences", () => ShowNotificationPreferencesAsync(descriptor), group: preferencesGroup);
            if (preferencesGroup.Items.Count == 0) menu.Flyout.Items.Remove(preferencesGroup);
            var widgetGroup = Group("Widget", "Widget");
            if (descriptor.CanManage)
                Add("Manage widget", "Widget.Manage", () => descriptor.IsUserWebApp ? EditUserWebAppAsync(descriptor.Id) : InvokeIntentCoreAsync(prepareNotification: token =>
                    owner!.Session.PrepareWidgetSettingsIntentAsync(owner.Session.GetTarget(descriptor.Id), token)), group: widgetGroup);
            Add("Reorder widgets", "Reorder", () => { anchor.Focus(FocusState.Keyboard); ToggleTrayReorder(); return Task.CompletedTask; }, ControllerButton.Y, group: widgetGroup);
            Add(descriptor.IsStopped ? "Start widget" : "Restart widget", "Restart", () => RestartWidgetAsync(descriptor, selection), group: widgetGroup);
            var quickActionStart = menu.Flyout.Items.Count;
            if (DashboardFrame(descriptor) is { } displayed)
                foreach (var action in displayed.Snapshot.QuickActions)
                    Add(action.Label, "Dashboard." + action.Button,
                        () => InvokeDashboardButtonAsync(descriptor, action.Button, ControllerInputOrigin.AccessibilityAutomation),
                        TrayShortcutAvailable(action.Button) ? action.Button : null);
            foreach (var action in descriptor.IsStopped ? [] : descriptor.QuickActions)
                Add(action.Label, "Quick." + action.Id, () => InvokeTrayQuickActionAsync(descriptor, action, selection),
                    action.ControllerButton is { } button && TrayShortcutAvailable(button) &&
                    DashboardFrame(descriptor)?.Snapshot.QuickActions.Any(item => item.Button == button) != true
                        ? button : null);
            if (menu.Flyout.Items.Count > quickActionStart)
                menu.Flyout.Items.Insert(quickActionStart, new MenuFlyoutSeparator());
            if (browserOwner is { } notificationOwner)
            {
                menu.Flyout.Items.Add(new MenuFlyoutSeparator());
                Add($"Notifications ({notificationOwner.Notifications.Entries.Count})", "Browser.Notifications", ShowNotificationsAsync,
                    HasPendingNotifications ? ControllerButton.Y : null, hold: true);
            }
            Add("Task Switcher", "Shell.TaskSwitcher", () => { FocusUtility(ShellUtility.Tasks); return Task.CompletedTask; },
                HasPendingNotifications ? null : ControllerButton.Y, hold: true);
            if (descriptor.CanStop && !descriptor.IsStopped)
                Add("Stop widget", "Widget.Stop", () => StopWidgetAsync(descriptor));
            menu.Navigation = new(menu.Flyout, () => ReferenceEquals(trayMenu, menu) && TrayOwnerCurrent(menu.Owner, menu.Selection));
            menu.Flyout.Opened += (_, _) => OnTrayMenuOpened(menu);
            menu.Flyout.Closed += (_, _) =>
            {
                menu.Theme?.Dispose(); menu.Navigation?.Dispose();
                if (!ReferenceEquals(trayMenu, menu)) return;
                trayMenu = null;
                if (ReferenceEquals(trayMenuRequest, request)) trayMenuRequest = null;
                UpdateTrayHelp();
                if (TrayOwnerCurrent(descriptor, selection)) RequestTrayFocus(descriptor);
            };
            UpdateTrayHelp();
            menu.Theme = NativePopupTheme.Menu(menu.Flyout, this);
            menu.Flyout.ShowAt(anchor);

            bool TrayShortcutAvailable(ControllerButton button) => button is not (ControllerButton.A or ControllerButton.B or ControllerButton.Y or ControllerButton.Menu) &&
                (button != ControllerButton.View || pinned is null);

            MenuFlyoutSubItem Group(string label, string id)
            {
                var group = new MenuFlyoutSubItem { Text = label };
                AutomationProperties.SetAutomationId(group, "Overlay.TrayGroup." + id);
                menu.Flyout.Items.Add(group);
                return group;
            }

            void Add(string label, string id, Func<Task> action, ControllerButton? shortcut = null, bool hold = false, MenuFlyoutSubItem? group = null)
            {
                var command = new AsyncRelayCommand(async () =>
                {
                    if (!ReferenceEquals(trayMenu, menu) || !TrayOwnerCurrent(descriptor, selection)) return;
                    CloseTrayMenu(true);
                    await action();
                });
                var item = new MenuFlyoutItem { Text = label, Command = command };
                if (shortcut is { } button) ApplyTrayCommandShortcut(item, button, hold);
                AutomationProperties.SetAutomationId(item, "Overlay.TrayCommand." + id);
                menu.Items.Add(item); (group?.Items ?? menu.Flyout.Items).Add(item);
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { ReportTrayMenuFailure(error, request, openedMenu, failureCurrent); }
    }

    private static void ApplyTrayCommandShortcut(MenuFlyoutItem item, ControllerButton button, bool hold)
    {
        var prompt = ControllerGuideModel.Prompt(button);
        var icon = new FontIcon();
        item.Icon = icon;
        item.KeyboardAcceleratorTextOverride = hold ? "Hold" : string.Empty;
        void Update()
        {
            WidgetGlyphs.Apply(icon, new() { Id = "tray.shortcut", Kind = ViewNodeKind.ControllerGlyph,
                ControllerPrompt = prompt }, WidgetControllerPrompts.PlayStation);
            AutomationProperties.SetAccessibilityView(icon, AccessibilityView.Raw);
            AutomationProperties.SetHelpText(item, "Tray shortcut: " + (hold ? "Hold " : string.Empty) +
                WidgetGlyphs.AccessibleName(prompt, WidgetControllerPrompts.PlayStation) + ".");
        }
        var subscribed = false;
        item.Loaded += (_, _) =>
        {
            if (!subscribed) { WidgetControllerPrompts.Changed += Update; subscribed = true; }
            Update();
        };
        item.Unloaded += (_, _) =>
        {
            if (subscribed) { WidgetControllerPrompts.Changed -= Update; subscribed = false; }
        };
        Update();
    }

    private void OnTrayMenuOpened(TrayMenu menu)
    {
        if (ReferenceEquals(trayMenu, menu) && TrayOwnerCurrent(menu.Owner, menu.Selection)) menu.Navigation?.FocusFirst();
        else if (ReferenceEquals(trayMenu, menu)) CloseTrayMenu(false);
        else menu.Flyout.Hide();
    }

    private void ReportTrayMenuFailure(Exception error, object request, TrayMenu? openedMenu, Func<bool> failureCurrent)
    {
        var report = failureCurrent();
        if (ReferenceEquals(trayMenuRequest, request) && ReferenceEquals(trayMenu, openedMenu)) CloseTrayMenu(false);
        ReportOperationFailure(error, () => report);
    }

    private void CloseTrayMenu(bool restoreFocus)
    {
        trayMenuRequest = null;
        if (trayMenu is not { } menu) return;
        trayMenu = null;
        UpdateTrayHelp();
        menu.Flyout.Hide();
        menu.Theme?.Dispose(); menu.Navigation?.Dispose();
        if (restoreFocus && TrayOwnerCurrent(menu.Owner, menu.Selection)) RequestTrayFocus(menu.Owner);
    }

    private bool NavigateTray(FocusNavigationDirection direction)
    {
        if (trayMenu is { } menu)
        {
            if (!TrayOwnerCurrent(menu.Owner, menu.Selection)) { CloseTrayMenu(false); return true; }
            menu.Navigation?.Move(direction);
            return true;
        }
        if (RadialOpen) { StepRadialSelection(direction); return true; }
        if (!reordering && NavigateNotificationBell(direction)) return true;
        if (!reordering)
        {
            if (direction == FocusNavigationDirection.Up && FocusedTrayWidget() is { } widget)
            { _ = SelectAsync(widget.Id); return true; }
            return false;
        }
        if (direction is FocusNavigationDirection.Left or FocusNavigationDirection.Right)
            MoveTrayWidget(direction == FocusNavigationDirection.Left ? -1 : 1);
        return true;
    }

    private async Task RouteTrayButtonAsync(ControllerButton button, ControllerEventPhase phase, ControllerInputOrigin origin)
    {
        if (phase == ControllerEventPhase.Repeated && trayMenu is null && !reordering && FocusedTrayWidget() is { } repeatedWidget)
        { await InvokeDashboardButtonAsync(repeatedWidget, button, origin, phase); return; }
        if (phase != ControllerEventPhase.Pressed) return;
        shellGestures.Claim(button);
        if (trayMenu is { } menu)
        {
            if (button == ControllerButton.Menu || button == ControllerButton.B && menu.Navigation?.Back() != true) CloseTrayMenu(true);
            else if (button == ControllerButton.A && TrayOwnerCurrent(menu.Owner, menu.Selection) && menu.Navigation is { } navigation) await navigation.ActivateAsync();
            return;
        }
        if (UtilityListFocused)
        {
            if (button == ControllerButton.B) ReturnToUtilityIcon();
            else if (button == ControllerButton.A && utilityPanel is { } panel && shownUtility is { } kind)
            { if (panel.FocusedId is { } id) await ActivateUtilityItemAsync(panel, kind, id); else panel.Activate(); }
            else if (button == ControllerButton.X && utilityPanel is { FocusedId: { } dismissId } target && shownUtility is { } dismissKind)
                await DismissUtilityItemAsync(target, dismissKind, dismissId);
            return;
        }
        if (UtilityIconFocused && button != ControllerButton.Y)
        {
            if (button == ControllerButton.A) EnterUtility();
            else if (button == ControllerButton.B) await BackFromTrayAsync();
            return;
        }
        if (button == ControllerButton.Y && TrayGestureIdentity is { } identity)
        { trayHold.Press(identity, !reordering, Environment.TickCount64); return; }
        if (reordering)
        {
            if (button is ControllerButton.A or ControllerButton.B) FinishTrayReorder();
            return;
        }
        if (button == ControllerButton.B)
        {
            // The shell owns the entire gesture even after focus returns to the
            // widget. Its release must not cancel the newly requested entry.
            shellGestures.Claim(button);
            await BackFromTrayAsync();
        }
        else if (FocusedTrayWidget() is { } widget)
        {
            if (button == ControllerButton.A) await SelectAsync(widget.Id);
            else if (button == ControllerButton.Menu) await ShowTrayMenuAsync(widget);
            else if (await InvokeDashboardButtonAsync(widget, button, origin)) { }
            else if (widget.QuickActions.FirstOrDefault(action => action.ControllerButton == button) is { } action)
                await InvokeTrayQuickActionAsync(widget, action, selectionVersion);
        }
    }
}
