using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Decorates only the host-owned native scroll viewport. The original
/// content presenter and its template bindings survive; no content is materialized,
/// no offset is changed, and decorative strips never become input targets.</summary>
internal sealed class WidgetScrollEdgeCues : IDisposable
{
    internal const double StripHeight = 4;
    internal const double CueWidth = 40;
    private readonly Control owner;
    private ScrollViewer? scroll;
    private Grid? templateGrid;
    private RowDefinition? topStrip, bottomStrip;
    private readonly List<(FrameworkElement Element, int Row, object LocalValue)> originalRows = [];
    private Border? above, below;
    private long extentToken, viewportToken, modeToken, templateToken;
    private Brush? paint;
    private bool highContrast, disposed;
    internal ScrollEdgeCueState State { get; private set; }
    internal FrameworkElement? AboveCue => above;
    internal FrameworkElement? BelowCue => below;
    internal ScrollViewer? Scroll => scroll;

    internal WidgetScrollEdgeCues(Control owner)
    {
        this.owner = owner;
        owner.Loaded += Loaded;
        owner.Unloaded += Unloaded;
        templateToken = owner.RegisterPropertyChangedCallback(Control.TemplateProperty, (_, _) =>
        {
            Detach();
            owner.DispatcherQueue.TryEnqueue(() => { if (!disposed && owner.IsLoaded) Attach(); });
        });
        if (owner.IsLoaded) Attach();
    }
    internal void Update(Brush? brush, bool contrast)
    {
        paint = brush ?? owner.Foreground;
        highContrast = contrast;
        if (owner.IsLoaded && scroll is null) Attach();
        Paint();
    }
    private void Loaded(object sender, RoutedEventArgs args) => Attach();
    private void Unloaded(object sender, RoutedEventArgs args) => Detach();
    private void Attach()
    {
        if (disposed || scroll is not null) return;
        owner.ApplyTemplate();
        // A ListView's first scroll viewer is its own viewport. Never descend into
        // item content to decorate another control's nested scroller.
        var candidate = owner as ScrollViewer ?? FindViewport(owner);
        if (candidate is null) return;
        candidate.ApplyTemplate();
        var content = WidgetScrollViewport.FindPresenter(candidate);
        if (content is null || VisualTreeHelper.GetParent(content) is not Grid parent) return;
        var count = parent.RowDefinitions.Count;
        // Only decorate the standard bounded native layout. Row bindings and
        // unsupported/custom layouts remain untouched rather than being replaced.
        if (count is < 1 or > 4 || Grid.GetRow(content) != 0 || Grid.GetRowSpan(content) != count) return;
        foreach (var element in parent.Children)
        {
            if (element is not FrameworkElement child) { originalRows.Clear(); return; }
            var local = child.ReadLocalValue(Grid.RowProperty);
            var spanLocal = child.ReadLocalValue(Grid.RowSpanProperty);
            var row = Grid.GetRow(child);
            var span = Grid.GetRowSpan(child);
            if ((local != DependencyProperty.UnsetValue && local is not int) ||
                (spanLocal != DependencyProperty.UnsetValue && spanLocal is not int) ||
                row < 0 || row >= count || span < 1 || span > count - row)
            { originalRows.Clear(); return; }
            originalRows.Add((child, row, local));
        }
        scroll = candidate; templateGrid = parent;
        topStrip = new RowDefinition { Height = new GridLength(StripHeight) };
        bottomStrip = new RowDefinition { Height = new GridLength(StripHeight) };
        // Never remove/reparent native content: that raises Unloaded/Loaded and
        // can steal modal entry focus. Only geometry changes, preserving controls,
        // bindings, template names, automation identity and virtualized row leases.
        parent.RowDefinitions.Insert(0, topStrip);
        parent.RowDefinitions.Add(bottomStrip);
        foreach (var original in originalRows) Grid.SetRow(original.Element, original.Row + 1);
        above = CreateCue(); below = CreateCue();
        Grid.SetRow(below, count + 1);
        var columns = Math.Max(1, parent.ColumnDefinitions.Count);
        Grid.SetColumnSpan(above, columns); Grid.SetColumnSpan(below, columns);
        parent.Children.Add(above); parent.Children.Add(below);
        scroll.ViewChanged += ViewChanged;
        scroll.SizeChanged += SizeChanged;
        extentToken = scroll.RegisterPropertyChangedCallback(ScrollViewer.ExtentHeightProperty, (_, _) => Refresh());
        viewportToken = scroll.RegisterPropertyChangedCallback(ScrollViewer.ViewportHeightProperty, (_, _) => Refresh());
        modeToken = scroll.RegisterPropertyChangedCallback(ScrollViewer.VerticalScrollModeProperty, (_, _) => Refresh());
        Paint(); Refresh();
    }
    private static Border CreateCue()
    {
        var cue = new Border { Width = CueWidth, Height = 2, CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAccessibilityView(cue, AccessibilityView.Raw);
        return cue;
    }
    private void Paint()
    {
        if (above is null || below is null) return;
        above.Background = below.Background = paint;
        above.Opacity = below.Opacity = highContrast ? 1 : .72;
    }
    private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs args) => Refresh();
    private void SizeChanged(object sender, SizeChangedEventArgs args) => Refresh();
    private void Refresh()
    {
        if (scroll is null || topStrip is null || bottomStrip is null || above is null || below is null) return;
        var vertical = scroll.VerticalScrollMode != ScrollMode.Disabled;
        // Fixed reserved strips do not appear/disappear as offset or content changes.
        // Horizontal-only lists do not need a vertical affordance or an inset.
        var height = new GridLength(vertical ? StripHeight : 0);
        if (topStrip.Height != height) { topStrip.Height = height; bottomStrip.Height = height; }
        State = ScrollEdgeCueState.From(scroll.VerticalOffset, scroll.ExtentHeight, scroll.ViewportHeight, vertical);
        above.Visibility = State.Above ? Visibility.Visible : Visibility.Collapsed;
        below.Visibility = State.Below ? Visibility.Visible : Visibility.Collapsed;
    }
    private static ScrollViewer? FindViewport(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewport) return viewport;
            // Native template chrome precedes realized items; never enter content rows.
            if (child is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem) continue;
            if (FindViewport(child) is { } found) return found;
        }
        return null;
    }
    private void Detach()
    {
        if (scroll is not null)
        {
            scroll.ViewChanged -= ViewChanged; scroll.SizeChanged -= SizeChanged;
            scroll.UnregisterPropertyChangedCallback(ScrollViewer.ExtentHeightProperty, extentToken);
            scroll.UnregisterPropertyChangedCallback(ScrollViewer.ViewportHeightProperty, viewportToken);
            scroll.UnregisterPropertyChangedCallback(ScrollViewer.VerticalScrollModeProperty, modeToken);
        }
        if (templateGrid is not null)
        {
            if (above is not null) templateGrid.Children.Remove(above);
            if (below is not null) templateGrid.Children.Remove(below);
            foreach (var original in originalRows)
            {
                // Do not overwrite a binding or replacement value applied by a
                // later template owner. Standard native templates are immutable.
                if (original.Element.ReadLocalValue(Grid.RowProperty) is int row && row == original.Row + 1)
                {
                    if (original.LocalValue == DependencyProperty.UnsetValue) original.Element.ClearValue(Grid.RowProperty);
                    else original.Element.SetValue(Grid.RowProperty, original.LocalValue);
                }
            }
            if (topStrip is not null) templateGrid.RowDefinitions.Remove(topStrip);
            if (bottomStrip is not null) templateGrid.RowDefinitions.Remove(bottomStrip);
        }
        originalRows.Clear();
        scroll = null; templateGrid = null; topStrip = bottomStrip = null; above = below = null; State = default;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        owner.Loaded -= Loaded; owner.Unloaded -= Unloaded;
        owner.UnregisterPropertyChangedCallback(Control.TemplateProperty, templateToken);
        Detach();
    }
}
