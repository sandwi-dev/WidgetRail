using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;
using Microsoft.UI.Dispatching;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
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
    private sealed record SourcePresentation(WidgetPresentationBinding Binding, ViewNode Collection)
    { internal WidgetPresentationFrame Frame => Binding.Frame; }
    private readonly PresentationSession session;
    private readonly DispatcherQueue dispatcher;
    private static readonly ConditionalWeakTable<PresentationSession, SemaphoreSlim> Admissions = new();
    private readonly SemaphoreSlim admission;
    private SourcePresentation presentation;
    internal IndexedItemsSource<WidgetIndexedRow> Items { get; }
    internal PresentationSession Session => session;
    internal WidgetPresentationBinding Presentation => Volatile.Read(ref presentation).Binding;
    internal WidgetPresentationFrame Frame => Presentation.Frame;
    internal string ActiveScope => Presentation.Scope;
    internal ViewNode Declaration => Volatile.Read(ref presentation).Collection;
    internal Action<Exception>? Failed { get; set; }
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource activity = new();
    internal bool IsPresentationActive => Items.IsPresentationActive;
    internal async Task SetPresentationActiveAsync(bool active)
    {
        if (Items.IsPresentationActive == active) return;
        if (!active)
        {
            activity.Cancel();
            var drain = Items.SetPresentationActiveAsync(false);
            if (continuation is { } pending) await pending;
            await drain;
        }
        else
        {
            activity.Dispose(); activity = new();
            ContinuationFailed = false;
            await Items.SetPresentationActiveAsync(true);
        }
    }
    private TaskCompletionSource? continuationPublication;
    private Task? continuation;
    private long continuationRevision;
    private int emptyContinuations;
    internal bool ContinuationFailed { get; private set; }
    internal bool ContinuationPaused { get; private set; }
    internal event Action? DiscoveryChanged;
    internal event Action? StylesChanged;

    internal WidgetIndexedRows(PresentationSession session, WidgetPresentationFrame frame, ViewNode collection, DispatcherQueue dispatcher)
        : this(session, WidgetPresentationBinding.ForMain(frame), collection, dispatcher) { }
    internal bool CanUpdate(WidgetPresentationFrame frame, ViewNode collection) => CanUpdate(WidgetPresentationBinding.ForMain(frame), collection);
    internal void Update(WidgetPresentationFrame frame, ViewNode collection) => Update(WidgetPresentationBinding.ForMain(frame), collection);

    internal WidgetIndexedRows(PresentationSession session, WidgetPresentationBinding binding, ViewNode collection,
        DispatcherQueue dispatcher)
    {
        this.session = session;
        this.dispatcher = dispatcher;
        if (session.MaximumConcurrentIndexedRequests < 1) throw new InvalidOperationException("The session has no indexed provider capacity.");
        admission = Admissions.GetValue(session, owner => new(owner.MaximumConcurrentIndexedRequests, owner.MaximumConcurrentIndexedRequests));
        var source = collection.IndexedCollection ?? throw new ArgumentException("Missing indexed source.", nameof(collection));
        var frame = binding.Frame;
        presentation = new(binding, collection);
        Items = new(new(new(frame.Authority.RuntimeGeneration, frame.Authority.WidgetInstanceId, collection.Id), source.QueryGeneration),
            source.Count, dispatcher, ReadAsync, contentRevision: source.ContentRevision);
        if (source.Discovery is not null)
        {
            Items.CanLoadMore = () => Declaration.IndexedCollection?.Discovery is
                { HasMore: true, Status: DiscoveredCollectionStatus.Ready } && !ContinuationFailed && !ContinuationPaused && continuation is not { IsCompleted: false };
            Items.LoadMore = token => ContinueAsync(false, token);
        }
    }

    internal bool CanUpdate(WidgetPresentationBinding binding, ViewNode collection)
    {
        var previous = presentation;
        var source = previous.Collection.IndexedCollection!;
        return previous.Binding.SameSurface(binding) && previous.Collection.Id == collection.Id &&
            collection.IndexedCollection is { } next && next.SourceId == source.SourceId &&
            (next.Discovery is null) == (source.Discovery is null) &&
            next.QueryGeneration == source.QueryGeneration &&
            (next.Discovery is not null ? IndexedCollectionContract.RetainsPrefix(next, source) :
                next.Count == source.Count && next.ContentRevision >= source.ContentRevision);
    }

    internal void Update(WidgetPresentationBinding binding, ViewNode collection)
    {
        if (!CanUpdate(binding, collection)) throw new InvalidOperationException("The indexed query requires a new native source.");
        var appearanceOnly = binding.IsAppearanceUpdateOf(presentation.Binding) && ReferenceEquals(collection, presentation.Collection);
        Volatile.Write(ref presentation, new(binding, collection));
        if (appearanceOnly) return;
        Items.RefreshContent(collection.IndexedCollection!.ContentRevision);
        Items.Append(collection.IndexedCollection.Count);
        Items.RetryFailedPages();
        if (collection.IndexedCollection.Discovery is { Status: not DiscoveredCollectionStatus.Loading } next && next.Revision > continuationRevision)
            continuationPublication?.TrySetResult();
    }

    internal Task ContinueAsync(bool retry = false, CancellationToken cancellationToken = default)
    {
        if (!IsPresentationActive) return Task.CompletedTask;
        if (continuation is { IsCompleted: false }) return continuation;
        return continuation = ContinueCoreAsync(retry, cancellationToken);
    }
    private async Task ContinueCoreAsync(bool retry, CancellationToken cancellationToken)
    {
        if (ContinuationPaused && !retry) return;
        if (retry) { ContinuationPaused = false; emptyContinuations = 0; }
        ContinuationFailed = false;
        var activeToken = activity.Token;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token, activeToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(35));
        var current = presentation;
        var source = current.Collection.IndexedCollection!;
        if (source.Discovery is not { HasMore: true }) return;
        var frame = session.GetState(current.Frame.Authority.WidgetId)?.LastGood ?? current.Frame;
        var publication = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        continuationRevision = source.Discovery.Revision;
        continuationPublication = publication;
        try
        {
            await admission.WaitAsync(cancellation.Token);
            try
            {
                frame = session.GetState(current.Frame.Authority.WidgetId)?.LastGood ?? frame;
                if (!SameOwner(current.Frame.Authority, frame.Authority)) throw new OperationCanceledException("Indexed widget owner changed.");
                if (current.Binding.Selection is { } selected)
                {
                    var projected = session.ResolvePinnedProjection(frame, current.Binding.PinnedLayoutId!);
                    await session.ContinuePinnedDiscoveredCollectionAsync(selected, projected, current.Collection.Id, source, retry, cancellation.Token);
                }
                else await session.ContinueDiscoveredCollectionAsync(frame.Authority, current.Collection.Id, source, retry, cancellation.Token);
            }
            finally { admission.Release(); }
            if (Declaration.IndexedCollection?.Discovery is { } next && next.Revision > source.Discovery.Revision && next.Status != DiscoveredCollectionStatus.Loading)
                publication.TrySetResult();
            await publication.Task.WaitAsync(cancellation.Token);
            if (Items.Count == source.Count && Declaration.IndexedCollection?.Discovery is
                { HasMore: true, Status: DiscoveredCollectionStatus.Ready })
            {
                // Native underfill demand must not consume unlimited provider
                // quota across filtered/duplicate-only pages with no new rows.
                ContinuationPaused = ++emptyContinuations >= 4;
            }
            else emptyContinuations = 0;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested || activeToken.IsCancellationRequested || cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        { ContinuationFailed = true; }
        finally
        {
            if (ReferenceEquals(continuationPublication, publication)) continuationPublication = null;
            if (!lifetime.IsCancellationRequested && !activeToken.IsCancellationRequested) DiscoveryChanged?.Invoke();
        }
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
            if (!current.Binding.IsCurrent) throw new OperationCanceledException("Pinned selection retired before range admission.");
            lease = await session.AcquireIndexedRangeAsync(frame.Authority, current.Collection.Id, source,
                request.StartIndex, request.Count, current.Binding.PinnedLayoutId, cancellationToken: cancellation).ConfigureAwait(false);
        }
        finally { admission.Release(); }
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!lease.IsCurrent || !current.Binding.IsCurrent) throw new OperationCanceledException("Indexed rows retired before native admission.");
            // The lease owns this subscription; the source does not retain old
            // leases after page eviction. Dispatch style-only notifications, not
            // collection Reset/reacquisition or provider ContentRevision changes.
            lease.RenderStylesChanged += (_, _) => ItemsDispatcherStylesChanged();
            return new(request.Query, request.RequestId, request.StartIndex,
                lease.Range.Items.Select(item => new KeyedCollectionItem<WidgetIndexedRow>(item.Key, new(item, lease, this))).ToArray(),
                request.ContentRevision, lease);
        }
        catch { await lease.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private void ItemsDispatcherStylesChanged() => dispatcher.TryEnqueue(() =>
    {
        if (!lifetime.IsCancellationRequested) StylesChanged?.Invoke();
    });

    internal async Task<WidgetEncodedArtwork?> ResolveArtworkAsync(WidgetIndexedRow row, string handle, CancellationToken cancellation)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, activity.Token, lifetime.Token);
        await admission.WaitAsync(linked.Token).ConfigureAwait(false);
        try { return await row.Lease.ResolveArtworkAsync(row.Item.Key, handle, linked.Token).ConfigureAwait(false); }
        finally { admission.Release(); }
    }

    internal static bool SameOwner(WidgetPresentationAuthority first, WidgetPresentationAuthority second) =>
        first.WidgetId == second.WidgetId && first.WidgetInstanceId == second.WidgetInstanceId &&
        first.RuntimeGeneration == second.RuntimeGeneration && first.PresentationGeneration == second.PresentationGeneration &&
        first.SessionGeneration == second.SessionGeneration;

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (continuation is not null) await continuation;
        await Items.DisposeAsync();
        lifetime.Dispose();
        activity.Dispose();
    }
}
