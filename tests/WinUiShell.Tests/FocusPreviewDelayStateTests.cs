using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.Tests.WinUiShell;

[TestClass]
public sealed class FocusPreviewDelayStateTests
{
    [TestMethod]
    public void ContentUpdatesDoNotStarveSettledSource()
    {
        var state = new FocusPreviewDelayState();
        Assert.IsTrue(state.Select(("owner", "first"), 350, 1000));
        for (var now = 1001; now < 1350; now += 8)
        {
            Assert.IsFalse(state.Select(("owner", "first"), 350, now));
            Assert.IsFalse(state.Ready(now));
        }
        Assert.IsTrue(state.Ready(1350));
        Assert.AreEqual(0, state.Remaining(1400));
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
