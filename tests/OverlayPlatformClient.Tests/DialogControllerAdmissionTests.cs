using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WidgetRail.OverlayPlatformClient.Tests;

[TestClass]
public sealed class DialogControllerAdmissionTests
{
    [TestMethod]
    public void OpeningGestureWaitsForNeutralWithoutBlockingStickDrift()
    {
        var gate = new DialogControllerAdmission();
        var frame = Neutral(); frame.State.Buttons = 0x1000;
        Assert.AreEqual(DialogControllerAdmissionResult.AwaitingRelease, gate.Observe(frame, true));
        frame.State.Buttons = 0; frame.State.LeftTrigger = 1;
        Assert.AreEqual(DialogControllerAdmissionResult.AwaitingRelease, gate.Observe(frame, true));
        frame.State.LeftTrigger = 0; frame.State.RightTrigger = 1;
        Assert.AreEqual(DialogControllerAdmissionResult.AwaitingRelease, gate.Observe(frame, true));
        frame.State.RightTrigger = 0; frame.State.LeftThumbX = 32767; frame.State.RightThumbY = -32768;
        Assert.AreEqual(DialogControllerAdmissionResult.Ready, gate.Observe(frame, true));
        frame.State.Buttons = 0x1000; frame.PressedButtons = 0x1000;
        Assert.AreEqual(DialogControllerAdmissionResult.Ready, gate.Observe(frame, true));
    }

    [TestMethod]
    [DataRow("disconnect")]
    [DataRow("priming")]
    [DataRow("ownership")]
    public void UnavailableBoundaryRequiresAnotherRelease(string boundary)
    {
        var gate = new DialogControllerAdmission();
        Assert.AreEqual(DialogControllerAdmissionResult.Ready, gate.Observe(Neutral(), true));
        var frame = Neutral();
        if (boundary == "disconnect") frame.Connected = 0;
        if (boundary == "priming") frame.Primed = 1;
        Assert.AreEqual(DialogControllerAdmissionResult.Unavailable, gate.Observe(frame, boundary != "ownership"));
        Assert.IsFalse(gate.IsArmed);
        frame = Neutral(); frame.State.Buttons = 0x1000; frame.PressedButtons = 0x1000;
        Assert.AreEqual(DialogControllerAdmissionResult.AwaitingRelease, gate.Observe(frame, true));
        Assert.AreEqual(DialogControllerAdmissionResult.Ready, gate.Observe(Neutral(), true));
        Assert.AreEqual(DialogControllerAdmissionResult.Ready, gate.Observe(frame, true));
    }

    [TestMethod]
    public void NewDialogAndNewStageCannotInheritAnotherDialogsAdmission()
    {
        var first = new DialogControllerAdmission(); var second = new DialogControllerAdmission();
        first.Observe(Neutral(), true);
        var held = Neutral(); held.State.Buttons = 0x2000;
        Assert.AreEqual(DialogControllerAdmissionResult.AwaitingRelease, second.Observe(held, true));
        first.Reset();
        Assert.AreEqual(DialogControllerAdmissionResult.AwaitingRelease, first.Observe(held, true));
    }

    private static ControllerFrame Neutral() => new() { Connected = 1 };
}
