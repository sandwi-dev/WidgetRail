using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public enum WidgetIndexedSelectionFailure { Changed, Busy }
public sealed class WidgetIndexedSelectionException : Exception
{
    public WidgetIndexedSelectionFailure Reason { get; }
    internal WidgetIndexedSelectionException(WidgetIndexedSelectionFailure reason, string message) : base(message) => Reason = reason;
}

public sealed partial class WidgetIndexedCollection<TQuery, TItem>
{
    private readonly SortedSet<int> selectedIndices = [];
    private bool selectionActive, selectionLimitReached;
    private int? selectionAnchor;
    private long selectionEpoch, selectionRevision;
    /// <summary>Published after explicit selection mutations, outside the source lock.
    /// Consumers can reconcile mode-dependent presentation using Selection; the
    /// framework continues to own input, epochs and selected item identities.</summary>
    public event EventHandler? SelectionChanged;
    private void PublishSelection()
    {
        try { SelectionChanged?.Invoke(this,EventArgs.Empty); }
        finally { changed(); }
    }

    /// <summary>Null for legacy sources. Selection contains positions in this exact query, never loaded-row membership.</summary>
    public IndexedCollectionSelection? Selection { get { lock (gate) return SelectionLocked(); } }
    private IndexedCollectionSelection? SelectionLocked() => options.EnableSelection
        ? new(selectionActive, selectionEpoch, Array.AsReadOnly(selectedIndices.ToArray()), selectionLimitReached){SupportsHeldSelection=true} : null;
    private void RequireSelection()
    {
        widgetLifetime.ThrowIfCancellationRequested();
        if (!options.EnableSelection) throw new InvalidOperationException("This source did not opt into selection.");
        if (current.Descriptor.Discovery is not null) throw new InvalidOperationException("Selection requires a complete finite query.");
    }
    private void ResetSelectionLocked(bool active)
    {
        selectedIndices.Clear(); selectionAnchor = null; selectionLimitReached = false;
        selectionActive = active; selectionEpoch = checked(selectionEpoch + 1); ++selectionRevision;
    }
    public void BeginSelection()
    {
        lock (gate) { RequireSelection(); if (selectionActive) return; ResetSelectionLocked(true); }
        PublishSelection();
    }
    public void EndSelection()
    {
        lock (gate) { RequireSelection(); if (!selectionActive && selectedIndices.Count == 0) return; ResetSelectionLocked(false); }
        PublishSelection();
    }
    public void ClearSelection()
    {
        lock (gate) { RequireSelection(); selectedIndices.Clear(); selectionAnchor = null; selectionLimitReached = false; ++selectionRevision; }
        PublishSelection();
    }
    /// <summary>Selects every matching position only when the entire query fits the 256-item bound. Rejection is atomic.</summary>
    public bool SelectAll()
    {
        bool accepted;
        lock (gate)
        {
            RequireSelection();
            if (!selectionActive) throw new InvalidOperationException("Begin selection first.");
            accepted = ApplySelectionLocked(0, IndexedSelectionGesture.SelectAll);
        }
        PublishSelection(); return accepted;
    }
    /// <summary>Replaces the selection with up to count positions starting at the beginning of the current query.</summary>
    public void SelectFirst(int count)
    {
        if (count is < 1 or > IndexedCollectionSelection.MaximumItems) throw new ArgumentOutOfRangeException(nameof(count));
        lock (gate)
        {
            RequireSelection();
            if (!selectionActive) throw new InvalidOperationException("Begin selection first.");
            SelectPrefixLocked(Math.Min(current.Descriptor.Count, count));
        }
        PublishSelection();
    }
    private bool SelectPrefixLocked(int count)
    {
        var anchor = count > 0 ? 0 : (int?)null;
        if (selectedIndices.Count == count && (count == 0 || selectedIndices.Max == count - 1) &&
            selectionAnchor == anchor && !selectionLimitReached) return true;
        selectedIndices.Clear();
        selectedIndices.UnionWith(Enumerable.Range(0, count));
        selectionAnchor = anchor;
        selectionLimitReached = false;
        ++selectionRevision;
        return true;
    }
    private ValueTask ApplySelectionAsync(Query captured, int index, IndexedSelectionInput input, CancellationToken token)
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested(); widgetLifetime.ThrowIfCancellationRequested();
            if (!options.EnableSelection || current.Descriptor != captured.Descriptor || input.Epoch != selectionEpoch ||
                index < 0 || index >= current.Descriptor.Count) return ValueTask.CompletedTask;
            if(input.Gesture==IndexedSelectionGesture.BeginAdd)
            {
                if(selectionActive)return ValueTask.CompletedTask;
                ResetSelectionLocked(true);
                ApplySelectionLocked(index,IndexedSelectionGesture.Add);
            }
            else
            {
                if(!selectionActive)return ValueTask.CompletedTask;
                var revision=selectionRevision;
                ApplySelectionLocked(index,input.Gesture);
                if(revision==selectionRevision)return ValueTask.CompletedTask;
            }
        }
        PublishSelection(); return ValueTask.CompletedTask;
    }
    private bool ApplySelectionLocked(int index, IndexedSelectionGesture gesture)
    {
        var next = new SortedSet<int>(selectedIndices);
        var anchor = selectionAnchor;
        switch (gesture)
        {
            case IndexedSelectionGesture.Add: next.Add(index); anchor??=index; break;
            case IndexedSelectionGesture.Clear: next.Clear(); anchor = null; break;
            case IndexedSelectionGesture.Toggle:
                if (!next.Remove(index)) next.Add(index);
                anchor = index; break;
            case IndexedSelectionGesture.Replace: next.Clear(); next.Add(index); anchor = index; break;
            case IndexedSelectionGesture.Extend:
            case IndexedSelectionGesture.ExtendAdd:
                var start = Math.Min(anchor ?? index, index); var end = Math.Max(anchor ?? index, index);
                if ((long)end - start + 1 > IndexedCollectionSelection.MaximumItems) return Reject();
                if (gesture == IndexedSelectionGesture.Extend) next.Clear();
                for (var item = start; item <= end; ++item) next.Add(item);
                anchor ??= index; break;
            case IndexedSelectionGesture.SelectAll:
                if (current.Descriptor.Count > IndexedCollectionSelection.MaximumItems) return Reject();
                return SelectPrefixLocked(current.Descriptor.Count);
            default: throw new ArgumentOutOfRangeException(nameof(gesture));
        }
        if (next.Count > IndexedCollectionSelection.MaximumItems) return Reject();
        if(selectedIndices.SetEquals(next)&&selectionAnchor==anchor&&!selectionLimitReached)return true;
        selectedIndices.Clear(); selectedIndices.UnionWith(next); selectionAnchor = anchor;
        selectionLimitReached = false; ++selectionRevision; return true;
        bool Reject() { selectionLimitReached = true; ++selectionRevision; return false; }
    }

    /// <summary>Resolves a bounded immutable selection before a domain mutation. No row templates are realized.
    /// Any query/content or selection change during the read rejects the result instead of returning a mixed selection.</summary>
    public async ValueTask<IReadOnlyList<TItem>> ReadSelectedAsync(CancellationToken cancellationToken = default)
    {
        Query captured; int[] indexes; long revision; CancellationToken queryToken;
        lock (gate)
        {
            RequireSelection();
            if (!selectionActive) throw new WidgetIndexedSelectionException(WidgetIndexedSelectionFailure.Changed, "Selection is no longer active.");
            captured = current; indexes = selectedIndices.ToArray(); revision = selectionRevision; queryToken = captured.Lifetime.Token;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, widgetLifetime, queryToken);
        lifetime.CancelAfter(options.ReadTimeout);
        var token = lifetime.Token;
        var result = new List<TItem>(indexes.Length);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
        for (var position = 0; position < indexes.Length;)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var start = indexes[position];
            var count = 1;
            while (count < IndexedCollectionLimits.MaximumRangeItems && position + count < indexes.Length &&
                indexes[position + count] == start + count) ++count;
            Task<IReadOnlyList<TItem>> read;
            lock (gate)
            {
                Current();
                if (activeLoads >= 4) throw new WidgetIndexedSelectionException(WidgetIndexedSelectionFailure.Busy, "The indexed source read limit has been reached.");
                ++activeLoads;
            }
            try
            {
                read = Task.Run(async () => await options.ReadRange(captured.Value, start, count, token).ConfigureAwait(false), lifetime.Token);
            }
            catch { lock (gate) --activeLoads; throw; }
            _ = read.ContinueWith(task => { _ = task.Exception; lock (gate) --activeLoads; }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var values = await read.WaitAsync(lifetime.Token).ConfigureAwait(false);
            if (values is null || values.Count != count) throw new InvalidOperationException("An indexed reader must return exactly the requested range.");
            while (position < indexes.Length && indexes[position] < start + count)
            {
                var index = indexes[position++]; var item = values[index - start];
                ArgumentNullException.ThrowIfNull(item);
                var key = options.ItemKey(item).Value;
                StableIdentifier.Validate(key, nameof(options.ItemKey));
                if (!keys.Add(key) || captured.Keys is { } order && order[index] != key)
                    throw new InvalidOperationException("Selected occurrences do not match the declared query.");
                result.Add(item);
            }
            lock (gate) Current();
        }
        lifetime.Token.ThrowIfCancellationRequested();
        lock (gate) Current();
        return Array.AsReadOnly(result.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !widgetLifetime.IsCancellationRequested)
        {
            lock (gate) Current();
            throw new TimeoutException("The selected item read timed out.");
        }

        void Current()
        {
            if (!ReferenceEquals(current, captured) || selectionRevision != revision || !selectionActive)
                throw new WidgetIndexedSelectionException(WidgetIndexedSelectionFailure.Changed, "The indexed query or selection changed while resolving selected items.");
        }
    }
}
