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
    internal void EnableDiscordVoiceControlsValidation(string path)
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
                    throw new InvalidOperationException("Voice controls validation requires its own isolated profile.");
                if(startup is not null)await startup;
                if(owner is null||!catalogItems.Any(widget=>widget.Id=="widgetrail.tests.discord-ui")||
                    catalogItems.Any(widget=>widget.Id.StartsWith("widgetrail.prototype.discord",StringComparison.Ordinal)))
                    throw new InvalidOperationException("Voice controls validation accepts only the synthetic fixture.");
                await SelectAsync("widgetrail.tests.discord-ui",true);App.Window!.Activate();
                await Until(()=>MainFocusEnabled&&OwnsMain()&&Node("discord.main.connected")?.AccessibilityLabel=="Voice connected · Connection details");
                Check(browserOwner?.ResidentCount==0,"Synthetic voice/device controls open no browser, capture or microphone session");
                step="connection info";
                Check(surface!.FocusHostTarget("discord.voice.pin"),"Connection info starts next to the native voice-controls pin button");
                await Until(()=>FocusId()=="Widget.discord.voice.pin");
                await Move(NavigationDirection.Left);
                Check(FocusId()=="Widget.discord.main.connected"&&VisibleFocus(),"One Left from pin voice controls reaches the visible connection info button");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.connection-dialog") is not null);
                await Button(ControllerButton.B);
                await Until(()=>Node("discord.connection-dialog") is null&&FocusId()=="Widget.discord.main.connected");
                Check(VisibleFocus(),"Closing connection details restores the info button focus");
                step="participants";
                Check(surface!.FocusHostTarget("discord.main.mute"),"Participant entry starts on a native main call control");
                await Until(()=>FocusId()=="Widget.discord.main.mute");
                await MoveUntil(NavigationDirection.Down,()=>FocusId().StartsWith("Widget.discord.participants.Item.",StringComparison.Ordinal));
                await MoveUntil(NavigationDirection.Left,()=>FocusId()=="Widget.discord.participants.Item.0");
                await Move(NavigationDirection.Right);
                Check(FocusId()=="Widget.discord.participants.Item.1","Controller navigation selects the first other participant without pointer priming");
                var participantOrigin=FocusId();
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.participant-dialog") is not null&&FocusId()=="Widget.discord.voice-dialog.mute");
                Check(surface.CurrentBinding!.Frame.Authority.ActiveInputScopeId=="discord.participant-dialog.scope",
                    "Participant selection enters the host modal's matching SDK navigation scope");
                Check(Node("discord.participant-dialog.title")?.Text=="Avery","Participant modal names the captured occurrence");
                Check(VisibleFocus(),"Initial participant mute focus is visible inside the dialog");
                var selfMuted=Node("discord.main.mute")!.IsSelected;
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.voice-dialog.mute")?.Text=="Unmute for me");
                Check(FocusId()=="Widget.discord.voice-dialog.mute"&&Node("discord.main.mute")!.IsSelected==selfMuted,
                    "Local participant mute updates without changing self microphone or losing focus");
                step="participant slider";
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.voice-dialog.volume");
                Check(Node("discord.voice-dialog.volume")?.Value==50&&VisibleFocus(),"Cold controller entry reaches the voice slider without changing its value");
                // Widget modals live in the host's separate overlay subtree,
                // so searching the background presenter's children cannot find
                // their controls. Inspect the actual natively focused control.
                var slider=FocusManager.GetFocusedElement(surface.XamlRoot) as Slider
                    ??throw new InvalidOperationException("The focused participant volume control is not the native Slider.");
                await Button(ControllerButton.A);await Until(()=>slider.IsFocusEngaged);
                await Move(NavigationDirection.Right);
                await Button(ControllerButton.B);
                await Until(()=>!slider.IsFocusEngaged&&Node("discord.voice-dialog.volume")?.Value==55);
                Check(Node("discord.participant-dialog") is not null&&FocusId()=="Widget.discord.voice-dialog.volume",
                    "First B ends slider adjustment without closing the participant modal");
                Check(Node("discord.voice-dialog.mute")?.Text=="Unmute for me","Voice volume adjustment preserves participant local mute");
                await Button(ControllerButton.B);
                await Until(()=>Node("discord.participant-dialog") is null&&FocusId()==participantOrigin);
                Check(VisibleFocus()&&OwnsMain(),"Second B restores the exact indexed participant, viewport and main foreground");

                step="voice settings menu";
                Check(surface.FocusHostTarget("discord.main.mute"),"Voice settings menu starts on the native call control");
                await Button(ControllerButton.Menu);
                await Until(()=>surface.HasTransientControl&&FocusId().Contains(".Context.",StringComparison.Ordinal));
                await MoveUntil(NavigationDirection.Down,()=>FocusId().Contains(".Context.voice-settings.",StringComparison.Ordinal));
                Check(true,"The actual native context menu exposes Voice settings");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.voice-settings-dialog") is not null&&Node("discord.voice-dialog.input") is not null);
                trace.Add(new{step,phase="settings declaration ready",focus=FocusId(),native=surface.FocusDiagnostics()});
                Check(surface.CurrentBinding!.Frame.Authority.ActiveInputScopeId=="discord.voice-settings-dialog.scope",
                    "Voice settings enters a distinct matching modal/navigation scope");
                await Until(()=>(FocusId() is "Widget.discord.voice-settings-dialog.close" or "Widget.discord.voice-dialog.input")&&
                    NativeEntrySettled()&&NativeControlReady("discord.voice-dialog.input")&&NativeControlReady("discord.voice-dialog.output"));
                trace.Add(new{step,phase="settings native entry",focus=FocusId(),native=surface.FocusDiagnostics()});
                if(FocusId()!="Widget.discord.voice-dialog.input")await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.voice-dialog.input");
                Check(VisibleFocus(),"The microphone selector is reachable after the asynchronous settings read");
                await Button(ControllerButton.A);
                await Until(()=>surface.HasTransientControl&&FocusId()=="Widget.discord.voice-dialog.input.Option.device-0"&&
                    FocusManager.GetFocusedElement(surface.XamlRoot) is ToggleMenuFlyoutItem {IsLoaded:true});
                Check(true,"Controller A opens the actual microphone dropdown");
                await Move(NavigationDirection.Down);await Until(()=>FocusId()=="Widget.discord.voice-dialog.input.Option.device-1");await Button(ControllerButton.B);
                await Until(()=>!surface.HasTransientControl&&FocusId()=="Widget.discord.voice-dialog.input");
                Check(Selected("discord.voice-dialog.input")=="Microphone A","Cancelling the dropdown keeps the confirmed microphone");
                await Button(ControllerButton.A);
                await Until(()=>surface.HasTransientControl&&FocusId()=="Widget.discord.voice-dialog.input.Option.device-0"&&
                    FocusManager.GetFocusedElement(surface.XamlRoot) is ToggleMenuFlyoutItem {IsLoaded:true});
                await Move(NavigationDirection.Down);await Until(()=>FocusId()=="Widget.discord.voice-dialog.input.Option.device-1");
                trace.Add(new{step,phase="confirm microphone",focus=FocusId()});await Button(ControllerButton.A);
                await Until(()=>!surface.HasTransientControl&&Selected("discord.voice-dialog.input")=="Microphone B");
                Check(FocusId()=="Widget.discord.voice-dialog.input","Confirmed microphone selection preserves its native opener focus");
                await MoveUntil(NavigationDirection.Down,()=>FocusId()=="Widget.discord.voice-dialog.output");
                await Button(ControllerButton.A);await Until(()=>surface.HasTransientControl&&FocusId()=="Widget.discord.voice-dialog.output.Option.device-0"&&
                    FocusManager.GetFocusedElement(surface.XamlRoot) is ToggleMenuFlyoutItem {IsLoaded:true});
                await Move(NavigationDirection.Down);await Until(()=>FocusId()=="Widget.discord.voice-dialog.output.Option.device-1");
                trace.Add(new{step,phase="confirm output",focus=FocusId()});await Button(ControllerButton.A);
                await Until(()=>!surface.HasTransientControl&&Selected("discord.voice-dialog.output")=="Speakers B");
                Check(VisibleFocus(),"Controller can change and return from the output-device dropdown");
                Check(Node("discord.voice-dialog.input-volume") is null&&Node("discord.voice-dialog.input-volume.unavailable") is not null,
                    "Unsupported input gain has an explanation rather than a nonfunctional slider");
                // The full-width selector is centered over the right footer
                // action. Follow actual two-dimensional geometry, then move
                // horizontally to Refresh rather than repeatedly pressing Down.
                await MoveUntil(NavigationDirection.Down,()=>FocusId() is "Widget.discord.voice-dialog.refresh" or "Widget.discord.voice-dialog.open");
                if(FocusId()=="Widget.discord.voice-dialog.open")await Move(NavigationDirection.Left);
                Check(FocusId()=="Widget.discord.voice-dialog.refresh"&&VisibleFocus(),"Refresh devices is reachable and revealed at the current surface size");
                var beforeRefresh=surface.CurrentBinding!.View.Sequence;
                await Button(ControllerButton.A);await Until(()=>surface.CurrentBinding!.View.Sequence>beforeRefresh&&Node("discord.voice-dialog.refresh")?.IsBusy!=true);
                Check(Selected("discord.voice-dialog.input")=="Microphone B"&&Selected("discord.voice-dialog.output")=="Speakers B",
                    "Refreshing device inventory retains provider-confirmed selections");
                await Button(ControllerButton.B);
                await Until(()=>Node("discord.voice-settings-dialog") is null&&FocusId()=="Widget.discord.main.mute");
                Check(OwnsMain()&&VisibleFocus(),"Closing voice settings restores the native context-menu origin");

                step="favorite channel continuity";
                await Button(ControllerButton.LeftStick);
                await Until(()=>Node("discord.picker.page") is not null&&FocusId()=="Widget.discord.channels.search");
                await MoveUntil(NavigationDirection.Down,()=>ChannelFocus() is {Index:>=2});
                var favorite=ChannelFocus()??throw new InvalidOperationException("Missing native channel occurrence.");
                Check(favorite.Index>0&&favorite.ItemKey!="channel.10"&&VisibleFocus(),
                    "Favorite scenario starts on a visible non-first channel through ordinary controller navigation");
                var channelName=favorite.ItemKey switch
                {"channel.11"=>"Co-op night","channel.12"=>"Quiet corner","channel.21"=>"Build planning",
                    "channel.22"=>"Trading lounge","channel.31"=>"Indie discoveries",_=>throw new InvalidOperationException("Unexpected synthetic channel identity.")};
                var channelContext=Find<WidgetIndexedCollectionView>(surface,"discord.channels")?.CaptureContextRow();
                trace.Add(new{step,phase="favorite context before Menu",favorite,hasContext=channelContext is not null,
                    row=channelContext is { } context?new{context.Row.Item.Root.Kind,context.Row.Item.Root.IsDisabled,context.Row.Item.Root.IsBusy,
                        context.Row.Item.Root.ContextMenuButton,context.Row.Item.Root.ContextActions,leaseCurrent=context.Row.Lease.IsCurrent}:null,
                    native=surface.FocusDiagnostics()});
                await Button(ControllerButton.Menu);
                trace.Add(new{step,phase="favorite after Menu",surface.HasTransientControl,focus=FocusId(),focusType=FocusManager.GetFocusedElement(surface.XamlRoot)?.GetType().Name,
                    native=surface.FocusDiagnostics()});
                await Until(()=>surface.HasTransientControl&&FocusManager.GetFocusedElement(surface.XamlRoot) is MenuFlyoutItem {Text:"Add favorite",IsEnabled:true});
                Check(true,"The selected channel's native context menu exposes Add favorite");
                await Button(ControllerButton.A);
                await Until(()=>!surface.HasTransientControl&&ChannelFocus() is {Index:0} moved&&moved.ItemKey==favorite.ItemKey);
                Check(ChannelFocus()!.ItemKey==favorite.ItemKey&&VisibleFocus(),
                    "Saving a favorite moves the channel to first position while keeping focus on its exact occurrence");
                await Button(ControllerButton.Menu);
                await Until(()=>surface.HasTransientControl&&FocusManager.GetFocusedElement(surface.XamlRoot) is MenuFlyoutItem {Text:"Remove favorite",IsEnabled:true});
                await Button(ControllerButton.B);
                await Until(()=>!surface.HasTransientControl&&ChannelFocus()?.ItemKey==favorite.ItemKey);
                Check(ChannelFocus()!.Index==0,"Context-menu B restores the same channel after reorder instead of its former index");
                await Button(ControllerButton.A);
                await Until(()=>Node("discord.picker.page") is null&&Node("discord.main.channel")?.Text==channelName);
                await Until(()=>FocusId()=="Widget.discord.main.mute"&&NativeEntrySettled());
                Check(FocusId()=="Widget.discord.main.mute"&&OwnsMain(),
                    "A after favorite reorder joins the exact synthetic channel and returns to the invoking main control");
                await Button(ControllerButton.LeftStick);
                // Reopening a previously visited scope may restore its exact
                // remembered channel. Search is the first-visit fallback.
                await Until(()=>Node("discord.picker.page") is not null&&NativeEntrySettled()&&
                    (FocusId()=="Widget.discord.channels.search"||ChannelFocus()?.ItemKey==favorite.ItemKey));
                await MoveUntil(NavigationDirection.Down,()=>ChannelFocus() is not null);
                Check(ChannelFocus() is {Index:0} selectedFavorite&&selectedFavorite.ItemKey==favorite.ItemKey&&
                    Find<WidgetIndexedCollectionView>(surface,"discord.channels")?.FocusedRow()?.Item.Root.IsSelected==true,
                    "Reopening the picker preserves the saved favorite and observes that exact channel as selected");
                await Button(ControllerButton.B);
                await Until(()=>Node("discord.picker.page") is null&&FocusId()=="Widget.discord.main.mute");
                // Native focus can precede ScrollViewer's queued bring-into-view.
                await Until(VisibleFocus);
                Check(VisibleFocus(),"Picker B restores the original main focus after saving and joining a favorite");
                Check(validationFailure is null&&browserOwner?.ResidentCount==0,"Voice controls finish without host recovery or media side effects");
            }
            catch(Exception failure){error=failure.ToString();}
            finally
            {
                try{trace.Add(new{step,focus=FocusId(),native=surface?.FocusDiagnostics(),inspection=surface?.CaptureDeveloperInspection()});}
                catch(Exception failure){trace.Add(new{inspectionError=failure.Message});}
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{passed=error is null,step,checks,trace,error,
                    scope="Synthetic provider and actual WinUI controller routes, modals, sliders and dropdowns; no physical controller, account, audio device or capture."}));
                File.Move(path+".tmp",path,true);
            }
            async Task Button(ControllerButton button)
            {await Until(()=>MainFocusEnabled);await RouteButtonAsync(button,ControllerEventPhase.Pressed);await RouteButtonAsync(button,ControllerEventPhase.Released);}
            async Task Move(NavigationDirection direction)
            {
                await Until(()=>MainFocusEnabled);var before=FocusId();
                var frame=Sample();frame.DpadNavigation=new(){Direction=direction,Phase=NavigationPhase.Pressed};Receive(frame);Receive(Sample());await Task.Delay(90,lifetime.Token);
                trace.Add(new{step,phase="directional move",direction=direction.ToString(),before,after=FocusId()});
            }
            async Task MoveUntil(NavigationDirection direction,Func<bool> target)
            {for(var i=0;i<18&&!target();i++)await Move(direction);Check(target(),step+": D-pad "+direction+" reaches its target; focus="+FocusId());}
            async Task Until(Func<bool> predicate)
            {var deadline=Environment.TickCount64+12000;while(!predicate()){if(validationFailure is not null||Environment.TickCount64>deadline)throw new InvalidOperationException("Voice controls did not settle: "+step+"; focus="+FocusId(),validationFailure);await Task.Delay(20,lifetime.Token);}}
            string FocusId()=>surface?.XamlRoot is { } root&&FocusManager.GetFocusedElement(root) is DependencyObject focused?AutomationProperties.GetAutomationId(focused):"";
            bool OwnsMain()=>App.Window is { } window&&DiscordValidationForegroundWindow()==WinRT.Interop.WindowNative.GetWindowHandle(window);
            ViewNode? Node(string id)=>surface?.CurrentBinding is { } binding?Nodes(binding.View.Root).FirstOrDefault(node=>node.Id==id):null;
            string? Selected(string id)=>Node(id)?.SelectOptions?.SingleOrDefault(option=>option.IsSelected)?.Label;
            bool NativeEntrySettled()
            {
                if(surface is null)return false;
                using var state=JsonDocument.Parse(surface.FocusDiagnostics());
                return !state.RootElement.GetProperty("needsEntry").GetBoolean()&&!state.RootElement.GetProperty("focusQueued").GetBoolean()&&
                    !state.RootElement.GetProperty("applying").GetBoolean();
            }
            bool NativeControlReady(string id)=>surface?.XamlRoot?.Content is { } root&&
                Find<FrameworkElement>(root,id) is { IsLoaded:true,ActualWidth:>0,ActualHeight:>0 };
            IndexedCollectionFocusTarget? ChannelFocus()=>surface is null?null:Find<WidgetIndexedCollectionView>(surface,"discord.channels")?.CaptureFocusedItem();
            bool VisibleFocus()
            {
                if(surface is null||FocusManager.GetFocusedElement(surface.XamlRoot) is not FrameworkElement focused)return false;
                var bounds=focused.TransformToVisual(surface).TransformBounds(new(0,0,focused.ActualWidth,focused.ActualHeight));
                return bounds.Width>0&&bounds.Height>0&&bounds.Left>=-.5&&bounds.Top>=-.5&&bounds.Right<=surface.ActualWidth+.5&&bounds.Bottom<=surface.ActualHeight+.5;
            }
            void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);checks.Add(message);}
        };
        static IEnumerable<ViewNode> Nodes(ViewNode root)=>new[]{root}.Concat(root.Children.SelectMany(Nodes));
        static T? Find<T>(DependencyObject root,string id) where T:FrameworkElement
        {if(root is T value&&AutomationProperties.GetAutomationId(value)=="Widget."+id)return value;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)if(Find<T>(VisualTreeHelper.GetChild(root,i),id) is { } child)return child;return null;}
    }
}
