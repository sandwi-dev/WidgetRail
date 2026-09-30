using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class PinnedPlacementAdjustmentTests
{
    [TestMethod]
    public void OpacityPreviewIsBoundedAndDoesNotChangePlacementOrCancelBaseline()
    {
        var monitor = new PinnedMonitor("display", new(0, 0, 1920, 1080), 1);
        var bounds = new PinnedBounds(20, 20, 480, 270);
        var adjustment = new PinnedPlacementAdjustment(bounds, monitor, PinnedPlacementLimits.Default, "compact", 65);
        Assert.IsTrue(adjustment.StepOpacity(PinnedPlacementDirection.Left));
        Assert.AreEqual(60, adjustment.OpacityPercent);
        Assert.IsFalse(adjustment.StepOpacity(PinnedPlacementDirection.Up));
        for (var i = 0; i < 30; ++i) adjustment.StepOpacity(PinnedPlacementDirection.Left);
        Assert.AreEqual(30, adjustment.OpacityPercent);
        Assert.IsFalse(adjustment.StepOpacity(PinnedPlacementDirection.Left));
        for (var i = 0; i < 30; ++i) adjustment.StepOpacity(PinnedPlacementDirection.Right);
        Assert.AreEqual(100, adjustment.OpacityPercent);
        Assert.IsFalse(adjustment.StepOpacity(PinnedPlacementDirection.Right));
        Assert.AreEqual(65, adjustment.Original.OpacityPercent);
        Assert.AreEqual(bounds, adjustment.Current);
        var committed = PinnedPlacementPolicy.Capture(adjustment.Current, monitor, adjustment.Limits, "compact", adjustment.OpacityPercent);
        Assert.AreEqual(100, committed.OpacityPercent);
    }

    [TestMethod]
    public void PreviewMovesAndResizesAtMonitorDpiWithoutMutatingCancelBaseline()
    {
        var monitor = new PinnedMonitor("left", new(-1920, -200, 1920, 1040), 1.25);
        var original = new PinnedBounds(-1000, 100, 600, 375);
        var adjustment = new PinnedPlacementAdjustment(original, monitor, PinnedPlacementLimits.Default, "compact", 65);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Left, false));
        Assert.AreEqual(original with { X = -1040 }, adjustment.Current);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Down, true));
        Assert.AreEqual(new PinnedBounds(-1040, 100, 600, 415), adjustment.Current);
        Assert.AreEqual(original, PinnedPlacementPolicy.Resolve([monitor], adjustment.Original, adjustment.Limits)!.Bounds);
        Assert.AreEqual(65, adjustment.Original.OpacityPercent);
        Assert.AreEqual("compact", adjustment.Original.LayoutId);
    }

    [TestMethod]
    public void PreviewConstrainsMoveAndResizeToMinimumMaximumAndWorkArea()
    {
        var monitor = new PinnedMonitor("small", new(-800, 50, 800, 550), 1);
        var adjustment = new PinnedPlacementAdjustment(new(-800, 50, 240, 135), monitor, PinnedPlacementLimits.Default, "compact", 100);
        Assert.IsFalse(adjustment.Step(PinnedPlacementDirection.Left, false));
        Assert.IsFalse(adjustment.Step(PinnedPlacementDirection.Up, true));
        for (var i = 0; i < 50; ++i)
        { adjustment.Step(PinnedPlacementDirection.Right, true); adjustment.Step(PinnedPlacementDirection.Down, true); }
        Assert.AreEqual(new PinnedBounds(-800, 50, 800, 540), adjustment.Current);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Down, false));
        Assert.AreEqual(60, adjustment.Current.Y);
        Assert.IsFalse(adjustment.Step(PinnedPlacementDirection.Down, false));
    }

    [TestMethod]
    public void CancelAndCommitRemainValidAfterDisplayRemovalOrDpiChange()
    {
        var old = new PinnedMonitor("removed", new(-1920, 0, 1920, 1080), 1);
        var adjustment = new PinnedPlacementAdjustment(new(-1000, 100, 480, 270), old, PinnedPlacementLimits.Default, "compact", 70);
        adjustment.Step(PinnedPlacementDirection.Right, true);
        var replacement = new PinnedMonitor("current", new(0, -100, 1600, 1200), 1.5, true);
        var canceled = PinnedPlacementPolicy.Resolve([replacement], adjustment.Original, adjustment.Limits)!;
        var committed = PinnedPlacementPolicy.Resolve([replacement], PinnedPlacementPolicy.Capture(adjustment.Current,
            old, adjustment.Limits, "compact", 70), adjustment.Limits)!;
        Assert.IsTrue(canceled.UsedFallback);
        Assert.AreEqual(720, canceled.Bounds.Width);
        Assert.AreEqual(768, committed.Bounds.Width);
        Assert.IsTrue(canceled.Bounds.X >= 0 && canceled.Bounds.Y >= -100);
    }

    [TestMethod]
    public void PrimingSuppressesInheritedPressAndRepeatNeverReplaysAHitch()
    {
        var input = new PinnedPlacementInput();
        input.Prime(8, 0, 0, 0, 0, 1000);
        Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(8, 0, 0, 0, 0, 1249).Dpad);
        Assert.AreEqual(PinnedPlacementDirection.Right, input.Sample(8, 0, 0, 0, 0, 1250).Dpad);
        Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(8, 0, 0, 0, 0, 1329).Dpad);
        Assert.AreEqual(PinnedPlacementDirection.Right, input.Sample(8, 0, 0, 0, 0, 8000).Dpad);
        Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(8, 0, 0, 0, 0, 8001).Dpad);
        input.Sample(0, 0, 0, 0, 0, 8002);
        Assert.AreEqual(PinnedPlacementDirection.Left, input.Sample(4, 0, 0, 0, 0, 8003).Dpad);
    }

    [TestMethod]
    public void SticksAreIndependentAndPreserveNativeAxisHysteresis()
    {
        var input = new PinnedPlacementInput(); input.Prime(0, 0, 0, 0, 0, 0);
        var start = input.Sample(0, 16000, 0, 0, short.MinValue, 1);
        Assert.AreEqual(PinnedPlacementDirection.Right, start.Move);
        Assert.AreEqual(PinnedPlacementDirection.Down, start.Resize);
        Assert.AreEqual(PinnedPlacementDirection.Right, input.Sample(0, 16000, 19000, 0, 0, 251).Move);
        Assert.AreEqual(PinnedPlacementDirection.Up, input.Sample(0, 16000, 21000, 0, 0, 252).Move);
        Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(0, 0, 8000, 0, 0, 500).Move);
        Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(0, 0, 14000, 0, 0, 600).Move);
        Assert.AreEqual(PinnedPlacementDirection.Up, input.Sample(0, 0, 15000, 0, 0, 601).Move);
    }

    [TestMethod]
    public void AdjustmentGuidePreservesCancelInsteadOfAddingContradictoryBack()
    {
        var hints = ControllerGuideModel.WithHost([new(ControllerPrompt.A, "Save", ControllerButton.A, Required: true),
            new(ControllerPrompt.B, "Cancel", ControllerButton.B, Required: true)]);
        Assert.AreEqual(1, hints.Count(hint => hint.Prompt == ControllerPrompt.B));
        Assert.AreEqual("Cancel", hints.Single(hint => hint.Prompt == ControllerPrompt.B).Label);
        Assert.AreEqual(1, hints.Count(hint => hint.Prompt == ControllerPrompt.Guide));
    }
}
