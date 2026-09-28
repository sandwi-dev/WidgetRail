using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class EmbeddedMediaOwner
{
    private Grid? fullscreenHost;
    private WidgetMediaPresentation? fullscreen;
    internal event Action? FullscreenChanged;
    internal string? FullscreenWidgetId => fullscreen?.Document.Authority.WidgetId;
    internal WidgetPresentationEmbeddedMediaState? FullscreenState => fullscreen is not null && session.IsMediaPresentationCurrent(fullscreen)
        ? session.GetEmbeddedMediaState(fullscreen.Document) : null;
    internal bool DispatchFullscreen(EmbeddedMediaHostCommand command) => fullscreen?.Document.Authority.WidgetId is { } id && IsFullscreen(id) &&
        entries.GetValueOrDefault(id)?.Surface?.DispatchPresentation(command, WidgetRail.WidgetProtocol.MediaPresentationKind.OverlayFullscreen) == true;

    // One durable native destination owned by the shell. It must share the ordinary
    // viewport's XamlRoot. Changing the destination retires its presentation request.
    internal void SetFullscreenHost(Grid? host)
    {
        DemandDispatcher();
        if (ReferenceEquals(host, fullscreenHost)) return;
        ExitFullscreen();
        fullscreenHost = host;
    }

    internal bool EnterFullscreen(WidgetPresentationFrame displayed, WidgetActionEvent action)
    {
        DemandDispatcher();
        var id = displayed.Authority.WidgetId;
        if (retired || fullscreenHost is null || fullscreen is not null || !visible || !inputEnabled || id != activeWidget ||
            entries.GetValueOrDefault(id) is not { Document: { } document, Surface: { IsReady: true } surface } ||
            MatchingViewport(id) is not { } ordinary || !ordinary.Element.AcceptsInput ||
            !ReferenceEquals(surface.Element.Parent, ordinary.Element.SurfaceHost) ||
            !ReferenceEquals(ordinary.Element.XamlRoot, fullscreenHost.XamlRoot)) return false;
        fullscreen = session.EnterEmbeddedMediaFullscreen(displayed, action, document);
        FullscreenChanged?.Invoke();
        Reconcile();
        return true;
    }

    internal bool ExitFullscreen()
    {
        DemandDispatcher();
        if (fullscreen is null) return false;
        fullscreen = null;
        FullscreenChanged?.Invoke();
        Reconcile();
        return true;
    }

    private bool IsFullscreen(string id) => visible && inputEnabled && id == activeWidget && fullscreenHost is not null &&
        fullscreen?.Document.Authority.WidgetId == id && session.IsMediaPresentationCurrent(fullscreen);
}
