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
    private sealed record ContextPopup(Binding Owner, ContextSource Declaration, WidgetPresentationBinding Presentation,
        ControllerButton Trigger, string? FocusedId, Control? ReturnFocus, FrameworkElement Anchor, MenuFlyout Flyout,
        IReadOnlyList<MenuFlyoutItem> Items, WidgetIndexedRow? Row, IDisposable? Retention)
    {
        internal bool Closed;
        internal bool Invoking;
        internal IDisposable? Theme;
        internal RoutedEventHandler? UnloadedHandler;
    }
    private ContextPopup? contextPopup;
    private int contextFocusIndex;

    // Match the SDK contract: a focused action surface wins, otherwise exactly
    // one visible container in the active scope may claim Menu/X/Y.
    private sealed record ContextTarget(Binding Owner, ViewNode Node, FrameworkElement Anchor,
        WidgetIndexedRow? Row = null, WidgetIndexedCollectionView? Collection = null);

    private void NativeContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.Handled) return;
        var keyboard = !args.TryGetPosition(sender, out _);
        if (OpenNativeContextMenu(args.OriginalSource as DependencyObject, keyboard)) args.Handled = true;
    }
    private void NativeContextKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || XamlRoot is null || Input.GamepadKeyBoundary.Owns(this, args)) return;
        var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) &
            Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        if (!IsNativeContextKey(args.Key, shift)) return;
        if (contextPopup is not null) { args.Handled = true; return; }
        if (!args.KeyStatus.WasKeyDown && OpenNativeContextMenu(FocusManager.GetFocusedElement(XamlRoot) as DependencyObject, keyboard: true))
            args.Handled = true;
    }
    internal static bool IsNativeContextKey(Windows.System.VirtualKey key, bool shift) =>
        key == Windows.System.VirtualKey.Application || key == Windows.System.VirtualKey.F10 && shift;

    // Native pointer/keyboard requests select a context owner, never its primary
    // action. The indexed route resolves the clicked resident occurrence rather
    // than reusing whatever row happened to have focus before the right click.
    internal bool OpenNativeContextMenu(DependencyObject? originalSource, bool keyboard)
    {
        if (!presentationInputEnabled || !presentationActive || applying || disposed || presentationOnly || frame is null ||
            textEntryPopup is not null || selectPopup is not null) return false;
        if (contextPopup is not null) return true; // Shift+F10 can also produce ContextRequested.
        var source = keyboard ? FocusManager.GetFocusedElement(XamlRoot) as DependencyObject : originalSource;
        var target = NativeContextTarget(source);
        // Keyboard context on an ordinary focused control may use the same
        // unambiguous scoped Menu owner used by the controller. Pointer requests
        // never jump to a distant container or a previously focused row.
        if (target is null && keyboard && FindBinding(source) is { Identity.Kind: not ViewNodeKind.ActionSurface,
            Element: not WidgetIndexedCollectionView } fallback && ContextOwnerAvailable(fallback))
            target = FindContextTarget(ControllerButton.Menu);
        if (target is null) return false;
        if (!HasAvailableActions(target.Node)) return true;
        IDisposable? retention = null;
        if (target.Row is { } row)
        {
            retention = target.Collection!.RetainContextRow(row, target.Anchor);
            if (retention is null) return true;
        }
        try
        {
            CancelGroupEntry(); CancelScrollReveal();
            if (target.Anchor is Control control && !control.Focus(FocusState.Keyboard)) return true;
            if (!ContextOwnerAvailable(target.Owner) || target.Row is { } captured &&
                !target.Collection!.IsContextRowCurrent(captured, target.Anchor) || target.Row is null &&
                (!bindings.TryGetValue(target.Owner.Identity.Id, out var currentOwner) || !ReferenceEquals(currentOwner, target.Owner) ||
                 !ReferenceEquals(declarations[target.Owner.Identity.Id].Node, target.Node))) return true;
            var transferred = retention; retention = null;
            return ShowContextMenu(target.Owner, target.Node, target.Node.ContextMenuButton ?? ControllerButton.Menu,
                target.Anchor, target.Row, transferred);
        }
        finally { retention?.Dispose(); }
    }
    private ContextTarget? NativeContextTarget(DependencyObject? source)
    {
        var nearest = FindBinding(source);
        if (nearest is { Element: WidgetIndexedCollectionView indexed } && ContextOwnerAvailable(nearest))
        {
            var captured = indexed.CapturePreviewRow(source);
            if (captured is { } row && Matches(row.Row.Item.Root, row.Row.Item.Root.ContextMenuButton ?? ControllerButton.Menu))
                return new(nearest, row.Row.Item.Root, row.Anchor, row.Row, indexed);
            // A loading, disabled or busy row is never replaced by another row.
            return null;
        }
        for (var binding = nearest; binding is not null; binding = declarations[binding.Identity.Id].ParentId is { } parent
            ? bindings.GetValueOrDefault(parent) : null)
        {
            if (!ContextOwnerAvailable(binding)) return null;
            var node = declarations[binding.Identity.Id].Node;
            if (node.ContextActions.Count > 0)
                return new(binding, node, binding.Element);
        }
        return null;
    }

    private bool OpenContextMenu(ControllerButton trigger)
    {
        if (!presentationInputEnabled) return false;
        if (FindContextTarget(trigger) is not { } target) return false;
        // Declared context ownership consumes the button even when every option
        // is unavailable; it must not fall through to an unrelated shortcut.
        if (!HasAvailableActions(target.Node)) return true;
        return ShowContextMenu(target.Owner, target.Node, trigger, target.Anchor, target.Row,
            target.Collection?.RetainFocusedRow());
    }

    // Shared by dispatch and the read-only guide; neither guessing from a label
    // nor retaining an indexed lease is necessary to discover the available route.
    private ContextTarget? FindContextTarget(ControllerButton trigger)
    {
        if (applying || disposed || presentationOnly || frame is null ||
            trigger is not (ControllerButton.Menu or ControllerButton.X or ControllerButton.Y)) return null;
        var focused = FocusedBinding();
        if (focused is { Element: WidgetIndexedCollectionView indexed } && Eligible(focused) &&
            indexed.CaptureContextRow() is { } rowTarget && Matches(rowTarget.Row.Item.Root, trigger))
            return new(focused, rowTarget.Row.Item.Root, rowTarget.Anchor, rowTarget.Row, indexed);
        if (focused is not null && ContextOwnerAvailable(focused) &&
            declarations[focused.Identity.Id].Node is { Kind: ViewNodeKind.ActionSurface } surface && Matches(surface, trigger))
            return new(focused, surface, focused.Element);
        var candidates = bindings.Values.Where(binding => ContextOwnerAvailable(binding) &&
            declarations[binding.Identity.Id].Node is { Kind: not ViewNodeKind.ActionSurface } node &&
            node.ContextMenuButton == trigger && node.ContextActions.Count > 0).Take(2).ToArray();
        return candidates.Length == 1 ? new(candidates[0], declarations[candidates[0].Identity.Id].Node,
            candidates[0].Element) : null;
    }

    private static bool HasAvailableActions(ViewNode node) => node.ContextActions.Any(action => !action.IsDisabled && !action.IsBusy);
    private static bool Matches(ViewNode node, ControllerButton trigger) => node.Kind == ViewNodeKind.ActionSurface &&
        (node.ContextMenuButton ?? ControllerButton.Menu) == trigger && node.IsDisabled != true && node.IsBusy != true && node.ContextActions.Count > 0;

    private bool ContextOwnerAvailable(Binding binding)
    {
        if (frame is null || binding.Identity.Scope != activeScope || !binding.Element.IsLoaded ||
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
        Input.GamepadKeyBoundary.ObserveFlyout(flyout, anchor);
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
        var popup = new ContextPopup(owner, source, presentation!, trigger, FocusedBinding()?.Identity.Id,
            FocusManager.GetFocusedElement(XamlRoot) as Control, anchor, flyout, items, row, retention);
        popup.Theme = NativePopupTheme.Menu(flyout, this);
        contextPopup = popup;
        NotifyControllerGuideChanged();
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
        if (!presentationInputEnabled) return false;
        if (disposed || frame is null || !popup.Presentation.SameInput(presentation) ||
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
        popup.Theme?.Dispose();
        popup.Anchor.Unloaded -= popup.UnloadedHandler;
        if (ReferenceEquals(contextPopup, popup)) contextPopup = null;
        NotifyControllerGuideChanged();
        if (!popup.Invoking) popup.Retention?.Dispose();
        if (!HasTransientControl && ContextIsCurrent(popup) && popup.ReturnFocus is { IsLoaded: true, IsEnabled: true } target)
            target.Focus(FocusState.Keyboard);
    }

    private bool MoveContextFocus(FocusNavigationDirection direction)
    {
        if (contextPopup is not { } popup) return false;
        if (!ContextIsCurrent(popup)) { DismissContextMenu(); return true; }
        NativeMenuFocus.Move(popup.Items, direction, ref contextFocusIndex);
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
        var displayed = presentation!;
        var inputSequence = ++actionSequence;
        var timestamp = Environment.TickCount64 * 1000;
        popup.Invoking = true;
        DismissContextMenu();
        try
        {
            if (!await AdmitBindingAsync(displayed) || !ContextIsCurrent(popup)) return;
            if (popup.Row is { } row)
                await row.Lease.AdmitInputAsync(displayed.Frame, row.Item.Key, popup.Trigger,
                    contextActionOwnerId: popup.Declaration.Id, contextActionId: action.ActionId,
                    sequence: inputSequence, monotonicTimestampMicroseconds: timestamp);
            else if (CanDispatchAction)
                await DispatchCapturedActionAsync(displayed, new WidgetActionEvent(action.ActionId, popup.Declaration.Id,
                    popup.Trigger, Sequence: inputSequence, MonotonicTimestampMicroseconds: timestamp,
                    InputScopeId: displayed.Scope) { FocusedElementId = popup.FocusedId });
        }
        catch (WidgetPresentationSessionException error) when (IsRetiredInput(error)) { Session?.RequestInputRefresh(displayed.Frame); }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { ReportFailure(error, IsBindingCurrent(displayed) && ContextIsCurrent(popup)); }
        finally { popup.Retention?.Dispose(); }
    }
}
