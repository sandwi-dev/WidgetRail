using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed class LocalWidgetInstallTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "settings", Name = "Settings", InstanceId = "settings.default",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Settings };
    private static WidgetActionEvent Action => new(WidgetPresentationSession.InstallLocalWidgetAction, "installed.install-local", InputScopeId: "installed.widgets");
    private static ViewSnapshot Snapshot(long sequence = 1, WidgetActionEvent? action = null) => new()
    {
        Sequence = sequence, WidgetInstanceId = Descriptor.InstanceId, ActiveInputScopeId = (action ?? Action).InputScopeId!,
        Root = new() { Id = (action ?? Action).InputScopeId!, Kind = ViewNodeKind.Stack,
            Children = [new() { Id = (action ?? Action).SourceElementId, Kind = ViewNodeKind.Button, Text = "Install", ActionId = (action ?? Action).ActionId }] },
    };
    private static string PackagePath => Path.Combine(Path.GetTempPath(), "fixture.wrwidget");

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task CompletionBeforeAcknowledgementIsNotLostAndUpdateKeepsTarget(bool update)
    {
        var action = update ? Action with { SourceElementId = "installed.update.file." + new string('d',64), InputScopeId = "installed.update" } : Action;
        await Run(async channel =>
        {
            var request = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.InstallLocalWidgetPackage, request.Type);
            var install = BridgeJson.FromElement<BridgeLocalWidgetPackageInstallRequest>(request.Payload);
            Assert.AreEqual(update ? new string('d',64) : null, install.Origin.UpdateTargetHash);
            Assert.AreEqual("widgetrail.firstparty.settings", install.Origin.PackageId);
            await Event(channel, install.OperationId, "installed-disabled");
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
        }, async (session, frame) =>
        {
            var result = await session.InstallLocalWidgetAsync(frame, action, PackagePath, (_, _) => throw new AssertFailedException("Unexpected review"));
            Assert.AreEqual("installed-disabled", result.Status);
        }, action);
    }

    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public async Task FullTrustRequiresExplicitDecision(bool approved)
    {
        await Run(async channel =>
        {
            var request = await Read(channel);
            var id = request.Payload.GetProperty("operationId").GetString()!;
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            await Event(channel, id, "approval-required");
            request = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.ApproveLocalWidgetPackageInstall, request.Type);
            Assert.AreEqual(approved, request.Payload.GetProperty("approved").GetBoolean());
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { approved = true });
            await Event(channel, id, approved ? "installed-disabled" : "cancelled");
        }, async (session, frame) =>
        {
            var reviews = 0;
            var result = await session.InstallLocalWidgetAsync(frame, Action, PackagePath, (package, _) =>
            { reviews++; Assert.AreEqual("fixture.widget", package.WidgetId); return Task.FromResult(approved); });
            Assert.AreEqual(1, reviews);
            Assert.AreEqual(approved ? "installed-disabled" : "cancelled", result.Status);
        });
    }

    [TestMethod]
    public async Task CancellationNotifiesBridgeAndLateCompletionDoesNotBreakSession()
    {
        using var cancel = new CancellationTokenSource();
        await Run(async channel =>
        {
            var request = await Read(channel);
            var id = request.Payload.GetProperty("operationId").GetString()!;
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            cancel.Cancel();
            request = await Read(channel);
            Assert.AreEqual(BridgeMessageTypes.CancelLocalWidgetPackageInstall, request.Type);
            Assert.AreEqual(id, request.Payload.GetProperty("operationId").GetString());
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { cancelled = true });
            await Event(channel, id, "cancelled");
        }, async (session, frame) =>
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await session.InstallLocalWidgetAsync(frame, Action, PackagePath, (_, _) => Task.FromResult(false), cancel.Token));
        });
    }

    [TestMethod]
    [DataRow("source")][DataRow("scope")][DataRow("repeat")][DataRow("widget")][DataRow("changed")]
    public async Task ForgedOrRetiredInstallActionIsRejectedBeforePicker(string mutation)
    {
        await Run(async channel =>
        {
            if (mutation != "changed") return;
            var request = await Read(channel);
            var snapshot = Snapshot(2);
            await ReplySnapshot(channel, request.RequestId, snapshot with { Root = snapshot.Root with {
                Children = [snapshot.Root.Children[0] with { IsDisabled = true }] } });
        }, async (session, frame) =>
        {
            if (mutation == "changed") await session.RefreshAsync(frame.Authority);
            var action = mutation switch
            {
                "source" => Action with { SourceElementId = "forged" },
                "scope" => Action with { InputScopeId = "root" },
                "repeat" => Action with { Phase = ControllerEventPhase.Repeated },
                _ => Action,
            };
            if (mutation == "widget") frame = frame with { Authority = frame.Authority with { WidgetId = "community" } };
            Assert.Throws<WidgetPresentationSessionException>(() => session.ValidateLocalWidgetInstall(frame, action));
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ExpiredApprovalEndsPendingReviewWithoutWaitingForUser()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = false;
        await Run(async channel =>
        {
            var request = await Read(channel);
            var id = request.Payload.GetProperty("operationId").GetString()!;
            await Reply(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { });
            await Event(channel, id, "approval-required");
            await entered.Task.WaitAsync(Limit);
            await Event(channel, id, "failed");
        }, async (session, frame) =>
        {
            var result = await session.InstallLocalWidgetAsync(frame, Action, PackagePath, async (_, token) =>
            {
                entered.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); return true; }
                finally { ended = true; }
            });
            Assert.AreEqual("failed", result.Status);
            Assert.IsTrue(ended);
        });
    }

    private static async Task Run(Func<BridgeFrameChannel, Task> script,
        Func<WidgetPresentationSession, WidgetPresentationFrame, Task> exercise, WidgetActionEvent? action = null)
    {
        await using var server = new ScriptedBridgeServer();
        var serving = server.RunAuthenticatedAsync(async channel =>
        {
            var catalog = await Read(channel);
            await Reply(channel, catalog.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
            var establish = await Read(channel); await ReplySnapshot(channel, establish.RequestId, Snapshot(action: action));
            await script(channel);
            var stop = await Read(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
            await Reply(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            await session.ListWidgetsAsync();
            var frame = await session.EstablishPresentationAsync(session.GetTarget("settings"), WidgetLifecycleState.Interactive);
            var test = exercise(session, frame);
            if (await Task.WhenAny(test, serving) == serving) await serving;
            await test.WaitAsync(Limit);
        }
        await serving.WaitAsync(Limit);
    }
    private static async Task ReplySnapshot(BridgeFrameChannel channel, long id, ViewSnapshot snapshot)
    {
        using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await Reply(channel, id, BridgeMessageTypes.Snapshot, new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint",
            baseSequence = 0, recoveryOriginSequence = 0, snapshot = json.RootElement, renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() });
    }
    private static Task Event(BridgeFrameChannel channel, string id, string status) => Reply(channel, 0,
        BridgeMessageTypes.LocalWidgetPackageInstallCompleted, new BridgeLocalWidgetPackageInstallCompleted(id,status,"fixture.widget","1.0.0","Fixture result"));
    private static Task<BridgeEnvelope> Read(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task Reply(BridgeFrameChannel channel, long id, string type, object value) =>
        await channel.WriteAsync(new() { Type = type, RequestId = id, Payload = BridgeJson.ToElement(value) }, CancellationToken.None);
}
