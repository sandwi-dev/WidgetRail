using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ScrollOffsetAccumulatorTests
{
    [TestMethod]
    public void QueuedInputAccumulatesWithoutRetreatingToStaleNativeOffsets()
    {
        var state = new ScrollOffsetAccumulator();
        state.Accepted(state.Propose(0, 110, 1000, .5));
        Assert.AreEqual(112.2, state.Propose(0, 2.2, 1000, .5), .001);
        state.Accepted(112.2);
        Assert.AreEqual(114.4, state.Propose(110, 2.2, 1000, .5), .001);
        state.Accepted(114.4);
        state.Observe(114.4, 1000, .5);
        Assert.IsFalse(state.IsPending);
        Assert.AreEqual(124.4, state.Propose(114.4, 10, 1000, .5), .001);
    }

    [TestMethod]
    public void ReversalReplacesPendingDestinationWithoutReplayingOldMovement()
    {
        var state = new ScrollOffsetAccumulator();
        state.Accepted(state.Propose(500, 100, 1000, .5));
        Assert.AreEqual(550, state.Propose(500, -50, 1000, .5));
        state.Accepted(550);
        Assert.AreEqual(450, state.Propose(500, -100, 1000, .5));
        state.Accepted(450);
        state.Observe(450, 1000, .5);
        Assert.IsFalse(state.IsPending);
    }

    [TestMethod]
    public void BoundsAndNativeRoundingDoNotKeepSettlementPending()
    {
        var state = new ScrollOffsetAccumulator();
        state.Accepted(state.Propose(980, 100, 1000, .8));
        Assert.AreEqual(1000, state.Propose(980, 100, 1000, .8));
        state.Observe(600, 600, .8);
        Assert.IsFalse(state.IsPending);
        state.Accepted(300.3);
        state.Observe(300, 600, .8);
        Assert.IsFalse(state.IsPending);
        Assert.AreEqual(0, state.Propose(300, -500, 600, .8));
    }

    [TestMethod]
    public void UnacceptedRequestAndExternalMovementDoNotLeaveStaleTargets()
    {
        var state = new ScrollOffsetAccumulator();
        Assert.AreEqual(150, state.Propose(100, 50, 1000, .5));
        Assert.IsFalse(state.IsPending);
        state.Accepted(150);
        state.Reset();
        Assert.AreEqual(410, state.Propose(400, 10, 1000, .5));
    }
}
