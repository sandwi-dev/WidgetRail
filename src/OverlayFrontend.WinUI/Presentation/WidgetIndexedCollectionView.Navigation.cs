using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetIndexedCollectionView
{
    private int? pendingIndex;
    private bool navigationQueued;
    private FocusNavigationDirection pendingDirection;

    internal bool MoveFocus(FocusNavigationDirection direction)
    {
        if (view is null || source is null || !view.IsEnabled || view.Items.Count == 0) return false;
        var current = pendingIndex ?? FocusedIndex();
        if (current is null) return false;
        var grid = view.ItemsPanelRoot as ItemsWrapGrid;
        var columns = grid is null || grid.ItemWidth <= 0 || double.IsNaN(grid.ItemWidth) ? 1 :
            Math.Max(1, (int)Math.Round(view.ActualWidth / grid.ItemWidth));
        var delta = direction switch
        {
            FocusNavigationDirection.Up when axis != ScrollAxis.Horizontal => -columns,
            FocusNavigationDirection.Down when axis != ScrollAxis.Horizontal => columns,
            FocusNavigationDirection.Left when axis == ScrollAxis.Horizontal || grid is not null && current % columns > 0 => -1,
            FocusNavigationDirection.Right when axis == ScrollAxis.Horizontal || grid is not null && current % columns < columns - 1 => 1,
            _ => 0,
        };
        if (delta == 0) return false;
        var target = current.Value + delta;
        if (target >= view.Items.Count && delta > 1 && current.Value / columns < (view.Items.Count - 1) / columns) target = view.Items.Count - 1;
        if (target < 0 || target >= view.Items.Count) return pendingIndex is not null;
        pendingIndex = target;
        pendingDirection = direction;
        view.LayoutUpdated -= FinishNavigation;
        view.LayoutUpdated += FinishNavigation;
        if (!navigationQueued)
        {
            navigationQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                navigationQueued = false;
                if (pendingIndex is not { } index || view is null) return;
                // One native request for the newest logical target. A run of
                // controller frames never creates a backlog of layout work.
                view.ScrollIntoView(view.Items[index], ScrollIntoViewAlignment.Default);
                FinishNavigation(null, null!);
            });
        }
        return true;
    }

    private int? FocusedIndex()
    {
        for (var focused = view?.XamlRoot is null ? null : FocusManager.GetFocusedElement(view.XamlRoot) as DependencyObject;
             focused is not null && !ReferenceEquals(focused, view); focused = VisualTreeHelper.GetParent(focused))
            if (focused is SelectorItem item && view!.IndexFromContainer(item) is var index && index >= 0) return index;
        return null;
    }

    private void FinishNavigation(object? sender, object args)
    {
        if (pendingIndex is not { } index || view is null || disposed) return;
        if (view.ContainerFromIndex(index) is not Control { IsLoaded: true } target) return;
        if (!target.IsEnabled)
        {
            if (!MoveFocus(pendingDirection)) CancelNavigation();
        }
        else if (target.Focus(FocusState.Keyboard)) CancelNavigation();
    }

    private void CancelNavigation()
    {
        pendingIndex = null;
        if (view is not null) view.LayoutUpdated -= FinishNavigation;
    }

    private void OnLosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        for (var target = args.NewFocusedElement as DependencyObject; target is not null; target = VisualTreeHelper.GetParent(target))
            if (ReferenceEquals(target, view)) return;
        CancelNavigation();
    }
}
