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
    private sealed record ImageDemand(string Identity, WidgetPresentationAuthority? Authority, CancellationTokenSource Lifetime)
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
        var identity = node.ArtworkHandle is { } handle ? "handle:" + generation + ":" + handle : node.ImageSource ?? string.Empty;
        // Ordinary opaque artwork is admitted against one snapshot. Indexed
        // artwork belongs to its retained lease generation. Keep decoded pixels,
        // but restart unfinished ordinary demand when its snapshot is replaced.
        var authority = node.ArtworkHandle is not null && generation.Length == 0 ? frame?.Authority : null;
        if (imageDemands.TryGetValue(binding, out var prior) && prior.Identity == identity &&
            (prior.Completed || prior.Authority == authority)) return;
        if (imageDemands.Remove(binding, out prior)) { prior.Lifetime.Cancel(); prior.Lifetime.Dispose(); }
        var lifetime = new CancellationTokenSource();
        var demand = new ImageDemand(identity, authority, lifetime);
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
                    else if (Session is { } session && demand.Authority is { } captured)
                        bytes = (await session.ResolveArtworkAsync(captured, opaque, token)).EncodedBytes.ToArray();
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
            catch (WidgetPresentationSessionException error) when (demand.Authority is not null &&
                error.Code is "presentation_stale" or "snapshot_stale")
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
        SettleTransitions();
        WidgetControllerPrompts.Changed -= ControllerPromptsChanged;
        DismissTransientControl();
        ClearSurfaceState();
        foreach (var binding in bindings.Values) Retire(binding);
        Content = null;
        bindings.Clear(); declarations.Clear();
        await Task.WhenAll(retirements.ToArray());
    }
}
