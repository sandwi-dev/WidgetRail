using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.Tests.WinUiShell;

[TestClass]
public sealed class FocusPreviewDelayStateTests
{
    [TestMethod]
    [DataRow(2500)]
    [DataRow(5000)]
    public void ContentUpdatesDoNotStarveSettledSource(int delay)
    {
        var state = new FocusPreviewDelayState();
        const int start = 1000;
        var due = start + delay;
        Assert.IsTrue(state.Select(("owner", "first"), delay, start));
        Assert.AreEqual(delay, state.Remaining(start));
        for (var now = start + 1; now < due; now += 8)
        {
            Assert.IsFalse(state.Select(("owner", "first"), delay, now));
            Assert.IsFalse(state.Ready(now));
        }
        Assert.IsFalse(state.Ready(due - 1));
        Assert.IsTrue(state.Ready(due));
        Assert.AreEqual(0, state.Remaining(due));
    }
    [TestMethod]
    public void NewSourceAndLifetimeRevokePriorDeadline()
    {
        var state = new FocusPreviewDelayState();
        state.Select("first", 350, 1000);
        Assert.IsTrue(state.Select("second", 350, 1349));
        Assert.IsFalse(state.Ready(1350));
        Assert.AreEqual(349, state.Remaining(1350));
        state.Clear();
        Assert.IsFalse(state.Ready(2000));
        state.Select("second", 350, 2000);
        Assert.IsFalse(state.Ready(2001));
        Assert.IsTrue(state.Ready(2350));
    }
    [TestMethod]
    public void ZeroDelayAndTimingOptionChangesAreDeterministic()
    {
        var state = new FocusPreviewDelayState();
        state.Select("first", 0, 10);
        Assert.IsTrue(state.Ready(10));
        Assert.IsTrue(state.Select("first", 500, 11));
        Assert.IsFalse(state.Ready(500));
        Assert.IsTrue(state.Ready(511));
        state.Select(null, 0, 600);
        Assert.IsFalse(state.Ready(900));
    }
}
