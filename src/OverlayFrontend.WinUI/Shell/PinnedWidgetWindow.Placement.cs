namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class PinnedWidgetWindow
{
    private PinnedSurfacePointer? pointerPlacement;
    private PinnedSurfacePointer? pointerActivation;
    internal nint PointerActivationHandle=>pointerActivation?.Handle??0;
    internal void SetPointerActivation(Action<bool>? activate,Action<Exception>? failed=null)
    {
        if(disposed)return;
        if(activate is null)
        {pointerActivation?.Dispose();pointerActivation=null;return;}
        if(pointerActivation is not null||Interactive||!IsVisible)return;
        pointerActivation=new(Handle,Bounds,activate,error=>{SetPointerActivation(null);failed?.Invoke(error);});
        ReconcileOrder();
    }
    internal nint PointerPlacementHandle=>pointerPlacement?.Handle??0;
    // Host-only input covers XAML and browser children; authored content stays passive.
    internal void SetPointerPlacement(Func<PinnedBounds,int,PinnedBounds>? preview,Action<Exception>? failed=null)
    {
        if(disposed)return;
        pointerPlacement?.Dispose();pointerPlacement=null;
        if(preview is null)return;
        SetPointerActivation(null);
        pointerPlacement=new(Handle,Bounds,()=>automationRoot.XamlRoot?.RasterizationScale??1,(bounds,edge)=>
        {
            var next=preview(bounds,edge);Place(next);return next;
        },error=>{SetPointerPlacement(null);failed?.Invoke(error);});
    }
}
