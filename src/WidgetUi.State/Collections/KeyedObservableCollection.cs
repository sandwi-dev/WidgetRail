using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace WidgetRail.WidgetUi.State.Collections;

public enum CollectionRevisionResult { Applied, Stale, WrongAuthority }

/// <summary>
/// Retained logical ItemsSource with granular notifications. Construct and mutate on
/// the owning UI thread. Notifications are synchronous, not an atomic render transaction.
/// </summary>
public sealed class KeyedObservableCollection<TPayload> where TPayload : notnull
{
    private readonly ObservableCollection<ObservableCollectionEntry<TPayload>> _items = [];
    private readonly Dictionary<string, ObservableCollectionEntry<TPayload>> _entries = new(StringComparer.Ordinal);
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private bool _applying;
    private bool _faulted;

    public CollectionAuthority Authority { get; }
    public ReadOnlyObservableCollection<ObservableCollectionEntry<TPayload>> Items { get; }
    public long Generation { get; private set; } = -1;
    public long Revision { get; private set; } = -1;

    public KeyedObservableCollection(CollectionAuthority authority)
    {
        KeyedCollectionRevision<TPayload>.ValidateAuthority(authority);
        Authority = authority;
        Items = new(_items);
    }

    public bool TryGetEntry(string key, [NotNullWhen(true)] out ObservableCollectionEntry<TPayload>? entry)
    {
        CheckThread();
        return _entries.TryGetValue(key, out entry);
    }

    /// <summary>
    /// Rejects stale and foreign updates without events. Surviving keys retain wrappers
    /// within a generation. A new generation retires all wrappers, even with equal keys.
    /// An exception from a notification subscriber faults the model; reconstruct it
    /// rather than continuing from a partly observed update.
    /// </summary>
    public CollectionRevisionResult Apply(KeyedCollectionRevision<TPayload> revision)
    {
        CheckThread();
        ArgumentNullException.ThrowIfNull(revision);
        if (_faulted) throw new InvalidOperationException("A notification failed; replace the collection model.");
        if (_applying) throw new InvalidOperationException("Collection updates cannot be reentrant.");
        if (revision.Authority != Authority) return CollectionRevisionResult.WrongAuthority;
        if (revision.Generation < Generation ||
            (revision.Generation == Generation && revision.Revision <= Revision)) return CollectionRevisionResult.Stale;

        _applying = true;
        try
        {
            var sameGeneration = revision.Generation == Generation;
            var nextEntries = new Dictionary<string, ObservableCollectionEntry<TPayload>>(StringComparer.Ordinal);
            var updates = new List<(ObservableCollectionEntry<TPayload> Entry, TPayload Value)>();
            foreach (var item in revision.Items)
            {
                if (sameGeneration && _entries.TryGetValue(item.Key, out var retained))
                {
                    nextEntries.Add(item.Key, retained);
                    // Evaluate arbitrary payload equality before emitting any notifications.
                    if (!EqualityComparer<TPayload>.Default.Equals(retained.Value, item.Value)) updates.Add((retained, item.Value));
                }
                else nextEntries.Add(item.Key, new(item.Key, item.Value));
            }

            // Linear order check means append/prepend/eviction/content updates never use
            // IndexOf. General reorder uses Move, whose array shifts are inherently costly.
            var surviving = sameGeneration
                ? _items.Where(entry => nextEntries.ContainsKey(entry.Key)).Select(entry => entry.Key).ToArray()
                : [];
            var survivorIndex = 0;
            var preservesOrder = true;
            if (sameGeneration)
            {
                foreach (var item in revision.Items)
                {
                    if (!_entries.ContainsKey(item.Key)) continue;
                    if (!StringComparer.Ordinal.Equals(surviving[survivorIndex++], item.Key)) preservesOrder = false;
                }
            }
            try
            {
                for (var i = _items.Count - 1; i >= 0; --i)
                {
                    var old = _items[i];
                    if (sameGeneration && nextEntries.ContainsKey(old.Key)) continue;
                    _entries.Remove(old.Key);
                    _items.RemoveAt(i);
                }
                for (var i = 0; i < revision.Items.Count; ++i)
                {
                    var item = revision.Items[i];
                    var entry = nextEntries[item.Key];
                    if (!_entries.ContainsKey(item.Key))
                    {
                        _entries.Add(item.Key, entry);
                        _items.Insert(i, entry);
                    }
                    else if (!preservesOrder && !ReferenceEquals(_items[i], entry))
                    {
                        _items.Move(_items.IndexOf(entry), i);
                    }
                }
                foreach (var update in updates) update.Entry.Update(update.Value);
                Generation = revision.Generation;
                Revision = revision.Revision;
            }
            catch
            {
                _faulted = true;
                throw;
            }
            return CollectionRevisionResult.Applied;
        }
        finally { _applying = false; }
    }

    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Collection access must use its owning UI thread.");
    }
}
