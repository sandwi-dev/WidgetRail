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

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private readonly TrayHoldGesture trayHold = new();
    private readonly HashSet<ControllerButton> shellOwnedReleases = [];
    private bool reordering;
    private long quickActionSequence;
    private TrayMenu? trayMenu;
    private sealed record TrayMenu(BridgeWidgetDescriptor Owner, long Selection, MenuFlyout Flyout,
        Control Anchor, List<MenuFlyoutItem> Items) { internal int FocusIndex; }

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
            else if (!reordering && trayMenu is null && args.Key == Windows.System.VirtualKey.Up && FocusedTrayWidget() is { } widget)
            { args.Handled = true; _ = SelectAsync(widget.Id); }
            else if (args.Key == Windows.System.VirtualKey.Escape && trayMenu is null)
            { HideRequested?.Invoke(); args.Handled = true; }
        };
    }

    private BridgeWidgetDescriptor? FindTrayWidget(DependencyObject? element)
    {
        for (; element is not null && !ReferenceEquals(element, Tray); element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem item && Tray.ItemFromContainer(item) is BridgeWidgetDescriptor descriptor) return descriptor;
        return null;
    }

    private object? TrayGestureIdentity => !retired && visible && foreground && !interactive && trayMenu is null &&
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
        trayGuide.SetWidgetHints(ControllerGuideModel.ResolveShellHints(interactive, RecoveryVisible,
            Retry.Visibility == Visibility.Visible, trayMenu is not null, () => surface?.CaptureControllerGuide() ?? []));
        trayGuide.SetState(reordering, interactive);
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

    private async Task RunTrayHoldAsync(TrayHoldAction action)
    {
        if (action == TrayHoldAction.ToggleReorder) ToggleTrayReorder();
        else if (action == TrayHoldAction.Restart && FocusedTrayWidget() is { } widget)
            await RestartWidgetAsync(widget, selectionVersion);
    }

    private bool TrayOwnerCurrent(BridgeWidgetDescriptor captured, long selection) =>
        !retired && visible && foreground && !interactive && selectionVersion == selection &&
        requestedWidget == captured.Id && catalogItems.FirstOrDefault(item => item.Id == captured.Id) is { } current &&
        SameSurfaceOwner(captured, current) && captured.QuickActions.SequenceEqual(current.QuickActions);

    private async Task RestartWidgetAsync(BridgeWidgetDescriptor widget, long selection)
    {
        if (owner is null) return;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (!TrayOwnerCurrent(widget, selection)) return;
                interactionAdmission.Invalidate();
                await owner.Session.RestartAsync(owner.Session.GetTarget(widget.Id), lifetime.Token);
            }
            finally { transitions.Release(); }
            if (!retired && visible && selection == selectionVersion) await SelectAsync(widget.Id, enterWidget: false);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "catalog_stale" or "presentation_stale" or "unknown_widget") { }
        catch (Exception error) { ReportFailure(error); }
    }

    private async Task InvokeTrayQuickActionAsync(BridgeWidgetDescriptor widget, BridgeQuickActionDescriptor action, long selection)
    {
        if (owner is null) return;
        try
        {
            await transitions.WaitAsync(lifetime.Token);
            try
            {
                if (!TrayOwnerCurrent(widget, selection)) return;
                // Dashboard quick actions use Visible authority. They must not
                // move focus into the widget or synthesize an ordinary action.
                var target = owner.Session.GetTarget(widget.Id);
                await owner.Session.InvokeQuickActionAsync(target, action.Id, ++quickActionSequence,
                    checked(Environment.TickCount64 * 1000), lifetime.Token);
            }
            finally { transitions.Release(); }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (WidgetPresentationSessionException error) when (error.Code is "catalog_stale" or "presentation_stale" or "unknown_quick_action" or "unknown_widget") { }
        catch (Exception error) { ReportFailure(error); }
    }

    private async Task ShowTrayMenuAsync(BridgeWidgetDescriptor descriptor)
    {
        try
        {
            if (retired || !visible || owner is null) return;
            CloseTrayMenu(false);
            FinishTrayReorder(); trayHold.Cancel();
            if (requestedWidget != descriptor.Id || interactive) await SelectAsync(descriptor.Id, enterWidget: false);
            var selection = selectionVersion;
            if (!TrayOwnerCurrent(descriptor, selection) || Tray.ContainerFromItem(descriptor) is not Control anchor) return;
            var menu = new TrayMenu(descriptor, selection, new MenuFlyout { Placement = FlyoutPlacementMode.Top }, anchor, []);
            Input.GamepadKeyBoundary.ObserveFlyout(menu.Flyout, anchor);
            trayMenu = menu;
            foreach (var action in descriptor.QuickActions)
                Add(action.Label, "Quick." + action.Id, () => InvokeTrayQuickActionAsync(descriptor, action, selection));
            if (menu.Items.Count != 0) menu.Flyout.Items.Add(new MenuFlyoutSeparator());
            AddPinnedCommands(descriptor, Add);
            Add("Reorder widgets", "Reorder", () => { anchor.Focus(FocusState.Keyboard); ToggleTrayReorder(); return Task.CompletedTask; });
            Add("Restart widget", "Restart", () => RestartWidgetAsync(descriptor, selection));
            menu.Flyout.Opened += (_, _) =>
            {
                if (ReferenceEquals(trayMenu, menu) && TrayOwnerCurrent(descriptor, selection)) menu.Items[0].Focus(FocusState.Keyboard);
                else CloseTrayMenu(false);
            };
            menu.Flyout.Closed += (_, _) =>
            {
                if (!ReferenceEquals(trayMenu, menu)) return;
                trayMenu = null;
                UpdateTrayHelp();
                if (TrayOwnerCurrent(descriptor, selection)) RequestTrayFocus(descriptor);
            };
            UpdateTrayHelp();
            menu.Flyout.ShowAt(anchor);

            void Add(string label, string id, Func<Task> action)
            {
                var command = new AsyncRelayCommand(async () =>
                {
                    if (!ReferenceEquals(trayMenu, menu) || !TrayOwnerCurrent(descriptor, selection)) return;
                    CloseTrayMenu(true);
                    await action();
                });
                var item = new MenuFlyoutItem { Text = label, Command = command };
                AutomationProperties.SetAutomationId(item, "Overlay.TrayCommand." + id);
                var index = menu.Items.Count;
                item.GotFocus += (_, _) => menu.FocusIndex = index;
                menu.Items.Add(item); menu.Flyout.Items.Add(item);
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { CloseTrayMenu(false); ReportFailure(error); }
    }

    private void CloseTrayMenu(bool restoreFocus)
    {
        if (trayMenu is not { } menu) return;
        trayMenu = null;
        UpdateTrayHelp();
        menu.Flyout.Hide();
        if (restoreFocus && TrayOwnerCurrent(menu.Owner, menu.Selection)) RequestTrayFocus(menu.Owner);
    }

    private bool NavigateTray(FocusNavigationDirection direction)
    {
        if (trayMenu is { } menu)
        {
            if (!TrayOwnerCurrent(menu.Owner, menu.Selection)) { CloseTrayMenu(false); return true; }
            var delta = direction == FocusNavigationDirection.Up ? -1 : direction == FocusNavigationDirection.Down ? 1 : 0;
            menu.FocusIndex = Math.Clamp(menu.FocusIndex + delta, 0, menu.Items.Count - 1);
            menu.Items[menu.FocusIndex].Focus(FocusState.Keyboard);
            return true;
        }
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

    private async Task RouteTrayButtonAsync(ControllerButton button, ControllerEventPhase phase)
    {
        if (phase != ControllerEventPhase.Pressed) return;
        shellOwnedReleases.Add(button);
        if (trayMenu is { } menu)
        {
            if (button is ControllerButton.B or ControllerButton.Menu) CloseTrayMenu(true);
            else if (button == ControllerButton.A && TrayOwnerCurrent(menu.Owner, menu.Selection) &&
                menu.Items[menu.FocusIndex].Command is IAsyncRelayCommand command) await command.ExecuteAsync(null);
            return;
        }
        if (button == ControllerButton.Y && TrayGestureIdentity is { } identity)
        { trayHold.Press(identity, !reordering, Environment.TickCount64); return; }
        if (reordering)
        {
            if (button is ControllerButton.A or ControllerButton.B) FinishTrayReorder();
            return;
        }
        if (button == ControllerButton.B) HideRequested?.Invoke();
        else if (FocusedTrayWidget() is { } widget)
        {
            if (button == ControllerButton.A) await SelectAsync(widget.Id);
            else if (button == ControllerButton.Menu) await ShowTrayMenuAsync(widget);
            else if (widget.QuickActions.FirstOrDefault(action => action.ControllerButton == button) is { } action)
                await InvokeTrayQuickActionAsync(widget, action, selectionVersion);
        }
    }
}
