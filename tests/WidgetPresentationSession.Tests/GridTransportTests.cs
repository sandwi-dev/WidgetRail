using System.Text;
using System.Text.Json;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class SessionTransportTests
{
    // Hand-authored wire input exercises the actual admission boundary, including
    // an old-version sender that cannot be produced by SnapshotJson.Serialize.
    private const string GridWireRoot = """
        {"id":"root","kind":"grid","gridLayout":{
          "rows":[{"sizing":"pixel","value":80}],
          "columns":[{"sizing":"star","value":3,"minimum":80,"maximum":640},{"sizing":"star","value":1}],
          "rowSpacing":8,"columnSpacing":12},
          "children":[{"id":"play","kind":"button","text":"Play","actionId":"play",
            "gridCell":{"row":0,"column":1,"rowSpan":1,"columnSpan":1}}]}
        """;

    [TestMethod]
    [DataRow(64)]
    [DataRow(63)]
    public async Task GridCheckpointUsesTheSameVersionAdmissionAsTheWorker(int version)
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            var list = await ReadAsync(channel);
            await ReplyCatalogAsync(channel, list.RequestId, 1);
            var establish = await ReadAsync(channel);
            using var snapshot = JsonDocument.Parse(GridSnapshotWire(version));
            await channel.WriteAsync(new BridgeEnvelope
            {
                Type = BridgeMessageTypes.Snapshot,
                RequestId = establish.RequestId,
                Payload = BridgeJson.ToElement(new
                {
                    widgetId = "session-widget", transactionKind = "ordinaryCheckpoint",
                    baseSequence = 0, recoveryOriginSequence = 0,
                    snapshot = snapshot.RootElement.Clone(), renderStyles = new Dictionary<string, BridgeNodeRenderStyles>(),
                }),
            }, CancellationToken.None);
            await ExpectStopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, Options()))
        {
            await session.ListWidgetsAsync();
            if (version < 64)
            {
                await Assert.ThrowsExactlyAsync<ProtocolValidationException>(() =>
                    session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Visible));
                Assert.IsNull(session.GetState("session-widget")?.LastGood);
            }
            else
            {
                var frame = await session.EstablishPresentationAsync(session.GetTarget("session-widget"), WidgetLifecycleState.Visible);
                Assert.AreEqual(64, frame.Snapshot.ProtocolVersion);
                using var admitted = JsonDocument.Parse(SnapshotJson.Serialize(frame.Snapshot));
                AssertGridWire(admitted.RootElement.GetProperty("root"));
            }
        }
        await serverTask.WaitAsync(TestDeadline);
    }

    [TestMethod]
    public async Task GridIndexedFragmentsSurviveGeneratedBridgeMetadataAndFraming()
    {
        var root = SnapshotJson.Deserialize(Encoding.UTF8.GetBytes(GridSnapshotWire(64))).Root;
        var item = new ViewNode { Id = "item.0", Kind = ViewNodeKind.ActionSurface, ActionId = "open",
            CollectionItemKey = "item.0", Children = [root] };
        var range = new IndexedCollectionRange("session.instance", "items", new("items", 1, 0, 1), "scope", 0,
            Guid.NewGuid().ToString("N"), [new("item.0", item)]);
        var payload = new BridgeIndexedRangeResponse("session-widget", "session.instance", "runtime", "presentation", range);
        using var wire = new MemoryStream();
        var channel = new BridgeFrameChannel(wire, 64 * 1024);
        await channel.WriteAsync(BridgeMessageTypes.IndexedRange, 7, payload, CancellationToken.None);
        wire.Position = 0;
        var reply = await channel.ReadAsync(CancellationToken.None);
        var decoded = BridgeJson.FromElement<BridgeIndexedRangeResponse>(reply.Payload);
        Assert.AreEqual(7L, reply.RequestId);
        Assert.AreEqual(range.DemandId, decoded.Range.DemandId);
        AssertGridWire(BridgeJson.ToElement(decoded).GetProperty("range").GetProperty("items")[0]
            .GetProperty("root").GetProperty("children")[0]);
    }

    private static string GridSnapshotWire(int version) =>
        "{\"protocolVersion\":" + version + ",\"sequence\":1,\"widgetInstanceId\":\"session.instance\",\"activeInputScopeId\":\"root\",\"root\":" + GridWireRoot + "}";

    private static void AssertGridWire(JsonElement root)
    {
        var layout = root.GetProperty("gridLayout");
        Assert.AreEqual(80d, layout.GetProperty("rows")[0].GetProperty("value").GetDouble());
        Assert.AreEqual(3d, layout.GetProperty("columns")[0].GetProperty("value").GetDouble());
        Assert.AreEqual(80d, layout.GetProperty("columns")[0].GetProperty("minimum").GetDouble());
        Assert.AreEqual(640d, layout.GetProperty("columns")[0].GetProperty("maximum").GetDouble());
        Assert.AreEqual(12d, layout.GetProperty("columnSpacing").GetDouble());
        var cell = root.GetProperty("children")[0].GetProperty("gridCell");
        Assert.AreEqual(1, cell.GetProperty("column").GetInt32());
        Assert.AreEqual(1, cell.GetProperty("columnSpan").GetInt32());
    }
}
