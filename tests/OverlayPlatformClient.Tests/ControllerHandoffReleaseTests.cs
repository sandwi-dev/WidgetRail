using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.OverlayPlatformClient;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class ControllerHandoffReleaseTests
{
    [TestMethod]
    public async Task HeldAAndQueuedHistoricalReleaseCannotCompleteHandoff()
    {
        var gate = new ControllerHandoffRelease();
        gate.Observe(Frame(0x1000));
        var release = gate.Begin();
        gate.Observe(Frame(0x1000));
        Assert.IsFalse(release.IsCompleted);
        var historical = Frame(0); historical.RemainingFrames = 1;
        gate.Observe(historical);
        Assert.IsFalse(release.IsCompleted);
        gate.Observe(Frame(0x1000));
        Assert.IsFalse(release.IsCompleted);
        gate.Observe(Frame(0));
        Assert.IsTrue(await release);
    }

    [TestMethod]
    public async Task EachBoundaryRequiresFreshNeutralAndIncludesNewPresses()
    {
        var gate = new ControllerHandoffRelease();
        gate.Observe(Frame(0));
        var first = gate.Begin();
        Assert.IsFalse(first.IsCompleted);
        gate.Observe(Frame(0));
        Assert.IsTrue(await first);
        var afterAnimation = gate.Begin();
        gate.Observe(Frame(0x1000));
        Assert.IsFalse(afterAnimation.IsCompleted);
        gate.Observe(Frame(0));
        Assert.IsTrue(await afterAnimation);
    }

    [TestMethod]
    public async Task DisconnectAndCancellationRejectWithoutCarryingIntoAnotherAttempt()
    {
        var gate = new ControllerHandoffRelease();
        gate.Observe(Frame(0x1000));
        var first = gate.Begin();
        gate.Observe(new());
        Assert.IsFalse(await first);
        var cancelled = gate.Begin(); gate.Cancel();
        Assert.IsFalse(await cancelled);
        var keyboardOnly = gate.Begin(); gate.Observe(new());
        Assert.IsTrue(await keyboardOnly);
    }

    [TestMethod]
    public async Task TriggerGestureWaitsForReleaseButStickDriftDoesNotBlock()
    {
        var gate = new ControllerHandoffRelease();
        var release = gate.Begin();
        var frame = Frame(0); frame.State.LeftTrigger = 200;
        gate.Observe(frame); Assert.IsFalse(release.IsCompleted);
        frame.State.LeftTrigger = 30; frame.State.RightThumbX = 500;
        gate.Observe(frame); Assert.IsTrue(await release);
    }

    private static ControllerFrame Frame(ushort buttons) => new() { Connected = 1, State = new() { Buttons = buttons } };
}
