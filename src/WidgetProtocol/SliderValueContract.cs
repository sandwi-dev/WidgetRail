namespace WidgetRail.WidgetProtocol;

/// <summary>Shared absolute slider-value admission for ordinary and leased preview controls.</summary>
public static class SliderValueContract
{
    public static bool IsValidRequestedValue(
        double value,
        double minimum,
        double maximum,
        double step)
    {
        if (!double.IsFinite(value) || !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) || !double.IsFinite(step) ||
            minimum >= maximum || value < minimum || value > maximum ||
            step <= 0 || step > maximum - minimum)
            return false;
        var tolerance = Math.Max(1d, Math.Abs(maximum - minimum)) * 1e-9;
        if (Math.Abs(value - maximum) <= tolerance || Math.Abs(value - minimum) <= tolerance)
            return true;
        var position = (value - minimum) / step;
        return Math.Abs(position - Math.Round(position)) <= 1e-9 * Math.Max(1d, Math.Abs(position));
    }

}
