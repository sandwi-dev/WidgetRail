using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class NotificationCategoryTests
{
    [TestMethod]
    public void DeclarationsAndConfigurationAreBounded()
    {
        WidgetNotificationCategory[] categories = [new("songs", "Song changes", EnabledByDefault: false)];
        Assert.IsTrue(WidgetNotificationContract.ValidCategories(categories));
        Assert.IsFalse(WidgetNotificationContract.ValidCategories([categories[0], categories[0]]));
        Assert.IsFalse(WidgetNotificationContract.ValidCategories([new("general", "Reserved")]));
        Assert.IsFalse(WidgetNotificationContract.ValidCategories([new("bad|key", "Name")]));
        Assert.IsFalse(WidgetNotificationContract.ValidCategories([new("songs", "Name", DefaultOpacityPercent: 39)]));
        Assert.IsFalse(WidgetNotificationContract.ValidCategories(Enumerable.Range(0, 9).Select(i => new WidgetNotificationCategory("c" + i, "Name")).ToArray()));
        var command = new WidgetNotificationCommand("songs") { ConfigureCategoryId = "songs" };
        Assert.IsTrue(WidgetNotificationContract.IsValid(command));
        Assert.IsFalse(WidgetNotificationContract.IsValid(command with { UpdateOnly = true }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(command with { Key = "other" }));
        Assert.IsFalse(WidgetNotificationContract.IsValid(command with { Notification = new("songs", "Title") }));
    }
    [TestMethod]
    public void FixedRetentionIsEnforcedWithoutChangingLegacyCategories()
    {
        WidgetNotificationCategory[] declarations = [new("songs", "Songs", Retention: WidgetNotificationRetention.BannerOnly),
            new("results", "Results", Retention: WidgetNotificationRetention.Pending), new("mixed", "Mixed")];
        Assert.IsTrue(WidgetNotificationContract.ValidCategories(declarations));
        Assert.IsFalse(WidgetNotificationContract.ValidCategories([new("bad", "Bad", Retention: (WidgetNotificationRetention)99)]));
        WidgetNotificationCommand Notice(string? category, WidgetNotificationRetention retention) => new("key", new("key", "Title") { CategoryId = category, Retention = retention });
        Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, Notice("songs", WidgetNotificationRetention.BannerOnly)));
        Assert.IsFalse(WidgetNotificationContract.AllowsCommand(declarations, Notice("songs", WidgetNotificationRetention.Pending)));
        Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, Notice("results", WidgetNotificationRetention.Pending)));
        Assert.IsFalse(WidgetNotificationContract.AllowsCommand(declarations, Notice("results", WidgetNotificationRetention.BannerOnly)));
        foreach (var kind in Enum.GetValues<WidgetNotificationRetention>())
        {
            Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, Notice(null, kind)));
            Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, Notice("general", kind)));
            Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, Notice("mixed", kind)));
            Assert.IsFalse(WidgetNotificationContract.AllowsCommand(declarations, Notice("unknown", kind)));
        }
        Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, new("songs") { ConfigureCategoryId = "songs" }));
        Assert.IsTrue(WidgetNotificationContract.AllowsCommand(declarations, new("key")));
    }
    [TestMethod]
    public async Task EnabledDefaultsApplyOnlyToFreshCategoryPreferences()
    {
        var path = Path.Combine(Path.GetTempPath(), "notification-defaults-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            WidgetNotificationCategory[] categories = [new("song-changes", "Song changes", EnabledByDefault: true,
                Retention: WidgetNotificationRetention.BannerOnly)];
            var store = new NotificationPreferences(path); await store.LoadAsync(default);
            await store.EnsureCategoriesAsync("new-install", categories, default);
            Assert.AreEqual(NotificationPresentation.BannerAndList, store.Effective("new-install", "song-changes"));
            await store.SetCategoryAsync("existing-install", "song-changes", new(NotificationPresentation.Off, 65), default);
            var reload = new NotificationPreferences(path); await reload.LoadAsync(default);
            await reload.EnsureCategoriesAsync("existing-install", categories, default);
            Assert.AreEqual(NotificationPresentation.Off, reload.Effective("existing-install", "song-changes"));
            Assert.AreEqual(65, reload.Category("existing-install", "song-changes").Opacity);
        }
        finally { File.Delete(path); }
    }
    [TestMethod]
    public async Task CategoryDefaultsPersistOnceAndMasterAlwaysWins()
    {
        var path = Path.Combine(Path.GetTempPath(), "notification-category-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, """{"version":1,"widgets":{"music":"ListOnly"}}""");
            var store = new NotificationPreferences(path); await store.LoadAsync(default);
            await store.EnsureCategoriesAsync("music", [new("songs", "Songs", EnabledByDefault: false, DefaultOpacityPercent: 80)], default);
            Assert.AreEqual(NotificationPresentation.Off, store.Effective("music", "songs"));
            await Task.WhenAll(store.SetCategoryAsync("music", "songs", new(NotificationPresentation.BannerAndList, 70), default),
                store.EnsureCategoriesAsync("music", [new("songs", "Renamed", EnabledByDefault: false)], default));
            Assert.AreEqual(NotificationPresentation.ListOnly, store.Effective("music", "songs"));
            var reload = new NotificationPreferences(path); await reload.LoadAsync(default);
            Assert.IsFalse(reload.LoadFailed);
            Assert.AreEqual(70, reload.Category("music", "songs").Opacity);
            await reload.SetAsync("music", NotificationPresentation.Off, default);
            Assert.AreEqual(NotificationPresentation.Off, reload.Effective("music", "songs"));
            await reload.SetAsync("music", NotificationPresentation.BannerAndList, default);
            Assert.AreEqual(NotificationPresentation.BannerAndList, reload.Effective("music", "songs"));
            Assert.IsNull(reload.Category("music", "general").Opacity);
        }
        finally { File.Delete(path); }
    }
    [TestMethod]
    public async Task CorruptAndCancelledWritesPreserveExistingFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "notification-category-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "broken");
            var store = new NotificationPreferences(path); await store.LoadAsync(default);
            await Assert.ThrowsAsync<IOException>(() => store.SetAsync("music", NotificationPresentation.Off, default));
            Assert.AreEqual("broken", await File.ReadAllTextAsync(path));
            File.Delete(path); store = new(path); await store.LoadAsync(default);
            await store.EnsureCategoriesAsync("music", [new("songs", "Songs", EnabledByDefault: false)], default);
            var before = await File.ReadAllTextAsync(path);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.SetCategoryAsync("music", "songs", new(NotificationPresentation.BannerAndList), cancel.Token));
            Assert.AreEqual(before, await File.ReadAllTextAsync(path));
        }
        finally { File.Delete(path); }
    }
    [TestMethod]
    public async Task HiddenSourceAdmissionAndQuietUpdatesShareTheCenterPolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "notification-center-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var center = new NotificationCenter(root); await center.Ready;
            await center.Preferences.EnsureCategoriesAsync("help", [new("results", "Results")], default);
            var releases = 0; var banners = 0;
            center.BannerRequested += (_, _) => banners++;
            var content = new WidgetNotification("ready", "Answer") { CategoryId = "results", Visibility = WidgetNotificationVisibility.SourceHidden };
            center.Publish("help", new(), new("ready", content), () => true, () => releases++, sourceVisible: true);
            Assert.HasCount(0, center.Inbox.Entries); Assert.AreEqual(1, releases); Assert.AreEqual(0, banners);
            center.Publish("help", new(), new("ready", content, true), () => true, () => releases++);
            Assert.HasCount(0, center.Inbox.Entries); Assert.AreEqual(2, releases);
            center.Publish("help", new(), new("ready", content), () => true, () => releases++);
            Assert.HasCount(1, center.Inbox.Entries); Assert.AreEqual(1, banners);
            var id = center.Inbox.Entries.Single().Id;
            center.Publish("help", new(), new("ready", content with { Body = "Quiet update" }, true), () => true, sourceVisible: true);
            Assert.AreEqual(id, center.Inbox.Entries.Single().Id);
            center.Publish("help", new(), new("ready", content with { CategoryId = "general" }, true), () => true);
            Assert.AreEqual("results", center.Inbox.Entries.Single().Content!.CategoryId);
            center.Publish("help", new(), new("ready", content), () => true, sourceVisible: true);
            Assert.HasCount(0, center.Inbox.Entries);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

}
