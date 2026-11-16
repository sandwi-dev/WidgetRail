using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Synthetic provider only. Exercises the production pin through the shell's
    // normal controller routes without a Discord document, device or capture.
    internal void EnableDiscordVoicePinValidation(string path)
    {
        var started=false;
        Loaded+=async (_,_)=>
        {
            if(started)return;started=true;
            var checks=new List<string>();var trace=new List<object>();string? error=null;var step="startup";
            try
            {
                var root=Path.GetDirectoryName(Path.GetFullPath(path))!+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(options.SettingsRoot).StartsWith(root,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Voice-pin validation requires a fresh isolated fixture profile.");
                if(startup is not null)await startup;
                if(owner is null||!catalogItems.Any(item=>item.Id=="widgetrail.tests.discord-ui")||
                    catalogItems.Any(item=>item.Id.StartsWith("widgetrail.prototype.discord",StringComparison.Ordinal)))
                    throw new InvalidOperationException("Only the synthetic Discord UI worker may run voice-pin validation.");
                await SelectAsync("widgetrail.tests.discord-ui",true);App.Window!.Activate();
                await Until(()=>MainFocusEnabled&&Node(surface,"discord.call.page") is not null&&OwnsMain());
                Check(Node(surface,"discord.main.channel")?.Text?.Length>40,"Fixture supplies a long channel title without loading a real account");
                Check(surface!.FocusHostTarget("discord.voice.pin"),"Voice pin can be requested from its actual native main control");
                await Until(()=>FocusId()=="Widget.discord.voice.pin");
                await Button(ControllerButton.A);
                await Until(()=>pinned?.LayoutId=="discord.call"&&pendingPinWidget is null&&pinned.Presenter?.CurrentBinding is not null);
                var pin=pinned!;var presenter=pin.Presenter!;
                Check(!PinnedInputActive&&!pin.Window.Interactive&&OwnsMain(),"Creating the voice pin is passive and preserves main foreground");
                Check(FocusId()=="Widget.discord.voice.pin","Passive pin creation keeps focus on the invoking main control");
                Check(browserOwner?.ResidentCount==0,"Synthetic voice pin opens no browser, microphone or capture session");

                foreach(var width in new[]{400,320})
                {
                    step=width+"x340 voice pin";
                    var bounds=pin.Window.Bounds;
                    pin.Window.Place(new(bounds.X,bounds.Y,(int)Math.Ceiling(width*pin.Monitor.Scale*pin.Window.InterfaceScale),(int)Math.Ceiling(340*pin.Monitor.Scale*pin.Window.InterfaceScale)));
                    await Until(()=>Math.Abs(presenter.ActualWidth-width)<1&&Math.Abs(presenter.ActualHeight-340)<1);
                    if(!PinnedInputActive)
                    {
                        await Button(ControllerButton.View);
                        await Until(()=>PinnedInputActive&&FocusId()=="Widget.discord.pin.mute");
                        Check(pin.HasForeground&&!OwnsMain(),"View enters the native voice pin and initially focuses Mute/Unmute");
                    }
                    else
                    {
                        Check(presenter.FocusHostTarget("discord.pin.mute"),"Resized pin still admits its native mute control");
                        await Until(()=>FocusId()=="Widget.discord.pin.mute");
                    }
                    CheckGeometry(presenter,width);
                    await Toggle("mute");
                    await Move(NavigationDirection.Right);
                    await Until(()=>FocusId()=="Widget.discord.pin.deafen");
                    Check(true,"D-pad Right moves from microphone to deafen at "+width+" DIPs");
                    await Toggle("deafen");
                    CheckGeometry(presenter,width);
                    await Move(NavigationDirection.Right);
                    await Until(()=>FocusId()=="Widget.discord.pin.leave");
                    Check(true,"D-pad Right reaches Leave without focusing decorative avatars at "+width+" DIPs");
                    await Move(NavigationDirection.Left);
                    await Until(()=>FocusId()=="Widget.discord.pin.deafen");
                    await Move(NavigationDirection.Left);
                    await Until(()=>FocusId()=="Widget.discord.pin.mute");
                    Check(true,"D-pad Left returns through the same three actions at "+width+" DIPs");
                }

                step="poll continuity";
                var sequence=presenter.CurrentBinding!.View.Sequence;
                await Until(()=>presenter.CurrentBinding!.View.Sequence>sequence);
                Check(PinnedInputActive&&FocusId()=="Widget.discord.pin.mute","Provider polling preserves active voice-pin focus");
                step="return to main";
                await Button(ControllerButton.B);
                await Until(()=>!PinnedInputActive&&MainFocusEnabled&&OwnsMain()&&FocusId()=="Widget.discord.voice.pin");
                Check(ReferenceEquals(pin,pinned)&&!pin.Window.Interactive,"B leaves pin interaction while preserving the passive voice pin");
                Check(true,"B restores main foreground and the exact invoking pin control");
                step="transparent participants";
                Check(surface!.FocusHostTarget("discord.people.pin"),"Participant overlay has a native open control");
                await Until(()=>FocusId()=="Widget.discord.people.pin");
                await Button(ControllerButton.A);
                await Until(()=>pins.Any(item=>item.LayoutId=="discord.people"&&item.Presenter?.CurrentBinding is not null));
                var peoplePin=pins.Single(item=>item.LayoutId=="discord.people");
                Check(peoplePin.Window.SurfaceBackground.Background is SolidColorBrush {Color.A:0},
                    "Transparent participant overlay has no host background fill");
                Check(peoplePin.Appearance==WidgetSurfaceAppearance.Transparent &&
                    peoplePin.Presenter!.CurrentBinding!.Frame.Snapshot.PinnedLayouts.Single(item=>item.Id=="discord.people").Root!.StyleClasses.Contains("discord-people-overlay"),
                    "The participant layout opts into transparent appearance and its background-free root style");
                Check(pins.Contains(pin)&&pin.Window.IsVisible && !pin.Window.Interactive && !peoplePin.Window.Interactive,
                    "Voice controls and transparent participants coexist as passive surfaces");
                Check(peoplePin.Presenter!.CurrentBinding!.Projection!.Interaction==PinnedInteractionMode.Passive,
                    "Participant input policy comes from the explicit SDK declaration");
                await Until(()=>peoplePin.Presenter!.ActualHeight>0);
                if(Find<TextBlock>(peoplePin.Presenter!,"discord.people.more") is { } morePeople)
                    Check(Inside(morePeople,peoplePin.Presenter!),"Participant overflow count fits the declared minimum pin height");
                step="widget requests exact passive pin management";
                Check(surface!.FocusHostTarget("discord.people.manage"),"Selected participants expose Manage pin in the widget");
                await Button(ControllerButton.A);
                await Until(()=>PinnedAdjustmentActive&&ReferenceEquals(managedPin,peoplePin));
                Check(peoplePin.Window.FocusIndicator.Visibility==Visibility.Visible&&!peoplePin.Window.Interactive,
                    "Manage outlines the exact passive window without enabling its input");
                Check(!HostChoiceActive&&pinAdjustmentToolbar?.Window.IsVisible==true&&!CanInteractWithPin(peoplePin),
                    "Passive management opens the shared adjustment toolbar without a menu or content interaction");
                var managedBounds=peoplePin.Window.Bounds;
                await Button(ControllerButton.DPadLeft);
                Check(peoplePin.Window.Bounds!=managedBounds,"Manage move uses the native controller adjustment flow");
                await Button(ControllerButton.B);
                await Until(()=>!PinnedAdjustmentActive&&MainFocusEnabled);
                Check(peoplePin.Window.Bounds==managedBounds,"Cancelling management movement restores saved placement");
                Check(surface.FocusHostTarget("discord.people.manage"),"Manage remains reachable after adjustment");
                await Button(ControllerButton.A);
                await Until(()=>pinnedAdjustment is not null);
                var managedOpacity=peoplePin.Window.OpacityPercent;
                await Button(ControllerButton.LeftBumper);
                Check(peoplePin.Window.OpacityPercent<managedOpacity,"Manage opacity uses the existing native adjustment flow");
                await Button(ControllerButton.B);
                await Until(()=>!PinnedAdjustmentActive&&MainFocusEnabled);
                Check(peoplePin.Window.OpacityPercent==managedOpacity,"Cancelling opacity preserves the original preference");
                Check(surface.FocusHostTarget("discord.voice.manage"),"Interactive voice controls expose exact Manage pin");
                await Button(ControllerButton.A);
                await Until(()=>PinnedAdjustmentActive&&ReferenceEquals(managedPin,pin));
                Check(!HostChoiceActive&&CanInteractWithPin(pin)&&!pin.Window.Interactive,
                    "Interactive voice pin management adjusts the exact surface without enabling its controls");
                await Button(ControllerButton.B);
                await Until(()=>!PinnedAdjustmentActive&&MainFocusEnabled);
                Check(pin.Window.FocusIndicator.Visibility==Visibility.Collapsed&&peoplePin.Window.FocusIndicator.Visibility==Visibility.Collapsed,
                    "Cancelling management removes the pin outline");
                WidgetRail.WidgetPresentationSession.WidgetPresentationHostEffect? staleManage=null;
                var managementSession=owner!.Session;
                void CaptureManage(object? sender,WidgetRail.WidgetPresentationSession.WidgetHostEffectEventArgs args)
                {
                    if(args.Effect.Kind==WidgetRail.WidgetPresentationSession.WidgetHostEffectKind.ManagePinnedSurface&&
                        args.Effect.PinnedLayoutId=="discord.people")staleManage=args.Effect;
                }
                managementSession.HostEffectReceived+=CaptureManage;
                try
                {
                    Check(surface.FocusHostTarget("discord.people.manage"),"Retirement test starts from the actual Manage control");
                    await Button(ControllerButton.A);
                    await Until(()=>PinnedAdjustmentActive&&staleManage is not null);
                }
                finally{managementSession.HostEffectReceived-=CaptureManage;}
                var retiredPeople=peoplePin;
                await UnpinAsync(save:false,retiredPeople);
                await Until(()=>!HostChoiceActive&&MainFocusEnabled&&Find<Button>(surface!,"discord.people.pin") is {} toggle&&
                    AutomationProperties.GetName(toggle)=="Pin voice participants");
                Check(managedPin is null&&pinAdjustmentToolbar is null&&!pins.Contains(retiredPeople),"Retiring a managed pin dismisses its toolbar and outline");
                Check(surface.FocusHostTarget("discord.people.pin"),"Participant pin can reopen after management retirement");
                await Button(ControllerButton.A);
                await Until(()=>pins.Any(item=>item.LayoutId=="discord.people"));
                peoplePin=pins.Single(item=>item.LayoutId=="discord.people");
                await RequestWidgetPinAsync(staleManage!);
                Check(!HostChoiceActive&&peoplePin.IsCurrent&&!ReferenceEquals(peoplePin,retiredPeople)&&pin.IsCurrent,
                    "A delayed exact Manage request cannot retarget the reopened selection or its neighbor");
                pinned=peoplePin;
                await EnterPinnedAsync();
                Check(!peoplePin.Window.Interactive&&MainFocusEnabled,"A passive participant pin cannot enter content interaction");
                await BeginPinnedAdjustmentAsync();
                Check(ReferenceEquals(pinnedAdjustment?.Surface,peoplePin)&&!peoplePin.Window.Interactive,
                    "Host move and resize remain available without enabling passive content input");
                CancelPinnedAdjustment();
                await Button(ControllerButton.View);
                await Until(()=>PinnedInputActive);
                Check(ReferenceEquals(pinned,pin)&&!peoplePin.Window.Interactive,
                    "View skips the passive participants and directly enters the only interactive voice pin");
                await Button(ControllerButton.B);
                await Until(()=>!PinnedInputActive&&MainFocusEnabled);
                await UnpinAsync(save:false,pin);
                Check(!HasInteractivePins,"An all-passive pin set exposes no interaction target");
                var commands=new List<string>();
                AddPinnedCommands(owner!.Session.GetState(peoplePin.WidgetId)!.LastGood!.Descriptor,(label,id,action)=>commands.Add(id));
                Check(!commands.Contains("Pin.Interact")&&commands.Contains("Pin.Adjust"),"All-passive menus omit Interact and retain placement");
                peoplePin.Window.SetOpacity(50);
                Check(peoplePin.Window.OpacityPercent==50 && peoplePin.Window.SurfaceBackground.Background is SolidColorBrush {Color.A:0},
                    "Whole-window opacity remains independent from the transparent background");
                Check(validationFailure is null,"Voice-pin interactions never enter host recovery");
            }
            catch(Exception failure){error=failure.ToString();}
            finally
            {
                try{trace.Add(new{step,phase="final",focus=FocusId(),main=surface?.CaptureDeveloperInspection(),pin=pinned?.Presenter?.CaptureDeveloperInspection()});}
                catch(Exception failure){trace.Add(new{inspectionError=failure.Message});}
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{passed=error is null,step,checks,trace,error,
                    scope="Actual native voice pin and shell controller routes with an isolated synthetic provider; no physical controller, Discord account, device or capture."}));
                File.Move(path+".tmp",path,true);
            }

            async Task Toggle(string action)
            {
                var presenter=pinned!.Presenter!;var id="discord.pin."+action;
                Check(FocusId()=="Widget."+id,step+": "+action+" owns native focus before A");
                var before=Node(presenter,id)!.IsSelected==true;
                await Button(ControllerButton.A);
                await Until(()=>(Node(presenter,id)?.IsSelected==true)!=before&&
                    (Node(surface,"discord.main."+action)?.IsSelected==true)==!before);
                Check(FocusId()=="Widget."+id&&PinnedInputActive,step+": confirmed "+action+" update retains pin focus");
                Check((Node(surface,"discord.main."+action)?.IsSelected==true)==!before,
                    step+": pin "+action+" and main control reflect the same provider state");
            }
            void CheckGeometry(WidgetViewPresenter presenter,int width)
            {
                var title=Find<TextBlock>(presenter,"discord.pin.channel")??throw new InvalidOperationException("Missing native voice title");
                var heading=Find<FrameworkElement>(presenter,"discord.pin.heading")!;
                var people=Find<FrameworkElement>(presenter,"discord.pin.overlay")!;
                var personRows=Walk(people).OfType<FrameworkElement>().Where(element=>AutomationProperties.GetAutomationId(element)
                    .StartsWith("Widget.discord.pin.row.",StringComparison.Ordinal)).Select(element=>BoundsIn(element,presenter)).OrderBy(rect=>rect.Top).ToArray();
                Check(personRows.Length==4&&personRows.Zip(personRows.Skip(1),(before,after)=>before.Bottom+6<=after.Top).All(value=>value),
                    step+": four participant names have separate vertically spaced rows");
                foreach(var person in new[]{"1","2","3","4"})
                {
                    var avatar=Find<FrameworkElement>(presenter,"discord.pin.person."+person)!;
                    var label=Find<FrameworkElement>(presenter,"discord.pin.label."+person)!;
                    var name=Find<TextBlock>(presenter,"discord.pin.name."+person)!;
                    var a=BoundsIn(avatar,presenter);var l=BoundsIn(label,presenter);var n=BoundsIn(name,presenter);
                    Check(Math.Abs(a.Top+a.Height/2-l.Top-l.Height/2)<1.1&&
                        Math.Abs(n.Top+n.Height/2-l.Top-l.Height/2)<1.1,
                        step+": avatar, name and pill share a vertical center for "+person);
                    Check(n.Left>=l.Left+7&&n.Right<=l.Right-7,
                        step+": name has horizontal breathing room inside pill for "+person);
                }
                var buttons=new[]{"mute","deafen","leave"}.Select(name=>Find<Button>(presenter,"discord.pin."+name)??throw new InvalidOperationException("Missing voice action "+name)).ToArray();
                var rectangles=buttons.Select(button=>BoundsIn(button,presenter)).ToArray();
                Check(Inside(heading,presenter)&&Inside(people,presenter)&&buttons.All(button=>Inside(button,presenter)),
                    step+": title, people and all action bounds fit inside the pin");
                Check(BoundsIn(heading,presenter).Top<=16&&BoundsIn(heading,presenter).Top>=8,
                    step+": header stays aligned to the top padding instead of vertically centering");
                Check(title.Text.Length>40&&title.MaxLines==1&&title.TextTrimming!=TextTrimming.None,
                    step+": long channel name uses a bounded single-line native ellipsis");
                if(Find<TextBlock>(presenter,"discord.pin.more") is { } more)
                    Check(Inside(more,presenter)&&BoundsIn(more,presenter).Bottom<=rectangles.Min(rect=>rect.Top),
                        step+": overflow count fits above the call controls");
                Check(rectangles.Max(rect=>rect.Width)-rectangles.Min(rect=>rect.Width)<1.5&&
                    rectangles.Max(rect=>rect.Top)-rectangles.Min(rect=>rect.Top)<1.5&&
                    rectangles[0].Right<=rectangles[1].Left&&rectangles[1].Right<=rectangles[2].Left,
                    step+": three action columns have equal widths and never overlap");
                Check(buttons.All(button=>button.IsEnabled&&button.IsTabStop)&&
                    !Walk(people).OfType<Control>().Any(control=>control.IsEnabled&&control.IsTabStop),
                    step+": only the three actions are focusable; names and avatars add no focus stops");
                trace.Add(new{step,width,title=title.Text,heading=BoundsIn(heading,presenter),people=BoundsIn(people,presenter),buttons=rectangles});
            }
            async Task Button(ControllerButton button)
            {
                await Until(()=>MainFocusEnabled||PinnedInputActive||HostChoiceActive||PinnedAdjustmentActive);
                if(HostChoiceActive)Receive(Sample()); // Real controllers supply neutral frames after menu entry.
                if(PinnedAdjustmentActive)
                {
                    var mask=button switch{ControllerButton.A=>0x1000,ControllerButton.B=>0x2000,
                        ControllerButton.LeftBumper=>0x0100,ControllerButton.RightBumper=>0x0200,
                        ControllerButton.DPadLeft=>0x0004,ControllerButton.DPadRight=>0x0008,_=>0};
                    if(mask==0)throw new InvalidOperationException("Unexpected placement test button.");
                    var press=Sample();var state=press.State;state.Buttons=(ushort)mask;press.State=state;press.PressedButtons=(ushort)mask;
                    Receive(press);
                    var release=Sample();release.ReleasedButtons=(ushort)mask;Receive(release);
                    await Task.Delay(80,lifetime.Token);return;
                }
                if(HostChoiceActive&&button==ControllerButton.DPadDown){await Move(NavigationDirection.Down);return;}
                await RouteButtonAsync(button,ControllerEventPhase.Pressed);
                await RouteButtonAsync(button,ControllerEventPhase.Released);
            }
            async Task Move(NavigationDirection direction)
            {
                await Until(()=>PinnedInputActive||HostChoiceActive);
                var frame=Sample();frame.DpadNavigation=new(){Direction=direction,Phase=NavigationPhase.Pressed};
                Receive(frame);Receive(Sample());await Task.Delay(80,lifetime.Token);
            }
            string FocusId()
            {
                var presenter=PinnedInputActive?pinned?.Presenter:surface;
                return presenter?.XamlRoot is { } root&&FocusManager.GetFocusedElement(root) is DependencyObject focused?AutomationProperties.GetAutomationId(focused):"";
            }
            bool OwnsMain()=>App.Window is { } window&&DiscordValidationForegroundWindow()==WinRT.Interop.WindowNative.GetWindowHandle(window);
            async Task Until(Func<bool> ready)
            {
                var deadline=Environment.TickCount64+12000;
                while(!ready())
                {
                    if(validationFailure is not null||Environment.TickCount64>deadline)
                        throw new InvalidOperationException($"Voice pin did not settle: {step}; focus={FocusId()}; main={MainFocusEnabled}; pin={PinnedInputActive}; nativeMain={OwnsMain()}",validationFailure);
                    await Task.Delay(20,lifetime.Token);
                }
            }
            void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
        };
        static ViewNode? Node(WidgetViewPresenter? presenter,string id)=>presenter?.CurrentBinding is { } binding?Nodes(binding.View.Root).FirstOrDefault(node=>node.Id==id):null;
        static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)foreach(var item in Walk(VisualTreeHelper.GetChild(root,index)))yield return item;
        }
        static T? Find<T>(DependencyObject root,string id)where T:FrameworkElement=>Walk(root).OfType<T>().FirstOrDefault(item=>AutomationProperties.GetAutomationId(item)=="Widget."+id);
        static Windows.Foundation.Rect BoundsIn(FrameworkElement element,FrameworkElement root)=>element.TransformToVisual(root).TransformBounds(new(0,0,element.ActualWidth,element.ActualHeight));
        static bool Inside(FrameworkElement element,FrameworkElement root)
        {
            var rect=BoundsIn(element,root);
            return rect.Width>0&&rect.Height>0&&rect.Left>=-.5&&rect.Top>=-.5&&rect.Right<=root.ActualWidth+.5&&rect.Bottom<=root.ActualHeight+.5;
        }
    }
}
