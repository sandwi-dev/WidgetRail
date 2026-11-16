using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using Windows.System;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePinnedKeyboardValidation(string output)
    {
        var started=false;
        Loaded+=async(_,_)=>
        {
            if(started)return;started=true;
            var checks=new List<string>();Exception? failure=null;object? failureContext=null;HostChoiceDialog? interactionMenu=null;
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>foreground&&activeWidget is not null&&!switching);
                var widget=activeWidget!;
                await PinAsync(widget,"gallery.summary");var first=pinned!;
                if(Environment.GetCommandLineArgs().Contains("--pin-return-only"))
                {
                    await SelectAsync(widget,false);FocusTray();App.Window!.Activate();
                    await WebAppUntil(()=>foreground&&!switching);
                    await EnterPinnedAsync();
                    await WebAppUntil(()=>PinnedInputActive&&first.FocusAcquired is null&&ReturnFromPinButton.IsEnabled);
                    Check(NotificationForegroundWindow()==first.Window.Handle,"Production pin entry establishes the expected native foreground for the isolated Return phase");
                    await VerifyNativePinReturnAndMenusAsync(first,Check,output);
                    await UnpinAsync(save:false,first);
                    return;
                }
                await PinAsync(widget,"gallery.level");var second=pinned!;
                await SelectAsync(widget,false);FocusTray();
                await WebAppUntil(()=>foreground&&ManagePinsButton.IsEnabled);
                ManagePinsButton.Focus(FocusState.Keyboard);NativeContextProbeInput.PressKey(VirtualKey.Enter);
                await WebAppUntil(()=>hostChoiceDialog?.IsOpenForValidation==true);
                var chooser=hostChoiceDialog!;
                var firstItem=FindChoice(chooser,first.ChoiceId);var secondItem=FindChoice(chooser,second.ChoiceId);
                await WebAppUntil(()=>chooser.FocusedChoiceId==second.ChoiceId);
                NativeContextProbeInput.PressKey(VirtualKey.Up);
                await WebAppUntil(()=>chooser.FocusedChoiceId==first.ChoiceId&&first.Window.FocusIndicator.Visibility==Visibility.Visible);
                var originalFocus=FocusManager.GetFocusedElement(chooser.XamlRoot);
                var originalSelection=ChoiceDescendants(chooser).OfType<ListView>().Single().SelectedItem;
                var pointerEvents=new List<string>();
                secondItem.AddHandler(UIElement.PointerEnteredEvent,new PointerEventHandler((_,_)=>pointerEvents.Add("entered")),true);
                secondItem.AddHandler(UIElement.PointerExitedEvent,new PointerEventHandler((_,_)=>pointerEvents.Add("exited")),true);
                await NativeContextProbeInput.HoverAsync(secondItem,async()=>
                {
                    try{await WebAppUntil(()=>second.Window.FocusIndicator.Visibility==Visibility.Visible&&first.Window.FocusIndicator.Visibility==Visibility.Collapsed);}
                    catch(Exception error){throw new InvalidOperationException(JsonSerializer.Serialize(new{pointerEvents,chooser.PreviewChoiceId,chooser.FocusedChoiceId,chooser.PreviewHoveredChoices,
                        first=first.Window.FocusIndicator.Visibility.ToString(),second=second.Window.FocusIndicator.Visibility.ToString(),pointer=NativeContextProbeInput.LastPointerTrace}),error);}
                    Check(ReferenceEquals(originalFocus,FocusManager.GetFocusedElement(chooser.XamlRoot))&&chooser.SelectedId is null&&
                        ReferenceEquals(originalSelection,ChoiceDescendants(chooser).OfType<ListView>().Single().SelectedItem),
                        "Actual pointer hover outlines its pin without changing keyboard focus or confirming a choice");
                });
                await WebAppUntil(()=>first.Window.FocusIndicator.Visibility==Visibility.Visible&&second.Window.FocusIndicator.Visibility==Visibility.Collapsed);
                Check(true,"Pointer exit restores the keyboard-focused pin outline");
                await NativeContextProbeInput.HoverAsync(secondItem,async()=>
                {
                    await WebAppUntil(()=>chooser.PreviewChoiceId==second.ChoiceId);
                    NativeContextProbeInput.PressKey(VirtualKey.Down);
                    await WebAppUntil(()=>chooser.FocusedChoiceId==second.ChoiceId);
                    NativeContextProbeInput.PressKey(VirtualKey.Up);
                    await WebAppUntil(()=>chooser.PreviewChoiceId==first.ChoiceId&&first.Window.FocusIndicator.Visibility==Visibility.Visible);
                    Check(true,"Physical keyboard navigation takes preview ownership back from hover");
                });
                NativeContextProbeInput.Escape();await WebAppUntil(()=>!HostChoiceActive);
                Check(pins.All(pin=>pin.Window.FocusIndicator.Visibility==Visibility.Collapsed),"Cancel clears every chooser outline");
                PinnedSurface? invoked=null;
                var stale=ChoosePinAsync("Choose a pin",target=>{invoked=target;return Task.CompletedTask;},selectTarget:false);
                await WebAppUntil(()=>hostChoiceDialog?.IsOpenForValidation==true);
                chooser=hostChoiceDialog!;secondItem=FindChoice(chooser,second.ChoiceId);
                await NativeContextProbeInput.HoverAsync(secondItem,async()=>
                {
                    await WebAppUntil(()=>second.Window.FocusIndicator.Visibility==Visibility.Visible);
                    await UnpinAsync(save:false,second);
                    if(hostChoiceDialog==chooser)chooser.CompleteChoice(second.ChoiceId);
                });
                await stale;
                Check(invoked is null&&pins.Contains(first)&&!first.Window.Interactive,
                    "Closing the hovered pin rejects its stale choice without retargeting the surviving pin");
                await SelectAsync(widget,false);FocusTray();App.Window!.Activate();
                await WebAppUntil(()=>foreground&&!switching&&!openingPinManagement&&ManagePinsButton.IsEnabled);
                ManagePinsButton.Focus(FocusState.Keyboard);
                await WebAppUntil(()=>NotificationForegroundWindow()==(nint)hostWindow&&ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot),ManagePinsButton));
                NativeContextProbeInput.PressControlShiftKey(VirtualKey.I);
                await WebAppUntil(()=>PinnedInputActive&&ReferenceEquals(pinned,first));
                Check(!HostChoiceActive,"The direct keyboard command enters the selected native pin without a management menu");
                var root=(Grid)first.Window.AutomationRoot;
                var editor=new TextBox{Text="Keep editing",Width=180,Height=40,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
                root.Children.Add(editor);await WebAppUntil(()=>editor.IsLoaded);editor.Focus(FocusState.Keyboard);
                NativeContextProbeInput.Escape();await Task.Delay(120);
                Check(PinnedInputActive&&editor.Text=="Keep editing","Escape in a native text editor preserves pin interaction and text");
                root.Children.Remove(editor);
                var handled=new Button{Content="Handled Escape",Width=180,Height=40,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
                var handledCount=0;handled.KeyDown+=(_,args)=>{if(args.Key==VirtualKey.Escape){handledCount++;args.Handled=true;}};
                root.Children.Add(handled);await WebAppUntil(()=>handled.IsLoaded);handled.Focus(FocusState.Keyboard);
                NativeContextProbeInput.Escape();await Task.Delay(120);
                Check(handledCount==1&&PinnedInputActive,"A child-handled Escape never returns from the native pin");
                root.Children.Remove(handled);first.Enter();
                NativeContextProbeInput.Escape();await WebAppUntil(()=>!PinnedInputActive&&foreground);
                Check(!first.Window.Interactive&&first.IsCurrent,"Unhandled Escape returns to the main overlay without closing the pin");
                ManagePinsButton.Focus(FocusState.Keyboard);NativeContextProbeInput.PressControlShiftKey(VirtualKey.I);
                await WebAppUntil(()=>PinnedInputActive&&first.FocusAcquired is null&&ReturnFromPinButton.IsEnabled);
                Check(!HostChoiceActive,"Physical keyboard interaction shortcut re-enters the sole eligible pin without an action menu");
                await VerifyNativePinReturnAndMenusAsync(first,Check,output);
                await UnpinAsync(save:false,first);
            }
            catch(Exception error)
            {
                failure=error;
                failureContext=new{foreground,visible,PinnedInputActive,PinnedInteractionRequested,HostChoiceActive,
                    dialog=hostChoiceDialog?.Title?.ToString(),selectedInteraction=interactionMenu?.SelectedId,
                    adjustment=pinnedAdjustment is null?"none":"adjust",
                    nativeForeground=(long)NotificationForegroundWindow(),main=(long)hostWindow,pin=(long?)pinned?.Window.Handle,
                    focus=XamlRoot is {} activeRoot?FocusManager.GetFocusedElement(activeRoot)?.GetType().Name:null,hit=NativeContextProbeInput.LastPointerTrace};
            }
            finally
            {
                ExitPinnedInteraction(restoreMain:true);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure?.ToString(),context=failureContext}));
            }
            void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
        };
    }
    private async Task VerifyNativePinReturnAndMenusAsync(PinnedSurface first,Action<bool,string> check,string output)
    {
        var returnScreen=Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(ReturnFromPinButton)!.GetBoundingRectangle();
        pinReturnGapTraceForValidation=[];
        await File.WriteAllTextAsync(output+".return-before.json",JsonSerializer.Serialize(new{
            main=(long)hostWindow,mainBounds=new{x=App.Window!.AppWindow.Position.X,y=App.Window.AppWindow.Position.Y,width=App.Window.AppWindow.Size.Width,height=App.Window.AppWindow.Size.Height},
            pin=(long)first.Window.Handle,pinBounds=first.Window.Bounds,
            returnBounds=new{x=returnScreen.X,y=returnScreen.Y,width=returnScreen.Width,height=returnScreen.Height},ReturnFromPinButton.IsEnabled}));
        await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ReturnFromPinButton);
        await File.WriteAllTextAsync(output+".return-after.json",JsonSerializer.Serialize(new{
            foreground,visible,PinnedInputActive,HostChoiceActive,hit=NativeContextProbeInput.LastPointerTrace,gap=pinReturnGapTraceForValidation}));
        await WebAppUntil(()=>foreground&&!PinnedInputActive);
        check(first.IsCurrent,"Clicking the visible main-overlay return action safely leaves native pin interaction");
        await Task.Delay(120);
        if(HostChoiceActive)throw new InvalidOperationException(System.Text.Json.JsonSerializer.Serialize(new{
            failure="Return click opened an unsolicited host dialog",title=hostChoiceDialog?.Title?.ToString(),
            openingPinManagement,owner=hostChoicePin?.ChoiceId,PinnedInputActive,foreground,
            manageVisible=ManagePinsButton.Visibility.ToString(),returnVisible=ReturnFromPinButton.Visibility.ToString()}));
        check(true,"A physical return click never activates Manage after the foreground transition");
        UpdateLayout();
        check(ReturnFromPinButton.Visibility==Visibility.Collapsed&&ManagePinsButton.Visibility==Visibility.Visible&&
            PinManagementCommands.DesiredSize.Width<=ManagePinsButton.ActualWidth+1,
            "Return collapses after routing the click and releases its reserved command width");
        foreach(var opacity in new[]{false,true})
        foreach(var save in new[]{false,true})
        {
            await EnterPinnedAsync();await WebAppUntil(()=>PinnedInputActive);
            var manage=ManagePinAsync(first,()=>first.IsCurrent&&visible);
            await manage;
            await WebAppUntil(()=>PinnedAdjustmentActive&&pinAdjustmentToolbar?.Save.IsLoaded==true&&
                NotificationForegroundWindow()==pinAdjustmentToolbar.Window.Handle);
            if(opacity)SetPinnedOpacity(55);else StepPinnedPlacement(PinnedPlacementDirection.Left,false);
            NativeContextProbeInput.PressKey(save?VirtualKey.Enter:VirtualKey.Escape);
            await WebAppUntil(()=>!PinnedAdjustmentActive);
            check(first.IsCurrent&&!first.Window.Interactive,
                $"Pin-owned management opens the shared toolbar for {(opacity?"opacity":"geometry")} edits and {(save?"Enter saves":"Escape cancels")}");
        }
    }

    private static ListViewItem FindChoice(HostChoiceDialog dialog,string id)=>ChoiceDescendants(dialog).OfType<ListViewItem>().Single(item=>item.Tag as string==id);
    private static IEnumerable<DependencyObject> ChoiceDescendants(DependencyObject parent)
    {
        yield return parent;
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach(var child in ChoiceDescendants(VisualTreeHelper.GetChild(parent,i)))yield return child;
    }
    private async Task VerifyBrowserPinKeyboardReturnAsync(BrowserSurface page,Action<bool,string> check)
    {
        var current=pinned!;
        pinnedKeyboardTraceForValidation=[];
        var navigations=page.NavigationCount;
        await page.EvaluateFixtureAsync("window.escapeKeys=0;window.pinFocusProof='alive';document.body.innerHTML='<input id=pinEdit value=unchanged style=\"position:fixed;inset:0;width:100vw;height:100vh;box-sizing:border-box\">';document.addEventListener('keydown',e=>{if(e.key==='Escape'){escapeKeys++;e.preventDefault();}});");
        await FocusEditor();
        await VerifyPinFocusHandoffIsolationAsync(check);
        if(Environment.GetCommandLineArgs().Contains("--pin-held-release"))
        {await VerifyPinHeldControllerReleaseAsync(page,check);await FocusEditor();}
        NativeContextProbeInput.PressKey(VirtualKey.End);
        NativeContextProbeInput.PressKey(VirtualKey.Space);
        await WebAppUntilScript(page,"document.querySelector('input').value==='unchanged '");
        check(PinnedInputActive,"A physical click and keyboard typing reach the browser editor while the exact pin retains interaction");
        NativeContextProbeInput.Escape();
        await Task.Delay(200);
        if(await page.EvaluateFixtureAsync("escapeKeys===1")!="true")throw new InvalidOperationException(
            "Native Escape did not reach the page: "+JsonSerializer.Serialize(new{PinnedInputActive,native=page.HasNativeKeyboardFocus,trace=pinnedKeyboardTraceForValidation}));
        check(PinnedInputActive&&await page.EvaluateFixtureAsync("document.querySelector('input').value==='unchanged '")=="true",
            "A browser-handled Escape remains in the page and preserves its editor instead of exiting the pin");
        if(Environment.GetCommandLineArgs().Contains("--pin-browser-select"))await VerifyBrowserSelectFocusAsync(page,check);
        if(Environment.GetCommandLineArgs().Contains("--pin-attached-popup"))await VerifyAttachedPopupFocusAsync(page,check);
        var alert=page.EvaluateGestureFixtureAsync("alert('Synthetic pin focus check');window.pinAlertDone=true;");
        await WebAppUntil(()=>hostChoiceDialog?.IsOpenForValidation==true&&ReferenceEquals(hostChoicePin,current));
        check(NotificationForegroundWindow()==current.Window.Handle&&PinnedInputActive,
            "A real page alert transfers the browser focus receipt to a native dialog owned by the same pin");
        hostChoiceDialog!.CompleteChoice("accept");
        await alert;
        await WebAppUntil(()=>!HostChoiceActive&&!page.HasDialog);
        await WebAppUntilScript(page,"pinAlertDone===true");
        await FocusEditor();
        await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ReturnFromPinButton);
        await WebAppUntil(()=>foreground&&!PinnedInputActive);
        check(current.IsCurrent&&!current.Window.Interactive,"A physical main-overlay return click ends browser interaction without closing its pin");

        await ClickPinForValidationAsync(current,check);await FocusEditor();
        if(long.TryParse(FrontendArguments.Value(Environment.GetCommandLineArgs(),"--pin-keyboard-peer"),out var peer)&&peer!=0)
        {
            WinUIEx.HwndExtensions.SetForegroundWindow((nint)peer);
            await WebAppUntil(()=>NotificationForegroundWindow()==(nint)peer&&!PinnedInputActive&&!current.Window.Interactive);
            check(current.IsCurrent,"External foreground loss immediately revokes the browser focus receipt while retaining the passive pin");
            NativePickerVisibilityForValidation!(true);
            await WebAppUntil(()=>visible&&foreground&&!switching);
            await ClickPinForValidationAsync(current,check);await FocusEditor();
            check(await page.EvaluateFixtureAsync("document.querySelector('input').value==='unchanged '")=="true",
                "Explicit re-entry after an external app preserves the browser editor and acquires fresh input ownership");
        }
        await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ReturnFromPinButton);
        await WebAppUntil(()=>foreground&&!PinnedInputActive);
        var old=current;var popup=current.Popup is not null;var widget=current.WidgetId;var layout=current.LayoutId;
        await UnpinAsync(save:false,current);
        await WebAppUntil(()=>!pins.Contains(old));
        if(popup)await PinBrowserPopupAsync(widget,expectedChild:page);
        else await PinAsync(widget,layout);
        current=pinned??throw new InvalidOperationException("Browser re-pin did not open.");
        await WebAppUntil(()=>popup?current.Popup?.Child==page:Slots(current.Presenter).Any(slot=>slot.Surface==page));
        check(!ReferenceEquals(current,old)&&page.NavigationCount==navigations&&await page.EvaluateFixtureAsync("pinFocusProof==='alive'")=="true",
            "Unpin and re-pin transfer the same browser document into a fresh pin without navigation or stale ownership");
        await ClickPinForValidationAsync(current,check);await FocusEditor();
        NativeContextProbeInput.Escape();await WebAppUntilScript(page,"escapeKeys===2");
        check(PinnedInputActive,"The fresh re-pinned browser receives its own physical Escape without reviving the retired pin");
        await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ReturnFromPinButton);
        await WebAppUntil(()=>foreground&&!PinnedInputActive);

        async Task FocusEditor()
        {
            var bounds=current.Window.Bounds;
            await NativeContextProbeInput.ClickWindowElementAsync(current.Window.Handle,page.CompositionRoot,screenPoint:new(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d));
            await Task.Delay(120);
            if(!PinnedInputActive)throw new InvalidOperationException("Physical browser click changed native ownership before Escape: "+JsonSerializer.Serialize(new{
                main=(long)hostWindow,pin=(long)current.Window.Handle,foreground=(long)NotificationForegroundWindow(),current.HasForeground,current.Window.Interactive,
                xamlWindow=(long)Microsoft.UI.Win32Interop.GetWindowFromWindowId(page.CompositionRoot.XamlRoot.ContentIslandEnvironment.AppWindowId),
                dom=await page.EvaluateFixtureAsync("JSON.stringify({focus:document.hasFocus(),active:document.activeElement?.id})"),trace=pinnedKeyboardTraceForValidation}));
            await WebAppUntilScript(page,"document.activeElement.id==='pinEdit' && document.hasFocus()");
        }
    }

}
