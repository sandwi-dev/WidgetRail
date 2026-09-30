using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private async Task NativeMediaLayoutsAsync(string directory)
    {
        var files = Directory.GetFiles(directory, "*.renderer.json");
        if (files.Length != 4) throw new InvalidDataException("Provide the four main/compact music renderer fixtures.");
        var scale = XamlRoot.RasterizationScale;
        App.Window.AppWindow.Resize(new((int)(1040 * scale), (int)(840 * scale)));
        presenter.ApplyAppearance(AppearanceSettings.Default with { Motion = MotionPreference.Reduced }, false);
        var artwork = await ArtworkPngAsync(128, 128);
        presenter.ResolveArtworkAsync = (_, _) => Task.FromResult<WidgetEncodedArtwork?>(new(WidgetArtworkContentType.Png, artwork));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        foreach (var path in files.Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            var snapshot = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
            // Preserve every authored layout/style; substitute only the external
            // image transport with a deterministic decoded fixture image.
            snapshot = snapshot with { Root = OfflineImages(snapshot.Root) };
            var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)!;
            var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));
            var descriptor = new BridgeWidgetDescriptor { Id = name, Name = name, InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = name, PresentationGeneration = name, Icon = WidgetGlyph.Music, PackageContentDigest = "" };
            var frame = new WidgetPresentationFrame(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, styles);
            presenter.Width = snapshot.Surface!.PreferredWidth!.Value;
            presenter.Height = snapshot.Surface.PreferredHeight!.Value;
            presenter.Apply(frame);
            var artId = Walk(snapshot.Root).First(node => node.Kind == ViewNodeKind.Image && node.Id.Contains("artwork", StringComparison.Ordinal)).Id;
            await Wait(() => Find<WidgetArtworkView>("Widget." + artId) is { Source: BitmapSource { PixelWidth: > 0 } });
            presenter.UpdateLayout();
            var art = Find<WidgetArtworkView>("Widget." + artId)!;
            var authoredWidth = styles[artId].Base.GetValueOrDefault("width")?.Number ?? 96;
            var authoredHeight = styles[artId].Base.GetValueOrDefault("height")?.Number ?? 96;
            Check(art.ActualWidth >= authoredWidth - 1 && art.ActualHeight >= authoredHeight - 1,
                name + " paints decoded player artwork at its authored size");
            foreach (var height in new[] { presenter.Height, snapshot.Surface.MinimumHeight ?? presenter.Height }.Distinct())
            {
                presenter.Height = height;
                presenter.UpdateLayout();
                foreach (var node in Walk(snapshot.Root).Where(node => node.Kind == ViewNodeKind.Button))
                {
                    if (Find<Button>("Widget." + node.Id) is not { IsLoaded: true, Visibility: Visibility.Visible } button || button.ActualWidth == 0) continue;
                    var bounds = button.TransformToVisual(presenter).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                    Check(bounds.Left >= -1 && bounds.Right <= presenter.ActualWidth + 1,
                        name + " keeps " + node.Id + " inside the horizontal viewport at height " + height);
                }
            }
            presenter.Height = snapshot.Surface.PreferredHeight.Value;
            presenter.UpdateLayout();
            await Task.Delay(80);
            presenter.WriteLayoutDiagnostics(Path.Combine(directory, name + ".layout.json"));
            var raster = new RenderTargetBitmap();
            await raster.RenderAsync(presenter);
            var output = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            var folder = await output.GetParentAsync();
            var png = await folder.CreateFileAsync(name + ".png", CreationCollisionOption.ReplaceExisting);
            using var stream = await png.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)raster.PixelWidth, (uint)raster.PixelHeight,
                96, 96, (await raster.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();
        }
        static IEnumerable<ViewNode> Walk(ViewNode node)
        { yield return node; foreach (var child in node.Children) foreach (var item in Walk(child)) yield return item; }
        static ViewNode OfflineImages(ViewNode node)
        {
            // Collection contents are independent of player/chrome geometry.
            // Preserve the viewport's style/visibility without its worker lease.
            if (node.Kind == ViewNodeKind.IndexedCollection)
                return new() { Id = node.Id, Kind = ViewNodeKind.Scroll, ScrollAxis = WidgetProtocol.ScrollAxis.Vertical,
                    StyleClasses = node.StyleClasses, InputScopeId = node.InputScopeId, VisibleWhen = node.VisibleWhen };
            return node with { Children = node.Children.Select(OfflineImages).ToArray(),
                ImageSource = node.Kind == ViewNodeKind.Image ? null : node.ImageSource,
                ArtworkHandle = node.Kind == ViewNodeKind.Image ? "fixture-artwork" : node.ArtworkHandle };
        }
    }
}
