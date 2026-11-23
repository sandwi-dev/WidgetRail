using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Motion;

internal sealed record WallpaperMotionStyle(string Transition, double FadeMs, Vector2 Travel,
    bool PanZoom, float FromScale, float ToScale, double DriftMs, Vector2 Origin)
{
    internal static readonly WallpaperMotionStyle Default = new("fade", 400, new(.005f, .0025f),
        false, 1.03f, 1.01f, 9000, new(.75f, .5f));

    private static readonly ConditionalWeakTable<IReadOnlyDictionary<string, BridgeComputedStyleValue>, WallpaperMotionStyle> cache = new();
    internal static WallpaperMotionStyle Read(IReadOnlyDictionary<string, BridgeComputedStyleValue>? values) =>
        values is null ? Default : cache.GetValue(values, Parse);

    private static WallpaperMotionStyle Parse(IReadOnlyDictionary<string, BridgeComputedStyleValue> values)
    {
        if (!values.Keys.Any(key => key.StartsWith("artwork-", StringComparison.Ordinal))) return Default;
        double Number(string name, double fallback) => values.GetValueOrDefault(name)?.Number ?? fallback;
        var origin = Default.Origin;
        if (values.GetValueOrDefault("artwork-motion-origin")?.Text.Split(' ') is [var x, var y] &&
            float.TryParse(x.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var ox) &&
            float.TryParse(y.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var oy))
            origin = Vector2.Clamp(new(ox / 100, oy / 100), Vector2.Zero, Vector2.One);
        return new(values.GetValueOrDefault("artwork-transition")?.Text ?? "fade",
            Math.Clamp(Number("artwork-transition-duration", 400), 0, 1000),
            new((float)Math.Clamp(Number("artwork-transition-travel-x", .5), 0, 5) / 100,
                (float)Math.Clamp(Number("artwork-transition-travel-y", .25), 0, 5) / 100),
            values.GetValueOrDefault("artwork-motion")?.Text == "pan-zoom",
            (float)Math.Clamp(Number("artwork-motion-scale-from", 1.03), 1, 1.25),
            (float)Math.Clamp(Number("artwork-motion-scale-to", 1.01), 1, 1.25),
            Math.Clamp(Number("artwork-motion-duration", 9000), 0, 30000), origin);
    }
}
