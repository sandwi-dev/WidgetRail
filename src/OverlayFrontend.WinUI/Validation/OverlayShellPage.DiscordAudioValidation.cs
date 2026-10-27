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
    // The production widget runs against a synthetic provider and an unregistered
    // receiver reference. Its real native controls/pin run; no media device,
    // Discord account, network connection, or system audio is touched.
    internal void EnableDiscordAudioValidation(string path)
    {
        var started=false;
        Loaded+=async (_,_)=>
        {
            if(started)return;started=true;
            var checks=new List<string>();var trace=new List<object>();string? error=null;var step="startup";
            try
            {
                var resultRoot=Path.GetDirectoryName(Path.GetFullPath(path))!+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(options.SettingsRoot).StartsWith(resultRoot,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Discord audio validation requires its own isolated profile.");
                if(startup is not null)await startup;
                if(owner is null||!catalogItems.Any(widget=>widget.Id=="widgetrail.tests.discord-audio")||
                    catalogItems.Any(widget=>widget.Id.StartsWith("widgetrail.prototype.discord",StringComparison.Ordinal)))
                    throw new InvalidOperationException("Only the synthetic audio fixture may run this scenario.");
                await SelectAsync("widgetrail.tests.discord-audio",true);App.Window!.Activate();
                await Until(()=>MainFocusEnabled&&Node(surface,"discord.call.page") is not null&&OwnsMain());
                step="watch synthetic screen";
                Check(surface!.FocusHostTarget("discord.stream.2"),"A native stream row can receive initial fixture focus");
                await Button(ControllerButton.A);
                await Until(()=>pinned?.LayoutId=="discord.stream"&&pendingPinWidget is null&&
                    Node(surface,"discord.streams.audio.volume") is {Value:100}&&
                    Node(pinned?.Presenter,"discord.pin.video") is not null);
                Check(!PinnedInputActive&&browserOwner?.ResidentCount==0,"Synthetic Watch creates a passive native pin without opening a browser or media session");

                step="cold main slider";
                await EnterSlider(surface,"discord.streams.audio",true);
                await Adjust(surface,"discord.streams.audio",50);
                await EnterSlider(surface,"discord.streams.audio",false);
                await Adjust(surface,"discord.streams.audio",0);
                await EnterSlider(surface,"discord.streams.audio",false);
                await Adjust(surface,"discord.streams.audio",100);
                await ToggleMute(surface,"discord.streams.audio");
                await ToggleMute(surface,"discord.streams.audio");

                step="video-only pin";
                var pin=pinned!;var presenter=pin.Presenter!;
                Check(!Nodes(presenter.CurrentBinding!.View.Root).Any(node=>node.Kind is ViewNodeKind.Button or ViewNodeKind.Slider),
                    "The selected pin contains video only; audio controls remain in the main widget");
                Check(!PinnedInputActive&&OwnsMain(),"The video-only pin remains passive without taking controller foreground");
                var initial=pin.Window.Bounds;
                step="minimum video pin";
                pin.Window.Place(new(initial.X,initial.Y,(int)Math.Ceiling(300*pin.Monitor.Scale),(int)Math.Ceiling(200*pin.Monitor.Scale)));
                await Task.Delay(150,lifetime.Token);
                var video=Find<FrameworkElement>(presenter,"discord.pin.video")!;
                Check(Inside(video,presenter),"The video surface remains inside the minimum-size pin");
                Check(Math.Abs(video.ActualHeight-presenter.ActualHeight)<1&&Math.Abs(video.ActualWidth-presenter.ActualWidth)<1,
                    "The minimum-size pin reserves its full presentation area for video");
                var before=presenter.CurrentBinding!.View.Sequence;
                await Task.Delay(2400,lifetime.Token);
                await Until(()=>presenter.CurrentBinding!.View.Sequence>before);
                Check(FocusId()=="Widget.discord.streams.audio.mute","Periodic provider publications preserve main audio focus while video remains pinned");
                Check(validationFailure is null,"Native main audio and video-only pin scenarios never enter host recovery");
            }
            catch(Exception failure){error=failure.ToString();}
            finally
            {
                try{trace.Add(new{step,phase="final",focus=FocusId(),main=surface?.CaptureDeveloperInspection(),pin=pinned?.Presenter?.CaptureDeveloperInspection()});}
                catch(Exception failure){trace.Add(new{inspectionFailure=failure.Message});}
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{passed=error is null,step,checks,trace,error,
                    scope="Production widget main audio controls and video-only native pin with synthetic provider; no pointer priming, real audio, Discord account, or physical controller."}));
                File.Move(path+".tmp",path,true);
            }

            async Task EnterSlider(WidgetViewPresenter presenter,string id,bool cold)
            {
                Check(presenter.FocusHostTarget(id+".mute"),$"{step}: native mute accepts focus");
                await Until(()=>FocusId()=="Widget."+id+".mute"&&Find<Control>(presenter,id+".mute") is {IsLoaded:true});
                var value=Node(presenter,id+".volume")!.Value;
                await Move(NavigationDirection.Right);
                trace.Add(new{step,cold,requested="right from mute",focus=FocusId(),inspection=presenter.CaptureDeveloperInspection()});
                Check(FocusId()=="Widget."+id+".volume",$"{step}: first Right enters the {(cold?"cold ":"")}slider at {value}% without mouse or direct slider focus");
                Check(Node(presenter,id+".volume")!.Value==value,$"{step}: navigation does not change volume");
            }
            async Task Adjust(WidgetViewPresenter presenter,string id,double desired)
            {
                var slider=Find<Slider>(presenter,id+".volume")!;
                await Button(ControllerButton.A);await Until(()=>slider.IsFocusEngaged);
                for(var index=0;Math.Abs(slider.Value-desired)>.01&&index<22;index++)
                    await Move(slider.Value>desired?NavigationDirection.Left:NavigationDirection.Right);
                await Button(ControllerButton.B);await Until(()=>!slider.IsFocusEngaged&&Node(presenter,id+".volume")?.Value==desired);
                Check(FocusId()=="Widget."+id+".volume",$"{step}: controller adjustment to {desired}% remains on the slider");
                await Move(NavigationDirection.Left);
                Check(FocusId()=="Widget."+id+".mute",$"{step}: Left after adjustment returns to mute");
            }
            async Task ToggleMute(WidgetViewPresenter presenter,string id)
            {
                Check(presenter.FocusHostTarget(id+".mute"),$"{step}: mute remains reachable after slider use");
                var selected=Node(presenter,id+".mute")!.IsSelected==true;
                var begun=Environment.TickCount64;await Button(ControllerButton.A);
                await Until(()=>(Node(presenter,id+".mute")?.IsSelected==true)!=selected);
                trace.Add(new{step,phase="mute confirmed",milliseconds=Environment.TickCount64-begun,selected=!selected});
                Check(FocusId()=="Widget."+id+".mute",$"{step}: mute update keeps native button focus");
            }
            async Task Button(ControllerButton button)
            {
                await Until(()=>MainFocusEnabled||PinnedInputActive);
                await RouteButtonAsync(button,ControllerEventPhase.Pressed);await RouteButtonAsync(button,ControllerEventPhase.Released);
            }
            async Task Move(NavigationDirection direction)
            {
                await Until(()=>MainFocusEnabled||PinnedInputActive);
                var sample=Sample();sample.DpadNavigation=new(){Direction=direction,Phase=NavigationPhase.Pressed};
                Receive(sample);Receive(Sample());await Task.Delay(70,lifetime.Token);
            }
            string FocusId()
            {
                var presenter=PinnedInputActive?pinned?.Presenter:surface;
                return presenter?.XamlRoot is { } root&&FocusManager.GetFocusedElement(root) is DependencyObject focused?AutomationProperties.GetAutomationId(focused):"";
            }
            bool OwnsMain()=>App.Window is { } window&&DiscordValidationForegroundWindow()==WinRT.Interop.WindowNative.GetWindowHandle(window);
            async Task Until(Func<bool> condition)
            {
                var deadline=Environment.TickCount64+10000;
                while(!condition())
                {
                    if(validationFailure is not null||Environment.TickCount64>deadline)
                        throw new InvalidOperationException($"Discord audio did not settle: {step}; focus={FocusId()}; main={MainFocusEnabled}; pin={PinnedInputActive}",validationFailure);
                    await Task.Delay(20,lifetime.Token);
                }
            }
            void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
        };
        static ViewNode? Node(WidgetViewPresenter? presenter,string id)=>presenter?.CurrentBinding is { } binding?Nodes(binding.View.Root).FirstOrDefault(node=>node.Id==id):null;
        static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        static T? Find<T>(DependencyObject root,string id) where T:FrameworkElement
        {
            if(root is T element&&AutomationProperties.GetAutomationId(element)=="Widget."+id)return element;
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)if(Find<T>(VisualTreeHelper.GetChild(root,index),id) is { } found)return found;
            return null;
        }
        static bool Inside(FrameworkElement element,FrameworkElement root)
        {
            var bounds=element.TransformToVisual(root).TransformBounds(new(0,0,element.ActualWidth,element.ActualHeight));
            return bounds.Width>0&&bounds.Height>0&&bounds.Left>=-.5&&bounds.Top>=-.5&&bounds.Right<=root.ActualWidth+.5&&bounds.Bottom<=root.ActualHeight+.5;
        }
    }
}
