using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.OverlayFrontend.WinUI.Notifications;

namespace WinUiShell.Tests;

[TestClass]
public sealed class PinnedPlacementAdjustmentTests
{
    [TestMethod]
    public void OpacityAndRevertedGeometryDoNotReplacePreferredPosition()
    {
        var monitor=new PinnedMonitor("main",new(0,0,1920,1080),1);
        var initial=new PinnedBounds(100,120,480,270);
        var preview=new PinnedPlacementAdjustment(initial,monitor,PinnedPlacementLimits.Default,"compact",100);
        preview.SetOpacity(45);Assert.IsFalse(preview.GeometryChanged);
        preview.Step(PinnedPlacementDirection.Right,false);Assert.IsTrue(preview.GeometryChanged);
        preview.Step(PinnedPlacementDirection.Left,false);Assert.IsFalse(preview.GeometryChanged);
    }

    [TestMethod]
    public void ToolbarStaysReachableAtEveryMonitorEdgeAndBesideLargePins()
    {
        foreach(var scale in new[]{1d,1.25,2})
        {
            var area=new PinnedBounds(-1920,-200,1920,1080);
            foreach(var pin in new[]{new PinnedBounds(-1800,0,600,400),new(-1920,-200,600,400),new(-600,480,600,400),area})
            {
                var toolbar=PinnedAdjustmentToolbarPlacement.Choose(pin,area,(int)(380*scale),(int)(120*scale),(int)(8*scale));
                Assert.IsTrue(toolbar.X>=area.X&&toolbar.Y>=area.Y&&toolbar.X+toolbar.Width<=area.X+area.Width&&toolbar.Y+toolbar.Height<=area.Y+area.Height);
                if(pin!=area)Assert.IsFalse(NotificationPlacement.Overlaps(pin,toolbar));
            }
        }
    }
    [TestMethod]
    public void PointerResizeKeepsOppositeEdgesAtLimitsAndCancelBaseline()
    {
        var monitor=new PinnedMonitor("main",new(-1200,-100,1200,900),1);
        var original=new PinnedBounds(-1000,100,480,270);
        var preview=new PinnedPlacementAdjustment(original,monitor,PinnedPlacementLimits.Default,"compact",65);
        Assert.AreEqual(new PinnedBounds(-760,235,240,135),preview.PreviewPointer(new(-600,300,80,70),4,[monitor]));
        Assert.AreEqual(new PinnedBounds(-1000,100,960,540),preview.PreviewPointer(new(-1000,100,1800,700),8,[monitor]));
        Assert.AreEqual(original,PinnedPlacementPolicy.Resolve([monitor],preview.Original,preview.Limits)!.Bounds);
    }

    [TestMethod]
    public void PointerMoveCrossesDisplaysPreservingDipsWhileResizeStaysOnDisplay()
    {
        var left=new PinnedMonitor("left",new(-1920,0,1920,1080),1);
        var right=new PinnedMonitor("right",new(0,0,2560,1440),2);
        var preview=new PinnedPlacementAdjustment(new(-800,100,480,270),left,PinnedPlacementLimits.Default,"compact",100);
        Assert.AreEqual(new PinnedBounds(100,100,960,540),preview.PreviewPointer(new(100,100,480,270),0,[left,right]));
        Assert.AreEqual(right,preview.Monitor);
        Assert.AreEqual(new PinnedBounds(180,140,960,540),preview.PreviewPointer(new(180,140,480,270),0,[left,right]));
        Assert.AreEqual(new PinnedBounds(220,160,960,540),preview.PreviewPointer(new(220,160,480,270),0,[left,right]));
        Assert.AreEqual(480d,PinnedPlacementPolicy.Capture(preview.Current,right,preview.Limits,"compact",100).WidthDip);
        preview.PreviewPointer(new(-1000,100,1960,540),1,[left,right]);
        Assert.AreEqual(right,preview.Monitor);
        Assert.AreEqual(0,preview.Current.X);
        Assert.AreEqual("left",preview.Original.MonitorId);
    }
    [TestMethod]
    public void CrossingDisplayEdgesPreservesLogicalSizeAndCancelBaseline()
    {
        var left = new PinnedMonitor("left", new(0, 0, 1920, 1080), 1, true);
        var right = new PinnedMonitor("right", new(1920, 0, 2560, 1440), 2);
        var original = new PinnedBounds(1440, 300, 480, 270);
        var adjustment = new PinnedPlacementAdjustment(original, left, PinnedPlacementLimits.Default, "compact", 65);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Right, false, [left, right]));
        Assert.AreEqual(right, adjustment.Monitor);
        Assert.AreEqual(new PinnedBounds(1920, 165, 960, 540), adjustment.Current);
        Assert.AreEqual(original, PinnedPlacementPolicy.Resolve([left, right], adjustment.Original, adjustment.Limits)!.Bounds);
        var committed = PinnedPlacementPolicy.Capture(adjustment.Current, adjustment.Monitor, adjustment.Limits, "compact", 65);
        Assert.AreEqual("right", committed.MonitorId);
        Assert.AreEqual(480d, committed.WidthDip);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Left, false, [left, right]));
        Assert.AreEqual(left, adjustment.Monitor);
        Assert.AreEqual(original, adjustment.Current);
    }

    [TestMethod]
    public void DisplayCrossingFollowsDirectionAndSkipsUnalignedOrTooSmallDisplays()
    {
        var main = new PinnedMonitor("main", new(0, 0, 1920, 1080), 1);
        var above = new PinnedMonitor("above", new(100, -1100, 1600, 1000), 1.25);
        var far = new PinnedMonitor("far", new(100, -2500, 1600, 1000), 1);
        var tiny = new PinnedMonitor("tiny", new(100, -80, 1600, 80), 1);
        var offset = new PinnedMonitor("offset", new(2000, -1080, 1920, 1080), 1);
        var adjustment = new PinnedPlacementAdjustment(new(500, 0, 480, 270), main, PinnedPlacementLimits.Default, "compact", 100);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Up, false, [main, far, tiny, offset, above]));
        Assert.AreEqual(above, adjustment.Monitor);
        Assert.AreEqual(new PinnedBounds(440, -438, 600, 338), adjustment.Current);
        Assert.IsTrue(adjustment.Step(PinnedPlacementDirection.Down, false, [main, above]));
        Assert.AreEqual(main, adjustment.Monitor);
        Assert.IsFalse(adjustment.Step(PinnedPlacementDirection.Up, false, [main, offset, tiny]));
        Assert.AreEqual(main, adjustment.Monitor);
    }

    [TestMethod]
    public void ResizingAtDisplayEdgeKeepsTheCurrentMonitor()
    {
        var left = new PinnedMonitor("left", new(0, 0, 960, 540), 1);
        var right = new PinnedMonitor("right", new(960, 0, 1920, 1080), 1);
        var adjustment = new PinnedPlacementAdjustment(new(0, 0, 960, 540), left, PinnedPlacementLimits.Default, "compact", 100);
        Assert.IsFalse(adjustment.Step(PinnedPlacementDirection.Right, true, [left, right]));
        Assert.AreEqual(left, adjustment.Monitor);
        Assert.AreEqual(new PinnedBounds(0, 0, 960, 540), adjustment.Current);
    }

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
    public void SnapUsesNewPressesAndReachesAllSixSlotsOnTheCurrentWorkArea()
    {
        var monitor = new PinnedMonitor("left", new(-2560, -80, 2560, 1401), 1.5);
        var pin = new PinnedPlacementAdjustment(new(-600, 600, 720, 405), monitor, PinnedPlacementLimits.Default, "compact", 65);
        var input = new PinnedPlacementInput();
        input.Prime(8, 0, 0, 0, 0, 0);
        Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(8, 0, 0, 0, 0, 2000, monitor).Dpad);
        foreach (var side in new[] { (Button: (ushort)4, X: -2560), (Button: (ushort)8, X: -720) })
        {
            Tap(side.Button);
            Tap(1); Tap(1);
            foreach (int y in new[] { -80, 418, 916 })
            {
                Assert.AreEqual(new PinnedBounds(side.X, y, 720, 405), pin.Current);
                Assert.AreEqual(monitor, pin.Monitor);
                Tap(2);
            }
        }
        void Tap(ushort button)
        {
            input.Sample(0, 0, 0, 0, 0, 2010, monitor);
            pin.Snap(input.Sample(button, 0, 0, 0, 0, 2011, monitor).Dpad);
            Assert.AreEqual(PinnedPlacementDirection.None, input.Sample(button, 0, 0, 0, 0, 3011, monitor).Dpad);
        }
    }

    [TestMethod]
    public void StickTravelScalesWithTiltAndTimeAndDropsStalledTime()
    {
        var monitor = new PinnedMonitor("4k", new(0, 0, 3840, 2160), 1.5);
        int Travel(int cadence, short tilt)
        {
            var input = new PinnedPlacementInput();
            input.Prime(0, 0, 0, 0, 0, 0);
            int travel = 0;
            for (int time = 0; time <= 1000; time += cadence)
                travel += input.Sample(0, tilt, 0, 0, 0, time, monitor).X;
            return travel;
        }
        Assert.IsTrue(Travel(10, short.MaxValue) > 3300);
        Assert.IsTrue(Math.Abs(Travel(20, short.MaxValue) - Travel(10, short.MaxValue)) < 45);
        Assert.IsTrue(Travel(10, 11000) is > 0 and < 40);
        var input = new PinnedPlacementInput();
        input.Prime(0, short.MaxValue, 0, 0, 0, 0);
        Assert.AreEqual(0, input.Sample(0, short.MaxValue, 0, 0, 0, 500, monitor).X);
        input.Sample(0, 0, 0, 0, 0, 510, monitor);
        var diagonal = input.Sample(0, short.MaxValue, short.MaxValue, 0, short.MinValue, 526, monitor);
        Assert.IsTrue(diagonal.X > 0 && diagonal.Y == -diagonal.X);
        Assert.AreEqual(PinnedPlacementDirection.Down, diagonal.Resize);
        Assert.IsTrue(input.Sample(0, short.MaxValue, 0, 0, 0, 9000, monitor).X is > 0 and < 25);
        Assert.IsTrue(input.Sample(0, short.MinValue, 0, 0, 0, 9016, monitor).X < 0);
        Assert.AreEqual(0, input.Sample(0, 100, 100, 0, 0, 9032, monitor).X);
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
