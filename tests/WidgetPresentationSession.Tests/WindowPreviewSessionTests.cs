using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class WindowPreviewSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(6);
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "previews", Name = "Previews", InstanceId = "previews.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Connection };
    private static object Target(string created = "1abc") => new { handle = "123a", processId = 42, processCreated = created, className = "Editor" };
    private static object Inventory(string created = "1abc") => new Dictionary<string, object> { ["window.1"] = Target(created) };

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task HandshakeNegotiatesOnlyExplicitFrontendSupport(bool enabled)
    {
        await Run(async channel =>
        {
            if (enabled) await PermissionReply(channel, ["window.1", "not-displayed"]);
        }, async (session, frame) =>
        {
            if (!enabled)
                await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.RefreshWindowPreviewPermissionsAsync(frame));
            else
            {
                var grant = await session.RefreshWindowPreviewPermissionsAsync(frame);
                Assert.AreEqual(1, grant.Targets.Count);
                Assert.AreEqual(new WidgetHostWindowTarget("window.1", 0x123a, 42, 0x1abc, "Editor"), grant.Targets["window.1"]);
                Assert.IsTrue(session.IsWindowPreviewCurrent(grant, "window.1"));
                Assert.IsFalse(session.IsWindowPreviewCurrent(grant, "not-displayed"));
                Assert.Throws<NotSupportedException>(() => ((IDictionary<string, WidgetHostWindowTarget>)grant.Targets).Clear());
                Assert.Throws<NotSupportedException>(() => ((IDictionary<string, WidgetHostWindowTarget>)frame.WindowPreviews).Clear());
            }
        }, enabled, enabled ? Inventory() : null);
    }

    [TestMethod]
    public async Task CompatiblePublicationRetainsCaptureAndConsentDenialImmediatelyRevokesOldGrant()
    {
        await Run(async channel =>
        {
            await PermissionReply(channel, ["window.1"]);
            var refresh = await Read(channel); await Snapshot(channel, refresh.RequestId, 2, Inventory());
            await PermissionReply(channel, []);
            await PermissionReply(channel, ["window.1"]);
        }, async (session, frame) =>
        {
            var first = await session.RefreshWindowPreviewPermissionsAsync(frame);
            var newer = await session.RefreshAsync(frame.Authority);
            Assert.IsTrue(session.IsWindowPreviewCurrent(first, "window.1"));
            var denied = await session.RefreshWindowPreviewPermissionsAsync(newer);
            Assert.AreEqual(0, denied.Targets.Count);
            Assert.IsFalse(session.IsWindowPreviewCurrent(first, "window.1"));
            var restored = await session.RefreshWindowPreviewPermissionsAsync(newer);
            Assert.IsTrue(session.IsWindowPreviewCurrent(restored, "window.1"));
            Assert.IsFalse(session.IsWindowPreviewCurrent(first, "window.1"));
            await session.DisposeAsync();
            Assert.IsFalse(session.IsWindowPreviewCurrent(restored, "window.1"));
        });
    }

    [TestMethod]
    [DataRow("omission")]
    [DataRow("identity")]
    public async Task OmissionOrNativeIdentityReplacementPermanentlyRetiresGrant(string replacement)
    {
        await Run(async channel =>
        {
            await PermissionReply(channel, ["window.1"]);
            var refresh = await Read(channel);
            await Snapshot(channel, refresh.RequestId, 2, replacement == "omission" ? null : Inventory("2abc"));
            refresh = await Read(channel); await Snapshot(channel, refresh.RequestId, 3, Inventory());
            await PermissionReply(channel, ["window.1"]);
        }, async (session, frame) =>
        {
            var grant = await session.RefreshWindowPreviewPermissionsAsync(frame);
            var changed = await session.RefreshAsync(frame.Authority);
            Assert.IsFalse(session.IsWindowPreviewCurrent(grant, "window.1"));
            var restored = await session.RefreshAsync(changed.Authority);
            Assert.IsFalse(session.IsWindowPreviewCurrent(grant, "window.1"));
            var fresh = await session.RefreshWindowPreviewPermissionsAsync(restored);
            Assert.IsTrue(session.IsWindowPreviewCurrent(fresh, "window.1"));
        });
    }

    [TestMethod]
    public async Task ForgedFrameCannotAcquireCapturePermission()
    {
        await Run(_ => Task.CompletedTask, async (session, frame) =>
        {
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.RefreshWindowPreviewPermissionsAsync(frame with { }));
        });
    }

    [TestMethod]
    public async Task GrantExpiresWithoutRenewal()
    {
        await Run(channel => PermissionReply(channel, ["window.1"]), async (session, frame) =>
        {
            var grant = await session.RefreshWindowPreviewPermissionsAsync(frame);
            Assert.IsTrue(session.IsWindowPreviewCurrent(grant, "window.1"));
            await Task.Delay(2100);
            Assert.IsFalse(session.IsWindowPreviewCurrent(grant, "window.1"));
        });
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("invalid-id")]
    [DataRow("wrong-type")]
    [DataRow("too-many")]
    public async Task InvalidPermissionResponseRevokesPreviousGrant(string variant)
    {
        await Run(async channel =>
        {
            await PermissionReply(channel, ["window.1"]);
            var request = await Read(channel);
            object ids = variant switch
            {
                "duplicate" => new[] { "window.1", "window.1" },
                "invalid-id" => new[] { "../../window" },
                "wrong-type" => 42,
                _ => Enumerable.Range(0, 65).Select(index => "window." + index).ToArray(),
            };
            await Reply(channel, request.RequestId, BridgeMessageTypes.WindowPreviewPermissions, new { allowedWindowIds = ids });
        }, async (session, frame) =>
        {
            var grant = await session.RefreshWindowPreviewPermissionsAsync(frame);
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.RefreshWindowPreviewPermissionsAsync(frame));
            Assert.IsFalse(session.IsWindowPreviewCurrent(grant, "window.1"));
        });
    }

    [TestMethod]
    public async Task PendingRefreshIsBoundedAndCancellationDoesNotPublishALateGrant()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Run(async channel =>
        {
            var pending = await Read(channel); entered.SetResult();
            await release.Task.WaitAsync(Limit);
            await Reply(channel, pending.RequestId, BridgeMessageTypes.WindowPreviewPermissions, new { allowedWindowIds = new[] { "window.1" } });
            await PermissionReply(channel, ["window.1"]);
        }, async (session, frame) =>
        {
            using var canceled = new CancellationTokenSource();
            var pending = session.RefreshWindowPreviewPermissionsAsync(frame, canceled.Token);
            await entered.Task.WaitAsync(Limit);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.RefreshWindowPreviewPermissionsAsync(frame));
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            release.SetResult();
            var grant = await session.RefreshWindowPreviewPermissionsAsync(frame);
            Assert.IsTrue(session.IsWindowPreviewCurrent(grant, "window.1"));
        });
    }

    [TestMethod]
    [DataRow("not-negotiated")]
    [DataRow("undeclared")]
    [DataRow("zero-handle")]
    [DataRow("bad-process")]
    [DataRow("unknown-field")]
    public async Task InvalidInventoryCannotBecomeACaptureSource(string variant)
    {
        object inventory = variant switch
        {
            "undeclared" => new Dictionary<string, object> { ["other"] = Target() },
            "zero-handle" => new Dictionary<string, object> { ["window.1"] = new { handle = "0", processId = 42, processCreated = "1abc", className = "Editor" } },
            "bad-process" => new Dictionary<string, object> { ["window.1"] = new { handle = "123a", processId = 0, processCreated = "1abc", className = "Editor" } },
            "unknown-field" => new Dictionary<string, object> { ["window.1"] = new { handle = "123a", processId = 42, processCreated = "1abc", className = "Editor", extra = true } },
            _ => Inventory(),
        };
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            await Catalog(channel);
            var establish = await Read(channel); await Snapshot(channel, establish.RequestId, 1, inventory);
            await Stop(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, new() { WindowPreviews = variant != "not-negotiated" }))
        {
            await session.ListWidgetsAsync();
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive));
            Assert.IsNull(session.GetState(Descriptor.Id)?.LastGood);
        }
        await serving.WaitAsync(Limit);
    }

    private static async Task Run(Func<BridgeFrameChannel, Task> serverAction,
        Func<WidgetPresentationSession, WidgetPresentationFrame, Task> clientAction, bool enabled = true, object? inventory = null)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAsync(async channel =>
        {
            var hello = await Read(channel);
            Assert.AreEqual(enabled, hello.Payload.GetProperty("windowPreviews").GetBoolean());
            await Reply(channel, hello.RequestId, BridgeMessageTypes.HelloAccepted, new { });
            await Catalog(channel);
            var establish = await Read(channel); await Snapshot(channel, establish.RequestId, 1, enabled ? inventory ?? Inventory() : null);
            await serverAction(channel);
            await Stop(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, new() { WindowPreviews = enabled }))
        {
            await session.ListWidgetsAsync();
            var frame = await session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive);
            await clientAction(session, frame).WaitAsync(Limit);
        }
        await serving.WaitAsync(Limit);
    }
    private static async Task Catalog(BridgeFrameChannel channel)
    {
        var request = await Read(channel);
        await Reply(channel, request.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, widgets = new[] { Descriptor }, isComplete = true });
    }
    private static async Task PermissionReply(BridgeFrameChannel channel, string[] ids)
    {
        var request = await Read(channel);
        Assert.AreEqual(BridgeMessageTypes.WindowPreviewPermissions, request.Type);
        Assert.AreEqual(Descriptor.Id, request.Payload.GetProperty("widgetId").GetString());
        Assert.AreEqual(1, request.Payload.EnumerateObject().Count());
        await Reply(channel, request.RequestId, request.Type, new { allowedWindowIds = ids });
    }
    private static async Task Snapshot(BridgeFrameChannel channel, long requestId, long sequence, object? inventory)
    {
        var snapshot = new ViewSnapshot { WidgetInstanceId = Descriptor.InstanceId, Sequence = sequence, ActiveInputScopeId = "root",
            Root = new() { Id = "root", Kind = ViewNodeKind.Stack, Children = [new() { Id = "preview", Kind = ViewNodeKind.WindowPreview,
                WindowId = "window.1", PreviewAspectRatio = 1.5, ImageFit = ImageFit.Contain, AccessibilityLabel = "Application preview" }] } };
        using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await Reply(channel, requestId, BridgeMessageTypes.Snapshot, new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint",
            baseSequence = 0, recoveryOriginSequence = 0, snapshot = json.RootElement, renderStyles = new Dictionary<string, BridgeNodeRenderStyles>(), windowPreviews = inventory });
    }
    private static async Task Stop(BridgeFrameChannel channel)
    { var request = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, request.Type); await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { }); }
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object payload) =>
        await channel.WriteAsync(new() { Type = type, RequestId = id, Payload = BridgeJson.ToElement(payload) }, CancellationToken.None);
}
