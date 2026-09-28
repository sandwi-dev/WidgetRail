using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using Microsoft.UI.Dispatching;
using System.Runtime.CompilerServices;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed record WidgetIndexedRow(IndexedCollectionItem Item, WidgetPresentationIndexedLease Lease,
    WidgetIndexedRows Owner);

/// <summary>Maps native range demand to explicitly owned sandboxed widget data.</summary>
internal sealed class WidgetIndexedRows : IAsyncDisposable
{
    private sealed record Presentation(WidgetPresentationFrame Frame, ViewNode Collection);
    private readonly PresentationSession session;
    private static readonly ConditionalWeakTable<PresentationSession, SemaphoreSlim> Admissions = new();
    private readonly SemaphoreSlim admission;
    private Presentation presentation;
    internal IndexedItemsSource<WidgetIndexedRow> Items { get; }
    internal WidgetPresentationFrame Frame => Volatile.Read(ref presentation).Frame;
    internal ViewNode Declaration => Volatile.Read(ref presentation).Collection;
    internal Action<Exception>? Failed { get; set; }

    internal WidgetIndexedRows(PresentationSession session, WidgetPresentationFrame frame, ViewNode collection,
        DispatcherQueue dispatcher)
    {
        this.session = session;
        if (session.MaximumConcurrentIndexedRequests < 1) throw new InvalidOperationException("The session has no indexed provider capacity.");
        admission = Admissions.GetValue(session, owner => new(owner.MaximumConcurrentIndexedRequests, owner.MaximumConcurrentIndexedRequests));
        var source = collection.IndexedCollection ?? throw new ArgumentException("Missing indexed source.", nameof(collection));
        presentation = new(frame, collection);
        Items = new(new(new(frame.Authority.RuntimeGeneration, frame.Authority.WidgetInstanceId, collection.Id), source.QueryGeneration),
            source.Count, dispatcher, ReadAsync, contentRevision: source.ContentRevision);
    }

    internal bool CanUpdate(WidgetPresentationFrame frame, ViewNode collection)
    {
        var previous = presentation;
        var source = previous.Collection.IndexedCollection!;
        return SameOwner(previous.Frame.Authority, frame.Authority) && previous.Collection.Id == collection.Id &&
            collection.IndexedCollection is { } next && next.SourceId == source.SourceId &&
            next.QueryGeneration == source.QueryGeneration && next.Count == source.Count && next.ContentRevision >= source.ContentRevision;
    }

    internal void Update(WidgetPresentationFrame frame, ViewNode collection)
    {
        if (!CanUpdate(frame, collection)) throw new InvalidOperationException("The indexed query requires a new native source.");
        Volatile.Write(ref presentation, new(frame, collection));
        Items.RefreshContent(collection.IndexedCollection!.ContentRevision);
        Items.RetryFailedPages();
    }

    private async Task<IndexedRangeResult<WidgetIndexedRow>> ReadAsync(IndexedRangeRequest request, CancellationToken cancellation)
    {
        var current = Volatile.Read(ref presentation);
        var source = current.Collection.IndexedCollection!;
        if (source.ContentRevision != request.ContentRevision) throw new OperationCanceledException("Indexed content changed before admission.");
        // The UI may not have applied an unrelated parent revision yet. Data is
        // query-owned; input still carries the exact frame actually displayed.
        var frame = session.GetState(current.Frame.Authority.WidgetId)?.LastGood ?? current.Frame;
        if (!SameOwner(current.Frame.Authority, frame.Authority)) throw new OperationCanceledException("Indexed widget owner changed.");
        WidgetPresentationIndexedLease lease;
        await admission.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            // Admission can wait behind artwork. Resolve the newest unchanged
            // query after that wait, without changing input's displayed frame.
            frame = session.GetState(current.Frame.Authority.WidgetId)?.LastGood ?? frame;
            if (!SameOwner(current.Frame.Authority, frame.Authority)) throw new OperationCanceledException("Indexed widget owner changed.");
            lease = await session.AcquireIndexedRangeAsync(frame.Authority, current.Collection.Id, source,
                request.StartIndex, request.Count, cancellationToken: cancellation).ConfigureAwait(false);
        }
        finally { admission.Release(); }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!lease.IsCurrent) throw new OperationCanceledException("Indexed rows retired before native admission.");
            return new(request.Query, request.RequestId, request.StartIndex,
                lease.Range.Items.Select(item => new KeyedCollectionItem<WidgetIndexedRow>(item.Key, new(item, lease, this))).ToArray(),
                request.ContentRevision, lease);
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    internal async Task<WidgetEncodedArtwork?> ResolveArtworkAsync(WidgetIndexedRow row, string handle, CancellationToken cancellation)
    {
        await admission.WaitAsync(cancellation).ConfigureAwait(false);
        try { return await row.Lease.ResolveArtworkAsync(row.Item.Key, handle, cancellation).ConfigureAwait(false); }
        finally { admission.Release(); }
    }

    internal static bool SameOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.WidgetInstanceId == second.WidgetInstanceId &&
        first.RuntimeGeneration == second.RuntimeGeneration && first.PresentationGeneration == second.PresentationGeneration &&
        first.SessionGeneration == second.SessionGeneration;

    public ValueTask DisposeAsync() => Items.DisposeAsync();
}
