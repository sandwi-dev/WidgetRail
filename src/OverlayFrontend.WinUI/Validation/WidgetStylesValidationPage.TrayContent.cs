using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeTrayContentAsync()
    {
        using var content = new WidgetCatalogItemContent((_, _, _) => throw new InvalidOperationException("No package artwork in this fixture"))
            { TileSize = 64, IconSize = 34, ShowLabel = false };
        var descriptor = new BridgeWidgetDescriptor { Id = "tray", Name = "Tray", InstanceId = "tray",
            RuntimeGeneration = "r", PresentationGeneration = "p", Icon = WidgetGlyph.Music, PackageContentDigest = "" };
        var container = new ListViewItem { Width = 64, Height = 64, Padding = new(0), IsSelected = true, Content = content,
            DataContext = descriptor, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        host.Children.Add(container);
        try
        {
            await Wait(() => ScrollParts(content).OfType<WidgetPackageIconView>().Any(value => value.ActualHeight > 0));
            foreach (var size in new[] { 64d, 44d })
            {
                content.TileSize = size; container.Width = container.Height = size; content.UpdateLayout();
                var icon = ScrollParts(content).OfType<WidgetPackageIconView>().Single();
                var bounds = icon.TransformToVisual(content).TransformBounds(new(0, 0, icon.ActualWidth, icon.ActualHeight));
                var mark = ((Grid)content.Content).Children.OfType<Border>().Single();
                var marker = mark.TransformToVisual(content).TransformBounds(new(0, 0, mark.ActualWidth, mark.ActualHeight));
                Check(Math.Abs(bounds.Top + bounds.Height / 2 - size / 2) < 1 && Math.Abs(bounds.Left + bounds.Width / 2 - size / 2) < 1,
                    "tray icon is centered in its full tile extent");
                Check(Math.Abs(marker.Bottom - size) < 1 && mark.Visibility == Visibility.Visible,
                    "tray selection mark uses the tile bottom instead of the icon bottom");
            }
        }
        finally { host.Children.Remove(container); }
    }
}
