using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class PlatformInputDiagnosticsTests
{
    [TestMethod]
    public async Task ShutdownDrainsSanitizedEventsAndPreservesPreviousRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WidgetRail-input-log-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "controller.log");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "previous process");
            using var log = new PlatformInputDiagnostics(path);
            log.Write("Guide accepted\r\nwindow visible");
            log.Write("Dispose");
            log.Dispose();
            log.Write("must not appear");
            await log.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNull(log.Failure);
            Assert.AreEqual("previous process", await File.ReadAllTextAsync(path + ".previous"));
            var lines = await File.ReadAllLinesAsync(path);
            Assert.HasCount(2, lines);
            StringAssert.Contains(lines[0], "Guide accepted  window visible");
            StringAssert.Contains(lines[0], "ticks=");
            StringAssert.EndsWith(lines[1], "Dispose");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task LargeMessagesAndEventBurstsKeepDiskBounded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WidgetRail-input-log-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "controller.log");
        const int limit = 64 * 1024;
        try
        {
            using var log = new PlatformInputDiagnostics(path, limit);
            // This is below channel capacity even if the background writer is
            // not scheduled. It still crosses several file-segment boundaries.
            for (var i = 0; i < 100; ++i) log.Write(new string('x', 20_000));
            log.Dispose();
            await log.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNull(log.Failure);
            Assert.HasCount(2, Directory.GetFiles(directory));
            foreach (var file in Directory.GetFiles(directory))
            {
                Assert.IsLessThanOrEqualTo(limit, new FileInfo(file).Length);
                foreach (var line in await File.ReadAllLinesAsync(file)) Assert.IsLessThan(4300, line.Length);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task UnwritableDestinationCannotThrowIntoInputOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), "WidgetRail-input-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            using var log = new PlatformInputDiagnostics(directory);
            log.Write("first");
            await log.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsNotNull(log.Failure);
            log.Write("after failure");
            log.Dispose();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
