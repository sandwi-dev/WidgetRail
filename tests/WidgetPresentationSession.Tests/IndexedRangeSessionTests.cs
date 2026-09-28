using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

[TestClass]
public sealed partial class IndexedRangeSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(8);
    private static readonly IndexedCollectionDescriptor Source = new("games", 1, 0, 10000);
    private static readonly BridgeWidgetDescriptor Descriptor = new()
    {
        Id = "indexed", Name = "Indexed", InstanceId = "indexed.instance", Icon = WidgetGlyph.Connection,
        RuntimeGeneration = new string('a', 32), PresentationGeneration = new string('b', 32), PackageContentDigest = new string('e', 64),
    };

    [TestMethod]
    public async Task SlowRangesLeaveActionCapacityAndResolveOutOfOrderWithUniqueDemands()
    {
        await RunAsync(async channel =>
        {
            var first = await ReadAsync(channel);
            var second = await ReadAsync(channel);
            Assert.AreNotEqual(Request(first).Range.DemandId, Request(second).Range.DemandId);
            var action = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.Action, action.Type);
            await ReplyAsync(channel, action.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            await RangeReplyAsync(channel, second);
            await RangeReplyAsync(channel, first);
        }, async (session, frame) =>
        {
            var first = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2);
            var second = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 100, 2);
            var saturated = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() =>
                session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 200, 2));
            Assert.AreEqual("indexed_saturated", saturated.Code);
            await session.SendActionAsync(frame.Authority, new("refresh", "refresh", InputScopeId: "root")).WaitAsync(Limit);
            Assert.AreEqual(0, (await first.WaitAsync(Limit)).StartIndex);
            Assert.AreEqual(100, (await second.WaitAsync(Limit)).StartIndex);
            Assert.AreEqual(2, session.GetState(Descriptor.Id)!.LastGood!.Snapshot.Root.Children.Count,
                "Range results must not materialize their nodes into page authority.");
        }, new() { MaximumPendingRequests = 5, MaximumPendingIndexedRanges = 2 });
    }

    [TestMethod]
    [DataRow("outer")]
    [DataRow("demand")]
    [DataRow("query")]
    [DataRow("scope")]
    public async Task ForeignReplyCannotPopulateParentAndNextReadRemainsHealthy(string variant)
    {
        await RunAsync(async channel =>
        {
            var read = await ReadAsync(channel);
            var request = Request(read);
            var range = Range(request.Range);
            range = variant switch
            {
                "demand" => range with { DemandId = "foreign" },
                "query" => range with { Source = Source with { QueryGeneration = 2 } },
                "scope" => range with { ScopeId = "foreign" },
                _ => range,
            };
            await ReplyAsync(channel, read.RequestId, BridgeMessageTypes.IndexedRange,
                new BridgeIndexedRangeResponse(variant == "outer" ? "foreign" : Descriptor.Id, Descriptor.InstanceId,
                    Descriptor.RuntimeGeneration, Descriptor.PresentationGeneration, range));
            await RangeReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            try { await session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2); Assert.Fail(); }
            catch (Exception error) when (error is BridgeProtocolException or ArgumentException) { }
            Assert.AreEqual(2, (await session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 4, 2).WaitAsync(Limit)).Items.Count);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationAndTimeoutTargetExactDemandAndPreserveFollowingRead(bool timeout)
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            var original = await ReadAsync(channel); admitted.SetResult();
            var cancel = await ReadAsync(channel);
            await CancelReplyAsync(channel, original, cancel);
            await RangeReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            using var cancellation = new CancellationTokenSource();
            var pending = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 4, 2, cancellationToken: cancellation.Token);
            await admitted.Task.WaitAsync(Limit);
            if (!timeout) cancellation.Cancel();
            if (timeout)
            {
                var error = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () => await pending.WaitAsync(Limit));
                Assert.AreEqual("indexed_timeout", error.Code);
            }
            else await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending.WaitAsync(Limit));
            Assert.AreEqual(2, (await session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 6, 2).WaitAsync(Limit)).Items.Count);
        }, new() { IndexedRangeTimeout = TimeSpan.FromMilliseconds(200) });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnrelatedParentRevisionPreservesPendingQueryData(bool openModal)
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            var original = await ReadAsync(channel); admitted.SetResult();
            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source, openModal);
            await RangeReplyAsync(channel, original);
        }, async (session, frame) =>
        {
            var pending = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2);
            await admitted.Task.WaitAsync(Limit);
            _ = await session.RefreshAsync(frame.Authority);
            Assert.AreEqual(2, (await pending.WaitAsync(Limit)).Items.Count);
        });
    }

    [TestMethod]
    public async Task RetiredFrameCancellationCannotCancelNewFrameDemand()
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source);
            var current = await ReadAsync(channel); admitted.SetResult();
            await release.Task.WaitAsync(Limit);
            await RangeReplyAsync(channel, current);
        }, async (session, frame) =>
        {
            var current = await session.RefreshAsync(frame.Authority);
            var pending = session.ReadIndexedRangeAsync(current.Authority, "list", Source, 0, 2);
            await admitted.Task.WaitAsync(Limit);
            session.CancelIndexedRanges(frame.Authority);
            release.SetResult();
            Assert.AreEqual(2, (await pending.WaitAsync(Limit)).Items.Count);
        });
    }

    [TestMethod]
    public async Task NewQueryRetiresPendingRangeAndRejectsOldAuthorityBeforeSending()
    {
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var next = Source with { QueryGeneration = 2 };
        await RunAsync(async channel =>
        {
            var original = await ReadAsync(channel); admitted.SetResult();
            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, next);
            await CancelReplyAsync(channel, original, await ReadAsync(channel));
            await RangeReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            var pending = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2);
            await admitted.Task.WaitAsync(Limit);
            var refreshed = await session.RefreshAsync(frame.Authority);
            var retired = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () => await pending.WaitAsync(Limit));
            Assert.AreEqual("indexed_retired", retired.Code);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2));
            Assert.AreEqual(next, (await session.ReadIndexedRangeAsync(refreshed.Authority, "list", next, 0, 2)).Source);
        });
    }

    [TestMethod]
    public async Task BackgroundRetiresDemandAndLateVisibleAckCannotReopenAdmission()
    {
        var visibleReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            var visible = await ReadAsync(channel); visibleReceived.SetResult();
            var hidden = await ReadAsync(channel);
            await ReplyAsync(channel, hidden.RequestId, BridgeMessageTypes.Acknowledged, new { });
            await ReplyAsync(channel, visible.RequestId, BridgeMessageTypes.Acknowledged, new { });
            var reopen = await ReadAsync(channel);
            await ReplyAsync(channel, reopen.RequestId, BridgeMessageTypes.Acknowledged, new { });
            await RangeReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            var target = session.GetTarget(Descriptor.Id);
            var visible = session.SetLifecycleAsync(target, WidgetLifecycleState.Visible);
            await visibleReceived.Task.WaitAsync(Limit);
            await session.SetLifecycleAsync(target, WidgetLifecycleState.Background);
            await visible;
            var hidden = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2));
            Assert.AreEqual("indexed_surface_hidden", hidden.Code);
            await session.SetLifecycleAsync(target, WidgetLifecycleState.Visible);
            Assert.AreEqual(2, (await session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2)).Items.Count);
        });
    }

    [TestMethod]
    public async Task SurfaceCancellationIsProjectionSpecificAndDisposalCancelsRemainingRead()
    {
        await using var server = new ScriptedBridgeServer();
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            var main = await ReadAsync(channel);
            var pinned = await ReadAsync(channel); admitted.SetResult();
            await CancelReplyAsync(channel, main, await ReadAsync(channel));
            await CancelReplyAsync(channel, pinned, await ReadAsync(channel));
            await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName))
        {
            var frame = await EstablishAsync(session);
            var main = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2);
            var pinned = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 2, "host.full-widget");
            await admitted.Task.WaitAsync(Limit);
            session.CancelIndexedRanges(frame.Authority);
            var retired = await Assert.ThrowsAsync<WidgetPresentationSessionException>(async () => await main.WaitAsync(Limit));
            Assert.AreEqual("indexed_retired", retired.Code);
            Assert.IsFalse(pinned.IsCompleted, "Closing the main projection must not cancel a pinned projection.");
            await session.DisposeAsync().AsTask().WaitAsync(Limit);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await pinned.WaitAsync(Limit));
        }
        await serverTask.WaitAsync(Limit);
    }

    private static async Task RunAsync(Func<BridgeFrameChannel, Task> serverScenario,
        Func<WidgetPresentationSession, WidgetPresentationFrame, Task> clientScenario, WidgetPresentationSessionOptions? options = null)
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel); await serverScenario(channel); await StopAsync(channel);
        });
        await using (var session = await WidgetPresentationSession.ConnectAsync(server.PipeName, options))
            await clientScenario(session, await EstablishAsync(session));
        await serverTask.WaitAsync(Limit);
    }
    private static BridgeIndexedRangeRequest Request(BridgeEnvelope envelope)
    {
        Assert.AreEqual(BridgeMessageTypes.ReadIndexedRange, envelope.Type);
        var result = BridgeJson.FromElement<BridgeIndexedRangeRequest>(envelope.Payload);
        Assert.AreEqual(Descriptor.Id, result.WidgetId); Assert.AreEqual(Descriptor.InstanceId, result.InstanceId);
        Assert.AreEqual(Descriptor.RuntimeGeneration, result.RuntimeGeneration); Assert.AreEqual(Descriptor.PresentationGeneration, result.PresentationGeneration);
        Assert.IsTrue(Guid.TryParseExact(result.Range.DemandId, "N", out _));
        return result;
    }
    private static IndexedCollectionRange Range(IndexedCollectionRangeRequest request) => new(Descriptor.InstanceId, "list", request.Source,
        "root", request.StartIndex, request.DemandId, Enumerable.Range(request.StartIndex, request.Count)
            .Select(index => new IndexedCollectionItem($"key-{index}", new ViewNode
                { Id = $"item-{index}", Kind = ViewNodeKind.Button, Text = $"Item {index}", ActionId = "play", CollectionItemKey = $"key-{index}" })).ToArray(), request.PinnedLayoutId);
    private static Task RangeReplyAsync(BridgeFrameChannel channel, BridgeEnvelope read)
    {
        var request = Request(read);
        return ReplyAsync(channel, read.RequestId, BridgeMessageTypes.IndexedRange,
            new BridgeIndexedRangeResponse(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration, Range(request.Range)));
    }
    private static async Task CancelReplyAsync(BridgeFrameChannel channel, BridgeEnvelope read, BridgeEnvelope cancel)
    {
        Assert.AreEqual(BridgeMessageTypes.CancelIndexedRange, cancel.Type);
        Assert.AreEqual(Request(read), BridgeJson.FromElement<BridgeIndexedRangeRequest>(cancel.Payload));
        // A range already sent during cancellation is still consumed for correlation,
        // never published to a replacement parent or demand.
        await RangeReplyAsync(channel, read);
        await ReplyAsync(channel, cancel.RequestId, BridgeMessageTypes.Acknowledged, new { cancelled = true });
    }
    private static async Task EstablishAsync(BridgeFrameChannel channel)
    {
        var list = await ReadAsync(channel);
        await ReplyAsync(channel, list.RequestId, BridgeMessageTypes.Widgets, new { revision = 1, isComplete = true, widgets = new[] { Descriptor } });
        await SnapshotReplyAsync(channel, (await ReadAsync(channel)).RequestId, 1, Source);
    }
    private static async Task<WidgetPresentationFrame> EstablishAsync(WidgetPresentationSession session)
    {
        await session.ListWidgetsAsync(); return await session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Visible);
    }
    private static async Task SnapshotReplyAsync(BridgeFrameChannel channel, long id, long sequence, IndexedCollectionDescriptor source, bool openModal = false)
    {
        var snapshot = new ViewSnapshot
        {
            ProtocolVersion = ProtocolConstants.IndexedCollectionVersion, Sequence = sequence, WidgetInstanceId = Descriptor.InstanceId,
            ActiveInputScopeId = "root", InitialFocusId = "refresh", Root = new ViewNode { Id = "root", Kind = ViewNodeKind.Stack, Children =
            [new ViewNode { Id = "refresh", Kind = ViewNodeKind.Button, Text = "Refresh", ActionId = "refresh" },
             new ViewNode { Id = "list", Kind = ViewNodeKind.IndexedCollection, IndexedCollection = source, AccessibilityLabel = "Games",
                ScrollAxis = ScrollAxis.Vertical, CollectionLayout = new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = 60 } }] },
        };
        if (openModal) snapshot = snapshot with
        {
            ActiveInputScopeId = "dialog", InitialFocusId = "dialog-button", Root = new ViewNode
            {
                Id = "modal-layer", Kind = ViewNodeKind.ModalLayer, Children = [snapshot.Root with { InputScopeId = "root" },
                    new ViewNode { Id = "dialog", Kind = ViewNodeKind.Stack, InputScopeId = "dialog", Children = [
                        new ViewNode { Id = "dialog-button", Kind = ViewNodeKind.Button, Text = "Close", ActionId = "close" }] }],
            },
        };
        using var document = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await ReplyAsync(channel, id, BridgeMessageTypes.Snapshot, new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint",
            baseSequence = 0, recoveryOriginSequence = 0, snapshot = document.RootElement.Clone(), renderStyles = new Dictionary<string, BridgeNodeRenderStyles>() });
    }
    private static Task<BridgeEnvelope> ReadAsync(BridgeFrameChannel channel) => channel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(Limit);
    private static async Task ReplyAsync(BridgeFrameChannel channel, long id, string type, object payload) =>
        await channel.WriteAsync(new BridgeEnvelope { RequestId = id, Type = type, Payload = BridgeJson.ToElement(payload) }, CancellationToken.None);
    private static async Task StopAsync(BridgeFrameChannel channel)
    {
        var stop = await ReadAsync(channel); Assert.AreEqual(BridgeMessageTypes.Stop, stop.Type);
        await ReplyAsync(channel, stop.RequestId, BridgeMessageTypes.Acknowledged, new { });
    }
}
