using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Presentation;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class ContentSizedPresentationStateTests
{
    [TestMethod]
    public void LateContentDoesNotResizeButGeometryAndNewSourcesDo()
    {
        var state = new ContentSizedPresentationState();
        var calls = 0;
        double Measure() { calls++; return 160; }
        Assert.AreEqual(160, state.Resolve(420, 96, 280, 1, Measure));
        Assert.AreEqual(160, state.Resolve(420, 96, 280, 1, () => throw new Exception("Late update measured")));
        Assert.AreEqual(160, state.Resolve(360, 96, 280, 1, Measure));
        Assert.AreEqual(160, state.Resolve(360, 96, 280, 1.5, Measure));
        Assert.AreEqual(120, state.Resolve(360, 96, 120, 1.5, Measure));
        state.Reset();
        Assert.AreEqual(100, state.Resolve(360, 96, 120, 1.5, () => { calls++; return 100; }));
        Assert.AreEqual(5, calls);
    }

    [TestMethod]
    public void InvalidOpeningAndContentRemainBounded()
    {
        var state = new ContentSizedPresentationState();
        Assert.AreEqual(0, state.Resolve(0, 96, 280, 1, () => throw new Exception("Unlaid-out measure")));
        Assert.AreEqual(96, state.Resolve(420, 96, 280, 1, () => 12));
        state.Reset();
        Assert.AreEqual(280, state.Resolve(420, 96, 280, 1, () => double.NaN));
        Assert.AreEqual(40, state.Resolve(100, 96, 40, 1, () => 900));
    }

    [TestMethod]
    public void CuesScaleWithViewportNotScrollPosition()
    {
        Assert.AreEqual(48, ScrollEdgeCueState.Width(340));
        Assert.AreEqual(64, ScrollEdgeCueState.Width(800));
        Assert.AreEqual(80, ScrollEdgeCueState.Width(1600));
        Assert.AreEqual(24, ScrollEdgeCueState.Width(24));
        Assert.AreEqual(0, ScrollEdgeCueState.Width(double.NaN));
        Assert.AreEqual(0, ScrollEdgeCueState.Width(double.PositiveInfinity));
    }
}
