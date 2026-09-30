using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WinUiShell.Tests;

[TestClass]
public sealed class SliderValueStateTests
{
    [TestMethod]
    public void BusyAcknowledgementCannotDiscardNewerFractionalIntent()
    {
        var state = new SliderValueState(.8, 1);
        state.Request(.85, 0); state.TakeDispatch(150);
        state.Request(.9, 180);
        state.Observe(.8, 2, true, false, 200);
        Assert.AreEqual(.9, state.Value, 1e-9);
        Assert.IsNull(state.TakeDispatch(500, true));
        Assert.IsTrue(state.Request(.95, 510));
        state.Observe(.85, 3, false, false, 600);
        Assert.AreEqual(.95, state.Value, 1e-9);
        Assert.AreEqual(.95, state.TakeDispatch(660)!.Value.Value, 1e-9);
    }

    [TestMethod]
    public void RapidStepsSettleToLastValueAndDuplicateRequestDoesNotDelay()
    {
        var state = new SliderValueState(10, 1);
        Assert.IsTrue(state.Request(11, 0));
        Assert.IsTrue(state.Request(12, 100));
        Assert.IsTrue(state.Request(13, 200));
        Assert.AreEqual(13d, state.Value);
        Assert.IsFalse(state.Request(13, 300));
        Assert.IsNull(state.TakeDispatch(349));
        Assert.AreEqual(13d, state.TakeDispatch(350)!.Value.Value);
        Assert.IsNull(state.TakeDispatch(351));
    }

    [TestMethod]
    public void DoneForcesOneLatestDispatchAndDoesNotRollbackOnOldEcho()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0);
        state.TakeDispatch(150);
        state.Request(14, 180);
        var final = state.TakeDispatch(190, force: true)!.Value;
        Assert.AreEqual(14d, final.Value);
        Assert.IsNull(state.TakeDispatch(191, force: true));
        state.Observe(11, 2, false, false, 200);
        Assert.AreEqual(14d, state.Value);
        state.Observe(14, 3, false, false, 210);
        Assert.AreEqual(14d, state.Value);
        Assert.IsFalse(state.IsCurrent(final.Generation));
    }

    [TestMethod]
    public void ReverseBeforeFirstDispatchCancelsWithoutSending()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0);
        state.Request(10, 100);
        Assert.AreEqual(10d, state.Value);
        Assert.IsFalse(state.HasPending);
        Assert.IsNull(state.TakeDispatch(1000, true));
    }

    [TestMethod]
    public void ReverseAfterSentValueStillDispatchesCompensation()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0); state.TakeDispatch(150);
        state.Request(12, 170); state.Request(10, 180);
        Assert.AreEqual(10d, state.TakeDispatch(330)!.Value.Value);
        state.Observe(11, 2, false, false, 340);
        Assert.AreEqual(10d, state.Value);
    }

    [TestMethod]
    public void PriorEchoAfterLatestAcknowledgementIsGuardedWithoutReplay()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0); state.TakeDispatch(150);
        state.Request(12, 160); state.TakeDispatch(310);
        state.Observe(12, 2, false, false, 350);
        state.Observe(11, 3, false, false, 400);
        Assert.AreEqual(12d, state.Value);
        Assert.IsTrue(state.HasPending);
        Assert.IsNull(state.TakeDispatch(500, true));
        state.Observe(12, 4, false, false, 550);
        Assert.AreEqual(12d, state.Value);
        state.TakeDispatch(2310);
        Assert.IsFalse(state.HasPending);
    }

    [TestMethod]
    public void GuardTimeoutConvergesToLastObservedValue()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0); state.TakeDispatch(150);
        state.Request(12, 160); state.TakeDispatch(310);
        state.Observe(12, 2, false, false, 350);
        state.Observe(11, 3, false, false, 400);
        state.TakeDispatch(2149); Assert.AreEqual(12d, state.Value);
        state.TakeDispatch(2150); Assert.AreEqual(11d, state.Value);
        state.TakeDispatch(2310); Assert.IsFalse(state.HasPending);
    }

    [TestMethod]
    public void DispatchWithoutAcknowledgementExpiresAtTwoSeconds()
    {
        var state = new SliderValueState(10, 1);
        state.Request(15, 0); state.TakeDispatch(150);
        state.Observe(10, 2, false, false, 2149);
        Assert.AreEqual(15d, state.Value);
        state.TakeDispatch(2150);
        Assert.AreEqual(10d, state.Value);
        Assert.IsFalse(state.HasPending);
    }

    [TestMethod]
    public void ExternalChangedValueOverridesDispatchedAndGuardedTargets()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0); state.TakeDispatch(150);
        state.Observe(30, 2, false, false, 170);
        Assert.AreEqual(30d, state.Value);
        Assert.IsFalse(state.HasPending);
        state.Request(31, 200); state.TakeDispatch(350);
        state.Request(32, 370); state.TakeDispatch(520);
        state.Observe(32, 3, false, false, 600);
        state.Observe(31, 4, false, false, 650);
        Assert.AreEqual(32d, state.Value);
        state.Observe(50, 5, false, false, 700);
        Assert.AreEqual(50d, state.Value);
        Assert.IsFalse(state.HasPending);
    }

    [TestMethod]
    public void SettlingPreservesUnsentUserTargetWhileProviderMoves()
    {
        var state = new SliderValueState(10, 1);
        state.Request(20, 0);
        state.Observe(11, 2, false, false, 50);
        Assert.AreEqual(20d, state.Value);
        Assert.AreEqual(20d, state.TakeDispatch(150)!.Value.Value);
    }

    [TestMethod]
    [DataRow(false, true)]
    public void UnavailableControlCancelsUnsentValueAndCannotDispatch(bool busy, bool disabled)
    {
        var state = new SliderValueState(10, 1);
        state.Request(15, 0);
        state.Observe(10, 2, busy, disabled, 25);
        Assert.AreEqual(10d, state.Value);
        Assert.IsNull(state.TakeDispatch(150, true));
        Assert.IsFalse(state.Request(17, 170));
        state.Observe(10, 3, false, false, 200);
        Assert.IsTrue(state.Request(17, 210));
    }

    [TestMethod]
    public void BusyAfterDispatchDoesNotUndoValueAwaitingAcknowledgement()
    {
        var state = new SliderValueState(10, 1);
        state.Request(15, 0); state.TakeDispatch(150);
        state.Observe(10, 2, true, false, 170);
        Assert.AreEqual(15d, state.Value);
        Assert.IsTrue(state.Request(20, 200));
        state.Observe(15, 3, false, false, 250);
        Assert.AreEqual(20d, state.Value);
        Assert.AreEqual(20d, state.TakeDispatch(350)!.Value.Value);
    }

    [TestMethod]
    public void OldRejectionCannotCancelNewerSettlingOrDispatchedIntent()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0); var first = state.TakeDispatch(150)!.Value;
        state.Request(12, 160);
        Assert.IsFalse(state.IsCurrent(first.Generation));
        Assert.IsTrue(state.Reject(first.Generation));
        Assert.AreEqual(12d, state.Value);
        var second = state.TakeDispatch(310)!.Value;
        Assert.IsFalse(state.Reject(first.Generation));
        Assert.AreEqual(12d, state.Value);
        Assert.IsTrue(state.Reject(second.Generation));
        Assert.AreEqual(10d, state.Value);
    }

    [TestMethod]
    public void ResetDoesNotReuseAsyncDispatchGeneration()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0); var first = state.TakeDispatch(150)!.Value;
        state.Reset();
        Assert.AreEqual(10d, state.Value);
        Assert.IsFalse(state.HasPending);
        state.Request(12, 200); var second = state.TakeDispatch(350)!.Value;
        Assert.IsTrue(second.Generation > first.Generation);
        Assert.IsFalse(state.Reject(first.Generation));
        Assert.AreEqual(12d, state.Value);
    }

    [TestMethod]
    public void OldOrDuplicateSnapshotsCannotOverwriteCurrentObservation()
    {
        var state = new SliderValueState(10, 10);
        state.Observe(99, 9, true, true, 0);
        state.Observe(88, 10, false, false, 1);
        Assert.AreEqual(10d, state.Value);
        Assert.IsTrue(state.Request(20, 2));
    }

    [TestMethod]
    public void HistoryIsBoundedAndToleranceUsesAuthoredRange()
    {
        var state = new SliderValueState(0, 1, toleranceScale: 100);
        Assert.IsFalse(state.Request(1e-8, 0));
        for (int i = 1; i <= 30; i++)
        {
            state.Request(i, i); state.TakeDispatch(i, true);
            Assert.IsTrue(state.SentHistoryCount <= SliderValueState.MaximumSentHistory);
        }
        Assert.AreEqual(16, state.SentHistoryCount);
        state.Observe(30 + 1e-8, 2, false, false, 40);
        Assert.AreEqual(30 + 1e-8, state.Value);
        state.TakeDispatch(2030);
        Assert.AreEqual(0, state.SentHistoryCount);
        Assert.IsFalse(state.HasPending);
    }

    [TestMethod]
    public void InvalidRequestsDoNotReplacePendingFiniteTarget()
    {
        var state = new SliderValueState(10, 1);
        state.Request(11, 0);
        Assert.IsFalse(state.Request(double.NaN, 10));
        Assert.IsFalse(state.Request(double.PositiveInfinity, 20));
        state.Observe(double.NaN, 2, false, false, 30);
        Assert.AreEqual(11d, state.TakeDispatch(150)!.Value.Value);
    }
}
