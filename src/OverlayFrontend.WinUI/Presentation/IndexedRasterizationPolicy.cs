namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Bounded quality for WinUI item caches under the host's transformed UI zoom.</summary>
internal static class IndexedRasterizationPolicy
{
    internal const double MaximumScale = 1.5;
    internal const double PixelBudget = 512 * 512;

    internal static double Resolve(double interfaceScale, double displayScale, double width, double height, double original)
    {
        // Respect a native owner's explicit quality setting and avoid allocating
        // additional raster detail at the normal untransformed interface size.
        if (original != 1 || !double.IsFinite(interfaceScale) || interfaceScale <= 0 || Math.Abs(interfaceScale - 1) < .001 ||
            !double.IsFinite(displayScale) || displayScale <= 0 || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return original;
        var pixels = width * height * Math.Pow(displayScale * Math.Max(1, interfaceScale), 2);
        var scale = Math.Min(MaximumScale, Math.Sqrt(PixelBudget / pixels));
        return Math.Max(original, Math.Floor(scale * 100) / 100);
    }
}
