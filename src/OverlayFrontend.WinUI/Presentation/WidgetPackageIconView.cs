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
            UpdateSizing();
        }, () => XamlRoot);
        SizeChanged += (_, _) => UpdateSizing();
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => UpdateSizing());
    }
    private void UpdateSizing()
    {
        if (Content is not ImageIcon image) return;
        // Keep a stable intrinsic size. Copying the arranged parent size into the
        // child would retain a former explicit size after its style was removed.
        var extent = Math.Clamp(FontSize, 1, 512);
        image.Width = image.Height = extent;
        var size = Math.Min(ActualWidth > 0 ? ActualWidth : extent, ActualHeight > 0 ? ActualHeight : extent);
        var factor = size / extent;
        image.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
        if (image.RenderTransform is ScaleTransform transform) transform.ScaleX = transform.ScaleY = factor;
        else image.RenderTransform = new ScaleTransform { ScaleX = factor, ScaleY = factor };
        NativePackageIconTint.For(image)?.RefreshSize(size);
        if (image.Source is SvgImageSource source)
        {
            var pixels = Math.Clamp(size * (XamlRoot?.RasterizationScale ?? 1), 1, ProtocolConstants.MaximumPackageIconRasterDimension);
            source.RasterizePixelWidth = source.RasterizePixelHeight = pixels;
        }
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
    private readonly Func<XamlRoot?> rasterRoot;
    private NativePackageIconTint? tint;
    private CancellationTokenSource? lifetime;
    private string? identity;
    private bool disposed;
    internal WidgetNativePackageIcon(Action<IconElement> publish, Func<XamlRoot?> rasterRoot)
    { this.publish = publish; this.rasterRoot = rasterRoot; }
    internal void Update(WidgetGlyph fallback, WidgetPackageIcon? asset, string? name,
        Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolver, string generation, Action<string>? diagnostic)
    {
        var next = $"{generation}|{fallback}|{asset?.AssetId}|{asset?.ColorMode}|{name}";
        if (disposed || identity == next) return;
        identity = next;
        tint?.Dispose(); tint = null;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = new();
        var token = lifetime.Token;
        var icon = new FontIcon();
        WidgetGlyphs.Apply(icon, new() { Id = "package-icon", Kind = ViewNodeKind.Icon, Glyph = fallback, AccessibilityLabel = name }, false);
        publish(icon);
        if (asset is null) return;
        if (resolver is null) { diagnostic?.Invoke("package_icon_resolver_unavailable"); return; }
        _ = LoadAsync();
        async Task LoadAsync()
        {
            try
            {
                var payload = await resolver(asset.AssetId, token);
                if (asset.ColorMode == WidgetPackageIconColorMode.ThemeTint)
                {
                    await WaitLoadedAsync(icon, token);
                    var root = rasterRoot() ?? throw new InvalidOperationException("Icon raster root is unavailable.");
                    var cache = NativePackageIconTintCache.For(root);
                    var lease = await cache.AcquireAsync(payload, 128, token);
                    if (disposed || token.IsCancellationRequested || identity != next) { lease.Dispose(); return; }
                    var tinted = new ImageIcon { Width = 20, Height = 20, IsHitTestVisible = false };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tinted, name ?? fallback.ToString());
                    tint = new(tinted, lease, cache, payload, root);
                    publish(tinted);
                    return;
                }
                var bytes = payload.NormalizedSvg.ToArray();
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
    private static async Task WaitLoadedAsync(FrameworkElement element, CancellationToken token)
    {
        if (element.IsLoaded && element.XamlRoot is not null) return;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Loaded(object sender, RoutedEventArgs args) => ready.TrySetResult();
        element.Loaded += Loaded;
        try { if (!element.IsLoaded) await ready.Task.WaitAsync(TimeSpan.FromSeconds(3), token); }
        finally { element.Loaded -= Loaded; }
    }
    public void Dispose() { if (disposed) return; disposed = true; lifetime?.Cancel(); lifetime?.Dispose(); tint?.Dispose(); tint = null; }
}
