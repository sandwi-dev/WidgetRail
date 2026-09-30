using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task OrdinaryArtworkStylesAsync()
    {
        var bytes = await ArtworkBandsPngAsync();
        var page = new WidgetViewPresenter { Width = 120, Height = 120 };
        var requests = 0;
        page.ResolveArtworkAsync = (_, _) =>
        { ++requests; return Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, bytes)); };
        page.ArtworkGeneration = "ordinary-styles";
        host.Children.Add(page);
        var node = new ViewNode { Id = "ordinary-art", Kind = ViewNodeKind.Image, ArtworkHandle = "bands",
            ImageFit = ImageFit.Cover, AccessibilityLabel = "Three colored bands" };
        var frame = CreateFrame(node, new Dictionary<string, BridgeNodeRenderStyles>());
        var revision = 0;
        try
        {
            Apply("object-position: left;");
            await Wait(() => Find<WidgetArtworkView>("Widget.ordinary-art", page)?.Source is BitmapImage);
            page.UpdateLayout(); await Task.Delay(100);
            var image = Find<WidgetArtworkView>("Widget.ordinary-art", page)!;
            var source = image.Source;
            var priorRequests = requests;
            var left = await SampleAsync(image, .5, .2);
            Check(left.R > 245 && left.G < 10 && left.B < 10, "ordinary artwork left alignment paints the left crop");
            Apply("object-position: right;");
            var right = await SampleAsync(image, .5, .2);
            Check(right.B > 245 && right.R < 10 && right.G < 10, "ordinary artwork right alignment paints the right crop");
            Apply("object-position: left; image-tint: rgba(0,0,0,0.5); scrim-color: rgba(0,0,0,0.5);");
            var top = await SampleAsync(image, .5, .2);
            var bottom = await SampleAsync(image, .5, .8);
            Check(top.R is >= 120 and <= 135 && bottom.R is >= 58 and <= 70,
                "ordinary artwork shares one tint and bottom scrim with background surfaces");
            Apply("");
            var reset = await SampleAsync(image, .5, .8);
            Check(reset.G > 245 && reset.R < 10 && reset.B < 10 && ReferenceEquals(source, image.Source) && requests == priorRequests,
                "ordinary style removal restores centered untinted pixels without refetching");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(image);
            Check(peer.GetAutomationControlType() == AutomationControlType.Image && peer.GetName() == node.AccessibilityLabel &&
                peer.GetChildren() is null or { Count: 0 }, "artwork exposes one named image peer with no decorative children");

            foreach (var stretch in new[] { Stretch.Fill, Stretch.Uniform, Stretch.UniformToFill, Stretch.None })
            {
                var native = new Image { Source = source, Stretch = stretch };
                var view = new WidgetArtworkView { Source = source };
                view.ApplyStyle(new(stretch));
                foreach (var constraint in new[] { new Windows.Foundation.Size(120, 120), new(180, double.PositiveInfinity),
                    new(double.PositiveInfinity, 90), new(double.PositiveInfinity, double.PositiveInfinity) })
                {
                    native.Measure(constraint); view.Measure(constraint);
                    Check(native.DesiredSize == view.DesiredSize, $"artwork delegates {stretch} intrinsic measurement under {constraint} to WinUI");
                }
                view.Source = null;
            }
            // Letterboxing is placed by the brush, not by moving the element.
            Apply("object-fit: contain; object-position: bottom;");
            image.UpdateLayout();
            var empty = await SampleAsync(image, .5, .1);
            var ink = await SampleAsync(image, .5, .9);
            Check(empty.A == 0 && ink.G > 245, "contained artwork aligns its letterbox to the bottom without changing its element bounds");
            Check(Math.Abs(image.ActualWidth - 120) < 1 && Math.Abs(image.ActualHeight - 120) < 1,
                "changing artwork fit retains the authored image viewport");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        void Apply(string style) => page.Apply(frame with { AppearanceRevision = ++revision,
            RenderStyles = new Dictionary<string, BridgeNodeRenderStyles>
                { [node.Id] = Compute("* { " + style + " }", node.Id, "image") } });
    }
}
