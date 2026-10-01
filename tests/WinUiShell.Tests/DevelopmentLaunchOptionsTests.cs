using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class DevelopmentLaunchOptionsTests
{
    private static readonly string DefaultProfile = Path.GetFullPath("normal-profile");
    private static string[] Arguments(string root, bool inspector = false) =>
    [
        "--development-catalog-root=" + Path.Combine(root, "catalog"),
        "--settings-root=" + Path.Combine(root, "profile"),
        "--development-ready-path=" + Path.Combine(root, "ready.txt"),
        "--development-ready-nonce=" + new string('a', 64),
        "--development-widget-id=dev.example.widget",
        "--development-widget-instance=dev.example.widget.default",
        "--development-job-name=Local\\WidgetRail.Dev." + new string('a', 64),
        .. inspector ? new[] { "--development-inspector" } : Array.Empty<string>(),
    ];

    [TestMethod]
    public void DevelopmentUsesOnlyItsExplicitRootsAndWidget()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-dev-config-test");
        var options = OverlayLaunchConfiguration.Resolve(Arguments(root), Path.GetFullPath("payload"), DefaultProfile);
        Assert.AreEqual(Path.Combine(root, "catalog"), options.InstalledCatalogRoot);
        Assert.AreEqual(Path.Combine(root, "profile"), options.SettingsRoot);
        Assert.AreEqual("dev.example.widget", options.InitialWidgetId);
        Assert.IsNotNull(options.Development);
        Assert.IsNull(DevelopmentLaunchOptions.Parse([], DefaultProfile));
    }

    [TestMethod]
    public void PartialForeignOrConflictingGenerationCannotFallBackToNormalLaunch()
    {
        var args = Arguments(Path.Combine(Path.GetTempPath(), "wrail-dev-config-test"));
        foreach (var missing in Enumerable.Range(0, args.Length))
            Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse(
                args.Where((_, index) => index != missing).ToArray(), DefaultProfile));
        foreach (var conflict in new[] { "--shell-config=C:\\settings.json", "--widget=other", "--installed-catalog-root=C:\\catalog",
                     "--development-ready-nonce=" + new string('b', 64) })
            Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse([.. args, conflict], DefaultProfile));
        Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse(
            args.Select(value => value.StartsWith("--settings-root=", StringComparison.Ordinal) ? "--settings-root=" + DefaultProfile : value).ToArray(), DefaultProfile));
        Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse(
            args.Select(value => value.StartsWith("--development-job-name=", StringComparison.Ordinal) ? "--development-job-name=Local\\unrelated" : value).ToArray(), DefaultProfile));
        Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse(
            [.. args, "--development-probe-only", "--development-inspector"], DefaultProfile));
        foreach (var invalid in new[] { "--development-unknown", "--development-inspector=false", "--hidden" })
            Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse([.. args, invalid], DefaultProfile));
        Assert.ThrowsExactly<InvalidDataException>(() => DevelopmentLaunchOptions.Parse(
            [.. args, "--development-probe-only", "--development-probe-only"], DefaultProfile));
    }

    [TestMethod]
    public void ReadinessIsAtomicExactAndCannotClaimAnUninitializedInspector()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-dev-ready-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var launch = DevelopmentLaunchOptions.Parse(Arguments(root, inspector: true), DefaultProfile)!;
            Assert.ThrowsExactly<InvalidOperationException>(() => launch.PublishReady(false));
            Assert.IsFalse(File.Exists(launch.ReadyPath));
            launch.PublishReady(true);
            var expected = "wrail-dev-ready-v1\n" + launch.Nonce + "\n" + launch.CatalogRoot + "\n" +
                launch.WidgetId + "\n" + launch.InstanceId + "\ninspector-v1\n";
            Assert.AreEqual(expected, File.ReadAllText(launch.ReadyPath));
            Assert.ThrowsExactly<IOException>(() => launch.PublishReady(true));
            Assert.AreEqual(expected, File.ReadAllText(launch.ReadyPath));
            Assert.HasCount(1, Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
