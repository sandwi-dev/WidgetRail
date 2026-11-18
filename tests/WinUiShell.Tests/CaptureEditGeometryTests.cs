using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Media.Editing;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class CaptureEditGeometryTests
{
    [TestMethod]
    public void HeldEditingAcceleratesButTapsReversalAndResetStayPrecise()
    {
        var acceleration = new CaptureEditAcceleration();
        Assert.AreEqual(2, acceleration.Step(1, false, 0, 2, 24));
        double previous = 2;
        for (var i = 1; i <= 20; ++i)
        {
            var step = acceleration.Step(1, true, i * 100, 2, 24);
            Assert.IsTrue(step >= previous && step <= 24);
            previous = step;
        }
        Assert.AreEqual(24, previous);
        Assert.AreEqual(2, acceleration.Step(2, true, 2100, 2, 24));
        Assert.AreEqual(2, acceleration.Step(2, false, 2200, 2, 24));
        acceleration.Reset();
        Assert.AreEqual(2, acceleration.Step(2, true, 2300, 2, 24));
        Assert.AreEqual(2, acceleration.Step(2, true, 30000, 2, 24), "A stalled first repeat must not jump");
    }

    [TestMethod]
    [DataRow(5d, .2)]
    [DataRow(120d, 1.2)]
    [DataRow(600d, 6d)]
    [DataRow(7200d, 30d)]
    public void TrimHoldScalesWithDurationWithoutLosingTenthSecondTaps(double duration, double fast)
    {
        Assert.AreEqual(fast, CaptureEditAcceleration.TrimFastStep(duration));
        var acceleration = new CaptureEditAcceleration();
        Assert.AreEqual(.1, acceleration.Step(1, false, 0, .1, fast));
        double step = 0;
        for (var i = 1; i <= 20; ++i) step = acceleration.Step(1, true, i * 100, .1, fast);
        Assert.AreEqual(fast, step, .000001);
        Assert.AreEqual(.1, acceleration.Step(1, false, 2100, .1, fast));
    }

    [TestMethod]
    public void RotationKeepsSelectedPixelsAndFourTurnsRestoreGeometry()
    {
        var state = ImageCropState.Full(31, 19).WithBox(new(3, 5, 11, 7));
        var clockwise = state.Rotate(true);
        Assert.AreEqual(new CropBox(7, 3, 7, 11), clockwise.Box);
        Assert.AreEqual(state, clockwise.Rotate(false));
        Assert.AreEqual(state, clockwise.Rotate(true).Rotate(true).Rotate(true));
        Assert.AreEqual(state, state.Rotate(false).Rotate(true));
    }

    [TestMethod]
    public void EveryGripClampsWithinOddSizedSourceForExtremeInput()
    {
        foreach (var ratio in new double?[] { null, 1, 16d / 9, 9d / 16, 4d / 3 })
        foreach (var grip in Enum.GetValues<CropGrip>())
        foreach (var delta in new[] { int.MinValue, -200, -1, 0, 1, 200, int.MaxValue })
        {
            var original = ImageCropState.Full(31, 19).WithBox(new(3, 5, 11, 7)).SetAspect(ratio);
            var state = original.Adjust(grip, delta, -Math.Clamp(delta, -int.MaxValue, int.MaxValue));
            Assert.IsGreaterThanOrEqualTo(0, state.Box.X);
            Assert.IsGreaterThanOrEqualTo(0, state.Box.Y);
            Assert.IsGreaterThan(0, state.Box.Width);
            Assert.IsGreaterThan(0, state.Box.Height);
            Assert.IsLessThanOrEqualTo(state.Width, state.Box.Right);
            Assert.IsLessThanOrEqualTo(state.Height, state.Box.Bottom);
            if (grip == CropGrip.Move) Assert.AreEqual(original.Box.Width, state.Box.Width);
        }
    }

    [TestMethod]
    public void SequentialMovesKeepCropValidAndResetRestoresOriginal()
    {
        var random = new Random(713);
        var state = ImageCropState.Full(3841, 2161);
        for (var i = 0; i < 10000; i++)
        {
            if (i % 31 == 0) state = state.Rotate(i % 2 == 0);
            if (i % 97 == 0) state = state.SetAspect(i % 2 == 0 ? 16d / 9 : null);
            state = state.Adjust((CropGrip)random.Next(9), random.Next(-1000, 1000), random.Next(-1000, 1000));
            Assert.IsTrue(state.Box.X >= 0 && state.Box.Y >= 0 && state.Box.Width > 0 && state.Box.Height > 0);
            Assert.IsTrue(state.Box.Right <= state.Width && state.Box.Bottom <= state.Height);
        }
        Assert.AreEqual(ImageCropState.Full(3841, 2161), state.Reset());
    }

    [TestMethod]
    public void TrimKeepsOrderedRangeWithoutMovingOppositeHandle()
    {
        var state = new VideoTrimState(1000, 10).WithStart(100).WithEnd(700);
        Assert.AreEqual(690, state.WithStart(long.MaxValue).StartTicks);
        Assert.AreEqual(700, state.WithStart(long.MaxValue).EndTicks);
        Assert.AreEqual(110, state.WithEnd(long.MinValue).EndTicks);
        Assert.AreEqual(100, state.WithEnd(long.MinValue).StartTicks);
        Assert.AreEqual(0, state.WithStart(long.MinValue).StartTicks);
        Assert.AreEqual(1000, state.WithEnd(long.MaxValue).EndTicks);
        Assert.IsFalse(state.Reset().Changed);
    }
}
