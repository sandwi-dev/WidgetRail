using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WinUiShell.Tests;

[TestClass]
public sealed class BrowserLibraryStoreTests
{
    [TestMethod]
    public async Task HistoryAndBookmarksSurviveReloadAndClearIndependently()
    {
        var root = Path.Combine(Path.GetTempPath(), "WidgetRail.Library.Tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "library.json");
        try
        {
            var store = await BrowserLibraryStore.LoadAsync(path);
            await store.RecordAsync("https://example.com/one", "One");
            await store.RecordAsync("https://example.com/two", "Two");
            await store.RecordAsync("https://example.com/one", "Updated");
            await store.ToggleBookmarkAsync("https://example.com/one", "Saved");
            var loaded = await BrowserLibraryStore.LoadAsync(path);
            Assert.AreEqual(2, loaded.History.Count);
            Assert.AreEqual("Updated", loaded.History[0].Title);
            Assert.AreEqual("Saved", loaded.Bookmarks.Single().Title);
            await loaded.ClearHistoryAsync();
            loaded = await BrowserLibraryStore.LoadAsync(path);
            Assert.AreEqual(0, loaded.History.Count);
            Assert.IsTrue(loaded.IsBookmarked("https://example.com/one"));
            await loaded.ToggleBookmarkAsync("https://example.com/one", "Saved");
            Assert.AreEqual(0, (await BrowserLibraryStore.LoadAsync(path)).Bookmarks.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task HistoryIsBoundedAndRejectsUnsafeOrBlankPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "WidgetRail.Library.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = await BrowserLibraryStore.LoadAsync(Path.Combine(root, "library.json"));
            foreach (var url in new[] { "about:blank", "file:///C:/private", "javascript:alert(1)", "https://user:password@example.com" })
            { await store.RecordAsync(url, "Bad"); await store.ToggleBookmarkAsync(url, "Bad"); }
            Assert.AreEqual(0, store.History.Count); Assert.AreEqual(0, store.Bookmarks.Count);
            for (var i = 0; i < 205; i++) await store.RecordAsync("https://example.com/" + i, new string('x', 300));
            Assert.AreEqual(200, store.History.Count); Assert.AreEqual(256, store.History[0].Title.Length);
            Assert.AreEqual("https://example.com/204", store.History[0].Url);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => store.ClearHistoryAsync(cancelled.Token));
            Assert.AreEqual(200, (await BrowserLibraryStore.LoadAsync(Path.Combine(root, "library.json"))).History.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CorruptStorageAndSeparateProfilesCannotLeakEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "WidgetRail.Library.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "one.json"); await File.WriteAllTextAsync(path, "broken");
            var store = await BrowserLibraryStore.LoadAsync(path);
            Assert.AreEqual(0, store.History.Count);
            await store.RecordAsync("https://example.com", "One");
            Assert.AreEqual(0, (await BrowserLibraryStore.LoadAsync(Path.Combine(root, "two.json"))).History.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
