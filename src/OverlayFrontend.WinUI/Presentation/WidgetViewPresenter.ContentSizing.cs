using Windows.Foundation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal Size MeasureSurfaceContent(Size constraint, WidgetSurfaceHints? hints)
    {
        var width = Width;
        var height = Height;
        try
        {
            Width = Height = double.NaN;
            Measure(constraint);
            return DesiredSize;
        }
        finally
        {
            Width = width;
            Height = height;
            InvalidateMeasure();
        }
    }
}
