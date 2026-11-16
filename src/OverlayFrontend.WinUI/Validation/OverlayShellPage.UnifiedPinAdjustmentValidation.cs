using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using Windows.System;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Notifications;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableUnifiedPinAdjustmentValidation(string path)
    {
        Loaded+=async(_,_)=>
        {
            var checks=new List<string>();Exception? failure=null;object? details=null;
            void Check(bool okay,string name){if(!okay)throw new InvalidOperationException(name);checks.Add(name);}
            try
            {
                if(startup is not null)await startup;
                await WebAppUntil(()=>foreground&&activeWidget is not null&&!switching);
                var widget=activeWidget!;
                await PinAsync(widget,"gallery.summary");var interactivePin=pinned!;
                await PinAsync(widget,"gallery.readout");var passive=pinned!;
                await WebAppUntil(()=>pins.Count==2&&MainFocusEnabled);
                Check(ReturnFromPinButton.Visibility==Visibility.Collapsed,"Unavailable Return is collapsed and occupies no command slot");
                Check(!CanInteractWithPin(passive),"Fixture uses the shipping Gallery's explicit passive declaration");
                await VerifyAdjustmentOutsideClicksAsync(passive,Check);
                foreach(var guideMode in new[]{"widget","tray","wheel"})
                {
                await SelectAsync(widget,enterWidget:guideMode=="widget");
                if(guideMode=="wheel")await BackFromTrayAsync();
                else if(guideMode=="tray"){radialRequested=false;RefreshRadialChooser();FocusTray();}
                await WebAppUntil(()=>!switching&&retainedSurfaces.TryGetValue(widget,out var guideOwner)&&
                    guideOwner.PresentedLifecycle==DesiredLifecycle&&!trayGuide.HasPendingHints);
                if(guideMode=="tray")await WebAppUntil(()=>FocusedTrayWidget()?.Id==widget&&
                    trayGuide.DisplayedHints.Any(h=>h.Button==ControllerButton.X&&h.Label=="Show feedback")&&!trayGuide.HasPendingHints);
                // Compare the complete published guide, not the width-dependent
                // subset selected by an in-flight rail layout.
                var priorGuide=trayGuide.PublishedHintsForValidation;
                foreach(var saveWithMouse in new[]{false,true})
                {
                    await RightClick(passive);
                    var mouseToolbar=pinAdjustmentToolbar!;
                    await NativeContextProbeInput.ClickWindowElementAsync(mouseToolbar.Window.Handle,
                        saveWithMouse?mouseToolbar.Save:mouseToolbar.Cancel,coordinateRoot:mouseToolbar.Window.AutomationRoot);
                    await WebAppUntil(()=>!PinnedAdjustmentActive);
                    await Task.Delay(500);
                    Check(!trayGuide.HasPendingHints&&trayGuide.PublishedHintsForValidation.SequenceEqual(priorGuide),
                        "Mouse "+(saveWithMouse?"Save":"Cancel")+" after right-click restores the "+guideMode+" guide without controller input; pending="+
                        trayGuide.HasPendingHints+", ready="+surface?.IsGuidePresentationReady(true)+", guide="+trayGuide.HelpText+
                        ", previous="+string.Join(" | ",priorGuide.Select(h=>h.Label)));
                }
                }
                await SelectAsync(widget,enterWidget:true);
                var original=passive.Window.Bounds;var opacity=passive.Window.OpacityPercent;
                await RightClick(passive);
                var toolbar=pinAdjustmentToolbar!;
                Check(!HostChoiceActive&&ReferenceEquals(pinnedAdjustment?.Surface,passive),"Right-click enters exact passive-pin adjustment without an action menu");
                Check(!passive.Window.Interactive&&!passive.Window.NativeInputEnabled&&toolbar.Window.NativeInputEnabled,
                    "Only the host toolbar accepts content input while the passive pin is adjusted");
                Check(!NotificationPlacement.Overlaps(toolbar.Window.Bounds,passive.Window.Bounds),"Adjustment controls sit outside this pin instead of covering its content");
                Check(toolbar.Window.OpacityPercent==100,"The adjustment toolbar stays opaque independently of the pin");
                await CapturePinToolbarAsync(toolbar,path+".initial.png");
                await File.WriteAllTextAsync(path+".initial.json",JsonSerializer.Serialize(new{toolbar.Window.Bounds,handle=(long)toolbar.Window.Handle,main=(long)hostWindow,
                    slider=toolbar.Opacity.TransformToVisual(toolbar.Window.AutomationRoot).TransformBounds(new(0,0,toolbar.Opacity.ActualWidth,toolbar.Opacity.ActualHeight))}));
                await NativeContextProbeInput.ClickWindowElementAsync(toolbar.Window.Handle,toolbar.Opacity,coordinateRoot:toolbar.Window.AutomationRoot);
                await File.WriteAllTextAsync(path+".click.json",JsonSerializer.Serialize(new{hit=NativeContextProbeInput.LastPointerTrace,foreground=(long)PinnedNativeForeground(),visible,adjustment=PinnedAdjustmentActive,toolbar=pinAdjustmentToolbar is not null}));
                var opacityBefore=passive.Window.OpacityPercent;
                NativeContextProbeInput.PressKey(VirtualKey.Right);await Task.Delay(120);
                Check(passive.Window.OpacityPercent==Math.Min(100,opacityBefore+5)&&passive.Window.Bounds==original,
                    "Physical slider focus and arrow input adjust only opacity");
                var start=new Windows.Foundation.Point(original.X+original.Width/2d,original.Y+original.Height/2d);
                await NativeContextProbeInput.DragWindowAsync(passive.Window.PointerPlacementHandle,start,new(start.X-40,start.Y+25));
                Check(passive.Window.Bounds!=original,"Physical dragging moves the same passive pin in the combined mode");
                NativeContextProbeInput.Escape();await WebAppUntil(()=>!PinnedAdjustmentActive);
                Check(passive.Window.Bounds==original&&passive.Window.OpacityPercent==opacity&&pinAdjustmentToolbar is null,
                    "Escape restores geometry and opacity together and retires the toolbar");
                await RightClick(passive);
                SetPinnedOpacity(55);StepPinnedPlacement(PinnedPlacementDirection.Left,false);
                var saved=passive.Window.Bounds;
                await NativeContextProbeInput.ClickWindowElementAsync(pinAdjustmentToolbar!.Window.Handle,pinAdjustmentToolbar.Save,coordinateRoot:pinAdjustmentToolbar.Window.AutomationRoot);
                await WebAppUntil(()=>!PinnedAdjustmentActive);
                Check(passive.Window.Bounds==saved&&passive.Window.OpacityPercent==55,"Physical Save commits both geometry and opacity");
                var stored=(await pinnedStore!.LoadAsync()).Find(passive.WidgetId,passive.LayoutId)!;
                Check(stored.OpacityPercent==55&&PinnedPlacementPolicy.Resolve(PinnedDisplayAreas.Read(),stored,passive.Limits)?.Bounds==saved,
                    "Combined save uses the existing persistent layout/display key");
                await ClickPinForValidationAsync(interactivePin,Check);
                Check(ReturnFromPinButton.Visibility==Visibility.Visible,"Return appears when actual pin interaction is available");
                await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ReturnFromPinButton);
                await File.WriteAllTextAsync(path+".return.json",JsonSerializer.Serialize(new{visible,HostChoiceActive,PinnedAdjustmentActive,
                    PinnedInteractionRequested,returnPointerPending,hit=NativeContextProbeInput.LastPointerTrace,foreground=(long)PinnedNativeForeground()}));
                await WebAppUntil(()=>!PinnedInteractionRequested&&ReturnFromPinButton.Visibility==Visibility.Collapsed);
                Check(visible&&!HostChoiceActive&&!PinnedAdjustmentActive,"Physical Return survives foreground activation without invoking a shifted control");
                ManagePinsButton.Focus(FocusState.Keyboard);
                NativeContextProbeInput.PressControlShiftKey(VirtualKey.I);
                await WebAppUntil(()=>PinnedInputActive);
                Check(ReferenceEquals(pinned,interactivePin)&&!HostChoiceActive&&!passive.Window.Interactive,
                    "Keyboard-only Interact selects the sole interactive pin without exposing the passive pin");
                await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,ReturnFromPinButton);
                await WebAppUntil(()=>!PinnedInteractionRequested&&ReturnFromPinButton.Visibility==Visibility.Collapsed);
                ManagePinsButton.Focus(FocusState.Keyboard);
                NativeContextProbeInput.PressControlShiftKey(VirtualKey.P);
                await WebAppUntil(()=>hostChoiceDialog?.IsOpenForValidation==true);
                var chooser=hostChoiceDialog!;
                var row=FindChoice(chooser,passive.ChoiceId);
                await WebAppUntil(()=>row.IsLoaded&&row.ActualHeight>0&&chooser.FocusedChoiceId==interactivePin.ChoiceId);
                NativeContextProbeInput.PressKey(VirtualKey.Down);
                await WebAppUntil(()=>chooser.FocusedChoiceId==passive.ChoiceId&&passive.Window.FocusIndicator.Visibility==Visibility.Visible);
                NativeContextProbeInput.PressKey(VirtualKey.Enter);
                await WebAppUntil(()=>ReferenceEquals(pinnedAdjustment?.Surface,passive)&&pinAdjustmentToolbar?.Save.IsLoaded==true);
                Check(!HostChoiceActive,"Keyboard chooser confirmation goes directly to adjustment with no second menu");
                var keyboardBounds=passive.Window.Bounds;
                pinAdjustmentToolbar!.Save.Focus(FocusState.Keyboard);
                NativeContextProbeInput.PressKey(VirtualKey.Left);
                await WebAppUntil(()=>passive.Window.Bounds!=keyboardBounds);
                var keyboardWidth=passive.Window.Bounds.Width;
                NativeContextProbeInput.PressKey(VirtualKey.Left,shift:true);
                await WebAppUntil(()=>passive.Window.Bounds.Width!=keyboardWidth);
                Check(true,"Keyboard arrows move and Shift-arrows resize within the same adjustment");
                var keyboardOpacity=passive.Window.OpacityPercent;
                Receive(Sample());Receive(Sample(0x0100,pressed:0x0100));Receive(Sample(released:0x0100));
                Check(passive.Window.OpacityPercent==keyboardOpacity-5,"Controller LB changes opacity in the combined mode");
                Receive(Sample(0x0200,pressed:0x0200));Receive(Sample(released:0x0200));
                Check(passive.Window.OpacityPercent==keyboardOpacity,"Controller RB restores opacity without changing modes");
                NativeContextProbeInput.Escape();await WebAppUntil(()=>!PinnedAdjustmentActive);
                Check(passive.Window.Bounds==keyboardBounds&&passive.Window.OpacityPercent==keyboardOpacity,
                    "Cancel restores the entire mixed keyboard/controller preview");
                await ManagePinAsync(passive,()=>true);
                var retiredToolbar=pinAdjustmentToolbar!;
                var staleClose=retiredToolbar.CaptureCloseForValidation();var staleSave=retiredToolbar.CaptureSaveForValidation();var staleOpacity=retiredToolbar.CaptureOpacityForValidation();
                CancelPinnedAdjustment();
                await ManagePinAsync(interactivePin,()=>true);
                var freshAdjustment=pinnedAdjustment;var freshOpacity=interactivePin.Window.OpacityPercent;
                staleOpacity(30);staleSave();staleClose();await Task.Yield();
                Check(ReferenceEquals(pinnedAdjustment,freshAdjustment)&&interactivePin.Window.OpacityPercent==freshOpacity&&pins.Count==2,
                    "Delayed controls captured from a retired toolbar cannot edit, save or close a newly adjusted pin");
                CancelPinnedAdjustment();
                await PinAsync(widget,"gallery.level");var closeTarget=pinned!;
                var preferred=pinnedPreferences.Find(closeTarget.WidgetId,closeTarget.LayoutId);
                await ManagePinAsync(closeTarget,()=>true);
                Receive(Sample());Receive(Sample(4,pressed:4));Receive(Sample(released:4));
                Receive(Sample(0x4000,pressed:0x4000));Receive(Sample(released:0x4000));
                await WebAppUntil(()=>!pins.Contains(closeTarget)&&!PinnedAdjustmentActive);
                Check(pins.Count==2&&pins.Contains(interactivePin)&&pins.Contains(passive)&&
                    pinnedPreferences.Find(closeTarget.WidgetId,closeTarget.LayoutId)==preferred,
                    "Controller X closes only the adjusted pin without saving its unfinished placement preview");
                await ManagePinAsync(passive,()=>true);
                Check(pinnedAdjustment is not null&&!HostChoiceActive,"The exact-pin SDK management destination opens adjustment directly");
                var captured=pinAdjustmentToolbar!;
                await WebAppUntil(()=>!captured.LayoutPending&&captured.Save.IsLoaded);
                var banner=notificationCenter!.Inbox.Add(widget,"fixture","https://fixture.invalid","","Adjustment notification","",
                    new object(),new NotificationCallbacks(()=>true,()=>{},()=>{},()=>{},()=>{}),NotificationPresentation.BannerAndList,
                    new WidgetNotification("adjustment","Adjustment notification"){Visibility=WidgetNotificationVisibility.Always,Retention=WidgetNotificationRetention.BannerOnly,ShowWidgetIcon=false})!;
                QueueNotificationBanner(banner,true);
                await WebAppUntil(()=>notificationBanner?.IsVisible==true);
                Check(!notificationBanner!.Interactive&&notificationBanner.PointerActivationHandle==0&&
                    !NotificationPlacement.Overlaps(notificationBanner.Bounds,captured.Window.Bounds),
                    "A simultaneous banner stays passive and avoids the floating adjustment toolbar");
                var writes=OverlayWindowFrame.ZOrderWritesForValidation;var frames=OverlayWindowFrame.FrameRefreshesForValidation;
                SurfaceAppearanceChanged?.Invoke();writes=OverlayWindowFrame.ZOrderWritesForValidation;frames=OverlayWindowFrame.FrameRefreshesForValidation;
                for(var i=0;i<30;i++){SurfaceAppearanceChanged?.Invoke();await Task.Delay(16);}
                Check(writes==OverlayWindowFrame.ZOrderWritesForValidation&&frames==OverlayWindowFrame.FrameRefreshesForValidation,
                    "Thirty unchanged updates keep the pin and floating toolbar stable without order/frame churn");
                HideNotificationBanner();
                await VerifyPinAdjustmentVisualsAsync(passive,Check,path);
                await CapturePinToolbarAsync(captured,path+".toolbar.png");
                await NativeContextProbeInput.ClickWindowElementAsync(captured.Window.Handle,captured.Close,coordinateRoot:captured.Window.AutomationRoot);
                await WebAppUntil(()=>!pins.Contains(passive)&&pinAdjustmentToolbar is null);
                Check(pins.Contains(interactivePin)&&!PinnedAdjustmentActive,"Close unpins only the adjusted surface and retires its toolbar");
                await UnpinAsync(save:false,interactivePin);
            }
            catch(Exception error)
            {
                failure=error;
                if(pinAdjustmentToolbar is {} t)details=new{t.Window.Bounds,t.Window.IsVisible,t.Window.NativeInputEnabled,
                    width=t.Window.AutomationRoot.ActualWidth,height=t.Window.AutomationRoot.ActualHeight,
                    childWidth=t.Window.Child?.ActualWidth,childHeight=t.Window.Child?.ActualHeight,
                    sliderWidth=t.Opacity.ActualWidth,sliderHeight=t.Opacity.ActualHeight,t.Opacity.IsLoaded,
                    focus=FocusManager.GetFocusedElement(t.Window.AutomationRoot.XamlRoot)?.GetType().Name};
            }
            finally
            {
                CancelPinnedAdjustment();
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure?.ToString(),details}));
            }
            async Task RightClick(PinnedSurface target)
            {
                await WebAppUntil(()=>foreground&&!switching);
                RefreshPinPointerAdmission();
                var bounds=target.Window.Bounds;
                var point=new Windows.Foundation.Point(bounds.X+bounds.Width/2d,bounds.Y+bounds.Height/2d);
                var main=App.Window!.AppWindow;
                var hit=point.X>=main.Position.X&&point.X<main.Position.X+main.Size.Width&&point.Y>=main.Position.Y&&point.Y<main.Position.Y+main.Size.Height
                    ?(nint)hostWindow:target.Window.PointerActivationHandle;
                await NativeContextProbeInput.ClickWindowElementAsync(hit,target.Window.AutomationRoot,screenPoint:point,right:true);
                await WebAppUntil(()=>ReferenceEquals(pinnedAdjustment?.Surface,target)&&pinAdjustmentToolbar is {} t&&t.Window.IsVisible&&t.Opacity.IsLoaded&&t.Opacity.ActualWidth>0);
            }
        };
    }

    private static async Task CapturePinToolbarAsync(PinnedAdjustmentToolbar toolbar,string path)
    {
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await bitmap.RenderAsync(toolbar.Window.AutomationRoot);
        using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.ToArray(await bitmap.GetPixelsAsync()));
        await encoder.FlushAsync();stream.Seek(0);
        using var reader=new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);var bytes=new byte[(int)stream.Size];reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path,bytes);
    }
}
