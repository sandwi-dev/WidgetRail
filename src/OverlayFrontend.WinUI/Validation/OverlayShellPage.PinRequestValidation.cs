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
            WidgetPresentationSession.WidgetPresentationSession? testSession=null;
            void Received(object? sender,WidgetHostEffectEventArgs args)
            { if(args.Effect.Kind==WidgetHostEffectKind.OpenPinnedLayout)DispatcherQueue.TryEnqueue(()=>effects++); }
            try
            {
                var root=Path.GetDirectoryName(Path.GetFullPath(path))!+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(options.SettingsRoot).StartsWith(root,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Pin tests require an isolated profile under the evidence directory.");
                if(startup is not null)await startup;
                if(owner is null || catalogItems.Any(w=>w.Id is not ("pinning-gallery" or "pinning-gallery-peer")))
                    throw new InvalidOperationException("Pin tests accept only the two synthetic Gallery workers.");
                testSession=owner.Session;testSession.HostEffectReceived+=Received;
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
                Check(hostChoiceDialog is null,"Changing the same widget's layout requires no redundant confirmation");
                await Pin("summary");
                await Until(()=>pinned?.LayoutId=="gallery.summary" && pendingPinWidget is null);
                Check(pinned!.Window.Bounds.Width==bounds.Width && pinned.Window.Bounds.Height==bounds.Height,
                    "Returning to a layout restores its own dimensions");
                var original=pinned;
                await OpenUtilities("pinning-gallery-peer");
                await Pin("summary",waitForSettlement:false);
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                await Button(ControllerButton.B);
                await Until(()=>hostChoiceDialog is null);
                Check(ReferenceEquals(pinned,original),"Cancelling replacement preserves the existing pin");
                await Pin("summary",waitForSettlement:false);
                await Until(()=>hostChoiceDialog?.IsOpenForValidation==true);
                hostChoiceDialog!.FocusFirstChoice();
                await Button(ControllerButton.A);
                await Until(()=>pinned?.WidgetId=="pinning-gallery-peer" && hostChoiceDialog is null && pendingPinWidget is null);
                Check(!PinnedInputActive && foreground,"Confirmed replacement remains passive and keeps main input ownership");
                SetVisible(false);
                Check(pinned?.WidgetId=="pinning-gallery-peer","Hiding the overlay leaves the requested pin alive");
                SetVisible(true);
                await Until(()=>foreground && !switching);
                await UnpinAsync(save:true);
                Check(pinned is null,"Normal unpin tears down a widget-requested pin");
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
                if(waitForSettlement)await Until(()=>pendingPinWidget is null && pinned?.LayoutId=="gallery."+layout);
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
