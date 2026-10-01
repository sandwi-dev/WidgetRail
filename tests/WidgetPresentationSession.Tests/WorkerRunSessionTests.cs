using WidgetRail.WidgetBridge;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    [TestMethod]
    [DataRow(0L, 1)]
    [DataRow(1L, 0)]
    public async Task InvalidWorkerRunCannotReplacePublishedFrame(long registry, int start)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel); await ReplyCatalogAsync(channel, list.RequestId, 1);
            var first = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, first.RequestId, Descriptor(), 8, workerRun: new(1, 1));
            var next = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, next.RequestId, Descriptor(), 9, workerRun: new(registry, start));
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var current = await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Visible);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.RefreshAsync(current.Authority));
            Assert.AreSame(current, session.GetState("session-widget")!.LastGood);
        }
        await serving.WaitAsync(TestDeadline);
    }

    [TestMethod]
    public async Task OlderReplyFromSameWorkerKeepsNewerFrameWithoutFailure()
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel); await ReplyCatalogAsync(channel, list.RequestId, 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, Descriptor(), 8, workerRun: new(1, 1));
            var refresh = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, refresh.RequestId, Descriptor(), 7, workerRun: new(1, 1));
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var current = await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive);
            var result = await session.RefreshAsync(current.Authority);
            Assert.AreSame(current, result);
            Assert.IsNull(session.GetState("session-widget")!.Failure);
        }
        await serving.WaitAsync(TestDeadline);
    }

    [TestMethod]
    public async Task RestartedWorkerAcceptsResetSequenceAndRetiresPreviousInputAuthority()
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel); await ReplyCatalogAsync(channel, list.RequestId, 1);
            var first = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, first.RequestId, Descriptor(), 90, workerRun: new(1, 1));
            var next = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, next.RequestId, Descriptor(), 1, workerRun: new(1, 2));
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var target = session.GetTarget("session-widget");
            var first = await session.EstablishPresentationAsync(target, WidgetLifecycleState.Visible);
            var next = await session.EstablishPresentationAsync(target, WidgetLifecycleState.Interactive);
            Assert.AreEqual(1L, next.Authority.SnapshotSequence);
            Assert.IsTrue(next.Authority.SessionGeneration > first.Authority.SessionGeneration);
            var failure = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.RefreshAsync(first.Authority));
            Assert.AreEqual("presentation_stale", failure.Code);
            Assert.AreSame(next, session.GetState("session-widget")!.LastGood);
        }
        await serving.WaitAsync(TestDeadline);
    }

    [TestMethod]
    [DataRow(true, 1L)]
    [DataRow(true, 3L)]
    [DataRow(false, 99L)]
    public async Task ConcurrentRestartResponsesCoalesceOnlyWithinAdmittedWorker(bool sameWorker, long delayedSequence)
    {
        await using var server = new ScriptedBridgeServer();
        var newerPublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel); await ReplyCatalogAsync(channel, list.RequestId, 1);
            var initial = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, initial.RequestId, Descriptor(), 20, workerRun: new(1, 1));
            var older = await ReadAsync(channel); firstReceived.TrySetResult();
            var newer = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, newer.RequestId, Descriptor(), 2, workerRun: new(1, 2));
            await newerPublished.Task.WaitAsync(TestDeadline);
            await ReplySnapshotAsync(channel, older.RequestId, Descriptor(), delayedSequence, workerRun: new(1, sameWorker ? 2 : 1));
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var target = session.GetTarget("session-widget");
            await session.EstablishPresentationAsync(target, WidgetLifecycleState.Visible);
            var older = session.EstablishPresentationAsync(target, WidgetLifecycleState.Interactive);
            await firstReceived.Task.WaitAsync(TestDeadline);
            var newer = await session.EstablishPresentationAsync(target, WidgetLifecycleState.Interactive);
            newerPublished.TrySetResult();
            if (sameWorker)
            {
                var delayed = await older;
                if (delayedSequence < 2) Assert.AreSame(newer, delayed);
                else
                {
                    Assert.AreEqual(delayedSequence, delayed.Authority.SnapshotSequence);
                    Assert.AreEqual(newer.Authority.SessionGeneration, delayed.Authority.SessionGeneration);
                    newer = delayed;
                }
            }
            else
            {
                var failure = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => older);
                Assert.AreEqual("presentation_stale", failure.Code);
            }
            Assert.AreSame(newer, session.GetState("session-widget")!.LastGood);
        }
        await serving.WaitAsync(TestDeadline);
    }
}
