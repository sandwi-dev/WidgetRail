using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPinFocusHandoffIsolationAsync(Action<bool,string> check)
    {
        var current=pinned!;
        var receipt=current.BorrowedBrowserFocus??throw new InvalidOperationException("Expected an exact native browser focus receipt.");
        var beforeFocus=FocusManager.GetFocusedElement(XamlRoot);
        var beforeVersion=selectionVersion;var beforeWidget=activeWidget;var beforeRequested=requestedWidget;
        var beforeInteractive=interactive;var beforeRadial=RadialOpen;var beforeMenu=trayMenu;
        try
        {
            // Keep the live browser and native focus, but deterministically hold
            // the authority gap that exists while a native transfer is settling.
            current.BorrowedBrowserFocus=()=>false;
            check(PinnedInteractionRequested&&!PinnedInputActive,"Fixture holds requested pin input without a validated native focus receipt");
            Receive(new ControllerFrame{Connected=1,PressedButtons=0x1000,
                State=new(){Buttons=0x1000,LeftThumbX=24000,RightThumbY=24000},
                DpadNavigation=new(){Direction=NavigationDirection.Down,Phase=NavigationPhase.Pressed}});
            await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.RightBumper,ControllerEventPhase.Pressed);
            await Task.Delay(100);
            check(Unchanged()&&shellGestures.Owns(ControllerButton.A)&&shellGestures.Owns(ControllerButton.B)&&
                shellGestures.Owns(ControllerButton.RightBumper),
                "Unvalidated pin focus consumes controller navigation/buttons without changing the main widget, rail or focus");
        }
        finally{current.BorrowedBrowserFocus=receipt;}
        Receive(new ControllerFrame{Connected=1,ReleasedButtons=0x1000,State=new(){Buttons=0x2200}});
        await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
        await RouteButtonAsync(ControllerButton.RightBumper,ControllerEventPhase.Released);
        check(PinnedInputActive&&Unchanged()&&!shellGestures.Owns(ControllerButton.A)&&!shellGestures.Owns(ControllerButton.B)&&
            !shellGestures.Owns(ControllerButton.RightBumper),
            "Restoring the exact receipt resumes pin input and consumes old releases without replay to main");
        bool Unchanged()=>selectionVersion==beforeVersion&&activeWidget==beforeWidget&&requestedWidget==beforeRequested&&
            interactive==beforeInteractive&&RadialOpen==beforeRadial&&ReferenceEquals(trayMenu,beforeMenu)&&
            ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),beforeFocus);
    }
}
