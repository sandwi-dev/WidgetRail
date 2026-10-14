using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WinUiShell.Tests;

[TestClass]
public sealed class NotificationPreferencesTests
{
    [TestMethod]
    public async Task ModesPersistPerWidgetAndConcurrentSavesRetainBothChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "widgetrail-notification-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new NotificationPreferences(path); await store.LoadAsync(default);
            Assert.AreEqual(NotificationPresentation.BannerAndList, store.Get("discord"));
            await Task.WhenAll(store.SetAsync("discord", NotificationPresentation.ListOnly, default), store.SetAsync("other", NotificationPresentation.Off, default));
            var reloaded = new NotificationPreferences(path); await reloaded.LoadAsync(default);
            Assert.AreEqual(NotificationPresentation.ListOnly, reloaded.Get("discord")); Assert.AreEqual(NotificationPresentation.Off, reloaded.Get("other"));
            Assert.AreEqual(NotificationPresentation.BannerAndList, reloaded.Get("new"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [TestMethod]
    public async Task InvalidFileFailsQuietlyWithoutEnablingBanners()
    {
        var path = Path.Combine(Path.GetTempPath(), "widgetrail-notification-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{broken"); var store = new NotificationPreferences(path); await store.LoadAsync(default);
            Assert.IsTrue(store.LoadFailed); Assert.AreEqual(NotificationPresentation.ListOnly, store.Get("discord"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
