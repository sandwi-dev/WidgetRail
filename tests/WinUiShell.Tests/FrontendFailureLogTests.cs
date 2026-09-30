using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Diagnostics;

namespace WinUiShell.Tests;

[TestClass]
public sealed class FrontendFailureLogTests
{
    [TestMethod]
    public void FailureIsReadableImmediatelyAndKeepsOriginalExceptionAndSwitchContext()
    {
        WithPath(path =>
        {
            var log = new FrontendFailureLog(path);
            log.SetContext("phase=preparing active=spotify requested=audio-mixer");
            var error = new System.Runtime.InteropServices.COMException("Layout cycle detected", unchecked((int)0x88000fa8));
            log.Write("xaml-unhandled", error, "XAML failure");
            var text = File.ReadAllText(path);
            StringAssert.Contains(text, "hresult=0x88000FA8");
            StringAssert.Contains(text, "active=spotify requested=audio-mixer");
            StringAssert.Contains(text, "Layout cycle detected");
            StringAssert.Contains(text, "XAML failure");
            Assert.AreEqual(unchecked((int)0x88000fa8), error.HResult);
        });
    }

    [TestMethod]
    public void OversizedFailureAndRotationKeepDiskUseBounded()
    {
        WithPath(path =>
        {
            File.WriteAllText(path, new string('x', 512 * 1024));
            new FrontendFailureLog(path).Write("xaml-unhandled", new Exception(new string('z', 100000)));
            Assert.IsTrue(File.Exists(path + ".previous"));
            Assert.IsLessThan(33000L, new FileInfo(path).Length);
            StringAssert.Contains(File.ReadAllText(path), "[truncated]");
        });
    }

    [TestMethod]
    public void LargeLayoutSnapshotDoesNotDisplaceOriginalException()
    {
        WithPath(path =>
        {
            var log = new FrontendFailureLog(path);
            log.Write("xaml-unhandled", new Exception("original failure"));
            log.Write("xaml-layout", null, new string('x', 200000));
            var text = File.ReadAllText(path);
            StringAssert.Contains(text, "original failure");
            StringAssert.Contains(text, "source=xaml-layout");
            StringAssert.Contains(text, "[truncated]");
            Assert.IsLessThan(140000L, new FileInfo(path).Length);
        });
    }

    [TestMethod]
    public void UnavailableLogCannotReplaceOriginalFailure()
    {
        WithPath(path =>
        {
            File.WriteAllText(path, "occupied");
            new FrontendFailureLog(Path.Combine(path, "errors.log")).Write("xaml-unhandled", new Exception("original"));
            Assert.AreEqual("occupied", File.ReadAllText(path));
        });
    }

    private static void WithPath(Action<string> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "widgetrail-failure-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "errors.log");
        try { run(path); }
        finally
        {
            File.Delete(path); File.Delete(path + ".previous"); Directory.Delete(directory);
        }
    }
}
