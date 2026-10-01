using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Motion;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task PosterArtworkRevealAsync()
    {
        var page = new WidgetViewPresenter { Width = 160, Height = 220 };
        page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Full }, true);
        host.Children.Add(page);
        var bytes = await ArtworkPngAsync(160, 220);
        page.ResolveArtworkAsync = (_, _) => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, bytes));
        var image = new ViewNode { Id = "reveal.art", Kind = ViewNodeKind.Image, ArtworkHandle = "poster-reveal", ImageFit = ImageFit.Cover, AccessibilityLabel = "Poster artwork" };
        var copy = new ViewNode { Id = "reveal.copy", Kind = ViewNodeKind.Stack, Children = (ViewNode[])[new() { Id = "reveal.title", Kind = ViewNodeKind.Text, Text = "Poster" }] };
        var root = new ViewNode { Id = "reveal.poster", Kind = ViewNodeKind.ActionSurface, ActionId = "poster",
            ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical, AccessibilityLabel = "Poster",
            ActionSurfacePresentation = ActionSurfacePresentation.Poster, Children = (ViewNode[])[image, copy] };
        var styles = new Dictionary<string, BridgeNodeRenderStyles>();
        try
        {
            page.Apply(CreateFrame(root, styles));
            await Wait(() => page.ArtworkRevealStarts == 1);
            var nativeImage = Descendants(page).OfType<WidgetArtworkView>().Single();
            var artworkLayer = Descendants(page).OfType<WidgetMotionHost>().Single(value => value.ArtworkLayer is not null).ArtworkLayer!;
            Check(nativeImage.Opacity == 1, "poster loading animates a dedicated layer, preserving authored image opacity");
            await Task.Delay(300);
            Check(ElementCompositionPreview.GetElementVisual(artworkLayer).Opacity == 1, "poster artwork fades fully in without remaining dim");
            page.Apply(CreateFrame(root, styles));
            await Task.Delay(100);
            Check(page.ArtworkRevealStarts == 1, "same poster pixels do not replay entrance on ordinary updates");
            page.Apply(CreateFrame(root with { Children = (ViewNode[])[copy] }, styles));
            page.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, true);
            page.Apply(CreateFrame(root, styles));
            await Wait(() => Descendants(page).OfType<WidgetArtworkView>().FirstOrDefault()?.Source is not null);
            artworkLayer = Descendants(page).OfType<WidgetMotionHost>().Single(value => value.ArtworkLayer is not null).ArtworkLayer!;
            Check(page.ArtworkRevealStarts == 0 && ElementCompositionPreview.GetElementVisual(artworkLayer).Opacity == 1,
                "reduced motion loads poster pixels immediately without fading");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }
}
