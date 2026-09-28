using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>One image consumer's size/lifetime demand; native codecs own decode work.</summary>
internal sealed class NativeArtworkDemand : IDisposable
{
    private readonly FrameworkElement target;
    private readonly Func<bool> admitted;
    private readonly Func<CancellationToken, Task<NativeArtworkPayload?>> resolve;
    private readonly Action<ImageSource?> publish;
    private readonly Action<bool> completed;
    private readonly Action<Task> observe;
    private readonly Action<Exception> failed;
    private readonly CancellationToken lifetime;
    private readonly CancellationTokenRegistration cancellation;
    private CancellationTokenSource? request;
    private XamlRoot? root;
    private readonly List<(Shell.OverlayScaleRoot Root, long Token)> zoomRoots = [];
    private ArtworkPixelSize? requested;
    private ArtworkPixelSize? sourceSize;
    private ArtworkPixelSize? decodedSize;
    private ImageFit? decodedFit;
    private ImageFit? fit;
    private bool queued;
    private bool disposed;
    internal NativeArtworkDecode? LastDecode { get; private set; }

    internal NativeArtworkDemand(FrameworkElement target, ImageFit? fit, Func<bool> admitted,
        Func<CancellationToken, Task<NativeArtworkPayload?>> resolve, Action<ImageSource?> publish,
        Action<bool> completed, Action<Task> observe, Action<Exception> failed, CancellationToken lifetime)
    {
        this.target = target; this.fit = fit; this.admitted = admitted; this.resolve = resolve;
        this.publish = publish; this.completed = completed; this.observe = observe; this.failed = failed; this.lifetime = lifetime;
        target.Loaded += Loaded;
        target.SizeChanged += SizeChanged;
        cancellation = lifetime.Register(() =>
        {
            if (target.DispatcherQueue.HasThreadAccess) Dispose();
            else target.DispatcherQueue.TryEnqueue(Dispose);
        });
        Refresh(fit);
    }
    internal void Refresh(ImageFit? value)
    {
        if (disposed) return;
        if (fit != value) { fit = value; requested = null; }
        if (queued) return;
        queued = true;
        if (!target.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        { queued = false; if (!disposed) ApplySize(); })) queued = false;
    }
    internal void RefreshSize() => Refresh(fit);
    private void Loaded(object sender, RoutedEventArgs args) => Refresh(fit);
    private void SizeChanged(object sender, SizeChangedEventArgs args) => Refresh(fit);
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Refresh(fit);

    private void ApplySize()
    {
        // Resource acquisition does not require Loaded. WinUI can have an attached,
        // arranged ContentControl whose IsLoaded flag has not caught up yet, with
        // no later size notification. Ownership plus XamlRoot establish admission;
        // source/lifecycle cancellation still rejects every stale publication.
        if (!admitted() || target.XamlRoot is not { } nextRoot) return;
        if (!ReferenceEquals(root, nextRoot))
        {
            if (root is not null) root.Changed -= RootChanged;
            root = nextRoot; root.Changed += RootChanged;
        }
        var nextZoom = new List<Shell.OverlayScaleRoot>();
        for (DependencyObject? parent = target; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is FrameworkElement { Visibility: Visibility.Collapsed }) return;
            if (parent is Shell.OverlayScaleRoot zoom) nextZoom.Add(zoom);
        }
        if (!nextZoom.SequenceEqual(zoomRoots.Select(value => value.Root)))
        {
            foreach (var (zoom, token) in zoomRoots) zoom.UnregisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, token);
            zoomRoots.Clear();
            foreach (var zoom in nextZoom) zoomRoots.Add((zoom,
                zoom.RegisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, (_, _) => Refresh(fit))));
        }
        var pixels = TargetPixels(target);
        if (requested == pixels) return;
        requested = pixels;
        if (sourceSize is { } source && decodedSize is { } existing && decodedFit == fit &&
            NativeArtworkDecoder.SizeFor(source, pixels, fit) is { } needed &&
            existing.Width >= needed.Width && existing.Height >= needed.Height)
        {
            request?.Cancel(); request?.Dispose(); request = null;
            completed(true); return;
        }
        request?.Cancel(); request?.Dispose();
        request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        completed(false);
        NativeArtworkCounters.Requested();
        observe(LoadAsync(request, pixels, fit));
    }

    internal static ArtworkPixelSize TargetPixels(FrameworkElement target)
    {
        var width = target.ActualWidth;
        var height = target.ActualHeight;
        var envelope = 1d;
        for (DependencyObject? parent = target; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement element) continue;
            if (NativeComputedStyleAdapter.For(element) is { } style) envelope *= style.ArtworkScaleEnvelope;
            // Layout zoom lives in RenderTransform. Facade/compositor Scale is
            // transient focus, press or entrance motion and must not be sampled:
            // its declared maximum is already represented by the envelope.
            if (element.RenderTransform is { } transform)
            {
                var zoomed = transform.TransformBounds(new Rect(0, 0, width, height));
                width = zoomed.Width; height = zoomed.Height;
            }
        }
        var scale = (target.XamlRoot?.RasterizationScale ?? 1) * envelope;
        return new(Bucket(width * scale), Bucket(height * scale));
        static int Bucket(double value) => !double.IsFinite(value) || value <= 0 ? 0 :
            (int)Math.Min(ProtocolConstants.MaximumEncodedArtworkDimension, Math.Ceiling(value / 32) * 32);
    }

    private async Task LoadAsync(CancellationTokenSource owner, ArtworkPixelSize pixels, ImageFit? imageFit)
    {
        var token = owner.Token;
        try
        {
            var payload = await resolve(token);
            token.ThrowIfCancellationRequested();
            var decoded = await NativeArtworkDecoder.DecodeAsync(payload, pixels, imageFit, token);
            if (disposed || token.IsCancellationRequested || !ReferenceEquals(request, owner) || !admitted()) return;
            sourceSize = decoded?.Source; decodedSize = decoded?.Decoded; decodedFit = imageFit; LastDecode = decoded;
            completed(true);
            NativeArtworkCounters.Completed();
            publish(decoded?.Image);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { NativeArtworkCounters.Cancelled(); }
        catch (Exception error)
        {
            if (disposed || token.IsCancellationRequested || !ReferenceEquals(request, owner)) return;
            completed(true); NativeArtworkCounters.Failed(); failed(error);
        }
        finally
        {
            if (ReferenceEquals(request, owner)) { request = null; owner.Dispose(); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        target.Loaded -= Loaded; target.SizeChanged -= SizeChanged;
        if (root is not null) root.Changed -= RootChanged;
        foreach (var (zoom, token) in zoomRoots) zoom.UnregisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, token);
        zoomRoots.Clear();
        request?.Cancel(); request?.Dispose(); request = null;
        cancellation.Unregister();
        LastDecode = null;
    }
}
