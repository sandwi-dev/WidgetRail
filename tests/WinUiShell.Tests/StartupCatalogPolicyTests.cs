using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class StartupCatalogPolicyTests
{
    [TestMethod]
    public void TrustedSettingsCanOpenBeforeInstalledValidationFinishes()
    {
        Assert.IsTrue(StartupCatalogPolicy.CanOpenInitial(false, null, ["settings", "power"]));
        Assert.IsFalse(StartupCatalogPolicy.CanOpenInitial(false, null, ["power"]));
        Assert.IsFalse(StartupCatalogPolicy.CanOpenInitial(false, null, []));
    }

    [TestMethod]
    public void ExplicitWidgetWaitsForItsOwnAdmission()
    {
        Assert.IsFalse(StartupCatalogPolicy.CanOpenInitial(false, "community", ["settings"]));
        Assert.IsTrue(StartupCatalogPolicy.CanOpenInitial(false, "community", ["settings", "community"]));
    }

    [TestMethod]
    public void CompletedCatalogAllowsExistingFallbackAndEmptyRecovery()
    {
        Assert.IsTrue(StartupCatalogPolicy.CanOpenInitial(true, "missing", ["settings"]));
        Assert.IsTrue(StartupCatalogPolicy.CanOpenInitial(true, null, []));
    }
}
