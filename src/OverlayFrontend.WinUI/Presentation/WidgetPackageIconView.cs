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
    private bool rasterQueued, disposed, hostVisible;
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
        Loaded += (_, _) => { ObserveRoot(); icon.RetryTransientFailure(); UpdateSizing(); };
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
        ObserveRoot();
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
    private void ObserveRoot()
    {
        if (XamlRoot is not { } root || ReferenceEquals(rasterRoot, root)) return;
        if (rasterRoot is not null) rasterRoot.Changed -= RasterRootChanged;
        rasterRoot = root; hostVisible = root.IsHostVisible; root.Changed += RasterRootChanged;
    }
    private void RasterRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        var reopened = !hostVisible && sender.IsHostVisible;
        hostVisible = sender.IsHostVisible;
        if (reopened) icon.RetryTransientFailure();
        QueueRasterSize();
    }
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
    internal void RetryTransientFailure() => icon.RetryTransientFailure();
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
    private IconElement? displayed;
    private Func<Task>? retry;
    private bool loading, retryable, succeeded;
    private Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolve;
    private string? accessibleName;
    private bool disposed;
    // Theme-tinted masks retain their square raster contract. Original-color SVGs
    // use their admitted viewBox so wordmarks can fill a rectangular author slot.
    internal double AspectRatio { get; private set; } = 1;
    internal WidgetNativePackageIcon(Action<IconElement> publish, Func<XamlRoot?> rasterRoot)
    { this.publish = publish; this.rasterRoot = rasterRoot; }
    internal void Update(WidgetGlyph fallback, WidgetPackageIcon? asset, string? name,
        Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? resolver, string generation, Action<string>? diagnostic)
    {
        // Labels and semantic fallback changes do not invalidate admitted SVG
        // bytes. Preserve the live native surface on those compatible updates.
        var next = $"{generation}|{asset?.AssetId}|{asset?.ColorMode}|{(asset is null ? fallback.ToString() : string.Empty)}";
        if (disposed) return;
        var resolverBecameAvailable = resolve is null && resolver is not null;
        resolve = resolver;
        accessibleName = name ?? fallback.ToString();
        if (identity == next)
        {
            if (displayed is not null) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(displayed, accessibleName);
            if (displayed is FontIcon placeholder)
                WidgetGlyphs.Apply(placeholder, new() { Id = "package-icon", Kind = ViewNodeKind.Icon, Glyph = fallback, AccessibilityLabel = name }, false);
            if (resolverBecameAvailable) RetryTransientFailure();
            return;
        }
        identity = next;
        retry = null; loading = false; retryable = false; succeeded = false;
        AspectRatio = 1;
        original?.Dispose(); original = null;
        tint?.Dispose(); tint = null;
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = new();
        var token = lifetime.Token;
        var icon = new FontIcon();
        WidgetGlyphs.Apply(icon, new() { Id = "package-icon", Kind = ViewNodeKind.Icon, Glyph = fallback, AccessibilityLabel = name }, false);
        displayed = icon; publish(icon);
        if (asset is null) return;
        retry = LoadAsync;
        _ = LoadAsync();
        async Task LoadAsync()
        {
            if (disposed || loading || succeeded || token.IsCancellationRequested || identity != next) return;
            loading = true; retryable = false;
            try
            {
                for (var attempt = 0; attempt < 4; ++attempt)
                {
                    try { await PrepareAsync(); return; }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        if (disposed || token.IsCancellationRequested || identity != next) return;
                        retryable = IsTransient(error);
                        if (retryable && attempt < 3)
                        {
                            await Task.Delay(150 * (1 << attempt), token);
                            continue;
                        }
                        var code = error is WidgetPresentationSessionException sessionError ? sessionError.Code : "package_icon_unavailable";
                        diagnostic?.Invoke(code);
                        // No payloads, labels, paths or exception messages. Keep
                        // enough detail to distinguish native/authority/capacity failures.
                        Diagnostics.FrontendFailureLog.Current.Write("package-icon", null,
                            $"code={code} asset={asset.AssetId} failure={error.GetType().Name} hresult=0x{error.HResult:X8} attempts={attempt + 1} retryable={retryable}");
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { if (identity == next && !token.IsCancellationRequested) loading = false; }
        }
        async Task PrepareAsync()
        {
            if (resolve is not { } currentResolver)
                throw new WidgetPresentationSessionException("package_icon_resolver_unavailable", "Icon resolver is not ready.");
            var payload = await currentResolver(asset.AssetId, token);
            token.ThrowIfCancellationRequested();
            if (identity != next || disposed) return;
            if (asset.ColorMode == WidgetPackageIconColorMode.ThemeTint)
            {
                await WaitLoadedAsync(icon, token);
                var root = rasterRoot() ?? throw new InvalidOperationException("Icon raster root is unavailable.");
                var cache = NativePackageIconTintCache.For(root);
                var lease = await cache.AcquireAsync(payload, 128, token);
                if (disposed || token.IsCancellationRequested || identity != next) { lease.Dispose(); return; }
                var tinted = new ImageIcon { Width = 20, Height = 20, IsHitTestVisible = false };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tinted, accessibleName);
                try { tint = new(tinted, lease, cache, payload, root); }
                catch { lease.Dispose(); throw; }
                displayed = tinted; publish(tinted); succeeded = true; retryable = false;
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
                throw new InvalidDataException("Native SVG decode failed.");
            if (disposed || token.IsCancellationRequested || identity != next) return;
            AspectRatio = aspect;
            var image = new ImageIcon { Source = source, Width = 20, Height = 20, IsHitTestVisible = false };
            original = new(image, bytes, aspect, token, diagnostic);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(image, accessibleName);
            displayed = image; publish(image); succeeded = true; retryable = false;
        }
    }
    internal void RetryTransientFailure()
    {
        // Reentering presentation rearms one bounded batch; ordinary snapshots
        // do not spin on permanently invalid assets or an exhausted retry batch.
        if (!disposed && retryable && !loading && !succeeded && retry is { } start) _ = start();
    }
    private static bool IsTransient(Exception error) => error switch
    {
        WidgetPresentationSessionException failure => failure.Code is "catalog_stale" or "package_icon_saturated" or "package_icon_resolver_unavailable",
        TimeoutException or OperationCanceledException or System.Runtime.InteropServices.COMException or InvalidOperationException => true,
        _ => false,
    };
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
    public void Dispose() { if (disposed) return; disposed = true; lifetime?.Cancel(); lifetime?.Dispose(); tint?.Dispose(); tint = null; original?.Dispose(); original = null; retry = null; resolve = null; displayed = null; }
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
