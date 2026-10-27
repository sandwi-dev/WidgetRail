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
                    step=width+"x180 voice pin";
                    var bounds=pin.Window.Bounds;
                    pin.Window.Place(new(bounds.X,bounds.Y,(int)Math.Ceiling(width*pin.Monitor.Scale),(int)Math.Ceiling(180*pin.Monitor.Scale)));
                    await Until(()=>Math.Abs(presenter.ActualWidth-width)<1&&Math.Abs(presenter.ActualHeight-180)<1);
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
                var people=Find<FrameworkElement>(presenter,"discord.pin.people.row")!;
                var buttons=new[]{"mute","deafen","leave"}.Select(name=>Find<Button>(presenter,"discord.pin."+name)??throw new InvalidOperationException("Missing voice action "+name)).ToArray();
                var rectangles=buttons.Select(button=>BoundsIn(button,presenter)).ToArray();
                Check(Inside(heading,presenter)&&Inside(people,presenter)&&buttons.All(button=>Inside(button,presenter)),
                    step+": title, people and all action bounds fit inside the pin");
                Check(BoundsIn(heading,presenter).Top<=16&&BoundsIn(heading,presenter).Top>=8,
                    step+": header stays aligned to the top padding instead of vertically centering");
                Check(title.Text.Length>40&&title.MaxLines==1&&title.TextTrimming!=TextTrimming.None,
                    step+": long channel name uses a bounded single-line native ellipsis");
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
                await Until(()=>MainFocusEnabled||PinnedInputActive);
                await RouteButtonAsync(button,ControllerEventPhase.Pressed);
                await RouteButtonAsync(button,ControllerEventPhase.Released);
            }
            async Task Move(NavigationDirection direction)
            {
                await Until(()=>PinnedInputActive);
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
