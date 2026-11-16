using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    internal bool SelectionOverlaysContent => layoutKind == CollectionLayoutKind.AdaptiveGrid;
    internal bool SelectionActive => source?.Declaration.IndexedSelection?.IsActive == true;
    private TextBlock? selectionStatus;
    private bool selectionWasDeclared;
    private bool selectionNavigationQueued;
    private UIElement CreateSelectionContent(ListViewBase items, bool declared)
    {
        selectionStatus = null;
        if (!declared) return items;
        selectionStatus = new()
        {
            TextWrapping = TextWrapping.Wrap, Margin = new(8), Visibility = Visibility.Collapsed,
            Text = "Select up to 256 items at a time. Your previous selection was kept.",
        };
        var host = new Grid();
        host.RowDefinitions.Add(new() { Height = GridLength.Auto });
        host.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        host.Children.Add(selectionStatus);
        host.Children.Add(items);
        Grid.SetRow(items, 1);
        return host;
    }
    private (WidgetIndexedRows Source, long Epoch, int Index, IndexedSelectionGesture Gesture)? extendAfterNavigation;
    internal string SelectionActionLabel => FocusedIndex() is { } index && IsSelected(index) ? "Deselect" : "Select";
    internal bool IsSelected(int index) => source?.Declaration.IndexedSelection is { IsActive: true } selection && selection.Indices.Contains(index);
    private static bool ModifierDown(VirtualKey key) => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private IndexedSelectionGesture PointerSelectionGesture => ModifierDown(VirtualKey.Shift)
        ? ModifierDown(VirtualKey.Control) ? IndexedSelectionGesture.ExtendAdd : IndexedSelectionGesture.Extend
        : IndexedSelectionGesture.Toggle;

    internal bool HandleSelectionKey(KeyRoutedEventArgs args)
    {
        if (args.Handled || !SelectionActive || !CanReceiveInput || Input.GamepadKeyBoundary.Owns(this, args)) return false;
        if (args.Key == VirtualKey.A && ModifierDown(VirtualKey.Control) || args.Key == VirtualKey.Space)
        {
            extendAfterNavigation = null; args.Handled = true;
            if (FocusedRow() is { } row) _ = SelectRowAsync(row, args.Key == VirtualKey.A ? IndexedSelectionGesture.SelectAll : IndexedSelectionGesture.Toggle);
            return true;
        }
        var direction = args.Key switch
        {
            VirtualKey.Up => FocusNavigationDirection.Up, VirtualKey.Down => FocusNavigationDirection.Down,
            VirtualKey.Left => FocusNavigationDirection.Left, VirtualKey.Right => FocusNavigationDirection.Right,
            _ => FocusNavigationDirection.None,
        };
        extendAfterNavigation = null;
        if (direction == FocusNavigationDirection.None || !ModifierDown(VirtualKey.Shift)) return false;
        if (!MoveFocus(direction) || pendingIndex is not { } target || source?.Declaration.IndexedSelection is not { } selection) return false;
        extendAfterNavigation = (source, selection.Epoch, target, ModifierDown(VirtualKey.Control) ? IndexedSelectionGesture.ExtendAdd : IndexedSelectionGesture.Extend);
        args.Handled = true; CompleteSelectionNavigation(); return true;
    }
    private void CompleteSelectionNavigation()
    {
        if (selectionNavigationQueued || extendAfterNavigation is not { } pending || FocusedIndex() != pending.Index) return;
        // Focus can arrive from FinishNavigation during LayoutUpdated. Defer IPC
        // and resulting publications until that native layout/focus transaction ends.
        selectionNavigationQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            selectionNavigationQueued = false;
            if (extendAfterNavigation is not { } current || !ReferenceEquals(current.Source,pending.Source) ||
                current.Epoch != pending.Epoch || current.Index != pending.Index || current.Gesture != pending.Gesture) return;
            extendAfterNavigation = null;
            if (CanReceiveInput && FocusedIndex() == pending.Index && ReferenceEquals(source, pending.Source) &&
                source.Declaration.IndexedSelection is { IsActive: true } selection && selection.Epoch == pending.Epoch &&
                FocusedRow() is { } row) _ = SelectRowAsync(row, pending.Gesture);
        })) { selectionNavigationQueued = false; extendAfterNavigation = null; }
    }
    internal Task ToggleSelectionAsync(WidgetIndexedRow row) => SelectRowAsync(row, IndexedSelectionGesture.Toggle);
    private void SelectionTapped(object sender, TappedRoutedEventArgs args)
    {
        if (!SelectionActive || !CanReceiveInput) return;
        CheckBox? toggle = null;
        // Include container padding, while the owned checkbox retains its own
        // Click/UIA toggle. IsItemClickEnabled is off to avoid a second dispatch.
        for (var target = args.OriginalSource as DependencyObject; target is not null && !ReferenceEquals(target, this);
            target = VisualTreeHelper.GetParent(target))
        {
            if (target is CheckBox check) toggle = check;
            if (target is WidgetIndexedRowView rowView && toggle is not null && ReferenceEquals(toggle, rowView.SelectionToggle)) return;
            if (target is SelectorItem container && containers.TryGetValue(container, out var item))
            {
                args.Handled = true;
                if (item.Slot.Value is { } row) _ = SelectRowAsync(row, PointerSelectionGesture);
                return;
            }
        }
    }
    private async Task SelectRowAsync(WidgetIndexedRow row, IndexedSelectionGesture gesture)
    {
        if (!CanReceiveInput || !SelectionActive || source is not { } captured || !ReferenceEquals(row.Owner, captured) || !row.Lease.IsCurrent) return;
        var binding = captured.Presentation; var displayed = binding.Frame;
        var selection = captured.Declaration.IndexedSelection!;
        try
        {
            if (EnsureInteractionAsync is { } admit && !await admit(displayed.Authority, CancellationToken.None)) return;
            if (!CanReceiveInput || !ReferenceEquals(source, captured) || !binding.SameInput(source.Presentation) || !row.Lease.IsCurrent ||
                source.Declaration.IndexedSelection is not { IsActive: true } current || current.Epoch != selection.Epoch) return;
            await row.Lease.AdmitSelectionAsync(displayed, row.Item.Key, new(selection.Epoch, gesture),
                ++sequence, Environment.TickCount64 * 1000);
        }
        catch (WidgetPresentationSessionException error) when (WidgetInputFailure.IsStale(error)) { session.RequestInputRefresh(displayed); }
        catch (OperationCanceledException) when (disposed || !row.Lease.IsCurrent) { }
        catch (Exception error)
        {
            if (!disposed && ReferenceEquals(source, captured) && binding.SameInput(source?.Presentation)) failed(error);
            else Diagnostics.FrontendFailureLog.Current.Write("retired-indexed-selection", error);
        }
    }
    private void RefreshSelectionState()
    {
        if (extendAfterNavigation is { } pending && (!ReferenceEquals(source, pending.Source) ||
            source?.Declaration.IndexedSelection is not { IsActive: true } current || current.Epoch != pending.Epoch)) extendAfterNavigation = null;
        if (view is null) return;
        var declared = source?.Declaration.IndexedSelection is not null;
        if (!declared && !selectionWasDeclared) return;
        selectionWasDeclared = declared;
        foreach (var row in WalkSelectionRows(view)) row.RefreshSelection();
        foreach (var (container, item) in containers) UpdateSelectionAccessibility(container, item.Slot);
        if (source?.Declaration.IndexedCollection?.Discovery is not null || selectionStatus is null) return;
        var limited = source?.Declaration.IndexedSelection is { IsActive: true, LimitReached: true };
        if (limited)
        {
            selectionStatus.Foreground = view.Foreground;
            AutomationProperties.SetAutomationId(selectionStatus, "Widget." + source!.Declaration.Id + ".SelectionStatus");
            AutomationProperties.SetLiveSetting(selectionStatus, AutomationLiveSetting.Polite);
            var changed = selectionStatus.Visibility != Visibility.Visible;
            selectionStatus.Visibility = Visibility.Visible;
            if (changed) FrameworkElementAutomationPeer.CreatePeerForElement(selectionStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        else selectionStatus.Visibility = Visibility.Collapsed;
    }
    private void UpdateSelectionAccessibility(SelectorItem container, IndexedItem<WidgetIndexedRow> slot)
    {
        var status = WidgetAccessibleState.ItemStatus(slot.Value?.Item.Root);
        if (SelectionActive) status = (IsSelected(slot.Index) ? "Selected" : "Not selected") + (string.IsNullOrEmpty(status) ? "" : "; " + status);
        AutomationProperties.SetItemStatus(container, status);
    }
    private static IEnumerable<WidgetIndexedRowView> WalkSelectionRows(DependencyObject root)
    {
        if (root is WidgetIndexedRowView row) { yield return row; yield break; }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            foreach (var child in WalkSelectionRows(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
