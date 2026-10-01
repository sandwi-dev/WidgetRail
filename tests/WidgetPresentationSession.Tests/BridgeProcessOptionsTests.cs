using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetPresentationSession;

namespace WidgetPresentationSession.Tests;

[TestClass]
public sealed class BridgeProcessOptionsTests
{
    [TestMethod]
    public void BundledRuntimeIsSelectedOnlyInTheChildEnvironment()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-runtime-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(root, "dotnet");
        var inherited = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64");
        Directory.CreateDirectory(runtime);
        try
        {
            File.WriteAllBytes(Path.Combine(runtime, "dotnet.exe"), []);
            var start = new BridgeProcessOptions(root, root, root).CreateStartInfo("private-runtime");
            Assert.AreEqual(runtime, start.Environment["DOTNET_ROOT"]);
            Assert.AreEqual(runtime, start.Environment["DOTNET_ROOT_X64"]);
            Assert.AreEqual("0", start.Environment["DOTNET_MULTILEVEL_LOOKUP"]);
            Assert.AreEqual(inherited, Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void BrokenBundledRuntimeDoesNotSilentlySelectAGlobalRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "dotnet"));
        try { Assert.ThrowsExactly<FileNotFoundException>(() => new BridgeProcessOptions(root, root, root).CreateStartInfo("broken-runtime")); }
        finally { Directory.Delete(root, recursive: true); }
    }

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
