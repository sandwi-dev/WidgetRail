using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.PlatformDiagnostics;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class ControllerControlSessionTests
{
    [TestMethod]
    public void GeneratedTransportMetadataSupportsControllerContractsWithoutReflection()
    {
        var options = new JsonSerializerOptions(BridgeJson.Options) { TypeInfoResolver = BridgeJsonContext.Default };
        var preference = new ControllerSettings { OpenShortcut = ControllerOpenShortcut.Guide, Revision = 17 };
        var payload = JsonSerializer.SerializeToElement(preference, options);
        Assert.AreEqual("guide", payload.GetProperty("openShortcut").GetString());
        Assert.AreEqual(preference, payload.Deserialize<ControllerSettings>(options));
        var status = new ControllerControlStatus(ControllerControlState.WaitingForController, true, true, true);
        var report = JsonSerializer.SerializeToElement(status, options);
        Assert.AreEqual("waitingForController", report.GetProperty("state").GetString());
        Assert.AreEqual(status, report.Deserialize<ControllerControlStatus>(options));
    }

    [TestMethod]
    public async Task ConsumerOptsInAndReportsStatusBeforeReadingPreferencesIncludingResetRevision()
    {
        await using var server = new ScriptedBridgeServer();
        var expected = new[]
        {
            new ControllerSettings { ExclusiveControl = true, Revision = 12, OpenShortcut = ControllerOpenShortcut.Guide, F1ShortcutEnabled = true },
            new ControllerSettings { Revision = 0, OpenShortcut = ControllerOpenShortcut.ViewMenu },
        };
        var status = new ControllerControlStatus(ControllerControlState.Off, true, true, true);
        var service = server.RunAsync(async channel =>
        {
            var hello = await channel.ReadAsync(default);
            Assert.IsTrue(BridgeJson.FromElement<BridgeHello>(hello.Payload).ExclusiveControllerControl);
            await ReplyAsync(channel, hello, new { }, BridgeMessageTypes.HelloAccepted);
            foreach (var preference in expected)
            {
                var report = await channel.ReadAsync(default);
                Assert.AreEqual(BridgeMessageTypes.ControllerControl, report.Type);
                Assert.AreEqual(status, BridgeJson.FromElement<ControllerControlStatus>(report.Payload));
                await ReplyAsync(channel, report, preference);
            }
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
            new() { ExclusiveControllerControl = true }))
        {
            var effects = 0;
            session.HostEffectReceived += (_, _) => ++effects;
            foreach (var preference in expected)
                Assert.AreEqual(preference, await session.ExchangeControllerControlAsync(status));
            Assert.AreEqual(0, effects);
        }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(new BridgeProcessOptions(@"C:\candidate", @"C:\profile", @"C:\catalog").ExclusiveControllerControl);
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("null")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false,\"f1ShortcutEnabled\":\"false\"}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false,\"f1ShortcutEnabled\":null}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false,\"f1ShortcutEnabled\":true,\"f1ShortcutEnabled\":false}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":-1,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0.5,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":\"Unknown\",\"holdDpadToScroll\":false}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":99,\"holdDpadToScroll\":false}")]
    [DataRow("{\"exclusiveControl\":null,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":\"false\"}")]
    [DataRow("{\"exclusiveControl\":true,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false,\"extra\":1}")]
    [DataRow("{\"exclusiveControl\":true,\"exclusiveControl\":false,\"revision\":0,\"openShortcut\":\"guide\",\"holdDpadToScroll\":false}")]
    public async Task RejectsMalformedPreferenceWithoutHostEffects(string json)
    {
        await using var server = new ScriptedBridgeServer();
        var service = server.RunAuthenticatedAsync(async channel =>
        {
            var report = await channel.ReadAsync(default);
            using var malformed = JsonDocument.Parse(json);
            await ReplyAsync(channel, report, malformed.RootElement);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
            new() { ExclusiveControllerControl = true }))
        {
            var effects = 0;
            session.HostEffectReceived += (_, _) => ++effects;
            await Assert.ThrowsExactlyAsync<BridgeProtocolException>(() =>
                session.ExchangeControllerControlAsync(ControllerControlStatus.Unavailable));
            Assert.AreEqual(0, effects);
        }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task LegacyReplyWithoutF1PreservesControllerPreferenceAndDefaultsKeyboardShortcutOff()
    {
        await using var server = new ScriptedBridgeServer();
        var service = server.RunAuthenticatedAsync(async channel =>
        {
            var report = await channel.ReadAsync(default);
            await ReplyAsync(channel, report, new { exclusiveControl = true, revision = 9, openShortcut = "guide", holdDpadToScroll = false });
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
            new() { ExclusiveControllerControl = true }))
        {
            var result = await session.ExchangeControllerControlAsync(new(ControllerControlState.Active, true, true, true));
            Assert.IsTrue(result.ExclusiveControl);
            Assert.AreEqual(9L, result.Revision);
            Assert.AreEqual(ControllerOpenShortcut.Guide, result.OpenShortcut);
            Assert.IsFalse(result.F1ShortcutEnabled);
        }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task CancelledWaitDoesNotReplayReportOrLetNextExchangeOvertakeItsReply()
    {
        await using var server = new ScriptedBridgeServer();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextReadReady = new TaskCompletionSource<Task<BridgeEnvelope>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = server.RunAuthenticatedAsync(async channel =>
        {
            var first = await channel.ReadAsync(default);
            Assert.AreEqual(BridgeMessageTypes.ControllerControl, first.Type);
            var nextRead = channel.ReadAsync(default).AsTask();
            nextReadReady.SetResult(nextRead);
            received.SetResult();
            await reply.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(nextRead.IsCompleted, "The second report overtook the outstanding preference.");
            await ReplyAsync(channel, first, new ControllerSettings { Revision = 1 });
            var second = await nextRead.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(ControllerControlState.Active,
                BridgeJson.FromElement<ControllerControlStatus>(second.Payload).State);
            await ReplyAsync(channel, second, new ControllerSettings { Revision = 2 });
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
            new() { ExclusiveControllerControl = true }))
        {
            using var cancellation = new CancellationTokenSource();
            var first = session.ExchangeControllerControlAsync(ControllerControlStatus.Unavailable, cancellation.Token);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => first);
            var second = session.ExchangeControllerControlAsync(new(ControllerControlState.Active, true, true, true));
            Assert.IsFalse(second.IsCompleted);
            Assert.IsFalse((await nextReadReady.Task).IsCompleted);
            reply.SetResult();
            Assert.AreEqual(2, (await second.WaitAsync(TimeSpan.FromSeconds(5))).Revision);
        }
        await service.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task UnadvertisedConsumerCannotReportAndInvalidStatusNeverReachesBridge()
    {
        foreach (var enabled in new[] { false, true })
        {
            await using var server = new ScriptedBridgeServer();
            var service = server.RunAuthenticatedAsync(StopAsync);
            await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName,
                new() { ExclusiveControllerControl = enabled }))
            {
                if (!enabled)
                    await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                        session.ExchangeControllerControlAsync(ControllerControlStatus.Unavailable));
                else
                    await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
                        session.ExchangeControllerControlAsync(new((ControllerControlState)99, true, true, true)));
            }
            await service.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static Task ReplyAsync<T>(BridgeFrameChannel channel, BridgeEnvelope request, T payload, string? type = null) =>
        channel.WriteAsync(new BridgeEnvelope { Type = type ?? request.Type, RequestId = request.RequestId,
            Payload = BridgeJson.ToElement(payload) }, default).AsTask();

    private static async Task StopAsync(BridgeFrameChannel channel)
    {
        var stop = await channel.ReadAsync(default);
        Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
        await ReplyAsync(channel, stop, new { }, BridgeMessageTypes.Acknowledged);
    }
}
