using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class SessionHostEventTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static BridgeWidgetDescriptor Descriptor(string runtime = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") => new()
    {
        Id = "event-widget", Name = "Events", InstanceId = "instance.1",
        RuntimeGeneration = runtime, PresentationGeneration = new string('b', 32),
        PackageContentDigest = new string('e', 64), Icon = WidgetGlyph.Connection,
    };

    [TestMethod]
    public async Task HostEffectsPreserveProducerPayloadAndRejectStaleRuntimeAndReplay()
    {
        await using var server = new ScriptedBridgeServer();
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await CatalogAsync(channel, descriptor, 1);
            var barrier = await ReadAsync(channel);
            await EventAsync(channel, BridgeMessageTypes.HostEffect, Effect(descriptor, 1));
            await EventAsync(channel, BridgeMessageTypes.HostEffect, Effect(descriptor, 1));
            await EventAsync(channel, BridgeMessageTypes.HostEffect,
                Effect(Descriptor(new string('c', 32)), 2));
            await EventAsync(channel, BridgeMessageTypes.HostEffect,
                Effect(descriptor with { Id = "removed-widget" }, 3));
            var activation = Effect(descriptor, 4, "activateTaskWindow");
            activation["windowPreviews"] = new Dictionary<string, object>
            {
                ["window.1"] = new { handle = "123a", processId = 42, processCreated = "1abc", className = "Editor" },
            };
            await EventAsync(channel, BridgeMessageTypes.HostEffect, activation);
            await EventAsync(channel, BridgeMessageTypes.HostEffect, Effect(descriptor, 5, "futureOptionalEffect"));
            await EventAsync(channel, BridgeMessageTypes.HostEffect, Effect(descriptor, 4));
            await CatalogReplyAsync(channel, barrier.RequestId, descriptor, 1);
            await CatalogAsync(channel, Descriptor(new string('d', 32)), 2);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var effects = new ConcurrentQueue<WidgetPresentationHostEffect>();
            session.HostEffectReceived += (_, args) => effects.Enqueue(args.Effect);
            await session.ListWidgetsAsync();
            await session.ListWidgetsAsync();
            var delivered = effects.ToArray();
            CollectionAssert.AreEqual(new long[] { 1, 4, 5 }, delivered.Select(item => item.Sequence).ToArray());
            Assert.AreEqual(WidgetHostEffectKind.CloseOverlayAfterAppLaunch, delivered[0].Kind);
            Assert.AreEqual(250L, delivered[0].InitiatedAtMilliseconds);
            Assert.IsNull(delivered[0].WindowTarget);
            Assert.IsTrue(session.IsHostEffectAuthorityCurrent(delivered[0].Authority));
            Assert.AreEqual(descriptor.InstanceId, delivered[0].Authority.WidgetInstanceId);
            Assert.AreEqual(WidgetHostEffectKind.ActivateTaskWindow, delivered[1].Kind);
            Assert.AreEqual(new WidgetHostWindowTarget("window.1", 0x123a, 42, 0x1abc, "Editor"), delivered[1].WindowTarget);
            Assert.AreEqual(WidgetHostEffectKind.Unsupported, delivered[2].Kind);
            Assert.AreEqual("futureOptionalEffect", delivered[2].WireName);
            Assert.IsNull(delivered[2].WindowTarget);
            Assert.AreEqual(4, session.Diagnostics.Count(item => item.Code == "stale_host_effect"));
            await session.ListWidgetsAsync();
            Assert.IsFalse(session.IsHostEffectAuthorityCurrent(delivered[0].Authority),
                "A queued UI effect must fail revalidation after worker replacement.");
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task RevisionEventsAreMonotonicAndDoNotReplaceAdmittedCatalog()
    {
        await using var server = new ScriptedBridgeServer();
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await CatalogAsync(channel, descriptor, 3);
            var barrier = await ReadAsync(channel);
            foreach (var revision in new[] { 2, 3, 5, 5, 4, 6 })
                await EventAsync(channel, BridgeMessageTypes.CatalogChanged, new { revision });
            foreach (var revision in new[] { 0, 2, 1, 2, 4 })
                await EventAsync(channel, BridgeMessageTypes.AppearanceChanged, new { revision });
            await CatalogReplyAsync(channel, barrier.RequestId, descriptor, 3);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var catalog = new ConcurrentQueue<long>();
            var appearance = new ConcurrentQueue<long>();
            session.CatalogChanged += (_, args) => catalog.Enqueue(args.Revision);
            session.AppearanceChanged += (_, args) => appearance.Enqueue(args.Revision);
            await session.ListWidgetsAsync();
            await session.ListWidgetsAsync();
            CollectionAssert.AreEqual(new long[] { 5, 6 }, catalog.ToArray());
            CollectionAssert.AreEqual(new long[] { 0, 2, 4 }, appearance.ToArray());
            Assert.AreEqual(6L, session.LatestCatalogNotificationRevision);
            Assert.AreEqual(4L, session.LatestAppearanceNotificationRevision);
            Assert.AreEqual(3L, session.GetTarget(descriptor.Id).CatalogRevision);
            Assert.IsFalse(session.Diagnostics.Any(item => item.Code == "transport_closed"));
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task RestartRejectsOldInitiationEvenWhenRuntimeFingerprintIsUnchanged()
    {
        await using var server = new ScriptedBridgeServer();
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await CatalogAsync(channel, descriptor, 1);
            var restart = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.RestartWidget, restart.Type);
            await ReplyAsync(channel, restart.RequestId, BridgeMessageTypes.Acknowledged,
                new { widgetId = descriptor.Id, state = WidgetLifecycleState.Visible });
            var barrier = await ReadAsync(channel);
            await EventAsync(channel, BridgeMessageTypes.HostEffect, Effect(descriptor, 1));
            await Task.Delay(20);
            var current = Effect(descriptor, 2);
            current["initiatedAtMilliseconds"] = Environment.TickCount64;
            await EventAsync(channel, BridgeMessageTypes.HostEffect, current);
            await CatalogReplyAsync(channel, barrier.RequestId, descriptor, 1);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var effects = new ConcurrentQueue<WidgetPresentationHostEffect>();
            session.HostEffectReceived += (_, args) => effects.Enqueue(args.Effect);
            await session.ListWidgetsAsync();
            await session.RestartAsync(session.GetTarget(descriptor.Id));
            await session.ListWidgetsAsync();
            CollectionAssert.AreEqual(new long[] { 2 }, effects.Select(effect => effect.Sequence).ToArray());
            Assert.AreEqual(2L, effects.Single().Authority.SessionGeneration);
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    public async Task RemovingAndReadmittingSameDescriptorDoesNotReviveOldHostEffects()
    {
        await using var server = new ScriptedBridgeServer();
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await CatalogAsync(channel, descriptor, 1);
            var removed = await ReadAsync(channel);
            await ReplyAsync(channel, removed.RequestId, BridgeMessageTypes.Widgets,
                new { revision = 2, isComplete = true, widgets = Array.Empty<BridgeWidgetDescriptor>() });
            await CatalogAsync(channel, descriptor, 3);
            var barrier = await ReadAsync(channel);
            await EventAsync(channel, BridgeMessageTypes.HostEffect, Effect(descriptor, 1));
            await Task.Delay(20);
            var current = Effect(descriptor, 2);
            current["initiatedAtMilliseconds"] = Environment.TickCount64;
            await EventAsync(channel, BridgeMessageTypes.HostEffect, current);
            await CatalogReplyAsync(channel, barrier.RequestId, descriptor, 3);
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var effects = new ConcurrentQueue<WidgetPresentationHostEffect>();
            session.HostEffectReceived += (_, args) => effects.Enqueue(args.Effect);
            for (var index = 0; index < 4; ++index) await session.ListWidgetsAsync();
            CollectionAssert.AreEqual(new long[] { 2 }, effects.Select(effect => effect.Sequence).ToArray());
            Assert.AreEqual(3L, effects.Single().Authority.SessionGeneration);
        }
        await serverTask.WaitAsync(Deadline);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("empty")]
    [DataRow("zeroHandle")]
    [DataRow("invalidPid")]
    [DataRow("extraCloseTarget")]
    public async Task MalformedNativeEffectNeverReachesHost(string variant)
    {
        await using var server = new ScriptedBridgeServer();
        var descriptor = Descriptor();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await CatalogAsync(channel, descriptor, 1);
            var barrier = await ReadAsync(channel);
            var payload = Effect(descriptor, 1, variant == "extraCloseTarget" ? "closeOverlayAfterAppLaunch" : "activateTaskWindow");
            if (variant != "missing")
                payload["windowPreviews"] = variant is "empty" or "extraCloseTarget"
                    ? new Dictionary<string, object>()
                    : new Dictionary<string, object>
                    {
                        ["window.1"] = new
                        {
                            handle = variant == "zeroHandle" ? "0" : "1", processId = variant == "invalidPid" ? 0 : 42,
                            processCreated = "1", className = "PRIVATE_PROVIDER_VALUE",
                        },
                    };
            await EventAsync(channel, BridgeMessageTypes.HostEffect, payload);
            try { await CatalogReplyAsync(channel, barrier.RequestId, descriptor, 1); }
            catch (IOException) { }
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var delivered = 0;
            session.HostEffectReceived += (_, _) => ++delivered;
            await session.ListWidgetsAsync();
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () =>
                await session.ListWidgetsAsync().WaitAsync(Deadline));
            Assert.AreEqual(0, delivered);
            Assert.IsFalse(session.Diagnostics.Any(item => item.Message.Contains("PRIVATE_PROVIDER_VALUE", StringComparison.Ordinal)));
        }
        await serverTask.WaitAsync(Deadline);
    }

    private static Dictionary<string, object> Effect(BridgeWidgetDescriptor descriptor, long sequence,
        string effect = "closeOverlayAfterAppLaunch") => new()
    {
        ["widgetId"] = descriptor.Id, ["runtimeGeneration"] = descriptor.RuntimeGeneration,
        ["effect"] = effect, ["sequence"] = sequence, ["initiatedAtMilliseconds"] = 250L,
    };
    private static Task<BridgeEnvelope> ReadAsync(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
    private static async Task CatalogAsync(BridgeFrameChannel channel, BridgeWidgetDescriptor descriptor, long revision) =>
        await CatalogReplyAsync(channel, (await ReadAsync(channel)).RequestId, descriptor, revision);
    private static Task CatalogReplyAsync(BridgeFrameChannel channel, long requestId, BridgeWidgetDescriptor descriptor, long revision) =>
        ReplyAsync(channel, requestId, BridgeMessageTypes.Widgets, new { revision, isComplete = true, widgets = new[] { descriptor } });
    private static async Task ReplyAsync(BridgeFrameChannel channel, long requestId, string type, object payload) =>
        await channel.WriteAsync(new BridgeEnvelope { Type = type, RequestId = requestId, Payload = BridgeJson.ToElement(payload) }, CancellationToken.None);
    private static Task EventAsync(BridgeFrameChannel channel, string type, object payload) => ReplyAsync(channel, 0, type, payload);
    private static async Task StopAsync(BridgeFrameChannel channel)
    {
        var stop = await ReadAsync(channel);
        Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
        await ReplyAsync(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
    }
}
