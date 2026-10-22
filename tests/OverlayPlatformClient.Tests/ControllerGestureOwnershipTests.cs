using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayPlatformClient.Tests;

[TestClass]
public sealed class ControllerGestureOwnershipTests
{
    [TestMethod]
    public void MenuGestureStaysOwnedUntilExactlyOneRelease()
    {
        var gestures = new ControllerGestureOwnership();
        gestures.Claim(ControllerButton.A);
        var duplicate = new ControllerFrame { PressedButtons = 0x1000 };
        Assert.IsTrue(gestures.Owns(ControllerButton.A));
        gestures.CaptureFrame(duplicate);
        Assert.IsTrue(gestures.Owns(ControllerButton.A));
        Assert.IsTrue(gestures.Release(ControllerButton.A));
        Assert.IsFalse(gestures.Release(ControllerButton.A));
        Assert.IsFalse(gestures.Owns(ControllerButton.A));
    }

    [TestMethod]
    public void ReleaseObservationDuringSwitchDoesNotClaimNewPressesOrTriggers()
    {
        var gestures = new ControllerGestureOwnership();
        gestures.Claim(ControllerButton.Y); gestures.Claim(ControllerButton.LeftTrigger);
        gestures.ReleaseDigitalButtons(new() { PressedButtons = 0x1000, ReleasedButtons = 0x8000, LeftTriggerReleased = 1 });
        Assert.IsFalse(gestures.Owns(ControllerButton.Y));
        Assert.IsFalse(gestures.Owns(ControllerButton.A));
        Assert.IsTrue(gestures.Owns(ControllerButton.LeftTrigger));
    }

    [TestMethod]
    public void PrimingReconcilesDigitalStateWithoutChangingTriggerPolicy()
    {
        var gestures = new ControllerGestureOwnership();
        gestures.Claim(ControllerButton.B); gestures.Claim(ControllerButton.RightTrigger);
        gestures.PrimeDigitalButtons(0x1000);
        Assert.IsTrue(gestures.Owns(ControllerButton.A));
        Assert.IsFalse(gestures.Owns(ControllerButton.B));
        Assert.IsTrue(gestures.Owns(ControllerButton.RightTrigger));
        gestures.PrimeDigitalButtons(0);
        Assert.IsFalse(gestures.Owns(ControllerButton.A));
    }

    [TestMethod]
    public void PickerNeutralStateRetiresMissingReleasesButDoesNotClaimHeldInput()
    {
        var gestures = new ControllerGestureOwnership();
        gestures.Claim(ControllerButton.A); gestures.Claim(ControllerButton.LeftTrigger); gestures.Claim(ControllerButton.RightTrigger);
        gestures.ReconcileReleasedState(new() { State = new() { Buttons = 0x2000, RightTrigger = 1 } });
        Assert.IsFalse(gestures.Owns(ControllerButton.A));
        Assert.IsFalse(gestures.Owns(ControllerButton.B));
        Assert.IsFalse(gestures.Owns(ControllerButton.LeftTrigger));
        Assert.IsTrue(gestures.Owns(ControllerButton.RightTrigger));
        gestures.Reset();
        Assert.IsFalse(gestures.Owns(ControllerButton.RightTrigger));
    }

    [TestMethod]
    public void ExplicitReleaseEdgesStayOwnedUntilRouterConsumesThem()
    {
        var gestures = new ControllerGestureOwnership();
        foreach (var (mask, button) in ControllerGestureOwnership.DigitalButtons)
        {
            gestures.Claim(button);
            gestures.ReconcileReleasedState(new() { ReleasedButtons = mask });
            Assert.IsTrue(gestures.Release(button), button.ToString());
            Assert.IsFalse(gestures.Release(button), "Duplicate releases must not dispatch twice.");
        }
        gestures.Claim(ControllerButton.LeftTrigger); gestures.Claim(ControllerButton.RightTrigger);
        gestures.ReconcileReleasedState(new() { LeftTriggerReleased = 1, RightTriggerReleased = 1 });
        Assert.IsTrue(gestures.Release(ControllerButton.LeftTrigger));
        Assert.IsTrue(gestures.Release(ControllerButton.RightTrigger));
        gestures.ReconcileReleasedState(new() { ReleasedButtons = 0x8000 });
        Assert.IsFalse(gestures.Owns(ControllerButton.Y), "An unowned release must never acquire ownership.");
    }

    [TestMethod]
    public void PickerCaptureConsumesExplicitEdgesBeforeReconciliation()
    {
        var gestures = new ControllerGestureOwnership();
        gestures.Claim(ControllerButton.Y);
        var release = new ControllerFrame { ReleasedButtons = 0x8000 };
        gestures.CaptureFrame(release);
        gestures.ReconcileReleasedState(release);
        Assert.IsFalse(gestures.Owns(ControllerButton.Y));
    }

    [TestMethod]
    public void CapturePreservesAllDigitalMappingsAndIndependentTriggers()
    {
        var gestures = new ControllerGestureOwnership();
        var expected = new[] { ControllerButton.A, ControllerButton.B, ControllerButton.X, ControllerButton.Y,
            ControllerButton.LeftBumper, ControllerButton.RightBumper, ControllerButton.LeftStick,
            ControllerButton.RightStick, ControllerButton.Menu, ControllerButton.View };
        gestures.CaptureFrame(new() { PressedButtons = 0xf3f0, LeftTriggerPressed = 1, RightTriggerPressed = 1 });
        foreach (var button in expected) Assert.IsTrue(gestures.Owns(button), button.ToString());
        Assert.IsTrue(gestures.Owns(ControllerButton.LeftTrigger));
        Assert.IsTrue(gestures.Owns(ControllerButton.RightTrigger));
        gestures.CaptureFrame(new() { ReleasedButtons = 0xf3f0, LeftTriggerReleased = 1 });
        foreach (var button in expected) Assert.IsFalse(gestures.Owns(button), button.ToString());
        Assert.IsFalse(gestures.Owns(ControllerButton.LeftTrigger));
        Assert.IsTrue(gestures.Owns(ControllerButton.RightTrigger));
        gestures.CaptureFrame(new() { RightTriggerReleased = 1 });
        Assert.IsFalse(gestures.Owns(ControllerButton.RightTrigger));
    }

    [TestMethod]
    public void CaptureConsumesCombinedPressReleaseAndLeavesUnrelatedOwnership()
    {
        var gestures = new ControllerGestureOwnership();
        gestures.Claim(ControllerButton.B);
        gestures.CaptureFrame(new() { PressedButtons = 0x1000, ReleasedButtons = 0x1000, LeftTriggerPressed = 1, LeftTriggerReleased = 1 });
        Assert.IsFalse(gestures.Owns(ControllerButton.A));
        Assert.IsFalse(gestures.Owns(ControllerButton.LeftTrigger));
        Assert.IsTrue(gestures.Owns(ControllerButton.B));
    }
}
