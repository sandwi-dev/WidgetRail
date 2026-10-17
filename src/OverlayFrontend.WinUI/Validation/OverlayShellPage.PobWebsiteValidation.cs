using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnablePobWebsiteValidation(string path,Func<Task> hideAndReopen)
    {
        Loaded += async (_,_) =>
        {
            var checks=new List<string>();string? error=null;
            var oldFixture=BrowserSurface.FixturePage;
            var originalAppearance=Appearance;
            BrowserSurface.FixturePage=_=>"<!doctype html><title>Website fixture</title><input aria-label='Fixture input'><a href='/next'>Next page</a><p style='height:2000px'>Scrollable reference</p>";
            try
            {
                if(startup is not null)await startup;
                await Until(()=>interactive&&foreground&&!switching&&surface?.CurrentBinding is not null&&!Busy());
                var widget=activeWidget!;
                await Action("menu");await Action("websites.overlay");
                var origin=FindControl<Button>(surface!,"Widget.pob.open-poe-ninja")!;
                origin.Focus(FocusState.Keyboard);
                var sourceHints=surface!.CurrentBinding!.Frame.Snapshot.Surface;
                await Action("open-poe-ninja");
                await Until(()=>Page() is {IsReady:true,IsNavigationInProgress:false});
                var page=Page()!;var profile=page.ProfileName;
                var dialog=Walk(XamlRoot.Content).OfType<FrameworkElement>().Single(e=>AutomationProperties.GetAutomationId(e)=="Widget.pob-website");
                // The menu's resize animation may still be settling when its
                // controls become available. Measure the final viewport.
                await Task.Delay(500);
                var sourceWidth=surface.ActualWidth;var sourceHeight=surface.ActualHeight;
                var rect=dialog.TransformToVisual(surface).TransformBounds(new(0,0,dialog.ActualWidth,dialog.ActualHeight));
                Check(sourceHints==surface.CurrentBinding!.Frame.Snapshot.Surface&&rect.X>=-1&&rect.Y>=-1&&rect.Right<=sourceWidth+1&&rect.Bottom<=sourceHeight+1,$"Browser modal remains inside widget bounds ({rect}; viewport {sourceWidth}x{sourceHeight})");
                Check(dialog.ActualWidth>=sourceWidth-80&&dialog.ActualHeight>=sourceHeight-80,"Browser modal uses most of the widget viewport");
                Check(!page.NativeProfile!.IsInPrivateModeEnabled,"Remote web session uses persistent storage");
                var browserDocument=new WebBrowserDocument("browser",1,"https://example.com","Browser"){InteractionMode=BrowserInteractionMode.InteractOnFocus,Options=new(){Storage=BrowserStorage.Persistent}};
                Check(profile!=BrowserProfileIdentity.Name("widgetrail.samples.browser",browserDocument),"PoB profile is isolated from Browser widget storage");
                await page.EvaluateFixtureAsync("localStorage.setItem('pob-modal-test','saved'); document.cookie='pob-modal-test=saved;Max-Age=3600;SameSite=Lax;Secure'; window.sessionTest=42; true");
                for(var cycle=0;cycle<3;cycle++)
                {
                    ApplyOverlayMotionValidationAppearance(originalAppearance with
                    {Motion=cycle==1?WidgetRail.PlatformSettings.MotionPreference.Reduced:WidgetRail.PlatformSettings.MotionPreference.Full});
                    await hideAndReopen();
                    await Until(()=>visible&&foreground&&interactive&&!switching&&ReferenceEquals(Page(),page));
                    await Task.Delay(300);
                    dialog=Walk(XamlRoot.Content).OfType<FrameworkElement>().Single(e=>AutomationProperties.GetAutomationId(e)=="Widget.pob-website");
                    var bounds=dialog.TransformToVisual(surface).TransformBounds(new(0,0,dialog.ActualWidth,dialog.ActualHeight));
                    var webBounds=page.TransformToVisual(surface).TransformBounds(new(0,0,page.ActualWidth,page.ActualHeight));
                    File.WriteAllText(path+".geometry",JsonSerializer.Serialize(new{cycle,bounds,webBounds,widgetWidth=surface!.ActualWidth,widgetHeight=surface.ActualHeight}));
                    Check(bounds.X>=-1&&bounds.Y>=-1&&bounds.Right<=surface!.ActualWidth+1&&bounds.Bottom<=surface.ActualHeight+1&&
                        webBounds.X>=bounds.X-1&&webBounds.Y>=bounds.Y-1&&webBounds.Right<=bounds.Right+1&&webBounds.Bottom<=bounds.Bottom+1,
                        "Hide/reopen "+cycle+" keeps modal and WebView within the widget");
                    Check(await page.EvaluateFixtureAsync("window.sessionTest")=="42","Hide/reopen "+cycle+" retains the same live session");
                }
                var originalWidth=surface!.Width;var originalHeight=surface.Height;
                try
                {
                    foreach(var size in new[]{(Width:720d,Height:640d),(Width:1040d,Height:880d)})
                    {
                        surface.Width=size.Width;surface.Height=size.Height;
                        await Task.Delay(400);
                        var bounds=dialog.TransformToVisual(surface).TransformBounds(new(0,0,dialog.ActualWidth,dialog.ActualHeight));
                        var webBounds=page.TransformToVisual(surface).TransformBounds(new(0,0,page.ActualWidth,page.ActualHeight));
                        Check(bounds.X>=-1&&bounds.Y>=-1&&bounds.Right<=surface.ActualWidth+1&&bounds.Bottom<=surface.ActualHeight+1&&
                            webBounds.X>=bounds.X-1&&webBounds.Y>=bounds.Y-1&&webBounds.Right<=bounds.Right+1&&webBounds.Bottom<=bounds.Bottom+1,
                            $"Resize to {size.Width}x{size.Height} keeps modal and WebView bounded");
                    }
                }
                finally {surface.Width=originalWidth;surface.Height=originalHeight;}
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
                await Until(()=>surface!.CurrentBinding!.Frame.Snapshot.Root.Kind!=ViewNodeKind.ModalLayer);
                await Until(()=>FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focus&&AutomationProperties.GetAutomationId(focus)=="Widget.pob.open-poe-ninja");
                Check(true,"Controller Back closes modal and restores the originating button");
                await Action("open-poe-ninja");await Until(()=>ReferenceEquals(Page(),page)&&page.IsReady);
                Check(await page.EvaluateFixtureAsync("window.sessionTest") == "42","Reopening attaches the same page without reload");
                await Action("website-close");
                await owner!.Session.RestartAsync(owner.Session.GetTarget(widget));
                await SelectAsync(widget,enterWidget:true);await Until(()=>!Busy()&&surface?.CurrentBinding is not null&&!switching);
                await Action("menu");await Action("open-poe-ninja");await Until(()=>Page() is {IsReady:true,IsNavigationInProgress:false});
                var reopened=Page()!;
                Check(!ReferenceEquals(page,reopened)&&reopened.ProfileName==profile,"Worker restart recreates the session with the same profile");
                Check(await reopened.EvaluateFixtureAsync("localStorage.getItem('pob-modal-test')==='saved' && document.cookie.includes('pob-modal-test=saved')")=="true","Website storage and persistent cookies survive session destruction");
                await reopened.EvaluateFixtureAsync("localStorage.removeItem('pob-modal-test');document.cookie='pob-modal-test=;Max-Age=0;Secure';true");
                await Action("website-close");
                Check(!fullscreenLayer.IsOpen&&expandedBrowser is null,"Website workflow never enters fullscreen expansion");
            }
            catch(Exception failure){error=failure.ToString();}
            finally
            {
                BrowserSurface.FixturePage=oldFixture;
                ApplyOverlayMotionValidationAppearance(originalAppearance);
                await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=error is null,checks,error}));
            }
            bool Busy()=>surface?.CurrentBinding is not {} binding||Nodes(binding.Frame.Snapshot.Root).Any(n=>n.Id=="pob.cancel-operation");
            BrowserSurface? Page()=>Walk(XamlRoot.Content).OfType<BrowserSlot>().FirstOrDefault(s=>AutomationProperties.GetAutomationId(s)=="Widget.pob.website")?.Surface;
            async Task Action(string id)
            {
                await Until(()=>!Busy());
                await Until(()=>surface?.CurrentBinding is {} current&&Nodes(current.Frame.Snapshot.Root).Any(n=>n.ActionId==id||n.SelectOptions.Any(option=>option.ActionId==id)));
                var frame=surface!.CurrentBinding!.Frame;
                var control=Nodes(frame.Snapshot.Root).FirstOrDefault(n=>n.ActionId==id||n.SelectOptions.Any(option=>option.ActionId==id));
                if(control is null)throw new InvalidOperationException("Missing action "+id);
                await InvokeAsync(new(frame,new(id,control.Id,ControllerButton.A,InputScopeId:frame.Snapshot.ActiveInputScopeId)));
                await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
                await Until(()=>surface?.CurrentBinding?.Frame.Snapshot.Sequence>frame.Snapshot.Sequence);
                await Until(()=>!Busy());
            }
            void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks.Add(label);File.WriteAllText(path+".progress",string.Join('\n',checks));}
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node)=>new[]{node}.Concat(node.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        {yield return node;for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(node,i)))yield return child;}
        static async Task Until(Func<bool> condition)
        {var end=Environment.TickCount64+60000;while(!condition()){if(Environment.TickCount64>end)throw new TimeoutException("PoB website fixture did not settle.");await Task.Delay(30);}}
    }
}
