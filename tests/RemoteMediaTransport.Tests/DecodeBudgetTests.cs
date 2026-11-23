using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Media;

namespace WidgetRail.RemoteMediaTransport.Tests;

[TestClass]
public sealed class DecodeBudgetTests
{
    [TestMethod]
    public async Task ViewportBurstsWaitWithoutAcquiringBytesAndRetiredDemandCancels()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var queuedCancellation = new CancellationTokenSource();
        var leases = new List<IDisposable>();
        var waiting = new List<Task<IDisposable>>();
        try
        {
            for (var index = 0; index < 4; index++) leases.Add(await NativePublicMediaTransport.AdmitDecodeAsync(deadline.Token));
            for (var index = 0; index < 64; index++) waiting.Add(NativePublicMediaTransport.AdmitDecodeAsync(queuedCancellation.Token));
            Assert.IsTrue(waiting.All(task => !task.IsCompleted));
            queuedCancellation.Cancel();
            foreach (var request in waiting) await Assert.ThrowsAsync<OperationCanceledException>(async () => await request.WaitAsync(deadline.Token));
            var next = NativePublicMediaTransport.AdmitDecodeAsync(deadline.Token);
            Assert.IsFalse(next.IsCompleted);
            leases[0].Dispose(); leases[0].Dispose();
            using var resumed = await next.WaitAsync(deadline.Token);
            Assert.IsNotNull(resumed);
        }
        finally
        {
            queuedCancellation.Cancel();
            foreach (var lease in leases) lease.Dispose();
        }
        using var after = await NativePublicMediaTransport.AdmitDecodeAsync(deadline.Token);
        Assert.IsNotNull(after);
    }

    [TestMethod]
    public async Task AFullViewportBurstDrainsWithinFourActiveOperations()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var counts = new System.Collections.Concurrent.ConcurrentBag<int>();
        var active = 0;
        await Task.WhenAll(Enumerable.Range(0, 80).Select(async _ =>
        {
            using var lease = await NativePublicMediaTransport.AdmitDecodeAsync(deadline.Token);
            counts.Add(Interlocked.Increment(ref active));
            await Task.Yield();
            Interlocked.Decrement(ref active);
        })).WaitAsync(deadline.Token);
        Assert.AreEqual(80, counts.Count);
        Assert.IsTrue(counts.All(count => count is >= 1 and <= 4));
        Assert.AreEqual(0, active);
    }
}
