namespace WidgetRail.OverlayFrontend.WinUI.Collections;

/// <summary>One owned, bounded successor. Commit transfers its range lifetimes;
/// cancellation or rejected admission releases them without changing the view.</summary>
internal sealed class PreparedIndexedWindow<T>(IReadOnlyList<IndexedRangeResult<T>> ranges) : IAsyncDisposable where T : notnull
{
    private IReadOnlyList<IndexedRangeResult<T>>? owned = ranges;
    internal IReadOnlyList<IndexedRangeResult<T>> Ranges => owned ?? throw new InvalidOperationException("Prepared window was consumed.");

    internal void Validate(IndexedQueryIdentity query, long revision, IReadOnlyList<string> keys, int pageSize)
    {
        var values = Ranges;
        if (keys.Count == 0 || keys.Count > WidgetProtocol.IndexedCollectionLimits.MaximumContinuityItems ||
            values.Count != (keys.Count + pageSize - 1) / pageSize)
            throw new InvalidDataException("Prepared window has incomplete bounded coverage.");
        for (var page = 0; page < values.Count; page++)
        {
            var range = values[page];
            var start = page * pageSize;
            if (range.Query != query || range.ContentRevision != revision || range.RequestId <= 0 ||
                range.StartIndex != start || range.Items.Count != Math.Min(pageSize, keys.Count - start))
                throw new InvalidDataException("Prepared window has different range authority.");
            for (var index = 0; index < range.Items.Count; index++)
                if (range.Items[index] is not { Value: not null } item || item.Key != keys[start + index])
                    throw new InvalidDataException("Prepared window changed an occurrence identity.");
        }
    }

    internal IReadOnlyList<IndexedRangeResult<T>> Take()
    {
        var result = Ranges;
        owned = null;
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        var previous = Interlocked.Exchange(ref owned, null);
        if (previous is null) return;
        // Start every release even if one owner fails to release.
        await Task.WhenAll(previous.Where(range => range.Lifetime is not null)
            .Select(range => ReleaseAsync(range.Lifetime!))).ConfigureAwait(false);
        static async Task ReleaseAsync(IAsyncDisposable lifetime) => await lifetime.DisposeAsync().ConfigureAwait(false);
    }
}
