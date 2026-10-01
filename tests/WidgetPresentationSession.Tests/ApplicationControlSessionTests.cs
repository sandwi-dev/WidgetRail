using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class ApplicationControlSessionTests
{
    [TestMethod]
    public async Task HostReadsQuitAndRestartWithoutRequiringAnyWidgetPresentation()
    {
        await using var server = new ScriptedBridgeServer();
        var service = server.RunAuthenticatedAsync(async channel =>
        {
            foreach (var action in new[] { 0, 1, 0, 2 })
            {
                var request = await channel.ReadAsync(default);
                Assert.AreEqual(BridgeMessageTypes.ApplicationControl, request.Type);
                Assert.AreEqual(0, request.Payload.EnumerateObject().Count());
                await channel.WriteAsync(new BridgeEnvelope { Type = request.Type, RequestId = request.RequestId,
                    Payload = BridgeJson.ToElement(new { action }) }, default);
            }
            var stop = await channel.ReadAsync(default);
            Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged,
                RequestId = stop.RequestId, Payload = BridgeJson.ToElement(new { }) }, default);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            foreach (var expected in new[] { WidgetApplicationControl.None, WidgetApplicationControl.Quit,
                         WidgetApplicationControl.None, WidgetApplicationControl.Restart })
                Assert.AreEqual(expected, await session.TakeApplicationControlAsync());
        }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ManagedHandshakeAdvertisesSupportedControllerBehaviorsExplicitly()
    {
        await using var server = new ScriptedBridgeServer();
        var service = server.RunAsync(async channel =>
        {
            var request = await channel.ReadAsync(default);
            var hello = BridgeJson.FromElement<BridgeHello>(request.Payload);
            Assert.IsFalse(hello.ExclusiveControllerControl);
            Assert.IsFalse(hello.HeldDpadScroll);
            Assert.IsFalse(hello.StartupRegistration);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.HelloAccepted,
                RequestId = request.RequestId, Payload = BridgeJson.ToElement(new { }) }, default);
            var stop = await channel.ReadAsync(default);
            await channel.WriteAsync(new BridgeEnvelope { Type = BridgeMessageTypes.Acknowledged,
                RequestId = stop.RequestId, Payload = BridgeJson.ToElement(new { }) }, default);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName)) { }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
        var native = BridgeJson.FromElement<BridgeHello>(BridgeJson.ToElement(new { clientName = "native" }));
        Assert.IsTrue(native.ExclusiveControllerControl);
        Assert.IsTrue(native.HeldDpadScroll);
        Assert.IsTrue(native.StartupRegistration);
    }
}
