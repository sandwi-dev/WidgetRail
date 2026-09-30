using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed class ImageDemand(string identity, WidgetPresentationBinding? origin, CancellationTokenSource lifetime)
    {
        internal string Identity { get; } = identity;
        internal WidgetPresentationBinding? Origin { get; set; } = origin;
        internal CancellationTokenSource Lifetime { get; } = lifetime;
        internal bool Completed { get; set; }
        internal NativeArtworkDemand? Native { get; set; }
    }
    private readonly Dictionary<Binding, ImageDemand> imageDemands = [];
    private readonly Dictionary<Binding, Motion.WidgetArtworkReveal> artworkReveals = [];
    internal int ArtworkRevealStarts => artworkReveals.Values.Sum(value => value.Starts);
    private readonly HashSet<Task> retirements = [];
    private bool disposed;
    public Action<Exception>? Failed { get; set; }
    internal Func<string, CancellationToken, Task<WidgetEncodedArtwork?>>? ResolveArtworkAsync { get; set; }
    internal string ArtworkGeneration { get; set; } = string.Empty;
    internal Func<bool>? ArtworkAuthorityCurrent { get; set; }

    private void ReportFailure(Exception error) => Failed?.Invoke(error);
    private void ReportFailure(Exception error, bool ownerCurrent)
    {
        if (ownerCurrent) ReportFailure(error);
        else Diagnostics.FrontendFailureLog.Current.Write("retired-presentation-operation", error);
    }

    private void Retire(Binding binding)
    {
        RetireSlider(binding);
        if (artworkReveals.Remove(binding, out var reveal)) reveal.Dispose();
        if (ReferenceEquals(waitingEntry, binding)) ClearEntryLayoutWait();
        RetireMediaViewport(binding);
        if (binding.Element is Previews.WidgetWindowPreview preview) preview.Dispose();
        RetireComputedStyles(binding);
        if (buttonIcons.Remove(binding, out var buttonIcon)) buttonIcon.Dispose();
        if (binding.Element is WidgetPackageIconView packageIcon) packageIcon.Dispose();
        if (imageDemands.Remove(binding, out var demand)) { demand.Lifetime.Cancel(); demand.Lifetime.Dispose(); }
        if (binding.Element is WidgetIndexedCollectionView collection) TrackRetirement(collection.DisposeAsync().AsTask());
        if (binding.Element is WidgetPresentationSurface surface) TrackRetirement(surface.DisposeAsync().AsTask());
        if (binding.Element is WidgetModalLayer layer) layer.Dispose();
    }

    private void TrackRetirement(Task task)
    {
        retirements.Add(task);
        _ = ObserveAsync();
        async Task ObserveAsync()
        {
            try { await task; }
            catch (Exception error) { ReportFailure(error); }
            finally { retirements.Remove(task); }
        }
    }

    private WidgetIndexedCollectionView? FindIndexedCollection()
    {
        if (XamlRoot is null) return null;
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null && !ReferenceEquals(node, this);
             node = VisualTreeHelper.GetParent(node))
            if (node is WidgetIndexedCollectionView collection) return collection;
        return null;
    }

    private void UpdateImage(Binding binding, WidgetArtworkView image, ViewNode node)
    {
        image.ApplyStyle(ResolveArtworkStyle(binding, node));
        UpdateArtwork(binding, node, value =>
        {
            var firstPixels = image.Source is null && value is not null;
            image.Source = value;
            if (value is null) { if (artworkReveals.Remove(binding, out var old)) old.Dispose(); return; }
            if (!firstPixels || binding.MotionHost?.ArtworkLayer is not { } layer) return;
            var parent = declarations.GetValueOrDefault(node.Id);
            while (parent is not null && parent.Node.ActionSurfacePresentation != ActionSurfacePresentation.Poster)
                parent = parent.ParentId is { } id ? declarations.GetValueOrDefault(id) : null;
            if (parent is null) return;
            if (!artworkReveals.TryGetValue(binding, out var reveal)) artworkReveals.Add(binding, reveal = new(layer));
            reveal.Play(appearance, systemAnimationsEnabled);
        }, ResolveArtworkAsync, ArtworkGeneration, ArtworkAuthorityCurrent);
    }

    private void UpdateArtwork(Binding binding, ViewNode node, Action<ImageSource?> publish,
        Func<string, CancellationToken, Task<WidgetEncodedArtwork?>>? resolver, string generation, Func<bool>? sourceCurrent = null)
    {
        if (!presentationActive || sourceCurrent?.Invoke() == false) return;
        var artworkStyle = ResolveArtworkStyle(binding, node);
        var fit = artworkStyle.DecodeFit;
        if (binding.Element is WidgetPresentationSurface surface)
            surface.SetArtworkStyle(artworkStyle);
        var identity = node.ArtworkHandle is { } handle ? "handle:" + generation + ":" + handle : node.ImageSource ?? string.Empty;
        // Ordinary artwork survives cosmetic snapshots of the same owner. The
        // session checks continuous handle declaration; indexed artwork retains
        // its lease generation and pinned artwork retains its selected scope.
        var origin = node.ArtworkHandle is not null && generation.Length == 0 ? presentation : null;
        if (imageDemands.TryGetValue(binding, out var prior) && prior.Identity == identity &&
            (origin?.Selection is not null ? prior.Completed || origin.SameInput(prior.Origin) :
                origin is null ? prior.Origin is null : origin.SameSurface(prior.Origin) &&
                origin.Frame.Authority.WorkerRun == prior.Origin?.Frame.Authority.WorkerRun))
        {
            // Retained pixels can survive a snapshot; a later size upgrade must
            // resolve against the newly displayed authority, never the old one.
            prior.Origin = origin;
            prior.Native?.Refresh(fit, artworkStyle.NaturalSize); return;
        }
        if (imageDemands.Remove(binding, out prior)) { prior.Lifetime.Cancel(); prior.Lifetime.Dispose(); }
        var lifetime = new CancellationTokenSource();
        var demand = new ImageDemand(identity, origin, lifetime);
        imageDemands.Add(binding, demand);
        // Retained logical rows keep their previous pixels while replacement
        // artwork decodes. A different item creates a different Image binding.
        if (identity.Length == 0) { demand.Completed = true; publish(null); return; }
        demand.Native = new(binding.Element, fit,
            () => !disposed && presentationActive && imageDemands.GetValueOrDefault(binding) == demand && (sourceCurrent?.Invoke() ?? true),
            ResolveAsync, publish, value => demand.Completed = value, TrackRetirement, DecodeFailed, lifetime.Token, artworkStyle.NaturalSize);

        async Task<NativeArtworkPayload?> ResolveAsync(CancellationToken token)
        {
            if (node.ArtworkHandle is { } opaque)
            {
                if (resolver is not null)
                {
                    var artwork = await resolver(opaque, token);
                    return artwork is null ? null : new(artwork.Bytes);
                }
                if (Session is { } session && demand.Origin is { } captured)
                {
                    var artwork = captured.Selection is { } selection && captured.Projection is { } projection
                        ? await session.ResolvePinnedArtworkAsync(selection, projection, opaque, token)
                        : await session.ResolveArtworkAsync(captured.Frame.Authority, opaque, token);
                    return new(artwork.EncodedBytes);
                }
            }
            else if (node.ImageSource is { } uri && Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
                return new(ReadOnlyMemory<byte>.Empty, parsed);
            else if (node.ImageSource?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true)
                return new(await Task.Run(() => DecodeInlineArtwork(node.ImageSource), token));
            return null;
        }
        void DecodeFailed(Exception error)
        {
            if (error is WidgetPresentationSessionException retired && demand.Origin is not null &&
                (IsRetiredInput(retired) || retired.Code is "unknown_artwork" or "stale_artwork_authority"))
            {
                // A fresh snapshot restarts this request using its real authority.
                if (imageDemands.GetValueOrDefault(binding) == demand)
                { imageDemands.Remove(binding); lifetime.Cancel(); lifetime.Dispose(); }
                return;
            }
            if (IsUnavailableArtwork(error))
            {
                // A failed optional resource must not replace the widget with
                // shell recovery. Keep prior pixels (or the empty image slot)
                // and retain a bounded diagnostic without logging the asset URL.
                Diagnostics.FrontendFailureLog.Current.Write("artwork-unavailable", null,
                    $"widget={presentation?.Frame.Authority.WidgetId} node={node.Id} error={error.GetType().Name} code={(error as WidgetPresentationSessionException)?.Code} hresult=0x{error.HResult:X8}");
                return;
            }
            ReportFailure(error);
        }
    }

    private NativeArtworkStyle ResolveArtworkStyle(Binding binding, ViewNode node) => NativeArtworkStyle.Resolve(
        presentation?.RenderStyles.GetValueOrDefault(binding.Identity.Id)?.Base, node.ImageFit,
        binding.Element is WidgetPresentationSurface ? ImageFit.Cover : ImageFit.Fill);

    private static bool IsUnavailableArtwork(Exception error) =>
        error is IOException or InvalidDataException or FormatException or System.Net.Http.HttpRequestException ||
        // An indexed provider can retire its lease before the replacement
        // snapshot reaches this dispatcher. A rejected optional image request
        // must not replace the still-valid widget with a recovery page.
        error is WidgetPresentationSessionException { Code: "request_failed" or "indexed_artwork_unavailable" or
            "indexed_artwork_timeout" or "indexed_artwork_saturated" } ||
        error is System.Runtime.InteropServices.COMException &&
        ((unchecked((uint)error.HResult) & 0xffff0000u) == 0x80190000u || // HTTP status errors
         (unchecked((uint)error.HResult) & 0xffffff00u) == 0x88982f00u); // WIC image/stream errors

    private void RefreshArtworkDemands()
    {
        foreach (var demand in imageDemands.Values) demand.Native?.RefreshSize();
    }

    private static ReadOnlyMemory<byte> DecodeInlineArtwork(string source)
    {
        var encoded = source.AsSpan(22);
        if (encoded.Length > ((ProtocolConstants.MaximumEncodedArtworkBytes + 2) / 3) * 4)
            throw new InvalidDataException("Inline artwork exceeds its encoded resource bound.");
        var buffer = new byte[(encoded.Length / 4) * 3];
        if (!Convert.TryFromBase64Chars(encoded, buffer, out var written) || written is 0 or > ProtocolConstants.MaximumEncodedArtworkBytes)
            throw new InvalidDataException("Inline artwork encoding is invalid.");
        return buffer.AsMemory(0, written);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        ResetSliderValues();
        DisposeSurfaceClip();
        surfaceResize?.Dispose(); surfaceResize = null;
        CancelMemoryRestoration();
        ClearEntryLayoutWait();
        SettleTransitions();
        SettleModalExit();
        WidgetControllerPrompts.Changed -= ControllerPromptsChanged;
        DismissTransientControl();
        ClearSurfaceState();
        foreach (var binding in bindings.Values) Retire(binding);
        motionStage?.SetModal(null);
        motionStage?.SetCurrent(null);
        Content = null;
        bindings.Clear(); declarations.Clear();
        await Task.WhenAll(retirements.ToArray());
    }
}
