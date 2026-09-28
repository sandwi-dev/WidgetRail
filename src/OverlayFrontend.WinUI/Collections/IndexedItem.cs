using System.ComponentModel;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

/// <summary>A stable binding target for one logical position in a query.</summary>
public sealed class IndexedItem<T> : INotifyPropertyChanged where T : notnull
{
    internal object Owner { get; }
    public int Index { get; }
    public string? Key { get; private set; }
    public T? Value { get; private set; }
    public bool HasValue => Key is not null;
    public bool Failed { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal IndexedItem(object owner, int index) => (Owner, Index) = (owner, index);
    internal void SetValue(string key, T value)
    {
        if (Key == key && !Failed && EqualityComparer<T>.Default.Equals(Value, value)) return;
        Key = key; Value = value; Failed = false;
        PropertyChanged?.Invoke(this, new(nameof(Key)));
        PropertyChanged?.Invoke(this, new(nameof(Value)));
        PropertyChanged?.Invoke(this, new(nameof(HasValue)));
        PropertyChanged?.Invoke(this, new(nameof(Failed)));
    }
    internal void SetFailed()
    {
        Failed = true;
        PropertyChanged?.Invoke(this, new(nameof(Failed)));
    }
}
