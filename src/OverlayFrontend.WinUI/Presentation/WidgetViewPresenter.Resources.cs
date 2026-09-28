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
    private readonly HashSet<Task> retirements = [];
    private bool disposed;
    public Action<Exception>? Failed { get; set; }
    internal Func<string, CancellationToken, Task<WidgetEncodedArtwork?>>? ResolveArtworkAsync { get; set; }
    internal string ArtworkGeneration { get; set; } = string.Empty;

    private void ReportFailure(Exception error) => Failed?.Invoke(error);

    private void Retire(Binding binding)
    {
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

    private void UpdateImage(Binding binding, Image image, ViewNode node)
    {
        image.Stretch = node.ImageFit switch { ImageFit.Contain => Stretch.Uniform, ImageFit.Cover => Stretch.UniformToFill, _ => Stretch.Fill };
        UpdateArtwork(binding, node, value => image.Source = value, ResolveArtworkAsync, ArtworkGeneration);
    }

    private void UpdateArtwork(Binding binding, ViewNode node, Action<ImageSource?> publish,
        Func<string, CancellationToken, Task<WidgetEncodedArtwork?>>? resolver, string generation)
    {
        if (!presentationActive) return;
        var fit = node.ImageFit ?? (binding.Element is WidgetPresentationSurface ? ImageFit.Cover : ImageFit.Fill);
        var identity = node.ArtworkHandle is { } handle ? "handle:" + generation + ":" + handle : node.ImageSource ?? string.Empty;
        // Ordinary opaque artwork is admitted against one snapshot. Indexed
        // artwork belongs to its retained lease generation. Keep decoded pixels,
        // but restart unfinished ordinary demand when its snapshot is replaced.
        var origin = node.ArtworkHandle is not null && generation.Length == 0 ? presentation : null;
        if (imageDemands.TryGetValue(binding, out var prior) && prior.Identity == identity &&
            (prior.Completed || (origin?.Selection is not null ? origin.SameInput(prior.Origin) : prior.Origin?.Frame.Authority == origin?.Frame.Authority)))
        {
            // Retained pixels can survive a snapshot; a later size upgrade must
            // resolve against the newly displayed authority, never the old one.
            if (prior.Completed) prior.Origin = origin;
            prior.Native?.Refresh(fit); return;
        }
        if (imageDemands.Remove(binding, out prior)) { prior.Lifetime.Cancel(); prior.Lifetime.Dispose(); }
        var lifetime = new CancellationTokenSource();
        var demand = new ImageDemand(identity, origin, lifetime);
        imageDemands.Add(binding, demand);
        // Retained logical rows keep their previous pixels while replacement
        // artwork decodes. A different item creates a different Image binding.
        if (identity.Length == 0) { demand.Completed = true; publish(null); return; }
        demand.Native = new(binding.Element, fit,
            () => !disposed && presentationActive && imageDemands.GetValueOrDefault(binding) == demand,
            ResolveAsync, publish, value => demand.Completed = value, TrackRetirement, DecodeFailed, lifetime.Token);

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
            ReportFailure(error);
        }
    }

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
