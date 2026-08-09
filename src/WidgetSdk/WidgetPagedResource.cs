using System.Collections.ObjectModel;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

/// <summary>Observable loading state for one bounded page window.</summary>
public enum WidgetPagedResourceStatus
{
    NotLoaded,
    Loading,
    Ready,
    Refreshing,
    LoadingAdjacent,
    Error,
}

/// <summary>Direction requested by a focus-edge pagination action.</summary>
public enum WidgetPageDirection
{
    Previous,
    Next,
}

/// <summary>A safe, bounded error that widget UI may display.</summary>
public sealed record WidgetResourceError
{
    public const int MaximumMessageLength = 256;

    public WidgetResourceError(string code, string message)
    {
        StableIdentifier.Validate(code, nameof(code));
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (message.Length > MaximumMessageLength || message.Any(char.IsControl))
            throw new ArgumentException(
                $"Resource error messages must contain at most {MaximumMessageLength} visible characters.",
                nameof(message));
        Code = code;
        Message = message;
    }

    public string Code { get; }
    public string Message { get; }

    public static WidgetResourceError Unexpected { get; } =
        new("resource_error", "This content could not be loaded. Try again.");

    public static WidgetResourceError InvalidPage { get; } =
        new("invalid_page", "This page returned invalid data. Try again.");
}

/// <summary>An immutable offset-based page returned by widget-authored loading code.</summary>
public sealed record WidgetPage<TItem>(
    IReadOnlyList<TItem> Items,
    int Offset,
    int Limit,
    int Total) where TItem : notnull;

/// <summary>
/// Maps a responsive scroll surface to stable focus IDs. The item index is the
/// absolute collection index, not its position within the current page.
/// </summary>
public sealed record WidgetPagedViewport<TItem>(
    string ScrollId,
    Func<TItem, int, string> ItemFocusId,
    string? EmptyFocusId = null) where TItem : notnull;

/// <summary>Construction policy for a bounded paged resource.</summary>
public sealed record WidgetPagedResourceOptions<TItem> where TItem : notnull
{
    public required int PageSize { get; init; }
    public required int MaximumCachedPages { get; init; }
    public int MaximumCachedItems { get; init; } = 256;
    public required Func<int, int, CancellationToken, ValueTask<WidgetPage<TItem>>> LoadPage { get; init; }
    public required Func<Exception, WidgetResourceError> MapError { get; init; }
    public required IReadOnlyList<WidgetPagedViewport<TItem>> Viewports { get; init; }
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(5);
    public WidgetOperationLifetime Lifetime { get; init; } = WidgetOperationLifetime.Active;
    public int PaginationThreshold { get; init; } = 1;
    public bool RetainLastGoodPage { get; init; } = true;
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>Immutable render-facing snapshot of one paged resource.</summary>
public sealed record WidgetPagedResourceSnapshot<TItem>(
    WidgetPagedResourceStatus Status,
    WidgetPage<TItem>? Page,
    WidgetResourceError? Error,
    string? RequestedFocusId,
    long Revision) where TItem : notnull
{
    public bool HasValue => Page is not null;
    public bool HasPrevious => Page is { Offset: > 0 };
    public bool HasNext => Page is { } page && page.Offset + page.Limit < page.Total;
}

/// <summary>
/// Runtime-integrated, offset-based page window with deterministic bounded LRU
/// caching. Provider calls happen only when an author invokes a load method or
/// routes an explicit host pagination action through <see cref="TryHandlePagination"/>.
/// </summary>
public sealed class WidgetPagedResource<TItem> where TItem : notnull
{
    public const int MaximumPageSize = 100;
    public const int MaximumCachePages = 8;
    public const int MaximumCacheItems = 512;

    private enum RequestKind { Initial, Refresh, Adjacent }

    private sealed record LoadIntent(
        int Offset,
        RequestKind Kind,
        WidgetPageDirection? Direction,
        string? SourceScrollId,
        bool BypassCache);

    private sealed class LoadRequest(
        LoadIntent intent,
        long epoch,
        WidgetPagedResourceSnapshot<TItem> before)
    {
        internal LoadIntent Intent { get; set; } = intent;
        internal long Epoch { get; } = epoch;
        internal WidgetPagedResourceSnapshot<TItem> Before { get; } = before;
        internal TaskCompletionSource<WidgetOperationResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CacheEntry(
        WidgetPage<TItem> page,
        DateTimeOffset loadedAt,
        LinkedListNode<int> node)
    {
        internal WidgetPage<TItem> Page { get; } = page;
        internal DateTimeOffset LoadedAt { get; } = loadedAt;
        internal LinkedListNode<int> Node { get; } = node;
    }

    private readonly object _gate = new();
    private readonly object _admissionGate = new();
    private readonly string _operationKey;
    private readonly string _previousActionId;
    private readonly string _nextActionId;
    private readonly WidgetPagedResourceOptions<TItem> _options;
    private readonly IReadOnlyDictionary<string, WidgetPagedViewport<TItem>> _viewports;
    private readonly WidgetOperations _operations;
    private readonly Action _invalidate;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<int, CacheEntry> _cache = [];
    private readonly LinkedList<int> _lru = [];
    private WidgetPagedResourceSnapshot<TItem> _snapshot =
        new(WidgetPagedResourceStatus.NotLoaded, null, null, null, 0);
    private LoadRequest? _currentRequest;
    private LoadIntent? _lastFailedIntent;
    private long _epoch;
    private int _cachedItemCount;

    internal WidgetPagedResource(
        string operationKey,
        WidgetPagedResourceOptions<TItem> options,
        WidgetOperations operations,
        Action invalidate)
    {
        StableIdentifier.Validate(operationKey, nameof(operationKey));
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.LoadPage);
        ArgumentNullException.ThrowIfNull(options.MapError);
        ArgumentNullException.ThrowIfNull(options.Viewports);
        if (options.PageSize is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(options.PageSize));
        if (options.MaximumCachedPages is < 1 or > MaximumCachePages)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumCachedPages));
        if (options.MaximumCachedItems is < 1 or > MaximumCacheItems ||
            options.MaximumCachedItems < options.PageSize)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumCachedItems));
        if (options.CacheDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.CacheDuration));
        if (!Enum.IsDefined(options.Lifetime))
            throw new ArgumentOutOfRangeException(nameof(options.Lifetime));
        if (options.PaginationThreshold is < 1 or >
            ProtocolConstants.MaximumScrollPaginationThreshold)
            throw new ArgumentOutOfRangeException(nameof(options.PaginationThreshold));

        var viewports = new Dictionary<string, WidgetPagedViewport<TItem>>(StringComparer.Ordinal);
        foreach (var viewport in options.Viewports)
        {
            ArgumentNullException.ThrowIfNull(viewport);
            StableIdentifier.Validate(viewport.ScrollId, nameof(options.Viewports));
            ArgumentNullException.ThrowIfNull(viewport.ItemFocusId);
            if (viewport.EmptyFocusId is not null)
                StableIdentifier.Validate(viewport.EmptyFocusId, nameof(options.Viewports));
            if (!viewports.TryAdd(viewport.ScrollId, viewport))
                throw new ArgumentException("Paged viewport scroll IDs must be unique.",
                    nameof(options.Viewports));
        }
        if (viewports.Count == 0)
            throw new ArgumentException("At least one paged viewport is required.",
                nameof(options.Viewports));

        _operationKey = operationKey;
        _previousActionId = operationKey + ".page.previous";
        _nextActionId = operationKey + ".page.next";
        StableIdentifier.Validate(_previousActionId, nameof(operationKey));
        StableIdentifier.Validate(_nextActionId, nameof(operationKey));
        _options = options;
        _viewports = new ReadOnlyDictionary<string, WidgetPagedViewport<TItem>>(viewports);
        _operations = operations;
        _invalidate = invalidate;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;
    }

    public WidgetPagedResourceSnapshot<TItem> Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public bool IsBusy => _operations.IsBusy(_operationKey);
    public int CachedPageCount { get { lock (_gate) return _cache.Count; } }
    public int CachedItemCount { get { lock (_gate) return _cachedItemCount; } }

    public WidgetOperationHandle EnsureLoaded(bool forceRefresh = false)
    {
        lock (_admissionGate)
        {
            lock (_gate)
            {
                if (!forceRefresh && _snapshot.Status == WidgetPagedResourceStatus.Ready &&
                    _snapshot.Page is { } page &&
                    TryGetFreshCachedPageLocked(page.Offset, out _))
                    return Completed();
            }
            return Start(new(0, forceRefresh ? RequestKind.Refresh : RequestKind.Initial,
                null, null, forceRefresh));
        }
    }

    public WidgetOperationHandle Refresh()
    {
        lock (_admissionGate)
        {
            int offset;
            lock (_gate) offset = _snapshot.Page?.Offset ?? 0;
            return Start(new(offset, RequestKind.Refresh, null, null, true));
        }
    }

    public WidgetOperationHandle Move(
        WidgetPageDirection direction,
        string sourceScrollId)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        StableIdentifier.Validate(sourceScrollId, nameof(sourceScrollId));
        lock (_admissionGate)
        {
            LoadIntent intent;
            lock (_gate)
            {
                if (!_viewports.ContainsKey(sourceScrollId))
                    throw new ArgumentException("The source is not a configured paged viewport.",
                        nameof(sourceScrollId));
                if (_snapshot.Page is not { } page) return Completed();
                var target = direction == WidgetPageDirection.Next
                    ? page.Offset + page.Limit
                    : Math.Max(0, page.Offset - _options.PageSize);
                if (target == page.Offset ||
                    direction == WidgetPageDirection.Next && !_snapshot.HasNext)
                    return Completed();
                intent = new(target, RequestKind.Adjacent, direction,
                    sourceScrollId, false);
            }
            return Start(intent);
        }
    }

    public WidgetOperationHandle Retry()
    {
        lock (_admissionGate)
        {
            LoadIntent? intent;
            lock (_gate) intent = _lastFailedIntent;
            return intent is null ? Refresh() : Start(intent with { BypassCache = true });
        }
    }

    public bool TryHandlePagination(
        WidgetActionEvent action,
        out WidgetOperationHandle operation)
    {
        ArgumentNullException.ThrowIfNull(action);
        var direction = action.ActionId == _previousActionId
            ? WidgetPageDirection.Previous
            : action.ActionId == _nextActionId
                ? WidgetPageDirection.Next
                : (WidgetPageDirection?)null;
        if (direction is null || !_viewports.ContainsKey(action.SourceElementId))
        {
            operation = default;
            return false;
        }
        operation = Move(direction.Value, action.SourceElementId);
        return true;
    }

    public ScrollElement Paginate(ScrollElement scroll)
    {
        ArgumentNullException.ThrowIfNull(scroll);
        if (!_viewports.ContainsKey(scroll.Id))
            throw new ArgumentException("The scroll is not a configured paged viewport.",
                nameof(scroll));
        if (scroll.NearStartActionId is not null || scroll.NearEndActionId is not null)
            throw new ArgumentException(
                "The scroll already has focus-edge pagination actions.", nameof(scroll));
        var snapshot = Snapshot;
        if (snapshot.Status == WidgetPagedResourceStatus.Error) return scroll;
        var previous = snapshot.HasPrevious ? _previousActionId : null;
        var next = snapshot.HasNext ? _nextActionId : null;
        return previous is null && next is null
            ? scroll
            : scroll.Paginate(previous, next, _options.PaginationThreshold);
    }

    public bool TryGetCurrentItem(int absoluteIndex, out TItem item)
    {
        lock (_gate)
        {
            if (_snapshot.Page is { } page)
            {
                var local = absoluteIndex - page.Offset;
                if (local >= 0 && local < page.Items.Count)
                {
                    item = page.Items[local];
                    return true;
                }
            }
        }
        item = default!;
        return false;
    }

    /// <summary>
    /// Cancels work and clears page/cache state. Pass <paramref name="invalidate"/>
    /// as false only when the owning widget immediately publishes one composed
    /// route/state invalidation.
    /// </summary>
    public void Reset(bool invalidate = true)
    {
        bool changed;
        lock (_admissionGate)
        {
            lock (_gate)
            {
                _epoch++;
                _currentRequest = null;
                _lastFailedIntent = null;
                _cache.Clear();
                _lru.Clear();
                _cachedItemCount = 0;
                changed = SetSnapshotLocked(
                    WidgetPagedResourceStatus.NotLoaded, null, null, null);
            }
            _operations.Cancel(_operationKey);
        }
        if (changed && invalidate) _invalidate();
    }

    /// <summary>
    /// Acknowledges an entering-edge focus request without discarding cached pages.
    /// </summary>
    public void ClearRequestedFocus(bool invalidate = true)
    {
        bool changed;
        lock (_gate)
            changed = SetSnapshotLocked(
                _snapshot.Status, _snapshot.Page, _snapshot.Error, null);
        if (changed && invalidate) _invalidate();
    }

    public Task WhenIdleAsync(CancellationToken cancellationToken = default) =>
        _operations.WhenIdleAsync(_operationKey, cancellationToken);

    private WidgetOperationHandle Start(LoadIntent intent)
    {
        lock (_admissionGate)
        {
            LoadRequest? request = null;
            var cacheHit = false;
            var changed = false;
            lock (_gate)
            {
                if (_currentRequest is { } duplicate && duplicate.Intent == intent)
                    return new(WidgetOperationAdmission.Joined, duplicate.Completion.Task);

                if (!intent.BypassCache &&
                    TryGetFreshCachedPageLocked(intent.Offset, out var cached))
                {
                    var focus = ResolveFocusLocked(cached, intent);
                    changed = SetSnapshotLocked(
                        WidgetPagedResourceStatus.Ready, cached, null, focus);
                    _lastFailedIntent = null;
                    _currentRequest = null;
                    cacheHit = true;
                }
                else
                {
                    var before = _snapshot;
                    request = new LoadRequest(intent, _epoch, before);
                    _currentRequest = request;
                    var status = before.Page is null
                        ? WidgetPagedResourceStatus.Loading
                        : intent.Kind == RequestKind.Adjacent
                            ? WidgetPagedResourceStatus.LoadingAdjacent
                            : WidgetPagedResourceStatus.Refreshing;
                    changed = SetSnapshotLocked(status, before.Page, null,
                        before.RequestedFocusId);
                }
            }

            if (cacheHit)
            {
                if (changed) _invalidate();
                _operations.Cancel(_operationKey);
                return Completed();
            }

            var underlying = _operations.RunLatest(_operationKey,
                context => LoadCoreAsync(request!, context), _options.Lifetime);
            if (changed && underlying.IsAccepted &&
                underlying.Admission != WidgetOperationAdmission.Started)
                _invalidate();
            ObserveCompletion(request!, underlying);
            return new(underlying.Admission, request!.Completion.Task);
        }
    }

    private async ValueTask LoadCoreAsync(
        LoadRequest request,
        WidgetOperationContext context)
    {
        var committed = false;
        try
        {
            var loaded = await _options.LoadPage(
                request.Intent.Offset, _options.PageSize, context.CancellationToken)
                .ConfigureAwait(false);
            var page = NormalizePage(loaded, request.Intent.Offset);
            string? focus;
            lock (_gate)
            {
                if (!CanCommitLocked(request, context)) return;
                AddCacheLocked(page);
                focus = ResolveFocusLocked(page, request.Intent);
                _lastFailedIntent = null;
                _currentRequest = null;
                SetSnapshotLocked(
                    WidgetPagedResourceStatus.Ready, page, null, focus);
                committed = true;
            }
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (!CanCommitLocked(request, context)) throw;
                var error = exception is InvalidPageException
                    ? WidgetResourceError.InvalidPage
                    : MapError(exception);
                _lastFailedIntent = request.Intent;
                _currentRequest = null;
                var page = _options.RetainLastGoodPage ? request.Before.Page : null;
                SetSnapshotLocked(
                    WidgetPagedResourceStatus.Error, page, error,
                    request.Before.RequestedFocusId);
                committed = true;
            }
            throw;
        }
        finally
        {
            if (!committed) RestoreCanceledRequest(request, invalidate: false);
        }
    }

    private void ObserveCompletion(
        LoadRequest request,
        WidgetOperationHandle underlying)
    {
        underlying.Completion.GetAwaiter().OnCompleted(() =>
        {
            var result = underlying.Completion.GetAwaiter().GetResult();
            RestoreCanceledRequest(request, invalidate: !underlying.IsAccepted);
            request.Completion.TrySetResult(result);
        });
    }

    private void RestoreCanceledRequest(LoadRequest request, bool invalidate)
    {
        bool changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_currentRequest, request) && request.Epoch == _epoch)
            {
                _currentRequest = null;
                changed = SetSnapshotLocked(
                    request.Before.Status, request.Before.Page, request.Before.Error,
                    request.Before.RequestedFocusId);
            }
        }
        if (changed && invalidate) _invalidate();
    }

    private bool CanCommitLocked(LoadRequest request, WidgetOperationContext context) =>
        request.Epoch == _epoch && ReferenceEquals(_currentRequest, request) && context.IsCurrent;

    private WidgetPage<TItem> NormalizePage(WidgetPage<TItem>? page, int requestedOffset)
    {
        if (page is null || page.Items is null || page.Offset != requestedOffset ||
            page.Offset < 0 || page.Limit is < 1 or > MaximumPageSize ||
            page.Limit != _options.PageSize || page.Items.Count > page.Limit ||
            page.Total < 0 || page.Total < page.Offset + page.Items.Count ||
            page.Items.Any(item => item is null) ||
            page.Offset > page.Total)
            throw new InvalidPageException();
        var immutable = new ReadOnlyCollection<TItem>(page.Items.ToArray());
        return new(immutable, page.Offset, page.Limit, page.Total);
    }

    private WidgetResourceError MapError(Exception exception)
    {
        try { return _options.MapError(exception) ?? WidgetResourceError.Unexpected; }
        catch { return WidgetResourceError.Unexpected; }
    }

    private string? ResolveFocusLocked(WidgetPage<TItem> page, LoadIntent intent)
    {
        if (intent.Kind != RequestKind.Adjacent || intent.SourceScrollId is null)
            return null;
        var viewport = _viewports[intent.SourceScrollId];
        if (page.Items.Count == 0) return viewport.EmptyFocusId;
        var local = intent.Direction == WidgetPageDirection.Previous
            ? page.Items.Count - 1
            : 0;
        var id = viewport.ItemFocusId(page.Items[local], page.Offset + local);
        StableIdentifier.Validate(id, nameof(viewport.ItemFocusId));
        return id;
    }

    private void AddCacheLocked(WidgetPage<TItem> page)
    {
        if (_cache.Remove(page.Offset, out var replaced))
        {
            _lru.Remove(replaced.Node);
            _cachedItemCount -= replaced.Page.Items.Count;
        }
        var node = _lru.AddLast(page.Offset);
        _cache.Add(page.Offset, new(page, _timeProvider.GetUtcNow(), node));
        _cachedItemCount += page.Items.Count;
        while (_cache.Count > _options.MaximumCachedPages ||
               _cachedItemCount > _options.MaximumCachedItems)
        {
            var oldest = _lru.First!;
            _lru.RemoveFirst();
            var removed = _cache[oldest.Value];
            _cache.Remove(oldest.Value);
            _cachedItemCount -= removed.Page.Items.Count;
        }
    }

    private bool TryGetFreshCachedPageLocked(int offset, out WidgetPage<TItem> page)
    {
        if (_options.CacheDuration > TimeSpan.Zero &&
            _cache.TryGetValue(offset, out var entry) &&
            (_options.CacheDuration == TimeSpan.MaxValue ||
             _timeProvider.GetUtcNow() - entry.LoadedAt < _options.CacheDuration))
        {
            _lru.Remove(entry.Node);
            _lru.AddLast(entry.Node);
            page = entry.Page;
            return true;
        }
        page = null!;
        return false;
    }

    private bool SetSnapshotLocked(
        WidgetPagedResourceStatus status,
        WidgetPage<TItem>? page,
        WidgetResourceError? error,
        string? focus)
    {
        if (_snapshot.Status == status && ReferenceEquals(_snapshot.Page, page) &&
            Equals(_snapshot.Error, error) && _snapshot.RequestedFocusId == focus)
            return false;
        _snapshot = new(status, page, error, focus, _snapshot.Revision + 1);
        return true;
    }

    private static WidgetOperationHandle Completed() => new(
        WidgetOperationAdmission.Completed,
        Task.FromResult(new WidgetOperationResult(WidgetOperationStatus.Succeeded)));

    private sealed class InvalidPageException : InvalidOperationException { }
}

public abstract partial class Widget
{
    /// <summary>Creates one runtime-integrated bounded page resource.</summary>
    protected WidgetPagedResource<TItem> CreatePagedResource<TItem>(
        string operationKey,
        WidgetPagedResourceOptions<TItem> options) where TItem : notnull =>
        new(operationKey, options, Operations, () =>
        {
            if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();
        });
}
