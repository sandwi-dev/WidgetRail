using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class EmbeddedMediaOwner
{
    private WidgetMediaPresentation? compact;
    private Grid? compactHost;
    private bool compactInput;
    internal event Action? CompactChanged;
    internal WidgetMediaPresentation? CompactPresentation => compact;
    internal bool CanPin(string widgetId) => !retired && compact is null && visible && activeWidget == widgetId &&
        IsReady(widgetId) && MatchingViewport(widgetId) is not null;
    internal WidgetPresentationEmbeddedMediaState? CompactState => compact is not null && session.IsMediaPresentationCurrent(compact)
        ? session.GetEmbeddedMediaState(compact.Document) : null;
    internal EmbeddedMediaPlaybackEvent? CompactPlayback => compact is not null
        ? entries.GetValueOrDefault(compact.Document.Authority.WidgetId)?.Surface?.Playback : null;

    internal WidgetMediaPresentation? EnterCompact(WidgetPresentationFrame displayed, Grid destination)
    {
        DemandDispatcher();
        var id = displayed.Authority.WidgetId;
        if (retired || compact is not null || !visible || id != activeWidget || !destination.IsLoaded ||
            entries.GetValueOrDefault(id) is not { Document: { } document, Surface: { IsReady: true } surface } ||
            MatchingViewport(id) is not { } ordinary || !ReferenceEquals(surface.Element.Parent, ordinary.Element.SurfaceHost)) return null;
        var receipt = session.EnterEmbeddedMediaCompact(displayed, document);
        ExitFullscreen();
        compact = receipt; compactHost = destination; compactInput = false;
        Reconcile(); CompactChanged?.Invoke();
        return receipt;
    }

    internal void SetCompactInput(bool enabled)
    {
        DemandDispatcher();
        compactInput = enabled && compact is not null && session.IsMediaPresentationCurrent(compact);
        Reconcile();
    }

    // Detach/return the durable browser before its peer window destroys XamlRoot.
    internal void ExitCompact()
    {
        DemandDispatcher();
        if (compact is null) return;
        var id = compact.Document.Authority.WidgetId;
        var destination = compactHost;
        compact = null; compactHost = null; compactInput = false;
        if (entries.TryGetValue(id, out var entry)) Place(entry, MatchingViewport(id));
        else if (destination is not null)
        {
            // Document retirement may already have removed its entry. Release
            // the peer XamlRoot synchronously before shell notification can close it.
            foreach (var element in destination.Children.ToArray())
            { destination.Children.Remove(element); parking.Children.Add(element); }
        }
        CompactChanged?.Invoke();
    }

    internal Task ReleaseCompactAsync()
    {
        var surface = compact is not null ? entries.GetValueOrDefault(compact.Document.Authority.WidgetId)?.Surface : null;
        ExitCompact();
        return surface?.PlacementCompletion ?? Task.CompletedTask;
    }
    internal Task WaitForCompactPlacementAsync() => compact is not null
        ? entries.GetValueOrDefault(compact.Document.Authority.WidgetId)?.Surface?.PlacementCompletion ?? Task.CompletedTask : Task.CompletedTask;

    internal bool CompactCommandPending => compact is not null &&
        entries.GetValueOrDefault(compact.Document.Authority.WidgetId)?.Surface?.IsCommandPending == true;

    internal bool DispatchCompact(EmbeddedMediaHostCommand command) => compact is not null && compactInput &&
        session.IsMediaPresentationCurrent(compact) && entries.GetValueOrDefault(compact.Document.Authority.WidgetId)?.Surface?
            .DispatchPresentation(command, MediaPresentationKind.CompactPinned) == true;
    internal bool DispatchCompact(EmbeddedMediaCommand command) => compact is not null && compactInput &&
        session.IsMediaPresentationCurrent(compact) && entries.GetValueOrDefault(compact.Document.Authority.WidgetId)?.Surface?.Dispatch(command) == true;

    private bool IsCompact(string id) => compact?.Document.Authority.WidgetId == id && compactHost is { IsLoaded: true } &&
        session.IsMediaPresentationCurrent(compact);
}
