using System.Collections;
using System.Collections.Specialized;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Data;
using WidgetRail.WidgetUi.State.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed record IndexedQueryIdentity(CollectionAuthority Authority, long Generation);
internal readonly record struct IndexedRangeRequest(IndexedQueryIdentity Query, long RequestId, int StartIndex, int Count);
internal sealed record IndexedRangeResult<T>(IndexedQueryIdentity Query, long RequestId, int StartIndex,
    IReadOnlyList<KeyedCollectionItem<T>> Items) where T : notnull;

/// <summary>
/// One immutable-count query and one native items control's demand. Cache eviction
/// releases payload/slot references without deleting logical positions. WinUI owns
/// item realization, layout and scrolling. Replace the source for a new query.
/// The trusted range reader must bound its work and honor cancellation; widget
/// code must execute behind the service boundary, never in this delegate.
/// </summary>
internal sealed class IndexedItemsSource<T> : IList, INotifyCollectionChanged, IItemsRangeInfo, IAsyncDisposable where T : notnull
{
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
    private readonly int pageSize;
    private readonly int concurrency;
    private HashSet<int> demandedPages = [];
    private IReadOnlyList<int> demandOrder = [];
    private bool queued;
    private bool disposed;
    private Task? disposal;
    private long requestId;
    public int Count { get; }
    public IndexedQueryIdentity Query { get; }
    public int RangeNotifications { get; private set; }
    public int IndexReads { get; private set; }
    public int EnumerationCalls { get; private set; }
    public int ResidentSlots => slots.Count;
    public int PeakResidentSlots { get; private set; }
    public int CompletedLoads { get; private set; }
    public int CancelledLoads { get; private set; }
    public int FailedLoads { get; private set; }
    public event EventHandler? StateChanged;

    public IndexedItemsSource(IndexedQueryIdentity query, int count, DispatcherQueue dispatcher,
        Func<IndexedRangeRequest, CancellationToken, Task<IndexedRangeResult<T>>> readRange,
        int pageSize = 32, int concurrency = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(query.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Authority.RuntimeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Authority.WidgetInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Authority.CollectionId);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Generation);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(readRange);
        if (pageSize is < 1 or > 256 || concurrency is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (!dispatcher.HasThreadAccess) throw new InvalidOperationException("Create items sources on their WinUI dispatcher.");
        Query = query; Count = count; this.dispatcher = dispatcher; this.readRange = readRange; this.pageSize = pageSize; this.concurrency = concurrency;
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

    public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
    {
        CheckAccess();
        ++RangeNotifications;
        var next = new HashSet<int>();
        var order = new List<int>();
        Include(visibleRange);
        foreach (var range in trackedItems) Include(range);
        demandedPages = next;
        demandOrder = order;
        QueuePump();

        void Include(ItemIndexRange range)
        {
            if (range.Length == 0 || Count == 0) return;
            var start = Math.Clamp((long)range.FirstIndex, 0, Count);
            var end = Math.Clamp((long)range.FirstIndex + range.Length, 0, Count);
            if (end <= start) return;
            for (var page = (int)start / pageSize; page <= (end - 1) / pageSize; ++page)
                if (next.Add(page)) order.Add(page);
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
            if (ownedFetches.Count >= concurrency) break;
            var start = page * pageSize;
            var length = Math.Min(pageSize, Count - start);
            if (fetching.ContainsKey(page) || failedPages.Contains(page) || Enumerable.Range(start, length).All(index => slots.TryGetValue(index, out var slot) && slot.HasValue)) continue;
            var fetch = new Fetch(new(Query, ++requestId, start, length));
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
        if (dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (!disposed && fetching.TryGetValue(page, out var current) && ReferenceEquals(current, fetch))
                {
                    fetching.Remove(page);
                    if (failure is null && !fetch.Cancellation.IsCancellationRequested)
                    {
                        try { Admit(fetch.Request, items!); ++CompletedLoads; }
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
                ownedFetches.Remove(fetch);
                fetch.Cancellation.Dispose();
                QueuePump();
                completion.TrySetResult();
            }
        })) await completion.Task.ConfigureAwait(false);
        else fetch.Cancellation.Dispose();
    }

    private void Admit(IndexedRangeRequest request, IndexedRangeResult<T> result)
    {
        if (result is null || result.Query != Query || result.RequestId != request.RequestId || result.StartIndex != request.StartIndex)
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
        slots.Clear(); demandedPages.Clear(); demandOrder = []; failedPages.Clear(); fetching.Clear();
    }
    public ValueTask DisposeAsync()
    {
        Dispose();
        return new(disposal ??= Task.WhenAll(ownedFetches.Select(fetch => fetch.Task).ToArray()));
    }
}
