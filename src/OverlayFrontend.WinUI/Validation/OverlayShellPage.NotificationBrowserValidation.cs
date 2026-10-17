using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyNotificationBrowserAsync(Action<bool,string> check)
    {
        var center=notificationCenter!;
        await center.Ready;
        await center.Preferences.SetAsync("intent-source",NotificationPresentation.ListOnly,lifetime.Token);
        center.Inbox.Clear();
        var fixture=BrowserSurface.FixturePage;
        BrowserSurface.FixturePage=_=>"<!doctype html><title>Notification reference</title><p>Isolated notification page</p>";
        try
        {
            await InvokeAsync(new(surface!.CurrentBinding!.Frame,new("notify-browser","notify-browser",ControllerButton.A,InputScopeId:"root")));
            await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
            await WaitFor(()=>center.Inbox.Entries.Count==1);
            check(center.Inbox.Entries[0].Interactive,"Widget notification publishes its declared browser intent through both pipes");
            await SelectAsync("intent-target",enterWidget:true);
            await WaitFor(()=>activeWidget=="intent-target"&&!switching&&surface?.CurrentBinding is not null);
            var original=surface;var originalText=surface!.CurrentBinding!.Frame.Snapshot.Root.Children[0].Text;
            var listTask=OpenNotificationListForValidation();
            await WaitFor(()=>hostChoiceDialog is {IsLoaded:true});
            var list=FindControl<ListView>(hostChoiceDialog!,"Host.Choice.Options")!;
            list.SelectedIndex=0;((Control)list.Items[0]).Focus(FocusState.Keyboard);
            hostChoiceDialog!.ControllerArmed=true;
            await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.A,ControllerEventPhase.Released);
            await listTask.WaitAsync(TimeSpan.FromSeconds(20));
            await WaitFor(()=>expandedBrowser?.Surface is {IsReady:true,IsNavigationInProgress:false,CurrentTitle:"Notification reference"});
            check(activeWidget=="intent-target"&&ReferenceEquals(surface,original)&&center.Inbox.Entries.Count==0,
                "Opening a notification uses a temporary page without navigating the widget underneath");
            check(expandedBrowser!.Surface!.NativeProfile!.IsInPrivateModeEnabled,"Notification browser respects its requested private storage");
            await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.B,ControllerEventPhase.Released);
            await WaitFor(()=>expandedBrowser is null&&!fullscreenLayer.IsOpen);
            check(activeWidget=="intent-target"&&MainFocusEnabled&&surface!.CurrentBinding!.Frame.Snapshot.Root.Children[0].Text==originalText,
                "Closing a notification page restores the unchanged widget underneath");
            await SelectAsync("intent-source",enterWidget:true);
            await WaitFor(()=>activeWidget=="intent-source"&&!switching);
        }
        finally {BrowserSurface.FixturePage=fixture;}
        static async Task WaitFor(Func<bool> condition)
        {
            var end=Environment.TickCount64+15_000;
            while(!condition()){if(Environment.TickCount64>=end)throw new TimeoutException("Notification browser fixture did not settle.");await Task.Delay(20);}
        }
    }
}
