using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class OverlayLaunchConfigurationTests
{
    private static string Installation => Path.Combine(Path.GetTempPath(), "wrail-launch", "payload");
    private static string Profile => Path.Combine(Path.GetTempPath(), "wrail-launch", "profile");

    [TestMethod]
    public void DefaultLaunchUsesItsOwnPayloadAndEstablishedUserDataLayout()
    {
        var options = OverlayLaunchConfiguration.Resolve([], Installation, Profile);
        Assert.AreEqual(Installation, options.InstallationRoot);
        Assert.AreEqual(Profile, options.SettingsRoot);
        Assert.AreEqual(Path.Combine(Profile, "widgets"), options.InstalledCatalogRoot);
        Assert.IsNull(options.InitialWidgetId);
        Assert.IsNull(options.LayoutDiagnosticsPath);
        Assert.IsNull(options.SwitchDiagnosticsPath);
    }

    [TestMethod]
    public void IsolatedProfileAlsoIsolatesItsCatalogByDefault()
    {
        var other = Path.Combine(Profile, "isolated");
        var options = OverlayLaunchConfiguration.Resolve([$"--settings-root={other}", "--widget=widgetrail.samples.playnite-library"], Installation, Profile);
        Assert.AreEqual(other, options.SettingsRoot);
        Assert.AreEqual(Path.Combine(other, "widgets"), options.InstalledCatalogRoot);
        Assert.AreEqual("widgetrail.samples.playnite-library", options.InitialWidgetId);
    }

    [TestMethod]
    public void ExplicitRootsRemainIndependentAndCanonical()
    {
        var payload = Path.Combine(Installation, "..", "other-payload");
        var catalog = Path.Combine(Profile, "explicit-catalog");
        var options = OverlayLaunchConfiguration.Resolve([$"--installation-root={payload}", $"--installed-catalog-root={catalog}"], Installation, Profile);
        Assert.AreEqual(Path.GetFullPath(payload), options.InstallationRoot);
        Assert.AreEqual(catalog, options.InstalledCatalogRoot);
    }

    [TestMethod]
    public void MissingAndConflictingArgumentsDoNotSilentlySelectAnotherProfile()
    {
        foreach (var arguments in new string[][] {
            ["--settings-root"], ["--settings-root="], ["--settings-root=relative"],
            [$"--settings-root={Profile}", $"--settings-root={Installation}"],
            ["--shell-config=missing.json", $"--settings-root={Profile}"], ["--widget=bad/id"] })
            Assert.ThrowsExactly<InvalidDataException>(() => OverlayLaunchConfiguration.Resolve(arguments, Installation, Profile));
    }

    [TestMethod]
    public void PreflightChecksOnlyTheSelectedPayloadWithoutCreatingUserState()
    {
        var options = OverlayLaunchConfiguration.Resolve([], Installation, Profile);
        var reads = new List<string>();
        Assert.IsTrue(OverlayLaunchConfiguration.HasInstallationFiles(options, path => { reads.Add(path); return true; }));
        CollectionAssert.AreEqual(new[] { Path.Combine(Installation, "widget-catalog.json"),
            Path.Combine(Installation, "runtime", "Bridge", "WidgetBridge.exe") }, reads);
        Assert.IsFalse(OverlayLaunchConfiguration.HasInstallationFiles(options, path => path.EndsWith("widget-catalog.json", StringComparison.Ordinal)));
    }
}
