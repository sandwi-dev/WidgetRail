using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Previews;

namespace OverlayPlatformClient.Tests;

[TestClass]
public sealed class PreviewCloseWatchdogTests
{
    [TestMethod]
    public void NativeHealthMatchesStableAbi()
    {
        Assert.AreEqual(56, Marshal.SizeOf<NativePreviewHealth>());
        Assert.AreEqual((nint)24, Marshal.OffsetOf<NativePreviewHealth>(nameof(NativePreviewHealth.Window)));
        Assert.AreEqual((nint)48, Marshal.OffsetOf<NativePreviewHealth>(nameof(NativePreviewHealth.StartedAt)));
    }
    [TestMethod]
    public void SlowPhaseReportsOnceAndCompletionPreservesSource()
    {
        var watchdog = new PreviewCloseWatchdog();
        var state = NativePreviewHealth.Empty;
        state.Phase = 2; state.StartedAt = Stopwatch.Frequency; state.Slot = 12; state.Window = 456; state.ProcessId = 789;
        Assert.IsNull(watchdog.Observe(state, state.StartedAt));
        var slow = watchdog.Observe(state, state.StartedAt + 2 * Stopwatch.Frequency);
        Assert.IsNotNull(slow); StringAssert.Contains(slow, "status=slow phase=close-session");
        StringAssert.Contains(slow, "sourcePid=789 sourceHwnd=456");
        Assert.IsNull(watchdog.Observe(state, state.StartedAt + 9 * Stopwatch.Frequency));
        state.Phase = 0;
        StringAssert.Contains(watchdog.Observe(state, state.StartedAt + 10 * Stopwatch.Frequency)!, "status=completed phase=close-session");
        Assert.IsNull(watchdog.Observe(state, state.StartedAt + 11 * Stopwatch.Frequency));
    }
    [TestMethod]
    public void NextPhaseOrSourceGetsItsOwnBoundedReport()
    {
        var watchdog = new PreviewCloseWatchdog();
        var state = NativePreviewHealth.Empty;
        Assert.IsNull(watchdog.Observe(state, 100));
        state.Phase = 1; state.StartedAt = 5 * Stopwatch.Frequency;
        Assert.IsNull(watchdog.Observe(state, state.StartedAt - 1));
        Assert.IsNotNull(watchdog.Observe(state, state.StartedAt + 2 * Stopwatch.Frequency));
        state.Phase = 3; state.StartedAt += 3 * Stopwatch.Frequency;
        StringAssert.Contains(watchdog.Observe(state, state.StartedAt)!, "completed");
        Assert.IsNull(watchdog.Observe(state, state.StartedAt + Stopwatch.Frequency));
        StringAssert.Contains(watchdog.Observe(state, state.StartedAt + 2 * Stopwatch.Frequency)!, "phase=close-pool");
    }
}
