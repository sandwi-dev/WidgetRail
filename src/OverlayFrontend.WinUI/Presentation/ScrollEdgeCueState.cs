namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Offset-only affordances: deliberately independent of scroll progress.</summary>
internal readonly record struct ScrollEdgeCueState(bool Above, bool Below)
{
    internal const double BoundaryTolerance = .5;
    internal static double Width(double viewportWidth) => !double.IsFinite(viewportWidth) || viewportWidth <= 0
        ? 0 : Math.Min(viewportWidth, Math.Clamp(viewportWidth * .08, 48, 80));
    internal static ScrollEdgeCueState From(double offset, double extent, double viewport, bool verticalEnabled)
    {
        if (!verticalEnabled || !double.IsFinite(offset) || !double.IsFinite(extent) ||
            !double.IsFinite(viewport) || viewport <= 0 || extent <= viewport + BoundaryTolerance)
            return default;
        var maximum = extent - viewport;
        offset = Math.Clamp(offset, 0, maximum);
        return new(offset > BoundaryTolerance, maximum - offset > BoundaryTolerance);
    }
}
