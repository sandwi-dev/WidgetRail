using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Composition;
using System.Numerics;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>A single native Grid cell: full-bleed artwork beneath bottom-aligned copy.</summary>
internal sealed partial class WidgetPosterPanel : Grid
{
    private CompositionRoundedRectangleGeometry? geometry;
    private CompositionGeometricClip? artworkClip;
    private Visual? visual;
    private CornerRadius contentRadius;
    internal double AspectRatio { get; set; } = 2d / 3;
    internal WidgetPosterPanel()
    {
        Loaded += (_, _) => UpdateClip();
        Unloaded += (_, _) => { if (!IsLoaded) ReleaseClip(); };
        SizeChanged += (_, _) => UpdateClip();
    }

    internal void SetContentCornerRadius(CornerRadius radius)
    {
        contentRadius = radius;
        UpdateClip();
    }

    private void UpdateClip()
    {
        if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (visual is null)
        {
            // Clip the artwork/copy body, not the outer control that owns focus,
            // scale and shadow. No control facade visual is replaced or scaled.
            visual = ElementCompositionPreview.GetElementVisual(this);
            geometry = visual.Compositor.CreateRoundedRectangleGeometry();
            artworkClip = visual.Compositor.CreateGeometricClip(geometry);
            visual.Clip = artworkClip;
        }
        geometry!.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        geometry.CornerRadius = new Vector2((float)Math.Clamp(contentRadius.TopLeft, 0, Math.Min(ActualWidth, ActualHeight) / 2));
    }

    private void ReleaseClip()
    {
        if (visual is not null && visual.Clip == artworkClip) visual.Clip = null;
        artworkClip?.Dispose(); geometry?.Dispose();
        visual = null; artworkClip = null; geometry = null;
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
