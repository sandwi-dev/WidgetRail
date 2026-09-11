using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

public enum WidgetCursorDirection { Before, After }

public sealed record WidgetCursorPage<TItem>(
    IReadOnlyList<TItem> Items,
    WidgetCollectionCursor? Before,
    WidgetCollectionCursor? After) where TItem : notnull
{
    /// <summary>Zero-based logical index of the first item, when known.</summary>
    public long? FirstItemIndex { get; init; }
    /// <summary>Total logical collection size, or null for an unknown extent.</summary>
    public long? TotalItemCount { get; init; }
}

public sealed record WidgetCursorViewport<TItem>(
    string ScrollId,
    Func<TItem, WidgetCollectionItemKey> ItemKey,
    Func<TItem, string> ItemFocusId,
    string? EmptyFocusId = null) where TItem : notnull
{
    /// <summary>
    /// Opts this viewport into protocol-v19 virtual-window projection. The
    /// host measures admitted items and uses this bounded estimate only for
    /// logical items outside the admitted window.
    /// </summary>
    public double? EstimatedItemExtent { get; init; }
}

public sealed record WidgetCursorResourceOptions<TItem> where TItem : notnull
{
    public required int PageSize { get; init; }
    public int MaximumRetainedItems { get; init; } = 256;
    /// <summary>Optional eviction target. Visible pages may exceed it, up to MaximumRetainedItems.</summary>
    public int? RetainedItemTarget { get; init; }
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
    public long? FirstItemIndex { get; init; }
    public long? TotalItemCount { get; init; }
    public long WindowGeneration { get; init; }
    /// <summary>Stable relative position of the retained window, including opaque-cursor providers.</summary>
    public long StartIndex { get; init; }
    public CollectionNavigationRequest? NavigationRequest { get; init; }
    public WidgetCursorDirection? LoadingDirection { get; init; }
    public CollectionLoadingState LoadingState => Status switch
    {
        WidgetPagedResourceStatus.Loading or WidgetPagedResourceStatus.Refreshing => CollectionLoadingState.After,
        WidgetPagedResourceStatus.LoadingAdjacent => LoadingDirection == WidgetCursorDirection.Before
            ? CollectionLoadingState.Before : CollectionLoadingState.After,
        _ => CollectionLoadingState.Idle,
    };
    public VirtualCollectionWindowChange WindowChange { get; init; }
}

/// <summary>One immutable collection revision used to construct both items and viewport metadata.</summary>
public sealed class WidgetCursorPresentation<TItem> where TItem : notnull
{
    private readonly Func<ScrollElement, ScrollElement> _present;
    private readonly Func<TItem, WidgetElement, WidgetElement> _presentItem;
    internal WidgetCursorPresentation(WidgetCursorResourceSnapshot<TItem> snapshot,
        Func<ScrollElement, ScrollElement> present,
        Func<TItem, WidgetElement, WidgetElement> presentItem)
    { Snapshot = snapshot; _present = present; _presentItem = presentItem; }
    public WidgetCursorResourceSnapshot<TItem> Snapshot { get; }
    public ScrollElement Present(ScrollElement scroll) => _present(scroll);
    public WidgetElement PresentItem(TItem item, WidgetElement element) => _presentItem(item, element);
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
    public const int MaximumCursorHistory = 256;

    private sealed record Intent(
        WidgetCollectionCursor? Cursor,
        WidgetCursorDirection? Direction,
        string? SourceScrollId,
        bool Refresh, bool MoveFocus = false, string? OriginFocusId = null, IReadOnlySet<string>? ProtectedKeys = null);
    private sealed record Segment(
        WidgetCursorPage<TItem> Page,
        WidgetCollectionCursor? RequestCursor, long StartIndex);

    private sealed class Request(Intent intent, long epoch,
        WidgetCursorResourceSnapshot<TItem> before, long focusIntentRevision)
    {
        internal Intent Intent { get; } = intent;
        internal long FocusIntentRevision { get; } = focusIntentRevision;
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
    private readonly bool _usesVirtualCollectionWindows;
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
    private long _windowGeneration;
    private long _navigationRequestId;
    private long _focusIntentRevision;

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
        if (options.RetainedItemTarget is { } target &&
            (target < options.PageSize * 2 || target > options.MaximumRetainedItems))
            throw new ArgumentOutOfRangeException(nameof(options.RetainedItemTarget));
        if (options.PaginationThreshold is < 1 or >
            WidgetRail.WidgetProtocol.ProtocolConstants.MaximumScrollPaginationThreshold)
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
            if (viewport.EstimatedItemExtent is { } estimate &&
                (!double.IsFinite(estimate) ||
                 estimate < WidgetRail.WidgetProtocol.ProtocolConstants.MinimumVirtualCollectionItemExtent ||
                 estimate > WidgetRail.WidgetProtocol.ProtocolConstants.MaximumVirtualCollectionItemExtent))
                throw new ArgumentOutOfRangeException(nameof(options.Viewports));
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
        _usesVirtualCollectionWindows = viewports.Values.Any(
            viewport => viewport.EstimatedItemExtent is not null);
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

    /// <summary>Loads adjacent data without requesting a focus move.</summary>
    public WidgetOperationHandle Prefetch(WidgetCursorDirection direction, string sourceScrollId) =>
        MoveCore(direction, sourceScrollId, false);

    public WidgetOperationHandle Move(WidgetCursorDirection direction, string sourceScrollId) =>
        MoveCore(direction, sourceScrollId, true);

    public WidgetOperationHandle Move(WidgetCursorDirection direction, string sourceScrollId, WidgetActionEvent action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return MoveCore(direction, sourceScrollId, true, action.FocusedElementId ?? action.SourceElementId);
    }

    private WidgetOperationHandle MoveCore(WidgetCursorDirection direction, string sourceScrollId, bool moveFocus, string? originFocusId = null, IReadOnlySet<string>? protectedKeys = null)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        StableIdentifier.Validate(sourceScrollId, nameof(sourceScrollId));
        WidgetCollectionCursor? cursor;
        lock (_admissionGate)
        {
            lock (_gate)
            {
                if (!_viewports.ContainsKey(sourceScrollId))
                    throw new ArgumentException("The source is not a configured cursor viewport.", nameof(sourceScrollId));
                // An action authored by the retained view must not replace the
                // refresh that is establishing its new cursor authority.
                if (_current is { Intent.Direction: null } refresh)
                    return new(WidgetOperationAdmission.Joined, refresh.Completion.Task);
                cursor = direction == WidgetCursorDirection.Before ? _snapshot.Before : _snapshot.After;
            }
            return cursor is null ? Completed() : Start(new(cursor, direction, sourceScrollId, false, moveFocus, originFocusId, protectedKeys));
        }
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
        IReadOnlySet<string>? protectedKeys = null;
        if (action.VisibleCollectionKeys is { } visible)
        {
            if (visible.Count > MaximumRetainedItems)
                throw new ArgumentException("Visible collection demand exceeds its bound.", nameof(action));
            foreach (var key in visible) StableIdentifier.Validate(key, nameof(action));
            protectedKeys = visible.ToHashSet(StringComparer.Ordinal);
        }
        operation = MoveCore(direction.Value, action.SourceElementId, false, protectedKeys: protectedKeys);
        return true;
    }

    public WidgetCursorPresentation<TItem> Capture()
    {
        var snapshot = Snapshot;
        return new(snapshot, scroll => PresentSnapshot(scroll, snapshot), PresentItem);
    }

    private ScrollElement PresentSnapshot(ScrollElement scroll, WidgetCursorResourceSnapshot<TItem> snapshot)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        if (!_viewports.ContainsKey(scroll.Id))
            throw new ArgumentException("The scroll is not a configured cursor viewport.", nameof(scroll));
        var before = snapshot.Status == WidgetPagedResourceStatus.Ready && snapshot.HasBefore
            ? _beforeActionId
            : null;
        var after = snapshot.Status == WidgetPagedResourceStatus.Ready && snapshot.HasAfter
            ? _afterActionId
            : null;
        var result = scroll with
        {
            NearStartActionId = before,
            NearEndActionId = after,
            PaginationThreshold = before is null && after is null ? null : _options.PaginationThreshold,
        };
        return result with
        {
            CollectionAnchorKey = snapshot.Anchor?.Value,
            CollectionStartIndex = snapshot.Items.Count == 0 ? null : snapshot.StartIndex,
            CollectionNavigation = snapshot.Items.Count == 0 ? null : snapshot.NavigationRequest,
            CollectionLoading = snapshot.LoadingState,
            VirtualCollectionWindow = _viewports[scroll.Id].EstimatedItemExtent is { } estimate &&
                snapshot.Items.Count != 0 && snapshot.WindowGeneration > 0
                ? new VirtualCollectionWindow
                {
                    RequestGeneration = snapshot.WindowGeneration,
                    Change = snapshot.WindowChange,
                    FirstItemIndex = snapshot.FirstItemIndex,
                    TotalItemCount = snapshot.TotalItemCount,
                    HasBefore = snapshot.Status != WidgetPagedResourceStatus.Error && snapshot.HasBefore,
                    HasAfter = snapshot.Status != WidgetPagedResourceStatus.Error && snapshot.HasAfter,
                    EstimatedItemExtent = estimate,
                }
                : null,
        };
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
            if (changed) ++_focusIntentRevision;
        }
        if (changed && invalidate) _invalidate();
    }

    public void ClearRequestedFocus(bool invalidate = true)
    {
        bool changed;
        lock (_gate)
        {
            ++_focusIntentRevision;
            changed = SetSnapshotLocked(_snapshot.Status, _snapshot.Items,
                _snapshot.Before, _snapshot.After, _snapshot.Anchor, null, _snapshot.Error);
            if (_snapshot.NavigationRequest is not null)
            {
                _snapshot = _snapshot with { NavigationRequest = null, Revision = _snapshot.Revision + 1 };
                changed = true;
            }
        }
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
                _snapshot = _snapshot with
                {
                    FirstItemIndex = null,
                    TotalItemCount = null,
                    WindowGeneration = 0,
                    StartIndex = 0,
                    NavigationRequest = null,
                    WindowChange = VirtualCollectionWindowChange.Replace,
                };
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    public Task WhenIdleAsync(CancellationToken token = default) =>
        _operations.WhenIdleAsync(_operationKey, token);

    private static bool SameIntent(Intent left, Intent right) =>
        (left with { ProtectedKeys = null }) == (right with { ProtectedKeys = null }) &&
        (left.ProtectedKeys is null ? right.ProtectedKeys is null || right.ProtectedKeys.Count == 0 :
            left.ProtectedKeys.SetEquals(right.ProtectedKeys ?? new HashSet<string>(StringComparer.Ordinal)));

    private WidgetOperationHandle Start(Intent intent)
    {
        lock (_admissionGate)
        {
            Request request;
            bool changed;
            lock (_gate)
            {
                if (_current is { } duplicate && SameIntent(duplicate.Intent, intent))
                    return new(WidgetOperationAdmission.Joined, duplicate.Completion.Task);
                if (intent.MoveFocus && _navigationRequestId >= ProtocolConstants.MaximumFocusGroupEntryRequestId)
                    throw new InvalidOperationException("Navigation request generation exhausted.");
                // Cancellation must restore settled data, never the loading
                // status of the operation that this request superseded.
                var before = _current?.Before ?? _snapshot;
                request = new(intent, _epoch, before, _focusIntentRevision);
                _current = request;
                var status = before.Items.Count == 0 ? WidgetPagedResourceStatus.Loading :
                    intent.Direction is null ? WidgetPagedResourceStatus.Refreshing :
                    WidgetPagedResourceStatus.LoadingAdjacent;
                changed = SetSnapshotLocked(status, before.Items, before.Before, before.After,
                    before.Anchor, null, null);
                _snapshot = _snapshot with { LoadingDirection = intent.Direction, NavigationRequest = intent.MoveFocus ? new()
                {
                    RequestId = ++_navigationRequestId, OriginFocusId = intent.OriginFocusId,
                } : null };
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
                if (_usesVirtualCollectionWindows && _windowGeneration >=
                    WidgetRail.WidgetProtocol.ProtocolConstants.MaximumVirtualCollectionRequestGeneration)
                    throw new InvalidOperationException("Virtual collection request generation is exhausted.");
                var proposed = Merge(page, request.Intent.Cursor, request.Intent.Direction, request.Intent.ProtectedKeys);
                var merged = new ReadOnlyCollection<TItem>(proposed.SelectMany(segment => segment.Page.Items).ToArray());
                var anchor = ResolveAnchor(_snapshot, merged, request.Intent.Direction);
                var focus = request.FocusIntentRevision == _focusIntentRevision
                    ? ResolveFocus(page.Items, request.Intent) : null;
                var beforeCursor = proposed.FirstOrDefault()?.Page.Before;
                var afterCursor = proposed.LastOrDefault()?.Page.After;
                // Commit only after every author callback and invariant has succeeded.
                _segments.Clear();
                foreach (var segment in proposed) _segments.AddLast(segment);
                CommitTraversalProgress(request.Intent, page);
                _current = null;
                _failed = null;
                SetSnapshotLocked(WidgetPagedResourceStatus.Ready, merged,
                    beforeCursor, afterCursor, anchor, focus, null);
                var firstIndex = _segments.First?.Value.Page.FirstItemIndex;
                var total = _segments.First?.Value.Page.TotalItemCount;
                var nextWindowGeneration = _usesVirtualCollectionWindows
                    ? _windowGeneration + 1
                    : 0;
                var windowChange = firstIndex is null
                    ? VirtualCollectionWindowChange.Replace
                    : request.Intent.Direction switch
                    {
                        WidgetCursorDirection.Before => VirtualCollectionWindowChange.Prepend,
                        WidgetCursorDirection.After => VirtualCollectionWindowChange.Append,
                        _ => VirtualCollectionWindowChange.Replace,
                    };
                if (_usesVirtualCollectionWindows)
                    _windowGeneration = nextWindowGeneration;
                _snapshot = _snapshot with
                {
                    FirstItemIndex = firstIndex,
                    StartIndex = proposed.FirstOrDefault()?.StartIndex ?? 0,
                    NavigationRequest = focus is not null && _snapshot.NavigationRequest is { } navigation ? navigation with { TargetFocusId = focus } : null,
                    TotalItemCount = total,
                    WindowGeneration = nextWindowGeneration,
                    WindowChange = windowChange,
                };
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
                    request.Before.Before, request.Before.After, _snapshot.Anchor,
                    _snapshot.RequestedFocusId, MapError(exception));
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
        if (page.FirstItemIndex is { } first &&
            (first < 0 ||
             first > WidgetRail.WidgetProtocol.ProtocolConstants.MaximumVirtualCollectionItems ||
             page.Items.Count >
                 WidgetRail.WidgetProtocol.ProtocolConstants.MaximumVirtualCollectionItems - first))
            throw new InvalidOperationException("Invalid cursor page extent.");
        if (page.TotalItemCount is { } total)
        {
            if (page.FirstItemIndex is not { } knownFirst || total < 1 ||
                total > WidgetRail.WidgetProtocol.ProtocolConstants.MaximumVirtualCollectionItems ||
                knownFirst > total || page.Items.Count > total - knownFirst ||
                _viewports.Values.Any(viewport =>
                    viewport.EstimatedItemExtent is { } estimate &&
                    total * estimate >
                        WidgetRail.WidgetProtocol.ProtocolConstants.MaximumVirtualCollectionExtent))
                throw new InvalidOperationException("Invalid cursor page extent.");
        }
        return new(new ReadOnlyCollection<TItem>(page.Items.ToArray()), page.Before, page.After)
        {
            FirstItemIndex = page.FirstItemIndex,
            TotalItemCount = page.TotalItemCount,
        };
    }

    private IReadOnlyList<Segment> Merge(WidgetCursorPage<TItem> page,
        WidgetCollectionCursor? requestCursor,
        WidgetCursorDirection? direction, IReadOnlySet<string>? protectedKeys)
    {
        var proposed = direction is null ? new List<Segment>() : _segments.ToList();
        var start = page.FirstItemIndex ?? (direction switch
        {
            WidgetCursorDirection.After when _segments.Last is { } last =>
                checked(last.Value.StartIndex + last.Value.Page.Items.Count),
            WidgetCursorDirection.Before when _segments.First is { } first =>
                checked(first.Value.StartIndex - page.Items.Count),
            _ => 0,
        });
        if (Math.Abs(start) > ProtocolConstants.MaximumVirtualCollectionItems)
            throw new InvalidOperationException("Cursor position exceeded its bound.");
        var incoming = new Segment(page, requestCursor, start);
        if (direction == WidgetCursorDirection.Before) proposed.Insert(0, incoming);
        else proposed.Add(incoming);
        ValidateLogicalWindow(proposed);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in proposed)
        foreach (var item in segment.Page.Items)
        {
            if (!keys.Add(KeyOf(item).Value))
                throw new InvalidOperationException("Duplicate collection item key.");
        }
        var count = proposed.Sum(segment => segment.Page.Items.Count);
        while (count > (_options.RetainedItemTarget ?? _options.MaximumRetainedItems) && proposed.Count > 1)
        {
            var removed = direction == WidgetCursorDirection.Before
                ? proposed[^1] : proposed[0];
            if (protectedKeys is not null && removed.Page.Items.Any(item => protectedKeys.Contains(KeyOf(item).Value)))
                break;
            if (direction == WidgetCursorDirection.Before) proposed.RemoveAt(proposed.Count - 1);
            else proposed.RemoveAt(0);
            count -= removed.Page.Items.Count;
        }
        if (count > _options.MaximumRetainedItems)
            throw new InvalidOperationException("Visible collection exceeds the configured retained capacity.");
        return proposed;
    }

    private static void ValidateLogicalWindow(IReadOnlyList<Segment> segments)
    {
        var indexed = segments.Where(segment => segment.Page.FirstItemIndex is not null).ToArray();
        if (indexed.Length != 0 && indexed.Length != segments.Count)
            throw new InvalidOperationException("Cursor page extent changed within one retained window.");
        for (var index = 1; index < indexed.Length; index++)
        {
            var prior = indexed[index - 1].Page;
            var page = indexed[index].Page;
            if (prior.FirstItemIndex + prior.Items.Count != page.FirstItemIndex)
                throw new InvalidOperationException("Cursor pages are not logically contiguous.");
        }
        var known = segments.Where(segment => segment.Page.TotalItemCount is not null).ToArray();
        if (known.Length != 0 && known.Length != segments.Count)
            throw new InvalidOperationException("Cursor page extent changed within one retained window.");
        if (known.Length == 0) return;
        var total = known[0].Page.TotalItemCount;
        if (known.Any(segment => segment.Page.TotalItemCount != total))
            throw new InvalidOperationException("Cursor page extent changed within one retained window.");
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
        if (!intent.MoveFocus || intent.Direction is null || intent.SourceScrollId is null) return null;
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
                    _snapshot.Anchor, _snapshot.RequestedFocusId, value.Error);
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
        var previous = _snapshot;
        _snapshot = new(status, items, before, after, anchor, focus, error, previous.Revision + 1)
        {
            FirstItemIndex = previous.FirstItemIndex,
            TotalItemCount = previous.TotalItemCount,
            WindowGeneration = previous.WindowGeneration,
            StartIndex = previous.StartIndex,
            NavigationRequest = previous.NavigationRequest,
            LoadingDirection = status == WidgetPagedResourceStatus.LoadingAdjacent ? previous.LoadingDirection : null,
            WindowChange = previous.WindowChange,
        };
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
