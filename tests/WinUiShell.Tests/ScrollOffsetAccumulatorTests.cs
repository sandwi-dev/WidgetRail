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
        state.Reset(); // ScrollBy resets the gesture's pending offsets on reversal.
        Assert.AreEqual(450, state.Propose(500, -50, 1000, .5));
        state.Accepted(450);
        Assert.AreEqual(350, state.Propose(500, -100, 1000, .5));
        state.Accepted(350);
        state.Observe(350, 1000, .5);
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

    [TestMethod]
    [DataRow(1045.2, 436.68, -18, 418.68)]
    [DataRow(400.0, 900.0, 18, 918.0)]
    public void NativeAnchorMovementCannotMakeHeldInputRequestTheOppositeDirection(
        double pending, double anchoredCurrent, double input, double expected)
    {
        var state = new ScrollOffsetAccumulator();
        state.Accepted(pending);
        // Native item measurement or insert/remove can change the coordinate
        // origin while a ChangeView destination is still pending. This need not
        // coincide with a new query or change in maximum extent.
        var next = state.Propose(anchoredCurrent, input, 5000, .5);
        Assert.AreEqual(expected, next, .001);
        state.Accepted(next);
        Assert.AreEqual(expected + input, state.Propose(anchoredCurrent, input, 5000, .5), .001);
    }

    [TestMethod]
    [DataRow(1000.0, -18.0, 1500.0, 1482.0)]
    [DataRow(1500.0, 18.0, 1000.0, 1018.0)]
    public void ChangedNativeExtentInvalidatesTheOldAbsoluteDestination(
        double before, double input, double anchoredCurrent, double expected)
    {
        var state = new ScrollOffsetAccumulator();
        state.Accepted(state.Propose(before, input, 2600, .5));
        // A retained item can move to a new coordinate as native row estimates
        // become real sizes. An obsolete destination can still lie in the
        // requested direction and skip many messages if only direction is checked.
        Assert.AreEqual(expected, state.Propose(anchoredCurrent, input, 3000, .5), .001);
    }
}
