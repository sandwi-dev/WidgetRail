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
        internal Func<bool>? SourceCurrent { get; set; }
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
    internal Func<string, CancellationToken, Task<WidgetArtworkSource?>>? ResolveArtworkAsync { get; set; }
    internal Func<string, CancellationToken, Task<WidgetEncodedArtwork?>>? ResolveArtworkFallbackAsync { get; set; }
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
        if (binding.Children is Motion.WidgetMotionStage stage)
            foreach (var page in retainedPages.Where(page => ReferenceEquals(page.Mount, stage)).ToArray())
                EvictCachedPage(page);
        retainedStaticArtwork.Remove(binding);
        if (ReferenceEquals(previewSource?.Owner, binding) || ReferenceEquals(previewSource?.Source, binding)) ClearFocusPreview();
        if (binding.Element is ScrollViewer scroll && readingScrolls.Remove(scroll,out var reading)) reading.Dispose();
        RetireInlineLink(binding);
        if (binding.Element is WidgetRichTextView rich) rich.DetachExcept(_ => false);
        RetireSlider(binding);
        if (artworkReveals.Remove(binding, out var reveal)) reveal.Dispose();
        if (ReferenceEquals(waitingEntry, binding)) ClearEntryLayoutWait();
        RetireMediaViewport(binding);
        if (binding.Element is Media.MediaPlayerView captured) captured.Dispose();
        if (binding.Element is Browser.BrowserSlot browser) { BrowserOwner?.Unbind(browser); browser.Retire(); }
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
        // A row fragment shares the window's FocusManager with sibling rows.
        // Walking from a sibling's focus can escape this presenter and find the
        // outer collection, recursively routing pressed-state reset back into
        // every row. Only a collection declared by this presenter owns its input.
        return FocusedBinding()?.Element as WidgetIndexedCollectionView;
    }

    private void UpdateImage(Binding binding, WidgetArtworkView image, ViewNode node)
    {
        image.ApplyStyle(ResolveArtworkStyle(binding, node));
        image.ConfigureFallback(ComputedStyle(node, "image-fallback")?.Text == "label" ? node.AccessibilityLabel : null, appearance.TextScale);
        UpdateArtwork(binding, node, value =>
        {
            retainedStaticArtwork.Remove(binding);
            TraceArtworkReview(node.Id, image.Source is not null, value is not null);
            var firstPixels = image.Source is null && value is not null;
            image.Source = value;
            image.SetUnavailable(value is null);
            if (value is null) { if (artworkReveals.Remove(binding, out var old)) old.Dispose(); return; }
            if (!firstPixels || binding.MotionHost?.ArtworkLayer is not { } layer) return;
            var parent = declarations.GetValueOrDefault(node.Id);
            while (parent is not null && parent.Node.ActionSurfacePresentation != ActionSurfacePresentation.Poster)
                parent = parent.ParentId is { } id ? declarations.GetValueOrDefault(id) : null;
            if (parent is null) return;
            if (!artworkReveals.TryGetValue(binding, out var reveal)) artworkReveals.Add(binding, reveal = new(layer));
            reveal.Play(appearance, systemAnimationsEnabled);
        }, ResolveArtworkAsync, ArtworkGeneration, ArtworkAuthorityCurrent, ResolveArtworkFallbackAsync);
    }
    partial void TraceArtworkReview(string id, bool hadPixels, bool hasPixels);

    private void UpdateMediaPoster(Binding binding, Media.MediaPlayerView media, ViewNode node)
    {
        if (node.ArtworkHandle is null && node.ImageSource is null && !imageDemands.ContainsKey(binding)) return;
        UpdateArtwork(binding, node, image => media.SetPoster(image ?? (node.ArtworkHandle is { } handle ? BorrowPreviewArtwork?.Invoke(handle) : null)), ResolveArtworkAsync, ArtworkGeneration,
            ArtworkAuthorityCurrent, ResolveArtworkFallbackAsync, retainOnReplacement: false);
    }

    // Borrow existing pixels only from the still-current renderer; no new
    // cache, file authority or decoding work is introduced by this handoff.
    internal ImageSource? BorrowDecodedArtwork(string handle)
    {
        if (disposed || !presentationActive || ArtworkAuthorityCurrent?.Invoke() == false) return null;
        return imageDemands.Where(pair => pair.Value.Completed &&
                declarations.TryGetValue(pair.Key.Identity.Id, out var declaration) && declaration.Node.ArtworkHandle == handle)
            .Select(pair => pair.Value.Native?.LastDecode).OfType<NativeArtworkDecode>()
            .OrderByDescending(value => (long)value.Decoded.Width * value.Decoded.Height).FirstOrDefault()?.Image;
    }

    private void UpdateArtwork(Binding binding, ViewNode node, Action<ImageSource?> publish,
        Func<string, CancellationToken, Task<WidgetArtworkSource?>>? resolver, string generation, Func<bool>? sourceCurrent = null,
        Func<string, CancellationToken, Task<WidgetEncodedArtwork?>>? fallback = null, bool retainOnReplacement = true)
    {
        if (!presentationActive || sourceCurrent?.Invoke() == false) return;
        var artworkStyle = ResolveArtworkStyle(binding, node);
        var fit = artworkStyle.DecodeFit;
        if (binding.Element is WidgetPresentationSurface surface)
            surface.SetArtworkStyle(artworkStyle);
        var identity = node.ArtworkHandle is { } handle ? "handle:" + generation + ":" + handle : node.ImageSource ?? string.Empty;
        if (node.ImageIsPublicMedia) identity = "public:" + identity;
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
            prior.SourceCurrent = sourceCurrent;
            prior.Native?.SetPlaybackSuspended(false);
            // Media may have released its presentation while this exact artwork
            // demand remained valid. Reuse the demand's existing decoded image.
            if (!retainOnReplacement && prior.Completed) publish(prior.Native?.LastDecode?.Image);
            prior.Native?.Refresh(fit, artworkStyle.NaturalSize); return;
        }
        if (imageDemands.Remove(binding, out prior)) { prior.Lifetime.Cancel(); prior.Lifetime.Dispose(); }
        if (binding.Element is WidgetArtworkView nextImage) nextImage.SetUnavailable(false);
        if (!retainOnReplacement) publish(null);
        var lifetime = new CancellationTokenSource();
        var demand = new ImageDemand(identity, origin, lifetime) { SourceCurrent = sourceCurrent };
        imageDemands.Add(binding, demand);
        // Retained logical rows keep their previous pixels while replacement
        // artwork decodes. A different item creates a different Image binding.
        if (identity.Length == 0) { demand.Completed = true; publish(null); return; }
        demand.Native = new(binding.Element, fit,
            () => !disposed && presentationActive && imageDemands.GetValueOrDefault(binding) == demand && (demand.SourceCurrent?.Invoke() ?? true),
            ResolveAsync, publish, value => demand.Completed = value, TrackRetirement, DecodeFailed, lifetime.Token, artworkStyle.NaturalSize,
            () => !Motion.WidgetMotionOptions.From(appearance, systemAnimationsEnabled).Reduced);

        async Task<NativeArtworkPayload?> ResolveAsync(CancellationToken token)
        {
            if (node.ArtworkHandle is { } opaque)
            {
                if (resolver is not null)
                {
                    var artwork = await resolver(opaque, token);
                    return artwork switch
                    {
                        WidgetEncodedArtwork encoded => new(encoded.Bytes),
                        WidgetFileArtwork file => new(ReadOnlyMemory<byte>.Empty, File: file.File,
                            Fallback: fallback is null ? null : async ct => await fallback(opaque, ct) is { } bytes ? new(bytes.Bytes) : null),
                        _ => null,
                    };
                }
                if (Session is { } session && demand.Origin is { } captured)
                {
                    var artwork = captured.Selection is { } selection && captured.Projection is { } projection
                        ? await session.ResolvePinnedArtworkAsync(selection, projection, opaque, token, allowFileSource: true)
                        : await session.ResolveArtworkAsync(captured.Frame.Authority, opaque, token, allowFileSource: true);
                    return new(artwork.EncodedBytes, File: artwork.File, Fallback: artwork.File is null ? null : async ct =>
                    {
                        var bytes = captured.Selection is { } pinned && captured.Projection is { } layout
                            ? await session.ResolvePinnedArtworkAsync(pinned, layout, opaque, ct)
                            : await session.ResolveArtworkAsync(captured.Frame.Authority, opaque, ct);
                        return new(bytes.EncodedBytes);
                    });
                }
            }
            else if (node.ImageSource is { } uri && Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
                return new(ReadOnlyMemory<byte>.Empty, parsed, IsPublicMedia: node.ImageIsPublicMedia);
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
                if (binding.Element is WidgetPresentationSurface background && imageDemands.GetValueOrDefault(binding) == demand)
                    background.ForegroundArtworkFailed();
                if (binding.Element is WidgetArtworkView missingImage) missingImage.SetUnavailable(true);
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
    private void RefreshArtworkPlayback()
    {
        foreach (var demand in imageDemands.Values) demand.Native?.RefreshPlayback();
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
        DisposePreviewEvents();
        CancelPassiveScrollFocus();
        scrollGesture = null;
        CancelScrollReveal();
        directionalScroll = null;
        ResetSliderValues();
        DisposeSurfaceClip();
        surfaceResize?.Dispose(); surfaceResize = null;
        CancelMemoryRestoration();
        returnViewports.Clear();
        suspendedCollectionReturn = null;
        ClearEntryLayoutWait();
        SettleTransitions();
        SettleModalExit();
        ClearPageCache();
        WidgetControllerPrompts.Changed -= ControllerPromptsChanged;
        DismissTransientControl();
        ClearSurfaceState();
        var browserSlots = bindings.Values.Select(binding => binding.Element).OfType<Browser.BrowserSlot>().ToArray();
        foreach (var binding in bindings.Values) Retire(binding);
        if (BrowserOwner is { } browsers) await browsers.ReleaseSlotsAsync(browserSlots);
        motionStage?.SetModal(null);
        motionStage?.SetCurrent(null);
        Content = null;
        bindings.Clear(); declarations.Clear();
        await Task.WhenAll(retirements.ToArray());
    }
}
