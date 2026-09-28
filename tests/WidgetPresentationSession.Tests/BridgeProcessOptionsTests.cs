using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetPresentationSession;

namespace WidgetPresentationSession.Tests;

[TestClass]
public sealed class BridgeProcessOptionsTests
{
    [TestMethod]
    public void LaunchUsesSeparateArgumentsAndExplicitProfiles()
    {
        var options = new BridgeProcessOptions(@"C:\candidate root", @"C:\isolated profile", @"C:\isolated catalog");
        var start = options.CreateStartInfo("WidgetRail.WinUI.test");
        Assert.IsFalse(start.UseShellExecute);
        Assert.IsTrue(start.CreateNoWindow);
        Assert.IsTrue(start.RedirectStandardError && start.RedirectStandardOutput);
        Assert.AreEqual(@"C:\candidate root\runtime\Bridge\WidgetBridge.exe", start.FileName);
        CollectionAssert.AreEqual(new[] { "--host-pipe", "WidgetRail.WinUI.test", "--catalog", @"C:\candidate root\widget-catalog.json",
            "--settings-root", @"C:\isolated profile", "--installed-catalog-root", @"C:\isolated catalog", "--accept-timeout-ms", "10000" }, start.ArgumentList.ToArray());
    }

    [TestMethod]
    public void ImplicitWorkingDirectoryProfilesAndUnboundedWaitsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new BridgeProcessOptions(@"C:\candidate", "relative", @"C:\catalog").CreateStartInfo("pipe"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => (new BridgeProcessOptions(@"C:\candidate", @"C:\profile", @"C:\catalog")
            { ConnectTimeout = TimeSpan.FromMinutes(2) }).CreateStartInfo("pipe"));
    }
}
