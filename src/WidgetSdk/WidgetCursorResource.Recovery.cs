using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>A bounded around-key read belongs to this resource, semantic reset, navigation intent and active lifetime.</summary>
public sealed class WidgetCursorRecoveryScope : IDisposable
{
    private Action<WidgetCursorRecoveryScope>? release;
    internal WidgetCursorRecoveryScope(object owner, long epoch, long intent, long mutations, CancellationToken lifetime, bool pending,
        Action<WidgetCursorRecoveryScope> release)
    { Owner = owner; Epoch = epoch; Intent = intent; Mutations = mutations; Lifetime = lifetime; Pending = pending; this.release = release; }
    internal object Owner { get; }
    internal long Epoch { get; }
    internal long Intent { get; }
    internal long Mutations { get; }
    internal CancellationToken Lifetime { get; }
    internal bool Pending { get; }
    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke(this);
}

public sealed partial class WidgetCursorResource<TItem> where TItem : notnull
{
    private long _recoveryIntentRevision;
    private WidgetCursorRecoveryScope? _recoveryScope;

    /// <summary>Capture before an external around-key read. A later navigation, reset or lifecycle exit rejects its completion.</summary>
    public WidgetCursorRecoveryScope CaptureRecovery()
    {
        lock (_gate) return _recoveryScope = new(this, _epoch, _recoveryIntentRevision, _liveRevision, _mutationLifetime(), _current is not null, ReleaseRecovery);
    }
    private void ReleaseRecovery(WidgetCursorRecoveryScope scope)
    { lock (_gate) if (ReferenceEquals(_recoveryScope, scope)) _recoveryScope = null; }
    private long RecoveryMutationRevisionLocked => _recoveryScope is { } scope && scope.Epoch == _epoch &&
        scope.Intent == _recoveryIntentRevision && !scope.Lifetime.IsCancellationRequested && !scope.Pending ? scope.Mutations : long.MaxValue;

    /// <summary>
    /// Commits one bounded provider page around an existing key without changing semantic reset identity or requesting focus.
    /// Live events received during the read are replayed. Returns false when the key disappeared or the captured scope retired.
    /// The caller must also hold its presentation recovery's commit admission; this method alone does not authorize scroll movement.
    /// </summary>
    public bool TryRecover(WidgetCursorRecoveryScope scope, WidgetCursorPage<TItem> loaded, WidgetCollectionItemKey anchor)
        => TryRecover(scope, loaded, anchor, [], out _);

    public bool TryRecover(WidgetCursorRecoveryScope scope, WidgetCursorPage<TItem> loaded, WidgetCollectionItemKey anchor,
        IReadOnlyList<WidgetCollectionItemKey> neighbors, out WidgetCollectionItemKey? restoredAnchor)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(neighbors);
        restoredAnchor = null;
        try
        {
        StableIdentifier.Validate(anchor.Value, nameof(anchor));
        if (neighbors.Count > IndexedCollectionLimits.MaximumContinuityItems) throw new ArgumentOutOfRangeException(nameof(neighbors));
        foreach (var neighbor in neighbors) StableIdentifier.Validate(neighbor.Value, nameof(neighbors));
        var page = Normalize(loaded, _options.MaximumRetainedItems);
        lock (_admissionGate)
        {
            lock (_gate)
            {
                if (!ReferenceEquals(_recoveryScope, scope) || !ReferenceEquals(scope.Owner, this) || scope.Epoch != _epoch || scope.Intent != _recoveryIntentRevision ||
                    scope.Lifetime.IsCancellationRequested || scope.Pending || _current is not null || _snapshot.ResetGeneration == 0) return false;
                IReadOnlyList<Segment> recovered = [new Segment(page, null, page.FirstItemIndex ?? _snapshot.StartIndex)];
                // Around reads can fill the entire retained window. Live-policy
                // cursors already supply item boundaries, so use their existing
                // page partitioning even without an intervening live mutation.
                // Otherwise the next adjacent read can only evict this whole
                // window, including a protected reader that should survive.
                if (_options.LiveUpdates is { } policy)
                    recovered = TransformLive(recovered, [], policy, false, out _);
                var proposed = ReplayLiveChanges(recovered, scope.Mutations);
                var items = new ReadOnlyCollection<TItem>(proposed.SelectMany(segment => segment.Page.Items).ToArray());
                WidgetCollectionItemKey? selected = ContainsKey(items, anchor) ? anchor : neighbors.Cast<WidgetCollectionItemKey?>().FirstOrDefault(key => key is { } value && ContainsKey(items, value));
                if (selected is null) return false;
                if (_windowGeneration >= ProtocolConstants.MaximumVirtualCollectionRequestGeneration)
                    throw new InvalidOperationException("Virtual collection request generation is exhausted.");
                _segments.Clear();
                foreach (var segment in proposed) _segments.AddLast(segment);
                foreach (var key in _liveChanges.Where(pair => pair.Value.Revision <= scope.Mutations).Select(pair => pair.Key).ToArray())
                    _liveChanges.Remove(key);
                _cursorHistory.Clear(); _cursorHistoryDirection = null;
                _failed = null;
                SetSnapshotLocked(WidgetPagedResourceStatus.Ready, items, proposed.FirstOrDefault()?.Page.Before,
                    proposed.LastOrDefault()?.Page.After, selected, null, null);
                _snapshot = _snapshot with
                {
                    WindowGeneration = ++_windowGeneration,
                    StartIndex = proposed.FirstOrDefault()?.StartIndex ?? 0,
                    FirstItemIndex = page.FirstItemIndex, TotalItemCount = page.TotalItemCount,
                    NavigationRequest = null, LoadingDirection = null, WindowChange = VirtualCollectionWindowChange.Replace,
                };
                ++_recoveryIntentRevision;
                restoredAnchor = selected;
            }
        }
        _invalidate();
        return true;
        }
        finally { scope.Dispose(); }
    }
}
