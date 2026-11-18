using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class IndexedRasterizationPolicyTests
{
    [TestMethod]
    public void SmallZoomedCardsGetBoundedQualityWhileNormalZoomKeepsDefault()
    {
        Assert.AreEqual(1.5, IndexedRasterizationPolicy.Resolve(.9, 1.25, 240, 220, 1));
        Assert.AreEqual(1, IndexedRasterizationPolicy.Resolve(1, 1.25, 240, 220, 1));
        Assert.AreEqual(1, IndexedRasterizationPolicy.Resolve(.9, 2, 2000, 1200, 1));
    }

    [TestMethod]
    public void BoostNeverExceedsItsSizeBudgetOrOverridesNativeQuality()
    {
        foreach (var zoom in new[] { .6, .9, 1.25, 1.5, 2 })
        foreach (var dpi in new[] { 1, 1.25, 1.5, 2, 3 })
        foreach (var width in new[] { 100, 320, 800, 1600 })
        {
            var scale = IndexedRasterizationPolicy.Resolve(zoom, dpi, width, 180, 1);
            Assert.IsTrue(scale >= 1 && scale <= 1.5);
            if (scale > 1) Assert.IsTrue(width * 180 * Math.Pow(dpi * Math.Max(1, zoom) * scale, 2) <= IndexedRasterizationPolicy.PixelBudget + .001);
        }
        Assert.AreEqual(2, IndexedRasterizationPolicy.Resolve(.9, 1.25, 240, 220, 2));
        Assert.AreEqual(.75, IndexedRasterizationPolicy.Resolve(.9, 1.25, 240, 220, .75));
        Assert.AreEqual(1, IndexedRasterizationPolicy.Resolve(.9, 1.25, double.NaN, 220, 1));
        Assert.AreEqual(1, IndexedRasterizationPolicy.Resolve(.9, 1.25, 240, 0, 1));
    }
}
