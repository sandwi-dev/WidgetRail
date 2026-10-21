using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class ApplicationRestartTests
{
    [TestMethod]
    public void SilentRestartPreservesProfileAndSelectionButRemovesVisibleLaunchFlags()
    {
        string[] launch = ["--show", "--installation-root=C:\\WidgetRail", "--settings-root=C:\\Profile", "--widget=settings", "--hidden", "--show"];
        CollectionAssert.AreEqual(new[] { "--installation-root=C:\\WidgetRail", "--settings-root=C:\\Profile", "--widget=settings", "--hidden" },
            ApplicationRestart.Arguments(launch, ApplicationRestartMode.Hidden));
    }

    [TestMethod]
    public void NormalRestartAfterSilentRestartOpensOverlay()
    {
        var hidden = ApplicationRestart.Arguments(["--shell-config=C:\\candidate.json"], ApplicationRestartMode.Hidden);
        CollectionAssert.AreEqual(new[] { "--shell-config=C:\\candidate.json", "--show" },
            ApplicationRestart.Arguments(hidden, ApplicationRestartMode.Visible));
    }
}
