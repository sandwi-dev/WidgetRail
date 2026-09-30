using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using WidgetRail.WidgetBridge;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

/// <summary>A native observable slice; all groups share one flat query and range budget.</summary>
public sealed class WidgetIndexedGroup : IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly IndexedItemsSource<WidgetIndexedRow> source;
    private string header;
    private BridgeNodeRenderStyles? headerStyle;
    internal WidgetIndexedGroup(string key, string header, int startIndex, int count, IndexedItemsSource<WidgetIndexedRow> source)
    { Key = key; this.header = header; StartIndex = startIndex; Count = count; this.source = source; }
    public string Key { get; }
    public string Header
    {
        get => header;
        internal set { if (header == value) return; header = value; PropertyChanged?.Invoke(this, new(nameof(Header))); }
    }
    public object? HeaderStyle
    {
        get => headerStyle;
        internal set { if (ReferenceEquals(headerStyle, value)) return; headerStyle = (BridgeNodeRenderStyles?)value; PropertyChanged?.Invoke(this, new(nameof(HeaderStyle))); }
    }
    internal int StartIndex { get; }
    public int Count { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    // Immutable query membership; this interface lets WinUI retain indexed access
    // instead of manufacturing a copied collection by enumerating every row.
    public event NotifyCollectionChangedEventHandler? CollectionChanged { add { } remove { } }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public object? this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return source[StartIndex + index];
        }
        set => throw new NotSupportedException();
    }
    public int IndexOf(object? value) => source.IndexOf(value) - StartIndex is var index && index >= 0 && index < Count ? index : -1;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (var index = 0; index < Count; ++index) yield return this[index]; }
    public void CopyTo(Array array, int index) { for (var item = 0; item < Count; ++item) array.SetValue(this[item], index + item); }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
