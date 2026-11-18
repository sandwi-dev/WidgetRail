using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native authored tracks with an optional width-derived viewport.</summary>
internal sealed partial class WidgetExplicitGrid : Grid
{
    private double? aspectRatio;

    internal void SetAspectRatio(double? value)
    {
        var next = value is > 0 && double.IsFinite(value.Value) ? value : null;
        if (next == aspectRatio) return;
        aspectRatio = next;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // An explicit height keeps native Grid sizing. Without a bounded width,
        // there is no width-derived viewport; ordinary native measurement wins.
        if (aspectRatio is not { } ratio || !double.IsFinite(availableSize.Width) || !double.IsNaN(Height))
            return base.MeasureOverride(availableSize);

        var size = new Size(availableSize.Width, Math.Min(availableSize.Height, availableSize.Width / ratio));
        // Star tracks receive this finite viewport before intrinsic media decode.
        // Auto tracks retain native content-sizing semantics, so authors should
        // use a Star cell for media that must fit the declared aspect ratio.
        base.MeasureOverride(size);
        return size;
    }
}
