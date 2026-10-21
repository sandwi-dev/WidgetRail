using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task VerifyAppUpdateNotificationAsync(Action<bool, string> check)
    {
        var center = notificationCenter!;
        await center.Ready;
        await WebAppUntil(() => center.Inbox.Entries.Any(entry => ReferenceEquals(entry.Owner, appUpdateNotificationOwner)));
        check(activeWidget == "webapp-a", "Startup check produces a pending update without opening Settings");
        var snapshot = await owner!.Session.StartAppUpdateCheckAsync(lifetime.Token);
        check(snapshot.State == "available" && snapshot.AvailableVersion == "0.1.0-preview.99" && snapshot.AutomaticallyCheck,
            "Startup update result crosses the real host pipe with the saved preference");
        ReceiveAppUpdate(owner.Session, snapshot);
        await Task.Delay(100);
        check(center.Inbox.Entries.Count(entry => ReferenceEquals(entry.Owner, appUpdateNotificationOwner)) == 1,
            "Repeated startup request and event do not duplicate the pending update");
        var pending = OpenNotificationListForValidation();
        await WebAppUntil(() => utilityPanel is { IsLoaded: true });
        var list = FindControl<ListView>(utilityPanel!, "Overlay.Utility.List")!;
        list.SelectedIndex = 0; ((Control)list.Items[0]).Focus(FocusState.Keyboard);
        Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame { Connected = 1 });
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Pressed);
        await RouteButtonAsync(ControllerButton.A, ControllerEventPhase.Released);
        await pending;
        await WebAppUntil(() => activeWidget == "settings" && FindControl<Button>(surface!, "Widget.updates") is not null);
        check(center.Inbox.Entries.Count == 0, "Opening the update uses the validated Settings intent and removes the pending entry");
        ReceiveAppUpdate(owner.Session, snapshot with { Revision = snapshot.Revision + 1 });
        await Task.Delay(100);
        check(center.Inbox.Entries.Count == 0, "The same version cannot re-notify after activation within this session");
        await SelectAsync("webapp-a", enterWidget: true);
        await WebAppUntil(() => Slots(surface).Any(slot => slot.Surface?.IsBrowsing == true));
    }
}
