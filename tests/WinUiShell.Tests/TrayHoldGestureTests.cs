using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class TrayHoldGestureTests
{
    [TestMethod]
    public void TapAndExactHoldBoundaryHaveOneOutcome()
    {
        var gesture = new TrayHoldGesture();
        gesture.Press("a", true, 10);
        Assert.AreEqual(TrayHoldAction.ToggleReorder, gesture.Release("a", true, 709));
        gesture.Press("a", true, 10);
        Assert.AreEqual(TrayHoldAction.Restart, gesture.Release("a", true, 710));
        Assert.AreEqual(TrayHoldAction.None, gesture.Release("a", true, 711));
    }

    [TestMethod]
    public void WinningHoldDoesNotReorderOnRelease()
    {
        var gesture = new TrayHoldGesture();
        gesture.Press("a", true, 10);
        Assert.AreEqual(TrayHoldAction.Restart, gesture.Tick("a", true, 711));
        Assert.AreEqual(TrayHoldAction.None, gesture.Tick("a", true, 1500));
        Assert.IsTrue(gesture.Capturing);
        Assert.AreEqual(TrayHoldAction.None, gesture.Release("a", true, 1600));
    }

    [TestMethod]
    public void OwnershipLossConsumesReleaseEvenIfTargetReturns()
    {
        var gesture = new TrayHoldGesture();
        gesture.Press(("a", 1), true, 10);
        Assert.AreEqual(TrayHoldAction.None, gesture.Tick(("a", 2), true, 200));
        gesture.Press(("a", 1), true, 210);
        Assert.IsTrue(gesture.Capturing);
        Assert.AreEqual(TrayHoldAction.None, gesture.Release(("a", 1), true, 300));
        gesture.Press("a", true, 500);
        gesture.Cancel();
        Assert.AreEqual(TrayHoldAction.None, gesture.Release("a", true, 900));
    }

    [TestMethod]
    public void ReorderModeRemainsTapOnlyAndResetAllowsReconnect()
    {
        var gesture = new TrayHoldGesture();
        gesture.Press("a", false, 0);
        Assert.AreEqual(TrayHoldAction.None, gesture.Tick("a", false, 5000));
        Assert.AreEqual(TrayHoldAction.ToggleReorder, gesture.Release("a", false, 6000));
        gesture.Press("a", true, 9000);
        gesture.Reset();
        gesture.Press("b", true, 10000);
        Assert.AreEqual(TrayHoldAction.ToggleReorder, gesture.Release("b", true, 10001));
    }
}
