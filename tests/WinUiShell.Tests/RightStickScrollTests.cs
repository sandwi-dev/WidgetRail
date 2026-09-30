using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class RightStickScrollTests
{
    [TestMethod]
    public void FocusSettlesAfterNeutralDelayAndCanWaitForReadyRows()
    {
        var scroll = new RightStickScroll();
        scroll.Sample(0, 0, 100);
        Assert.IsFalse(scroll.ShouldSettle(1000));
        scroll.Sample(0, short.MinValue, 1100);
        scroll.Sample(0, 0, 1200);
        Assert.IsFalse(scroll.ShouldSettle(1319));
        Assert.IsTrue(scroll.ShouldSettle(1320));
        scroll.Sample(0, 0, 1400);
        Assert.IsTrue(scroll.ShouldSettle(1400));
        scroll.Reset();
        Assert.IsFalse(scroll.ShouldSettle(2000));
    }

    [TestMethod]
    public void ResumingScrollRestartsTheSettleDelayWithoutClockBacklog()
    {
        var scroll = new RightStickScroll();
        scroll.Sample(0, short.MinValue, 100);
        scroll.Sample(0, 0, 200);
        Assert.AreEqual((0d, -35.2d), scroll.Sample(0, short.MaxValue, 300));
        Assert.IsFalse(scroll.ShouldSettle(400));
        scroll.Sample(0, 0, 450);
        Assert.IsFalse(scroll.ShouldSettle(569));
        Assert.IsTrue(scroll.ShouldSettle(570));
    }

    [TestMethod]
    public void NeutralDoesNotMoveAndInitialRateMatchesNativePolicy()
    {
        var scroll = new RightStickScroll();
        Assert.AreEqual((0d, 0d), scroll.Sample(8000, -8000, 100));
        Assert.AreEqual((0d, -35.2d), scroll.Sample(0, short.MaxValue, 200));
    }

    [TestMethod]
    public void HitchesAreBoundedAndNoCatchupReplaysAfterward()
    {
        var scroll = new RightStickScroll();
        scroll.Sample(0, short.MaxValue, 100);
        Assert.AreEqual((0d, -110d), scroll.Sample(0, short.MaxValue, 1000));
        Assert.AreEqual((0d, 0d), scroll.Sample(0, short.MaxValue, 1000));
        Assert.AreEqual((0d, -22d), scroll.Sample(0, short.MaxValue, 1010));
    }

    [TestMethod]
    public void AxisHysteresisPreventsDiagonalJitterAndDirectionReversesImmediately()
    {
        var scroll = new RightStickScroll();
        Assert.IsTrue(scroll.Sample(20000, 18000, 10).X > 0);
        Assert.AreEqual(0, scroll.Sample(20000, 22000, 20).Y);
        Assert.IsTrue(scroll.Sample(20000, 30000, 30).Y < 0);
        Assert.IsTrue(scroll.Sample(0, -30000, 40).Y > 0);
    }

    [TestMethod]
    public void ResetOnOwnerChangeDropsPriorClockAndAxis()
    {
        var scroll = new RightStickScroll();
        scroll.Sample(short.MaxValue, 0, 100);
        scroll.Reset();
        Assert.AreEqual((0d, -35.2d), scroll.Sample(0, short.MaxValue, 1000));
        Assert.AreEqual((-35.2d, 0d), new RightStickScroll().Sample(short.MinValue, 0, 100));
    }
}
