using WidgetRail.WidgetBridge;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IntentDestinationAllowsLoadingScopeChangeButRejectsWorkerReplacement(bool replaceWorker)
    {
        var run = new BridgeWorkerRun(1, 1);
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel); await ReplyCatalogAsync(channel, list.RequestId, 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, Descriptor(), 1, "loading", run);
            var refresh = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, refresh.RequestId, Descriptor(), 2, "ready", replaceWorker ? new(1, 2) : run);
            if (!replaceWorker)
            {
                var commit = await ReadAsync(channel);
                Assert.AreEqual(BridgeMessageTypes.CommitIntent, commit.Type);
                var request = BridgeJson.FromElement<BridgeIntentCommitRequest>(commit.Payload);
                Assert.AreEqual("session-widget", request.TargetWidgetId);
                Assert.AreEqual(run, request.TargetWorkerRun);
                await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.IntentCompleted, RequestId = commit.RequestId,
                    Payload = BridgeJson.ToElement(new WidgetIntentCompletion(true)) }, default);
            }
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var loading = await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive);
            await session.RefreshAsync(loading.Authority);
            var prepared = new WidgetIntentPreparation("source", Guid.NewGuid().ToString("N"), WidgetIntentLaunchKind.Widget,
                [new("session-widget", "Destination")]);
            if (replaceWorker)
                await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.CommitIntentAsync(prepared, loading));
            else
                Assert.IsTrue((await session.CommitIntentAsync(prepared, loading)).Accepted);
        }
        await serving.WaitAsync(TestDeadline);
    }
}
