using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WinUiShell.Tests;

[TestClass]
public sealed class NotificationInboxTests
{
    private sealed class Source
    {
        internal bool Current = true;
        internal int Shown, Clicked, Closed, Detached;
        internal NotificationCallbacks Callbacks => new(() => Current, () => Shown++, () => Clicked++, () => Closed++, () => Detached++);
    }
    private static NotificationEntry Add(NotificationInbox inbox, Source source, string tag = "", string widget = "webapp", string profile = "default", string origin = "https://fixture.invalid") =>
        inbox.Add(widget, profile, origin, tag, "Title", "Body", source, source.Callbacks, WebNotificationPresentation.BannerAndList)!;

    [TestMethod]
    public void BannerExpiryAndBurstUpdatesKeepPendingItemsAndDoNotExtendInterruption()
    {
        using var inbox = new NotificationInbox(); var first = new Source(); var second = new Source(); var banner = new NotificationBannerState();
        var a = Add(inbox, first); Assert.IsTrue(banner.Offer(a, 100)); inbox.MarkShown(a.Id);
        var b = Add(inbox, second); Assert.IsTrue(banner.Offer(b, 3000)); inbox.MarkShown(b.Id);
        Assert.AreEqual(4100, banner.ExpiresAt); banner.Expire(4100);
        Assert.IsNull(banner.EntryId); Assert.HasCount(2, inbox.Entries);
        Assert.AreEqual(0, first.Closed + second.Closed);
        Assert.IsFalse(banner.Offer(a, 4500));
    }
    [TestMethod]
    public void TagsAreScopedByWidgetProfileAndOrigin()
    {
        using var inbox = new NotificationInbox(); var original = new Source(); var replacement = new Source();
        var a = Add(inbox, original, "chat"); inbox.MarkShown(a.Id);
        Add(inbox, new(), "chat", widget: "other"); Add(inbox, new(), "chat", profile: "other"); Add(inbox, new(), "chat", origin: "https://other.invalid");
        Add(inbox, replacement, "chat"); Assert.HasCount(4, inbox.Entries);
        Assert.AreEqual(1, original.Closed); Assert.AreEqual(1, original.Detached); Assert.AreEqual(0, replacement.Shown);
    }
    [TestMethod]
    public void WithdrawalDoesNotEchoCloseAndOverflowDoesNotEvictPendingItems()
    {
        using var inbox = new NotificationInbox(1); var a = new Source(); var b = new Source();
        var item = Add(inbox, a); inbox.MarkShown(item.Id);
        Assert.IsNull(Add(inbox, b)); Assert.AreEqual(1, inbox.OverflowCount); Assert.AreEqual(1, b.Detached);
        Assert.AreSame(item, inbox.Find(item.Id)); inbox.Withdraw(item.Id);
        Assert.AreEqual(0, a.Closed); Assert.AreEqual(1, a.Detached);
    }
    [TestMethod]
    public async Task FailedOrStaleActivationPreservesThePendingEntryWithoutCallingReplacementPage()
    {
        using var inbox = new NotificationInbox(); var source = new Source(); var item = Add(inbox, source);
        Assert.IsFalse(await inbox.ActivateAsync(item.Id, _ => Task.FromResult(false)));
        Assert.AreSame(item, inbox.Find(item.Id)); Assert.AreEqual(0, source.Clicked);
        Assert.IsFalse(await inbox.ActivateAsync(item.Id, _ => { inbox.InvalidateOwner(source); return Task.FromResult(true); }));
        Assert.IsNull(item.Owner); Assert.AreEqual(0, source.Clicked); Assert.AreEqual(1, source.Detached);
        inbox.Dismiss(item.Id); Assert.AreEqual(0, source.Closed);
    }
    [TestMethod]
    public async Task SuccessfulActivationReportsShownAndClickedWithoutAlsoDismissing()
    {
        using var inbox = new NotificationInbox(); var source = new Source(); var item = Add(inbox, source);
        inbox.MarkShown(item.Id); inbox.MarkShown(item.Id);
        Assert.IsTrue(await inbox.ActivateAsync(item.Id, _ => Task.FromResult(true)));
        Assert.IsFalse(await inbox.ActivateAsync(item.Id, _ => Task.FromResult(true)));
        Assert.AreEqual(1, source.Shown); Assert.AreEqual(1, source.Clicked); Assert.AreEqual(0, source.Closed); Assert.AreEqual(1, source.Detached);
    }
    [TestMethod]
    public void OffDoesNotClaimToDisplayOrRetainNativeCallbacks()
    {
        using var inbox = new NotificationInbox(); var source = new Source();
        Assert.IsNull(inbox.Add("app", "default", "https://fixture.invalid", "", "Title", "Body", source, source.Callbacks, WebNotificationPresentation.Off));
        Assert.AreEqual(0, source.Shown + source.Closed); Assert.AreEqual(1, source.Detached); Assert.HasCount(0, inbox.Entries);
    }
}
