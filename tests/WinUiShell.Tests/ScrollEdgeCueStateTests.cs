using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class ScrollEdgeCueStateTests
{
    [TestMethod]
    public void BoundariesAndMiddleReflectHiddenContent()
    {
        Assert.AreEqual(new ScrollEdgeCueState(false, true), ScrollEdgeCueState.From(0, 900, 300, true));
        Assert.AreEqual(new ScrollEdgeCueState(true, true), ScrollEdgeCueState.From(200, 900, 300, true));
        Assert.AreEqual(new ScrollEdgeCueState(true, false), ScrollEdgeCueState.From(600, 900, 300, true));
        Assert.AreEqual(default, ScrollEdgeCueState.From(0, 300, 300, true));
    }
    [TestMethod]
    public void FractionalBoundariesAndOverscrollAreStable()
    {
        Assert.AreEqual(new ScrollEdgeCueState(false, true), ScrollEdgeCueState.From(.4, 900, 300, true));
        Assert.AreEqual(new ScrollEdgeCueState(true, false), ScrollEdgeCueState.From(599.6, 900, 300, true));
        Assert.AreEqual(new ScrollEdgeCueState(false, true), ScrollEdgeCueState.From(-10, 900, 300, true));
        Assert.AreEqual(new ScrollEdgeCueState(true, false), ScrollEdgeCueState.From(700, 900, 300, true));
        Assert.AreEqual(default, ScrollEdgeCueState.From(0, 300.4, 300, true));
    }
    [TestMethod]
    public void InvalidUnmeasuredAndHorizontalGeometryHasNoCues()
    {
        Assert.AreEqual(default, ScrollEdgeCueState.From(20, 900, 300, false));
        Assert.AreEqual(default, ScrollEdgeCueState.From(20, 900, 0, true));
        Assert.AreEqual(default, ScrollEdgeCueState.From(double.NaN, 900, 300, true));
        Assert.AreEqual(default, ScrollEdgeCueState.From(20, double.PositiveInfinity, 300, true));
        Assert.AreEqual(default, ScrollEdgeCueState.From(20, 900, double.NaN, true));
    }
}
