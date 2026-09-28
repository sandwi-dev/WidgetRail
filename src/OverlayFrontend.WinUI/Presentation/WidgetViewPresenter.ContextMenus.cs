using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record ContextSource(string Id, string? ActionId, string? CollectionItemKey,
        ControllerButton? ContextMenuButton, IReadOnlyList<WidgetContextAction> ContextActions);
    private sealed record ContextPopup(Binding Owner, ContextSource Declaration, WidgetPresentationAuthority Authority,
        ControllerButton Trigger, string? FocusedId, Control? ReturnFocus, FrameworkElement Anchor, MenuFlyout Flyout,
        IReadOnlyList<MenuFlyoutItem> Items, WidgetIndexedRow? Row, IDisposable? Retention)
    {
        internal bool Closed;
        internal bool Invoking;
        internal RoutedEventHandler? UnloadedHandler;
    }
    private ContextPopup? contextPopup;
    private int contextFocusIndex;

    // Match the SDK contract: a focused action surface wins, otherwise exactly
    // one visible container in the active scope may claim Menu/X/Y.
    private bool OpenContextMenu(ControllerButton trigger)
    {
        if (applying || disposed || presentationOnly || frame is null ||
            trigger is not (ControllerButton.Menu or ControllerButton.X or ControllerButton.Y)) return false;
        var focused = FocusedBinding();
        if (focused is { Element: WidgetIndexedCollectionView indexed } && Eligible(focused) &&
            indexed.CaptureContextRow() is { } rowTarget && Matches(rowTarget.Row.Item.Root, trigger))
            return ShowContextMenu(focused, rowTarget.Row.Item.Root, trigger, rowTarget.Anchor, rowTarget.Row,
                indexed.RetainFocusedRow());
        if (focused is not null && ContextOwnerAvailable(focused) &&
            declarations[focused.Identity.Id].Node is { Kind: ViewNodeKind.ActionSurface } surface && Matches(surface, trigger))
            return ShowContextMenu(focused, surface, trigger, focused.Element);
        var candidates = bindings.Values.Where(binding => ContextOwnerAvailable(binding) &&
            declarations[binding.Identity.Id].Node is { Kind: not ViewNodeKind.ActionSurface } node &&
            node.ContextMenuButton == trigger && HasAvailableActions(node)).Take(2).ToArray();
        return candidates.Length == 1 && ShowContextMenu(candidates[0], declarations[candidates[0].Identity.Id].Node,
            trigger, candidates[0].Element);
    }

    private static bool HasAvailableActions(ViewNode node) => node.ContextActions.Any(action => !action.IsDisabled && !action.IsBusy);
    private static bool Matches(ViewNode node, ControllerButton trigger) => node.Kind == ViewNodeKind.ActionSurface &&
        (node.ContextMenuButton ?? ControllerButton.Menu) == trigger && node.IsDisabled != true && node.IsBusy != true && HasAvailableActions(node);

    private bool ContextOwnerAvailable(Binding binding)
    {
        if (frame is null || binding.Identity.Scope != frame.Authority.ActiveInputScopeId || !binding.Element.IsLoaded ||
            binding.Element.ActualWidth <= 0 || binding.Element.ActualHeight <= 0) return false;
        for (var declaration = declarations.GetValueOrDefault(binding.Identity.Id); declaration is not null;
            declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            if (declaration.Node.IsDisabled == true || declaration.Node.IsBusy == true ||
                bindings[declaration.Node.Id].Element.Visibility != Visibility.Visible) return false;
        return true;
    }

    private bool ShowContextMenu(Binding owner, ViewNode node, ControllerButton trigger, FrameworkElement anchor,
        WidgetIndexedRow? row = null, IDisposable? retention = null)
    {
        DismissTransientControl();
        CancelGroupEntry();
        var flyout = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        var items = node.ContextActions.Select(action =>
        {
            var item = new MenuFlyoutItem { Text = action.Label, IsEnabled = !action.IsDisabled && !action.IsBusy };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(item, $"Widget.{node.Id}.Context.{action.ActionId}");
            if (action.Style == WidgetContextActionStyle.Danger)
                item.Icon = new SymbolIcon(Symbol.Important);
            flyout.Items.Add(item);
            return item;
        }).ToArray();
        var source = new ContextSource(node.Id, node.ActionId, node.CollectionItemKey, node.ContextMenuButton, node.ContextActions.ToArray());
        var popup = new ContextPopup(owner, source, frame!.Authority, trigger, FocusedBinding()?.Identity.Id,
            FocusManager.GetFocusedElement(XamlRoot) as Control, anchor, flyout, items, row, retention);
        contextPopup = popup;
        contextFocusIndex = Array.FindIndex(items, item => item.IsEnabled);
        for (var index = 0; index < items.Length; ++index)
        {
            var selected = index;
            items[index].GotFocus += (_, _) => { if (ReferenceEquals(contextPopup, popup)) contextFocusIndex = selected; };
            items[index].Click += async (_, _) => await InvokeContextAsync(popup, selected);
        }
        flyout.Opened += (_, _) =>
        {
            if (!ReferenceEquals(contextPopup, popup)) return;
            if (!ContextIsCurrent(popup)) { DismissContextMenu(); return; }
            if (contextFocusIndex >= 0) items[contextFocusIndex].Focus(FocusState.Keyboard);
        };
        flyout.Closed += (_, _) => CloseContextMenu(popup);
        popup.UnloadedHandler = AnchorUnloaded;
        anchor.Unloaded += AnchorUnloaded;
        try { flyout.ShowAt(anchor); }
        catch (Exception error) { CloseContextMenu(popup); ReportFailure(error); }
        return true;
        void AnchorUnloaded(object sender, RoutedEventArgs args)
        {
            if (!anchor.IsLoaded && ReferenceEquals(contextPopup, popup)) DismissContextMenu();
        }
    }

    private bool ContextIsCurrent(ContextPopup popup)
    {
        if (disposed || frame is null || !SameOwner(popup.Authority, frame.Authority) ||
            popup.Authority.ActiveInputScopeId != frame.Authority.ActiveInputScopeId ||
            !bindings.TryGetValue(popup.Owner.Identity.Id, out var owner) || !ReferenceEquals(owner, popup.Owner) ||
            !ContextOwnerAvailable(owner) || !popup.Anchor.IsLoaded) return false;
        ViewNode current;
        if (popup.Row is { } row)
        {
            if (owner.Element is not WidgetIndexedCollectionView indexed || !indexed.IsContextRowCurrent(row, popup.Anchor)) return false;
            current = row.Item.Root;
        }
        else current = declarations[owner.Identity.Id].Node;
        return current.ActionId == popup.Declaration.ActionId && current.CollectionItemKey == popup.Declaration.CollectionItemKey &&
            current.ContextMenuButton == popup.Declaration.ContextMenuButton && current.ContextActions.SequenceEqual(popup.Declaration.ContextActions);
    }

    private bool DismissContextMenu()
    {
        if (contextPopup is not { } popup) return false;
        // Revoke action authority synchronously, before WinUI's exit animation.
        contextPopup = null;
        popup.Flyout.Hide();
        CloseContextMenu(popup);
        return true;
    }
    private void CloseContextMenu(ContextPopup popup)
    {
        if (popup.Closed) return;
        popup.Closed = true;
        popup.Anchor.Unloaded -= popup.UnloadedHandler;
        if (ReferenceEquals(contextPopup, popup)) contextPopup = null;
        if (!popup.Invoking) popup.Retention?.Dispose();
        if (!HasTransientControl && ContextIsCurrent(popup) && popup.ReturnFocus is { IsLoaded: true, IsEnabled: true } target)
            target.Focus(FocusState.Keyboard);
    }

    private bool MoveContextFocus(FocusNavigationDirection direction)
    {
        if (contextPopup is not { } popup) return false;
        if (!ContextIsCurrent(popup)) { DismissContextMenu(); return true; }
        if (direction is not (FocusNavigationDirection.Up or FocusNavigationDirection.Down)) return true;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        var currentIndex = popup.Items.ToList().FindIndex(item => ReferenceEquals(item, focused));
        var step = direction == FocusNavigationDirection.Up ? -1 : 1;
        for (var index = currentIndex + step; index >= 0 && index < popup.Items.Count; index += step)
            if (popup.Items[index].IsEnabled) { if (popup.Items[index].Focus(FocusState.Keyboard)) contextFocusIndex = index; break; }
        return true;
    }
    private bool ActivateContextMenu()
    {
        if (contextPopup is not { } popup) return false;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        var index = popup.Items.ToList().FindIndex(item => ReferenceEquals(item, focused));
        if (index >= 0) _ = InvokeContextAsync(popup, index);
        return true;
    }
    private async Task InvokeContextAsync(ContextPopup popup, int index)
    {
        if (applying || !ReferenceEquals(contextPopup, popup) || !ContextIsCurrent(popup) ||
            index < 0 || index >= popup.Items.Count || !popup.Items[index].IsEnabled) return;
        var action = popup.Declaration.ContextActions[index];
        var authority = frame!.Authority;
        var inputSequence = ++actionSequence;
        var timestamp = Environment.TickCount64 * 1000;
        popup.Invoking = true;
        DismissContextMenu();
        try
        {
            if (!await AdmitInteractionAsync(authority) || !ContextIsCurrent(popup)) return;
            if (popup.Row is { } row)
                await row.Lease.AdmitInputAsync(authority, row.Item.Key, popup.Trigger,
                    contextActionOwnerId: popup.Declaration.Id, contextActionId: action.ActionId,
                    sequence: inputSequence, monotonicTimestampMicroseconds: timestamp);
            else if (DispatchActionAsync is not null)
                await DispatchActionAsync(new(authority, new WidgetActionEvent(action.ActionId, popup.Declaration.Id,
                    popup.Trigger, Sequence: inputSequence, MonotonicTimestampMicroseconds: timestamp,
                    InputScopeId: authority.ActiveInputScopeId) { FocusedElementId = popup.FocusedId }));
        }
        catch (WidgetPresentationSessionException error) when (error.Code is "snapshot_stale" or "input_scope_stale" or "presentation_stale" || popup.Row?.Lease.IsCurrent == false) { }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error); }
        finally { popup.Retention?.Dispose(); }
    }
}
