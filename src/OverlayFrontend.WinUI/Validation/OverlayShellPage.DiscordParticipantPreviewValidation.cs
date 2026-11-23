using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Validation;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;
internal sealed partial class OverlayShellPage
{
    internal void EnableDiscordParticipantPreviewValidation(string path)
    {
        var started=false;
        Loaded+=async (_,_)=>
        {
            if(started)return;started=true;
            var checks=new List<string>();var trace=new List<object>();var geometry=new List<object>();Exception? failed=null;
            EventHandler<WidgetRail.WidgetPresentationSession.WidgetPresentationChangedEventArgs>? publication=null;var gaps=0;var gapLost=false;
            try
            {
                var root=Path.GetDirectoryName(Path.GetFullPath(path))!+Path.DirectorySeparatorChar;
                if(!Path.GetFullPath(options.SettingsRoot).StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Synthetic profile required");
                if(startup is not null)await startup;
                if(!catalogItems.Any(item=>item.Id=="widgetrail.tests.discord-ui"))throw new InvalidOperationException("Synthetic Discord worker required");
                await SelectAsync("widgetrail.tests.discord-ui",true);App.Window!.Activate();
                await Until(()=>MainFocusEnabled&&Find<ListViewItem>("discord.participants.Item.1") is {IsLoaded:true,ActualWidth:>0,ActualHeight:>0} ready && Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(ready)?.GetBoundingRectangle().Width>0);
                surface!.FocusTrace=value=>{if(trace.Count<160)trace.Add(value);};
                publication=(_,args)=>DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High,()=>
                {
                    if(args.State.WidgetId!="widgetrail.tests.discord-ui" || surface?.IsFocusPreviewOpen!=true)return;
                    if(surface.HasPendingPreviewPublicationForValidation)
                    {gaps++;surface.RefreshPreviewForValidation();if(!surface.IsFocusPreviewOpen)gapLost=true;}
                });
                owner!.Session.PresentationChanged+=publication;
                var row=Find<ListViewItem>("discord.participants.Item.1")!;
                await NativeContextProbeInput.HoverAsync(row,async ()=>
                {
                    await Until(()=>surface.IsFocusPreviewOpen);
                    var chrome=surface.FocusPreviewChrome!;
                    if(Environment.GetCommandLineArgs().Contains("--discord-participant-speaking"))
                    {
                        var avatar=Walk(row).OfType<FrameworkElement>().Single(element=>AutomationProperties.GetAutomationId(element).EndsWith(".avatar",StringComparison.Ordinal));
                        var ring=Walk(row).OfType<FrameworkElement>().Single(element=>AutomationProperties.GetAutomationId(element).EndsWith(".avatar-frame",StringComparison.Ordinal));
                        var bounds=avatar.TransformToVisual(ring).TransformBounds(new(0,0,avatar.ActualWidth,avatar.ActualHeight));
                        geometry.Add(new{ringWidth=ring.ActualWidth,ringHeight=ring.ActualHeight,avatar=bounds,
                            centerX=bounds.X+bounds.Width/2-ring.ActualWidth/2,centerY=bounds.Y+bounds.Height/2-ring.ActualHeight/2});
                        var revision=surface.FocusPreviewLeaseForValidation!.Range.Source.ContentRevision;
                        var frames=0;var lost=false;
                        EventHandler<object> observe=(_,_)=>{frames++;if(!surface.IsFocusPreviewOpen||!ReferenceEquals(surface.FocusPreviewChrome,chrome))lost=true;};
                        CompositionTarget.Rendering+=observe;
                        try {await Until(()=>surface.FocusPreviewLeaseForValidation?.Range.Source.ContentRevision>=revision+3);}
                        finally {CompositionTarget.Rendering-=observe;}
                        trace.Add(new{kind="speaking-hover",frames,lost});
                        Check(!lost&&frames>0,"Speaking-only publications retain passive hover preview on every sampled native frame");
                        Check(Math.Abs(bounds.X+bounds.Width/2-ring.ActualWidth/2)<.6&&Math.Abs(bounds.Y+bounds.Height/2-ring.ActualHeight/2)<.6,
                            "Participant avatar is centered inside its speaking ring");
                    }
                    var mute=Walk(chrome).OfType<Button>().Single();
                    await Until(()=>mute.IsLoaded&&mute.ActualWidth>0&&mute.ActualHeight>0);
                    // Enter through the supported route before using UIA screen
                    // geometry; unentered portal controls are not exposed there.
                    if(!row.Focus(FocusState.Keyboard))throw new InvalidOperationException("Participant row did not accept controller entry focus");
                    await surface.HandleControllerButtonAsync(WidgetRail.WidgetProtocol.ControllerButton.X);
                    await Until(()=>ReferenceEquals(FocusManager.GetFocusedElement(surface.XamlRoot),mute) && Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(mute)?.GetBoundingRectangle().Width>0);
                    await NativeContextProbeInput.ClickWindowElementAsync((nint)hostWindow,mute);
                    await Task.Delay(1400);
                    Check(!gapLost,"Pending publication retains preview paint without reauthorizing stale input");
                    Check(surface.IsFocusPreviewOpen&&ReferenceEquals(surface.FocusPreviewChrome,chrome),"Mouse mute acknowledgement retains the open preview");
                    Check(ReferenceEquals(FocusManager.GetFocusedElement(surface.XamlRoot),mute),"Mouse mute retains its exact native focus");
                    var slider=Walk(chrome).OfType<Slider>().Single();double released=0;
                    await NativeContextProbeInput.HoldSliderDragAsync(slider,async ()=>
                    {
                        await Task.Delay(1800);
                        Check(surface.IsFocusPreviewOpen&&ReferenceEquals(surface.FocusPreviewChrome,chrome),"Held mouse drag retains the preview through provider updates");
                        Check(ReferenceEquals(FocusManager.GetFocusedElement(surface.XamlRoot),slider),"Held mouse drag keeps slider focus rather than header fallback");
                        released=slider.Value;
                    },async ()=>
                    {
                        await Task.Delay(500);
                        Check(slider.Value==released,"Mouse motion after release cannot continue adjusting volume");
                    });
                    slider.Focus(FocusState.Keyboard);surface.ActivateFocused();surface.MoveFocus(FocusNavigationDirection.Left);
                    await Task.Delay(1400);
                    Check(surface.IsFocusPreviewOpen&&ReferenceEquals(FocusManager.GetFocusedElement(surface.XamlRoot),slider),"Controller adjustment retains preview and focus through the update");
                });
                Check(gaps>0,"Real worker publication exercised the lease-retirement/UI-commit gap");
            }
            catch(Exception error){failed=error;}
            finally{if(publication is not null && owner is not null)owner.Session.PresentationChanged-=publication;if(surface is not null)surface.FocusTrace=null;}
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=failed is null,checks,geometry,error=failed?.ToString(),preview=surface?.FocusPreviewDiagnostics(),trace}));
            void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);}
            T? Find<T>(string id)where T:FrameworkElement=>surface is null?null:Walk(surface).OfType<T>().FirstOrDefault(item=>AutomationProperties.GetAutomationId(item)=="Widget."+id);
            async Task Until(Func<bool> ready){var until=Environment.TickCount64+15000;while(!ready()){if(Environment.TickCount64>until)throw new TimeoutException("Participant preview did not settle");await Task.Delay(25);}}
        };
        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {yield return root;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(root,i)))yield return child;}
    }
}


