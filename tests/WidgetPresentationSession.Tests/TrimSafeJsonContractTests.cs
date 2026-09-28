using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class TrimSafeJsonContractTests
{
    [TestMethod]
    public void SnapshotContractKeepsWireNamesEnumsAndStrictAdmission()
    {
        var snapshot = new ViewSnapshot
        {
            WidgetInstanceId = "fixture.default", Sequence = 7, ActiveInputScopeId = "root",
            Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack, Children =
                [new() { Id = "ready", Kind = ViewNodeKind.Button, Text = "Ready", ActionId = "ready" }] },
        };
        var bytes = SnapshotJson.Serialize(snapshot);
        using var json = JsonDocument.Parse(bytes);
        Assert.AreEqual("stack", json.RootElement.GetProperty("root").GetProperty("kind").GetString());
        Assert.AreEqual(7L, SnapshotJson.Deserialize(bytes).Sequence);
        Assert.ThrowsExactly<JsonException>(() => SnapshotJson.Deserialize("{\"unknown\":true}"u8));
    }

    [TestMethod]
    public void CompactSnapshotPreservesOmittedDefaultsWithoutAcceptingExplicitNull()
    {
        var snapshot = SnapshotJson.Deserialize("{\"sequence\":1,\"widgetInstanceId\":\"fixture.default\",\"activeInputScopeId\":\"root\",\"root\":{\"id\":\"root\",\"kind\":\"stack\"}}"u8);
        Assert.AreEqual(ProtocolConstants.CurrentVersion, snapshot.ProtocolVersion);
        Assert.AreEqual(0, snapshot.QuickActions.Count);
        Assert.AreEqual(0, snapshot.Root.Children.Count);
        Assert.ThrowsExactly<ProtocolValidationException>(() => SnapshotJson.Deserialize("{\"sequence\":1,\"widgetInstanceId\":\"fixture.default\",\"activeInputScopeId\":\"root\",\"root\":{\"id\":\"root\",\"kind\":\"stack\",\"children\":null}}"u8));
    }

    [TestMethod]
    public async Task DirectArtworkFramingPreservesBase64AndEnvelopeShape()
    {
        using var stream = new MemoryStream();
        var channel = new BridgeFrameChannel(stream, 65536);
        var payload = new BridgeEncodedArtworkEvent("widget", "art", "runtime", "presentation", "image/png", new byte[] { 1, 2, 3 }, "demand");
        await channel.WriteAsync(BridgeMessageTypes.Artwork, 11, payload, CancellationToken.None);
        var bytes = stream.ToArray();
        Assert.AreEqual(bytes.Length - sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(bytes));
        stream.Position = 0;
        var envelope = await channel.ReadAsync(CancellationToken.None);
        Assert.AreEqual(11L, envelope.RequestId);
        Assert.AreEqual("AQID", envelope.Payload.GetProperty("contentBase64").GetString());
        Assert.AreEqual("demand", envelope.Payload.GetProperty("demandId").GetString());
        var decoded = BridgeJson.FromElement<BridgeEncodedArtworkEvent>(envelope.Payload);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, decoded.ContentBase64.ToArray());
    }

    [TestMethod]
    public async Task OmittedEnvelopeVersionKeepsDefaultButExplicitInvalidVersionFails()
    {
        Assert.AreEqual(BridgeProtocol.CurrentVersion, (await Read("{\"type\":\"hello\",\"requestId\":1,\"payload\":{}}")).ProtocolVersion);
        await Assert.ThrowsExactlyAsync<BridgeProtocolException>(() => Read("{\"protocolVersion\":0,\"type\":\"hello\",\"requestId\":1,\"payload\":{}}"));
        static async Task<BridgeEnvelope> Read(string json)
        {
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            using var stream = new MemoryStream();
            var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
            stream.Write(prefix); stream.Write(payload); stream.Position = 0;
            return await new BridgeFrameChannel(stream, 65536).ReadAsync(CancellationToken.None);
        }
    }

    [TestMethod]
    public void BridgeMetadataRetainsEnumAndUnknownMemberRules()
    {
        Assert.AreEqual("\"visible\"", BridgeJson.ToElement(WidgetLifecycleState.Visible).GetRawText());
        Assert.AreEqual("{}", BridgeJson.ToElement(new BridgeEmptyPayload()).GetRawText());
        using var unexpected = JsonDocument.Parse("{\"widgetId\":\"widget\",\"unexpected\":true}");
        Assert.ThrowsExactly<JsonException>(() => BridgeJson.FromElement<WidgetIdRequest>(unexpected.RootElement));
        using var style = JsonDocument.Parse("{\"background\":{\"kind\":\"color\",\"text\":\"#123456\",\"number\":null,\"unit\":null}}");
        var decoded = BridgeJson.FromElement<Dictionary<string, BridgeComputedStyleValue>>(style.RootElement);
        Assert.IsNull(decoded["background"].Number);
        Assert.IsTrue(BridgeJson.ToElement(decoded).GetProperty("background").TryGetProperty("unit", out var unit));
        Assert.AreEqual(JsonValueKind.Null, unit.ValueKind);
    }

    [TestMethod]
    public void CatalogOmissionsRemainEmptyWhileExplicitNullStaysInvalid()
    {
        const string descriptor = "{\"id\":\"widget\",\"name\":\"Widget\",\"instanceId\":\"widget.default\",\"runtimeGeneration\":\"runtime\",\"presentationGeneration\":\"presentation\",\"icon\":\"settings\",\"packageContentDigest\":\"\"}";
        using var omitted = JsonDocument.Parse("[" + descriptor + "]");
        var value = BridgeJson.FromElement<BridgeWidgetDescriptor[]>(omitted.RootElement)[0];
        Assert.AreEqual(0, value.IconAssets.Count);
        Assert.AreEqual(0, value.QuickActions.Count);
        using var explicitNull = JsonDocument.Parse("[" + descriptor[..^1] + ",\"quickActions\":null}]");
        Assert.IsNull(BridgeJson.FromElement<BridgeWidgetDescriptor[]>(explicitNull.RootElement)[0].QuickActions);
    }

    [TestMethod]
    public async Task RealSessionUsesClosedHandshakeCatalogDisplayPaletteAndStopContracts()
    {
        await using var server = new ScriptedBridgeServer();
        var scenario = server.RunAuthenticatedAsync(async channel =>
        {
            var catalog = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual(BridgeMessageTypes.ListWidgets, catalog.Type);
            await Reply(channel, catalog, BridgeMessageTypes.Widgets, "{\"revision\":1,\"isComplete\":true,\"widgets\":[]}");
            var display = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual("monitor", display.Payload.GetProperty("display").GetProperty("id").GetString());
            await Reply(channel, display, BridgeMessageTypes.PlatformAppearance, "{\"activeDisplayId\":\"physical\"}");
            var palette = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual("{}", palette.Payload.GetRawText());
            await Reply(channel, palette, BridgeMessageTypes.PlatformAppearance, "{\"shellStyles\":{\"panel\":{\"background\":{\"kind\":\"color\",\"text\":\"#123456\",\"number\":null,\"unit\":null}}}}");
            var stop = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop, BridgeMessageTypes.Acknowledged, "{}");
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            Assert.AreEqual(0, (await session.ListWidgetsAsync()).Widgets.Count);
            Assert.AreEqual("physical", await session.ResolveDisplayAsync("monitor", "Display", ["MONITOR.A"]));
            Assert.AreEqual("#123456", (await session.ReadShellStylesAsync())["panel"].Base["background"].Text);
        }
        await scenario.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task Reply(BridgeFrameChannel channel, BridgeEnvelope request, string type, string json)
    {
        using var document = JsonDocument.Parse(json);
        await channel.WriteAsync(new BridgeEnvelope { Type = type, RequestId = request.RequestId, Payload = document.RootElement }, CancellationToken.None);
    }
}
