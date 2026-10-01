using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>Owns one worker's semantic range; disposal never targets a replacement worker.</summary>
internal sealed class WidgetProcessIndexedLease : IAsyncDisposable
{
    private readonly WidgetProcessClient owner;
    private readonly WidgetProcessSession session;
    private readonly IndexedCollectionRangeRequest request;
    private readonly Action retired;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private Task? disposal;
    private bool disposed;
    internal IndexedCollectionLease Lease { get; }

    internal WidgetProcessIndexedLease(WidgetProcessClient owner, WidgetProcessSession session,
        IndexedCollectionLease lease, IndexedCollectionRangeRequest request, Action retired)
    { this.owner = owner; this.session = session; Lease = lease; this.request = request; this.retired = retired; }

    internal async Task<WidgetOperationAdmission?> AdmitInputAsync(IndexedCollectionInputRequest input,
        IndexedCollectionInputContext correlation, CancellationToken cancellationToken = default)
    {
        IndexedCollectionInputContract.ValidateInput(input);
        lock (gate) ObjectDisposedException.ThrowIf(disposed, this);
        if (input.Item.LeaseId != Lease.LeaseId || !Lease.Range.Items.Any(item => item.Key == input.Item.ItemKey))
            throw new ArgumentException("Input does not belong to this indexed lease.", nameof(input));
        return await owner.AdmitIndexedInputAsync(session, request, input, correlation, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<WidgetEncodedArtwork?> ResolveArtworkAsync(string itemKey, string artworkHandle,
        CancellationToken cancellationToken = default)
    {
        CancellationTokenSource demand;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            demand = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        }
        using (demand)
        {
            var reference = new IndexedCollectionItemReference(Lease.LeaseId, itemKey);
            IndexedCollectionInputContract.ValidateReference(reference);
            if (!Lease.Range.Items.Any(item => item.Key == itemKey)) throw new ArgumentException("Artwork item is outside this indexed lease.", nameof(itemKey));
            return await owner.ResolveIndexedArtworkAsync(session, request, reference, artworkHandle, demand.Token).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) return new(disposal);
            disposed = true;
            return new(disposal = ReleaseAsync());
        }
    }
    private async Task ReleaseAsync()
    {
        try
        {
            await lifetime.CancelAsync().ConfigureAwait(false);
            await owner.ReleaseIndexedLeaseAsync(session, Lease.LeaseId).ConfigureAwait(false);
        }
        finally { lifetime.Dispose(); retired(); }
    }
}
