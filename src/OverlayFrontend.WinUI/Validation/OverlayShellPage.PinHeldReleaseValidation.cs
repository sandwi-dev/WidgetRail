using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyPinHeldControllerReleaseAsync(BrowserSurface page,Action<bool,string> check)
    {
        var current=pinned!;
        await NativeContextProbeInput.WithPointerParkedAsync(new(current.Monitor.WorkArea.X+2,current.Monitor.WorkArea.Y+2),async()=>
        {
        await page.EvaluateFixtureAsync("window.__heldMarkup=document.body.innerHTML;window.__heldClicks=0;window.__heldDowns=0;document.body.innerHTML='<button style=\"position:fixed;inset:0;width:100vw;height:100vh\" onmousedown=\"window.__heldDowns++\" onclick=\"window.__heldClicks++\">Gesture probe</button>';");
        try
        {
            await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
            await WebAppUntilScript(page,"__heldDowns===1");
            await Task.Delay(120);
            check(page.ControllerPointerHeldForValidation,"A valid controller press reaches the browser before focus proof is lost");
            var receipt=current.BorrowedBrowserFocus;
            var nativeOwned=receipt is null&&current.HasForeground;
            try
            {
                current.HasForeground=false;current.BorrowedBrowserFocus=()=>false;
                check(PinnedInteractionRequested&&!PinnedInputActive,"The earlier browser press remains held while its focus receipt is deliberately invalid");
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                var cancelledEpoch=page.ControllerPointerEpochForValidation;
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                check(page.ControllerPointerEpochForValidation==cancelledEpoch,
                    "Duplicate unvalidated releases do not requeue or regenerate the browser cancellation");
                try{await WebAppUntil(()=>!page.ControllerPointerHeldForValidation);}
                catch(Exception error)
                {
                    throw new InvalidOperationException("Browser press after unvalidated release: "+System.Text.Json.JsonSerializer.Serialize(new{
                        held=page.ControllerPointerHeldForValidation,PinnedInputActive,PinnedInteractionRequested,
                        dom=await page.EvaluateFixtureAsync("JSON.stringify({down:__heldDowns,click:__heldClicks})")}),error);
                }
                check(await page.EvaluateFixtureAsync("__heldClicks===0")=="true",
                    "A release while focus is unvalidated cancels the earlier browser press without a click");
            }
            finally{current.HasForeground=nativeOwned;current.BorrowedBrowserFocus=receipt;}
            await WebAppUntil(()=>PinnedInputActive);
            await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
            await WebAppUntilScript(page,"__heldDowns===2");
            await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
            await WebAppUntilScript(page,"__heldClicks===1");
            check(!page.ControllerPointerHeldForValidation,
                "The next fresh controller click completes exactly once after focus proof resumes");
        }
        finally{await page.EvaluateFixtureAsync("document.body.innerHTML=window.__heldMarkup;delete window.__heldMarkup;");}
        });
    }
}
