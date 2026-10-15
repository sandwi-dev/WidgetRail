using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>
/// One host-rendered icon. A package SVG is referenced only by its logical
/// manifest asset ID and always carries a semantic glyph fallback.
/// </summary>
public sealed record WidgetIcon
{
    private WidgetIcon(WidgetGlyph fallbackGlyph, WidgetPackageIcon? packageIcon)
    {
        if (!Enum.IsDefined(fallbackGlyph))
            throw new ArgumentOutOfRangeException(nameof(fallbackGlyph));
        FallbackGlyph = fallbackGlyph;
        PackageIcon = packageIcon;
    }

    public WidgetGlyph FallbackGlyph { get; }
    internal WidgetPackageIcon? PackageIcon { get; }

    public static WidgetIcon Glyph(WidgetGlyph glyph) => new(glyph, null);

    /// <summary>Reference a verified catalog icon from a trusted management view.
    /// Ordinary widgets cannot use catalog authority. No target worker is started.</summary>
    public static WidgetIcon CatalogSvg(WidgetCatalogIconSource source, string assetId,
        WidgetPackageIconColorMode colorMode, WidgetGlyph fallbackGlyph)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!WidgetCatalogIconValidation.IsValid(source)) throw new ArgumentException("Invalid catalog identity.", nameof(source));
        var icon = PackageSvg(assetId, colorMode, fallbackGlyph);
        return new(fallbackGlyph, icon.PackageIcon! with { Catalog = source });
    }

    public static WidgetIcon PackageSvg(
        string assetId,
        WidgetPackageIconColorMode colorMode,
        WidgetGlyph fallbackGlyph)
    {
        if (!WidgetManifestValidator.IsPackageIconAssetId(assetId))
            throw new ArgumentException("Package icon asset ID is invalid.", nameof(assetId));
        if (!Enum.IsDefined(colorMode))
            throw new ArgumentOutOfRangeException(nameof(colorMode));
        return new(fallbackGlyph, new WidgetPackageIcon(assetId, colorMode));
    }
}

internal static class WidgetIconMaterializer
{
    internal static WidgetGlyph Glyph(WidgetIcon icon) =>
        (icon ?? throw new ArgumentNullException(nameof(icon))).FallbackGlyph;

    internal static WidgetPackageIcon? PackageIcon(WidgetIcon icon) =>
        (icon ?? throw new ArgumentNullException(nameof(icon))).PackageIcon;

    internal static WidgetIcon? Resolve(WidgetIcon? icon, WidgetGlyph? glyph) =>
        icon ?? (glyph is { } fallback ? WidgetIcon.Glyph(fallback) : null);
}
