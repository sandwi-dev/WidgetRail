using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A single native Grid cell: full-bleed artwork beneath bottom-aligned copy.</summary>
internal sealed class WidgetPosterPanel : Grid
{
    private readonly RectangleGeometry artworkClip = new();
    internal double AspectRatio { get; set; } = 2d / 3;
    internal WidgetPosterPanel()
    {
        Clip = artworkClip;
        SizeChanged += (_, _) => artworkClip.Rect = new(0, 0, ActualWidth, ActualHeight);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // A rail measures its children with unbounded width. Its item contract or
        // authored width normally constrains it; the SDK poster default is 150 DIPs.
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 150;
        var height = Math.Min(availableSize.Height, width / AspectRatio);
        base.MeasureOverride(new(width, height));
        return new(width, height);
    }
}
