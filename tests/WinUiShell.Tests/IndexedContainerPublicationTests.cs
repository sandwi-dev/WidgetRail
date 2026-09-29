using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Collections;

namespace WinUiShell.Tests;

[TestClass]
public sealed class IndexedContainerPublicationTests
{
    [TestMethod]
    public void ContainerPolicySeesOneCommittedRowAfterNativeBindingsHaveUpdated()
    {
        var slot = new IndexedItem<string>(new object(), 7);
        var bound = new Dictionary<string, object?>();
        var publications = 0;
        slot.PropertyChanged += (_, args) => bound[args.PropertyName!] = slot.Content;
        slot.StateChanged += (_, _) =>
        {
            ++publications;
            Assert.AreEqual("game.7", slot.Key);
            Assert.AreEqual("row", slot.Value);
            Assert.IsTrue(slot.HasValue);
            Assert.IsFalse(slot.Failed);
            Assert.AreEqual(slot.Value, bound[nameof(slot.Content)]);
        };
        slot.SetValue("game.7", "row");
        slot.SetValue("game.7", "row");
        Assert.AreEqual(1, publications, "Identical admission must not trigger more guide/layout work.");
    }

    [TestMethod]
    public void FailureAndRecoveryEachNotifyContainerAndRetirementDetachesIt()
    {
        var slot = new IndexedItem<string>(new object(), 7);
        var states = new List<bool>();
        EventHandler listener = (_, _) => states.Add(slot.Failed);
        slot.StateChanged += listener;
        slot.SetFailed();
        slot.SetValue("game.7", "row");
        slot.StateChanged -= listener;
        slot.SetValue("game.8", "replacement");
        CollectionAssert.AreEqual(new[] { true, false }, states);
    }
}
