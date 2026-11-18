using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetPresentationSession.Tests;

public sealed partial class IndexedRangeSessionTests
{
    [TestMethod]
    public async Task SemanticRetentionIsBoundedAndLastOperationReleasesExactlyOnce()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, displayed) =>
        {
            var lease = await session.AcquireIndexedRangeAsync(displayed.Authority, "list", Source, 0, 1);
            var operations = Enumerable.Range(0, 16).Select(_ => lease.Retain()).ToArray();
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.Retain());
            await lease.DisposeAsync();
            foreach (var operation in operations.Take(15)) await operation.DisposeAsync();
            Assert.IsTrue(lease.IsCurrent);
            await operations[^1].DisposeAsync();
            Assert.IsFalse(lease.IsCurrent);
            foreach (var operation in operations) await operation.DisposeAsync();
        });
    }

    [TestMethod]
    public async Task RetainedPreviewHandsOffToChooserAfterPageOwnerReleaseAndReleasesOnce()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, displayed) =>
        {
            var lease = await session.AcquireIndexedRangeAsync(displayed.Authority, "list", Source, 0, 1);
            var operation = lease.Retain();
            await lease.DisposeAsync();
            Assert.IsTrue(lease.IsCurrent, "Preview keeps its still-current semantics after partial-page replacement.");
            var chooser = lease.Retain();
            await operation.DisposeAsync();
            Assert.IsTrue(lease.IsCurrent, "Chooser keeps the admitted proof after its preview closes.");
            await chooser.DisposeAsync();
            Assert.IsFalse(lease.IsCurrent);
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.Retain(), "Final release cannot be resurrected.");
            await operation.DisposeAsync();
            await lease.DisposeAsync();
        });
    }

    [TestMethod]
    public async Task QueryReplacementForceRetiresRetainedIntentProof()
    {
        await RunAsync(async channel =>
        {
            await LeaseReplyAsync(channel, await ReadAsync(channel));
            var refresh = await ReadAsync(channel);
            await SnapshotReplyAsync(channel, refresh.RequestId, 2, Source with { ContentRevision = 1 });
            await ReleaseReplyAsync(channel, await ReadAsync(channel));
        }, async (session, displayed) =>
        {
            var lease = await session.AcquireIndexedRangeAsync(displayed.Authority, "list", Source, 0, 1);
            var operation = lease.Retain();
            await lease.DisposeAsync();
            _ = await session.RefreshAsync(displayed.Authority);
            Assert.IsFalse(lease.IsCurrent, "Retention never bypasses query/content invalidation.");
            Assert.ThrowsExactly<WidgetPresentationSessionException>(() => lease.Retain(), "A stale retained owner cannot hand off authority.");
            await operation.DisposeAsync();
        });
    }
}
