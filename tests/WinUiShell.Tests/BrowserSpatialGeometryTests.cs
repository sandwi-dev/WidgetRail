using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class BrowserSpatialGeometryTests
{
    private static SpatialCandidate At(int id, double x, double y, bool source = false, double width = 40, double height = 30) => new(0, id, new(x, y, width, height), new(x + width / 2, y + height / 2), source);
    [TestMethod]
    public void GridFollowsRowsAndColumns()
    {
        var items = new[] { At(0, 100, 100, true), At(1, 200, 100), At(2, 100, 200), At(3, 0, 100), At(4, 100, 0), At(5, 170, 170) };
        foreach (var pair in new[] { (SpatialDirection.Right, 1), (SpatialDirection.Down, 2), (SpatialDirection.Left, 3), (SpatialDirection.Up, 4) })
            Assert.AreEqual(pair.Item2, BrowserSpatialGeometry.Choose(items, items[0].Rect, pair.Item1)?.Index);
    }
    [TestMethod]
    public void NearbyStaggeredControlBeatsVeryDistantAlignedControl()
    {
        var source = At(0, 0, 0, true);
        Assert.AreEqual(1, BrowserSpatialGeometry.Choose([source, At(1, 80, 32), At(2, 900, 0)], source.Rect, SpatialDirection.Right)?.Index);
    }
    [TestMethod]
    public void DisabledDirectionAndSourceCannotBeSelected()
    {
        var source = At(0, 100, 100, true);
        Assert.IsNull(BrowserSpatialGeometry.Choose([source, At(1, 0, 100), At(2, double.NaN, 0)], source.Rect, SpatialDirection.Right));
    }
    [TestMethod]
    public void StableTiesAndPartiallyOverlappingRects()
    {
        var source = At(0, 100, 100, true);
        Assert.AreEqual(1, BrowserSpatialGeometry.Choose([At(1, 110, 120), At(2, 110, 120)], source.Rect, SpatialDirection.Down)?.Index);
    }
    [TestMethod]
    public void WideOverlappingTextFieldDoesNotBeatAnAdjacentButton()
    {
        var source = At(0, 15, 15, true, 70, 40);
        Assert.AreEqual(1, BrowserSpatialGeometry.Choose([source, At(1, 95, 15, false, 70, 40), At(2, 8, 34, false, 800, 26)], source.Rect, SpatialDirection.Right)?.Index);
    }
    [TestMethod]
    public void LandingMustAdvanceFromThePointerNotJustTheSourceCenter()
    {
        var source = At(0, 40, 40, true, 300, 40);
        Assert.AreEqual(2, BrowserSpatialGeometry.Choose([source, At(1, 250, 100), At(2, 450, 100)],
            source.Rect, SpatialDirection.Right, new(320, 60))?.Index);
        Assert.IsFalse(BrowserSpatialGeometry.Advances(new(10, 10), new(10, 20), SpatialDirection.Right));
        Assert.IsTrue(BrowserSpatialGeometry.Advances(new(10, 10), new(0, 20), SpatialDirection.Left));
    }
    [TestMethod]
    public void FreePointerAndScrolledOriginCanBeOutsideControls()
    {
        Assert.AreEqual(1, BrowserSpatialGeometry.Choose([At(1, 100, 10), At(2, 100, 80)], new(120, -10, .01, .01), SpatialDirection.Down)?.Index);
        Assert.AreEqual(1, BrowserSpatialGeometry.Choose([At(1, 100, 100), At(2, 0, 100)], new(70, 115, .01, .01), SpatialDirection.Right)?.Index);
    }
}
