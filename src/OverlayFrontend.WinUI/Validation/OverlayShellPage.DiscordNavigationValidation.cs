using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit synthetic-only regression. No OS SendInput, physical controller,
    // Discord account, microphone permission or capture selection is involved.
    internal void EnableDiscordNavigationValidation(string path)
    {
        var started=false;
        Loaded+=async (_,_)=>
        {
            if(started)return;started=true;
            var checks=new List<string>();var trace=new List<object>();var icons=new List<object>();string? failure=null;var step="startup";
            try
            {
                var root=Path.GetDirectoryName(Path.GetFullPath(path))!+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(options.SettingsRoot).StartsWith(root,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Discord navigation requires an isolated profile under the result directory.");
                if(startup is not null)await startup;
                if(owner is null || !catalogItems.Any(w=>w.Id=="widgetrail.tests.discord-ui") ||
                    catalogItems.Any(w=>w.Id.StartsWith("widgetrail.prototype.discord",StringComparison.Ordinal)))
                    throw new InvalidOperationException("This validation accepts the synthetic Discord worker only.");
                await SelectAsync("widgetrail.tests.discord-ui",true);
                await Until(()=>MainFocusEnabled && CurrentPage()=="call" && ActionableFocus());
                surface!.FocusTrace=message=>{if(trace.Count<600)trace.Add(new{step,native=message});};
                // The no-controller launch omits the real input owner's show
                // handshake. Activate this isolated fixture window explicitly;
                // do not mistake logical XAML focus in a background window for
                // a foreground controller test.
                App.Window!.Activate();
                await Until(OwnsNativeForeground);
                Check(OwnsNativeForeground(),"Synthetic main window owns Windows foreground before controller checks");
                Check(ActionableFocus(),"Initial focus is an enabled native control: "+FocusId());
                using var dimensions=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"expected-surface.json")));
                var width=dimensions.RootElement.GetProperty("width").GetDouble();
                var height=dimensions.RootElement.GetProperty("height").GetDouble();
                Check(width is 420 or 1040 && height is 360 or 680,"Fixture declares a supported minimum or expanded surface");
                var compact=width==420;
                var activityId=compact?"discord.activities.compact":"discord.activities";
                Check(Math.Abs(surface!.ActualWidth-width)<1 && Math.Abs(surface.ActualHeight-height)<1,
                    $"Controller scenario runs at requested size: {surface.ActualWidth}x{surface.ActualHeight} DIPs");
                await Task.Delay(1500,lifetime.Token);
                RecordIcons("initial");
                Check(Node("discord.navigation.shell") is null&&Node("discord.call.page") is not null,
                    "Call controls render together without the retired section rail");
                step="root shortcuts";
                var muted=Node("discord.main.mute")!.IsSelected==true;
                await Button(ControllerButton.X);
                await Until(()=>(Node("discord.main.mute")?.IsSelected==true)!=muted);
                Check(ActionableFocus()&&VisibleFocus(),"Root X changes microphone state without losing enabled visible focus");
                var afterMute=Node("discord.main.mute")!.IsSelected==true;
                Check(!Nodes(surface.CurrentBinding!.View.Root).SelectMany(n=>n.Shortcuts).Any(s=>s.Button==ControllerButton.Y),
                    "Fixture's unavailable outgoing provider does not advertise a sharing shortcut");
                var pickerOrigin=FocusId();
                await Button(ControllerButton.LeftStick);
                await Until(()=>CurrentPage()=="channels"&&FocusId()=="Widget.discord.channels.search"&&ActionableFocus());
                Check(surface.CurrentBinding?.Frame.Authority.ActiveInputScopeId=="discord.channels.scope",
                    "L3 opens server/channel selection in a navigator-owned input scope");
                Check(!Nodes(surface.CurrentBinding!.View.Root).SelectMany(n=>n.Shortcuts).Any(s=>s.Button is ControllerButton.X or ControllerButton.Y),
                    "Server selection has no underlying call mutation shortcuts");
                await Button(ControllerButton.X);
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.channels.server");
                await Button(ControllerButton.A);
                await Until(()=>surface.HasTransientControl&&FocusId().StartsWith("Widget.discord.channels.server.Option.",StringComparison.Ordinal));
                Check(true,"Controller A opens the actual native server dropdown");
                await Move(NavigationDirection.Down);
                await Button(ControllerButton.B);
                await Until(()=>!surface.HasTransientControl&&FocusId()=="Widget.discord.channels.server");
                Check(true,"Dropdown B restores its opener without leaving the picker");
                await MoveUntil(NavigationDirection.Up,()=>FocusId()=="Widget.discord.channels.search");
                await Button(ControllerButton.A);
                await Until(()=>surface.ControllerTextEntry is {IsLoaded:true});
                Check(true,"Controller A opens the server/channel search keyboard on its first attempt");
                await Button(ControllerButton.B);
                await Until(()=>surface.ControllerTextEntry is null&&FocusId()=="Widget.discord.channels.search");
                Check(true,"Keyboard B restores search within the picker");
                await Button(ControllerButton.B);
                await Until(()=>CurrentPage()=="call"&&FocusId()==pickerOrigin&&ActionableFocus());
                Check((Node("discord.main.mute")!.IsSelected==true)==afterMute,
                    "Picker B restores the original call control and picker X did not mute the call");
                step="account scope";
                await MoveUntil(NavigationDirection.Up,()=>FocusId() is "Widget.discord.refresh" or "Widget.discord.account.open");
                await MoveUntil(NavigationDirection.Right,()=>FocusId()=="Widget.discord.account.open");
                var origin=FocusId();await Button(ControllerButton.A);
                await Until(()=>CurrentPage()=="account" && FocusId()=="Widget.discord.account.back" && ActionableFocus());
                Check(surface.CurrentBinding?.Frame.Authority.ActiveInputScopeId=="discord.account.scope",
                    "Account navigation focuses Back in its owned input scope without a real account session");
                await Button(ControllerButton.B);
                await Until(()=>CurrentPage()=="call" && FocusId()==origin);
                Check(ActionableFocus(),"Account B restores the exact invoking account icon");
                step="main Watch";
                Check(surface.FocusHostTarget("discord.main.mute"),"Main Watch scenario starts on the native mute control");
                await Until(()=>FocusId()=="Widget.discord.main.mute");
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.stream.2");
                Check(VisibleFocus(),"D-pad reaches and reveals the main shared-screen row");
                await Button(ControllerButton.A);
                await Until(()=>pinned?.LayoutId=="discord.stream"&&pendingPinWidget is null&&PinText("discord.pin.status.title")=="Screen unavailable");
                Check(!PinnedInputActive&&OwnsNativeForeground(),"Main stream activation opens a passive pin without transferring foreground");
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.streams.stop");
                Check(ActionableFocus()&&VisibleFocus(),"D-pad reveals the main Stop watching control with the receiver error present");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.notice") is null&&PinText("discord.pin.status.title")=="No screen selected");
                await UnpinAsync(save:true);
                step="indexed Watch";
                Check(surface.FocusHostTarget("discord.main.mute"),"Activity navigation starts on the native mute control");
                await Until(()=>FocusId()=="Widget.discord.main.mute");
                await MoveUntil(compact?NavigationDirection.Down:NavigationDirection.Right,
                    ()=>FocusId().StartsWith("Widget."+activityId+".Item.",StringComparison.Ordinal));
                Check(ActionableFocus()&&VisibleFocus(),"D-pad reaches the visible "+(compact?"compact":"expanded")+" indexed activity projection");
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget."+activityId+".Item.2");
                var indexedOrigin=FocusId();
                Check(ActionableFocus()&&VisibleFocus(),"A deeper indexed activity occurrence is visible before opening servers");
                await Button(ControllerButton.LeftStick);
                await Until(()=>CurrentPage()=="channels"&&FocusId()=="Widget.discord.channels.search"&&ActionableFocus());
                Check(true,"L3 from an indexed activity occurrence opens the owned server picker");
                await Button(ControllerButton.B);
                await Until(()=>CurrentPage()=="call"&&FocusId()==indexedOrigin&&ActionableFocus()&&VisibleFocus());
                Check(true,"Picker B restores the exact indexed activity occurrence and its visible viewport");
                // Move to the first occurrence using D-pad, preserving the native
                // indexed lease instead of dispatching a fabricated row action.
                for(var i=0;!FocusId().EndsWith(".Item.0",StringComparison.Ordinal)&&i<8;i++)await Move(NavigationDirection.Up);
                Check(FocusId()=="Widget."+activityId+".Item.0","Watch starts on the first native indexed activity occurrence");
                await Button(ControllerButton.A);
                await Until(()=>pinned?.LayoutId=="discord.stream" && pendingPinWidget is null &&
                    PinText("discord.pin.status.title")=="Screen unavailable");
                Check(OwnsNativeForeground(),"Passive Watch pin preserves the actual main foreground HWND");
                Check(!PinnedInputActive && foreground,"Indexed Watch opens a passive pin and keeps main input ownership");
                Check(PinText("discord.pin.status.detail")?.Contains("retry",StringComparison.Ordinal)==true,
                    "Failed synthetic receiver renders Screen unavailable and retry guidance in the native pin");
                step="indexed recovery";
                await MoveUntil(compact?NavigationDirection.Up:NavigationDirection.Left,
                    ()=>!FocusId().StartsWith("Widget."+activityId+".Item.",StringComparison.Ordinal));
                Check(ActionableFocus(),"D-pad returns from activity to native main controls");
                Check(surface.FocusHostTarget("discord.stream.2"),"Recovery scenario starts at the selected native stream row");
                await Until(()=>FocusId()=="Widget.discord.stream.2");
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.streams.stop");
                Check(ActionableFocus()&&VisibleFocus(),"D-pad reaches and reveals Stop watching after indexed activation");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.notice") is null && PinText("discord.pin.status.title")=="No screen selected");
                Check(true,"Native Stop watching clears failed selection and recovery notice");
                await UnpinAsync(save:true);
                step="idle picker";
                Check(surface.FocusHostTarget("discord.main.mute"),"Leave navigation starts at the native mute control");
                await Until(()=>FocusId()=="Widget.discord.main.mute");
                // The responsive action grid wraps below Mute at minimum width.
                await MoveUntil(compact ? NavigationDirection.Down : NavigationDirection.Right,
                    () => FocusId() == "Widget.discord.main.leave");
                await Button(ControllerButton.A);
                await Until(()=>CurrentPage()=="picker"&&ActionableFocus());
                Check(Node("discord.channels.server") is not null&&Node("discord.main.mute") is null,
                    "Leaving voice automatically shows the server picker rather than an empty call tab");
                Check(!Nodes(surface.CurrentBinding!.View.Root).SelectMany(n=>n.Shortcuts).Any(s=>s.Button is ControllerButton.X or ControllerButton.Y),
                    "Idle server picker does not expose voice or share shortcuts");
                Check(VisibleFocus(),"Idle transition retains a visible enabled native picker control");
                Check(validationFailure is null,"Scenario never enters host recovery");
            }
            catch(Exception error){failure=error.ToString();}
            finally
            {
                if(surface is not null)surface.FocusTrace=null;
                try{RecordIcons("final");}catch(Exception error){icons.Add(new{phase="final",inspectionError=error.Message});}
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{passed=failure is null,step,checks,trace,icons,error=failure,
                    scope="Synthetic worker and actual native shell/controller routes; not physical hardware, live Discord, or browser account rendering."}));
                File.Move(path+".tmp",path,true);
            }
            async Task Button(ControllerButton button)
            {
                await Until(()=>MainFocusEnabled);
                await RouteButtonAsync(button,ControllerEventPhase.Pressed);
                await RouteButtonAsync(button,ControllerEventPhase.Released);
                trace.Add(new{step,input=button.ToString(),page=CurrentPage(),focus=FocusId()});
            }
            async Task Move(NavigationDirection direction)
            {
                await Until(()=>MainFocusEnabled);
                var frame=Sample();frame.DpadNavigation=new(){Direction=direction,Phase=NavigationPhase.Pressed};
                Receive(frame);Receive(Sample());
                await Task.Delay(100,lifetime.Token);
                trace.Add(new{step,input=direction.ToString(),page=CurrentPage(),focus=FocusId()});
            }
            async Task MoveUntil(NavigationDirection direction,Func<bool> target)
            {
                for(var i=0;i<24&&!target();i++)await Move(direction);
                Check(target(),"D-pad "+direction+" reaches target during "+step+"; focus="+FocusId());
            }
            bool ActionableFocus()=>FocusManager.GetFocusedElement(XamlRoot) is Control {IsEnabled:true,IsTabStop:true,IsLoaded:true} control &&
                control is not (ScrollViewer or ListViewBase or WidgetIndexedCollectionView) &&
                FocusId().StartsWith("Widget.",StringComparison.Ordinal) && Walk(surface!).Any(item=>ReferenceEquals(item,control));
            bool VisibleFocus()
            {
                if(FocusManager.GetFocusedElement(XamlRoot) is not FrameworkElement element)return false;
                var bounds=element.TransformToVisual(surface).TransformBounds(new(0,0,element.ActualWidth,element.ActualHeight));
                if(bounds.Bottom<=0||bounds.Top>=surface!.ActualHeight)return false;
                ScrollViewer? outer=null;
                for(DependencyObject? parent=element;parent is not null;parent=VisualTreeHelper.GetParent(parent))
                    if(parent is ScrollViewer scroll)outer=scroll;
                return outer is null || WidgetReadingScrollFocus.IsVisible(element,outer);
            }
            string FocusId()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused?AutomationProperties.GetAutomationId(focused):"";
            bool OwnsNativeForeground()=>App.Window is {} window&&DiscordValidationForegroundWindow()==WinRT.Interop.WindowNative.GetWindowHandle(window);
            ViewNode? Node(string id)=>surface?.CurrentBinding is {} binding?Nodes(binding.Frame.Snapshot.Root).FirstOrDefault(n=>n.Id==id):null;
            string CurrentPage()=>Node("discord.account") is not null?"account":Node("discord.picker.page") is not null?"channels":
                Node("discord.call.page") is not null?"call":Node("discord.channels.search") is not null?"picker":"loading";
            string? PinText(string id)=>pinned?.Presenter is {} presenter?Walk(presenter).OfType<TextBlock>().FirstOrDefault(item=>AutomationProperties.GetAutomationId(item)=="Widget."+id)?.Text:null;
            void RecordIcons(string phase)
            {
                if(surface is null)return;
                foreach(var icon in Walk(surface).OfType<WidgetPackageIconView>())
                {
                    var ancestors=new List<string>();var visible=icon.IsLoaded&&icon.ActualWidth>0&&icon.ActualHeight>0;
                    for(DependencyObject? parent=icon;parent is not null;parent=VisualTreeHelper.GetParent(parent))
                    {
                        if(parent is UIElement element&&element.Visibility!=Visibility.Visible)visible=false;
                        var id=AutomationProperties.GetAutomationId(parent);if(id.Length>0)ancestors.Add(id);
                    }
                    icons.Add(new{phase,ancestors,visible,icon.ActualWidth,icon.ActualHeight,content=icon.Content?.GetType().Name,
                        preparedTint=icon.Content is ImageIcon image&&NativePackageIconTint.For(image) is not null});
                }
            }
            async Task Until(Func<bool> condition)
            {
                var until=Environment.TickCount64+15000;
                while(!condition())
                {
                    if(validationFailure is not null || Environment.TickCount64>until)
                        throw new InvalidOperationException($"Discord navigation did not settle: step={step}; page={CurrentPage()}; focus={FocusId()}; interactive={interactive}; foreground={foreground}; nativeForeground={OwnsNativeForeground()}; switching={switching}; pin={pinned?.LayoutId}; presenter={surface?.FocusDiagnostics()}",validationFailure);
                    await Task.Delay(20,lifetime.Token);
                }
            }
            void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
        };
        static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            yield return root;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
    }
    [LibraryImport("user32.dll",EntryPoint="GetForegroundWindow"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint DiscordValidationForegroundWindow();
}
