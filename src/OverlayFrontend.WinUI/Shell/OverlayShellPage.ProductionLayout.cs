using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private SurfaceExtent shellViewport;
    private RailGeometry railGeometry;
    private ScrollViewer? trayScroll;
    private Style? railItemStyle;
    private double RailIconSize => railGeometry.TileSize > 0
        ? Math.Max(1, railGeometry.TileSize - 2 * Math.Min(15, railGeometry.TileSize * .24)) : 34;

    internal void ConfigureProductionViewport(SurfaceExtent viewport)
    {
        shellViewport = viewport;
        var zoom = Appearance.InterfaceScale;
        ProductionLayout.Margin = new(24 / zoom, 24 / zoom, 24 / zoom, 14 / zoom);
        var available = new SurfaceExtent(Math.Max(1, viewport.Width - 48 / zoom), Math.Max(1, viewport.Height - 38 / zoom));
        var bands = ProductionShellGeometry.Bands(available.Height);
        ProductionLayout.RowDefinitions[1].Height = new(bands.ContentGap);
        ProductionLayout.RowDefinitions[2].Height = new(bands.GuideHeight);
        ProductionLayout.RowDefinitions[3].Height = new(bands.GuideGap);
        ProductionLayout.RowDefinitions[4].Height = new(bands.RailHeight);
        var content = new SurfaceExtent(available.Width, bands.ContentHeight);
        var resolved = OverlaySurfaceSizing.Resolve(SurfaceHints, content, new(0, 0), Appearance.TextScale, MeasureContent);
        WidgetSurface.Width = resolved.Width; WidgetSurface.Height = resolved.Height;
        if (surface is not null) ConfigurePresenterExtent(surface, resolved);
        if (preparingSurface is { } incoming && !ReferenceEquals(incoming.Presenter, surface))
            SizePreparingSurface(incoming);
        StatusChrome.Width = Math.Min(560, content.Width);
        StatusChrome.MaxHeight = content.Height;
        var alignment = Appearance.OverlayPosition switch
        { OverlayPosition.BottomLeft => HorizontalAlignment.Left, OverlayPosition.BottomRight => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        WidgetSurface.HorizontalAlignment = StatusChrome.HorizontalAlignment = TrayHelp.HorizontalAlignment = RailRegion.HorizontalAlignment = alignment;
        ConfigureRail(available.Width);
        RecordSizing(viewport, new(48 / zoom, ProductionShellGeometry.ReservedHeight + 38 / zoom), resolved);
    }

    private void ConfigureRail(double width)
    {
        var next = ProductionShellGeometry.Rail(width, catalogItems.Count, Appearance.OverlayPosition == OverlayPosition.Center);
        if (railItemStyle is null || next.TileSize != railGeometry.TileSize)
        {
            railItemStyle = new(typeof(ListViewItem));
            railItemStyle.Setters.Add(new Setter(WidthProperty, next.TileSize));
            railItemStyle.Setters.Add(new Setter(HeightProperty, next.TileSize));
            railItemStyle.Setters.Add(new Setter(MinWidthProperty, 0d));
            railItemStyle.Setters.Add(new Setter(MinHeightProperty, 0d));
            railItemStyle.Setters.Add(new Setter(MarginProperty, new Thickness(7, 4, 7, 4)));
            railItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            railItemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            railItemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            Tray.ItemContainerStyle = railItemStyle;
        }
        railGeometry = next;
        UpdateIcons(Tray);
        Tray.Width = next.ViewportWidth;
        TrayHelp.Width = next.GuideWidth;
        TrayPrevious.Visibility = TrayNext.Visibility = next.Overflow ? Visibility.Visible : Visibility.Collapsed;
        var statusLeft = Appearance.OverlayPosition == OverlayPosition.BottomLeft;
        // Centered rails reserve matching space on both sides, keeping icons
        // centered as passive status appears/disappears or changes width.
        var centered = Appearance.OverlayPosition == OverlayPosition.Center;
        LeadingStatusColumn.Width = new(statusLeft || centered ? next.StatusWidth : 0);
        TrailingStatusColumn.Width = new(!statusLeft || centered ? next.StatusWidth : 0);
        Grid.SetColumn(RailStatusHost, statusLeft ? 0 : 2);
        RailStatusHost.Visibility = next.StatusWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
        RailRegion.ColumnSpacing = next.StatusWidth > 0 ? 14 : 0;
        UpdateRailOverflow();
        void UpdateIcons(DependencyObject root)
        {
            if (root is WidgetCatalogItemContent content) { content.IconSize = RailIconSize; return; }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i) UpdateIcons(VisualTreeHelper.GetChild(root, i));
        }
    }

    private void AttachRailScroll()
    {
        if (trayScroll is not null) return;
        trayScroll = FindScroll(Tray);
        if (trayScroll is not null) trayScroll.ViewChanged += (_, _) => UpdateRailOverflow();
        UpdateRailOverflow();
        static ScrollViewer? FindScroll(DependencyObject root)
        {
            if (root is ScrollViewer scroll) return scroll;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
                if (FindScroll(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
    }

    private void UpdateRailOverflow()
    {
        TrayPrevious.IsEnabled = trayScroll?.HorizontalOffset > .5;
        TrayNext.IsEnabled = trayScroll is { } scroll && scroll.HorizontalOffset < scroll.ScrollableWidth - .5;
    }

    private void TrayPreviousClicked(object sender, RoutedEventArgs args) => PageTray(-1);
    private void TrayNextClicked(object sender, RoutedEventArgs args) => PageTray(1);
    private void PageTray(int direction)
    {
        if (retired || !visible || catalogItems.Count == 0) return;
        var index = Math.Max(0, Tray.SelectedIndex);
        var next = Math.Clamp(index + direction * Math.Max(1, railGeometry.VisibleCount), 0, catalogItems.Count - 1);
        SetInteractive(false);
        RequestTrayFocus(catalogItems[next]);
    }

    private void ProductionGapPressed(object sender, PointerRoutedEventArgs args)
    {
        if (IsMediaFullscreen || args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse &&
            !args.GetCurrentPoint(ProductionRoot).Properties.IsLeftButtonPressed) return;
        for (var node = args.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, WidgetSurface) || node is Presentation.WidgetViewPresenter || ReferenceEquals(node, RailRegion) || ReferenceEquals(node, TrayHelp) || ReferenceEquals(node, StatusChrome)) return;
        args.Handled = true;
        HideRequested?.Invoke();
    }
}
