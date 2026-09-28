using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class BridgeLifetimeTests
{
    [TestMethod]
    public async Task ShutdownInterruptsBlockedWriteGateAndCapacityAndDrainsConcurrentDisposals()
    {
        await using var server = new ScriptedBridgeServer();
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(_ => releaseServer.Task);
        var transport = await BridgePresentationTransport.ConnectAsync(server.PipeName,
            new() { MaximumPendingRequests = 2 }, CancellationToken.None);
        transport.Start();
        try
        {
            // Far larger than the server's 4 KiB pipe buffer. The server deliberately
            // never drains it: second request waits for the write gate, third for capacity.
            var writer = transport.RequestAsync("blocked", new { data = new string('x', 512 * 1024) }, CancellationToken.None);
            var gateWaiter = transport.RequestAsync("gate", new { }, CancellationToken.None);
            var capacityWaiter = transport.RequestAsync("capacity", new { }, CancellationToken.None);
            await Task.Delay(100);
            Assert.IsFalse(writer.IsCompleted);
            Assert.IsFalse(gateWaiter.IsCompleted);
            Assert.IsFalse(capacityWaiter.IsCompleted);

            var disposal = transport.DisposeAsync().AsTask();
            var concurrentDisposal = transport.DisposeAsync().AsTask();
            Assert.AreSame(disposal, concurrentDisposal);
            await Task.WhenAll(disposal, concurrentDisposal).WaitAsync(TimeSpan.FromSeconds(8));
            foreach (var request in new[] { writer, gateWaiter, capacityWaiter })
            {
                // Resource ownership is drained before disposal returns; allow the
                // async method's final Task completion to run on its scheduling turn.
                try { await request.WaitAsync(TimeSpan.FromSeconds(1)); Assert.Fail("A blocked request unexpectedly succeeded."); }
                catch (Exception error) when (error is not AssertFailedException)
                {
                    Assert.IsFalse(error is TimeoutException or ObjectDisposedException,
                        "Shared synchronization was disposed before its request drained.");
                }
            }
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
                transport.RequestAsync("late", new { }, CancellationToken.None));
        }
        finally
        {
            releaseServer.TrySetResult();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            await transport.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task GracefulShutdownStillAcknowledgesStopAndCompletesExistingResponse()
    {
        await using var server = new ScriptedBridgeServer();
        var requestRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var request = await channel.ReadAsync(CancellationToken.None);
            requestRead.SetResult();
            var stop = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            foreach (var pending in new[] { request, stop })
                await channel.WriteAsync(new BridgeEnvelope
                {
                    Type = BridgeMessageTypes.Acknowledged, RequestId = pending.RequestId,
                    Payload = BridgeJson.ToElement(new { }),
                }, CancellationToken.None);
        });
        await using var transport = await BridgePresentationTransport.ConnectAsync(server.PipeName,
            new(), CancellationToken.None);
        transport.Start();
        var request = transport.RequestAsync("work", new { }, CancellationToken.None);
        await requestRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await transport.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(BridgeMessageTypes.Acknowledged, (await request).Type);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task PeerExitReleasesRequestsWaitingForAdmission()
    {
        await using var server = new ScriptedBridgeServer();
        var requestRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseServer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await channel.ReadAsync(CancellationToken.None);
            requestRead.SetResult();
            await releaseServer.Task;
        });
        await using var transport = await BridgePresentationTransport.ConnectAsync(server.PipeName,
            new() { MaximumPendingRequests = 1 }, CancellationToken.None);
        transport.Start();
        var first = transport.RequestAsync("first", new { }, CancellationToken.None);
        await requestRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = transport.RequestAsync("queued", new { }, CancellationToken.None);
        releaseServer.SetResult();
        await serverTask;
        await server.DisposeAsync();
        foreach (var task in new[] { first, queued })
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Fail("Disconnected request succeeded."); }
            catch (Exception error) when (error is not AssertFailedException)
            {
                Assert.IsFalse(error is TimeoutException or ObjectDisposedException);
            }
        }
    }

    [TestMethod]
    public async Task FailedStartRetainsOriginalCancellationWhenCleanupFails()
    {
        var original = new OperationCanceledException("startup cancelled");
        var cleanupFailure = new TimeoutException("child cleanup timed out");
        var caught = await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            try { throw original; }
            catch (Exception error)
            {
                await OwnedBridgeProcess.CleanupAfterFailedStartAsync(error,
                    () => ValueTask.FromException(cleanupFailure));
                throw;
            }
        });
        Assert.AreSame(original, caught);
        Assert.AreSame(cleanupFailure, caught.Data["BridgeCleanupFailure"]);
    }
}
