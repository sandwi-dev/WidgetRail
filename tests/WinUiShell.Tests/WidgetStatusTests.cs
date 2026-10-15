using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayFrontend.WinUI.Notifications;
using WidgetRail.WidgetProtocol;
namespace WinUiShell.Tests;
[TestClass]
public sealed class WidgetStatusTests
{
    [TestMethod] public void RadialKeepsOneStatusAndPrioritizesExplicitLifecycle()
    {
        Assert.AreEqual(WidgetStatusKind.Stopped, new WidgetStatus(7, Audio: true, Stopped: true, Recording: true).Radial);
        Assert.AreEqual(WidgetStatusKind.Failed, new WidgetStatus(7, Failed: true).Radial);
        Assert.AreEqual(WidgetStatusKind.Recording, new WidgetStatus(7, Audio: true, Recording: true, Failed: true).Radial);
        Assert.AreEqual(WidgetStatusKind.Notifications, new WidgetStatus(7, Audio: true).Radial);
        Assert.AreEqual(WidgetStatusKind.Audio, new WidgetStatus(Audio: true).Radial);
        Assert.AreEqual(WidgetStatusKind.None, default(WidgetStatus).Radial);
    }
    [TestMethod] public void HiddenSecondaryStatesRemainInAccessibleDescription()
    {
        var value = new WidgetStatus(120, Audio: true, RemotePlayback: true);
        Assert.AreEqual("99+", value.Count);
        StringAssert.Contains(value.Describe("Music"), "120 pending notifications");
        StringAssert.Contains(value.Describe("Music"), "Playing audio");
        StringAssert.Contains(value.Describe("Music"), "Remote playback");
        Assert.AreEqual("Music. Stopped", new WidgetStatus(Audio: true, Stopped: true).Describe("Music"));
    }
    [TestMethod] public void PendingCountsExcludeTransientBannersAndFollowDismissal()
    {
        using var inbox = new NotificationInbox();
        var callbacks = new NotificationCallbacks(() => true, () => {}, () => {}, () => {}, () => {});
        inbox.Add("music", "widget", "", "song", "Song", "", this, callbacks, NotificationPresentation.BannerAndList,
            new WidgetNotification("song", "Song") { Retention = WidgetNotificationRetention.BannerOnly });
        var pending = inbox.Add("chat", "web", "", "message", "Message", "", this, callbacks, NotificationPresentation.BannerAndList)!;
        Assert.HasCount(1, inbox.Entries);
        Assert.AreEqual("chat", inbox.Entries[0].Widget);
        inbox.Dismiss(pending.Id); Assert.HasCount(0, inbox.Entries);
    }
}
