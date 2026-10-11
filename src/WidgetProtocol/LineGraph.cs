namespace WidgetRail.WidgetProtocol;

/// <summary>A sample in graph coordinates. A null value breaks the line.</summary>
public sealed record GraphPoint(double X, double? Value);

/// <summary>One informational series, with explicit horizontal bounds and optional automatic vertical maximum.</summary>
public sealed record LineGraphDefinition(IReadOnlyList<GraphPoint> Points,
    double XMinimum, double XMaximum, double Minimum = 0, double? Maximum = null, string Unit = "")
{
    public const int MaximumPoints = 120;
    public bool IsWellFormed()
    {
        if (Points is null || Points.Count > MaximumPoints || !Finite(XMinimum) || !Finite(XMaximum) ||
            XMaximum <= XMinimum || !Finite(XMaximum - XMinimum) || !Finite(Minimum) ||
            Maximum is { } max && (!Finite(max) || max <= Minimum || !Finite(max - Minimum)) ||
            Unit is null || Unit.Length > 24 || Unit.Any(char.IsControl)) return false;
        var previous = double.NegativeInfinity;
        foreach (var point in Points)
        {
            if (point is null || !Finite(point.X) || point.X < XMinimum || point.X > XMaximum || point.X <= previous ||
                point.Value is { } value && (!Finite(value) || value < Minimum || Maximum is { } ceiling && value > ceiling)) return false;
            previous = point.X;
        }
        return true;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public double EffectiveMaximum => Maximum ?? Minimum + Math.Max(1,
        (Points.Where(p => p.Value.HasValue).Select(p => p.Value!.Value).DefaultIfEmpty(Minimum).Max() - Minimum) * 1.1);

    private static bool Finite(double value) => double.IsFinite(value) && Math.Abs(value) <= 1e12;
}
