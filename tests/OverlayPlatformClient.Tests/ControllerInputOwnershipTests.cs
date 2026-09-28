using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WidgetRail.OverlayPlatformClient.Tests;

[TestClass]
public sealed class ControllerInputOwnershipTests
{
    [TestMethod]
    public void VisibleBackgroundSamplingCannotDeliverNavigation()
    {
        var owner = new ControllerInputOwnership();
        Assert.AreEqual((false, false), owner.Update(true, false));
        Assert.AreEqual((true, true), owner.Update(true, true));
        Assert.AreEqual((true, false), owner.Update(true, true));
        Assert.AreEqual((false, false), owner.Update(true, false));
        Assert.AreEqual((false, false), owner.Update(true, false));
    }

    [TestMethod]
    public void RegainingForegroundPrimesBeforeNewInputButDoesNotResetEveryFrame()
    {
        var owner = new ControllerInputOwnership();
        Assert.AreEqual((true, true), owner.Update(true, true));
        Assert.AreEqual((false, false), owner.Update(true, false));
        Assert.AreEqual((true, true), owner.Update(true, true));
        Assert.AreEqual((true, false), owner.Update(true, true));
    }

    [TestMethod]
    public void HiddenOverlayNeverDeliversEvenIfWindowsStillReportsItForeground()
    {
        var owner = new ControllerInputOwnership();
        owner.Update(true, true);
        Assert.AreEqual((false, false), owner.Update(false, true));
        Assert.AreEqual((false, false), owner.Update(false, false));
        Assert.AreEqual((true, true), owner.Update(true, true));
    }

    [TestMethod]
    public void ExplicitReopenEstablishesAnotherNeutralBoundary()
    {
        var owner = new ControllerInputOwnership();
        owner.Update(true, true);
        owner.Reset();
        Assert.AreEqual((true, true), owner.Update(true, true));
        Assert.AreEqual((true, false), owner.Update(true, true));
    }
}
