using System.Collections;
using Microsoft.UI.Xaml.Data;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Small UI-thread contract checks; visual/native grouping is tested separately.</summary>
internal static class NativeGroupedRangeViewScenarios
{
    public static int Run()
    {
        var passed = 0;
        var native = new View(3, 0, 7);
        var ranges = new Ranges();
        var wrapper = new NativeGroupedRangeView(native, ranges);
        Check(native.SubscriptionCount == 1 && native.CurrencySubscriptions == 2 && native.Groups.SubscriptionCount == 1,
            "native subscriptions installed once");
        Check(ReferenceEquals(wrapper.CollectionGroups, native.CollectionGroups), "native group identity preserved");

        wrapper.RangesChanged(new(3, 2), [new(0, 4), new(8, 2)]);
        Check(ranges.Visible!.FirstIndex == 3 && ranges.Visible.Length == 2 &&
            ranges.Tracked.Select(range => (range.FirstIndex, range.Length)).SequenceEqual([(0, 4u), (8, 2u)]),
            "flat demand is unchanged across groups, including an empty middle group");
        ++passed;

        wrapper.RangesChanged(new(-2, uint.MaxValue), [new(int.MaxValue, uint.MaxValue), new(-1, 1), new(8, uint.MaxValue)]);
        Check(ranges.Visible!.FirstIndex == 0 && ranges.Visible.Length == 10 && ranges.Tracked.Count == 1 &&
            ranges.Tracked[0].FirstIndex == 8 && ranges.Tracked[0].Length == 2, "overflow and sentinel ranges clipped");
        wrapper.RangesChanged(new(0, 0), []);
        Check(ranges.Visible!.Length == 0 && ranges.Tracked.Count == 0, "empty demand clears all retained positions");
        var calls = ranges.Calls;
        Throws<ArgumentNullException>(() => wrapper.RangesChanged(new(0, 1), [null!]));
        Check(ranges.Calls == calls, "invalid input is not partly delivered");
        ++passed;

        object? vectorSender = null, changingSender = null, changedSender = null;
        wrapper.VectorChanged += (sender, _) => vectorSender = sender;
        wrapper.CurrentChanging += (sender, args) => { changingSender = sender; args.Cancel = true; };
        wrapper.CurrentChanged += (sender, _) => changedSender = sender;
        native.Raise(CollectionChange.ItemChanged, 4);
        wrapper.RangesChanged(new(4, 1), []);
        var changing = new CurrentChangingEventArgs(true);
        native.RaiseChanging(changing); native.RaiseChanged();
        Check(ReferenceEquals(vectorSender, wrapper) && ReferenceEquals(changingSender, wrapper) &&
            ReferenceEquals(changedSender, wrapper) && changing.Cancel, "events preserve wrapper identity and cancellation");
        Check(wrapper.MoveCurrentToPosition(7) && wrapper.CurrentPosition == 7 && ReferenceEquals(wrapper.CurrentItem, native[7]),
            "currency delegates to native view");
        Check(wrapper.IndexOf(native[7]) == 7, "native item identity and index lookup preserved");
        ++passed;

        native.Raise(CollectionChange.Reset, 0);
        Throws<InvalidOperationException>(() => wrapper.RangesChanged(new(0, 1), []));
        wrapper.Dispose(); wrapper.Dispose();
        Check(native.SubscriptionCount == 0 && native.CurrencySubscriptions == 0 && native.Groups.SubscriptionCount == 0,
            "dispose removes every native subscription");
        Check(!ranges.Disposed && ranges.Visible!.Length == 0 && ranges.Tracked.Count == 0,
            "dispose clears demand without destroying caller-owned source");
        Throws<ObjectDisposedException>(() => wrapper.RangesChanged(new(0, 1), []));
        ++passed;

        using (var empty = new NativeGroupedRangeView(new View(0, 0), new Ranges()))
            empty.RangesChanged(new(0, uint.MaxValue), []);
        using (var changed = new NativeGroupedRangeView(new View(1), new Ranges()))
        {
            ((Vector)changed.CollectionGroups).Raise(CollectionChange.ItemChanged, 0);
            Throws<InvalidOperationException>(() => changed.RangesChanged(new(0, 1), []));
        }
        ++passed;
        return passed;
    }

    private static void Check(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException("Grouped range adapter: " + reason); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

    private sealed class Ranges : IItemsRangeInfo
    {
        public int Calls;
        public bool Disposed;
        public ItemIndexRange? Visible;
        public IReadOnlyList<ItemIndexRange> Tracked = [];
        public void RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
        { ++Calls; Visible = visibleRange; Tracked = trackedItems; }
        public void Dispose() => Disposed = true;
    }
    private sealed class Change(CollectionChange change, uint index) : IVectorChangedEventArgs
    { public CollectionChange CollectionChange => change; public uint Index => index; }
    private class Vector : IObservableVector<object>
    {
        private readonly List<object> values = [];
        private VectorChangedEventHandler<object>? changed;
        public int SubscriptionCount => changed?.GetInvocationList().Length ?? 0;
        public event VectorChangedEventHandler<object>? VectorChanged
        { add => changed += value; remove => changed -= value; }
        public void Raise(CollectionChange change, uint index) => changed?.Invoke(this, new Change(change, index));
        public int Count => values.Count;
        public bool IsReadOnly => false;
        public object this[int index] { get => values[index]; set => values[index] = value; }
        public void Add(object item) => values.Add(item);
        public void Clear() => values.Clear();
        public bool Contains(object item) => values.Contains(item);
        public void CopyTo(object[] array, int arrayIndex) => values.CopyTo(array, arrayIndex);
        public IEnumerator<object> GetEnumerator() => values.GetEnumerator();
        public int IndexOf(object item) => values.IndexOf(item);
        public void Insert(int index, object item) => values.Insert(index, item);
        public bool Remove(object item) => values.Remove(item);
        public void RemoveAt(int index) => values.RemoveAt(index);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class GroupView(Vector items) : ICollectionViewGroup
    { public object Group => this; public IObservableVector<object> GroupItems => items; }
    private sealed class View : Vector, ICollectionView
    {
        private EventHandler<object>? changed;
        private CurrentChangingEventHandler? changing;
        public readonly Vector Groups = new();
        public View(params int[] sizes)
        {
            foreach (var size in sizes)
            {
                var items = new Vector();
                for (var index = 0; index < size; ++index) { var item = new object(); items.Add(item); Add(item); }
                Groups.Add(new GroupView(items));
            }
        }
        public int CurrencySubscriptions => (changed?.GetInvocationList().Length ?? 0) + (changing?.GetInvocationList().Length ?? 0);
        public event EventHandler<object>? CurrentChanged { add => changed += value; remove => changed -= value; }
        public event CurrentChangingEventHandler? CurrentChanging { add => changing += value; remove => changing -= value; }
        public void RaiseChanging(CurrentChangingEventArgs args) => changing?.Invoke(this, args);
        public void RaiseChanged() => changed?.Invoke(this, new object());
        public IObservableVector<object> CollectionGroups => Groups;
        public object CurrentItem => this[CurrentPosition];
        public int CurrentPosition { get; private set; }
        public bool HasMoreItems => false;
        public bool IsCurrentAfterLast => CurrentPosition >= Count;
        public bool IsCurrentBeforeFirst => CurrentPosition < 0;
        public bool MoveCurrentTo(object item) => MoveCurrentToPosition(IndexOf(item));
        public bool MoveCurrentToPosition(int index) { CurrentPosition = index; return index >= 0 && index < Count; }
        public bool MoveCurrentToFirst() => MoveCurrentToPosition(0);
        public bool MoveCurrentToLast() => MoveCurrentToPosition(Count - 1);
        public bool MoveCurrentToNext() => MoveCurrentToPosition(CurrentPosition + 1);
        public bool MoveCurrentToPrevious() => MoveCurrentToPosition(CurrentPosition - 1);
        public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count) => throw new NotSupportedException();
    }
}
