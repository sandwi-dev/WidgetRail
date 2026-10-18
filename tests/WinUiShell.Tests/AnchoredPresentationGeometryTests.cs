using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class AnchoredPresentationGeometryTests
{
    [TestMethod]
    public void FlipsAtEdgesAndKeepsVisibleAnchorClear()
    {
        var right = AnchoredPresentationGeometry.Place(1000, 800, new(100, 200, 80, 40), 360, 280);
        Assert.AreEqual(188d, right.X);
        var left = AnchoredPresentationGeometry.Place(1000, 800, new(900, 200, 80, 40), 360, 280);
        Assert.AreEqual(532d, left.X);
        var below = AnchoredPresentationGeometry.Place(1000, 800, new(200, 100, 80, 40), 360, 280, AnchorPlacement.Below);
        Assert.AreEqual(148d, below.Y);
    }

    [TestMethod]
    public void MissingOffscreenAndOversizedAnchorsUseBoundedCenter()
    {
        var expected = new AnchorRectangle(320, 260, 360, 280);
        foreach (var anchor in new AnchorRectangle?[] { null, new(-500, -500, 10, 10), new(double.NaN, 0, 30, 30), new(0, 0, 1000, 800) })
            Assert.AreEqual(expected, AnchoredPresentationGeometry.Place(1000, 800, anchor, 360, 280));
        Assert.AreEqual(new AnchorRectangle(16, 16, 168, 88), AnchoredPresentationGeometry.Place(200, 120, null, 960, 800));
    }

    [TestMethod]
    public void FractionalSizesAndAllPlacementsStayInsideWidget()
    {
        foreach (var width in new[] { 0d, 20, 400.4, 800.8, 1320 })
        foreach (var height in new[] { 0d, 20, 160.2, 720.6 })
        foreach (var side in Enum.GetValues<AnchorPlacement>())
        {
            var placed = AnchoredPresentationGeometry.Place(width, height, new(width - 40, height - 40, 80, 80), 360, 280, side);
            Assert.IsTrue(double.IsFinite(placed.X) && double.IsFinite(placed.Y));
            Assert.IsTrue(placed.X >= 0 && placed.Y >= 0 && placed.Right <= width && placed.Bottom <= height);
        }
    }
}
