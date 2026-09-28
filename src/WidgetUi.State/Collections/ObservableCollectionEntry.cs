using System.ComponentModel;

namespace WidgetRail.WidgetUi.State.Collections;

/// <summary>Stable binding target within one logical collection generation.</summary>
public sealed class ObservableCollectionEntry<TPayload> : INotifyPropertyChanged where TPayload : notnull
{
    public string Key { get; }
    public TPayload Value { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal ObservableCollectionEntry(string key, TPayload value) => (Key, Value) = (key, value);

    internal void Update(TPayload value)
    {
        Value = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}
