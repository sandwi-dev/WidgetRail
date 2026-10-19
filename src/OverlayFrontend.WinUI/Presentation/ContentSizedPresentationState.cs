namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>One measurement per opened source and geometry. Late content updates cannot
/// move a reader's panel; reopening, resizing or changing text size starts a new measure.</summary>
internal sealed class ContentSizedPresentationState
{
    private (double Width, double Minimum, double Maximum, double TextScale)? geometry;
    private double height;

    internal double Resolve(double width, double minimum, double maximum, double textScale, Func<double> measure)
    {
        // Opening at zero size is transient. Do not cache it as the initial content height.
        if (!double.IsFinite(width) || !double.IsFinite(maximum) || width <= 0 || maximum <= 0) return 0;
        minimum = Math.Clamp(double.IsFinite(minimum) ? minimum : 96, 0, maximum);
        var next = (width, minimum, maximum, textScale);
        if (geometry == next) return height;
        var desired = measure();
        height = Math.Clamp(double.IsFinite(desired) ? desired : maximum, minimum, maximum);
        geometry = next;
        return height;
    }

    internal void Reset() => geometry = null;
}
