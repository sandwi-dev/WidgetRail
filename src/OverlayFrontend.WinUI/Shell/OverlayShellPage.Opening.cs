using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private WidgetOpeningIndicator? openingIndicator;

    private void InitializeOpeningIndicator()
    {
        openingIndicator = new(ResolveTrayIconAsync) { VerticalAlignment = VerticalAlignment.Bottom };
        Canvas.SetZIndex(openingIndicator, 10);
        ShellOverlayLayer.Children.Add(openingIndicator);
        openingIndicator.SizeChanged += (_, _) => PositionOpeningIndicator();
    }

    private void BeginOpening(long version, string id, bool overlayReopen)
    {
        if (!visible || retired) return;
        var descriptor = catalogItems.FirstOrDefault(item => item.Id == id);
        // This changes feedback timing only. A newer snapshot from the same
        // owner can be pending and SelectAsync still performs full admission.
        bool resuming = overlayReopen && id == activeWidget && !RecoveryVisible && !RadialOpen &&
            descriptor is { IsStopped: false } && retainedSurfaces.TryGetValue(id, out var retained) &&
            retained.Frame is { } frame && ReferenceEquals(retained.Presenter, surface) &&
            surface is { IsLoaded: true, Visibility: Visibility.Visible, Opacity: > 0, ActualWidth: > 0, ActualHeight: > 0 } &&
            owner?.Session.GetState(id) is { Failure: null, LastGood: { } latest } &&
            SameSurfaceOwner(frame.Descriptor, descriptor) && SameSurfaceOwner(frame.Descriptor, latest.Descriptor) &&
            frame.Authority.SessionGeneration == latest.Authority.SessionGeneration &&
            frame.Authority.WorkerRun == latest.Authority.WorkerRun;
        StatusChrome.Visibility = Visibility.Collapsed;
        if (!startupPresentationPending)
            openingIndicator?.Begin(version, descriptor, resuming);
        PositionOpeningIndicator();
    }

    private void PositionOpeningIndicator()
    {
        if (openingIndicator is not { } indicator || indicator.IsCompleting) return;
        indicator.HorizontalAlignment = WidgetSurface.HorizontalAlignment;
        var height = RadialOpen ? radialView?.Height ?? 0 : WidgetSurface.Height;
        if (!double.IsFinite(height)) height = 0;
        // Stay near the top of the presented surface, above radial controls.
        var available = ProductionLayout.RowDefinitions[0].ActualHeight;
        var badgeHeight = Math.Max(70, indicator.ActualHeight);
        var bottom = RadialOpen ? height + (radialView?.Margin.Bottom ?? 0) + 12 : height - badgeHeight - 16;
        indicator.Margin = new(16, 0, 16, Math.Max(16, Math.Min(bottom, available - badgeHeight - 8)));
        indicator.MaxWidth = Math.Max(1, Math.Min(360, shellViewport.Width > 0 ? shellViewport.Width - 80 / Appearance.InterfaceScale : 360));
    }
}
