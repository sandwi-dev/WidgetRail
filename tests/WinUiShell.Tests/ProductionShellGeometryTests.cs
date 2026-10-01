using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ProductionShellGeometryTests
{
    [TestMethod]
    public void GuideUsesBalancedCompactGapsWithoutShrinkingItsTextSlot()
    {
        var bands = ProductionShellGeometry.Bands(900);
        Assert.AreEqual(8, bands.ContentGap);
        Assert.AreEqual(8, bands.GuideGap);
        Assert.AreEqual(58, bands.GuideHeight);
        Assert.AreEqual(76, bands.RailHeight);
    }

    [TestMethod]
    public void TileCapacityIsBoundedAndStatusCannotConsumeIcons()
    {
        foreach (var width in new[] { 1d, 64, 180, 320, 640, 900, 1920, 3840 })
        foreach (var count in new[] { 0, 1, 5, 9, 15, 100, 256 })
        foreach (var centered in new[] { true, false })
        {
            var layout = ProductionShellGeometry.Rail(width, count, centered);
            Assert.IsTrue(layout.TileSize > 0 && layout.TileSize <= 64);
            Assert.IsTrue(layout.VisibleCount <= count && layout.VisibleCount >= 0);
            Assert.IsTrue(layout.GuideWidth > 0 && layout.GuideWidth <= width);
            Assert.IsTrue(layout.ViewportWidth + (layout.Overflow ? 84 : 0) <= width);
            if (layout.StatusWidth > 0)
                Assert.IsTrue(layout.ViewportWidth + (layout.Overflow ? 84 : 0) + layout.StatusWidth * (centered ? 2 : 1) + 28 <= width);
        }
    }

    [TestMethod]
    public void CatalogShrinksTilesBeforeIntroducingOverflow()
    {
        var fitting = ProductionShellGeometry.Rail(1200, 10);
        Assert.IsFalse(fitting.Overflow);
        Assert.AreEqual(58, fitting.TileSize);
        var overflowing = ProductionShellGeometry.Rail(1200, 30);
        Assert.IsTrue(overflowing.Overflow);
        Assert.AreEqual(64, overflowing.TileSize);
        Assert.AreEqual(8, overflowing.VisibleCount);
    }

    [TestMethod]
    public void ShortViewportsCollapseEmptyBandsWithoutNegativeOrOverflowingRegions()
    {
        foreach (var height in new[] { 0d, 1, 64, 140, 183, 400, 1080 })
        {
            var bands = ProductionShellGeometry.Bands(height);
            Assert.IsTrue(bands.ContentHeight >= 0 && bands.ContentGap >= 0 && bands.GuideHeight >= 0 && bands.GuideGap >= 0 && bands.RailHeight >= 0);
            Assert.AreEqual(height, bands.ContentHeight + bands.ContentGap + bands.GuideHeight + bands.GuideGap + bands.RailHeight, .001);
        }
    }
}
