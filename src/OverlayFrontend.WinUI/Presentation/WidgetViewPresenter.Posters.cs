using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private void UpdatePoster(WidgetPosterPanel panel, ViewNode node)
    {
        var ratio = ComputedStyle(node, "aspect-ratio")?.Number;
        var resolved = ratio is > 0 && double.IsFinite(ratio.Value) ? ratio.Value : 2d / 3;
        if (panel.AspectRatio != resolved) { panel.AspectRatio = resolved; panel.InvalidateMeasure(); }
        foreach (var child in node.Children)
        {
            // The wrapper owns authored dimensions and is the direct Grid child.
            // Aligning only its content centers a fixed-height badge's wrapper.
            var element = bindings[child.Id].LayoutElement;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            element.VerticalAlignment = child.Kind == ViewNodeKind.Image ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
            Grid.SetRow(element, 0); Grid.SetColumn(element, 0);
        }
    }
}
