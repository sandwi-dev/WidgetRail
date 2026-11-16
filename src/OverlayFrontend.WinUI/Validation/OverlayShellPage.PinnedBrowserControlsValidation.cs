using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyBrowserSelectFocusAsync(BrowserSurface page,Action<bool,string> check)
    {
        var current=pinned!;
        await page.EvaluateFixtureAsync("document.querySelector('input').style.visibility='hidden';document.body.insertAdjacentHTML('beforeend','<select id=pinSelect style=\"position:fixed;inset:0;width:100vw;height:100vh\"><option value=one>First</option><option value=two>Second</option></select>');");
        var bounds=current.Window.Bounds;
        await NativeContextProbeInput.ClickWindowElementAsync(current.Window.Handle,page.CompositionRoot,screenPoint:new(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d));
        await WebAppUntilScript(page,"document.activeElement.id==='pinSelect'");
        if(await page.EvaluateFixtureAsync("document.querySelector('select').matches(':open')")!="true")NativeContextProbeInput.PressAltKey(VirtualKey.Down);
        await WebAppUntilScript(page,"document.querySelector('select').matches(':open')");
        var open=await page.EvaluateFixtureAsync("document.querySelector('select').matches(':open')");
        var output=FrontendArguments.Value(Environment.GetCommandLineArgs(),"--validate-webapp-shell")!;
        await File.WriteAllTextAsync(output+".select.json",System.Text.Json.JsonSerializer.Serialize(new{
            open,PinnedInputActive,PinnedInteractionRequested,current.HasForeground,receipt=current.BorrowedBrowserFocus?.Invoke(),
            nativeBrowserPickerActive,MainFocusEnabled,foreground,nativeForeground=(long)PinnedNativeForeground(),nativeFocus=(long)PinnedNativeKeyboardFocus()}));
        check(open=="true","Physical browser input opens a real native HTML dropdown before selection");
        NativeContextProbeInput.PressKey(VirtualKey.Down);
        NativeContextProbeInput.PressKey(VirtualKey.Enter);
        await WebAppUntilScript(page,"document.querySelector('select').value==='two'");
        check(current.IsCurrent&&PinnedInputActive,"A native HTML dropdown accepts physical selection and returns input to its browser pin");
        await page.EvaluateFixtureAsync("document.querySelector('select').remove();document.querySelector('input').style.visibility='visible';");
        await NativeContextProbeInput.ClickWindowElementAsync(current.Window.Handle,page.CompositionRoot,screenPoint:new(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d));
        await WebAppUntilScript(page,"document.activeElement.id==='pinEdit'&&document.hasFocus()");
    }

    private async Task VerifyAttachedPopupFocusAsync(BrowserSurface parent,Action<bool,string> check)
    {
        var current=pinned!;
        if(current.Popup is not null)throw new InvalidOperationException("Attached-popup coverage starts with the root browser pin.");
        await PopupPreferences.SetAsync(parent.WidgetId,parent.ProfileName,"https://shell.fixture.invalid",BrowserPopupPlacement.InWidget,lifetime.Token);
        await parent.EvaluateGestureFixtureAsync("window.focusChild=window.open('https://shell.fixture.invalid/focus-child');");
        await WebAppUntil(()=>parent.PinnablePopup is {IsReady:true,IsNavigationInProgress:false});
        var child=parent.PinnablePopup!;
        check(current.IsCurrent&&current.Window.Interactive,"Opening an attached browser popup preserves the existing pin's interaction mode");
        await child.EvaluateFixtureAsync("window.childEscapes=0;document.body.innerHTML='<input id=pinChildEdit value=child style=\"position:fixed;inset:0;width:100vw;height:100vh;box-sizing:border-box\">';document.addEventListener('keydown',e=>{if(e.key==='Escape'){childEscapes++;e.preventDefault();}});");
        var bounds=current.Window.Bounds;
        await NativeContextProbeInput.ClickWindowElementAsync(current.Window.Handle,child.CompositionRoot,screenPoint:new(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d));
        await WebAppUntilScript(child,"document.activeElement.id==='pinChildEdit'&&document.hasFocus()");
        NativeContextProbeInput.PressKey(VirtualKey.End);NativeContextProbeInput.PressKey(VirtualKey.Space);
        await WebAppUntilScript(child,"document.querySelector('input').value==='child '");
        NativeContextProbeInput.Escape();await WebAppUntilScript(child,"childEscapes===1");
        check(PinnedInputActive&&ReferenceEquals(pinned,current),"The attached popup acquires its own exact focus receipt for typing and Escape inside the same pin");
        if(Environment.GetCommandLineArgs().Contains("--pin-stale-browser-focus"))
        {
            var receipt=current.BorrowedBrowserFocus;
            check(receipt?.Invoke()==true,"The attached popup has a validated current native focus receipt before delayed parent work");
            SettlePinnedBrowserFocus(current,parent);
            check(PinnedInputActive&&ReferenceEquals(current.BorrowedBrowserFocus,receipt),
                "A delayed focus callback captured for the parent cannot revoke the popup's newer valid receipt");
            NativeContextProbeInput.PressKey(VirtualKey.End);NativeContextProbeInput.PressKey(VirtualKey.Space);
            await WebAppUntilScript(child,"document.querySelector('input').value==='child  '");
        }
        await child.EvaluateGestureFixtureAsync("setTimeout(()=>window.close(),0);true;");
        await WebAppUntil(()=>parent.PinnablePopup is null&&ReferenceEquals(parent.ActiveSurface,parent));
        check(current.IsCurrent&&current.Window.Interactive,"Closing the attached popup retains the parent pin without reviving the retired popup receipt");
        await NativeContextProbeInput.ClickWindowElementAsync(current.Window.Handle,parent.CompositionRoot,screenPoint:new(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d));
        await WebAppUntilScript(parent,"document.activeElement.id==='pinEdit'&&document.hasFocus()&&pinFocusProof==='alive'");
        check(PinnedInputActive,"A physical click returns keyboard ownership to the unchanged parent browser document");
    }
}
