using System.ComponentModel;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

/// <summary>A stable binding target for one logical position in a query.</summary>
public abstract class IndexedItem : INotifyPropertyChanged
{
    public abstract object? Content { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
}

public sealed class IndexedItem<T> : IndexedItem where T : notnull
{
    internal object Owner { get; }
    public int Index { get; }
    public string? Key { get; private set; }
    public T? Value { get; private set; }
    public bool HasValue => Key is not null;
    public bool Failed { get; private set; }
    public override object? Content => Value;

    internal IndexedItem(object owner, int index) => (Owner, Index) = (owner, index);
    internal void SetValue(string key, T value)
    {
        if (Key == key && !Failed && EqualityComparer<T>.Default.Equals(Value, value)) return;
        Key = key; Value = value; Failed = false;
        Changed(nameof(Key));
        Changed(nameof(Value));
        Changed(nameof(Content));
        Changed(nameof(HasValue));
        Changed(nameof(Failed));
    }
    internal void SetFailed()
    {
        Failed = true;
        Changed(nameof(Failed));
    }
}
