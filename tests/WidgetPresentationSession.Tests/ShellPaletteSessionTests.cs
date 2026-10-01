using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class ShellPaletteSessionTests
{
    [TestMethod]
    public async Task ShellPaletteUsesReadOnlyAppearanceRequestAndRejectsForeignRoles()
    {
        await using var server = new ScriptedBridgeServer();
        var task = server.RunAuthenticatedAsync(async channel =>
        {
            foreach (var json in new[]
            {
                "{\"shellStyles\":{\"panel\":{\"background\":{\"kind\":\"color\",\"text\":\"rgba(20,30,40,0.5)\",\"number\":null,\"unit\":null}}}}",
                "{\"shellStyles\":{\"arbitrary\":{}}}",
                "{\"shellStyles\":{\"panel\":{},\"panel\":{}}}",
                "{}",
            })
            {
                var request = await channel.ReadAsync(CancellationToken.None);
                Assert.AreEqual(BridgeMessageTypes.GetPlatformAppearance, request.Type);
                Assert.AreEqual(0, request.Payload.EnumerateObject().Count());
                await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.PlatformAppearance, RequestId = request.RequestId,
                    Payload = JsonDocument.Parse(json).RootElement.Clone() }, CancellationToken.None);
            }
            var stop = await channel.ReadAsync(CancellationToken.None);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged, RequestId = stop.RequestId,
                Payload = BridgeJson.ToElement(new { }) }, CancellationToken.None);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var styles = await session.ReadShellStylesAsync();
            Assert.AreEqual("rgba(20,30,40,0.5)", styles["panel"].Base["background"].Text);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.ReadShellStylesAsync());
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.ReadShellStylesAsync());
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.ReadShellStylesAsync());
        }
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
