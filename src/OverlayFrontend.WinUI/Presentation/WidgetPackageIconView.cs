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
internal sealed partial class WidgetPackageIconView : ContentControl, IDisposable
{
    private readonly WidgetNativePackageIcon icon;
    private readonly List<(Shell.OverlayScaleRoot Root, long Token)> zoomRoots = [];
    private XamlRoot? rasterRoot;
    private bool rasterQueued, disposed;
    internal WidgetPackageIconView()
    {
        IsTabStop = false; IsHitTestVisible = false;
        Style = new Style(typeof(ContentControl)) { Setters = { new Setter(FontSizeProperty, 20d) } };
        HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center;
        icon = new(value =>
        {
            if (value is FontIcon glyph) glyph.SetBinding(FontIcon.FontSizeProperty,
                new Microsoft.UI.Xaml.Data.Binding { Source = this, Path = new PropertyPath(nameof(FontSize)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
            // ImageIcon is an icon-font-sized presentation internally, even when
            // its outer bounds stretch. Original-color wordmarks use Image's
            // direct SVG surface; native icon slots and tint masks keep IconElement.
            if (value is ImageIcon { Source: SvgImageSource })
            {
                var image = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
                image.SetBinding(Image.SourceProperty, new Microsoft.UI.Xaml.Data.Binding
                { Source = value, Path = new PropertyPath(nameof(ImageIcon.Source)), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
                Content = image;
            }
            else Content = value;
            UpdateSizing();
        }, () => XamlRoot);
        SizeChanged += (_, _) => UpdateSizing();
        Loaded += (_, _) => UpdateSizing();
        Unloaded += (_, _) => DetachRasterObservers();
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => { InvalidateMeasure(); UpdateSizing(); });
    }
    protected override Windows.Foundation.Size MeasureOverride(Windows.Foundation.Size availableSize)
    {
        if (Content is not Image { Source: SvgImageSource }) return base.MeasureOverride(availableSize);
        // Original-color artwork is arranged at its final size, not enlarged
        // from a font-sized ImageIcon render surface. Its *desired* size remains
        // intrinsic, so removing authored width/height cannot retain a prior slot.
        var extent = Math.Clamp(FontSize, 1, 512);
        var intrinsic = new Windows.Foundation.Size(extent * Math.Min(1, icon.AspectRatio), extent / Math.Max(1, icon.AspectRatio));
        // Let Image measure at the real available viewport as well as arrange
        // there; only the owner's unstyled desired size is intrinsic.
        _ = base.MeasureOverride(availableSize);
        return intrinsic;
    }
    private void UpdateSizing()
    {
        if (disposed) return;
        if (Content is Image)
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            QueueRasterSize();
            return;
        }
        if (Content is not ImageIcon image)
        {
            HorizontalContentAlignment = HorizontalAlignment.Center;
            VerticalContentAlignment = VerticalAlignment.Center;
            return;
        }
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        // Keep a stable intrinsic size. Copying the arranged parent size into the
        // child would retain a former explicit size after its style was removed.
        var extent = Math.Clamp(FontSize, 1, 512);
        var aspect = icon.AspectRatio;
        image.Width = extent * Math.Min(1, aspect);
        image.Height = extent / Math.Max(1, aspect);
        var factor = Math.Min((ActualWidth > 0 ? ActualWidth : image.Width) / image.Width,
            (ActualHeight > 0 ? ActualHeight : image.Height) / image.Height);
        image.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
        if (image.RenderTransform is ScaleTransform transform) transform.ScaleX = transform.ScaleY = factor;
        else image.RenderTransform = new ScaleTransform { ScaleX = factor, ScaleY = factor };
        NativePackageIconTint.For(image)?.RefreshSize(extent * factor);
        QueueRasterSize();
    }
    private void QueueRasterSize()
    {
        if (disposed || rasterQueued) return;
        rasterQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        { rasterQueued = false; if (!disposed) UpdateRasterSize(); })) rasterQueued = false;
    }
    private void UpdateRasterSize()
    {
        if (XamlRoot is not { } root || Content is not (Image or ImageIcon)) return;
        if (!ReferenceEquals(rasterRoot, root))
        {
            if (rasterRoot is not null) rasterRoot.Changed -= RasterRootChanged;
            rasterRoot = root; root.Changed += RasterRootChanged;
        }
        var roots = new List<Shell.OverlayScaleRoot>();
        for (DependencyObject? parent = this; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Shell.OverlayScaleRoot zoom) roots.Add(zoom);
        if (!roots.SequenceEqual(zoomRoots.Select(value => value.Root)))
        {
            foreach (var (zoom, token) in zoomRoots) zoom.UnregisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, token);
            zoomRoots.Clear();
            foreach (var zoom in roots) zoomRoots.Add((zoom,
                zoom.RegisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, (_, _) => QueueRasterSize())));
        }
        // Share artwork's physical-pixel policy: DPI, ancestor layout zoom and
        // declared motion envelopes, without sampling transient animation frames.
        var demand = NativeArtworkDemand.TargetPixels(this);
        var aspect = icon.AspectRatio;
        var needed = aspect >= 1 ? Math.Min(demand.Width, demand.Height * aspect) : Math.Min(demand.Height, demand.Width / aspect);
        var pixels = Math.Clamp(Math.Ceiling(needed / 32) * 32, 1, ProtocolConstants.MaximumPackageIconRasterDimension);
        if (Content is Image)
        {
            icon.RequestOriginalRaster(pixels);
        }
        else if (Content is ImageIcon image) NativePackageIconTint.For(image)?.RefreshSize(pixels / root.RasterizationScale);
    }
    private void RasterRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => QueueRasterSize();
    private void DetachRasterObservers()
    {
        if (rasterRoot is not null) rasterRoot.Changed -= RasterRootChanged;
        rasterRoot = null;
        foreach (var (zoom, token) in zoomRoots) zoom.UnregisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, token);
        zoomRoots.Clear();
    }
    internal void Update(ViewNode node, Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolver,
        string generation, Action<string>? diagnostic)
    {
        icon.Update(node.Glyph!.Value, node.PackageIcon, node.AccessibilityLabel, resolver, generation, diagnostic);
        UpdateSizing();
    }
    public void Dispose() { disposed = true; DetachRasterObservers(); icon.Dispose(); }
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
    private OriginalPackageIconRaster? original;
    private CancellationTokenSource? lifetime;
    private string? identity;
    private bool disposed;
    // Theme-tinted masks retain their square raster contract. Original-color SVGs
    // use their admitted viewBox so wordmarks can fill a rectangular author slot.
    internal double AspectRatio { get; private set; } = 1;
    internal WidgetNativePackageIcon(Action<IconElement> publish, Func<XamlRoot?> rasterRoot)
    { this.publish = publish; this.rasterRoot = rasterRoot; }
    internal void Update(WidgetGlyph fallback, WidgetPackageIcon? asset, string? name,
        Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolver, string generation, Action<string>? diagnostic)
    {
        var next = $"{generation}|{fallback}|{asset?.AssetId}|{asset?.ColorMode}|{name}";
        if (disposed || identity == next) return;
        identity = next;
        AspectRatio = 1;
        original?.Dispose(); original = null;
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
                var aspect = ReadAspectRatio(bytes);
                token.ThrowIfCancellationRequested();
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer()).AsTask(token); stream.Seek(0);
                var source = new SvgImageSource { RasterizePixelWidth = Math.Max(1, 64 * Math.Min(1, aspect)),
                    RasterizePixelHeight = Math.Max(1, 64 / Math.Max(1, aspect)) };
                if (await source.SetSourceAsync(stream).AsTask(token) != SvgImageSourceLoadStatus.Success)
                { diagnostic?.Invoke("package_icon_decode_failed"); return; }
                if (disposed || token.IsCancellationRequested || identity != next) return;
                AspectRatio = aspect;
                var image = new ImageIcon { Source = source, Width = 20, Height = 20, IsHitTestVisible = false };
                original = new(image, bytes, aspect, token, diagnostic);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, name ?? fallback.ToString());
                publish(image);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { if (!disposed && !token.IsCancellationRequested) diagnostic?.Invoke("package_icon_unavailable"); }
        }
    }
    internal void RequestOriginalRaster(double pixels) => original?.Request(pixels);
    private static double ReadAspectRatio(byte[] normalizedSvg)
    {
        using var input = new MemoryStream(normalizedSvg, writable: false);
        using var reader = System.Xml.XmlReader.Create(input, new System.Xml.XmlReaderSettings
        { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
        reader.MoveToContent();
        var viewBox = reader.GetAttribute("viewBox") ?? throw new InvalidDataException("SVG viewBox is missing.");
        var numbers = System.Text.RegularExpressions.Regex.Matches(viewBox,
            @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?");
        if (numbers.Count != 4) throw new InvalidDataException("SVG viewBox is invalid.");
        var width = double.Parse(numbers[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var height = double.Parse(numbers[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        var aspect = width / height;
        if (width <= 0 || height <= 0 || !double.IsFinite(aspect) || aspect <= 0)
            throw new InvalidDataException("SVG viewBox dimensions are invalid.");
        return aspect;
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
    public void Dispose() { if (disposed) return; disposed = true; lifetime?.Cancel(); lifetime?.Dispose(); tint?.Dispose(); tint = null; original?.Dispose(); original = null; }
}

/// <summary>One SVG demand, one visible raster and at most one bounded upgrade.</summary>
internal sealed class OriginalPackageIconRaster(ImageIcon image, byte[] bytes, double aspect,
    CancellationToken token, Action<string>? diagnostic) : IDisposable
{
    private double requested = 64, prepared = 64;
    private bool preparing, disposed;
    internal void Request(double pixels)
    {
        if (disposed) return;
        requested = Math.Max(requested, Math.Clamp(pixels, 1, ProtocolConstants.MaximumPackageIconRasterDimension));
        if (!preparing && requested > prepared) _ = PrepareAsync();
    }
    private async Task PrepareAsync()
    {
        preparing = true;
        try
        {
            while (!disposed && requested > prepared)
            {
                var extent = requested;
                // For SetSourceAsync SVGs, changing RasterizePixelWidth/Height
                // after decoding does not upgrade the stream-backed pixel data.
                // Decode the admitted bytes at the new size, then atomically
                // replace the old source so preparation never blanks the logo.
                var source = new SvgImageSource { RasterizePixelWidth = Math.Max(1, extent * Math.Min(1, aspect)),
                    RasterizePixelHeight = Math.Max(1, extent / Math.Max(1, aspect)) };
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer()).AsTask(token); stream.Seek(0);
                if (await source.SetSourceAsync(stream).AsTask(token) != SvgImageSourceLoadStatus.Success)
                    throw new InvalidDataException("SVG raster upgrade failed.");
                if (disposed || token.IsCancellationRequested) return;
                image.Source = source;
                prepared = extent;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { if (!disposed && !token.IsCancellationRequested) diagnostic?.Invoke("package_icon_raster_resize_failed"); }
        finally { preparing = false; }
    }
    public void Dispose() => disposed = true;
}
