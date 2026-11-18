using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyNativeNotificationIntentsAsync(Action<bool, string> check)
    {
        var inbox = browserOwner!.Notifications;
        await browserOwner.NotificationPreferences.SetAsync("webapp-a", NotificationPresentation.ListOnly, lifetime.Token);
        await Send("native-open");
        await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface is { IsReady: true, IsHostedSession: true }));
        var page = Slots(surface).Select(slot => slot.Surface).Single(item => item?.IsHostedSession == true)!;
        await page.NativeProfile!.SetPermissionStateAsync(CoreWebView2PermissionKind.Notifications,
            "https://shell.fixture.invalid", CoreWebView2PermissionState.Allow);
        page.NavigateForValidation("https://shell.fixture.invalid/native-notification?allowed=1");
        await WebAppUntil(() => !page.IsNavigationInProgress && page.IsReady);
        await Send("native-hide");
        await WebAppUntil(() => Status() == "parked:0" && Slots(surface).All(slot => !ReferenceEquals(slot.Surface, page)));
        check(page.IsReady, "Opted-in owned session remains resident while its native view is parked");
        await CreateNotice("native-known");
        var first = inbox.Entries.Single();
        check(browserOwner.TryGetNativeNotificationIntent(first, out var proof) && proof!.Tag == "native-known" && first.Tag != proof.Tag,
            "Actual native notification retains its raw tag separately from inbox deduplication hash");
        await OpenByController();
        await WebAppUntil(() => Status() == "handled:1" && inbox.Entries.Count == 0);
        await WebAppUntilScript(page, "notificationEvents.join(',')==='shown,closed'");
        check(Slots(surface).All(slot => !ReferenceEquals(slot.Surface, page)),
            "Accepted native notification opens the same-widget native route without attaching the parked browser");
        check(await page.EvaluateFixtureAsync("notificationEvents.filter(x=>x==='clicked').length===0") == "true",
            "Native handoff suppresses website click and retires the original notice exactly once");
        await CreateNotice("native-unmapped");
        await OpenByController();
        await WebAppUntil(() => Status() == "fallback:1" && Slots(surface).Any(slot => ReferenceEquals(slot.Surface, page) && slot.AcceptsInput));
        await WebAppUntilScript(page, "notificationEvents.join(',')==='shown,clicked'");
        check(inbox.Entries.Count == 0 && page.IsBrowsing,
            "Declined native route publishes and focuses its existing browser before the original source click");
        await Send("native-hide");
        await WebAppUntil(() => Status() == "parked:1");
        await CreateNotice("native-known");
        var stale = inbox.Entries.Single();
        page.NavigateForValidation("https://shell.fixture.invalid/native-notification?new-document=1");
        await WebAppUntil(() => !page.IsNavigationInProgress && !stale.Available);
        check(!browserOwner.TryGetNativeNotificationIntent(stale, out _), "Document replacement revokes native notification intent proof");
        await OpenByController();
        await WebAppUntil(() => inbox.Entries.Count == 0);
        check(Status() == "parked:1" && inbox.Entries.Count == 0,
            "Stale document notification opens its widget without invoking a native handler or replacement page callback");

        string? Status() => FindControl<TextBlock>(surface!, "Widget.native.status")?.Text;
        async Task Send(string id)
        {
            var frame = owner!.Session.GetState("webapp-a")!.LastGood!;
            await owner.Session.SendActionAsync(frame.Authority, new(id, id, InputScopeId: frame.Snapshot.ActiveInputScopeId), lifetime.Token);
        }
        async Task CreateNotice(string tag)
        {
            await page.EvaluateFixtureAsync("window.notificationEvents=[];window.n=new Notification('Synthetic native route',{tag:'" + tag +
                "'});n.onshow=()=>notificationEvents.push('shown');n.onclick=()=>notificationEvents.push('clicked');n.onclose=()=>notificationEvents.push('closed');");
            await WebAppUntil(() => inbox.Entries.Count == 1);
        }
        async Task OpenByController()
        {
            var shown = OpenNotificationListForValidation();
            await WebAppUntil(() => utilityPanel is { IsLoaded: true });
            var list = FindControl<ListView>(utilityPanel!, "Overlay.Utility.List")!;
            list.SelectedIndex = 0;
            ((Control)list.Items[0]).Focus(FocusState.Keyboard);
            Receive(new ControllerFrame { Connected = 1 });
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
            await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
            await shown;
            await WebAppUntil(() => utilityPanel is null && !preparingIntent && intentCancellation is null);
        }
    }
}
