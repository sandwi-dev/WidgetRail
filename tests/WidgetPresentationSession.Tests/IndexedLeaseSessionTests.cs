using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class IndexedRangeSessionTests
{
    [TestMethod]
    public async Task DisplayedIndexedActivationSurvivesAnUnrelatedPublicationBeforeUiDelivery()
    {
        Exception? failure = null;
        long? deliveredOrigin = null;
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source);
            var next = await ReadAsync(channel);
            if (next.Type == BridgeMessageTypes.IndexedInput)
            {
                deliveredOrigin = BridgeJson.FromElement<BridgeIndexedInputRequest>(next.Payload).Context.SnapshotSequence;
                await ReplyAsync(channel, next.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
                next = await ReadAsync(channel);
            }
            await ReleaseReplyAsync(channel, next);
        }, async (session, displayed) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(displayed.Authority, "list", Source, 0, 1);
            _ = await session.RefreshAsync(displayed.Authority);
            Assert.IsTrue(lease.IsCurrent, "An unrelated parent publication must retain item data.");
            // Deliberately keep the UI's displayed frame while the session has
            // already published the next snapshot on its receive thread.
            try
            {
                Assert.IsTrue(lease.ClaimsInput(displayed, "key-0", ControllerButton.A));
                Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(displayed, "key-0", ControllerButton.A));
            }
            catch (Exception error) { failure = error; }
        });
        Assert.IsNull(failure, $"The displayed unchanged action was dropped: {failure}");
        Assert.AreEqual(1L, deliveredOrigin, "The original displayed sequence must cross the wire without rebasing.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DisplayedIndexedInputRejectsDisabledCollectionOrChangedShortcut(bool changedShortcut)
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source,
                collectionDisabled: !changedShortcut, backShortcut: changedShortcut ? "new.back" : null);
            // No input frame can cross the wire after the semantic guard fails.
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, displayed) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(displayed.Authority, "list", Source, 0, 1);
            _ = await session.RefreshAsync(displayed.Authority);
            Assert.IsTrue(lease.IsCurrent);
            var button = changedShortcut ? ControllerButton.B : ControllerButton.A;
            var claims = Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.ClaimsInput(displayed, "key-0", button));
            Assert.AreEqual("indexed_input_stale", claims.Code);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(displayed, "key-0", button));
        });
    }

    [TestMethod]
    public async Task DisplayedIndexedInputRequiresAnOriginalSessionPublishedFrame()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, displayed) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(displayed.Authority, "list", Source, 0, 1);
            // Equal value does not establish origin provenance. Native callers
            // retain the admitted frame; they cannot invent a future/old frame.
            var copy = displayed with { };
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.ClaimsInput(copy, "key-0", ControllerButton.A));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(copy, "key-0", ControllerButton.A));
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.ClaimsInput(displayed, "missing-key", ControllerButton.A));
        });
    }

    [TestMethod]
    public async Task DisplayedInputClaimsDoNotTreatStaleNullAdmissionAsUnbound()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var input = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.IndexedInput, input.Type);
            await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new Dictionary<string, object?> { ["admission"] = null });
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source);
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            Assert.IsTrue(lease.ClaimsInput(frame.Authority, "key-0", ControllerButton.A));
            Assert.IsFalse(lease.ClaimsInput(frame.Authority, "key-0", ControllerButton.B));
            Assert.IsNull(await lease.AdmitInputAsync(frame.Authority, "key-0", ControllerButton.A));
            Assert.IsTrue(lease.ClaimsInput(frame.Authority, "key-0", ControllerButton.A), "A rejected by a racing worker snapshot remains claimed by the displayed action.");
            var current = await session.RefreshAsync(frame.Authority);
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.ClaimsInput(frame.Authority, "key-0", ControllerButton.B));
            Assert.IsFalse(lease.ClaimsInput(current.Authority, "key-0", ControllerButton.B));
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.ClaimsInput(current.Authority, "missing", ControllerButton.B));
        });
    }

    [TestMethod]
    [DataRow("joined", WidgetOperationAdmission.Joined)]
    [DataRow("rejectedInactive", WidgetOperationAdmission.RejectedInactive)]
    [DataRow("rejectedCapacity", WidgetOperationAdmission.RejectedCapacity)]
    [DataRow("replaced", WidgetOperationAdmission.Replaced)]
    public async Task IndexedInputPreservesAllAdmissionResults(string wire, WidgetOperationAdmission expected)
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var input = await ReadAsync(channel);
            await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new { admission = wire });
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            Assert.AreEqual(expected, await lease.AdmitInputAsync(frame.Authority, "key-0", ControllerButton.A));
        });
    }
    [TestMethod]
    public async Task OwnedLeasePreservesQueryDataButInputRequiresExactPresentedFrame()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var input = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.IndexedInput, input.Type);
            Assert.AreEqual(1L, BridgeJson.FromElement<BridgeIndexedInputRequest>(input.Payload).Context.SnapshotSequence);
            await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source);
            input = await ReadAsync(channel);
            Assert.AreEqual(2L, BridgeJson.FromElement<BridgeIndexedInputRequest>(input.Payload).Context.SnapshotSequence);
            await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 2);
            Assert.IsTrue(lease.IsCurrent);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(frame.Authority, "key-0", ControllerButton.A));
            var current = await session.RefreshAsync(frame.Authority);
            Assert.IsTrue(lease.IsCurrent);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(frame.Authority, "key-0", ControllerButton.A));
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(current.Authority, "key-0", ControllerButton.A));
            Assert.ThrowsExactly<NotSupportedException>(() => ((IList<IndexedCollectionItem>)lease.Range.Items).Clear());
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ModalPreservesLeaseAndArtworkButOnlyPinnedProjectionKeepsParentInput(bool pinned)
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel), artwork: true);
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source, openModal: true);
            if (pinned)
            {
                var input = await ReadAsync(channel);
                var context = BridgeJson.FromElement<BridgeIndexedInputRequest>(input.Payload).Context;
                Assert.AreEqual("root", context.InputScopeId); Assert.AreEqual(2L, context.SnapshotSequence);
                await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            }
            var artwork = await ReadAsync(channel);
            await ArtworkReplyAsync(channel, artwork);
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1, pinned ? "host.full-widget" : null);
            var modal = await session.RefreshAsync(frame.Authority);
            Assert.IsTrue(lease.IsCurrent);
            if (pinned) Assert.IsTrue(lease.ClaimsInput(frame, "key-0", ControllerButton.A));
            else
            {
                Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.ClaimsInput(frame, "key-0", ControllerButton.A));
                await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(frame, "key-0", ControllerButton.A));
            }
            if (pinned) Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(modal.Authority, "key-0", ControllerButton.A));
            else await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(modal.Authority, "key-0", ControllerButton.A));
            Assert.IsNull(await lease.ResolveArtworkAsync("key-0", "cover"));
        });
    }

    [TestMethod]
    public async Task NewQueryAutomaticallyReleasesOwnedRetention()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source with { QueryGeneration = 2 });
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 2);
            _ = await session.RefreshAsync(frame.Authority);
            Assert.IsFalse(lease.IsCurrent);
            await lease.DisposeAsync().AsTask().WaitAsync(Limit);
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(frame.Authority, "key-0", ControllerButton.A));
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.AdmitInputAsync(frame, "key-0", ControllerButton.A));
        });
    }

    [TestMethod]
    public async Task MalformedDeliveredLeaseWithdrawsExactDemandBeforeReturningFailure()
    {
        await RunAsync(async channel =>
        {
            var acquire = await ReadAsync(channel); var request = AcquireRequest(acquire);
            await ReplyAsync(channel, acquire.RequestId, BridgeMessageTypes.IndexedLease,
                new BridgeIndexedLeaseResponse("foreign", request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration,
                    new(Guid.NewGuid().ToString("N"), Range(request.Range)), EmptyRangeStyles(Range(request.Range))));
            var cancel = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.CancelIndexedRange, cancel.Type);
            Assert.AreEqual(request, BridgeJson.FromElement<BridgeIndexedRangeRequest>(cancel.Payload));
            await ReplyAsync(channel, cancel.RequestId, BridgeMessageTypes.Acknowledged, new { cancelled = true });
            await RangeReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await Assert.ThrowsAsync<BridgeProtocolException>(() => session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1));
            Assert.AreEqual(1, (await session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 0, 1)).Items.Count);
        });
    }

    [TestMethod]
    public async Task WorkerRejectionDoesNotSendCancellationOrPoisonNextLease()
    {
        await RunAsync(async channel =>
        {
            var rejected = await ReadAsync(channel); _ = AcquireRequest(rejected);
            await ReplyAsync(channel, rejected.RequestId, BridgeMessageTypes.Error, new { code = "indexed_range_failed", message = "Unavailable" });
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1));
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            Assert.IsTrue(lease.IsCurrent);
        });
    }

    [TestMethod]
    public async Task CancelledAcquisitionWithdrawsLeaseEvenWhenSuccessArrivesDuringCancellation()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            var acquire = await ReadAsync(channel); received.SetResult();
            var cancel = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.CancelIndexedRange, cancel.Type);
            await LeaseReplyAsync(channel, acquire);
            await ReplyAsync(channel, cancel.RequestId, BridgeMessageTypes.Acknowledged, new { cancelled = true });
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            using var cancellation = new CancellationTokenSource();
            var pending = session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1, cancellationToken: cancellation.Token);
            await received.Task.WaitAsync(Limit); cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
            await using var fresh = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            Assert.IsTrue(fresh.IsCurrent);
        });
    }

    [TestMethod]
    public async Task OwnedItemBudgetRejectsAdditionalAcquisitionBeforeSending()
    {
        await RunAsync(async channel =>
        {
            for (var i = 0; i < 16; ++i) await LeaseReplyAsync(channel, await ReadAsync(channel));
            for (var i = 0; i < 16; ++i) await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            var leases = new List<WidgetPresentationIndexedLease>();
            try
            {
                for (var i = 0; i < 16; ++i) leases.Add(await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, i * 64, 64));
                var error = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 2048, 1));
                Assert.AreEqual("indexed_lease_saturated", error.Code);
            }
            finally { await Task.WhenAll(leases.Select(lease => lease.DisposeAsync().AsTask())); }
        });
    }

    [TestMethod]
    public async Task ArtworkCancellationRetainsCorrelationWithoutBlockingRowInput()
    {
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel), artwork: true);
            var artwork = await ReadAsync(channel); received.SetResult();
            var input = await ReadAsync(channel); Assert.AreEqual(BridgeMessageTypes.IndexedInput, input.Type);
            await ReplyAsync(channel, input.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            var cancel = await ReadAsync(channel); Assert.AreEqual(BridgeMessageTypes.CancelIndexedArtwork, cancel.Type);
            Assert.AreEqual(BridgeJson.FromElement<BridgeIndexedArtworkRequest>(artwork.Payload), BridgeJson.FromElement<BridgeIndexedArtworkRequest>(cancel.Payload));
            await ReplyAsync(channel, cancel.RequestId, BridgeMessageTypes.Acknowledged, new { cancelled = true });
            await ReplyAsync(channel, artwork.RequestId, BridgeMessageTypes.Error, new { code = "indexed_artwork_cancelled", message = "Cancelled" });
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            using var cancellation = new CancellationTokenSource();
            var artwork = lease.ResolveArtworkAsync("key-0", "cover", cancellation.Token);
            await received.Task.WaitAsync(Limit);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await lease.AdmitInputAsync(frame.Authority, "key-0", ControllerButton.A));
            cancellation.Cancel(); await Assert.ThrowsAsync<OperationCanceledException>(() => artwork);
            Assert.IsTrue(lease.IsCurrent);
        });
    }

    [TestMethod]
    public async Task SessionDisposalReleasesUndisposedLeaseBeforeStoppingBridge()
    {
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel); await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel)); await StopAsync(channel);
        });
        var session = await WidgetPresentationSession.ConnectAsync(server.PipeName);
        var frame = await EstablishAsync(session);
        var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
        await session.DisposeAsync();
        Assert.IsFalse(lease.IsCurrent);
        await lease.DisposeAsync();
        await serverTask.WaitAsync(Limit);
    }

    [TestMethod]
    public async Task BulkRetirementWaitsForAdmissionAndLeavesProviderAndControlCapacity()
    {
        await RunAsync(async channel =>
        {
            for (var index = 0; index < 32; ++index) await LeaseReplyAsync(channel, await ReadAsync(channel));
            var releases = new List<BridgeEnvelope>();
            var sawRange = false; var sawAction = false;
            while (releases.Count < 4 || !sawRange || !sawAction)
            {
                var request = await ReadAsync(channel);
                switch (request.Type)
                {
                    case BridgeMessageTypes.ReleaseIndexedLease: releases.Add(request); Assert.IsTrue(releases.Count <= 4); break;
                    case BridgeMessageTypes.ReadIndexedRange: sawRange = true; await RangeReplyAsync(channel, request); break;
                    case BridgeMessageTypes.Action: sawAction = true; await ReplyAsync(channel, request.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" }); break;
                    default: Assert.Fail("Unexpected request during held release batch: " + request.Type); break;
                }
            }
            for (var batch = 0; batch < 8; ++batch)
            {
                if (batch != 0)
                {
                    releases.Clear();
                    for (var index = 0; index < 4; ++index) releases.Add(await ReadAsync(channel));
                }
                // The final batch waits >2 seconds locally but <2 seconds after write.
                await Task.Delay(350);
                foreach (var release in releases) await ReleaseReplyAsync(channel, release);
            }
        }, async (session, frame) =>
        {
            var leases = new List<WidgetPresentationIndexedLease>();
            for (var index = 0; index < 32; ++index) leases.Add(await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, index, 1));
            var retiring = Task.WhenAll(leases.Select(lease => lease.DisposeAsync().AsTask()));
            var range = session.ReadIndexedRangeAsync(frame.Authority, "list", Source, 100, 1);
            var action = session.SendActionAsync(frame.Authority, new("refresh", "refresh", InputScopeId: "root"));
            Assert.AreEqual(1, (await range.WaitAsync(Limit)).Items.Count);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await action.WaitAsync(Limit));
            await retiring.WaitAsync(Limit);
            Assert.IsFalse(session.Diagnostics.Any(item => item.Code is "transport_closed" or "indexed_release_failed"));
        });
    }

    [TestMethod]
    public async Task FullRangeLaneRejectsArtworkBeforeWriteAndStillAdmitsCancellation()
    {
        var rangesRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel), artwork: true);
            var ranges = new Dictionary<string, BridgeEnvelope>();
            for (var index = 0; index < 4; ++index)
            {
                var read = await ReadAsync(channel);
                ranges.Add(Request(read).Range.DemandId, read);
            }
            rangesRead.SetResult();
            var action = await ReadAsync(channel);
            Assert.AreEqual(BridgeMessageTypes.Action, action.Type);
            await ReplyAsync(channel, action.RequestId, BridgeMessageTypes.Acknowledged, new { admission = "enqueued" });
            for (var index = 0; index < 4; ++index)
            {
                var cancel = await ReadAsync(channel);
                Assert.AreEqual(BridgeMessageTypes.CancelIndexedRange, cancel.Type);
                var demandId = BridgeJson.FromElement<BridgeIndexedRangeRequest>(cancel.Payload).Range.DemandId;
                await CancelReplyAsync(channel, ranges[demandId], cancel);
            }
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, frame) =>
        {
            await using var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
            using var cancellation = new CancellationTokenSource();
            var reads = Enumerable.Range(0, 4).Select(index => session.ReadIndexedRangeAsync(frame.Authority, "list", Source, index * 10, 1,
                cancellationToken: cancellation.Token)).ToArray();
            await rangesRead.Task.WaitAsync(Limit);
            var error = await Assert.ThrowsAsync<WidgetPresentationSessionException>(() => lease.ResolveArtworkAsync("key-0", "cover"));
            Assert.AreEqual("indexed_artwork_saturated", error.Code);
            Assert.AreEqual(WidgetOperationAdmission.Enqueued, await session.SendActionAsync(frame.Authority, new("refresh", "refresh", InputScopeId: "root")));
            cancellation.Cancel();
            foreach (var read in reads) await Assert.ThrowsAsync<OperationCanceledException>(() => read);
            Assert.IsTrue(lease.IsCurrent);
        });
    }
    [TestMethod]
    public async Task LeaseDisposalBoundsAdmissionBehindStalledControls()
    {
        var occupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ScriptedBridgeServer();
        var serverTask = server.RunAuthenticatedAsync(async channel =>
        {
            await EstablishAsync(channel);
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            // Default admission reserves seven control slots, separate from the
            // four provider and four cancellation slots. Never acknowledge them.
            for (var index = 0; index < 7; ++index)
                Assert.AreEqual(BridgeMessageTypes.Action, (await ReadAsync(channel)).Type);
            occupied.SetResult();
            try
            {
                var unexpected = await ReadAsync(channel);
                Assert.Fail("Release or Stop bypassed saturated control admission: " + unexpected.Type);
            }
            catch (System.IO.IOException) { }
        });
        await using var session = await WidgetPresentationSession.ConnectAsync(server.PipeName);
        var frame = await EstablishAsync(session);
        var lease = await session.AcquireIndexedRangeAsync(frame.Authority, "list", Source, 0, 1);
        var controls = Enumerable.Range(0, 7).Select(_ => session.SendActionAsync(frame.Authority,
            new("refresh", "refresh", InputScopeId: "root"))).ToArray();
        await occupied.Task.WaitAsync(Limit);
        // The session itself remains open here; its shutdown guard cannot be
        // responsible for releasing this await.
        await lease.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.IsFalse(lease.IsCurrent);
        Assert.IsTrue(session.Diagnostics.Any(item => item.Code == "transport_closed"));
        foreach (var control in controls)
        {
            try { await control; Assert.Fail("A stalled action received a false success."); }
            catch (WidgetPresentationSessionException) { }
        }
        await serverTask.WaitAsync(Limit);
    }

    private static BridgeIndexedRangeRequest AcquireRequest(BridgeEnvelope envelope)
    {
        Assert.AreEqual(BridgeMessageTypes.AcquireIndexedRange, envelope.Type);
        var request = BridgeJson.FromElement<BridgeIndexedRangeRequest>(envelope.Payload);
        Assert.IsTrue(Guid.TryParseExact(request.Range.DemandId, "N", out _));
        return request;
    }
    private static Task LeaseReplyAsync(BridgeFrameChannel channel, BridgeEnvelope envelope, bool artwork = false)
    {
        var request = AcquireRequest(envelope);
        var range = Range(request.Range);
        if (artwork) range = range with { Items = range.Items.Select(item => item with { Root = item.Root with { FocusBackgroundArtworkHandle = "cover" } }).ToArray() };
        return ReplyAsync(channel, envelope.RequestId, BridgeMessageTypes.IndexedLease,
            new BridgeIndexedLeaseResponse(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration,
                new(Guid.NewGuid().ToString("N"), range), EmptyRangeStyles(range)));
    }
    private static Task ReleaseReplyAsync(BridgeFrameChannel channel, BridgeEnvelope envelope)
    {
        Assert.AreEqual(BridgeMessageTypes.ReleaseIndexedLease, envelope.Type);
        var request = BridgeJson.FromElement<BridgeIndexedLeaseRequest>(envelope.Payload);
        Assert.AreEqual(Descriptor.InstanceId, request.InstanceId); Assert.AreEqual(Descriptor.RuntimeGeneration, request.RuntimeGeneration);
        Assert.IsTrue(Guid.TryParseExact(request.LeaseId, "N", out _));
        return ReplyAsync(channel, envelope.RequestId, BridgeMessageTypes.Acknowledged, new { released = true });
    }
    private static Task ArtworkReplyAsync(BridgeFrameChannel channel, BridgeEnvelope envelope)
    {
        Assert.AreEqual(BridgeMessageTypes.ResolveIndexedArtwork, envelope.Type);
        var request = BridgeJson.FromElement<BridgeIndexedArtworkRequest>(envelope.Payload);
        return ReplyAsync(channel, envelope.RequestId, BridgeMessageTypes.IndexedArtwork,
            new BridgeIndexedArtworkResponse(request.WidgetId, request.InstanceId, request.RuntimeGeneration, request.PresentationGeneration,
                request.Item, request.ArtworkHandle, request.DemandId, string.Empty, ReadOnlyMemory<byte>.Empty));
    }
}
