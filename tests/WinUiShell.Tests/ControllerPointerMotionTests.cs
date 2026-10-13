using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ControllerPointerMotionTests
{
    [TestMethod]
    public void NewSurfaceAndFocusRecoveryDoNotReplayTimeAway()
    {
        var pointer = new ControllerPointerMotion();
        Assert.AreEqual((0d, 0d), pointer.Sample(32767, 32767, 100));
        Assert.IsTrue(pointer.Sample(32767, 32767, 116).X > 0);
        pointer.Reset();
        Assert.AreEqual((0d, 0d), pointer.Sample(32767, 32767, 90000));
    }

    [TestMethod]
    public void HitchesCannotThrowThePointerAcrossTheDialog()
    {
        var pointer = new ControllerPointerMotion();
        pointer.Sample(32767, 0, 100);
        var hitch = pointer.Sample(32767, 0, 10000);
        Assert.IsTrue(hitch.X > 0 && hitch.X <= .03);
        Assert.AreEqual((0d, 0d), pointer.Sample(32767, 0, 10000));
    }

    [TestMethod]
    public void DriftIsIgnoredAndUpMovesTowardTheTopOfTheScreen()
    {
        var pointer = new ControllerPointerMotion();
        pointer.Sample(0, 0, 100);
        Assert.AreEqual((0d, 0d), pointer.Sample(7848, -7848, 116));
        var movement = pointer.Sample(-32768, 32767, 132);
        Assert.IsTrue(movement.X < 0 && movement.Y < 0);
    }
}
