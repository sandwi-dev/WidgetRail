using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WinUiShell.Tests;

[TestClass]
public sealed class NativeDialogLifetimeTests
{
    [TestMethod]
    public void MissingOrUninspectableChooserFailsWithoutPretendingItClosed()
    {
        foreach (bool? result in new bool?[] { false, null })
        {
            var state = new NativeDialogLifetime(); state.Begin(100);
            state.Observe(result, 10100);
            Assert.AreEqual(NativeDialogPhase.Failed, state.Phase);
            Assert.IsFalse(state.HasOpened);
            state.Observe(true, 10200);
            Assert.AreEqual(NativeDialogPhase.Open, state.Phase);
        }
    }
    [TestMethod]
    public void TransientMissingAccessibilityNodeAndErrorsDoNotDismissTheModal()
    {
        var state = new NativeDialogLifetime(); state.Begin(100); state.Observe(true, 200);
        state.Observe(false, 300); state.Observe(null, 400); state.Observe(false, 500);
        Assert.AreEqual(NativeDialogPhase.Open, state.Phase);
        state.Observe(true, 600); state.Observe(false, 700); state.Observe(false, 800);
        Assert.AreEqual(NativeDialogPhase.Completing, state.Phase);
    }
    [TestMethod]
    public void NextRequestDoesNotInheritPriorDismissalEvidence()
    {
        var state = new NativeDialogLifetime(); state.Begin(100); state.Observe(true, 200);
        state.Observe(false, 300); state.Observe(false, 400); state.End(); state.Begin(500);
        state.Observe(false, 600); state.Observe(false, 700);
        Assert.AreEqual(NativeDialogPhase.Acquiring, state.Phase);
        state.Observe(true, 800); Assert.AreEqual(NativeDialogPhase.Open, state.Phase);
    }
    [TestMethod]
    public void ExplicitCancelStillRequiresConfirmedAbsence()
    {
        var state = new NativeDialogLifetime(); state.Begin(100); state.RequestCancellation();
        state.Observe(null, 200); Assert.AreEqual(NativeDialogPhase.Acquiring, state.Phase);
        state.Observe(false, 300); Assert.AreEqual(NativeDialogPhase.Acquiring, state.Phase);
        state.Observe(false, 400); Assert.AreEqual(NativeDialogPhase.Completing, state.Phase);
    }
    [TestMethod]
    public void RepeatedCancelDoesNotEraseConfirmedAbsenceOrFinishAStillOpenDialog()
    {
        var state = new NativeDialogLifetime(); state.Begin(0); state.Observe(true, 100);
        state.RequestCancellation(); state.Observe(true, 200);
        Assert.AreEqual(NativeDialogPhase.Open, state.Phase);
        Assert.IsTrue(state.CancellationRequested);
        state.Observe(false, 300); state.RequestCancellation(); state.Observe(false, 400);
        Assert.AreEqual(NativeDialogPhase.Completing, state.Phase);
    }
    [TestMethod]
    public void HostVisibilityTransitionDiscardsPreHideAbsenceButPreservesCancellation()
    {
        var state = new NativeDialogLifetime(); state.Begin(0); state.Observe(true, 100);
        state.RequestCancellation(); state.Observe(false, 200); state.Recheck(300);
        state.Observe(false, 400);
        Assert.AreEqual(NativeDialogPhase.Open, state.Phase);
        Assert.IsTrue(state.CancellationRequested);
        state.Observe(true, 500);
        Assert.AreEqual(NativeDialogPhase.Open, state.Phase);
    }
}
