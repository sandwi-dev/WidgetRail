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
        ProductionLayout.Children.Add(openingIndicator);
        openingIndicator.SizeChanged += (_, _) => PositionOpeningIndicator();
    }

    private void BeginOpening(long version, string id)
    {
        if (!visible || retired) return;
        // Retained content stays drawable; this passive badge grants no input.
        StatusChrome.Visibility = Visibility.Collapsed;
        if (!startupPresentationPending)
            openingIndicator?.Begin(version, catalogItems.FirstOrDefault(item => item.Id == id));
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
