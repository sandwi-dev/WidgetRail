using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WinUiShell.Tests;

[TestClass]
public sealed class DirectionalFocusPolicyTests
{
    [TestMethod]
    public void OffscreenTargetsRemainEligibleWithoutInventingViewportClipping()
    {
        var origin = new FocusRectangle(0, 50, 300, 44);
        Assert.AreEqual("shortcut", DirectionalFocusPolicy.Choose(origin, FocusDirection.Up,
            [new("shortcut", new(0, -70, 300, 44))]));
    }

    [TestMethod]
    public void HorizontalNavigationCannotEscapeDiagonallyOrThroughSubpixelOverlap()
    {
        var origin = new FocusRectangle(100, 100, 100, 100);
        foreach (var y in new[] { -1d, 0, .1, .5 })
            Assert.IsNull(DirectionalFocusPolicy.Choose(origin, FocusDirection.Left, [new("header", new(0, y, 80, 100))]));
        Assert.AreEqual("neighbor", DirectionalFocusPolicy.Choose(origin, FocusDirection.Left,
            [new("header", new(0, 0, 80, 90)), new("neighbor", new(-100, 100, 100, 100))]));
    }

    [TestMethod]
    public void VerticalBeamWinsBeforeDistanceAndTerminalPartialRowsStayReachable()
    {
        var origin = new FocusRectangle(110, 0, 100, 44);
        Assert.AreEqual("same-column", DirectionalFocusPolicy.Choose(origin, FocusDirection.Down,
            [new("outside-column", new(0, 45, 100, 44)), new("same-column", new(110, 100, 100, 44))]));
        Assert.AreEqual("partial-row", DirectionalFocusPolicy.Choose(origin, FocusDirection.Down,
            [new("partial-row", new(0, 52, 100, 44))]));
    }

    [TestMethod]
    public void TiesUseOrdinalIdentityAndInvalidGeometryCannotWin()
    {
        var origin = new FocusRectangle(0, 0, 44, 44);
        var targets = new FocusCandidate[] { new("z", new(0, 50, 44, 44)), new("a", new(0, 50, 44, 44)),
            new("broken", new(0, 20, double.NaN, 44)), new("empty", new(0, 20, 0, 44)) };
        Assert.AreEqual("a", DirectionalFocusPolicy.Choose(origin, FocusDirection.Down, targets));
        Assert.AreEqual("a", DirectionalFocusPolicy.Choose(origin, FocusDirection.Down, targets.Reverse()));
        Assert.IsNull(DirectionalFocusPolicy.Choose(new(0, 0, 0, 0), FocusDirection.Down, targets));
    }

    [DataRow(1d)]
    [DataRow(1.5d)]
    [DataRow(2d)]
    [TestMethod]
    public void PositiveScalingPreservesGeometricChoice(double scale)
    {
        FocusRectangle Rect(double x, double y) => new(x * scale, y * scale, 100 * scale, 44 * scale);
        Assert.AreEqual("below", DirectionalFocusPolicy.Choose(Rect(0, 0), FocusDirection.Down,
            [new("right", Rect(110, 0)), new("below", Rect(0, 52)), new("diagonal", Rect(110, 52))]));
    }
}
