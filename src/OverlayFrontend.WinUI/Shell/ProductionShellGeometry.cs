using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal readonly record struct RailGeometry(double TileSize, int VisibleCount, double ViewportWidth,
    double StatusWidth, double GuideWidth, bool Overflow);
internal readonly record struct ShellBands(double ContentHeight, double ContentGap, double GuideHeight, double GuideGap, double RailHeight);

/// <summary>Host shell capacity only; native Grid/ListView still measure and arrange all content.</summary>
internal static class ProductionShellGeometry
{
    internal const double GuideHeight = 58;
    internal const double GuideRailGap = 46;
    internal const double RailHeight = 76;
    internal const double ContentGuideGap = 3;
    internal const double ReservedHeight = GuideHeight + GuideRailGap + RailHeight + ContentGuideGap;
    internal static ShellBands Bands(double availableHeight)
    {
        var height = double.IsFinite(availableHeight) ? Math.Max(0, availableHeight) : 0;
        var rail = Math.Min(RailHeight, height);
        var guide = Math.Min(GuideHeight, height - rail);
        var guideGap = Math.Min(GuideRailGap, height - rail - guide);
        var contentGap = Math.Min(ContentGuideGap, height - rail - guide - guideGap);
        return new(Math.Max(0, height - rail - guide - guideGap - contentGap), contentGap, guide, guideGap, rail);
    }
    internal static RailGeometry Rail(double availableWidth, int count, bool centered = true)
    {
        var width = double.IsFinite(availableWidth) ? Math.Max(1, availableWidth) : 1;
        count = Math.Max(0, count);
        var capacity = Math.Min(width, Math.Max(width * .6, 192));
        var allTile = count > 0 ? (capacity - count * 14) / count : 64;
        var tile = allTile >= 44 ? Math.Min(64, allTile) : 64;
        var overflow = count * (tile + 14) > capacity && capacity >= 132;
        var inner = Math.Max(1, capacity - (overflow ? 84 : 0));
        var visible = Math.Min(count, Math.Max(1, (int)Math.Floor(inner / (tile + 14))));
        if (inner < tile + 14) tile = Math.Max(1, inner - 14);
        var viewport = Math.Min(inner, visible * (tile + 14));
        var spare = width - viewport - (overflow ? 84 : 0);
        var statusSpace = Math.Max(0, spare - 28) / (centered ? 2 : 1);
        var status = statusSpace >= 188 ? 188d : statusSpace >= 100 ? 100d : 0;
        var railEnvelope = viewport + (overflow ? 84 : 0) + (status > 0 ? status * (centered ? 2 : 1) + 28 : 0);
        return new(tile, visible, viewport, status, Math.Min(width, Math.Max(880, railEnvelope)), overflow);
    }
}
