using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    [TestMethod]
    public async Task ProviderContentSurvivesInteractionModeChangeButRejectsRetiredWorker()
    {
        var reference = new ProviderDocumentReference(new string('a', 32), ProviderDocumentReference.RestrictedHtml);
        var document = new WebBrowserDocument("provider.fixture", 1, reference.Url, "Search suggestions")
        { ProviderDocument = reference, InteractionMode = BrowserInteractionMode.ActivateToInteract };
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await channel.ReadAsync(default); await ReplyCatalogAsync(channel, list.RequestId, 1);
            await Snapshot(1, 1, BrowserInteractionMode.ActivateToInteract);
            await Resolve();
            await Snapshot(2, 1, BrowserInteractionMode.InteractOnFocus);
            await Resolve();
            await Snapshot(1, 2, BrowserInteractionMode.ActivateToInteract);
            await ExpectStopAsync(channel);
            async Task Snapshot(long sequence, int run, BrowserInteractionMode mode)
            {
                var request = await channel.ReadAsync(default);
                var snapshot = new WidgetView(UI.Stack("root", UI.WebBrowser(document, mode, "provider")), "provider").CreateSnapshot(Descriptor().InstanceId, sequence);
                using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
                await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Snapshot, RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new { widgetId = Descriptor().Id, transactionKind = "ordinaryCheckpoint", baseSequence = 0,
                        recoveryOriginSequence = 0, workerRun = new BridgeWorkerRun(1, run), snapshot = json.RootElement.Clone(),
                        renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() }) }, default);
            }
            async Task Resolve()
            {
                var request = await channel.ReadAsync(default);
                Assert.AreEqual(BridgeMessageTypes.ResolveProviderDocument, request.Type);
                Assert.AreEqual(reference, BridgeJson.FromElement<BridgeProviderDocumentRequest>(request.Payload).Reference);
                await channel.WriteAsync(new BridgeEnvelope { Type = request.Type, RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new BridgeProviderDocument(reference, ["<div>Original attribution</div>"])) }, default);
            }
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            var original = await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive);
            Assert.AreEqual(reference, (await session.ResolveProviderDocumentAsync(original.Authority, document)).Reference);
            await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive);
            Assert.AreEqual(reference, (await session.ResolveProviderDocumentAsync(original.Authority, document)).Reference);
            await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Interactive);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ResolveProviderDocumentAsync(original.Authority, document));
        }
        await serverTask.WaitAsync(TestDeadline);
    }
}
