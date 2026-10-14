using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WinUiShell.Tests;

[TestClass]
public sealed class BrowserPopupPreferencesTests
{
    [TestMethod]
    public async Task RememberedPlacementPersistsAndIsScopedToWidgetAccountAndOrigin()
    {
        var path = Path.Combine(Path.GetTempPath(), "widgetrail-popup-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new BrowserPopupPreferences(path);
            await store.SetAsync("chat", "personal", "https://chat.example", BrowserPopupPlacement.Pinned, default);
            var restored = new BrowserPopupPreferences(path); await restored.LoadAsync(default);
            Assert.AreEqual(BrowserPopupPlacement.Pinned, restored.Get("chat", "personal", "https://chat.example"));
            Assert.AreEqual(BrowserPopupPlacement.Ask, restored.Get("other", "personal", "https://chat.example"));
            Assert.AreEqual(BrowserPopupPlacement.Ask, restored.Get("chat", "work", "https://chat.example"));
            Assert.AreEqual(BrowserPopupPlacement.Ask, restored.Get("chat", "personal", "https://other.example"));
            await restored.SetAsync("chat", "personal", "https://chat.example", BrowserPopupPlacement.Ask, default);
            var reset = new BrowserPopupPreferences(path); await reset.LoadAsync(default);
            Assert.AreEqual(BrowserPopupPlacement.Ask, reset.Get("chat", "personal", "https://chat.example"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [TestMethod]
    public async Task ConcurrentWritesPreserveChoicesAndMalformedPreferencesAlwaysAsk()
    {
        var path = Path.Combine(Path.GetTempPath(), "widgetrail-popup-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(path, "{broken");
            var store = new BrowserPopupPreferences(path); await store.LoadAsync(default);
            Assert.AreEqual(BrowserPopupPlacement.Ask, store.Get("chat", "personal", "https://chat.example"));
            await Task.WhenAll(store.SetAsync("chat", "personal", "https://chat.example", BrowserPopupPlacement.InWidget, default),
                store.SetAsync("chat", "work", "https://chat.example", BrowserPopupPlacement.Pinned, default));
            var restored = new BrowserPopupPreferences(path); await restored.LoadAsync(default);
            Assert.AreEqual(BrowserPopupPlacement.InWidget, restored.Get("chat", "personal", "https://chat.example"));
            Assert.AreEqual(BrowserPopupPlacement.Pinned, restored.Get("chat", "work", "https://chat.example"));
            await Assert.ThrowsAsync<ArgumentException>(() => restored.SetAsync("chat", "personal", "https://chat.example/path?token=no", BrowserPopupPlacement.Pinned, default));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
