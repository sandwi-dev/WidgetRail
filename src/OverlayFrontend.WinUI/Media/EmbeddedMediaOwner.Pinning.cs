using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class EmbeddedMediaOwner
{
    private sealed record CompactSurface(WidgetMediaPresentation Presentation,Grid Host);
    private readonly Dictionary<string,CompactSurface> compactSurfaces=new(StringComparer.Ordinal);
    private string? compactInputWidget;
    internal event Action? CompactChanged;
    internal WidgetMediaPresentation? GetCompact(string widget)=>compactSurfaces.GetValueOrDefault(widget)?.Presentation;
    internal bool CanPin(string widgetId)=>!retired && !compactSurfaces.ContainsKey(widgetId) &&
        visible && activeWidget==widgetId && IsReady(widgetId) && MatchingViewport(widgetId) is not null;
    internal WidgetPresentationEmbeddedMediaState? GetCompactState(WidgetMediaPresentation presentation)=>
        session.IsMediaPresentationCurrent(presentation)?session.GetEmbeddedMediaState(presentation.Document):null;
    internal EmbeddedMediaPlaybackEvent? GetCompactPlayback(WidgetMediaPresentation presentation)=>
        entries.GetValueOrDefault(presentation.Document.Authority.WidgetId)?.Surface?.Playback;
    internal bool CompactCommandPending=>compactInputWidget is { } id && entries.GetValueOrDefault(id)?.Surface?.IsCommandPending==true;

    internal WidgetMediaPresentation? EnterCompact(WidgetPresentationFrame displayed,Grid destination)
    {
        DemandDispatcher();
        var id=displayed.Authority.WidgetId;
        if(retired || compactSurfaces.ContainsKey(id) || !visible || id!=activeWidget || !destination.IsLoaded ||
            entries.GetValueOrDefault(id) is not {Document:{ } document,Surface:{IsReady:true} surface} ||
            MatchingViewport(id) is not { } ordinary || !ReferenceEquals(surface.Element.Parent,MainHost(id,ordinary)))return null;
        var receipt=session.EnterEmbeddedMediaCompact(displayed,document);
        if(fullscreen?.Document.Authority.WidgetId==id)ExitFullscreen();
        compactSurfaces.Add(id,new(receipt,destination));
        Reconcile();CompactChanged?.Invoke();
        return receipt;
    }

    internal void SetCompactInput(WidgetMediaPresentation presentation,bool enabled)
    {
        DemandDispatcher();
        var id=presentation.Document.Authority.WidgetId;
        if(enabled && ReferenceEquals(GetCompact(id),presentation) && session.IsMediaPresentationCurrent(presentation))compactInputWidget=id;
        else if(compactInputWidget==id)compactInputWidget=null;
        Reconcile();
    }

    private void ExitCompact(string id)
    {
        DemandDispatcher();
        if(!compactSurfaces.Remove(id,out var pin))return;
        if(compactInputWidget==id)compactInputWidget=null;
        if(entries.TryGetValue(id,out var entry))Place(entry,MatchingViewport(id));
        else foreach(var element in pin.Host.Children.ToArray()){pin.Host.Children.Remove(element);parking.Children.Add(element);}
        CompactChanged?.Invoke();
    }

    internal Task ReleaseCompactAsync(WidgetMediaPresentation presentation)
    {
        var id=presentation.Document.Authority.WidgetId;
        if(!ReferenceEquals(GetCompact(id),presentation))return Task.CompletedTask;
        var surface=entries.GetValueOrDefault(id)?.Surface;
        ExitCompact(id);
        return surface?.PlacementCompletion??Task.CompletedTask;
    }
    internal Task WaitForCompactPlacementAsync(WidgetMediaPresentation presentation)=>
        entries.GetValueOrDefault(presentation.Document.Authority.WidgetId)?.Surface?.PlacementCompletion??Task.CompletedTask;

    internal bool DispatchCompact(EmbeddedMediaHostCommand command)=>compactInputWidget is { } id &&
        GetCompact(id) is { } pin && session.IsMediaPresentationCurrent(pin) &&
        entries.GetValueOrDefault(id)?.Surface?.DispatchPresentation(command,MediaPresentationKind.CompactPinned)==true;
    internal bool DispatchCompact(EmbeddedMediaCommand command)=>compactInputWidget is { } id &&
        GetCompact(id) is { } pin && session.IsMediaPresentationCurrent(pin) &&
        entries.GetValueOrDefault(id)?.Surface?.Dispatch(command)==true;

    private bool IsCompact(string id)=>compactSurfaces.GetValueOrDefault(id) is {Host.IsLoaded:true} pin &&
        session.IsMediaPresentationCurrent(pin.Presentation);
}
