using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Previews;

/// <summary>A native layout slot. Capture authority and resources belong to the shell renderer.</summary>
internal sealed class WidgetWindowPreview : ContentControl, IDisposable
{
    private WindowPreviewRenderer? renderer;
    private WindowPreviewSurface? surface;
    private WidgetHostWindowTarget? target;
    private ImageFit fit;
    private double aspectRatio = 16d / 9;

    internal WidgetWindowPreview()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    internal void Configure(WindowPreviewRenderer? nextRenderer, WidgetPresentationFrame frame, ViewNode node)
    {
        var nextTarget = node.WindowId is { } id ? frame.WindowPreviews.GetValueOrDefault(id) : null;
        var nextFit = node.ImageFit ?? ImageFit.Contain;
        var nextRatio = node.PreviewAspectRatio ?? 16d / 9;
        if (aspectRatio != nextRatio) { aspectRatio = nextRatio; InvalidateMeasure(); }
        if (ReferenceEquals(renderer, nextRenderer) && target == nextTarget && fit == nextFit &&
            (surface is not null || nextRenderer is null || nextTarget is null)) return;
        DisposeSurface();
        renderer = nextRenderer; target = nextTarget; fit = nextFit;
        if (renderer is null || target is null) return;
        // A missing declaration or capture-capacity rejection leaves the authored
        // surface visible; it cannot turn a nonessential preview into a widget failure.
        if (renderer.TryCreateSurface(target.WindowId, fit) is { } created)
            Content = surface = created;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width :
            double.IsFinite(availableSize.Height) ? availableSize.Height * aspectRatio : 320;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : width / aspectRatio;
        var size = new Size(Math.Max(0, width), Math.Max(0, height));
        base.MeasureOverride(size);
        return size;
    }

    private void DisposeSurface()
    {
        Content = null;
        if (surface is { } previous) renderer?.RemoveSurface(previous);
        surface = null;
    }

    public void Dispose() { DisposeSurface(); renderer = null; target = null; }
}
