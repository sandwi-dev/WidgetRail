using System.Collections.Specialized;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Collections;

internal sealed partial class IndexedItemsSource<T> where T : notnull
{
    // Bounded occurrence maps are an explicit continuity contract. Restrict this
    // path to insert/remove edits: arbitrary reorder still uses query replacement.
    internal static bool CanRebaseWindow(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        if (before.Count == 0 || after.Count == 0 ||
            Math.Max(before.Count, after.Count) > IndexedCollectionLimits.MaximumContinuityItems) return false;
        var oldKeys = before.ToHashSet(StringComparer.Ordinal);
        var newKeys = after.ToHashSet(StringComparer.Ordinal);
        if (oldKeys.Count != before.Count || newKeys.Count != after.Count) return false;
        var retained = before.Where(newKeys.Contains).ToArray();
        return retained.Length > 0 && retained.SequenceEqual(after.Where(oldKeys.Contains), StringComparer.Ordinal);
    }

    internal void RebaseWindow(IndexedQueryIdentity query, long revision,
        IReadOnlyList<string> before, IReadOnlyList<string> after, PreparedIndexedWindow<T>? prepared = null)
    {
        CheckAccess();
        if (query.Authority != Query.Authority || query.Generation <= Query.Generation || revision < 0 ||
            before.Count != Count || !CanRebaseWindow(before, after))
            throw new InvalidOperationException("The query is not an ordered bounded occurrence update.");
        prepared?.Validate(query, revision, after, pageSize);

        // Cancel even admitted batches that have yielded between slot updates.
        // Retained pixels never keep their old range/action authority alive.
        foreach (var fetch in ownedFetches) { fetch.Cancellation.Cancel(); ++CancelledLoads; }
        fetching.Clear(); failedPages.Clear();
        foreach (var page in pages.Values) Release(page.Lifetime);
        pages.Clear();
        Query = query; ContentRevision = revision;
        var ready = prepared?.Take();
        if (ready is not null)
            foreach (var range in ready) pages.Add(range.StartIndex / pageSize, range);

        var positions = after.Select((key, index) => (key, index))
            .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.Ordinal);
        IReadOnlyList<int> RemapDemand(IReadOnlyList<int> demand) => demand
            .SelectMany(page => Enumerable.Range(page * pageSize, Math.Max(0, Math.Min(pageSize, before.Count - page * pageSize))))
            .Where(index => positions.ContainsKey(before[index])).Select(index => positions[before[index]] / pageSize).Distinct().ToArray();
        visiblePages = RemapDemand(visiblePages); trackedPages = RemapDemand(trackedPages);
        var ordered = Enumerable.Range(0, Count).Select(GetSlot).ToList();
        var keys = before.ToList();
        for (var index = keys.Count - 1; index >= 0; --index)
        {
            if (positions.ContainsKey(keys[index])) continue;
            var removed = ordered[index];
            ordered.RemoveAt(index); keys.RemoveAt(index);
            retainedSlots.Remove(removed); removed.Reindex(-1);
            Reindex();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
            if (!Current()) return;
        }
        for (var index = 0; index < after.Count; ++index)
        {
            if (index < keys.Count && keys[index] == after[index]) continue;
            var inserted = new IndexedItem<T>(identity, index);
            if (ready is not null)
            {
                var item = ready[index / pageSize].Items[index % pageSize];
                inserted.SetValue(item.Key, item.Value);
            }
            ordered.Insert(index, inserted); keys.Insert(index, after[index]);
            Reindex();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, inserted, index));
            if (!Current()) return;
        }
        // Container policy needs its new index; content bindings remain intact.
        foreach (var slot in ordered)
        {
            if (ready is not null)
            {
                var item = ready[slot.Index / pageSize].Items[slot.Index % pageSize];
                slot.SetValue(item.Key,item.Value);
                if (!Current()) return;
            }
            slot.ClearFailure(); slot.NotifyIndexChanged();
            if (!Current()) return;
        }
        RebuildDemand();
        StateChanged?.Invoke(this, EventArgs.Empty);

        bool Current() => !disposed && Query == query && ContentRevision == revision;

        void Reindex()
        {
            slots.Clear(); Count = ordered.Count;
            for (var index = 0; index < ordered.Count; ++index)
            { ordered[index].Reindex(index); slots.Add(index, ordered[index]); }
            PeakResidentSlots = Math.Max(PeakResidentSlots, slots.Count);
        }
    }
}
