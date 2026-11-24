namespace WidgetRail.WidgetSdk;

public sealed partial class WidgetCursorResource<TItem> where TItem : notnull
{
    // Separate from the admitted window: fetching never changes rows, focus,
    // cursors, loading status or protected-page eviction. Two finite slots only.
    private sealed record PreparedPage(WidgetCollectionCursor Cursor, WidgetCursorDirection Direction,
        long Epoch, long Window, long Mutations, long Boundaries, CancellationToken Lifetime)
    {
        internal TaskCompletionSource<WidgetCursorPage<TItem>?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly Dictionary<WidgetCursorDirection, PreparedPage> preparedPages = [];
    private string PrepareKey(WidgetCursorDirection direction) => _operationKey +
        (direction == WidgetCursorDirection.Before ? ".prepare.before" : ".prepare.after");

    private bool IsPreparedCurrent(PreparedPage page) => page.Epoch == _epoch &&
        page.Window == _snapshot.WindowGeneration && page.Mutations == _liveRevision &&
        page.Boundaries == _liveBoundaryRevision && !page.Lifetime.IsCancellationRequested &&
        page.Lifetime == _mutationLifetime() &&
        page.Cursor == (page.Direction == WidgetCursorDirection.Before ? _snapshot.Before : _snapshot.After);

    private void ClearPreparedPagesLocked() => preparedPages.Clear();
    private void RememberPreparedPage(Request request, WidgetCursorPage<TItem> page)
    {
        if (request.Intent is not { Direction: { } direction, Cursor: { } cursor } ||
            request.MutationRevision != _liveRevision || request.BoundaryRevision != _liveBoundaryRevision) return;
        var prepared = new PreparedPage(cursor, direction, _epoch, _snapshot.WindowGeneration,
            _liveRevision, _liveBoundaryRevision, _mutationLifetime());
        prepared.Completion.TrySetResult(page);
        preparedPages[direction] = prepared;
    }
    private void CancelPreparedOperations()
    {
        if (!_options.PrepareAdjacentPages) return;
        _operations.Cancel(PrepareKey(WidgetCursorDirection.Before));
        _operations.Cancel(PrepareKey(WidgetCursorDirection.After));
    }

    private void PrepareAdjacentPages()
    {
        if (!_options.PrepareAdjacentPages) return;
        lock (_admissionGate)
        {
            foreach (var direction in new[] { WidgetCursorDirection.Before, WidgetCursorDirection.After })
            {
                PreparedPage? page;
                lock (_gate)
                {
                    if (_current is not null || _snapshot.Status != WidgetPagedResourceStatus.Ready) return;
                    var cursor = direction == WidgetCursorDirection.Before ? _snapshot.Before : _snapshot.After;
                    if (preparedPages.TryGetValue(direction, out var existing) && IsPreparedCurrent(existing)) continue;
                    preparedPages.Remove(direction);
                    page = cursor is null ? null : new(cursor.Value, direction, _epoch, _snapshot.WindowGeneration,
                        _liveRevision, _liveBoundaryRevision, _mutationLifetime());
                    if (page is not null) preparedPages[direction] = page;
                }
                if (page is null) { _operations.Cancel(PrepareKey(direction)); continue; }
                var handle = _operations.RunLatest(PrepareKey(direction), async context =>
                {
                    WidgetCursorPage<TItem>? loaded = null;
                    try
                    {
                        var result = await _options.LoadPage(page.Cursor, direction, _options.PageSize,
                            context.CancellationToken).ConfigureAwait(false);
                        loaded = NormalizePreparedPage(page, result, context);
                    }
                    // Speculation never turns a readable window into an error.
                    // A real demand retries through the normal observable path.
                    catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
                    finally { page.Completion.TrySetResult(loaded); }
                }, _options.Lifetime);
                // Rejected/canceled-before-start operations must also release a waiter.
                handle.Completion.GetAwaiter().OnCompleted(() => page.Completion.TrySetResult(null));
            }
        }
    }

    // Validation may throw for malformed provider pages. Release the monitor
    // in a synchronous frame before the speculative operation handles failure.
    private WidgetCursorPage<TItem>? NormalizePreparedPage(PreparedPage page, WidgetCursorPage<TItem> result,
        WidgetOperationContext context)
    {
        lock (_gate)
            return context.IsCurrent && IsPreparedCurrent(page) ? Normalize(result) : null;
    }

    private async ValueTask<WidgetCursorPage<TItem>> LoadPreparedOrFreshAsync(Request request, CancellationToken token)
    {
        PreparedPage? prepared = null;
        lock (_gate)
            if (request.Intent.Direction is { } direction && preparedPages.TryGetValue(direction, out var candidate) &&
                IsPreparedCurrent(candidate) && candidate.Cursor == request.Intent.Cursor) prepared = candidate;
        if (prepared is not null)
        {
            var page = await prepared.Completion.Task.WaitAsync(token).ConfigureAwait(false);
            lock (_gate)
                if (page is not null && IsPreparedCurrent(prepared)) return page;
        }
        token.ThrowIfCancellationRequested();
        return await _options.LoadPage(request.Intent.Cursor, request.Intent.Direction, _options.PageSize, token).ConfigureAwait(false);
    }
}
