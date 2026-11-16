using Microsoft.UI.Xaml;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPointerPinAdjustmentAsync(Action<bool,string> check,bool transparent=true)
    {
        var current=pinned!;
        var original=current.Window.Bounds;
        var foregroundBefore=NotificationForegroundWindow();
        var siblings=pins.Where(pin=>pin!=current).Select(pin=>(pin,pin.Window.Bounds)).ToArray();
        await BeginPinnedAdjustmentAsync();
        Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame{Connected=0});
        check(PinnedAdjustmentActive,"Disconnected controller samples do not cancel desktop adjustment");
        Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame{Connected=1,State=new(){LeftThumbX=20000}});
        Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame{Connected=0});
        check(!PinnedAdjustmentActive&&current.Window.Bounds==original,"Disconnect after controller movement still cancels and restores placement");
        await BeginPinnedAdjustmentAsync();
        foregroundBefore=NotificationForegroundWindow();
        check(!current.Window.Interactive&&!current.Window.NativeInputEnabled&&current.Window.PointerPlacementHandle!=0,
            "Move mode enables only the host frame while passive widget/native browser input stays disabled");
        // Exercise a completely transparent surface, including its empty center.
        var background=(FrameworkElement)current.Window.SurfaceBackground.Parent;
        if(transparent)background.Visibility=Visibility.Collapsed;
        var start=new Point(original.X+original.Width/2d,original.Y+original.Height/2d);
        check(await NativeContextProbeInput.CursorOverWindowAsync(current.Window.PointerPlacementHandle,new(original.X+3,original.Y+3),32642)&&
            await NativeContextProbeInput.CursorOverWindowAsync(current.Window.PointerPlacementHandle,new(original.X+3,start.Y),32644)&&
            await NativeContextProbeInput.CursorOverWindowAsync(current.Window.PointerPlacementHandle,new(start.X,original.Y+3),32645)&&
            await NativeContextProbeInput.CursorOverWindowAsync(current.Window.PointerPlacementHandle,start,32512),
            "Native corner and edge cursors switch correctly and restore the arrow over the drag body");
        var delta=original.X-current.Monitor.WorkArea.X>100?-64:64;
        await NativeContextProbeInput.DragWindowAsync(current.Window.PointerPlacementHandle,start,new(start.X+delta,start.Y+32));
        var moved=current.Window.Bounds;
        check(moved.X!=original.X&&moved.Width==original.Width&&moved.Height==original.Height,
            $"Native SendInput drags the {(transparent?"transparent":"browser")} pin without resizing it: {original} => {moved}");
        check(NotificationForegroundWindow()==foregroundBefore&&!current.Window.Interactive&&PinnedAdjustmentActive,
            $"Dragging keeps main foreground and host adjustment ownership: before={foregroundBefore},after={NotificationForegroundWindow()},main={hostWindow},guard={current.Window.PointerPlacementHandle},active={PinnedAdjustmentActive}");
        start=new(moved.X+3,moved.Y+3);
        var cornerX=moved.Width>(current.Limits.MinWidth+40)*current.Monitor.Scale?36:-36;
        var cornerY=moved.Height>(current.Limits.MinHeight+32)*current.Monitor.Scale?28:-28;
        await NativeContextProbeInput.DragWindowAsync(current.Window.PointerPlacementHandle,start,new(start.X+cornerX,start.Y+cornerY));
        var resized=current.Window.Bounds;
        check(resized.Width!=moved.Width&&resized.Height!=moved.Height&&
            Math.Abs(resized.X+resized.Width-moved.X-moved.Width)<=2&&Math.Abs(resized.Y+resized.Height-moved.Y-moved.Height)<=2,
            "Native corner sizing changes both dimensions while preserving the opposite corner");
        CancelPinnedAdjustment();background.Visibility=Visibility.Visible;
        check(current.Window.Bounds==original&&!current.Window.NativeInputEnabled&&!current.Window.Interactive,
            "Cancel restores exact pre-drag geometry and passive click-through input");
        await BeginPinnedAdjustmentAsync();
        start=new(original.X+original.Width/2d,original.Y+original.Height/2d);
        await NativeContextProbeInput.DragWindowAsync(current.Window.PointerPlacementHandle,start,new(start.X+delta,start.Y+24));
        var saved=current.Window.Bounds;
        await FinishPinnedAdjustmentAsync(true);
        check(current.Window.Bounds==saved&&saved!=original&&!PinnedAdjustmentActive&&!current.Window.NativeInputEnabled,
            "Save commits the native drag and leaves the pin passive");
        var placement=(await pinnedStore!.LoadAsync()).Find(current.WidgetId,current.LayoutId);
        check(placement is not null&&PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(),placement,current.Limits)?.Bounds==saved,
            "Saved native drag uses the existing monitor/DIP placement key");
        check(siblings.All(item=>item.pin.Window.Bounds==item.Bounds),"Pointer adjustment leaves every other pin unchanged");
        await BeginPinnedAdjustmentAsync();
        NativeContextProbeInput.Escape();await Task.Delay(150);
        check(!PinnedAdjustmentActive&&current.Window.Bounds==saved,"Physical Escape cancels adjustment without losing its saved placement");
        var guide=GuideRegion.TransformToVisual(null).TransformBounds(new(0,0,GuideRegion.ActualWidth,GuideRegion.ActualHeight));
        var mainOrigin=App.Window!.AppWindow.Position;var mainScale=XamlRoot.RasterizationScale;
        var guideCenter=new Point(mainOrigin.X+(guide.X+guide.Width/2)*mainScale,mainOrigin.Y+(guide.Y+guide.Height/2)*mainScale);
        foreach(var commit in new[]{false,true})
        {
            await BeginPinnedAdjustmentAsync();
            var before=current.Window.Bounds;
            await NativeContextProbeInput.DragWindowAsync(current.Window.PointerPlacementHandle,
                new(before.X+before.Width/2d,before.Y+before.Height/2d),guideCenter);
            var overlapping=current.Window.Bounds;
            check(guideCenter.X>=overlapping.X&&guideCenter.X<overlapping.X+overlapping.Width&&
                guideCenter.Y>=overlapping.Y&&guideCenter.Y<overlapping.Y+overlapping.Height,
                "Pointer fixture places the actual pin and guard over the overlay command area");
            NativeContextProbeInput.PressKey(commit?Windows.System.VirtualKey.Enter:Windows.System.VirtualKey.Escape);
            await Task.Delay(200);
            check(!PinnedAdjustmentActive&&current.Window.PointerPlacementHandle==0&&current.Window.Bounds==(commit?overlapping:before),
                commit?"Enter saves even while the pin covers the mouse Save button":"Escape cancels even while the pin covers the mouse Cancel button");
        }
        saved=current.Window.Bounds;
        await BeginPinnedAdjustmentAsync();
        start=new(saved.X+saved.Width/2d,saved.Y+saved.Height/2d);
        var guard=current.Window.PointerPlacementHandle;
        await NativeContextProbeInput.DragWindowAsync(guard,start,new(start.X+delta,start.Y+16),()=>OnUi(()=>
        {
            check(NativeContextProbeInput.GetCapture()==guard,"Native drag capture belongs only to the temporary host input frame");
            NativeContextProbeInput.ReleaseCapture();
            return Task.CompletedTask;
        }));
        check(current.Window.Bounds==saved&&PinnedAdjustmentActive,"Capture loss stops subsequent mouse movement without changing the saved pin");
        CancelPinnedAdjustment();
        await BeginPinnedAdjustmentAsync();
        guard=current.Window.PointerPlacementHandle;
        await NativeContextProbeInput.DragWindowAsync(guard,start,new(start.X+delta,start.Y+16),()=>OnUi(async()=>
        {
            NativeContextProbeInput.Escape();await Task.Delay(100);
        }));
        check(!PinnedAdjustmentActive&&current.Window.PointerPlacementHandle==0&&current.Window.Bounds==saved&&
            NativeContextProbeInput.GetCapture()==0,"Escape while held releases capture and rejects remaining drag motion");
        await BeginPinnedAdjustmentAsync();
        guard=current.Window.PointerPlacementHandle;
        await NativeContextProbeInput.DragWindowAsync(guard,start,new(start.X+delta,start.Y+16),()=>OnUi(()=>UnpinAsync(save:false,current)));
        check(!pins.Contains(current)&&!PinnedAdjustmentActive&&NativeContextProbeInput.GetCapture()==0&&
            siblings.All(item=>pins.Contains(item.pin)&&item.pin.Window.Bounds==item.Bounds),
            "Closing the pin while held releases capture without moving or closing siblings");

        Task OnUi(Func<Task> action)
        {
            var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if(!DispatcherQueue.TryEnqueue(async()=>{try{await action();done.SetResult();}catch(Exception error){done.SetException(error);}}))
                done.SetException(new InvalidOperationException("Fixture dispatcher unavailable."));
            return done.Task;
        }
    }
}
