namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record WidgetStateOwner(string WidgetId, string InstanceId, string RuntimeGeneration,
    string PresentationGeneration, string PackageContentDigest);

/// <summary>Bounded lightweight state history, independent of the native view cache.</summary>
internal sealed class WidgetStateHistory<TState> where TState : class
{
    internal const int MaximumEntries = 256;
    private sealed record Entry(WidgetStateOwner Owner, TState State, long LastUse);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly int capacity;
    private long use;
    internal int Count => entries.Count;

    internal WidgetStateHistory(int capacity = MaximumEntries)
    {
        if (capacity is < 1 or > MaximumEntries) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    internal void Remember(WidgetStateOwner owner, TState state)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(state);
        entries[owner.WidgetId] = new(owner, state, ++use);
        if (entries.Count > capacity) entries.Remove(entries.MinBy(pair => pair.Value.LastUse).Key);
    }

    internal bool TryGet(WidgetStateOwner owner, out TState? state)
    {
        state = null;
        if (!entries.TryGetValue(owner.WidgetId, out var entry)) return false;
        if (entry.Owner != owner) { entries.Remove(owner.WidgetId); return false; }
        entries[owner.WidgetId] = entry with { LastUse = ++use };
        state = entry.State; return true;
    }

    internal void Reconcile(IEnumerable<WidgetStateOwner> catalog, bool complete)
    {
        var owners = catalog.ToDictionary(owner => owner.WidgetId, StringComparer.Ordinal);
        foreach (var (id, entry) in entries.ToArray())
            if (owners.TryGetValue(id, out var owner) ? owner != entry.Owner : complete) entries.Remove(id);
    }

    internal void Remove(string id) => entries.Remove(id);
    internal void Clear() => entries.Clear();
}
