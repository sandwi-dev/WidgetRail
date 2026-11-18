using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Provider policy for an ordered live window with opaque, item-boundary cursors.</summary>
public sealed record WidgetCursorLiveOptions<TItem>(Comparison<TItem> Compare,
    Func<TItem, WidgetCollectionCursor> BoundaryCursor) where TItem : notnull;

/// <summary>One replacement/insertion or removal, identified by the resource's stable item key.</summary>
public sealed record WidgetCursorMutation<TItem>(WidgetCollectionItemKey Key, TItem? Value, bool IsRemoval = false)
    where TItem : notnull;

/// <summary>An exact resource/reset identity. It cannot authorize mutations of a replacement query.</summary>
public sealed class WidgetCursorMutationScope
{
    internal WidgetCursorMutationScope(object owner, long epoch, CancellationToken lifetime) { Owner = owner; Epoch = epoch; Lifetime = lifetime; }
    internal object Owner { get; }
    internal long Epoch { get; }
    internal CancellationToken Lifetime { get; }
}

public sealed record WidgetCursorMutationResult(bool Applied, bool HasUnseenItems, bool RequiresRefresh = false);

/// <summary>Authoritative provider paging edges after reconciling a retained interval.</summary>
public sealed record WidgetCursorBoundaryUpdate(WidgetCollectionCursor? Before, WidgetCollectionCursor? After);

public sealed partial class WidgetCursorResource<TItem> where TItem : notnull
{
    private const int MaximumLiveMutations = 512;
    private sealed record LiveChange(long Revision, WidgetCursorMutation<TItem> Mutation);
    private readonly Dictionary<string, LiveChange> _liveChanges = new(StringComparer.Ordinal);
    private long _liveRevision;
    private long _liveBoundaryRevision;
    private readonly Func<CancellationToken> _mutationLifetime;

    public WidgetCursorMutationScope MutationScope { get { lock (_gate) return new(this, _epoch, _mutationLifetime()); } }

    /// <summary>
    /// Reconciles keyed changes without resetting focus or paging identity. New
    /// tail items enter only a latest window. Eviction is opt-in: a caller must
    /// have authoritative permission to advance the retained reading window.
    /// </summary>
    public WidgetCursorMutationResult ApplyMutations(WidgetCursorMutationScope scope,
        IReadOnlyList<WidgetCursorMutation<TItem>> mutations, bool allowTailEviction = false,
        WidgetCursorBoundaryUpdate? boundaries = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(mutations);
        bool changed;
        bool unseen;
        lock (_admissionGate)
        {
          lock (_gate)
          {
            if (!ReferenceEquals(scope.Owner, this) || scope.Epoch != _epoch || scope.Lifetime.IsCancellationRequested) return new(false, false);
            var policy = _options.LiveUpdates ?? throw new InvalidOperationException("Live updates are not enabled for this cursor resource.");
            if (mutations.Count > MaximumLiveMutations) throw new ArgumentOutOfRangeException(nameof(mutations));
            var next = new Dictionary<string, LiveChange>(_liveChanges, StringComparer.Ordinal);
            var revision = _liveRevision;
            foreach (var mutation in mutations)
            {
                ArgumentNullException.ThrowIfNull(mutation);
                StableIdentifier.Validate(mutation.Key.Value, nameof(mutations));
                if (!mutation.IsRemoval && (mutation.Value is null || KeyOf(mutation.Value) != mutation.Key))
                    throw new ArgumentException("A live value must match its stable key.", nameof(mutations));
                next[mutation.Key.Value] = new(++revision, mutation);
            }
            var segments = _segments.ToArray();
            if(boundaries is not null)
            {
                if(boundaries.Before is {} before)StableIdentifier.Validate(before.Value,nameof(boundaries));
                if(boundaries.After is {} after)StableIdentifier.Validate(after.Value,nameof(boundaries));
                if(segments.Length>0)
                {
                    segments[0]=segments[0] with{Page=segments[0].Page with{Before=boundaries.Before}};
                    segments[^1]=segments[^1] with{Page=segments[^1].Page with{After=boundaries.After}};
                }
            }
            var proposed = TransformLive(segments, next.Values, policy, allowTailEviction, out unseen);
            proposed = MarkDeferredTailBoundary(proposed, next.Values, policy, ref unseen,out var incompleteInterval);
            if(incompleteInterval) { _recoveryScope = null; return new(false,true,true); }
            var retained = proposed.SelectMany(segment=>segment.Page.Items).Select(item=>KeyOf(item).Value).ToHashSet(StringComparer.Ordinal);
            var tail = proposed.LastOrDefault();
            var hasTailItem = tail is {Page.Items.Count:>0};
            var tailItem = hasTailItem?tail!.Page.Items[^1]:default!;
            var firstItem = proposed.SelectMany(segment=>segment.Page.Items).FirstOrDefault();
            var pendingRevision = boundaries is null && _current is {} pending ? pending.MutationRevision : long.MaxValue;
            pendingRevision = Math.Min(pendingRevision, RecoveryMutationRevisionLocked);
            // Committed items already live in the segments. Keep only updates
            // that can still race an in-flight page, plus a bounded deferred tail.
            next = next.Where(pair=>pair.Value.Revision>pendingRevision ||
                tail is not null && !pair.Value.Mutation.IsRemoval && !retained.Contains(pair.Key) &&
                (!hasTailItem || (proposed[0].Page.Before is null || policy.Compare(pair.Value.Mutation.Value!,firstItem!)>=0) &&
                    (tail.Page.After is null || policy.Compare(pair.Value.Mutation.Value!,tailItem)<=0)))
                .ToDictionary(pair=>pair.Key,pair=>pair.Value,StringComparer.Ordinal);
            if(next.Count>MaximumLiveMutations)
            {
                // An around-key read cannot commit after its bounded replay
                // journal missed an event. Recovery must use fresh provider data.
                _recoveryScope = null;
                return new(false,true,true);
            }
            var items = new ReadOnlyCollection<TItem>(proposed.SelectMany(segment => segment.Page.Items).ToArray());
            changed = !_snapshot.Items.SequenceEqual(items) || _snapshot.Before!=proposed.FirstOrDefault()?.Page.Before ||
                _snapshot.After!=proposed.LastOrDefault()?.Page.After;
            var requestedFocus=_snapshot.RequestedFocusId;
            if(requestedFocus is not null && !items.Any(item=>_viewports.Values.Any(viewport=>viewport.ItemFocusId(item)==requestedFocus)))
                requestedFocus=null;
            if (changed && _windowGeneration >= ProtocolConstants.MaximumVirtualCollectionRequestGeneration)
                throw new InvalidOperationException("Virtual collection request generation is exhausted.");
            // Validate all callbacks before changing either the journal or paint.
            _liveChanges.Clear();foreach (var pair in next) _liveChanges.Add(pair.Key, pair.Value);
            _liveRevision = revision;
            if(boundaries is not null)++_liveBoundaryRevision;
            if (changed)
            {
                _segments.Clear();foreach (var segment in proposed) _segments.AddLast(segment);
                var anchor = ResolveAnchor(_snapshot, items, null);
                SetSnapshotLocked(_snapshot.Status, items, proposed.FirstOrDefault()?.Page.Before,
                    proposed.LastOrDefault()?.Page.After, anchor, requestedFocus, _snapshot.Error);
                _snapshot = _snapshot with { WindowGeneration = ++_windowGeneration,
                    StartIndex = proposed.FirstOrDefault()?.StartIndex ?? _snapshot.StartIndex,
                    NavigationRequest=requestedFocus is null?null:_snapshot.NavigationRequest,
                    WindowChange = VirtualCollectionWindowChange.Replace };
                if (_current is { } request)
                    request.Before = _snapshot with { Status = request.Before.Status, Error = request.Before.Error };
            }
          }
          if(boundaries is not null)_operations.Cancel(_operationKey);
        }
        if (changed) _invalidate();
        return new(true, unseen);
    }

    private IReadOnlyList<Segment> ReplayLiveChanges(IReadOnlyList<Segment> segments, Request request)
        => ReplayLiveChanges(segments, request.MutationRevision);

    private IReadOnlyList<Segment> ReplayLiveChanges(IReadOnlyList<Segment> segments, long mutationRevision)
    {
        if (_options.LiveUpdates is not { } policy || _liveChanges.Count == 0) return segments;
        // A fresh read supersedes earlier observations; only events during it
        // can overrule its result. Adjacent reads retain existing tombstones.
        var changes = _liveChanges.Values.Where(change => change.Revision > mutationRevision).ToArray();
        var result=TransformLive(segments, changes, policy, false, out var unseen);
        result=MarkDeferredTailBoundary(result, changes, policy, ref unseen,out var incompleteInterval);
        if(incompleteInterval)throw new InvalidOperationException("Live interval changes exceeded the retained recovery bound.");
        return result;
    }

    private IReadOnlyList<Segment> MarkDeferredTailBoundary(IReadOnlyList<Segment> segments,
        IEnumerable<LiveChange> changes,WidgetCursorLiveOptions<TItem> policy,ref bool unseen,out bool incompleteInterval)
    {
        incompleteInterval=false;
        if(segments.LastOrDefault() is not {} last || last.Page.Items.Count==0)return segments;
        var keys=segments.SelectMany(segment=>segment.Page.Items).Select(item=>KeyOf(item).Value).ToHashSet(StringComparer.Ordinal);
        var tail=last.Page.Items[^1];
        var first=segments.SelectMany(segment=>segment.Page.Items).First();
        var deferred=changes.Where(change=>!change.Mutation.IsRemoval && !keys.Contains(change.Mutation.Key.Value) &&
            (segments[0].Page.Before is null || policy.Compare(change.Mutation.Value!,first)>=0) &&
            (last.Page.After is null || policy.Compare(change.Mutation.Value!,tail)<=0)).ToArray();
        if(deferred.Length<=_options.MaximumRetainedItems)return segments;
        if(deferred.Any(change=>policy.Compare(change.Mutation.Value!,tail)<=0)){incompleteInterval=true;return segments;}
        var after=policy.BoundaryCursor(tail);StableIdentifier.Validate(after.Value,nameof(policy.BoundaryCursor));
        var result=segments.ToArray();result[^1]=last with{Page=last.Page with{After=after}};
        unseen=true;
        return result;
    }

    private IReadOnlyList<Segment> TransformLive(IReadOnlyList<Segment> segments, IEnumerable<LiveChange> changes,
        WidgetCursorLiveOptions<TItem> policy, bool allowTailEviction, out bool unseen)
    {
        unseen = false;
        if (segments.Count == 0) { unseen = changes.Any(change => !change.Mutation.IsRemoval); return segments; }
        if (segments.Any(segment => segment.Page.FirstItemIndex is not null || segment.Page.TotalItemCount is not null))
            throw new InvalidOperationException("Live updates require opaque-index pages; known extents must be refreshed by their provider.");
        var original = segments.SelectMany(segment => segment.Page.Items).ToArray();
        var values = original.ToDictionary(item => KeyOf(item).Value, StringComparer.Ordinal);
        var first = segments[0];var last = segments[^1];
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes.OrderBy(change => change.Revision))
        {
            var mutation = change.Mutation;
            if (mutation.IsRemoval) { values.Remove(mutation.Key.Value); continue; }
            if (values.ContainsKey(mutation.Key.Value)) { values[mutation.Key.Value] = mutation.Value!; continue; }
            // Events and direct mutation acknowledgments can arrive out of
            // order. Insert anywhere inside known coverage, not only at its tail.
            if ((first.Page.Before is null || original.Length>0 && policy.Compare(mutation.Value!,original[0])>=0) &&
                (last.Page.After is null || original.Length>0 && policy.Compare(mutation.Value!,original[^1])<=0))
            { values.Add(mutation.Key.Value, mutation.Value!);added.Add(mutation.Key.Value); }
            else unseen = true;
        }
        var ordered = values.Values.ToList();ordered.Sort(policy.Compare);
        if (!allowTailEviction && ordered.Count > _options.MaximumRetainedItems)
        {
            // Keep the reading window. Pending values remain in the bounded
            // journal until a host-authorized advance or a fresh latest read.
            for (var index = ordered.Count - 1; index >= 0 && ordered.Count > _options.MaximumRetainedItems; index--)
                if (added.Contains(KeyOf(ordered[index]).Value)) { ordered.RemoveAt(index); unseen = true; }
        }
        var removed = Math.Max(0, ordered.Count - _options.MaximumRetainedItems);
        if (removed > 0) ordered.RemoveRange(0, removed);
        var before = removed > 0 && ordered.Count > 0 ? Boundary(ordered[0]) : first.Page.Before;
        var result = new List<Segment>();
        if (ordered.Count == 0) return [new(new([], before, last.Page.After), first.RequestCursor, first.StartIndex)];
        for (var offset = 0; offset < ordered.Count; offset += _options.PageSize)
        {
            var pageItems = ordered.Skip(offset).Take(_options.PageSize).ToArray();
            result.Add(new(new(pageItems, offset == 0 ? before : Boundary(pageItems[0]),
                offset + pageItems.Length == ordered.Count ? last.Page.After : Boundary(pageItems[^1])),
                null, checked(first.StartIndex + removed + offset)));
        }
        return result;

        WidgetCollectionCursor Boundary(TItem item)
        {
            var cursor = policy.BoundaryCursor(item);
            StableIdentifier.Validate(cursor.Value, nameof(policy.BoundaryCursor));
            return cursor;
        }
    }
}
