using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ForegroundDismissalPolicyTests
{
    [TestMethod]
    public void ExternalApplicationDismissesVisibleOverlay() =>
        Assert.IsTrue(ForegroundDismissalPolicy.ShouldDismiss(true, 10, 10, true, 2, 1));

    [TestMethod]
    public void OwnWindowTransfersKeepOverlayOpen() =>
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(true, 10, 10, true, 1, 1));

    [TestMethod]
    public void HiddenOverlayIgnoresExternalActivation() =>
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(false, 10, 10, true, 2, 1));

    [TestMethod]
    public void MissingOrDestroyedForegroundIsNotDismissalEvidence()
    {
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(true, 0, 0, false, 0, 1));
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(true, 10, 10, false, 2, 1));
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(true, 10, 10, true, 0, 1));
    }

    [TestMethod]
    public void StaleObservationCannotHideReopenedOverlay() =>
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(true, 10, 20, true, 1, 1));

    [TestMethod]
    public void StaleExternalObservationWaitsForCurrentForeground() =>
        Assert.IsFalse(ForegroundDismissalPolicy.ShouldDismiss(true, 10, 20, true, 3, 1));
}
