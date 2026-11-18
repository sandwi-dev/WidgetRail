using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class CaptureReplaySessionTests
{
    [TestMethod]
    public async Task SaveRejectsAReceiptForAnotherRequest()
    {
        await using var server = new ScriptedBridgeServer();
        var service = server.RunAuthenticatedAsync(async channel =>
        {
            var request = await channel.ReadAsync(default);
            Assert.AreEqual(BridgeMessageTypes.SaveReplay, request.Type);
            var save = BridgeJson.FromElement<CaptureReplaySaveRequest>(request.Payload);
            var result = new CaptureReplaySaveResult(CaptureReplayOutcome.Unavailable, Guid.NewGuid().ToString("N"), Issue: CaptureReplayIssue.BufferNotReady);
            Assert.AreNotEqual(save.RequestId, result.RequestId);
            await channel.WriteAsync(new BridgeEnvelope { Type = request.Type, RequestId = request.RequestId, Payload = BridgeJson.ToElement(result) }, default);
            var stop = await channel.ReadAsync(default);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged, RequestId = stop.RequestId, Payload = BridgeJson.ToElement(new { }) }, default);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => session.SaveReplayAsync(Guid.NewGuid().ToString("N")));
        await service.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task StatusCacheAndEventsRejectOlderRevisions()
    {
        await using var server = new ScriptedBridgeServer();
        var service = server.RunAuthenticatedAsync(async channel =>
        {
            foreach (var revision in new long[] { 2, 1, 3 })
            {
                var request = await channel.ReadAsync(default);
                Assert.AreEqual(BridgeMessageTypes.GetReplayStatus, request.Type);
                await channel.WriteAsync(new BridgeEnvelope { Type = request.Type, RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new CaptureReplayStatus(revision, 0, new(), CaptureReplayPhase.Off)) }, default);
            }
            var stop = await channel.ReadAsync(default);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged, RequestId = stop.RequestId, Payload = BridgeJson.ToElement(new { }) }, default);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var revisions = new List<long>(); session.ReplayStatusChanged += value => revisions.Add(value.Revision);
            await session.GetReplayStatusAsync(); await session.GetReplayStatusAsync();
            Assert.AreEqual(2L, session.ReplayStatus!.Revision);
            await session.GetReplayStatusAsync();
            CollectionAssert.AreEqual(new long[] { 2, 3 }, revisions);
        }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
