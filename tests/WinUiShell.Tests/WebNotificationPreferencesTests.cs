using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WinUiShell.Tests;

[TestClass]
public sealed class WebNotificationPreferencesTests
{
    [TestMethod]
    public async Task ModesPersistPerWidgetAndConcurrentSavesRetainBothChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "widgetrail-notification-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "preferences.json");
        try
        {
            var store = new WebNotificationPreferences(path); await store.LoadAsync(default);
            Assert.AreEqual(WebNotificationPresentation.BannerAndList, store.Get("discord"));
            await Task.WhenAll(store.SetAsync("discord", WebNotificationPresentation.ListOnly, default), store.SetAsync("other", WebNotificationPresentation.Off, default));
            var reloaded = new WebNotificationPreferences(path); await reloaded.LoadAsync(default);
            Assert.AreEqual(WebNotificationPresentation.ListOnly, reloaded.Get("discord")); Assert.AreEqual(WebNotificationPresentation.Off, reloaded.Get("other"));
            Assert.AreEqual(WebNotificationPresentation.BannerAndList, reloaded.Get("new"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [TestMethod]
    public async Task InvalidFileFailsQuietlyWithoutEnablingBanners()
    {
        var path = Path.Combine(Path.GetTempPath(), "widgetrail-notification-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{broken"); var store = new WebNotificationPreferences(path); await store.LoadAsync(default);
            Assert.IsTrue(store.LoadFailed); Assert.AreEqual(WebNotificationPresentation.ListOnly, store.Get("discord"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
