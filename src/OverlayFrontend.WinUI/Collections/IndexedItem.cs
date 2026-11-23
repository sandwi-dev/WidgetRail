using System.ComponentModel;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

/// <summary>A stable binding target for one occurrence; bounded window edits may move its index.</summary>
public abstract class IndexedItem : INotifyPropertyChanged
{
    public abstract object? Content { get; }
    public abstract bool IsAwaitingContent { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    // Native container policy consumes one completed update, independently of
    // the per-property notifications needed by XAML bindings.
    internal event EventHandler? StateChanged;
    // Native containers explicitly subscribe until recycling/detachment. Their
    // stable binding identity must survive temporary changes in range demand.
    internal bool HasStateObservers => StateChanged is not null;
    protected void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
    protected void StateUpdated() => StateChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class IndexedItem<T> : IndexedItem where T : notnull
{
    internal object Owner { get; }
    public int Index { get; private set; }
    public string? Key { get; private set; }
    public T? Value { get; private set; }
    public bool HasValue => Key is not null;
    public bool Failed { get; private set; }
    public override object? Content => Value;
    public override bool IsAwaitingContent => !HasValue && !Failed;

    internal IndexedItem(object owner, int index) => (Owner, Index) = (owner, index);
    internal void Reindex(int index) => Index = index;
    internal void NotifyIndexChanged() { Changed(nameof(Index)); StateUpdated(); }
    internal void SetValue(string key, T value)
    {
        if (Key == key && !Failed && EqualityComparer<T>.Default.Equals(Value, value)) return;
        var wasWaiting = IsAwaitingContent;
        Key = key; Value = value; Failed = false;
        Changed(nameof(Key));
        Changed(nameof(Value));
        Changed(nameof(Content));
        Changed(nameof(HasValue));
        Changed(nameof(Failed));
        if (wasWaiting) Changed(nameof(IsAwaitingContent));
        StateUpdated();
    }
    internal void SetFailed()
    {
        var wasWaiting = IsAwaitingContent;
        Failed = true;
        Changed(nameof(Failed));
        if (wasWaiting) Changed(nameof(IsAwaitingContent));
        StateUpdated();
    }
    internal void ClearFailure()
    {
        if (!Failed) return;
        Failed = false;
        Changed(nameof(Failed));
        if (IsAwaitingContent) Changed(nameof(IsAwaitingContent));
        StateUpdated();
    }
}
