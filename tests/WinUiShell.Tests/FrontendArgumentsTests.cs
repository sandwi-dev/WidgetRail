using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Shell;

namespace WinUiShell.Tests;

[TestClass]
public sealed class FrontendArgumentsTests
{
    [TestMethod]
    public void PackagedActivationRetainsQuotedPaths()
    {
        var arguments = FrontendArguments.Parse("--shell-config=\"C:\\Users\\Some User\\shell options.json\" --shell-no-controller", []);
        Assert.AreEqual("C:\\Users\\Some User\\shell options.json", FrontendArguments.Value(arguments, "--shell-config"));
        CollectionAssert.Contains(arguments, "--shell-no-controller");
    }

    [TestMethod]
    public void AlreadyTokenizedPathsAreNotSplitAgain()
    {
        var arguments = FrontendArguments.Parse("", ["--shell-config=C:\\Some User\\options.json", "--validate-indexed"]);
        Assert.AreEqual("C:\\Some User\\options.json", FrontendArguments.Value(arguments, "--shell-config"));
        Assert.HasCount(2, arguments);
    }

    [TestMethod]
    public void DuplicateActivationSourcesDoNotDuplicateModes()
    {
        var arguments = FrontendArguments.Parse("--validate-indexed --shell-no-controller", ["--validate-indexed"]);
        Assert.HasCount(2, arguments);
    }

    [TestMethod]
    public void TrailingBackslashesAndLiteralQuotesUseWindowsRules()
    {
        var arguments = FrontendArguments.Parse("--path=\"C:\\Some User\\\\\" --name=\"a\\\"b\"", []);
        Assert.AreEqual("C:\\Some User\\", FrontendArguments.Value(arguments, "--path"));
        Assert.AreEqual("a\"b", FrontendArguments.Value(arguments, "--name"));
    }

    [TestMethod]
    public void OptionsSupportCaseInsensitiveSerializedPropertiesAndOptionalInitialWidget()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"installationRoot\":\"C:/Install\",\"settingsRoot\":\"C:/Settings\",\"installedCatalogRoot\":\"C:/Catalog\"}");
            var options = OverlayShellOptions.Load(path);
            Assert.AreEqual("C:/Install", options.InstallationRoot);
            Assert.IsNull(options.InitialWidgetId);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void OptionsRejectRelativeRootBeforeAnyBridgeStarts()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"InstallationRoot\":\"relative\",\"SettingsRoot\":\"C:/Settings\",\"InstalledCatalogRoot\":\"C:/Catalog\"}");
            Assert.Throws<InvalidDataException>(() => OverlayShellOptions.Load(path));
        }
        finally { File.Delete(path); }
    }
}
