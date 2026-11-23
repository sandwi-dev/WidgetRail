using System.Text.Json;
using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task StaticArtworkPaintAsync(string output)
    {
        var first = new WidgetViewPresenter { Width = 120, Height = 100 };
        var next = new WidgetViewPresenter { Width = 120, Height = 100, ArtworkAuthorityCurrent = () => false };
        host.Children.Add(first); host.Children.Add(next);
        Exception? failure = null;
        try
        {
            var uri = "data:image/png;base64," + Convert.ToBase64String(await ArtworkPngAsync(32, 32));
            var different = "data:image/png;base64," + Convert.ToBase64String(await ArtworkPngAsync(48, 32));
            var node = new ViewNode { Id = "photo", Kind = ViewNodeKind.Image, ImageSource = uri, ImageFit = ImageFit.Cover, AccessibilityLabel = "Generated portrait" };
            var styles = new Dictionary<string, BridgeNodeRenderStyles> { [node.Id] = Compute("#photo { width:48px; height:48px; }", node.Id, "image") };
            WidgetRail.WidgetPresentationSession.WidgetPresentationFrame Frame(string scope, ViewNode image) =>
                CreateFrame(new() { Id = "portrait-root", Kind = ViewNodeKind.Stack, InputScopeId = scope, Children = (ViewNode[])[image] }, styles, scope);
            first.Apply(Frame("first", node));
            await Wait(() => first.CaptureStaticArtworkPaint().Count == 1);
            var paint = first.CaptureStaticArtworkPaint();
            var bitmap = paint.Single().Image;
            Check(!bitmap.IsAnimatedBitmap && bitmap.PixelWidth > 0, "Static portrait handoff starts with actually decoded native pixels");
            next.Apply(Frame("second", node));
            next.SeedStaticArtworkPaint(paint);
            Check(ReferenceEquals(next.CaptureStaticArtworkPaint().Single().Image, bitmap),
                "A new view paints the exact decoded image without resource authority or another decode");
            await first.DisposeAsync(); host.Children.Remove(first);
            Check(ReferenceEquals(next.CaptureStaticArtworkPaint().Single().Image, bitmap),
                "Retiring the original view does not invalidate immutable static paint");
            next.Apply(Frame("third", node));
            Check(ReferenceEquals(next.CaptureStaticArtworkPaint().Single().Image, bitmap),
                "Fresh-scope row rebinding retains the seeded image until its own artwork arrives");
            next.Apply(Frame("third", node with { ImageSource = different }));
            Check(next.CaptureStaticArtworkPaint().Count == 0,
                "Old pixels awaiting replacement cannot be exported under the new source identity");
            next.Apply(Frame("fourth", node with { ImageSource = different }));
            next.SeedStaticArtworkPaint(paint);
            Check(Walk(next).OfType<WidgetArtworkView>().Single().Source is null,
                "Different image identity never inherits the old portrait across scopes");
            next.Apply(Frame("fifth", node));
            var animation = await NativeArtworkDecoder.DecodeAsync(new(TwoFrameGif()), new(32, 32), ImageFit.Contain,
                CancellationToken.None, autoPlay: false);
            Check(animation?.Image.IsAnimatedBitmap == true, "Animation exclusion uses a real native animated bitmap");
            next.SeedStaticArtworkPaint(new[] { paint.Single() with { Image = animation!.Image } });
            Check(Walk(next).OfType<WidgetArtworkView>().Single().Source is null,
                "Static paint transfer cannot borrow an animation or its playback owner");
            next.SeedStaticArtworkPaint(paint);
            var replacement = Frame("sixth", node);
            next.Apply(replacement with { Authority = replacement.Authority with { WorkerRun = new(1, 2) } });
            Check(Walk(next).OfType<WidgetArtworkView>().Single().Source is null,
                "A replacement worker cannot inherit the retired worker's static paint");
        }
        catch (Exception error) { failure = error; }
        finally
        {
            await first.DisposeAsync(); await next.DisposeAsync(); host.Children.Remove(first); host.Children.Remove(next);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }));
        }
        if (failure is not null) throw failure;
        static IEnumerable<DependencyObject> Walk(DependencyObject value)
        {
            yield return value;
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(value); ++index)
                foreach (var child in Walk(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(value, index))) yield return child;
        }
    }
}
