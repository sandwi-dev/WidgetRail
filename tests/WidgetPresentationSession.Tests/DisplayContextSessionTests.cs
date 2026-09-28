using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class DisplayContextSessionTests
{
    [TestMethod]
    public async Task DisplayReportUsesExistingRequestAndBridgeResolvedPhysicalIdentity()
    {
        await using var server = new ScriptedBridgeServer();
        var task = server.RunAuthenticatedAsync(async channel =>
        {
            var request = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual(BridgeMessageTypes.GetPlatformAppearance, request.Type);
            var display = request.Payload.GetProperty("display");
            Assert.AreEqual("connection", display.GetProperty("id").GetString());
            Assert.AreEqual("Display", display.GetProperty("name").GetString());
            Assert.AreEqual("MONITOR.A", display.GetProperty("devicePaths")[0].GetString());
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.PlatformAppearance, RequestId = request.RequestId,
                Payload = BridgeJson.ToElement(new { activeDisplayId = "physical.identity" }) }, CancellationToken.None);
            var stop = await channel.ReadAsync(CancellationToken.None);
            Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged, RequestId = stop.RequestId, Payload = BridgeJson.ToElement(new { }) }, CancellationToken.None);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            Assert.AreEqual("physical.identity", await session.ResolveDisplayAsync("connection", "Display", ["MONITOR.A"]));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ResolveDisplayAsync("id", "Display", ["path", "PATH"]));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ResolveDisplayAsync("id", "Display", []));
            await Assert.ThrowsAsync<ArgumentException>(() => session.ResolveDisplayAsync("id", "bad\nname", ["path"]));
        }
        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
