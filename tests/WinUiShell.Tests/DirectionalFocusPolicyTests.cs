using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WinUiShell.Tests;

[TestClass]
public sealed class DirectionalFocusPolicyTests
{
    [TestMethod]
    public void AdjacentPanelFallbackUsesPanelGeometryAndNearestEntry()
    {
        var origin = new FocusRectangle(25, 480, 415, 44);
        var panel = new FocusRectangle(486, 20, 470, 705);
        var entries = new FocusPanelCandidate[] { new("details", panel, new("top", new(507, 85, 427, 54))),
            new("details", panel, new("nearer", new(507, 300, 427, 44))) };
        Assert.IsNull(DirectionalFocusPolicy.Choose(origin, FocusDirection.Right, entries.Select(entry => entry.Entry)));
        Assert.AreEqual("nearer", DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Right, entries));
        Assert.AreEqual("nearer", DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Right, entries.Reverse()));
        Assert.AreEqual("top", DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Right, entries.Take(1)));
        Assert.IsNull(DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Up, entries));
    }

    [TestMethod]
    public void AdjacentPanelFallbackRejectsHeadersStackedAndOverlappingRegions()
    {
        var origin = new FocusRectangle(100, 300, 200, 44);
        foreach (var panel in new[] { new FocusRectangle(320, 0, 200, 60), new(100, 400, 200, 300),
            new(120, 0, 600, 600), new(320, 344, 200, 200), new(320, 0, 0, 600) })
            Assert.IsNull(DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Right,
                [new("panel", panel, new("button", new(320, 20, 200, 44)))]));
        Assert.AreEqual("left", DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Left,
            [new("panel", new(0, 0, 80, 600), new("left", new(0, 20, 80, 44)))]));
    }

    [TestMethod]
    public void AdjacentPanelFallbackDoesNotSkipNearerPanelsForCloserChildren()
    {
        var origin = new FocusRectangle(0, 300, 200, 44);
        Assert.AreEqual("near-panel", DirectionalFocusPolicy.ChooseAdjacentPanel(origin, FocusDirection.Right,
            [new("near", new(220, 0, 200, 600), new("near-panel", new(220, 20, 200, 44))),
             new("far", new(440, 0, 200, 600), new("far-panel", new(440, 250, 200, 44)))]));
    }

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
