using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task BackgroundArtworkStylesAsync()
    {
        var page = new WidgetViewPresenter { Width = 120, Height = 120 };
        host.Children.Add(page);
        var requests = 0;
        var bytes = await ArtworkBandsPngAsync();
        page.ResolveArtworkAsync = (_, _) =>
        { ++requests; return Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, bytes)); };
        page.ArtworkGeneration = "background-style-fixture";
        var node = new ViewNode { Id = "styled-background", Kind = ViewNodeKind.BackgroundSurface,
            ArtworkHandle = "bands", ImageFit = ImageFit.Cover, RetainLastPresentation = true,
            Children = new ViewNode[] { new() { Id = "background-content", Kind = ViewNodeKind.Stack } } };
        var frame = CreateFrame(node, new Dictionary<string, BridgeNodeRenderStyles>());
        var revision = 0;
        try
        {
            Apply("object-position: left;");
            await Wait(() => Find<WidgetPresentationSurface>("Widget.styled-background", page)?.ArtworkSource is BitmapImage);
            var surface = Find<WidgetPresentationSurface>("Widget.styled-background", page)!;
            page.UpdateLayout(); await Task.Delay(100);
            var source = surface.ArtworkSource;
            var priorRequests = requests;
            var left = await SampleAsync(surface, .5, .2);
            Check(left.R > 245 && left.G < 10 && left.B < 10, "background cover left paints left source pixels");
            Apply("object-position: right;");
            await Wait(() => Brushes(surface).All(brush => brush.AlignmentX == AlignmentX.Right));
            var right = await SampleAsync(surface, .5, .2);
            Check(right.B > 245 && right.R < 10 && right.G < 10, "background cover right paints right source pixels");
            foreach (var (position, x, y) in new[] {
                ("top-left", AlignmentX.Left, AlignmentY.Top), ("top", AlignmentX.Center, AlignmentY.Top),
                ("top-right", AlignmentX.Right, AlignmentY.Top), ("left", AlignmentX.Left, AlignmentY.Center),
                ("center", AlignmentX.Center, AlignmentY.Center), ("right", AlignmentX.Right, AlignmentY.Center),
                ("bottom-left", AlignmentX.Left, AlignmentY.Bottom), ("bottom", AlignmentX.Center, AlignmentY.Bottom),
                ("bottom-right", AlignmentX.Right, AlignmentY.Bottom) })
            {
                Apply($"object-position: {position};");
                await Wait(() => Brushes(surface).All(brush => brush.AlignmentX == x && brush.AlignmentY == y));
                Check(true, position + " maps both retained crossfade layers to native brush alignment");
            }
            Apply("object-position: left; image-tint: rgba(0, 0, 0, 0.5); scrim-color: rgba(0, 0, 0, 0.5);");
            await Wait(() => Brushes(surface).All(brush => brush.AlignmentX == AlignmentX.Left));
            var top = await SampleAsync(surface, .5, .2);
            var bottom = await SampleAsync(surface, .5, .8);
            Check(top.R is >= 120 and <= 135 && top.G < 10 && top.B < 10,
                "image tint composites once over decoded background pixels");
            Check(bottom.R is >= 58 and <= 70 && bottom.G < 10 && bottom.B < 10,
                "scrim composites only the bottom 45 percent after tint");
            Check(surface.Opacity == 1 && ReferenceEquals(source, surface.ArtworkSource) && requests == priorRequests,
                "background style changes retain decoded pixels without refetching or fading the entire content surface");
            Apply("");
            await Wait(() => Brushes(surface).All(brush => brush.AlignmentX == AlignmentX.Center && brush.AlignmentY == AlignmentY.Center));
            var reset = await SampleAsync(surface, .5, .8);
            Check(reset.G > 245 && reset.R < 10 && reset.B < 10,
                "removing artwork styles restores center placement and removes tint and scrim");

            // Empty selected sources may retain pixels; their style still follows
            // current appearance rather than the last nonempty artwork request.
            frame = CreateFrame(node with { ArtworkHandle = null, ImageFit = null }, new Dictionary<string, BridgeNodeRenderStyles>());
            Apply("object-position: left; image-tint: rgba(0, 0, 0, 0.5);");
            await Wait(() => Brushes(surface).All(brush => brush.AlignmentX == AlignmentX.Left));
            var retained = await SampleAsync(surface, .5, .2);
            Check(ReferenceEquals(source, surface.ArtworkSource) && retained.R is >= 120 and <= 135,
                "retained background with no new source still receives current theme styles");
        }
        finally { host.Children.Remove(page); await page.DisposeAsync(); }

        void Apply(string style) => page.Apply(frame with { AppearanceRevision = ++revision,
            RenderStyles = new Dictionary<string, BridgeNodeRenderStyles>
                { [node.Id] = Compute("* { " + style + " }", node.Id, "backgroundSurface") } });
        static IEnumerable<ImageBrush> Brushes(WidgetPresentationSurface surface) =>
            surface.ArtworkMotion.View.Children.OfType<Border>().Select(layer => (ImageBrush)layer.Background);
    }

    private static async Task<byte[]> ArtworkBandsPngAsync()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        var pixels = new byte[360 * 120 * 4];
        for (var y = 0; y < 120; ++y)
            for (var x = 0; x < 360; ++x)
            {
                var offset = (y * 360 + x) * 4;
                pixels[offset + 2 - x / 120] = 255; pixels[offset + 3] = 255;
            }
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, 360, 120, 96, 96, pixels);
        await encoder.FlushAsync(); stream.Seek(0);
        var bytes = new byte[checked((int)stream.Size)];
        await stream.ReadAsync(bytes.AsBuffer(), (uint)bytes.Length, InputStreamOptions.None);
        return bytes;
    }

    private static async Task<Color> SampleAsync(FrameworkElement surface, double x, double y)
    {
        surface.UpdateLayout();
        var raster = new RenderTargetBitmap(); await raster.RenderAsync(surface);
        var data = (await raster.GetPixelsAsync()).ToArray();
        var offset = ((int)(raster.PixelHeight * y) * raster.PixelWidth + (int)(raster.PixelWidth * x)) * 4;
        return Color.FromArgb(data[offset + 3], data[offset + 2], data[offset + 1], data[offset]);
    }
}
