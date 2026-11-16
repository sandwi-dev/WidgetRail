using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private bool refreshingPinPointers;
    private PinnedSurface? pointerPressedPin;
    private uint pointerPressedId;
    private long pointerPressedSession;
    private bool pointerPressedManage;
    private bool AllowsPinPointerEntry => !retired && visible && !switching && !HostChoiceActive &&
        !LocalInstallActive && !SystemFilePickerOpen && !nativeBrowserPickerActive &&
        !IsMediaFullscreen && !PinnedAdjustmentActive && ActiveControllerKeyboard is null;

    private void RefreshPinPointerAdmission()
    {
        if(refreshingPinPointers)return;
        refreshingPinPointers=true;
        try
        {
            if(pointerPressedPin is { } pressed&&(!AllowsPinPointerEntry||!pins.Contains(pressed)||!pressed.IsCurrent||!pressed.Window.IsVisible))
            {
                pointerPressedPin=null;
                ProductionRoot.ReleasePointerCaptures();
            }
            foreach(var target in pins)
            {
                var allowed=AllowsPinPointerEntry&&target.IsCurrent&&target.Window.IsVisible&&!target.Window.Interactive;
                target.Window.SetPointerActivation(allowed?manage=>
                {
                    // Leave the Win32 callback before changing focus or destroying
                    // its input frame. The exact pin is revalidated on dispatch.
                    var session=visibleSince;
                    DispatcherQueue.TryEnqueue(async()=>{if(visibleSince==session)await EnterPinFromPointerAsync(target,manage);});
                }:null,error=>ReportOperationFailure(error,()=>pins.Contains(target)&&target.IsCurrent));
            }
        }
        finally{refreshingPinPointers=false;}
    }

    private PinnedSurface? PinAtOverlayPoint(PointerRoutedEventArgs args)
    {
        var point=TransformToVisual(null).TransformPoint(args.GetCurrentPoint(this).Position);
        var origin=App.Window!.AppWindow.Position;
        var scale=XamlRoot.RasterizationScale;
        point=new Point(origin.X+point.X*scale,origin.Y+point.Y*scale);
        PinnedSurface? top=null;
        foreach(var target in pins)
        {
            var bounds=target.Window.Bounds;
            if(!target.IsCurrent||!target.Window.IsVisible||point.X<bounds.X||point.X>=bounds.X+bounds.Width||
                point.Y<bounds.Y||point.Y>=bounds.Y+bounds.Height)continue;
            if(top is null||OverlayWindowFrame.IsAbove(target.Window.Handle,top.Window.Handle))top=target;
        }
        // A passive foreground pin must not activate a different pin behind it.
        return top;
    }

    private bool TryBeginPinPointer(PointerRoutedEventArgs args)
    {
        if(!AllowsPinPointerEntry||PinAtOverlayPoint(args) is not { } target)return false;
        var manage=args.GetCurrentPoint(this).Properties.IsRightButtonPressed;
        if(!manage&&!CanInteractWithPin(target))return false;
        if(!ProductionRoot.CapturePointer(args.Pointer))return false;
        pointerPressedPin=target;pointerPressedId=args.Pointer.PointerId;pointerPressedSession=visibleSince;pointerPressedManage=manage;
        return true;
    }

    private async void PinPointerReleased(object sender,PointerRoutedEventArgs args)
    {
        if(pointerPressedPin is not { } target||args.Pointer.PointerId!=pointerPressedId)return;
        var current=pointerPressedSession==visibleSince&&AllowsPinPointerEntry&&ReferenceEquals(PinAtOverlayPoint(args),target);
        pointerPressedPin=null;ProductionRoot.ReleasePointerCapture(args.Pointer);args.Handled=true;
        if(current)await EnterPinFromPointerAsync(target,pointerPressedManage);
    }
    private void PinPointerCanceled(object sender,PointerRoutedEventArgs args)=>pointerPressedPin=null;

    private async Task EnterPinFromPointerAsync(PinnedSurface target,bool manage=false)
    {
        if(!AllowsPinPointerEntry||!pins.Contains(target)||!target.IsCurrent||!target.Window.IsVisible||target.Window.Interactive)return;
        if(!manage&&!CanInteractWithPin(target)){HideRequested?.Invoke();return;}
        if(!ReferenceEquals(pinned,target))ExitPinnedInteraction(restoreMain:false);
        CloseTrayMenu(restoreFocus:false);
        pinned=target;
        if(manage)
        {
            ReturnFromPinnedRequested?.Invoke();
            // Direct manipulation has no launcher button to return to. Restore
            // the retained widget/rail/wheel context when adjustment finishes.
            pinManagementAdjustmentRequested=false;
            await BeginPinnedAdjustmentAsync();return;
        }
        await EnterPinnedAsync(pointerActivation:true);
        pinManagementInteractionRequested=PinnedInputActive;
        PublishControllerGuide();
    }
}
