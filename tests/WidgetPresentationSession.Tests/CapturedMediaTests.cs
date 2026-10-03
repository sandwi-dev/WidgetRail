using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    [TestMethod]
    public async Task CapturePresentationAcceptsCompatibleFramesButRejectsReplacementAndWorkerRestart()
    {
        var first = new CaptureAttachment(new string('a', 32), "image/png", 100, 100, 32, 0, DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds());
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await channel.ReadAsync(default);
            await ReplyCatalogAsync(channel, list.RequestId, 1);
            foreach (var (sequence, run, attachment) in new[] { (1L, 1, first), (2L, 1, first), (3L, 1, first with { Id = new string('b', 32) }), (1L, 2, first) })
            {
                var request = await channel.ReadAsync(default);
                var snapshot = new WidgetView(UI.Stack("root", UI.CapturedMedia(attachment, "capture")), "capture").CreateSnapshot(Descriptor().InstanceId, sequence);
                using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
                await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Snapshot, RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new { widgetId = Descriptor().Id, transactionKind = "ordinaryCheckpoint", baseSequence = 0,
                        recoveryOriginSequence = 0, workerRun = new BridgeWorkerRun(1, run), snapshot = json.RootElement.Clone(),
                        renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() }) }, default);
            }
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            async Task<WidgetPresentationFrame> Next() => (await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive));
            var frame = await Next();
            Assert.AreEqual(first, session.ResolveCapturedMedia(frame, "capture"));
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveCapturedMedia(frame with { }, "capture"));
            await Next();
            Assert.AreEqual(first, session.ResolveCapturedMedia(frame, "capture"));
            await Next();
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveCapturedMedia(frame, "capture"));
            var restarted = await Next();
            Assert.AreEqual(first, session.ResolveCapturedMedia(restarted, "capture"));
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveCapturedMedia(frame, "capture"));
        }
        await serverTask.WaitAsync(TestDeadline);
    }
    [TestMethod]
    public async Task MediaPlayerPresentationAcceptsCompatibleFramesButRejectsReplacementAndWorkerRestart()
    {
        var first = MediaPlayerSource.WebUrl("https://example.com/video.mp4");
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await channel.ReadAsync(default);
            await ReplyCatalogAsync(channel, list.RequestId, 1);
            foreach (var (sequence, run, attachment) in new[] { (1L, 1, first), (2L, 1, first), (3L, 1, MediaPlayerSource.WebUrl("https://example.com/replaced.mp4")), (1L, 2, first) })
            {
                var request = await channel.ReadAsync(default);
                var snapshot = new WidgetView(UI.Stack("root", UI.MediaPlayer(attachment, "capture")), "capture").CreateSnapshot(Descriptor().InstanceId, sequence);
                using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
                await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Snapshot, RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new { widgetId = Descriptor().Id, transactionKind = "ordinaryCheckpoint", baseSequence = 0,
                        recoveryOriginSequence = 0, workerRun = new BridgeWorkerRun(1, run), snapshot = json.RootElement.Clone(),
                        renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() }) }, default);
            }
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            async Task<WidgetPresentationFrame> Next() => (await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive));
            var frame = await Next();
            Assert.AreEqual(first, session.ResolveMediaPlayer(frame, "capture").Source);
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveMediaPlayer(frame with { }, "capture"));
            await Next();
            Assert.AreEqual(first, session.ResolveMediaPlayer(frame, "capture").Source);
            await Next();
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveMediaPlayer(frame, "capture"));
            var restarted = await Next();
            Assert.AreEqual(first, session.ResolveMediaPlayer(restarted, "capture").Source);
            Assert.Throws<WidgetPresentationSessionException>(() => session.ResolveMediaPlayer(frame, "capture"));
        }
        await serverTask.WaitAsync(TestDeadline);
    }
}
