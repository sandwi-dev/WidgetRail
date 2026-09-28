using System.Collections;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

/// <summary>
/// Adds native flattened-range demand to an existing grouped WinUI collection view.
/// WinUI retains grouping, currency, index lookup, realization and layout. This
/// wrapper owns only its event subscriptions and demand; the caller owns the native
/// view and flat data source, and must dispose this wrapper before that source.
/// Native groups must be slices of that same flat immutable query in identical order.
/// All calls belong to the constructing UI thread. Replace on membership/order changes.
/// </summary>
internal sealed class NativeGroupedRangeView : ICollectionView, IItemsRangeInfo
{
    private readonly ICollectionView native;
    private readonly IObservableVector<object> nativeGroups;
    private readonly IItemsRangeInfo rangeOwner;
    private readonly int groupCount;
    private readonly int count;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool invalidated;
    private bool disposed;

    public NativeGroupedRangeView(ICollectionView native, IItemsRangeInfo rangeOwner)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(rangeOwner);
        this.native = native;
        this.rangeOwner = rangeOwner;
        nativeGroups = native.CollectionGroups ?? throw new ArgumentException("A grouped native view is required.", nameof(native));
        groupCount = nativeGroups.Count;
        long total = 0;
        for (var index = 0; index < groupCount; ++index)
        {
            if (nativeGroups[index] is not ICollectionViewGroup group)
                throw new ArgumentException("The native collection contains an invalid group.", nameof(native));
            var size = group.GroupItems.Count;
            if (size < 0 || total + size > int.MaxValue)
                throw new ArgumentException("The flattened count exceeds native indexed capacity.", nameof(native));
            total += size;
        }
        count = (int)total;
        if (native.Count != count)
            throw new ArgumentException("The flattened count does not match the native view.", nameof(native));
        native.VectorChanged += NativeVectorChanged;
        native.CurrentChanged += NativeCurrentChanged;
        native.CurrentChanging += NativeCurrentChanging;
        nativeGroups.VectorChanged += NativeGroupsChanged;
    }

    public int RangeNotifications { get; private set; }
    public event VectorChangedEventHandler<object>? VectorChanged;
    public event EventHandler<object>? CurrentChanged;
    public event CurrentChangingEventHandler? CurrentChanging;

    public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
    {
        CheckCurrent();
        ArgumentNullException.ThrowIfNull(visibleRange);
        ArgumentNullException.ThrowIfNull(trackedItems);
        foreach (var range in trackedItems) ArgumentNullException.ThrowIfNull(range);
        // Native item indices exclude group headers and already use flattened query
        // coordinates. Preserve them; no scatter, item enumeration or extra providers.
        // Clip defensively using Int64 to avoid uint Length overflow or sentinel indices.
        var visible = Clip(visibleRange);
        var tracked = trackedItems.Select(Clip).Where(range => range.Length != 0).ToArray();
        ++RangeNotifications;
        rangeOwner.RangesChanged(visible, tracked);
    }

    private ItemIndexRange Clip(ItemIndexRange range)
    {
        var start = Math.Max(0L, range.FirstIndex);
        var end = Math.Min((long)count, (long)range.FirstIndex + range.Length);
        return end > start ? new((int)start, (uint)(end - start)) : new(0, 0);
    }

    private void NativeVectorChanged(IObservableVector<object> sender, IVectorChangedEventArgs args)
    {
        // Content replacement at a fixed position preserves range geometry. Insertion,
        // removal or Reset changes the immutable grouped query and requires a new view.
        if (args.CollectionChange != CollectionChange.ItemChanged) invalidated = true;
        VectorChanged?.Invoke(this, args);
    }
    private void NativeGroupsChanged(IObservableVector<object> sender, IVectorChangedEventArgs args) => invalidated = true;
    private void NativeCurrentChanged(object? sender, object args) => CurrentChanged?.Invoke(this, args);
    private void NativeCurrentChanging(object sender, CurrentChangingEventArgs args) => CurrentChanging?.Invoke(this, args);

    private void CheckAccess()
    {
        if (ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("The grouped range view belongs to its constructing UI thread.");
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    private void CheckCurrent()
    {
        CheckAccess();
        if (invalidated || native.Count != count || nativeGroups.Count != groupCount)
            throw new InvalidOperationException("The grouped query changed; replace its range view.");
    }

    /// <summary>
    /// Detach from the ItemsControl before disposal. Clears flat query demand and
    /// subscriptions, without disposing caller-owned native or data-source objects.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        CheckAccess();
        disposed = true;
        native.VectorChanged -= NativeVectorChanged;
        native.CurrentChanged -= NativeCurrentChanged;
        native.CurrentChanging -= NativeCurrentChanging;
        nativeGroups.VectorChanged -= NativeGroupsChanged;
        VectorChanged = null; CurrentChanged = null; CurrentChanging = null;
        rangeOwner.RangesChanged(new(0, 0), Array.Empty<ItemIndexRange>());
    }

    // Delegate the ICollectionView contract; no copying, reordering or synthetic
    // currency. IndexOf remains the native view's group-aware lookup.
    public IObservableVector<object> CollectionGroups { get { CheckAccess(); return nativeGroups; } }
    public object CurrentItem { get { CheckAccess(); return native.CurrentItem; } }
    public int CurrentPosition { get { CheckAccess(); return native.CurrentPosition; } }
    public bool HasMoreItems { get { CheckAccess(); return native.HasMoreItems; } }
    public bool IsCurrentAfterLast { get { CheckAccess(); return native.IsCurrentAfterLast; } }
    public bool IsCurrentBeforeFirst { get { CheckAccess(); return native.IsCurrentBeforeFirst; } }
    public bool MoveCurrentTo(object item) { CheckAccess(); return native.MoveCurrentTo(item); }
    public bool MoveCurrentToPosition(int index) { CheckAccess(); return native.MoveCurrentToPosition(index); }
    public bool MoveCurrentToFirst() { CheckAccess(); return native.MoveCurrentToFirst(); }
    public bool MoveCurrentToLast() { CheckAccess(); return native.MoveCurrentToLast(); }
    public bool MoveCurrentToNext() { CheckAccess(); return native.MoveCurrentToNext(); }
    public bool MoveCurrentToPrevious() { CheckAccess(); return native.MoveCurrentToPrevious(); }
    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint requestedCount)
    { CheckAccess(); return native.LoadMoreItemsAsync(requestedCount); }
    public int Count { get { CheckAccess(); return native.Count; } }
    public bool IsReadOnly { get { CheckAccess(); return native.IsReadOnly; } }
    public object this[int index]
    { get { CheckAccess(); return native[index]; } set { CheckAccess(); native[index] = value; } }
    public int IndexOf(object item) { CheckAccess(); return native.IndexOf(item); }
    public bool Contains(object item) { CheckAccess(); return native.Contains(item); }
    public void Add(object item) { CheckAccess(); native.Add(item); }
    public void Clear() { CheckAccess(); native.Clear(); }
    public void CopyTo(object[] array, int arrayIndex) { CheckAccess(); native.CopyTo(array, arrayIndex); }
    public void Insert(int index, object item) { CheckAccess(); native.Insert(index, item); }
    public bool Remove(object item) { CheckAccess(); return native.Remove(item); }
    public void RemoveAt(int index) { CheckAccess(); native.RemoveAt(index); }
    public IEnumerator<object> GetEnumerator() { CheckAccess(); return native.GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
