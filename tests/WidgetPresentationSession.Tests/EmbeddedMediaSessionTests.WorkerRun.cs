using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class EmbeddedMediaSessionTests
{
    [TestMethod]
    public async Task PlaybackEventsCarryWorkerReceiptAndRetireOldDocumentWhenSequencesCollide()
    {
        var firstRun = new BridgeWorkerRun(1, 1);
        var replacementRun = new BridgeWorkerRun(1, 2);
        await Run(async channel =>
        {
            var request = await Read(channel);
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel);
            await Snapshot(channel, request.RequestId, Media, 2, workerRun: firstRun);
            request = await Read(channel);
            Assert.AreEqual(1L, request.Payload.GetProperty("sequence").GetInt64());
            Assert.AreEqual(firstRun, BridgeJson.FromElement<BridgeEmbeddedMediaPlaybackEventRequest>(request.Payload).WorkerRun);
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            request = await Read(channel);
            Assert.AreEqual(2L, request.Payload.GetProperty("sequence").GetInt64());
            Assert.AreEqual(firstRun, BridgeJson.FromElement<BridgeEmbeddedMediaPlaybackEventRequest>(request.Payload).WorkerRun);
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            request = await Read(channel);
            await Snapshot(channel, request.RequestId, Media, 1, workerRun: replacementRun);
            request = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.ResolveEmbeddedMedia, request.Type, "The retired document must send no observation.");
            await Reply(channel, request.RequestId, BridgeMessageTypes.EmbeddedMediaSession, Bundle());
            request = await Read(channel);
            Assert.AreEqual(replacementRun, BridgeJson.FromElement<BridgeEmbeddedMediaPlaybackEventRequest>(request.Payload).WorkerRun);
            Assert.AreEqual(1L, request.Payload.GetProperty("sequence").GetInt64());
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var document = await session.ResolveEmbeddedMediaAsync(frame.Authority);
            await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Visible);
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation);
            await session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 2, CommandSequence = 0 });
            var replacement = await session.EstablishPresentationAsync(session.GetTarget("media"), WidgetLifecycleState.Visible);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() =>
                session.SendEmbeddedMediaPlaybackEventAsync(document, Observation with { Sequence = 3, CommandSequence = 0 }));
            var nextDocument = await session.ResolveEmbeddedMediaAsync(replacement.Authority);
            await session.SendEmbeddedMediaPlaybackEventAsync(nextDocument, Observation);
        }, workerRun: firstRun);
    }
}
