using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal static partial class NativeContextProbeInput
{
    internal static string? LastPointerTrace { get; private set; }
    internal static async Task ClickWindowElementAsync(nint window,FrameworkElement target,FrameworkElement? coordinateRoot=null,Windows.Foundation.Point? screenPoint=null,bool right=false)
    {
        DemandOwnForeground();GetWindowThreadProcessId(window,out var process);
        if(process!=Environment.ProcessId)throw new InvalidOperationException("Click target is not owned by the fixture.");
        NativePoint screen;
        if(screenPoint is { } exact)screen=new(){X=(int)exact.X,Y=(int)exact.Y};
        else if(coordinateRoot is not null)
        {
            var point=target.TransformToVisual(coordinateRoot).TransformPoint(new(target.ActualWidth/2,target.ActualHeight/2));
            var scale=coordinateRoot.XamlRoot.RasterizationScale;
            screen=new(){X=(int)(point.X*scale),Y=(int)(point.Y*scale)};
            if(ClientToScreen(window,ref screen)==0)throw new InvalidOperationException("Pin coordinates unavailable.");
        }
        else screen=ScreenCenter(target);
        if(GetCursorPos(out var previous)==0)throw new InvalidOperationException("Pointer coordinates unavailable.");
        var held=false;
        try
        {
            await ContinuePointerMoveAsync(screen,previous);
            if(screenPoint is null&&coordinateRoot is null)
            {
                // Opening dialogs can still finish their layout while mouse
                // admission wakes. Use the intended control's current bounds.
                var latest=ScreenCenter(target);
                if(latest.X!=screen.X||latest.Y!=screen.Y)
                {screen=latest;SendPointerMove(screen);await Task.Delay(80);}
            }
            GetWindowThreadProcessId(WindowFromPoint(screen),out var hitProcess);
            if(hitProcess!=Environment.ProcessId)throw new InvalidOperationException($"Pointer target {screen.X},{screen.Y} is outside the fixture.");
            LastPointerTrace=$"click={screen.X},{screen.Y};hitRoot={GetAncestor(WindowFromPoint(screen),2)};expectedRoot={window};foreground={GetForegroundWindow()}";
            if((screenPoint is not null||coordinateRoot is not null)&&GetAncestor(WindowFromPoint(screen),2)!=window)
                throw new InvalidOperationException($"Expected pin {window} at {screen.X},{screen.Y}, hit root {GetAncestor(WindowFromPoint(screen),2)}.");
            if(right)SendMouse();
            else{SendLeftButton(up:false);held=true;await Task.Delay(60);SendLeftButton(up:true);held=false;}
            await Task.Delay(120);
        }
        finally{if(held)SendLeftButton(up:true);SendPointerMove(previous);}
    }
    internal static async Task HoverAsync(FrameworkElement target,Func<Task> verify)
    {
        DemandOwnForeground();
        if(GetCursorPos(out var previous)==0)throw new InvalidOperationException("Pointer position unavailable.");
        var screen=ScreenCenter(target);
        try
        {
            await ContinuePointerMoveAsync(screen,previous);GetCursorPos(out var actual);
            LastPointerTrace=$"target={screen.X},{screen.Y};actual={actual.X},{actual.Y};size={target.ActualWidth},{target.ActualHeight};scale={target.XamlRoot.RasterizationScale};hit={target.IsHitTestVisible}";
            await verify();
        }
        finally{SendPointerMove(previous);}
    }
    internal static async Task HoverTransitAsync(FrameworkElement source, Func<FrameworkElement> destination,
        Func<Task> opened, Func<Task> verify)
    {
        DemandOwnForeground();
        if(GetCursorPos(out var previous)==0)throw new InvalidOperationException("Pointer coordinates unavailable.");
        try
        {
            var start=ScreenCenter(source);await ContinuePointerMoveAsync(start,previous);await opened();
            var end=ScreenCenter(destination());
            for(var step=1;step<=16;step++)
            {
                DemandOwnForeground();
                SendPointerMove(new(){X=start.X+(end.X-start.X)*step/16,Y=start.Y+(end.Y-start.Y)*step/16});
                await Task.Delay(40);
            }
            await Task.Delay(300);await verify();
        }
        finally{SendPointerMove(previous);}
    }

    private static async Task ContinuePointerMoveAsync(NativePoint target,NativePoint previous)
    {
        // Guarantee a changed first position even when a previous run left the
        // cursor at this target. The admission guard consumes that wake move;
        // subsequent motion must reach the newly exposed native control.
        SendPointerMove(new(){X=previous.X==target.X+8?target.X-8:target.X+8,Y=target.Y});await Task.Delay(80);
        SendPointerMove(target);await Task.Delay(80);
        SendPointerMove(new(){X=target.X+1,Y=target.Y});await Task.Delay(80);
    }
    private static NativePoint ScreenCenter(FrameworkElement target)
    {
        // UIA includes the ContentDialog popup's screen offset. Transforming to
        // a null visual root gives popup-local coordinates, not main-client ones.
        var bounds=FrameworkElementAutomationPeer.CreatePeerForElement(target)?.GetBoundingRectangle()
            ??throw new InvalidOperationException("The pointer target has no automation peer.");
        if(bounds.Width<=0||bounds.Height<=0)throw new InvalidOperationException("The pointer target is not visible.");
        return new(){X=(int)(bounds.X+bounds.Width/2),Y=(int)(bounds.Y+bounds.Height/2)};
    }
}
