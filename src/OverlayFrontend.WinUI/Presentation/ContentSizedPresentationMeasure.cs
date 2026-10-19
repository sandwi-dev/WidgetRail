using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal static class ContentSizedPresentationMeasure
{
    internal static double Height(FrameworkElement element, double width, double maximum)
    {
        var previousHeight = element.Height;
        var previousMargin = element.Margin;
        try
        {
            element.Height = double.NaN;
            // Placement margins are not content. Measuring them would make the next
            // viewport resize grow the panel according to its distance from the origin.
            element.Margin = new();
            element.Measure(new(width, maximum));
            return element.DesiredSize.Height;
        }
        finally
        {
            element.Height = previousHeight;
            element.Margin = previousMargin;
        }
    }
}
