using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Native SVG source with an immediately available semantic fallback.</summary>
internal sealed class WidgetPackageIconView : ContentControl, IDisposable
{
    private readonly WidgetNativePackageIcon icon;
    internal WidgetPackageIconView()
    {
        IsTabStop = false; IsHitTestVisible = false;
        Style = new Style(typeof(ContentControl)) { Setters = { new Setter(FontSizeProperty, 20d) } };
        HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center;
        icon = new(value =>
        {
            if (value is FontIcon glyph) glyph.SetBinding(FontIcon.FontSizeProperty,
                new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(FontSize)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
            Content = value;
            if (value is ImageIcon image) { image.Width = ActualWidth > 0 ? ActualWidth : 20; image.Height = ActualHeight > 0 ? ActualHeight : 20; }
        });
        SizeChanged += (_, args) =>
        {
            if (Content is ImageIcon image)
            {
                image.Width = args.NewSize.Width; image.Height = args.NewSize.Height;
                if (image.Source is SvgImageSource source)
                {
                    var scale = XamlRoot?.RasterizationScale ?? 1;
                    source.RasterizePixelWidth = Math.Clamp(args.NewSize.Width * scale, 1, ProtocolConstants.MaximumPackageIconRasterDimension);
                    source.RasterizePixelHeight = Math.Clamp(args.NewSize.Height * scale, 1, ProtocolConstants.MaximumPackageIconRasterDimension);
                }
            }
        };
    }
    internal void Update(ViewNode node, Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolver,
        string generation, Action<string>? diagnostic) => icon.Update(node.Glyph!.Value, node.PackageIcon,
            node.AccessibilityLabel, resolver, generation, diagnostic);
    public void Dispose() => icon.Dispose();
}

/// <summary>
/// Owns one admitted icon demand. It never reads widget files or constructs URIs.
/// Select and inline icons share the same native source and fallback lifetime.
/// </summary>
internal sealed class WidgetNativePackageIcon : IDisposable
{
    private readonly Action<IconElement> publish;
    private CancellationTokenSource? lifetime;
    private string? identity;
    private bool disposed;
    internal WidgetNativePackageIcon(Action<IconElement> publish) => this.publish = publish;
    internal void Update(WidgetGlyph fallback, WidgetPackageIcon? asset, string? name,
        Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolver, string generation, Action<string>? diagnostic)
    {
        var next = $"{generation}|{fallback}|{asset?.AssetId}|{asset?.ColorMode}|{name}";
        if (disposed || identity == next) return;
        identity = next;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = new();
        var token = lifetime.Token;
        var icon = new FontIcon();
        WidgetGlyphs.Apply(icon, new() { Id = "package-icon", Kind = ViewNodeKind.Icon, Glyph = fallback, AccessibilityLabel = name }, false);
        publish(icon);
        if (asset is null) return;
        if (resolver is null) { diagnostic?.Invoke("package_icon_resolver_unavailable"); return; }
        // SvgImageSource keeps authored color. ThemeTint is an alpha-mask contract,
        // not a license to silently substitute original-color artwork.
        if (asset.ColorMode == WidgetPackageIconColorMode.ThemeTint)
        { diagnostic?.Invoke("package_icon_theme_tint_unavailable"); return; }
        _ = LoadAsync();
        async Task LoadAsync()
        {
            try
            {
                var bytes = (await resolver(asset.AssetId, token)).NormalizedSvg.ToArray();
                token.ThrowIfCancellationRequested();
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer()).AsTask(token); stream.Seek(0);
                var source = new SvgImageSource { RasterizePixelWidth = 64, RasterizePixelHeight = 64 };
                if (await source.SetSourceAsync(stream).AsTask(token) != SvgImageSourceLoadStatus.Success)
                { diagnostic?.Invoke("package_icon_decode_failed"); return; }
                if (disposed || token.IsCancellationRequested || identity != next) return;
                var image = new ImageIcon { Source = source, Width = 20, Height = 20, IsHitTestVisible = false };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, name ?? fallback.ToString());
                publish(image);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { if (!disposed && !token.IsCancellationRequested) diagnostic?.Invoke("package_icon_unavailable"); }
        }
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime?.Cancel(); lifetime?.Dispose(); }
}
