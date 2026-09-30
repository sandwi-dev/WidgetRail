using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private BridgeComputedStyleValue? ComputedStyle(ViewNode node, string property) =>
        presentation?.RenderStyles.GetValueOrDefault(node.Id)?.Base.GetValueOrDefault(property);

    private double? Length(ViewNode node, string property)
    {
        if (ComputedStyle(node, property) is not { Number: { } number } value || !double.IsFinite(number) || number < 0) return null;
        var viewport = Viewport();
        return value.Unit switch { "px" or null => number, "vw" => viewport.Width * number / 100,
            "vh" => viewport.Height * number / 100, _ => null };
    }

    private void ApplySizeAndTypography(FrameworkElement element, ViewNode node)
    {
        // ProgressRing's animated visual needs a finite box even when its parent
        // measures an Auto row/column without a bound. Preserve the declaration's
        // intrinsic indicator size instead of erasing it during style projection.
        var intrinsic = node.Kind == ViewNodeKind.LoadingIndicator
            ? node.IndicatorSize switch { LoadingIndicatorSize.Compact => 16d, LoadingIndicatorSize.Large => 48d, _ => 32d }
            : double.NaN;
        var containerOwnsBox = indexedRootStyleOnContainer && node.Id == fragmentRootId;
        element.Width = containerOwnsBox ? double.NaN : Length(node, "width") ?? intrinsic;
        element.Height = containerOwnsBox ? double.NaN : Length(node, "height") ?? intrinsic;
        element.MinWidth = containerOwnsBox ? 0 : Length(node, "min-width") ?? 0;
        var minimumHeight = containerOwnsBox ? 0 : Length(node, "min-height") ?? 0;
        if (element is WidgetSlider slider) slider.SetAuthoredMinimumHeight(minimumHeight);
        else element.MinHeight = minimumHeight;
        element.MaxWidth = containerOwnsBox ? double.PositiveInfinity : Length(node, "max-width") ?? double.PositiveInfinity;
        element.MaxHeight = containerOwnsBox ? double.PositiveInfinity : Length(node, "max-height") ?? double.PositiveInfinity;
        element.Margin = containerOwnsBox ? new Thickness(0) :
            NativeComputedStyleAdapter.Spacing(presentation?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "margin") ?? new Thickness(0);
    }

    private void UpdateContainerLayout(Binding binding, ViewNode node)
    {
        if (binding.Children is not null && node.GridLayout is null)
            foreach (var child in node.Children)
            {
                // Attached layout values belong to the current parent. A retained
                // control can move from an explicit cell into a row, scroll or
                // poster without carrying its old spans/position into that owner.
                var element = bindings[child.Id].LayoutElement;
                if (binding.Children is not Grid)
                {
                    if (Grid.GetRow(element) != 0) Grid.SetRow(element, 0);
                    if (Grid.GetColumn(element) != 0) Grid.SetColumn(element, 0);
                }
                if (Grid.GetRowSpan(element) != 1) Grid.SetRowSpan(element, 1);
                if (Grid.GetColumnSpan(element) != 1) Grid.SetColumnSpan(element, 1);
            }
        if (binding.Element is ScrollViewer scroll && binding.Children is StackPanel scrollContent)
        {
            var horizontal = node.ScrollAxis == ScrollAxis.Horizontal;
            var gap = NativeComputedStyleAdapter.Spacing(presentation?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "gap");
            scrollContent.Spacing = (horizontal ? gap?.Right : gap?.Top) ?? 12;
            scroll.HorizontalContentAlignment = horizontal ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            scroll.VerticalContentAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            foreach (var child in node.Children)
            {
                var element = bindings[child.Id].LayoutElement;
                // StackPanel owns the unbounded scrolling axis. Only stretch
                // across the viewport; reset the old axis when direction changes.
                if (horizontal) element.HorizontalAlignment = HorizontalAlignment.Left;
                else element.VerticalAlignment = VerticalAlignment.Top;
                ApplyCrossAxisAlignment(element, child, node, horizontal);
            }
        }
        if (binding.Element is WidgetPresentationSurface { Fragment: not null } surface)
        {
            UpdateLayout(surface.ContentPanel, node);
            var horizontal = ComputedStyle(node, "direction")?.Text == "row";
            var axis = horizontal ? "width" : "height";
            var grows = node.Children.Where(child => bindings[child.Id].Element.Visibility == Visibility.Visible).Any(child => (ComputedStyle(child, "flex-grow")?.Number ??
                (NeedsConstrainedViewport(child) && Length(child, axis) is null && Length(child, "max-" + axis) is null ? 1 : 0)) > 0);
            var gap = NativeComputedStyleAdapter.Spacing(presentation?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "gap");
            surface.ConfigureFocusLayout(horizontal, ComputedStyle(node, "justify")?.Text, ComputedStyle(node, "align")?.Text,
                (horizontal ? gap?.Right : gap?.Top) ?? 12, grows);
        }
        if (binding.Element is Grid layout && layout is not (WidgetModalLayer or WidgetPosterPanel or WidgetArtworkView)) UpdateLayout(layout, node);
        if (node.Kind == ViewNodeKind.ActionSurface && binding.Children is Grid content &&
            content is not WidgetPosterPanel && !ReferenceEquals(content, binding.Element)) UpdateLayout(content, node);
    }

    private void UpdateLayout(Grid grid, ViewNode node)
    {
        if (node.Kind == ViewNodeKind.Grid && node.GridLayout is { } explicitLayout)
        {
            UpdateExplicitGrid(grid, node, explicitLayout);
            return;
        }
        var horizontal = ComputedStyle(node, "direction")?.Text switch
        {
            "row" => true,
            "column" => false,
            _ => node.Kind == ViewNodeKind.Row ||
                node.Kind == ViewNodeKind.ActionSurface && node.ActionSurfaceOrientation == ActionSurfaceOrientation.Horizontal,
        };
        var children = node.Children.Where(child => bindings[child.Id].Element.Visibility == Visibility.Visible).ToArray();
        if (grid is WidgetResponsiveGrid responsive)
        {
            var spacing = NativeComputedStyleAdapter.Spacing(presentation?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "gap");
            responsive.RowSpacing = spacing?.Top ?? 12;
            responsive.ColumnSpacing = spacing?.Right ?? 12;
            responsive.Configure(children.Select(child => bindings[child.Id].LayoutElement).ToArray(),
                node.GridMinimumColumnWidth ?? 160, node.GridMaximumColumns ?? 12);
            return;
        }
        var gap = NativeComputedStyleAdapter.Spacing(presentation?.RenderStyles.GetValueOrDefault(node.Id)?.Base, "gap");
        var spacingSize = (horizontal ? gap?.Right : gap?.Top) ?? 12;
        var grows = children.Select(child => ComputedStyle(child, "flex-grow")?.Number ??
            (NeedsConstrainedViewport(child) && Length(child, horizontal ? "width" : "height") is null &&
                Length(child, horizontal ? "max-width" : "max-height") is null ? 1 : 0)).ToArray();
        var plan = NativeFlowTracks.Create(grows, spacingSize, ComputedStyle(node, "justify")?.Text);
        var tracks = plan.Tracks;
        var slots = plan.Slots;
        // A capped flex child must also cap its star track. Otherwise Grid
        // reserves unused space around the smaller element instead of returning
        // it to other growing children (or leaving it at the end of the row).
        var maximums = Enumerable.Repeat(double.PositiveInfinity, tracks.Count).ToArray();
        for (var index = 0; index < children.Length; ++index)
        {
            if (grows[index] <= 0 || Length(children[index], horizontal ? "max-width" : "max-height") is not { } maximum) continue;
            var margin = NativeComputedStyleAdapter.Spacing(presentation?.RenderStyles.GetValueOrDefault(children[index].Id)?.Base, "margin") ?? new Thickness(0);
            maximums[slots[index]] = Math.Max(0, maximum + (horizontal ? margin.Left + margin.Right : margin.Top + margin.Bottom));
        }
        // Explicit gap tracks avoid spacing around invisible children and at the
        // ends of centered groups. Native Grid still owns every measurement.
        grid.RowSpacing = grid.ColumnSpacing = 0;
        if (horizontal)
        {
            grid.RowDefinitions.Clear();
            while (grid.ColumnDefinitions.Count > tracks.Count) grid.ColumnDefinitions.RemoveAt(grid.ColumnDefinitions.Count - 1);
            while (grid.ColumnDefinitions.Count < tracks.Count) grid.ColumnDefinitions.Add(new());
            for (var index = 0; index < tracks.Count; ++index)
            { grid.ColumnDefinitions[index].Width = tracks[index]; grid.ColumnDefinitions[index].MinWidth = 0;
                grid.ColumnDefinitions[index].MaxWidth = maximums[index]; }
        }
        else
        {
            grid.ColumnDefinitions.Clear();
            while (grid.RowDefinitions.Count > tracks.Count) grid.RowDefinitions.RemoveAt(grid.RowDefinitions.Count - 1);
            while (grid.RowDefinitions.Count < tracks.Count) grid.RowDefinitions.Add(new());
            for (var index = 0; index < tracks.Count; ++index)
            { grid.RowDefinitions[index].Height = tracks[index]; grid.RowDefinitions[index].MinHeight = 0;
                grid.RowDefinitions[index].MaxHeight = maximums[index]; }
        }
        for (var index = 0; index < children.Length; ++index)
        {
            var child = children[index];
            Grid.SetRow(bindings[child.Id].LayoutElement, horizontal ? 0 : slots[index]);
            Grid.SetColumn(bindings[child.Id].LayoutElement, horizontal ? slots[index] : 0);
            var element = bindings[child.Id].LayoutElement;
            if (horizontal)
            {
                // Star tracks alone do not expand native controls: Button's
                // default main-axis alignment is Left. Fill an authored growing
                // track, and clear the old stretch when its grow is removed.
                element.HorizontalAlignment = grows[index] > 0 || FillsAxis(child, "width")
                    ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            }
            else
            {
                element.VerticalAlignment = grows[index] > 0 || FillsAxis(child, "height")
                    ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            }
            ApplyCrossAxisAlignment(element, child, node, horizontal);
        }
    }

    private void ApplyCrossAxisAlignment(FrameworkElement element, ViewNode child, ViewNode parent, bool horizontal)
    {
        var alignment = ComputedStyle(parent, "align")?.Text;
        // Apply to the layout owner (including motion wrappers). Native sizing
        // still enforces authored fixed/min/max dimensions independently.
        if (horizontal)
            element.VerticalAlignment = FillsAxis(child, "height") ? VerticalAlignment.Stretch : alignment switch
            { "start" => VerticalAlignment.Top, "center" => VerticalAlignment.Center, "end" => VerticalAlignment.Bottom, _ => VerticalAlignment.Stretch };
        else
            element.HorizontalAlignment = FillsAxis(child, "width") ? HorizontalAlignment.Stretch : alignment switch
            { "start" => HorizontalAlignment.Left, "center" => HorizontalAlignment.Center, "end" => HorizontalAlignment.Right, _ => HorizontalAlignment.Stretch };
    }
    private bool FillsAxis(ViewNode node, string property) => ComputedStyle(node, property) is { Unit: "%", Number: 100 };
    private static bool NeedsConstrainedViewport(ViewNode node) => node.Kind is ViewNodeKind.IndexedCollection or ViewNodeKind.Scroll or ViewNodeKind.ModalLayer or ViewNodeKind.MediaViewport || node.Children.Any(NeedsConstrainedViewport);
}
