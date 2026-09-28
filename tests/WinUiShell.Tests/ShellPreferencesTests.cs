using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ShellPreferencesTests
{
    [TestMethod]
    public void PartialCatalogDoesNotEraseUndiscoveredPreferences()
    {
        var saved = new ShellPreferences(["a", "b", "c"], "c", true);
        var partial = saved.Reconcile(["b", "d"], false);
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, partial.Order.ToArray());
        Assert.AreEqual("c", partial.LastWidget);
        var complete = partial.Reconcile(["b", "d"], true);
        CollectionAssert.AreEqual(new[] { "b", "d" }, complete.Order.ToArray());
        Assert.IsNull(complete.LastWidget);
        Assert.IsFalse(complete.ReopenWidget);
    }

    [TestMethod]
    public void ReorderingRetainsIdentityAndDoesNotWrapAtEdges()
    {
        var saved = new ShellPreferences(["a", "b", "c"], "b", true);
        Assert.AreSame(saved, saved.Move("a", -1));
        Assert.AreSame(saved, saved.Move("c", 1));
        var moved = saved.Move("b", 1);
        CollectionAssert.AreEqual(new[] { "a", "c", "b" }, moved.Order.ToArray());
        Assert.AreEqual("b", moved.LastWidget);
    }

    [TestMethod]
    public void SavedCapacityDoesNotHideNewlyDiscoveredWidgets()
    {
        var saved = new ShellPreferences(Enumerable.Range(0, 256).Select(index => "old-" + index).ToArray(), "old-0", true);
        var partial = saved.Reconcile(["new", "old-10"], false);
        Assert.AreEqual(256, partial.Order.Count);
        CollectionAssert.AreEqual(new[] { "old-10", "new" }, partial.AvailableOrder(["new", "old-10"]).ToArray());
    }

    [TestMethod]
    public async Task RoundTripUsesLatestRequestAndIsolatesProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-shell-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ShellPreferencesStore(root);
            var first = new ShellPreferences(["a", "b"], "a", true);
            var last = new ShellPreferences(["b", "a"], "b", true);
            await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(last));
            var loaded = await store.LoadAsync();
            CollectionAssert.AreEqual(last.Order.ToArray(), loaded.Order.ToArray());
            Assert.AreEqual(last.LastWidget, loaded.LastWidget);
            Assert.IsTrue(loaded.ReopenWidget);
            Assert.AreEqual(ShellPreferences.Empty, await new ShellPreferencesStore(Path.Combine(root, "other")).LoadAsync());
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task ImportsOnlyBoundedValidLegacyStateWithoutRewritingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-shell-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var legacy = Path.Combine(root, "overlay-state.ini");
            const string original = "v2 2 widget.one widget.two widget.two 1\n";
            await File.WriteAllTextAsync(legacy, original);
            var store = new ShellPreferencesStore(root);
            var loaded = await store.LoadAsync();
            Assert.AreEqual("widget.two", loaded.LastWidget);
            await store.SaveAsync(loaded);
            Assert.AreEqual(original, await File.ReadAllTextAsync(legacy));
            await File.WriteAllTextAsync(Path.Combine(root, "winui-shell-state.json"), "{\"Order\":[\"bad/id\"],\"ReopenWidget\":false}");
            Assert.AreEqual(ShellPreferences.Empty, await store.LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
