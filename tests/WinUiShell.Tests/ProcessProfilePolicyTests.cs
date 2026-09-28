using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class ProcessProfilePolicyTests
{
    private const string Production = @"C:\Users\Fixture\AppData\Local\WidgetRail";
    [TestMethod]
    public void DefaultSettingsRetainNativeProductionIdentityAcrossPathSpellings()
    {
        Assert.AreEqual("production", ProcessProfilePolicy.ForSettingsRoot(Production.ToUpperInvariant() + @"\", Production));
        Assert.AreEqual("production", ProcessProfilePolicy.ForSettingsRoot(@"\\?\" + Production, Production));
        Assert.AreEqual("production", ProcessProfilePolicy.ForSettingsRoot(Production + @"\child\..", Production));
    }
    [TestMethod]
    public void IsolatedRootsHaveStableBoundedIndependentIdentities()
    {
        var first = ProcessProfilePolicy.ForSettingsRoot(@"C:\isolated\first", Production);
        Assert.AreEqual(64, first.Length);
        Assert.IsTrue(first.All(char.IsAsciiHexDigit));
        Assert.AreEqual(first, ProcessProfilePolicy.ForSettingsRoot("c:/isolated/first/", Production));
        Assert.AreNotEqual(first, ProcessProfilePolicy.ForSettingsRoot(@"C:\isolated\second", Production));
    }
    [TestMethod]
    public void UncSpellingsNormalizeAndRelativePathsCannotElect()
    {
        Assert.AreEqual(ProcessProfilePolicy.ForSettingsRoot(@"\\server\share\profile", Production),
            ProcessProfilePolicy.ForSettingsRoot(@"\\?\UNC\server\share\profile", Production));
        Assert.Throws<InvalidDataException>(() => ProcessProfilePolicy.ForSettingsRoot("relative", Production));
    }
}
