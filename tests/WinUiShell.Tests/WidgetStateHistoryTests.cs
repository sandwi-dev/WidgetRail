using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class WidgetStateHistoryTests
{
    private static WidgetStateOwner Owner(string id) => new(id, id + ".instance", "runtime", "presentation", "package");

    [TestMethod]
    public void LightweightStateOutlivesNativeEvictionAndRepeatedReads()
    {
        var history = new WidgetStateHistory<string>();
        var owner = Owner("games");
        history.Remember(owner, "key:game70");
        Assert.IsTrue(history.TryGet(owner, out var first));
        Assert.IsTrue(history.TryGet(owner, out var second));
        Assert.AreEqual("key:game70", first); Assert.AreEqual(first, second);
        history.Remember(owner, "key:game90");
        Assert.IsTrue(history.TryGet(owner, out var latest)); Assert.AreEqual("key:game90", latest);
        Assert.AreEqual(1, history.Count);
    }

    [TestMethod]
    public void EveryOwnerBoundaryRejectsAndDropsPreviousState()
    {
        var owner = Owner("games");
        foreach (var replacement in new[] { owner with { InstanceId = "new" }, owner with { RuntimeGeneration = "new" },
            owner with { PresentationGeneration = "new" }, owner with { PackageContentDigest = "new" } })
        {
            var history = new WidgetStateHistory<string>(); history.Remember(owner, "old");
            Assert.IsFalse(history.TryGet(replacement, out _)); Assert.AreEqual(0, history.Count);
            Assert.IsFalse(history.TryGet(owner, out _));
        }
    }

    [TestMethod]
    public void NativeCacheIndependentBoundEvictsLeastRecentlyUsedMemory()
    {
        var history = new WidgetStateHistory<string>(2);
        history.Remember(Owner("one"), "1"); history.Remember(Owner("two"), "2");
        Assert.IsTrue(history.TryGet(Owner("one"), out _));
        history.Remember(Owner("three"), "3");
        Assert.AreEqual(2, history.Count);
        Assert.IsFalse(history.TryGet(Owner("two"), out _));
        Assert.IsTrue(history.TryGet(Owner("one"), out _));
        Assert.IsTrue(history.TryGet(Owner("three"), out _));
    }

    [TestMethod]
    public void CatalogRemovesKnownReplacementsButWaitsForCompleteRemovalEvidence()
    {
        var history = new WidgetStateHistory<string>();
        history.Remember(Owner("one"), "1"); history.Remember(Owner("two"), "2");
        history.Reconcile([Owner("one")], complete: false);
        Assert.AreEqual(2, history.Count);
        history.Reconcile([Owner("one") with { PackageContentDigest = "new" }], complete: false);
        Assert.IsFalse(history.TryGet(Owner("one"), out _)); Assert.AreEqual(1, history.Count);
        history.Reconcile([], complete: true); Assert.AreEqual(0, history.Count);
    }

    [TestMethod]
    public void WidgetIdsAreExactAndExplicitRemovalAndCloseRetireHistory()
    {
        var history = new WidgetStateHistory<string>();
        history.Remember(Owner("games"), "lower"); history.Remember(Owner("Games"), "upper");
        history.Remove("games"); Assert.IsTrue(history.TryGet(Owner("Games"), out var value)); Assert.AreEqual("upper", value);
        history.Clear(); Assert.AreEqual(0, history.Count);
    }

    [TestMethod]
    public void HistoryCannotExceedDeclaredMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetStateHistory<string>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetStateHistory<string>(257));
        var history = new WidgetStateHistory<string>();
        for (var index = 0; index < 300; ++index) history.Remember(Owner(index.ToString()), index.ToString());
        Assert.AreEqual(WidgetStateHistory<string>.MaximumEntries, history.Count);
        Assert.IsFalse(history.TryGet(Owner("0"), out _));
        Assert.IsTrue(history.TryGet(Owner("299"), out _));
    }
}
