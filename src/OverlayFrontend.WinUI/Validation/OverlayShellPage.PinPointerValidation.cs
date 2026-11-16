using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Validation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task ClickPinForValidationAsync(PinnedSurface target,Action<bool,string> check)
    {
        RefreshPinPointerAdmission();
        var guard=target.Window.PointerActivationHandle;
        check(guard!=0&&!target.Window.NativeInputEnabled,"Eligible inactive pin has a host activation target with content input still disabled");
        var bounds=target.Window.Bounds;
        var point=new Windows.Foundation.Point(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d);
        // The full-screen transparent main window receives background clicks on
        // its monitor. The pin frame receives clicks outside that window.
        var main=App.Window!.AppWindow;
        var hit=point.X>=main.Position.X&&point.X<main.Position.X+main.Size.Width&&
            point.Y>=main.Position.Y&&point.Y<main.Position.Y+main.Size.Height?(nint)hostWindow:guard;
        var contentPresses=0;
        PointerEventHandler observed=(_,_)=>contentPresses++;
        target.Window.AutomationRoot.AddHandler(UIElement.PointerPressedEvent,observed,true);
        try{await NativeContextProbeInput.ClickWindowElementAsync(hit,target.Window.AutomationRoot,screenPoint:point);}
        finally{target.Window.AutomationRoot.RemoveHandler(UIElement.PointerPressedEvent,observed);}
        await WebAppUntil(()=>ReferenceEquals(pinned,target)&&PinnedInputActive);
        check(contentPresses==0,"The activating click is consumed and never presses the pin's content");
        check(target.Window.PointerActivationHandle==0&&target.Window.NativeInputEnabled&&pins.Count(pin=>pin.Window.Interactive)==1,
            "A physical click selects this pin without a menu and grants input only to this pin");
    }

    private async Task VerifyPinPointerSwitchingAsync(Action<bool,string> check)
    {
        var eligible=pins.Where(CanInteractWithPin).Take(2).ToArray();
        foreach(var target in eligible)await ClickPinForValidationAsync(target,check);
        ExitPinnedInteraction(restoreMain:true);
        await WebAppUntil(()=>foreground&&!PinnedInteractionRequested);
        var peer=PinnedDisplayAreas.Read().FirstOrDefault(monitor=>monitor.Id!=eligible[0].Monitor.Id);
        if(peer is not null)
        {
            var target=eligible[0];var original=target.Window.Bounds;
            target.Window.Place(original with{X=peer.WorkArea.X+40,Y=peer.WorkArea.Y+40});
            await ClickPinForValidationAsync(target,check);
            check(target.IsCurrent,"Click activation works on a second physical monitor outside the main overlay window");
            ExitPinnedInteraction(restoreMain:true);target.Window.Place(original);
            await WebAppUntil(()=>foreground&&!PinnedInteractionRequested);
        }
        check(eligible.All(pin=>pin.Window.PointerActivationHandle!=0),"Returning to main rearms click activation for every eligible pin");
        var dialog=ChoosePinAsync("Pointer admission test",_=>Task.CompletedTask);
        await WebAppUntil(()=>HostChoiceActive);
        RefreshPinPointerAdmission();
        check(pins.All(pin=>pin.Window.PointerActivationHandle==0),"An open host chooser suspends pin activation targets");
        hostChoiceDialog!.Hide();await dialog;
        RefreshPinPointerAdmission();
        SetVisible(false);
        check(pins.All(pin=>pin.Window.PointerActivationHandle==0&&!pin.Window.Interactive),"Hiding the overlay removes every activation target and leaves pins click-through");
        SetVisible(true);await WebAppUntil(()=>MainFocusEnabled);
        RefreshPinPointerAdmission();
        check(eligible.All(pin=>pin.Window.PointerActivationHandle!=0),"Reopening restores activation targets without recreating pins");
    }

    private async Task VerifyBackdropBandAsync(Action<bool,string> check)
    {
        var main=(MainWindow)App.Window!;
        var backdrop=main.BackdropHandleForValidation;
        var ordinary=new Window{Title="WidgetRail ordinary-window ordering fixture",Content=new TextBlock{Text="Ordinary foreground app"}};
        try
        {
            ordinary.Activate();
            var other=WinRT.Interop.WindowNative.GetWindowHandle(ordinary);
            OverlayWindowFrame.DemoteForValidation(backdrop);
            OverlayWindowFrame.PlaceBelow(backdrop,other);
            check(OverlayWindowFrame.IsAbove((nint)hostWindow,backdrop)&&OverlayWindowFrame.IsAbove(other,backdrop),
                "Reproduced: ordinary foreground window covers a demoted scrim while the old relative-main constraint still passes");
            SurfaceAppearanceChanged?.Invoke();
            check(OverlayWindowFrame.IsAbove(backdrop,other)&&OverlayWindowFrame.IsAbove((nint)hostWindow,backdrop),
                "Appearance reconciliation restores the scrim above ordinary foreground windows and below the overlay");
            var writes=OverlayWindowFrame.ZOrderWritesForValidation;
            var frames=OverlayWindowFrame.FrameRefreshesForValidation;
            for(var cycle=0;cycle<30;cycle++)SurfaceAppearanceChanged?.Invoke();
            check(writes==OverlayWindowFrame.ZOrderWritesForValidation&&frames==OverlayWindowFrame.FrameRefreshesForValidation,
                "Thirty repeated foreground-app refreshes produce zero additional order writes or frame refreshes");
        }
        finally{ordinary.Close();main.Activate();}
        await WebAppUntil(()=>foreground);
    }
}
