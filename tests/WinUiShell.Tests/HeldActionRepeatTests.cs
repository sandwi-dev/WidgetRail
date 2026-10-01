using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WinUiShell.Tests;

[TestClass]
public sealed class HeldActionRepeatTests
{
    [TestMethod]
    public void NativeCadenceAndMissedFramesNeverBurst()
    {
        var repeat = new HeldActionRepeat();
        repeat.Begin("seek", 1000);
        Assert.IsFalse(repeat.Tick("seek", true, true, 1359));
        Assert.IsTrue(repeat.Tick("seek", true, true, 1360));
        Assert.IsFalse(repeat.Tick("seek", true, true, 1484));
        Assert.IsTrue(repeat.Tick("seek", true, true, 1485));
        Assert.IsTrue(repeat.Tick("seek", true, true, 10000));
        Assert.IsFalse(repeat.Tick("seek", true, true, 10000));
    }

    [TestMethod]
    public void BusyOwnerWaitsButReleaseAndChangedOwnerRetirePermanently()
    {
        var repeat = new HeldActionRepeat();
        repeat.Begin(("widget", "action"), 0);
        Assert.IsFalse(repeat.Tick(("widget", "action"), true, false, 500));
        Assert.IsTrue(repeat.Tick(("widget", "action"), true, true, 1000));
        Assert.IsFalse(repeat.Tick(("widget", "action"), false, true, 2000));
        Assert.IsFalse(repeat.Tick(("widget", "action"), true, true, 3000));
        repeat.Begin(("widget", "action"), 0);
        Assert.IsFalse(repeat.Tick(("widget", "other action"), true, true, 500));
        Assert.IsFalse(repeat.Tick(("widget", "action"), true, true, 1000));
        repeat.Begin("seek", 0);
        repeat.Reset();
        Assert.IsFalse(repeat.Tick("seek", true, true, 1000));
    }
}
