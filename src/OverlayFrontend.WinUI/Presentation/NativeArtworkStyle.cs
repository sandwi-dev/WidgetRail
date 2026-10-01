using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native stretch semantics shared by image consumers and decode sizing.</summary>
internal readonly record struct NativeArtworkStyle(Stretch Stretch, AlignmentX AlignmentX = AlignmentX.Center,
    AlignmentY AlignmentY = AlignmentY.Center, Color? Tint = null, Color? Scrim = null)
{
    internal ImageFit DecodeFit => Stretch switch
    {
        Stretch.Uniform => ImageFit.Contain,
        Stretch.Fill => ImageFit.Fill,
        _ => ImageFit.Cover,
    };
    internal bool NaturalSize => Stretch == Stretch.None;

    internal static NativeArtworkStyle Resolve(IReadOnlyDictionary<string, BridgeComputedStyleValue>? style,
        ImageFit? declared, ImageFit fallback)
    {
        var fit = style?.GetValueOrDefault("object-fit")?.Text switch
        {
            "contain" => Stretch.Uniform,
            "cover" => Stretch.UniformToFill,
            "fill" => Stretch.Fill,
            "none" => Stretch.None,
            _ => (declared ?? fallback) switch
            { ImageFit.Contain => Stretch.Uniform, ImageFit.Cover => Stretch.UniformToFill, _ => Stretch.Fill },
        };
        var position = style?.GetValueOrDefault("object-position")?.Text;
        var x = position switch
        { "left" or "top-left" or "bottom-left" => AlignmentX.Left, "right" or "top-right" or "bottom-right" => AlignmentX.Right, _ => AlignmentX.Center };
        var y = position switch
        { "top" or "top-left" or "top-right" => AlignmentY.Top, "bottom" or "bottom-left" or "bottom-right" => AlignmentY.Bottom, _ => AlignmentY.Center };
        return new(fit, x, y, ColorValue("image-tint"), ColorValue("scrim-color"));

        Color? ColorValue(string property) => style?.GetValueOrDefault(property) is { } value &&
            NativeComputedStyleAdapter.TryColor(value.Text, out var color) ? color : null;
    }

}
