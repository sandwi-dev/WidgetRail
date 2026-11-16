using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePinRequestValidation(string path)
    {
        var started=false;
        Loaded+=async (_,_)=>
        {
            if(started)return;started=true;
            var checks=new List<string>();string? failure=null;
            var effects=0;
            WidgetPresentationHostEffect? lastClose=null;
            WidgetPresentationSession.WidgetPresentationSession? testSession=null;
            void Received(object? sender,WidgetHostEffectEventArgs args)
            {
                if(args.Effect.Kind==WidgetHostEffectKind.OpenPinnedLayout)DispatcherQueue.TryEnqueue(()=>effects++);
                if(args.Effect.Kind==WidgetHostEffectKind.ClosePinnedLayout)lastClose=args.Effect;
            }
            try
            {
                var root=Path.GetDirectoryName(Path.GetFullPath(path))!+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(options.SettingsRoot).StartsWith(root,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Pin tests require an isolated profile under the evidence directory.");
                if(startup is not null)await startup;
                if(owner is null || catalogItems.Any(w=>w.Id is not ("pinning-gallery" or "pinning-gallery-peer" or "pinning-gallery-third" or "pinning-gallery-fourth")))
                    throw new InvalidOperationException("Pin tests accept only the two synthetic Gallery workers.");
                testSession=owner.Session;testSession.HostEffectReceived+=Received;
                App.Window!.Activate();
                if(Environment.GetCommandLineArgs().Contains("--pointer-pins-only"))
                {
                    await OpenUtilities("pinning-gallery");await Pin("readout");await Pin("level");
                    pinned=pins.Single(pin=>pin.LayoutId=="gallery.readout");
                    await VerifyPointerPinAdjustmentAsync(Check);
                    return;
                }
                if (Environment.GetCommandLineArgs().Contains("--passive-pins-only"))
                {
                    foreach(var id in new[]{"pinning-gallery","pinning-gallery-peer","pinning-gallery-third"})
                    {
                        await OpenUtilities(id);await Pin("readout");await Pin("level");
                    }
                    var passive=pins.Where(pin=>pin.LayoutId=="gallery.readout").ToArray();
                    var interactivePins=pins.Where(pin=>pin.LayoutId=="gallery.level").ToArray();
                    Check(pins.Count==6&&passive.Length==3&&interactivePins.Length==3,"Six mixed pins retain independent native windows");
                    await Until(()=>passive.All(pin=>((DesktopAcrylicSurface)pin.Window.SurfaceBackground.Parent).HasDesktopAcrylic));
                    var priorAppearance=savedAppearance;
                    savedAppearance=savedAppearance with {Transparency=PlatformSettings.TransparencyPreference.Reduced};
                    ApplyEffectiveAppearance();
                    Check(passive.All(pin=>!((DesktopAcrylicSurface)pin.Window.SurfaceBackground.Parent).HasDesktopAcrylic&&
                        pin.Window.SurfaceBackground.Background is SolidColorBrush {Color.A:255}),
                        "Reduced transparency updates every live acrylic pin, not only the selected one");
                    savedAppearance=priorAppearance;ApplyEffectiveAppearance();
                    await Until(()=>passive.All(pin=>((DesktopAcrylicSurface)pin.Window.SurfaceBackground.Parent).HasDesktopAcrylic));
                    Check(true,"Restoring transparency reconnects all three materials without replacing pins");
                    pinned=passive[0];await EnterPinnedAsync();
                    RefreshPinPointerAdmission();
                    Check(passive.All(pin=>pin.Window.PointerActivationHandle!=0&&!pin.Window.NativeInputEnabled&&!CanInteractWithPin(pin)),
                        "Explicit passive layouts have host management hit targets without content-interaction authority");
                    Check(!PinnedInputActive&&passive.All(pin=>!pin.Window.Interactive&&!pin.Window.NativeInputEnabled),"Direct interaction entry cannot activate a passive pin");
                    var mixedChoice=ChoosePinAsync("Interact with pinned widget",_=>EnterPinnedAsync(),interactionOnly:true);
                    await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                    Check(hostChoiceDialog!.ChoiceIdsForValidation.ToHashSet().SetEquals(interactivePins.Select(pin=>pin.ChoiceId)),
                        "Interaction chooser lists exactly the three interactive surfaces");
                    hostChoiceDialog.FocusFirstChoice();
                    await Until(()=>interactivePins[0].Window.FocusIndicator.Visibility==Visibility.Visible);
                    Check(passive.All(pin=>pin.Window.FocusIndicator.Visibility==Visibility.Collapsed),
                        "Interaction chooser highlights an actual eligible window and no passive surface");
                    await Button(ControllerButton.A);await mixedChoice;await Until(()=>PinnedInputActive);
                    Check(ReferenceEquals(pinned,interactivePins[0])&&pinned!.Window.NativeInputEnabled,"Chooser confirmation enters only the highlighted interactive pin");
                    ExitPinnedInteraction(restoreMain:true);await Until(()=>MainFocusEnabled);
                    foreach(var pin in interactivePins)await UnpinAsync(save:false,pin);
                    Check(!HasInteractivePins&&pins.Count==3,"Closing interactive pins retains every passive surface");
                    await Button(ControllerButton.View);
                    Check(!PinnedInputActive&&hostChoiceDialog is null&&passive.All(pin=>!pin.Window.Interactive),
                        "View does not offer interaction when only passive pins remain");
                    pinned=passive[0];var before=pinned.Window.Bounds;
                    await BeginPinnedAdjustmentAsync();StepPinnedPlacement(PinnedPlacementDirection.Right,false);
                    Check(pinned.Window.Bounds!=before&&!pinned.Window.Interactive&&!pinned.Window.NativeInputEnabled,"Host placement moves a passive pin without content focus");
                    CancelPinnedAdjustment();
                    Check(pinned.Window.Bounds==before,"Cancel restores a passive pin's exact saved placement");
                    return;
                }
                await OpenUtilities("pinning-gallery");
                await Pin("summary");
                await Until(()=>pinned?.LayoutId=="gallery.summary" && pendingPinWidget is null);
                var first=pinned!;
                var bounds=first.Window.Bounds;
                Check(foreground && !PinnedInputActive && !first.Window.Interactive,"Native widget action creates a passive pin without transferring foreground");
                Check(FocusId()=="Widget.gallery.pin.open.summary","Pin creation retains the invoking native control's focus");
                await Pin("summary");
                Check(ReferenceEquals(first,pinned),"Opening an already-selected layout keeps the existing native window");
                await Pin("level");
                await Until(()=>pinned?.LayoutId=="gallery.level" && pendingPinWidget is null);
                Check(pins.Count==2 && first.Window.IsVisible && !first.Window.Interactive,
                    "Two different layouts of the same widget remain visible and passive");
                await Pin("summary");
                Check(pins.Count==2 && first.Window.Bounds==bounds,"Reopening the existing summary preserves its window and bounds");
                foreach(var id in new[]{"pinning-gallery-peer","pinning-gallery-third"})
                {
                    await OpenUtilities(id);
                    await Pin("summary");await Pin("level");
                }
                var six=pins.ToArray();
                Check(six.Length==6 && six.All(pin=>pin.IsCurrent && pin.Window.IsVisible && !pin.Window.Interactive),
                    "Six native windows from three widget workers are visible and passive");
                await OpenUtilities("pinning-gallery-fourth");
                await Pin("summary",waitForSettlement:false);
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                await Button(ControllerButton.B);
                await Until(()=>hostChoiceDialog is null);
                Check(six.SequenceEqual(pins),"Cancelling the capacity chooser preserves all six original windows");
                var mainHandle=(nint)hostWindow;
                await VerifyPinPointerSwitchingAsync(Check);
                await VerifyBackdropBandAsync(Check);
                var backdropHandle=((MainWindow)App.Window!).BackdropHandleForValidation;
                Check(backdropHandle!=0&&OverlayWindowFrame.IsWindowVisible(backdropHandle)!=0,
                    "Steady-layer test uses the actual visible overlay backdrop");
                var oldOrderWrites=OverlayWindowFrame.ZOrderWritesForValidation;
                for(var cycle=0;cycle<3;cycle++)
                {
                    // Reproduce the old adjacency algorithm with unchanged windows:
                    // scrim directly below main, then every pin in that same slot.
                    OverlayWindowFrame.PlaceBelow(backdropHandle,mainHandle);
                    foreach(var item in six)OverlayWindowFrame.PlaceBelow(item.Window.Handle,mainHandle);
                }
                var churn=OverlayWindowFrame.ZOrderWritesForValidation-oldOrderWrites;
                Check(churn>=18,$"Old adjacency ordering reproduces native churn ({churn} z-order writes for three unchanged refreshes)");
                SurfaceAppearanceChanged?.Invoke();
                var stableOrderWrites=OverlayWindowFrame.ZOrderWritesForValidation;
                var stableFrameWrites=OverlayWindowFrame.FrameRefreshesForValidation;
                for(var cycle=0;cycle<30;cycle++)SurfaceAppearanceChanged?.Invoke();
                Check(OverlayWindowFrame.ZOrderWritesForValidation==stableOrderWrites&&
                    OverlayWindowFrame.FrameRefreshesForValidation==stableFrameWrites,
                    "Thirty unchanged real appearance refreshes issue no z-order writes or native frame refreshes");
                Check(six.All(item=>OverlayWindowFrame.IsAbove(mainHandle,item.Window.Handle)&&
                    OverlayWindowFrame.IsAbove(item.Window.Handle,backdropHandle)),
                    "Every passive pin remains above the backdrop and below main without sibling rotation");
                var choose=ChoosePinAsync("Move / resize pinned widget",_=>BeginPinnedAdjustmentAsync());
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                // The chooser intentionally remembers the selected pin. Navigate
                // to the first item rather than assuming opening focus is index 0.
                for(var attempt=0;attempt<8&&hostChoiceDialog!.FocusedChoiceId!=first.ChoiceId;attempt++)
                {
                    Receive(Sample());
                    var up=Sample();up.DpadNavigation=new(){Direction=WidgetRail.OverlayPlatformClient.NavigationDirection.Up,
                        Phase=WidgetRail.OverlayPlatformClient.NavigationPhase.Pressed};
                    Receive(up);Receive(Sample());await Task.Delay(50,lifetime.Token);
                }
                await Until(()=>first.Window.FocusIndicator.Visibility==Visibility.Visible);
                Check(pins.Count(pin=>pin.Window.FocusIndicator.Visibility==Visibility.Visible)==1 && !PinnedInputActive,
                    "Highlighting the first named choice outlines only its real native window before A");
                var nextChoice=Sample();nextChoice.DpadNavigation=new(){Direction=WidgetRail.OverlayPlatformClient.NavigationDirection.Down,Phase=WidgetRail.OverlayPlatformClient.NavigationPhase.Pressed};
                Receive(nextChoice);Receive(Sample());
                await Until(()=>six[1].Window.FocusIndicator.Visibility==Visibility.Visible);
                Check(first.Window.FocusIndicator.Visibility==Visibility.Collapsed && !six[1].Window.Interactive,
                    "D-pad transfers the preview outline to the next actual window while the chooser retains input");
                var previousChoice=Sample();previousChoice.DpadNavigation=new(){Direction=WidgetRail.OverlayPlatformClient.NavigationDirection.Up,Phase=WidgetRail.OverlayPlatformClient.NavigationPhase.Pressed};
                Receive(previousChoice);Receive(Sample());
                await Until(()=>first.Window.FocusIndicator.Visibility==Visibility.Visible);
                await Button(ControllerButton.A);
                await choose.WaitAsync(TimeSpan.FromSeconds(5));
                Check(ReferenceEquals(pinnedAdjustment?.Surface,first),"Placement is bound to the outlined pin");
                StepPinnedPlacement(PinnedPlacementDirection.Left,false);
                await Button(ControllerButton.B);
                Check(first.Window.Bounds==bounds && pins.Count==6,"Cancelling placement restores the exact opening geometry");
                SetVisible(false);
                Check(six.All(pin=>pin.Window.IsVisible),"Hiding main leaves all six pin windows visible");
                SetVisible(true);
                await Until(()=>foreground && !switching);
                await UnpinAsync(save:true,first);
                Check(pins.Count==5 && six.Skip(1).All(pin=>pin.IsCurrent && pin.Window.IsVisible),
                    "Closing one selected layout retains its sibling and the other workers' pins");
                await Task.WhenAll(PinAsync("pinning-gallery-fourth","gallery.summary"),PinAsync("pinning-gallery-fourth","gallery.level"));
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                await Button(ControllerButton.B);
                await Until(()=>hostChoiceDialog is null);
                Check(pins.Count==6 && pins.Count(pin=>pin.WidgetId=="pinning-gallery-fourth")==1,
                    "Two concurrent opens racing for the last slot admit exactly one surface");
                var requested=pins.Single(pin=>pin.WidgetId=="pinning-gallery-fourth");
                var requestedLayout=requested.LayoutId=="gallery.summary"?"summary":"level";
                var closeControl="Widget.gallery.pin.close."+requestedLayout;
                await Until(()=>Find(closeControl) is Button);
                Find(closeControl)!.Focus(FocusState.Keyboard);
                await Button(ControllerButton.A);
                await Until(()=>pins.Count==5 && lastClose is not null);
                var oldClose=lastClose!;
                Check(!pins.Contains(requested)&&six.Skip(1).All(pin=>pin.IsCurrent),"Widget-requested unpin closes only its exact native window");
                await Pin(requestedLayout);
                var reopened=pins.Single(pin=>pin.WidgetId=="pinning-gallery-fourth");
                await RequestWidgetPinAsync(oldClose);
                Check(pins.Count==6&&pins.Contains(reopened),"A delayed close effect cannot close a reopened layout");

                var identity=pins.Select(pin=>pin.ChoiceId).ToArray();
                var callbacks=new NotificationCallbacks(()=>true,()=>{},()=>{},()=>{},()=>{});
                var banner=notificationCenter!.Inbox.Add("pinning-gallery","fixture","https://fixture.invalid","","Six pins notification","",
                    new object(),callbacks,NotificationPresentation.BannerAndList)!;
                SetVisible(false);
                QueueNotificationBanner(banner,true);
                await Until(()=>notificationBanner?.IsVisible==true);
                Check(pins.Select(pin=>pin.ChoiceId).SequenceEqual(identity)&&!notificationBanner!.Interactive,
                    "A native notification banner remains click-through above six pin windows without consuming a slot");
                Check(pins.All(pin=>!NotificationPlacement.Overlaps(pin.Window.Bounds,notificationBanner!.Bounds)),
                    "Banner placement avoids every visible pin");
                SetVisible(true);await Until(()=>foreground&&!switching);
                SurfaceAppearanceChanged?.Invoke();
                Check(notificationBanner!.PointerActivationHandle==0&&!notificationBanner.Interactive&&
                    OverlayWindowFrame.IsAbove(notificationBanner.Handle,(nint)hostWindow),
                    "Banner stays noninteractive above the open overlay and never receives a pin activation target");
                var bannerWrites=OverlayWindowFrame.ZOrderWritesForValidation;
                var bannerFrames=OverlayWindowFrame.FrameRefreshesForValidation;
                for(var cycle=0;cycle<30;cycle++)SurfaceAppearanceChanged?.Invoke();
                Check(bannerWrites==OverlayWindowFrame.ZOrderWritesForValidation&&bannerFrames==OverlayWindowFrame.FrameRefreshesForValidation,
                    "Thirty open-overlay refreshes with six pins and a banner cause no native ordering or frame churn");
                HideNotificationBanner();
                Check(notificationCenter.Inbox.Find(banner.Id) is not null&&pins.Count==6,"Dismissing the banner retains its pending inbox entry and all pins");
                SetVisible(true);
                await Until(()=>foreground&&!switching);
                var preferred=reopened.Window.Bounds;
                var savedBefore=pinnedPreferences.Find(reopened.WidgetId,reopened.LayoutId)!;
                var blocker=pins.First(pin=>!ReferenceEquals(pin,reopened));
                var blockerBounds=blocker.Window.Bounds;
                blocker.Window.Place(preferred);
                await UnpinAsync(save:true,reopened);
                await Pin(requestedLayout);
                var displaced=pins.Single(pin=>pin.WidgetId=="pinning-gallery-fourth");
                Check(displaced.Window.Bounds!=preferred&&pinnedPreferences.Find(displaced.WidgetId,displaced.LayoutId)==savedBefore,
                    "An occupied saved position causes temporary displacement without replacing the saved preference");
                await BeginPinnedAdjustmentAsync();
                StepPinnedOpacity(PinnedPlacementDirection.Left);
                await FinishPinnedAdjustmentAsync(save:true);
                var savedOpacity=pinnedPreferences.Find(displaced.WidgetId,displaced.LayoutId)!;
                Check(savedOpacity==(savedBefore with{OpacityPercent=95}),
                    "Saving opacity on a displaced pin preserves its preferred monitor, position and size");
                await UnpinAsync(save:true,displaced);
                blocker.Window.Place(blockerBounds);
                await Pin(requestedLayout);
                Check(pins.Single(pin=>pin.WidgetId=="pinning-gallery-fourth").Window.Bounds==preferred,
                    "Reopening after the collision clears restores the original saved position");

                var closingChoice=pinned!;
                var survivors=pins.Where(pin=>!ReferenceEquals(pin,closingChoice)).ToArray();
                var survivorBounds=survivors.Select(pin=>pin.Window.Bounds).ToArray();
                var staleChoice=ChoosePinAsync("Move / resize pinned widget",_=>BeginPinnedAdjustmentAsync());
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                await Until(()=>hostChoiceDialog!.FocusedChoiceId==closingChoice.ChoiceId &&
                    closingChoice.Window.FocusIndicator.Visibility==Visibility.Visible);
                await UnpinAsync(save:true,closingChoice);
                Check(survivors.All(pin=>pin.Window.FocusIndicator.Visibility==Visibility.Collapsed),
                    "Closing the highlighted pin clears its preview without outlining a surviving window");
                await Button(ControllerButton.A);
                await staleChoice.WaitAsync(TimeSpan.FromSeconds(5));
                Check(pinnedAdjustment is null && pins.SequenceEqual(survivors) &&
                    survivors.Select(pin=>pin.Window.Bounds).SequenceEqual(survivorBounds),
                    "Confirming a retired chooser row cannot adjust or move another pin");
                var freshTarget=pinned!;
                var freshChoice=ChoosePinAsync("Move / resize pinned widget",_=>BeginPinnedAdjustmentAsync());
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                await Until(()=>hostChoiceDialog!.FocusedChoiceId==freshTarget.ChoiceId &&
                    freshTarget.Window.FocusIndicator.Visibility==Visibility.Visible);
                await Button(ControllerButton.A);
                await freshChoice.WaitAsync(TimeSpan.FromSeconds(5));
                Check(ReferenceEquals(pinnedAdjustment?.Surface,freshTarget),
                    "A fresh chooser selects the surviving window after a highlighted pin closes");
                await Button(ControllerButton.B);

                var displays=PinnedDisplayAreas.Read();
                if(displays.Count>1)
                {
                    var source=freshTarget.Monitor;
                    var destination=displays.First(display=>display.Id!=source.Id);
                    var opening=freshTarget.Window.Bounds;
                    var neighbors=pins.Where(pin=>!ReferenceEquals(pin,freshTarget)).ToArray();
                    var neighborBounds=neighbors.Select(pin=>pin.Window.Bounds).ToArray();
                    await MoveAcross();
                    Check(pinnedAdjustment is not null && freshTarget.Monitor.Id==destination.Id &&
                        neighbors.Select(pin=>pin.Window.Bounds).SequenceEqual(neighborBounds),
                        "Moving a selected pin across a real display edge preserves the other windows and adjustment input");
                    await Button(ControllerButton.B);
                    await Until(()=>freshTarget.Monitor.Id==source.Id && freshTarget.Window.Bounds==opening);
                    Check(true,"Cancelling a move across displays restores the original monitor and exact bounds");
                    await MoveAcross();
                    await Button(ControllerButton.A);
                    var persisted=await new PinnedPreferencesStore(options.SettingsRoot).LoadAsync(lifetime.Token);
                    Check(persisted.Find(freshTarget.WidgetId,freshTarget.LayoutId)?.MonitorId==destination.Id,
                        "Saving a move across displays persists the selected pin's destination monitor");
                    var onSecondDisplay=ChoosePinAsync("Move / resize pinned widget",_=>BeginPinnedAdjustmentAsync());
                    await Until(()=>hostChoiceDialog?.IsOpenForValidation==true &&
                        freshTarget.Window.FocusIndicator.Visibility==Visibility.Visible);
                    Check(neighbors.All(pin=>pin.Window.FocusIndicator.Visibility==Visibility.Collapsed),
                        "The chooser outlines only its selected window on the second physical display");
                    await Button(ControllerButton.B);
                    await onSecondDisplay;

                    async Task MoveAcross()
                    {
                        await BeginPinnedAdjustmentAsync();
                        var from=source.WorkArea;
                        var to=destination.WorkArea;
                        var direction=to.X>=from.X+from.Width?PinnedPlacementDirection.Right:
                            to.X+to.Width<=from.X?PinnedPlacementDirection.Left:
                            to.Y>=from.Y+from.Height?PinnedPlacementDirection.Down:PinnedPlacementDirection.Up;
                        var horizontal=direction is PinnedPlacementDirection.Left or PinnedPlacementDirection.Right;
                        var start=Math.Max(horizontal?from.Y:from.X,horizontal?to.Y:to.X);
                        var end=Math.Min(horizontal?from.Y+from.Height:from.X+from.Width,
                            horizontal?to.Y+to.Height:to.X+to.Width);
                        if(end<=start)throw new InvalidOperationException("Display test requires aligned display edges.");
                        var center=(start+end)/2d;
                        for(var step=0;step<512;step++)
                        {
                            var current=freshTarget.Window.Bounds;
                            var actual=horizontal?current.Y+current.Height/2d:current.X+current.Width/2d;
                            if(Math.Abs(actual-center)<=16*source.Scale)break;
                            StepPinnedPlacement(horizontal?(actual<center?PinnedPlacementDirection.Down:PinnedPlacementDirection.Up):
                                actual<center?PinnedPlacementDirection.Right:PinnedPlacementDirection.Left,false);
                        }
                        for(var step=0;step<512 && freshTarget.Monitor.Id==source.Id;step++)StepPinnedPlacement(direction,false);
                        await Until(()=>freshTarget.Monitor.Id==destination.Id &&
                            freshTarget.Window.AutomationRoot.XamlRoot?.RasterizationScale==destination.Scale);
                        await Task.Delay(100,lifetime.Token);
                    }
                }
            }
            catch(Exception error){failure=error.ToString();}
            finally
            {
                if(testSession is not null)testSession.HostEffectReceived-=Received;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{passed=failure is null,checks,error=failure}));
                File.Move(path+".tmp",path,true);
            }
            async Task OpenUtilities(string id)
            {
                await SelectAsync(id,true);
                await Until(()=>MainFocusEnabled && surface?.CurrentBinding is not null);
                for(var i=0;CurrentPage()!="gallery.utilities" && i<8;i++)
                {
                    var before=CurrentPage();
                    await Button(ControllerButton.RightBumper);
                    await Until(()=>CurrentPage()!=before && MainFocusEnabled);
                }
                await Until(()=>Find("Widget.gallery.pin.open.summary") is {IsLoaded:true,IsEnabled:true,IsTabStop:true});
            }
            async Task Pin(string layout,bool waitForSettlement=true)
            {
                var control=Find("Widget.gallery.pin.open."+layout)??throw new InvalidOperationException("Native pin button is missing.");
                control.Focus(FocusState.Keyboard);
                await Until(()=>FocusId()=="Widget.gallery.pin.open."+layout);
                var before=effects;
                await Button(ControllerButton.A);
                await Until(()=>effects>before);
                if(waitForSettlement)await Until(()=>pendingPinWidget is null && pins.Any(pin=>pin.WidgetId==activeWidget && pin.LayoutId=="gallery."+layout));
            }
            async Task Button(ControllerButton button)
            {
                // A real controller contributes neutral samples after a dialog
                // opens. Preserve its release barrier instead of bypassing it.
                if(hostChoiceDialog is not null)Receive(Sample());
                await RouteButtonAsync(button,ControllerEventPhase.Pressed);
                await RouteButtonAsync(button,ControllerEventPhase.Released);
            }
            Control? Find(string id)=>surface is null?null:Walk(surface).OfType<Control>().FirstOrDefault(c=>AutomationProperties.GetAutomationId(c)==id);
            string? CurrentPage()=>surface?.CurrentBinding is { } binding ? Nodes(binding.Frame.Snapshot.Root)
                .FirstOrDefault(n=>n.Id is "gallery.overview" or "gallery.controls" or "gallery.tiles" or "gallery.backgrounds" or "gallery.text" or "gallery.surfaces" or "gallery.utilities")?.Id:null;
            static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
            string? FocusId()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused?AutomationProperties.GetAutomationId(focused):null;
            async Task Until(Func<bool> condition)
            {
                var deadline=Environment.TickCount64+15000;
                while(!condition())
                {
                    if(validationFailure is not null || Environment.TickCount64>deadline)
                        throw new InvalidOperationException($"Pin test did not settle. Focus={FocusId()}, active={activeWidget}, pin={pinned?.LayoutId}, pending={pendingPinWidget}, foreground={foreground}, effects={effects}",validationFailure);
                    await Task.Delay(20,lifetime.Token);
                }
            }
            void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        };
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
    }
}
