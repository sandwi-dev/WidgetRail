namespace GameBarAlternative.WidgetSdk;

/// <summary>Deterministic range stepping anchored at the slider minimum.</summary>
public static class SliderMath
{
    public static double Increment(double value, double minimum, double maximum, double step) =>
        Adjust(value, minimum, maximum, step, 1);

    public static double Decrement(double value, double minimum, double maximum, double step) =>
        Adjust(value, minimum, maximum, step, -1);

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

    private static double Adjust(
        double value,
        double minimum,
        double maximum,
        double step,
        int direction)
    {
        if (!double.IsFinite(value) || !double.IsFinite(minimum) ||
            !double.IsFinite(maximum) || !double.IsFinite(step) ||
            minimum >= maximum || value < minimum || value > maximum ||
            step <= 0 || step > maximum - minimum)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Slider stepping requires a finite valid range, in-range value, and positive bounded step.");

        var position = (value - minimum) / step;
        var tolerance = Math.Max(1d, Math.Abs(position)) * 1e-10;
        double index = direction > 0
            ? Math.Floor(position + tolerance) + 1d
            : Math.Ceiling(position - tolerance) - 1d;
        var candidate = minimum + index * step;
        var rangeTolerance = Math.Max(1d, Math.Abs(maximum - minimum)) * 1e-12;
        if (direction > 0 && candidate > maximum - rangeTolerance) return maximum;
        if (direction < 0 && candidate < minimum + rangeTolerance) return minimum;
        return Math.Clamp(candidate, minimum, maximum);
    }
}
