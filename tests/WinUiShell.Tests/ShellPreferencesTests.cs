using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ShellPreferencesTests
{
    [TestMethod]
    public async Task NewProfilesHaveFourEmptySlotsAndExplicitAssignmentsPersist()
    {
        var root = Directory.CreateTempSubdirectory("wrail-tray-favorites-").FullName;
        try
        {
            var store = new ShellPreferencesStore(root);
            var fresh = await store.LoadAsync();
            Assert.AreEqual(new TrayActionSlots(), fresh.TrayActions);
            var favorite = new TrayActionBinding("spotify", "play-pause", "publisher", Package: "widgetrail.samples.spotify", Label: "Play or pause");
            await store.SaveAsync(fresh with { TrayActions = fresh.TrayActions.Assign(TrayDirection.Up, null).Assign(TrayDirection.Right, favorite) });
            var restored = await new ShellPreferencesStore(root).LoadAsync();
            var path = Path.Combine(root, "winui-shell-state.json");
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            legacy["RadialDpadActions"] = false;
            await File.WriteAllTextAsync(path, legacy.ToJsonString());
            Assert.AreEqual(restored.TrayActions, (await store.LoadAsync()).TrayActions);
            Assert.IsNull(restored.TrayActions.Up);
            Assert.AreEqual(favorite, restored.Reconcile([], true).TrayActions.Right);
            await File.WriteAllTextAsync(Path.Combine(root,"winui-shell-state.json"), """{"Order":["spotify"],"ReopenWidget":false}""");
            var existing = await store.LoadAsync();
            Assert.IsNull(existing.TrayActions.Up);
            CollectionAssert.AreEqual(new[] { "spotify" }, existing.Order.ToArray());
        }
        finally { Directory.Delete(root, recursive:true); }
    }

    [TestMethod]
    public void AudioChoicesAreWidgetScopedAndSurviveCatalogReconciliation()
    {
        Assert.IsTrue(new MediaAudioPreference().Muted);
        var saved = ShellPreferences.Empty.RememberAudio("discord", new(false, .35));
        saved = saved.RememberAudio("capture", new(true, .8));
        var reconciled = saved.Reconcile(["discord", "capture"], true).Move("capture", -1);
        Assert.AreEqual(new MediaAudioPreference(false, .35), reconciled.MediaAudio["discord"]);
        Assert.AreEqual(new MediaAudioPreference(true, .8), reconciled.MediaAudio["capture"]);
        Assert.AreEqual(0, ShellPreferences.Empty.MediaAudio.Count);
        Assert.IsFalse(new MediaAudioPreference(Volume: double.NaN).IsValid);
        Assert.IsFalse(new MediaAudioPreference(Volume: 1.01).IsValid);
    }

    [TestMethod]
    public async Task AudioChoicesSurviveRestartAndRemainWithinTheStateFileBound()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-audio-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ids = Enumerable.Range(0, ShellPreferences.MaximumWidgets)
                .Select(index => new string('a', 120) + index.ToString("D8")).ToArray();
            var value = new ShellPreferences(ids, ids[0], true);
            foreach (var direction in Enum.GetValues<TrayDirection>())
                value = value with { TrayActions = value.TrayActions.Assign(direction, new(ids[0], ids[1], ids[2], ids[3], ids[4], new string('z',80))) };
            for (var i = 0; i < ShellPreferences.MaximumAudioWidgets; i++)
                value = value.RememberAudio(ids[i], new(i % 2 == 0, .1234567890123456));
            Assert.AreSame(value, value.RememberAudio("another.widget", new()));
            await new ShellPreferencesStore(root).SaveAsync(value);
            Assert.IsLessThanOrEqualTo(65536L, new FileInfo(Path.Combine(root, "winui-shell-state.json")).Length);
            var restored = await new ShellPreferencesStore(root).LoadAsync();
            Assert.AreEqual(ShellPreferences.MaximumAudioWidgets, restored.MediaAudio.Count);
            Assert.AreEqual(value.MediaAudio[ids[1]], restored.MediaAudio[ids[1]]);
            CollectionAssert.AreEqual(ids, restored.Order.ToArray());
            Assert.AreEqual(0, (await new ShellPreferencesStore(Path.Combine(root, "other-profile")).LoadAsync()).MediaAudio.Count);
            await File.WriteAllTextAsync(Path.Combine(root, "winui-shell-state.json"),
                "{\"Order\":[],\"ReopenWidget\":false,\"MediaAudio\":{\"discord\":{\"Muted\":false,\"Volume\":999}}}");
            Assert.AreEqual(ShellPreferences.Empty, await new ShellPreferencesStore(root).LoadAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
