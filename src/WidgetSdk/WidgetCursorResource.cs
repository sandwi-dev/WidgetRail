using System.Collections.ObjectModel;

namespace GameBarAlternative.WidgetSdk;

public enum WidgetCursorDirection { Before, After }

public sealed record WidgetCursorPage<TItem>(
    IReadOnlyList<TItem> Items,
    WidgetCollectionCursor? Before,
    WidgetCollectionCursor? After) where TItem : notnull;

public sealed record WidgetCursorViewport<TItem>(
    string ScrollId,
    Func<TItem, WidgetCollectionItemKey> ItemKey,
    Func<TItem, string> ItemFocusId,
    string? EmptyFocusId = null) where TItem : notnull;

public sealed record WidgetCursorResourceOptions<TItem> where TItem : notnull
{
    public required int PageSize { get; init; }
    public int MaximumRetainedItems { get; init; } = 256;
    public int PaginationThreshold { get; init; } = 2;
    public required Func<WidgetCollectionCursor?, WidgetCursorDirection?, int,
        CancellationToken, ValueTask<WidgetCursorPage<TItem>>> LoadPage { get; init; }
    public required Func<Exception, WidgetResourceError> MapError { get; init; }
    public required IReadOnlyList<WidgetCursorViewport<TItem>> Viewports { get; init; }
    public WidgetOperationLifetime Lifetime { get; init; } = WidgetOperationLifetime.Active;
}

public sealed record WidgetCursorResourceSnapshot<TItem>(
    WidgetPagedResourceStatus Status,
    IReadOnlyList<TItem> Items,
    WidgetCollectionCursor? Before,
    WidgetCollectionCursor? After,
    WidgetCollectionItemKey? Anchor,
    string? RequestedFocusId,
    WidgetResourceError? Error,
    long Revision) where TItem : notnull
{
    public bool HasBefore => Before is not null;
    public bool HasAfter => After is not null;
}

/// <summary>
/// One bounded, bidirectional cursor collection. Provider cursors are opaque;
/// successful adjacent loads append or prepend instead of replacing the
/// current window. The retained window, pending work, cursor history, and
/// serialized item count are all finite.
/// </summary>
public sealed class WidgetCursorResource<TItem> where TItem : notnull
{
    public const int MaximumPageSize = 100;
    public const int MaximumRetainedItems = 256;
    public const int MaximumCursorHistory = 128;

    private sealed record Intent(
        WidgetCollectionCursor? Cursor,
        WidgetCursorDirection? Direction,
        string? SourceScrollId,
        bool Refresh);
    private sealed record Segment(
        WidgetCursorPage<TItem> Page,
        WidgetCollectionCursor? RequestCursor);

    private sealed class Request(Intent intent, long epoch,
        WidgetCursorResourceSnapshot<TItem> before)
    {
        internal Intent Intent { get; } = intent;
        internal long Epoch { get; } = epoch;
        internal WidgetCursorResourceSnapshot<TItem> Before { get; } = before;
        internal TaskCompletionSource<WidgetOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly object _gate = new();
    private readonly object _admissionGate = new();
    private readonly string _operationKey;
    private readonly string _beforeActionId;
    private readonly string _afterActionId;
    private readonly WidgetCursorResourceOptions<TItem> _options;
    private readonly IReadOnlyDictionary<string, WidgetCursorViewport<TItem>> _viewports;
    private readonly WidgetOperations _operations;
    private readonly Action _invalidate;
    private readonly HashSet<string> _cursorHistory = new(StringComparer.Ordinal);
    private WidgetCursorDirection? _cursorHistoryDirection;
    private readonly LinkedList<Segment> _segments = [];
    private WidgetCursorResourceSnapshot<TItem> _snapshot = new(
        WidgetPagedResourceStatus.NotLoaded, [], null, null, null, null, null, 0);
    private Request? _current;
    private Intent? _failed;
    private long _epoch;

    internal WidgetCursorResource(string operationKey,
        WidgetCursorResourceOptions<TItem> options,
        WidgetOperations operations, Action invalidate)
    {
        StableIdentifier.Validate(operationKey, nameof(operationKey));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.LoadPage);
        ArgumentNullException.ThrowIfNull(options.MapError);
        ArgumentNullException.ThrowIfNull(options.Viewports);
        if (options.PageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(options.PageSize));
        if (options.MaximumRetainedItems < options.PageSize ||
            options.MaximumRetainedItems > MaximumRetainedItems ||
            options.MaximumRetainedItems < Math.Min(MaximumRetainedItems, options.PageSize * 2))
            throw new ArgumentOutOfRangeException(nameof(options.MaximumRetainedItems));
        if (options.PaginationThreshold is < 1 or >
            GameBarAlternative.WidgetProtocol.ProtocolConstants.MaximumScrollPaginationThreshold)
            throw new ArgumentOutOfRangeException(nameof(options.PaginationThreshold));
        if (!Enum.IsDefined(options.Lifetime))
            throw new ArgumentOutOfRangeException(nameof(options.Lifetime));

        var viewports = new Dictionary<string, WidgetCursorViewport<TItem>>(StringComparer.Ordinal);
        foreach (var viewport in options.Viewports)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            StableIdentifier.Validate(viewport.ScrollId, nameof(options.Viewports));
            ArgumentNullException.ThrowIfNull(viewport.ItemKey);
            ArgumentNullException.ThrowIfNull(viewport.ItemFocusId);
            if (viewport.EmptyFocusId is not null)
                StableIdentifier.Validate(viewport.EmptyFocusId, nameof(options.Viewports));
            if (!viewports.TryAdd(viewport.ScrollId, viewport))
                throw new ArgumentException("Cursor viewport IDs must be unique.", nameof(options.Viewports));
        }
        if (viewports.Count == 0)
            throw new ArgumentException("At least one cursor viewport is required.", nameof(options.Viewports));

        _operationKey = operationKey;
        _beforeActionId = operationKey + ".cursor.before";
        _afterActionId = operationKey + ".cursor.after";
        StableIdentifier.Validate(_beforeActionId, nameof(operationKey));
        StableIdentifier.Validate(_afterActionId, nameof(operationKey));
        _options = options;
        _viewports = new ReadOnlyDictionary<string, WidgetCursorViewport<TItem>>(viewports);
        _operations = operations;
        _invalidate = invalidate;
    }

    public WidgetCursorResourceSnapshot<TItem> Snapshot { get { lock (_gate) return _snapshot; } }
    public bool IsBusy => _operations.IsBusy(_operationKey);
    public int RetainedItemCount { get { lock (_gate) return _snapshot.Items.Count; } }
    public int RetainedCursorCount { get { lock (_gate) return _cursorHistory.Count; } }

    public WidgetOperationHandle EnsureLoaded(bool forceRefresh = false)
    {
        lock (_gate)
            if (!forceRefresh && _snapshot.Status == WidgetPagedResourceStatus.Ready)
                return Completed();
        return forceRefresh ? Refresh() : Start(new(null, null, null, false));
    }

    public WidgetOperationHandle Refresh()
    {
        WidgetCollectionCursor? cursor;
        lock (_gate)
        {
            var anchored = _snapshot.Anchor is { } anchor
                ? _segments.FirstOrDefault(segment =>
                    segment.Page.Items.Any(item =>
                        KeyOf(item) == anchor))
                : null;
            cursor = anchored?.RequestCursor ?? _segments.First?.Value.RequestCursor;
        }
        return Start(new(cursor, null, null, true));
    }

    public WidgetOperationHandle Move(WidgetCursorDirection direction, string sourceScrollId)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        StableIdentifier.Validate(sourceScrollId, nameof(sourceScrollId));
        WidgetCollectionCursor? cursor;
        lock (_gate)
        {
            if (!_viewports.ContainsKey(sourceScrollId))
                throw new ArgumentException("The source is not a configured cursor viewport.", nameof(sourceScrollId));
            cursor = direction == WidgetCursorDirection.Before ? _snapshot.Before : _snapshot.After;
        }
        return cursor is null ? Completed() : Start(new(cursor, direction, sourceScrollId, false));
    }

    public WidgetOperationHandle Retry()
    {
        Intent? failed;
        lock (_gate) failed = _failed;
        return failed is not null ? Start(failed) : Refresh();
    }

    public bool TryHandlePagination(WidgetActionEvent action, out WidgetOperationHandle operation)
    {
        ArgumentNullException.ThrowIfNull(action);
        var direction = action.ActionId == _beforeActionId ? WidgetCursorDirection.Before :
            action.ActionId == _afterActionId ? WidgetCursorDirection.After : (WidgetCursorDirection?)null;
        if (direction is null || !_viewports.ContainsKey(action.SourceElementId))
        {
            operation = default;
            return false;
        }
        operation = Move(direction.Value, action.SourceElementId);
        return true;
    }

    public ScrollElement Present(ScrollElement scroll)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        if (!_viewports.ContainsKey(scroll.Id))
            throw new ArgumentException("The scroll is not a configured cursor viewport.", nameof(scroll));
        var snapshot = Snapshot;
        var before = snapshot.HasBefore ? _beforeActionId : null;
        var after = snapshot.HasAfter ? _afterActionId : null;
        var result = before is null && after is null ? scroll :
            scroll.Paginate(before, after, _options.PaginationThreshold);
        return result with { CollectionAnchorKey = snapshot.Anchor?.Value };
    }

    public WidgetElement PresentItem(TItem item, WidgetElement element)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(element);
        return element.CollectionItem(KeyOf(item));
    }

    public void SelectAnchor(WidgetCollectionItemKey key, bool invalidate = true)
    {
        bool changed;
        lock (_gate)
        {
            if (!ContainsKey(_snapshot.Items, key)) return;
            changed = SetSnapshotLocked(_snapshot.Status, _snapshot.Items,
                _snapshot.Before, _snapshot.After, key, _snapshot.RequestedFocusId, _snapshot.Error);
        }
        if (changed && invalidate) _invalidate();
    }

    public void ClearRequestedFocus(bool invalidate = true)
    {
        bool changed;
        lock (_gate) changed = SetSnapshotLocked(_snapshot.Status, _snapshot.Items,
            _snapshot.Before, _snapshot.After, _snapshot.Anchor, null, _snapshot.Error);
        if (changed && invalidate) _invalidate();
    }

    public void Reset(bool invalidate = true)
    {
        bool changed;
        lock (_admissionGate)
        {
            lock (_gate)
            {
                _epoch++;
                _current = null;
                _failed = null;
                _cursorHistory.Clear();
                _cursorHistoryDirection = null;
                _segments.Clear();
                changed = SetSnapshotLocked(WidgetPagedResourceStatus.NotLoaded,
                    [], null, null, null, null, null);
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    public Task WhenIdleAsync(CancellationToken token = default) =>
        _operations.WhenIdleAsync(_operationKey, token);

    private WidgetOperationHandle Start(Intent intent)
    {
        lock (_admissionGate)
        {
            Request request;
            bool changed;
            lock (_gate)
            {
                var before = _snapshot;
                request = new(intent, _epoch, before);
                _current = request;
                var status = before.Items.Count == 0 ? WidgetPagedResourceStatus.Loading :
                    intent.Direction is null ? WidgetPagedResourceStatus.Refreshing :
                    WidgetPagedResourceStatus.LoadingAdjacent;
                changed = SetSnapshotLocked(status, before.Items, before.Before, before.After,
                    before.Anchor, before.RequestedFocusId, null);
            }
            var underlying = _operations.RunLatest(_operationKey,
                context => LoadCoreAsync(request, context), _options.Lifetime);
            if (changed && underlying.IsAccepted && underlying.Admission != WidgetOperationAdmission.Started)
                _invalidate();
            Observe(request, underlying);
            return new(underlying.Admission, request.Completion.Task);
        }
    }

    private async ValueTask LoadCoreAsync(Request request, WidgetOperationContext context)
    {
        var committed = false;
        try
        {
            var loaded = await _options.LoadPage(request.Intent.Cursor,
                request.Intent.Direction, _options.PageSize, context.CancellationToken).ConfigureAwait(false);
            var page = Normalize(loaded);
            lock (_gate)
            {
                if (!CanCommit(request, context)) return;
                ValidateTraversalProgress(request.Intent, page);
                var merged = Merge(page, request.Intent.Cursor, request.Intent.Direction);
                var anchor = ResolveAnchor(request.Before, merged, request.Intent.Direction);
                var focus = ResolveFocus(page.Items, request.Intent);
                var beforeCursor = _segments.First?.Value.Page.Before;
                var afterCursor = _segments.Last?.Value.Page.After;
                CommitTraversalProgress(request.Intent, page);
                _current = null;
                _failed = null;
                SetSnapshotLocked(WidgetPagedResourceStatus.Ready, merged,
                    beforeCursor, afterCursor, anchor, focus, null);
                committed = true;
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!CanCommit(request, context)) throw;
                _current = null;
                _failed = request.Intent;
                SetSnapshotLocked(WidgetPagedResourceStatus.Error, request.Before.Items,
                    request.Before.Before, request.Before.After, request.Before.Anchor,
                    request.Before.RequestedFocusId, MapError(exception));
                committed = true;
            }
            throw;
        }
        finally { if (!committed) Restore(request); }
    }

    private WidgetCursorPage<TItem> Normalize(WidgetCursorPage<TItem>? page)
    {
        if (page is null || page.Items is null || page.Items.Count > _options.PageSize ||
            page.Items.Any(item => item is null))
            throw new InvalidOperationException("Invalid cursor page.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in page.Items)
        {
            var key = KeyOf(item).Value;
            if (!keys.Add(key)) throw new InvalidOperationException("Duplicate collection item key.");
        }
        if (page.Before is { } before) StableIdentifier.Validate(before.Value, nameof(page.Before));
        if (page.After is { } after) StableIdentifier.Validate(after.Value, nameof(page.After));
        return new(new ReadOnlyCollection<TItem>(page.Items.ToArray()), page.Before, page.After);
    }

    private IReadOnlyList<TItem> Merge(WidgetCursorPage<TItem> page,
        WidgetCollectionCursor? requestCursor,
        WidgetCursorDirection? direction)
    {
        var proposed = direction is null ? new List<Segment>() : _segments.ToList();
        var incoming = new Segment(page, requestCursor);
        if (direction == WidgetCursorDirection.Before) proposed.Insert(0, incoming);
        else proposed.Add(incoming);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in proposed)
        foreach (var item in segment.Page.Items)
        {
            if (!keys.Add(KeyOf(item).Value))
                throw new InvalidOperationException("Duplicate collection item key.");
        }
        var count = proposed.Sum(segment => segment.Page.Items.Count);
        while (count > _options.MaximumRetainedItems && proposed.Count > 1)
        {
            var removed = direction == WidgetCursorDirection.Before
                ? proposed[^1] : proposed[0];
            if (direction == WidgetCursorDirection.Before) proposed.RemoveAt(proposed.Count - 1);
            else proposed.RemoveAt(0);
            count -= removed.Page.Items.Count;
        }
        _segments.Clear();
        foreach (var segment in proposed) _segments.AddLast(segment);
        var result = proposed.SelectMany(segment => segment.Page.Items).ToList();
        return new ReadOnlyCollection<TItem>(result);
    }

    private WidgetCollectionItemKey? ResolveAnchor(WidgetCursorResourceSnapshot<TItem> before,
        IReadOnlyList<TItem> merged, WidgetCursorDirection? direction)
    {
        if (before.Anchor is { } retained && ContainsKey(merged, retained)) return retained;
        if (merged.Count == 0) return null;
        if (before.Anchor is { } removed && before.Items.Count != 0)
        {
            var old = IndexOf(before.Items, removed);
            if (old >= 0) return KeyOf(merged[Math.Min(old, merged.Count - 1)]);
        }
        return KeyOf(direction == WidgetCursorDirection.Before ? merged[^1] : merged[0]);
    }

    private string? ResolveFocus(IReadOnlyList<TItem> items, Intent intent)
    {
        if (intent.Direction is null || intent.SourceScrollId is null) return null;
        var viewport = _viewports[intent.SourceScrollId];
        if (items.Count == 0) return viewport.EmptyFocusId;
        var item = intent.Direction == WidgetCursorDirection.Before ? items[^1] : items[0];
        var id = viewport.ItemFocusId(item);
        StableIdentifier.Validate(id, nameof(viewport.ItemFocusId));
        return id;
    }

    private bool ContainsKey(IReadOnlyList<TItem> items, WidgetCollectionItemKey key) =>
        IndexOf(items, key) >= 0;
    private int IndexOf(IReadOnlyList<TItem> items, WidgetCollectionItemKey key)
    {
        for (var i = 0; i < items.Count; i++) if (KeyOf(items[i]) == key) return i;
        return -1;
    }
    private WidgetCollectionItemKey KeyOf(TItem item)
    {
        var key = _viewports.Values.First().ItemKey(item);
        StableIdentifier.Validate(key.Value, nameof(WidgetCursorViewport<TItem>.ItemKey));
        foreach (var viewport in _viewports.Values.Skip(1))
            if (viewport.ItemKey(item) != key)
                throw new InvalidOperationException("Cursor viewport item keys must agree.");
        return key;
    }
    private void ValidateTraversalProgress(Intent intent, WidgetCursorPage<TItem> page)
    {
        if (intent.Direction is not { } direction) return;
        var outbound = direction == WidgetCursorDirection.Before ? page.Before : page.After;
        if (outbound is { } next && intent.Cursor == next)
            throw new InvalidOperationException("Cursor loop.");

        var sameTraversal = _cursorHistoryDirection == direction;
        if (sameTraversal && outbound is { } repeated &&
            _cursorHistory.Contains(repeated.Value))
            throw new InvalidOperationException("Cursor loop.");

        var projected = sameTraversal ? _cursorHistory.Count : 0;
        if (intent.Cursor is { } requested &&
            (!sameTraversal || !_cursorHistory.Contains(requested.Value))) projected++;
        if (outbound is { } candidate &&
            (!sameTraversal || !_cursorHistory.Contains(candidate.Value))) projected++;
        if (projected > MaximumCursorHistory)
            throw new InvalidOperationException("Cursor traversal exceeded its bound.");
    }

    private void CommitTraversalProgress(Intent intent, WidgetCursorPage<TItem> page)
    {
        if (intent.Direction is not { } direction)
        {
            _cursorHistory.Clear();
            _cursorHistoryDirection = null;
            return;
        }
        if (_cursorHistoryDirection != direction)
        {
            _cursorHistory.Clear();
            _cursorHistoryDirection = direction;
        }
        if (intent.Cursor is { } requested) _cursorHistory.Add(requested.Value);
        var outbound = direction == WidgetCursorDirection.Before ? page.Before : page.After;
        if (outbound is { } next) _cursorHistory.Add(next.Value);
    }
    private bool CanCommit(Request request, WidgetOperationContext context) =>
        request.Epoch == _epoch && ReferenceEquals(_current, request) && context.IsCurrent;
    private WidgetResourceError MapError(Exception exception)
    {
        try { return _options.MapError(exception) ?? WidgetResourceError.Unexpected; }
        catch { return WidgetResourceError.Unexpected; }
    }
    private void Observe(Request request, WidgetOperationHandle underlying) =>
        underlying.Completion.GetAwaiter().OnCompleted(() =>
        {
            var result = underlying.Completion.GetAwaiter().GetResult();
            Restore(request);
            request.Completion.TrySetResult(result);
        });
    private void Restore(Request request)
    {
        bool changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_current, request) && request.Epoch == _epoch)
            {
                _current = null;
                var value = request.Before;
                changed = SetSnapshotLocked(value.Status, value.Items, value.Before, value.After,
                    value.Anchor, value.RequestedFocusId, value.Error);
            }
        }
        if (changed) _invalidate();
    }
    private bool SetSnapshotLocked(WidgetPagedResourceStatus status, IReadOnlyList<TItem> items,
        WidgetCollectionCursor? before, WidgetCollectionCursor? after,
        WidgetCollectionItemKey? anchor, string? focus, WidgetResourceError? error)
    {
        if (_snapshot.Status == status && ReferenceEquals(_snapshot.Items, items) &&
            _snapshot.Before == before && _snapshot.After == after && _snapshot.Anchor == anchor &&
            _snapshot.RequestedFocusId == focus && Equals(_snapshot.Error, error)) return false;
        _snapshot = new(status, items, before, after, anchor, focus, error, _snapshot.Revision + 1);
        return true;
    }
    private static WidgetOperationHandle Completed() => new(WidgetOperationAdmission.Completed,
        Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));
}

public abstract partial class Widget
{
    protected WidgetCursorResource<TItem> CreateCursorResource<TItem>(string operationKey,
        WidgetCursorResourceOptions<TItem> options) where TItem : notnull =>
        new(operationKey, options, Operations, () =>
        {
            if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
        });
}
