using System.Collections;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed record IndexedQueryIdentity(CollectionAuthority Authority, long Generation);
internal readonly record struct IndexedRangeRequest(IndexedQueryIdentity Query, long RequestId, int StartIndex, int Count,
    long ContentRevision = 0);
internal sealed record IndexedRangeResult<T>(IndexedQueryIdentity Query, long RequestId, int StartIndex,
    IReadOnlyList<KeyedCollectionItem<T>> Items, long ContentRevision = 0, IAsyncDisposable? Lifetime = null) where T : notnull;

/// <summary>
/// One immutable-count query and one native items control's demand. Cache eviction
/// releases payload/slot references without deleting logical positions. WinUI owns
/// item realization, layout and scrolling. Replace the source for a new query.
/// The trusted range reader must bound its work and honor cancellation; widget
/// code must execute behind the service boundary, never in this delegate.
/// </summary>
internal sealed class IndexedItemsSource<T> : IList, INotifyCollectionChanged, IItemsRangeInfo, IAsyncDisposable where T : notnull
{
    internal const int MaximumRetainedIndices = 8;

    /// <summary>
    /// One explicit presentation lifetime over the existing source slot/page. Dispose
    /// on the source dispatcher; disposal after source shutdown is always harmless.
    /// </summary>
    internal sealed class Retention : IDisposable
    {
        private IndexedItemsSource<T>? owner;
        internal Retention(IndexedItemsSource<T> owner, IndexedItem<T> slot) { this.owner = owner; Slot = slot; }
        public IndexedItem<T> Slot { get; }
        public void Dispose()
        {
            // A wrong-thread call must not consume the token before CheckAccess fails.
            owner?.ReleaseRetention(Slot.Index);
            owner = null;
        }
    }

    private sealed class Fetch(IndexedRangeRequest request)
    {
        public IndexedRangeRequest Request { get; } = request;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Task { get; set; } = Task.CompletedTask;
    }
    private readonly object identity = new();
    private readonly DispatcherQueue dispatcher;
    private readonly Func<IndexedRangeRequest, CancellationToken, Task<IndexedRangeResult<T>>> readRange;
    private readonly Dictionary<int, IndexedItem<T>> slots = [];
    private readonly Dictionary<int, Fetch> fetching = [];
    private readonly HashSet<Fetch> ownedFetches = [];
    private readonly HashSet<int> failedPages = [];
    private readonly Dictionary<int, IndexedRangeResult<T>> pages = [];
    private readonly object releaseGate = new();
    private readonly HashSet<Task> releases = [];
    private Exception? releaseFailure;
    private readonly int pageSize;
    private readonly int concurrency;
    private readonly Dictionary<int, int> retainedIndices = [];
    private IReadOnlyList<int> visiblePages = [];
    private IReadOnlyList<int> trackedPages = [];
    private HashSet<int> demandedPages = [];
    private IReadOnlyList<int> demandOrder = [];
    private bool queued;
    private bool disposed;
    private Task? disposal;
    private long requestId;
    public int Count { get; }
    public IndexedQueryIdentity Query { get; }
    public long ContentRevision { get; private set; }
    public int RangeNotifications { get; private set; }
    public int IndexReads { get; private set; }
    public int EnumerationCalls { get; private set; }
    public int ResidentSlots => slots.Count;
    public int PeakResidentSlots { get; private set; }
    public int CompletedLoads { get; private set; }
    public int CancelledLoads { get; private set; }
    public int FailedLoads { get; private set; }
    public int PendingReleases { get { lock (releaseGate) return releases.Count; } }
    internal int RetainedIndices => retainedIndices.Count;
    public event EventHandler? StateChanged;

    public IndexedItemsSource(IndexedQueryIdentity query, int count, DispatcherQueue dispatcher,
        Func<IndexedRangeRequest, CancellationToken, Task<IndexedRangeResult<T>>> readRange,
        int pageSize = 32, int concurrency = 4, long contentRevision = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Authority.RuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Authority.WidgetInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Authority.CollectionId);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Generation);
        ArgumentOutOfRangeException.ThrowIfNegative(contentRevision);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(readRange);
        if (pageSize is < 1 or > 256 || concurrency is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Create items sources on their WinUI dispatcher.");
        Query = query; Count = count; this.dispatcher = dispatcher; this.readRange = readRange; this.pageSize = pageSize; this.concurrency = concurrency;
        ContentRevision = contentRevision;
    }

    /// <summary>Refresh payloads without replacing logical slots, focus or scroll position.</summary>
    public void RefreshContent(long revision)
    {
        CheckAccess();
        if (revision < ContentRevision) throw new ArgumentOutOfRangeException(nameof(revision));
        if (revision == ContentRevision) return;
        ContentRevision = revision;
        foreach (var fetch in fetching.Values) { fetch.Cancellation.Cancel(); ++CancelledLoads; }
        fetching.Clear();
        failedPages.Clear();
        QueuePump();
    }

    public void RetryFailedPages()
    {
        CheckAccess();
        if (failedPages.Count == 0) return;
        failedPages.Clear();
        QueuePump();
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public object this[int index]
    {
        get
        {
            CheckAccess();
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            ++IndexReads;
            return GetSlot(index);
        }
        set => throw new NotSupportedException();
    }

    private IndexedItem<T> GetSlot(int index)
    {
        if (!slots.TryGetValue(index, out var slot))
        {
            slots.Add(index, slot = new(identity, index));
            PeakResidentSlots = Math.Max(PeakResidentSlots, slots.Count);
        }
        return slot;
    }

    /// <summary>
    /// Retains an item for a focused background or presentation fragment, independent
    /// of native container realization. Shares normal page acquisition, revisions and
    /// provider budgets; never creates a second semantic lease for the same page.
    /// </summary>
    internal Retention Retain(int index)
    {
        CheckAccess();
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (!retainedIndices.TryGetValue(index, out var references) && retainedIndices.Count >= MaximumRetainedIndices)
            throw new InvalidOperationException("The indexed presentation-retention limit has been reached.");
        retainedIndices[index] = checked(references + 1);
        var retention = new Retention(this, GetSlot(index));
        RebuildDemand();
        return retention;
    }

    private void ReleaseRetention(int index)
    {
        if (disposed) return;
        CheckAccess();
        if (retainedIndices[index] == 1) retainedIndices.Remove(index);
        else --retainedIndices[index];
        RebuildDemand();
    }

    public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
    {
        CheckAccess();
        ++RangeNotifications;
        var visible = new List<int>();
        var tracked = new List<int>();
        var seenVisible = new HashSet<int>();
        var seenTracked = new HashSet<int>();
        Include(visibleRange, visible, seenVisible);
        foreach (var range in trackedItems) Include(range, tracked, seenTracked);
        visiblePages = visible;
        trackedPages = tracked;
        RebuildDemand();

        void Include(ItemIndexRange range, List<int> pages, HashSet<int> seen)
        {
            if (range.Length == 0 || Count == 0) return;
            var start = Math.Clamp((long)range.FirstIndex, 0, Count);
            var end = Math.Clamp((long)range.FirstIndex + range.Length, 0, Count);
            if (end <= start) return;
            for (var page = (int)start / pageSize; page <= (end - 1) / pageSize; ++page)
                if (seen.Add(page)) pages.Add(page);
        }
    }

    private void RebuildDemand()
    {
        var next = new HashSet<int>();
        var order = new List<int>();
        Include(visiblePages);
        Include(retainedIndices.Keys.Select(index => index / pageSize));
        Include(trackedPages);
        demandedPages = next;
        demandOrder = order;
        QueuePump();
        void Include(IEnumerable<int> pages)
        {
            foreach (var page in pages) if (next.Add(page)) order.Add(page);
        }
    }

    private void QueuePump()
    {
        if (disposed || queued) return;
        queued = true;
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => { queued = false; if (!disposed) Pump(); })) queued = false;
    }

    private void Pump()
    {
        foreach (var page in pages.Keys.ToArray())
            if (!demandedPages.Contains(page))
            {
                Release(pages[page].Lifetime);
                pages.Remove(page);
            }
        foreach (var index in slots.Keys.ToArray())
            if (!demandedPages.Contains(index / pageSize)) slots.Remove(index);
        failedPages.RemoveWhere(page => !demandedPages.Contains(page));
        foreach (var pair in fetching.ToArray())
            if (!demandedPages.Contains(pair.Key))
            {
                fetching.Remove(pair.Key);
                pair.Value.Cancellation.Cancel();
                ++CancelledLoads;
            }
        foreach (var page in demandOrder)
        {
            // Slow release cannot create an unbounded backlog of owned ranges.
            if (ownedFetches.Count + PendingReleases >= concurrency) break;
            var start = page * pageSize;
            var length = Math.Min(pageSize, Count - start);
            if (fetching.ContainsKey(page) || failedPages.Contains(page) ||
                pages.TryGetValue(page, out var loaded) && loaded.ContentRevision == ContentRevision) continue;
            var fetch = new Fetch(new(Query, ++requestId, start, length, ContentRevision));
            fetching.Add(page, fetch);
            ownedFetches.Add(fetch);
            fetch.Task = FetchAsync(page, fetch);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task FetchAsync(int page, Fetch fetch)
    {
        IndexedRangeResult<T>? items = null;
        Exception? failure = null;
        try
        {
            // A provider's synchronous prefix cannot block WinUI's dispatcher.
            items = await Task.Run(() => readRange(fetch.Request, fetch.Cancellation.Token), fetch.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = false;
        if (dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (!disposed && fetching.TryGetValue(page, out var current) && ReferenceEquals(current, fetch))
                {
                    fetching.Remove(page);
                    if (failure is null && !fetch.Cancellation.IsCancellationRequested)
                    {
                        try
                        {
                            Admit(fetch.Request, items!);
                            if (pages.Remove(page, out var previous)) Release(previous.Lifetime);
                            pages.Add(page, items!);
                            retained = true;
                            ++CompletedLoads;
                        }
                        catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
                    }
                    if (failure is not null && !fetch.Cancellation.IsCancellationRequested)
                    {
                        failedPages.Add(page); ++FailedLoads;
                        for (var index = fetch.Request.StartIndex; index < fetch.Request.StartIndex + fetch.Request.Count; ++index)
                            if (slots.TryGetValue(index, out var slot)) slot.SetFailed();
                    }
                    QueuePump();
                }
            }
            finally
            {
                if (!retained) Release(items?.Lifetime);
                ownedFetches.Remove(fetch);
                fetch.Cancellation.Dispose();
                QueuePump();
                completion.TrySetResult();
            }
        })) await completion.Task.ConfigureAwait(false);
        else
        {
            fetch.Cancellation.Dispose();
            // Even after dispatcher shutdown, a delivered worker range has an
            // explicit owner. The fetch task drains it before it can finish.
            if (items?.Lifetime is { } lifetime)
                await ReleaseUndeliveredAsync(lifetime).ConfigureAwait(false);
        }
    }

    private void Admit(IndexedRangeRequest request, IndexedRangeResult<T> result)
    {
        if (result is null || result.Query != Query || result.RequestId != request.RequestId || result.StartIndex != request.StartIndex ||
            result.ContentRevision != request.ContentRevision || request.ContentRevision != ContentRevision)
            throw new InvalidDataException("Indexed range returned different query or request authority.");
        if (result.Items is null || result.Items.Count != request.Count) throw new InvalidDataException("Indexed range returned a different count.");
        var copy = result.Items.ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in slots)
            if ((pair.Key < request.StartIndex || pair.Key >= request.StartIndex + request.Count) && pair.Value.Key is { } key) keys.Add(key);
        for (var index = 0; index < copy.Length; ++index)
        {
            var item = copy[index];
            if (item is null || string.IsNullOrWhiteSpace(item.Key) || item.Value is null || !keys.Add(item.Key)) throw new InvalidDataException("Indexed range returned invalid item identities.");
            if (slots.TryGetValue(request.StartIndex + index, out var old) && old.Key is { } oldKey && oldKey != item.Key)
                throw new InvalidDataException("An indexed query changed an existing logical position.");
        }
        for (var index = 0; index < copy.Length; ++index)
            GetSlot(request.StartIndex + index).SetValue(copy[index].Key, copy[index].Value);
    }

    // Count/order never change in this source. Payload publications notify slots,
    // so cache refill/eviction never emits structural collection notifications.
    public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => identity;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public int IndexOf(object? value) => value is IndexedItem<T> item && ReferenceEquals(item.Owner, identity) ? item.Index : -1;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    public void CopyTo(Array array, int index) { for (var i = 0; i < Count; ++i) array.SetValue(this[i], index + i); }
    public IEnumerator GetEnumerator()
    {
        ++EnumerationCalls;
        for (var index = 0; index < Count; ++index) yield return this[index];
    }
    private void CheckAccess()
    {
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Access items sources on their WinUI dispatcher.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    public void Dispose()
    {
        if (disposed) return;
        CheckAccess(); disposed = true;
        foreach (var fetch in ownedFetches) fetch.Cancellation.Cancel();
        foreach (var page in pages.Values) Release(page.Lifetime);
        pages.Clear();
        slots.Clear(); demandedPages.Clear(); demandOrder = []; failedPages.Clear(); fetching.Clear();
        retainedIndices.Clear(); visiblePages = []; trackedPages = [];
    }
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(disposal ??= DrainAsync(ownedFetches.Select(fetch => fetch.Task).ToArray()));
    }

    private void Release(IAsyncDisposable? lifetime)
    {
        if (lifetime is null) return;
        // Dispose may start pipe I/O; never run even its synchronous prefix on UI.
        var task = Task.Run(() => ReleaseUndeliveredAsync(lifetime));
        lock (releaseGate) releases.Add(task);
        _ = task.ContinueWith(completed =>
        {
            lock (releaseGate) releases.Remove(completed);
            dispatcher.TryEnqueue(() => { if (!disposed) QueuePump(); });
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private async Task ReleaseUndeliveredAsync(IAsyncDisposable lifetime)
    {
        try { await lifetime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (releaseGate) releaseFailure ??= error; }
    }

    private async Task DrainAsync(Task[] fetches)
    {
        await Task.WhenAll(fetches).ConfigureAwait(false);
        Task[] pending;
        lock (releaseGate) pending = releases.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(false);
        lock (releaseGate)
            if (releaseFailure is not null) throw new InvalidOperationException("An indexed range could not be released.", releaseFailure);
    }
}
