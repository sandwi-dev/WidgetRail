using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetPresentationSession;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private sealed record ImageDemand(string Identity, WidgetPresentationBinding? Origin, CancellationTokenSource Lifetime)
    { internal bool Completed { get; set; } }
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
        var identity = node.ArtworkHandle is { } handle ? "handle:" + generation + ":" + handle : node.ImageSource ?? string.Empty;
        // Ordinary opaque artwork is admitted against one snapshot. Indexed
        // artwork belongs to its retained lease generation. Keep decoded pixels,
        // but restart unfinished ordinary demand when its snapshot is replaced.
        var origin = node.ArtworkHandle is not null && generation.Length == 0 ? presentation : null;
        if (imageDemands.TryGetValue(binding, out var prior) && prior.Identity == identity &&
            (prior.Completed || (origin?.Selection is not null ? origin.SameInput(prior.Origin) : prior.Origin?.Frame.Authority == origin?.Frame.Authority))) return;
        if (imageDemands.Remove(binding, out prior)) { prior.Lifetime.Cancel(); prior.Lifetime.Dispose(); }
        var lifetime = new CancellationTokenSource();
        var demand = new ImageDemand(identity, origin, lifetime);
        imageDemands.Add(binding, demand);
        // Retained logical rows keep their previous pixels while replacement
        // artwork decodes. A different item creates a different Image binding.
        if (identity.Length == 0) { demand.Completed = true; publish(null); return; }
        if (node.ImageSource is { } uri && Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps)
        { demand.Completed = true; publish(new BitmapImage(parsed)); return; }
        TrackRetirement(LoadAsync());
        async Task LoadAsync()
        {
            var token = lifetime.Token;
            try
            {
                byte[]? bytes = null;
                if (node.ArtworkHandle is { } opaque)
                {
                    if (resolver is not null) bytes = (await resolver(opaque, token))?.Bytes.ToArray();
                    else if (Session is { } session && demand.Origin is { } captured)
                        bytes = (captured.Selection is { } selection && captured.Projection is { } projection
                            ? await session.ResolvePinnedArtworkAsync(selection, projection, opaque, token)
                            : await session.ResolveArtworkAsync(captured.Frame.Authority, opaque, token)).EncodedBytes.ToArray();
                }
                else if (node.ImageSource?.StartsWith("data:image/png;base64,", StringComparison.Ordinal) == true)
                    bytes = Convert.FromBase64String(node.ImageSource[22..]);
                token.ThrowIfCancellationRequested();
                if (bytes is null)
                {
                    if (!disposed && imageDemands.GetValueOrDefault(binding) == demand) { demand.Completed = true; publish(null); }
                    return;
                }
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer()).AsTask(token);
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream).AsTask(token);
                if (!disposed && !token.IsCancellationRequested && imageDemands.GetValueOrDefault(binding) == demand)
                { demand.Completed = true; publish(bitmap); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (WidgetPresentationSessionException error) when (demand.Origin is not null &&
                (IsRetiredInput(error) || error.Code is "unknown_artwork" or "stale_artwork_authority"))
            {
                // The next snapshot may reach the session before UI dispatch.
                // Its Apply starts a fresh request; this is not a widget failure.
                if (imageDemands.GetValueOrDefault(binding) == demand)
                { imageDemands.Remove(binding); lifetime.Dispose(); }
            }
        }
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
