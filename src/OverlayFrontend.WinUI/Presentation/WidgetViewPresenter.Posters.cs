using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private void UpdatePoster(WidgetPosterPanel panel, ViewNode node)
    {
        foreach (var child in node.Children)
        {
            var element = bindings[child.Id].Element;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            element.VerticalAlignment = child.Kind == ViewNodeKind.Image ? VerticalAlignment.Stretch : VerticalAlignment.Bottom;
            Grid.SetRow(element, 0); Grid.SetColumn(element, 0);
        }
    }
}
