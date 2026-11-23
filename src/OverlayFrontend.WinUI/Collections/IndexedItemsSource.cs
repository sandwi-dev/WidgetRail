using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;
using WidgetRail.WidgetUi.State.Collections;
using WidgetRail.OverlayFrontend.WinUI.Diagnostics;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed record IndexedQueryIdentity(CollectionAuthority Authority, long Generation);
internal readonly record struct IndexedRangeRequest(IndexedQueryIdentity Query, long RequestId, int StartIndex, int Count,
    long ContentRevision = 0);
internal sealed record IndexedRangeResult<T>(IndexedQueryIdentity Query, long RequestId, int StartIndex,
    IReadOnlyList<KeyedCollectionItem<T>> Items, long ContentRevision = 0, IAsyncDisposable? Lifetime = null) where T : notnull;

/// <summary>
/// One query and one native items control's demand. Complete counts are fixed;
/// discovered counts grow only by admitted tail appends. Cache eviction
/// releases payload/slot references without deleting logical positions. WinUI owns
/// item realization, layout and scrolling. Bounded ordered occurrence updates
/// can rebase in place; unrelated queries still replace the source.
/// The trusted range reader must bound its work and honor cancellation; widget
/// code must execute behind the service boundary, never in this delegate.
/// </summary>
internal sealed partial class IndexedItemsSource<T> : IList, INotifyCollectionChanged, IItemsRangeInfo, ISupportIncrementalLoading, IAsyncDisposable where T : notnull
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
            owner?.ReleaseRetention(Slot);
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
    internal int PageSize => pageSize;
    private readonly int concurrency;
    private readonly Dictionary<IndexedItem<T>, int> retainedSlots = [];
    private IReadOnlyList<int> visiblePages = [];
    private IReadOnlyList<int> trackedPages = [];
    private HashSet<int> demandedPages = [];
    private IReadOnlyList<int> demandOrder = [];
    private bool queued;
    private bool disposed;
    private bool active = true;
    private Task? disposal;
    private long requestId;
    public int Count { get; private set; }
    internal Func<bool>? CanLoadMore { get; set; }
    internal Func<CancellationToken, Task>? LoadMore { get; set; }
    public bool HasMoreItems => active && !disposed && (CanLoadMore?.Invoke() ?? false);
    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint requestedCount) => AsyncInfo.Run(async token =>
    {
        CheckAccess();
        var before = Count;
        if (HasMoreItems && LoadMore is { } load) await load(token);
        return new LoadMoreItemsResult { Count = (uint)Math.Max(0, Count - before) };
    });
    public IndexedQueryIdentity Query { get; private set; }
    internal bool IsPresentationActive => active;
    internal Task SetPresentationActiveAsync(bool value)
    {
        CheckAccess();
        if (active == value) return Task.CompletedTask;
        active = value;
        if (value) { ResetFailures(); QueuePump(); return Task.CompletedTask; }
        foreach (var fetch in fetching.Values) { fetch.Cancellation.Cancel(); ++CancelledLoads; }
        fetching.Clear(); failedPages.Clear();
        foreach (var page in pages.Values) Release(page.Lifetime);
        pages.Clear();
        // Logical slots and their last presentation stay intact. Their retired
        // payloads confer no input authority; resume reacquires every demanded page.
        StateChanged?.Invoke(this, EventArgs.Empty);
        return DrainAsync(ownedFetches.Select(fetch => fetch.Task).ToArray());
    }
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
    internal int RetainedIndices => retainedSlots.Count;
    internal bool IsDemanded(int index) => index >= 0 && index < Count && demandedPages.Contains(index / pageSize);
    internal bool DemandIsSettled => !ownedFetches.Any(fetch => fetch.Request.Query == Query && fetch.Request.ContentRevision == ContentRevision) &&
        demandedPages.All(page => failedPages.Contains(page) || pages.TryGetValue(page, out var loaded) &&
            loaded.Query == Query && loaded.ContentRevision == ContentRevision && loaded.Items.Count >= Math.Min(pageSize, Count - page * pageSize));
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

    /// <summary>Copies only already-resident pixels by exact occurrence into a new query.
    /// This transfers no leases, retained indices, outstanding requests or input authority.</summary>
    internal void SeedPresentation(IndexedItemsSource<T> previous, IReadOnlyList<string> keys)
    {
        CheckAccess();
        var positions = keys.Select((key, index) => (key, index)).ToDictionary(pair => pair.key, pair => pair.index, StringComparer.Ordinal);
        foreach (var old in previous.slots.Values)
            if (old.Key is { } key && old.Value is { } value && positions.TryGetValue(key, out var index))
                GetSlot(index).SetValue(key, value);
    }

    // A recreated native view of the exact ordered query can show already
    // resident rows while it acquires its own ranges. This copies presentation
    // only: no page, lease, demand, retention or request ownership is transferred.
    internal void SeedSameQueryPresentation(IndexedItemsSource<T> previous, Func<T, T>? copyPaint = null)
    {
        CheckAccess();
        previous.CheckAccess();
        if (Query != previous.Query || Count != previous.Count || ContentRevision < previous.ContentRevision)
            throw new InvalidOperationException("Presentation seeding requires the same ordered query.");
        foreach (var (index, old) in previous.slots)
            if (old.Key is { } key && old.Value is { } value)
                GetSlot(index).SetValue(key, copyPaint is null ? value : copyPaint(value));
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
        ResetFailures();
        QueuePump();
    }

    /// <summary>Publish only an admitted tail. Prefix slot identity, payloads and native viewport stay in place.</summary>
    internal void Append(int count)
    {
        CheckAccess();
        if (count < Count) throw new InvalidOperationException("An append cannot remove discovered positions.");
        while (Count < count)
        {
            var index = Count++;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, GetSlot(index), index));
        }
        QueuePump();
    }

    public void RetryFailedPages()
    {
        CheckAccess();
        if (failedPages.Count == 0) return;
        ResetFailures();
        QueuePump();
    }

    private void ResetFailures()
    {
        failedPages.Clear();
        // A new read is pending, not terminally failed. Preserve slots/pixels;
        // navigation still waits for current lease authority before entering.
        // Notifications may retain another slot, so iterate a stable snapshot.
        foreach (var slot in slots.Values.ToArray()) slot.ClearFailure();
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
        var slot = GetSlot(index);
        if (!retainedSlots.TryGetValue(slot, out var references) && retainedSlots.Count >= MaximumRetainedIndices)
            throw new InvalidOperationException("The indexed presentation-retention limit has been reached.");
        retainedSlots[slot] = checked(references + 1);
        var retention = new Retention(this, slot);
        RebuildDemand();
        return retention;
    }

    private void ReleaseRetention(IndexedItem<T> slot)
    {
        if (disposed) return;
        CheckAccess();
        if (!retainedSlots.TryGetValue(slot, out var references)) return;
        if (references == 1) retainedSlots.Remove(slot);
        else retainedSlots[slot] = references - 1;
        RebuildDemand();
    }

    public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
    {
        CheckAccess();
        // Preserve existing demand through collapsed layout, but a newly created
        // inactive source still needs its first native viewport for later resume.
        // Independent retention (such as a grid's measurement item) is not proof
        // that the native viewport has reported its demand.
        if (!active && RangeNotifications != 0) return;
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
        Include(retainedSlots.Keys.Select(slot => slot.Index / pageSize));
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
        if (disposed || !active || queued) return;
        queued = true;
        if (!dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => { queued = false; if (!disposed) Pump(); })) queued = false;
    }

    private void Pump()
    {
        if (!active) return;
        foreach (var page in pages.Keys.ToArray())
            if (!demandedPages.Contains(page))
            {
                Release(pages[page].Lifetime);
                pages.Remove(page);
            }
        foreach (var index in slots.Keys.ToArray())
            // WinUI may keep a bound container while temporarily excluding its
            // page from range demand (notably during retained hide/show). Keep
            // that slot identity so a later refill updates the same binding.
            // This does not retain the page lease or add provider demand.
            if (!demandedPages.Contains(index / pageSize) && !slots[index].HasStateObservers) slots.Remove(index);
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
                pages.TryGetValue(page, out var loaded) && loaded.ContentRevision == ContentRevision && loaded.Items.Count >= length) continue;
            var fetch = new Fetch(new(Query, ++requestId, start, length, ContentRevision));
            fetching.Add(page, fetch);
            ownedFetches.Add(fetch);
            fetch.Task = FetchAsync(page, fetch);
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task FetchAsync(int page, Fetch fetch)
    {
        var timing = PresentationPipelineTrace.Log.Begin();
        IndexedRangeResult<T>? items = null;
        Exception? failure = null;
        try
        {
            // A provider's synchronous prefix cannot block WinUI's dispatcher.
            items = await Task.Run(() => readRange(fetch.Request, fetch.Cancellation.Token), fetch.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
        PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.RangeData, timing.Started, fetch.Request.Count,
            fetch.Cancellation.IsCancellationRequested ? PipelineOutcome.Cancelled : failure is null ? PipelineOutcome.Completed : PipelineOutcome.Failed);
        var queuedAt = PresentationPipelineTrace.Timestamp(timing.Id);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retained = false;
        var accepted = false;
        var finished = false;
        KeyedCollectionItem<T>[]? admitted = null;
        var nextIndex = 0;
        var batchQueuedAt = 0L;
        if (dispatcher.TryEnqueue(() =>
        {
            PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.RangeUiQueue, queuedAt, fetch.Request.Count, PipelineOutcome.Completed);
            try
            {
                if (!disposed && fetching.TryGetValue(page, out var current) && ReferenceEquals(current, fetch))
                {
                    fetching.Remove(page);
                    accepted = true;
                    if (failure is null && !fetch.Cancellation.IsCancellationRequested)
                    {
                        admitted = Validate(fetch.Request, items!);
                        if (pages.Remove(page, out var previous)) Release(previous.Lifetime);
                        pages.Add(page, items!);
                        retained = true;
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
            if (admitted is not null) PublishBatch();
            else Finish();
        })) await completion.Task.ConfigureAwait(false);
        else
        {
            PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.RangeTotal, timing.Started, 0, PipelineOutcome.Retired);
            fetch.Cancellation.Dispose();
            // Even after dispatcher shutdown, a delivered worker range has an
            // explicit owner. The fetch task drains it before it can finish.
            if (items?.Lifetime is { } lifetime)
                await ReleaseUndeliveredAsync(lifetime).ConfigureAwait(false);
        }

        void PublishBatch()
        {
            if (finished) return;
            if (batchQueuedAt != 0)
                PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.RangeBatchQueue, batchQueuedAt, 0, PipelineOutcome.Completed);
            var batchStarted = PresentationPipelineTrace.Timestamp(timing.Id);
            var firstIndex = nextIndex;
            try
            {
                var start = Stopwatch.GetTimestamp();
                var count = 0;
                while (nextIndex < admitted!.Length)
                {
                    // Publishing a slot synchronously builds its bound native
                    // row. Yield between small batches so input/layout can run.
                    // Every continuation rechecks ownership, including changes
                    // caused reentrantly by a binding notification.
                    if (disposed || !active || fetch.Cancellation.IsCancellationRequested ||
                        Query != fetch.Request.Query || ContentRevision != fetch.Request.ContentRevision ||
                        !pages.TryGetValue(page, out var published) || !ReferenceEquals(published, items)) break;
                    var index = nextIndex++;
                    GetSlot(fetch.Request.StartIndex + index).SetValue(admitted[index].Key, admitted[index].Value);
                    if (nextIndex < admitted.Length && (++count >= 8 || Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 2))
                    {
                        if (timing.Id != 0)
                        {
                            // Paired diagnostic only: distinguish a busy dispatcher
                            // from starvation of its Low-priority publication lane.
                            var probeQueuedAt = PresentationPipelineTrace.Timestamp(timing.Id);
                            batchQueuedAt = probeQueuedAt;
                            dispatcher.TryEnqueue(() => PresentationPipelineTrace.Log.End(timing.Id,
                                PipelineStage.RangeNormalProbe, probeQueuedAt, 0, PipelineOutcome.Completed));
                        }
                        // These rows are already demanded by the native view. Low
                        // priority can starve behind layout/artwork callbacks for
                        // hundreds of milliseconds even while Normal work runs.
                        // Keep the bounded batch/yield, but continue at Normal.
                        var priority = DispatcherQueuePriority.Normal;
                        ConfigureValidationPublicationPriority(ref priority);
                        if (dispatcher.TryEnqueue(priority, PublishBatch)) return;
                        break;
                    }
                }
            }
            catch (Exception error) when (error is not OutOfMemoryException) { failure = error; }
            finally
            {
                PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.RangeBatch, batchStarted, nextIndex - firstIndex,
                    fetch.Cancellation.IsCancellationRequested ? PipelineOutcome.Cancelled : failure is null ? PipelineOutcome.Completed : PipelineOutcome.Failed);
            }
            Finish();
        }

        void Finish()
        {
            if (finished) return;
            finished = true;
            PresentationPipelineTrace.Log.End(timing.Id, PipelineStage.RangeTotal, timing.Started, nextIndex,
                fetch.Cancellation.IsCancellationRequested ? PipelineOutcome.Cancelled : failure is not null ? PipelineOutcome.Failed :
                retained && nextIndex == fetch.Request.Count ? PipelineOutcome.Completed : PipelineOutcome.Retired);
            try
            {
                if (accepted && !disposed && active && Query == fetch.Request.Query && ContentRevision == fetch.Request.ContentRevision &&
                    failure is not null && !fetch.Cancellation.IsCancellationRequested)
                {
                    failedPages.Add(page); ++FailedLoads;
                    for (var index = fetch.Request.StartIndex; index < fetch.Request.StartIndex + fetch.Request.Count; ++index)
                        if (slots.TryGetValue(index, out var slot)) slot.SetFailed();
                }
            }
            finally
            {
                if (retained) ++CompletedLoads;
                if (!retained) Release(items?.Lifetime);
                // The native dispatcher can keep its completed callback wrapper
                // until a later GC. Only pages/slots should retain the delivered
                // range, not that callback's captured variable.
                items = null;
                admitted = null;
                failure = null;
                ownedFetches.Remove(fetch);
                fetch.Cancellation.Dispose();
                QueuePump();
                completion.TrySetResult();
            }
        }
    }

    // No implementation in production; lets the native fixture compare the old
    // lane using the exact same optimized binary and foreground conditions.
    static partial void ConfigureValidationPublicationPriority(ref DispatcherQueuePriority priority);

    private KeyedCollectionItem<T>[] Validate(IndexedRangeRequest request, IndexedRangeResult<T> result)
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
        return copy;
    }

    // Explicit append/window edits emit structural notifications; payload cache
    // refill/eviction notifies slots and never resets the collection.
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public bool IsReadOnly => true;
    public bool IsFixedSize => CanLoadMore is null;
    public bool IsSynchronized => false;
    public object SyncRoot => identity;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public int IndexOf(object? value) => value is IndexedItem<T> item && ReferenceEquals(item.Owner, identity) &&
        slots.TryGetValue(item.Index, out var current) && ReferenceEquals(current, item) ? item.Index : -1;
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
        retainedSlots.Clear(); visiblePages = []; trackedPages = [];
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
