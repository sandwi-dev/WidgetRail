using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private SurfaceExtent shellViewport;
#if ENABLE_WIDGET_VALIDATION
    internal SurfaceExtent? ValidationViewport { get; set; }
#endif
    private (SurfaceExtent Viewport, double InterfaceScale, double TextScale, OverlayPosition Position, bool Fullscreen)? sizingContext;
    private RailGeometry railGeometry;
    private ScrollViewer? trayScroll;
    private Style? railItemStyle;
    private double RailIconSize => railGeometry.TileSize > 0
        ? Math.Max(1, railGeometry.TileSize - 2 * Math.Min(15, railGeometry.TileSize * .24)) : 34;

    internal void ConfigureProductionViewport(SurfaceExtent viewport)
    {
#if ENABLE_WIDGET_VALIDATION
        viewport = ValidationViewport ?? viewport;
#endif
        var context = (viewport, Appearance.InterfaceScale, Appearance.TextScale, Appearance.OverlayPosition, IsMediaFullscreen);
        var sameContext = sizingContext == context;
        sizingContext = context;
        // Sample before measuring/mutating native dimensions. Only content changes
        // animate here; widget publication owns its separate switch transaction.
        var previousSize = surface?.PresentedWidgetSize;
        shellViewport = viewport;
        var zoom = Appearance.InterfaceScale;
        ProductionLayout.Margin = new(24 / zoom, 24 / zoom, 24 / zoom, 14 / zoom);
        var available = new SurfaceExtent(Math.Max(1, viewport.Width - 48 / zoom), Math.Max(1, viewport.Height - 38 / zoom));
        var bands = ProductionShellGeometry.Bands(available.Height);
        ShellOverlayLayer.Margin = ProductionLayout.Margin;
        ShellOverlayLayer.Height = bands.ContentHeight;
        ProductionLayout.RowDefinitions[1].Height = new(bands.ContentGap);
        ProductionLayout.RowDefinitions[2].Height = new(bands.GuideHeight);
        ProductionLayout.RowDefinitions[3].Height = new(bands.GuideGap);
        ProductionLayout.RowDefinitions[4].Height = new(bands.RailHeight);
        var content = new SurfaceExtent(available.Width, bands.ContentHeight);
        var resolved = OverlaySurfaceSizing.Resolve(SurfaceHints, content, new(0, 0), Appearance.TextScale, MeasureContent);
        var extentChanged = WidgetSurface.Width != resolved.Width || WidgetSurface.Height != resolved.Height;
        var animateContentResize = extentChanged && sameContext && visible && !switching && !IsMediaFullscreen &&
            surface is { IsLoaded: true, Visibility: Visibility.Visible } && previousSize is { X: > 0, Y: > 0 };
        // Unchanged placement notifications must not interrupt an active resize.
        if (extentChanged || !sameContext) surface?.SettleWidgetResize();
        WidgetSurface.Width = resolved.Width; WidgetSurface.Height = resolved.Height;
        if (surface is not null) ConfigurePresenterExtent(surface, resolved);
        if (preparingSurface is { } incoming && !ReferenceEquals(incoming.Presenter, surface))
            SizePreparingSurface(incoming);
        StatusChrome.Width = Math.Min(560, content.Width);
        StatusChrome.MaxHeight = content.Height;
        var alignment = Appearance.OverlayPosition switch
        { OverlayPosition.BottomLeft => HorizontalAlignment.Left, OverlayPosition.BottomRight => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
        WidgetSurface.HorizontalAlignment = StatusChrome.HorizontalAlignment = GuideRegion.HorizontalAlignment = RailRegion.HorizontalAlignment = alignment;
        ConfigureRail(available.Width);
        RefreshRadialChooser();
        PositionOpeningIndicator();
        if (animateContentResize)
            surface!.ResizeWidgetContent(previousSize!.Value, WidgetSurface, Motion.WidgetResizeReason.ContentSizeChanged);
        RecordSizing(viewport, new(48 / zoom, ProductionShellGeometry.ReservedHeight + 38 / zoom), resolved);
    }

    private void ConfigureRail(double width)
    {
        PinManagementCommands.Measure(new(double.PositiveInfinity, ProductionShellGeometry.RailHeight));
        reservedPinCommandWidth = PinManagementCommands.DesiredSize.Width is > 0 and var commandsWidth ? commandsWidth + 8 : 0;
        var next = ProductionShellGeometry.Rail(Math.Max(1, width - reservedPinCommandWidth), catalogItems.Count, Appearance.OverlayPosition == OverlayPosition.Center);
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
        QueueWidgetStatusRefresh();
        Tray.Width = next.ViewportWidth;
        GuideRegion.Width = Math.Min(width, Math.Max(880, next.GuideWidth));
        TrayPrevious.Visibility = TrayNext.Visibility = next.Overflow ? Visibility.Visible : Visibility.Collapsed;
        RailControls.ColumnSpacing = next.Overflow ? 6 : 0;
        var statusLeft = Appearance.OverlayPosition == OverlayPosition.BottomLeft;
        // Centered rails reserve matching space on both sides, keeping icons
        // centered as passive status appears/disappears or changes width.
        var centered = Appearance.OverlayPosition == OverlayPosition.Center;
        var commandWidth = Math.Max(0, reservedPinCommandWidth - 8);
        var statusGroupWidth = next.StatusWidth + commandWidth + (next.StatusWidth > 0 && commandWidth > 0 ? 8 : 0);
        LeadingStatusColumn.Width = new(statusLeft ? statusGroupWidth : centered ? next.StatusWidth : 0);
        TrailingStatusColumn.Width = new(!statusLeft ? statusGroupWidth : centered ? next.StatusWidth : 0);
        Grid.SetColumn(RailStatusGroup, statusLeft ? 0 : 2);
        RailStatusHost.Width = next.StatusWidth;
        RailStatusHost.Visibility = next.StatusWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
        RailStatusGroup.ColumnSpacing = next.StatusWidth > 0 && commandWidth > 0 ? 8 : 0;
        RailRegion.ColumnSpacing = next.StatusWidth > 0 ? 14 : commandWidth > 0 ? 4 : 0;
        UpdateRailOverflow();
        void UpdateIcons(DependencyObject root)
        {
            if (root is WidgetCatalogItemContent content) { content.IconSize = RailIconSize; content.TileSize = railGeometry.TileSize; return; }
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
        TrayPrevious.IsEnabled = !RadialOpen && trayScroll?.HorizontalOffset > .5;
        TrayNext.IsEnabled = !RadialOpen && trayScroll is { } scroll && scroll.HorizontalOffset < scroll.ScrollableWidth - .5;
    }

    private void TrayPreviousClicked(object sender, RoutedEventArgs args) => PageTray(-1);
    private void TrayNextClicked(object sender, RoutedEventArgs args) => PageTray(1);
    private void PageTray(int direction)
    {
        if (retired || !visible || RadialOpen || catalogItems.Count == 0) return;
        var index = Math.Max(0, Tray.SelectedIndex);
        var next = Math.Clamp(index + direction * Math.Max(1, railGeometry.VisibleCount), 0, catalogItems.Count - 1);
        SetInteractive(false);
        RequestTrayFocus(catalogItems[next]);
    }

    private void ProductionGapPressed(object sender, PointerRoutedEventArgs args)
    {
        if (IsMediaFullscreen || args.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse &&
            !args.GetCurrentPoint(ProductionRoot).Properties.IsLeftButtonPressed&&!args.GetCurrentPoint(ProductionRoot).Properties.IsRightButtonPressed) return;
        if(TryReturnFromAdjustment(args))return;
#if ENABLE_WIDGET_VALIDATION
        if(pinReturnGapTraceForValidation is {Count:<16} trace)
            trace.Add("source="+args.OriginalSource?.GetType().Name+"#"+(args.OriginalSource as FrameworkElement)?.Name);
#endif
        for (var node = args.OriginalSource as DependencyObject; node is not null; node = Presentation.PresentationLayers.LogicalParent(node))
            // Popup routed input can reach this root even though its visual
            // ancestry lives outside WidgetSurface. It is still UI content.
            if (ReferenceEquals(node, introductionCard) || ReferenceEquals(node, WidgetSurface) || node is Presentation.WidgetViewPresenter or MenuFlyoutPresenter or MenuFlyoutItemBase or ContentDialog || ReferenceEquals(node, RailRegion) || ReferenceEquals(node, GuideRegion) || ReferenceEquals(node, StatusChrome) || ReferenceEquals(node, radialView))
            {
#if ENABLE_WIDGET_VALIDATION
                if(pinReturnGapTraceForValidation is {Count:<16} kept)kept.Add("kept="+(node as FrameworkElement)?.Name);
#endif
                return;
            }
#if ENABLE_WIDGET_VALIDATION
        if(pinReturnGapTraceForValidation is {Count:<16} hidden)hidden.Add("hide requested");
#endif
        args.Handled = true;
        if(!TryBeginPinPointer(args)&&args.GetCurrentPoint(ProductionRoot).Properties.IsLeftButtonPressed)HideRequested?.Invoke();
    }
}
