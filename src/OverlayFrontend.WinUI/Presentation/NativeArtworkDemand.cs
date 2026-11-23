using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using Windows.Foundation;
using WidgetRail.OverlayFrontend.WinUI.Diagnostics;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>One image consumer's size/lifetime demand; native codecs own decode work.</summary>
internal sealed class NativeArtworkDemand : IDisposable
{
    private readonly FrameworkElement target;
    private readonly Func<bool> admitted;
    private readonly Func<bool> animationAllowed;
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
    private bool naturalSize;
    private bool queued;
    private bool disposed;
    private bool playbackSuspended;
    private NativeAnimatedArtwork? animation;
    internal NativeArtworkDecode? LastDecode { get; private set; }

    internal NativeArtworkDemand(FrameworkElement target, ImageFit? fit, Func<bool> admitted,
        Func<CancellationToken, Task<NativeArtworkPayload?>> resolve, Action<ImageSource?> publish,
        Action<bool> completed, Action<Task> observe, Action<Exception> failed, CancellationToken lifetime, bool naturalSize = false,
        Func<bool>? animationAllowed = null)
    {
        this.target = target; this.fit = fit; this.admitted = admitted; this.resolve = resolve;
        this.animationAllowed = animationAllowed ?? (() => true);
        this.publish = publish; this.completed = completed; this.observe = observe; this.failed = failed; this.lifetime = lifetime;
        // Ordinary image cells use a fresh viewport target only if the decoded
        // bitmap animates. Media elements with a persistent motion owner keep
        // their early native observation for delayed source replacement.
        if (target is not WidgetArtworkView) NativeMotionVisibility.Prepare(target);
        target.Loaded += Loaded;
        target.SizeChanged += SizeChanged;
        cancellation = lifetime.Register(() =>
        {
            if (target.DispatcherQueue.HasThreadAccess) Dispose();
            else target.DispatcherQueue.TryEnqueue(Dispose);
        });
        Refresh(fit, naturalSize);
    }
    internal void Refresh(ImageFit? value, bool preserveNaturalSize = false)
    {
        if (disposed) return;
        if (fit != value || naturalSize != preserveNaturalSize)
        { fit = value; naturalSize = preserveNaturalSize; requested = null; }
        if (queued) return;
        queued = true;
        if (!target.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        { queued = false; if (!disposed) ApplySize(); })) queued = false;
    }
    internal void RefreshSize() => Refresh(fit, naturalSize);
    internal void SetPlaybackSuspended(bool value) { playbackSuspended = value; RefreshPlayback(); }
    internal void RefreshPlayback() => animation?.Refresh();
    private void Loaded(object sender, RoutedEventArgs args) => RefreshSize();
    private void SizeChanged(object sender, SizeChangedEventArgs args) => RefreshSize();
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => RefreshSize();

    private void ApplySize()
    {
        RefreshPlayback();
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
        for (DependencyObject? parent = target; parent is not null; parent = PresentationLayers.LogicalParent(parent))
        {
            if (parent is FrameworkElement { Visibility: Visibility.Collapsed }) return;
            if (parent is Shell.OverlayScaleRoot zoom) nextZoom.Add(zoom);
        }
        if (!nextZoom.SequenceEqual(zoomRoots.Select(value => value.Root)))
        {
            foreach (var (zoom, token) in zoomRoots) zoom.UnregisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, token);
            zoomRoots.Clear();
            foreach (var zoom in nextZoom) zoomRoots.Add((zoom,
                zoom.RegisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, (_, _) => RefreshSize())));
        }
        var pixels = TargetPixels(target);
        if (requested == pixels) return;
        requested = pixels;
        if (sourceSize is { } source && decodedSize is { } existing &&
            (decodedFit == fit || decodedFit != ImageFit.Fill && fit != ImageFit.Fill) &&
            NativeArtworkDecoder.SizeFor(source, pixels, fit, naturalSize) is { } needed &&
            existing.Width >= needed.Width && existing.Height >= needed.Height)
        {
            request?.Cancel(); request?.Dispose(); request = null;
            completed(true); return;
        }
        request?.Cancel(); request?.Dispose();
        request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        completed(false);
        NativeArtworkCounters.Requested();
        observe(LoadAsync(request, pixels, fit, naturalSize));
    }

    internal static ArtworkPixelSize TargetPixels(FrameworkElement target)
    {
        var width = target.ActualWidth;
        var height = target.ActualHeight;
        var envelope = 1d;
        for (DependencyObject? parent = target; parent is not null; parent = PresentationLayers.LogicalParent(parent))
        {
            if (parent is PresentationPortal or Media.MediaPresentationStage) continue; // Placement already represents the logical ancestors' zoom.
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

    private async Task LoadAsync(CancellationTokenSource owner, ArtworkPixelSize pixels, ImageFit? imageFit, bool preserveNaturalSize)
    {
        var token = owner.Token;
        var timing = PresentationPipelineTrace.Log.Begin();
        var phase = PipelineStage.ArtworkResolve;
        var phaseStarted = timing.Started;
        var outcome = PipelineOutcome.Retired;
        try
        {
            var payload = await resolve(token);
            token.ThrowIfCancellationRequested();
            PresentationPipelineTrace.Log.End(timing.Id, phase, phaseStarted, payload is null ? 0 : 1, PipelineOutcome.Completed);
            phase = PipelineStage.ArtworkDecode;
            phaseStarted = PresentationPipelineTrace.Timestamp(timing.Id);
            var decoded = await NativeArtworkDecoder.DecodeAsync(payload, pixels, imageFit, token, preserveNaturalSize, autoPlay: false);
            PresentationPipelineTrace.Log.End(timing.Id, phase, phaseStarted, decoded is null ? 0 : 1, PipelineOutcome.Completed);
            phase = PipelineStage.ArtworkPublish;
            phaseStarted = PresentationPipelineTrace.Timestamp(timing.Id);
            if (disposed || token.IsCancellationRequested || !ReferenceEquals(request, owner) || !admitted()) return;
            animation?.Dispose(); animation = null;
            sourceSize = decoded?.Source; decodedSize = decoded?.Decoded; decodedFit = imageFit; LastDecode = decoded;
            if (decoded?.Image.IsAnimatedBitmap == true)
                animation = new(target, decoded.Image, () => !disposed && !playbackSuspended && admitted() && animationAllowed());
            completed(true);
            NativeArtworkCounters.Completed();
            publish(decoded?.Image);
            RefreshPlayback();
            outcome = PipelineOutcome.Completed;
            PresentationPipelineTrace.Log.End(timing.Id, phase, phaseStarted, decoded is null ? 0 : 1, outcome);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = PipelineOutcome.Cancelled;
            PresentationPipelineTrace.Log.End(timing.Id, phase, phaseStarted, 0, outcome);
            NativeArtworkCounters.Cancelled();
        }
        catch (Exception error)
        {
            outcome = PipelineOutcome.Failed;
            PresentationPipelineTrace.Log.End(timing.Id, phase, phaseStarted, 0, outcome);
            if (disposed || token.IsCancellationRequested || !ReferenceEquals(request, owner) || !admitted()) return;
            completed(true); NativeArtworkCounters.Failed(); failed(error);
        }
        finally
        {
            PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.ArtworkTotal, timing.Started, 1, outcome);
            if (ReferenceEquals(request, owner)) { request = null; owner.Dispose(); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        target.Loaded -= Loaded; target.SizeChanged -= SizeChanged;
        animation?.Dispose(); animation = null;
        if (root is not null) root.Changed -= RootChanged;
        foreach (var (zoom, token) in zoomRoots) zoom.UnregisterPropertyChangedCallback(Shell.OverlayScaleRoot.InterfaceScaleProperty, token);
        zoomRoots.Clear();
        request?.Cancel(); request?.Dispose(); request = null;
        cancellation.Unregister();
        LastDecode = null;
    }
}
