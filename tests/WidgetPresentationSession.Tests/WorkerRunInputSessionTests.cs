using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    [TestMethod]
    public async Task InputEchoesExactSnapshotWorkerRun()
    {
        var run = new BridgeWorkerRun(11, 3);
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel); await ReplyCatalogAsync(channel, list.RequestId, 1);
            var establish = await ReadAsync(channel);
            await ReplySnapshotAsync(channel, establish.RequestId, Descriptor(), 7, workerRun: run);
            var action = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.Action, action.Type);
            Assert.AreEqual(run, action.Payload.GetProperty("workerRun").Deserialize<BridgeWorkerRun>(BridgeJson.Options));
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged, RequestId = action.RequestId,
                Payload = BridgeJson.ToElement(new { admission = WidgetOperationAdmission.Enqueued }) }, default);
            var input = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.ControllerInput, input.Type);
            Assert.AreEqual(run, input.Payload.GetProperty("workerRun").Deserialize<BridgeWorkerRun>(BridgeJson.Options));
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.ControllerInputResult, RequestId = input.RequestId,
                Payload = BridgeJson.ToElement(new { handled = true }) }, default);
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var frame = await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive);
            Assert.AreEqual(run, frame.Authority.WorkerRun);
            await session.SendActionAsync(frame.Authority, new("action", "root", InputScopeId: "root"));
            Assert.IsTrue(await session.SendControllerInputAsync(frame.Authority, new(ControllerButton.A,
                ControllerEventPhase.Pressed, ControllerInputContext.OpenWidget, ActiveInputScopeId: "root", SnapshotSequence: 7)));
        }
        await serving.WaitAsync(TestDeadline);
    }
}
