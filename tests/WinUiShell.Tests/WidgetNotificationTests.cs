using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class WidgetNotificationTests
{
    private static NotificationCallbacks Callbacks => new(() => true, () => { }, () => { }, () => { }, () => { });
    [TestMethod]
    public void BannerOnlyNeverCountsAsPendingAndExpiryReleasesIt()
    {
        using var inbox = new NotificationInbox();
        var content = new WidgetNotification("song", "Track") { Retention = WidgetNotificationRetention.BannerOnly };
        var entry = inbox.Add("music", "widget", "", "song", "Track", "Artist", new(), Callbacks, NotificationPresentation.BannerAndList, content)!;
        Assert.HasCount(0, inbox.Entries); Assert.IsNotNull(inbox.Find(entry.Id));
        inbox.ReleaseBanner(entry.Id); Assert.IsNull(inbox.Find(entry.Id));
    }
    [TestMethod]
    public async Task RetentionAndInteractionAreIndependent()
    {
        using var inbox = new NotificationInbox();
        var content = new WidgetNotification("status", "Saved") { Interaction = WidgetNotificationInteraction.Informational };
        var entry = inbox.Add("app", "widget", "", "status", "Saved", "", new(), Callbacks, NotificationPresentation.BannerAndList, content)!;
        Assert.HasCount(1, inbox.Entries);
        Assert.IsFalse(await inbox.OpenAsync(entry.Id, _ => throw new Exception("Not interactive"), _ => throw new Exception("Not interactive")));
        inbox.ReleaseBanner(entry.Id); Assert.IsNotNull(inbox.Find(entry.Id));
    }
    [TestMethod]
    public void ReplacementDoesNotExtendBannerAndKeysAreWidgetScoped()
    {
        using var inbox = new NotificationInbox(); var banner = new NotificationBannerState();
        var content = new WidgetNotification("song", "First") { Retention = WidgetNotificationRetention.BannerOnly };
        var first = inbox.Add("music", "widget", "", "song", "First", "", new(), Callbacks, NotificationPresentation.BannerAndList, content)!;
        banner.Offer(first, 100);
        var second = inbox.Add("music", "widget", "", "song", "Second", "", new(), Callbacks, NotificationPresentation.BannerAndList, content)!;
        Assert.IsNull(inbox.Find(first.Id)); Assert.IsTrue(banner.Offer(second, 200)); Assert.AreEqual(4100, banner.ExpiresAt);
        inbox.WithdrawWidgetKey("other", "song"); Assert.IsNotNull(inbox.Find(second.Id));
        inbox.WithdrawWidgetKey("music", "song"); Assert.IsNull(inbox.Find(second.Id));
    }
    [TestMethod]
    public void ContentUpdatesKeepIdentityAndCannotReviveOrChangeRetention()
    {
        using var inbox = new NotificationInbox();
        var content = new WidgetNotification("song", "Track") { Retention = WidgetNotificationRetention.BannerOnly };
        var entry = inbox.Add("music", "widget", "", "song", "Track", "", new(), Callbacks, NotificationPresentation.BannerAndList, content)!;
        var updated = inbox.UpdateWidget("music", content with { Metadata = "3:45" });
        Assert.AreSame(entry, updated); Assert.AreEqual(1, entry.ContentRevision);
        Assert.IsNull(inbox.UpdateWidget("music", content with { Retention = WidgetNotificationRetention.Pending }));
        inbox.ReleaseBanner(entry.Id);
        Assert.IsNull(inbox.UpdateWidget("music", content));
    }
    [TestMethod]
    public void PlacementUsesTheSameCornersForEveryVisibleObstacleAndKeepsAStableCorner()
    {
        var area = new PinnedBounds(100, 50, 1920, 1080);
        var first = NotificationPlacement.Choose(area, 420, 120, [])!.Value;
        var second = NotificationPlacement.Choose(area, 420, 120, [first])!.Value;
        Assert.AreNotEqual(first, second);
        Assert.AreEqual(second, NotificationPlacement.Choose(area, 420, 120, [], second));
        Assert.IsNull(NotificationPlacement.Choose(area, 420, 120, [area]));
    }
    [TestMethod]
    public void ContractRejectsUnsafeOrUnboundedCustomization()
    {
        var content = new WidgetNotification("key", "Title"); Assert.IsTrue(WidgetNotificationContract.IsValid(content));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { ArtworkUrl = "file:///private.png" }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { BackgroundOpacityPercent = 0 }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { DurationMilliseconds = int.MaxValue }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { Retention = (WidgetNotificationRetention)99 }));
        var intent = WidgetIntentRequest.Create(WidgetIntentContracts.AppUpdates, System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        Assert.IsTrue(WidgetNotificationContract.IsValid(content with { OpenIntent = intent }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { OpenIntent = intent, Retention = WidgetNotificationRetention.BannerOnly }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { OpenIntent = intent, Interaction = WidgetNotificationInteraction.Informational }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(content with { OpenIntent = intent with { SchemaDigest = "invalid" } }));
    }
}
