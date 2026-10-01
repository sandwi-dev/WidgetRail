using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class IndexedRangeSessionTests
{
    [TestMethod]
    public async Task AppearanceRefreshPreservesOrdinarySnapshotAndIndexedActionLease()
    {
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await begin.Task.WaitAsync(Limit);
            await Appearance(channel, 7);
            var normal = await ReadAsync(channel);
            await FrameStyles(channel, normal, 7);
            var indexed = await ReadAsync(channel);
            await LeaseStyles(channel, indexed, 7);
            for (var index = 0; index < 2; ++index)
            {
                var input = await ReadAsync(channel);
                Assert.AreEqual(BridgeMessageTypes.IndexedInput, input.Type);
                Assert.AreEqual(1, BridgeJson.FromElement<BridgeIndexedInputRequest>(input.Payload).Context.SnapshotSequence);
                await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            }
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, original) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(original.Authority, "list", Source, 0, 1);
            var oldStyles = lease.RenderStyles;
            var range = lease.Range;
            var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lease.RenderStylesChanged += (_, _) => updated.TrySetResult();
            begin.SetResult();
            await updated.Task.WaitAsync(Limit);
            var current = session.GetState(Descriptor.Id)!.LastGood!;
            Assert.AreEqual(7, current.AppearanceRevision);
            Assert.AreSame(original.Snapshot, current.Snapshot);
            Assert.AreSame(original.Authority, current.Authority);
            Assert.AreSame(original.Descriptor, current.Descriptor);
            Assert.AreSame(original.WindowPreviews, current.WindowPreviews);
            Assert.AreEqual("#765432", current.RenderStyles["root"].Base["color"].Text);
            Assert.AreSame(range, lease.Range);
            Assert.AreEqual(7, lease.AppearanceRevision);
            Assert.AreEqual("#765432", lease.RenderStyles["item-0"].Focused["color"].Text);
            Assert.AreEqual(0, oldStyles["item-0"].Focused.Count);
            Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, BridgeNodeRenderStyles>)lease.RenderStyles).Clear());
            Assert.IsTrue(lease.IsCurrent);
            Assert.IsTrue(lease.ClaimsInput(current, "key-0", ControllerButton.A), "Refreshed frames retain genuine session provenance.");
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(original, "key-0", ControllerButton.A));
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(current, "key-0", ControllerButton.A));
        });
    }

    [TestMethod]
    public async Task NewerAppearanceSupersedesAnInflightReplyWithoutPublishingOldStyles()
    {
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await begin.Task.WaitAsync(Limit);
            await Appearance(channel, 2);
            var first = await ReadAsync(channel);
            await Appearance(channel, 3);
            await FrameStyles(channel, first, 2);
            await FrameStyles(channel, await ReadAsync(channel), 3);
            await LeaseStyles(channel, await ReadAsync(channel), 3);
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            var published = new System.Collections.Concurrent.ConcurrentQueue<long>();
            session.PresentationChanged += (_, args) => { if (args.State.LastGood is { } next) published.Enqueue(next.AppearanceRevision); };
            var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lease.RenderStylesChanged += (_, _) => updated.TrySetResult();
            begin.SetResult();
            await updated.Task.WaitAsync(Limit);
            CollectionAssert.AreEqual(new long[] { 3 }, published.ToArray());
            Assert.AreEqual(3, lease.AppearanceRevision);
        });
    }

    [TestMethod]
    public async Task OrdinarySnapshotReplacementCannotBeOverwrittenByLateStyleReply()
    {
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await begin.Task.WaitAsync(Limit);
            await Appearance(channel, 2);
            var obsolete = await ReadAsync(channel);
            requested.SetResult();
            var refresh = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.GetSnapshot, refresh.Type);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source);
            await FrameStyles(channel, obsolete, 2);
            var current = await ReadAsync(channel);
            Assert.AreEqual(2, BridgeJson.FromElement<BridgePresentationStylesRequest>(current.Payload).SnapshotSequence);
            await FrameStyles(channel, current, 2);
        }, async (session, original) =>
        {
            var styled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            session.PresentationChanged += (_, args) =>
            {
                if (args.State.LastGood is { AppearanceRevision: 2 } next)
                {
                    Assert.AreEqual(2, next.Authority.SnapshotSequence);
                    styled.TrySetResult();
                }
            };
            begin.SetResult();
            await requested.Task.WaitAsync(Limit);
            var replacement = await session.RefreshAsync(original.Authority);
            await styled.Task.WaitAsync(Limit);
            var final = session.GetState(Descriptor.Id)!.LastGood!;
            Assert.AreSame(replacement.Snapshot, final.Snapshot);
            Assert.AreEqual(2, final.AppearanceRevision);
        });
    }

    [TestMethod]
    public async Task LeaseRetirementDuringStyleRefreshRejectsLateReply()
    {
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await begin.Task.WaitAsync(Limit);
            await Appearance(channel, 2);
            await FrameStyles(channel, await ReadAsync(channel), 2);
            var refresh = await ReadAsync(channel);
            requested.SetResult();
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
            await LeaseStyles(channel, refresh, 2);
        }, async (session, frame) =>
        {
            var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            var changes = 0;
            lease.RenderStylesChanged += (_, _) => Interlocked.Increment(ref changes);
            begin.SetResult();
            await requested.Task.WaitAsync(Limit);
            await lease.DisposeAsync();
            Assert.IsFalse(lease.IsCurrent);
            Assert.AreEqual(0, lease.AppearanceRevision);
            Assert.AreEqual(0, changes);
        });
    }

    [TestMethod]
    [DataRow("foreign-lease")]
    [DataRow("foreign-node")]
    [DataRow("older-revision")]
    public async Task InvalidStyleReplyRetainsPreviousStylesAndLease(string defect)
    {
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await begin.Task.WaitAsync(Limit);
            await Appearance(channel, 2);
            await FrameStyles(channel, await ReadAsync(channel), 2);
            await LeaseStyles(channel, await ReadAsync(channel), 2, defect);
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            var original = lease.RenderStyles;
            begin.SetResult();
            var end = DateTime.UtcNow + Limit;
            while (!session.Diagnostics.Any(entry => entry.Code == "style_refresh_failed"))
            { if (DateTime.UtcNow > end) Assert.Fail("No style validation diagnostic"); await Task.Delay(10); }
            Assert.AreSame(original, lease.RenderStyles);
            Assert.IsTrue(lease.IsCurrent);
            Assert.AreEqual(0, lease.AppearanceRevision);
        });
    }

    private static Task Appearance(BridgeFrameChannel channel, long revision) => channel.WriteAsync(new BridgeEnvelope
        { Type = BridgeMessageTypes.AppearanceChanged, Payload = BridgeJson.ToElement(new { revision }) }, CancellationToken.None).AsTask();
    private static IReadOnlyDictionary<string, BridgeNodeRenderStyles> StyleMap(params string[] ids) => ids.ToDictionary(id => id, _ => new BridgeNodeRenderStyles
    {
        Base = new Dictionary<string, BridgeComputedStyleValue> { ["color"] = new() { Kind = WrssValueKind.Color, Text = "#765432" } },
        Focused = new Dictionary<string, BridgeComputedStyleValue> { ["color"] = new() { Kind = WrssValueKind.Color, Text = "#765432" } },
        Pressed = new Dictionary<string, BridgeComputedStyleValue>(),
    });
    private static Task FrameStyles(BridgeFrameChannel channel, BridgeEnvelope request, long revision)
    {
        Assert.AreEqual(BridgeMessageTypes.RefreshPresentationStyles, request.Type);
        var value = BridgeJson.FromElement<BridgePresentationStylesRequest>(request.Payload);
        return ReplyAsync(channel, request.RequestId, BridgeMessageTypes.PresentationStyles,
            new BridgePresentationStylesResponse(value.WidgetId, value.InstanceId, value.RuntimeGeneration, value.PresentationGeneration,
                value.SnapshotSequence, revision, StyleMap("root", "refresh", "list")));
    }
    private static Task LeaseStyles(BridgeFrameChannel channel, BridgeEnvelope request, long revision, string? defect = null)
    {
        Assert.AreEqual(BridgeMessageTypes.RefreshIndexedStyles, request.Type);
        var value = BridgeJson.FromElement<BridgeIndexedLeaseRequest>(request.Payload);
        return ReplyAsync(channel, request.RequestId, BridgeMessageTypes.IndexedStyles,
            new BridgeIndexedStylesResponse(value.WidgetId, value.InstanceId, value.RuntimeGeneration, value.PresentationGeneration,
                defect == "foreign-lease" ? Guid.NewGuid().ToString("N") : value.LeaseId, defect == "older-revision" ? revision - 1 : revision,
                StyleMap(defect == "foreign-node" ? "foreign" : "item-0")));
    }
}
