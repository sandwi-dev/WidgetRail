using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>
/// A frozen application query, before widget-local title/fixed-row projection.
/// Holding this value retains lightweight data, never rendered rows or image bytes.
/// </summary>
internal sealed class PlayniteLibraryCapturedQuery
{
    private readonly Func<PlayniteBridgeGame, PlayniteLibraryItem> project;
    private readonly Func<PlayniteBridgeGame, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>> resolveArtwork;
    internal PlayniteLibraryCapturedQuery(WidgetAppLibraryQuery query, PlayniteLibraryQueryContext context,
        IReadOnlyList<PlayniteBridgeGame> games, PlayniteLibraryAuthorityProjection authority,
        IReadOnlyList<WidgetAppLibrarySource> sources, string revision, long retrievedAt, bool stale,
        Func<PlayniteBridgeGame, PlayniteLibraryItem> project,
        Func<PlayniteBridgeGame, WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>> resolveArtwork)
    {
        Query = query; Context = context; Games = games; Authority = authority;
        Sources = sources; CatalogRevision = revision; RetrievedAt = retrievedAt; RetainedLastGood = stale;
        this.project = project; this.resolveArtwork = resolveArtwork;
    }

    internal WidgetAppLibraryQuery Query { get; }
    internal PlayniteLibraryQueryContext Context { get; }
    internal IReadOnlyList<PlayniteBridgeGame> Games { get; }
    internal int Count => Games.Count;
    internal PlayniteLibraryAuthorityProjection Authority { get; }
    internal IReadOnlyList<WidgetAppLibrarySource> Sources { get; }
    internal string CatalogRevision { get; }
    internal long RetrievedAt { get; }
    internal bool RetainedLastGood { get; }

    /// <summary>Random access to 1–64 rows. Empty queries never require a range read.</summary>
    internal IReadOnlyList<PlayniteLibraryCapturedItem> ReadRange(int start, int count, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (start < 0 || count is < 1 or > PlayniteBridgeClient.MaximumPageSize || start > Count - count)
            throw new ArgumentOutOfRangeException(nameof(start), "The range must fit the captured query and contain 1–64 items.");
        var result = new PlayniteLibraryCapturedItem[count];
        for (var i = 0; i < count; ++i)
        {
            token.ThrowIfCancellationRequested();
            result[i] = new(this, start + i, project(Games[start + i]));
        }
        return Array.AsReadOnly(result);
    }

    /// <summary>
    /// Only this query's row and its exact declared role/revision can resolve bytes.
    /// The Bridge has no historic-image endpoint: an uncached read uses that exact
    /// game's current image, without substituting another game or query revision.
    /// </summary>
    internal ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(PlayniteLibraryCapturedItem item,
        WidgetArtworkHandle handle, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(item.Query, this)) return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
        return resolveArtwork(Games[item.Index], handle, token);
    }
}

internal sealed class PlayniteLibraryCapturedItem
{
    internal PlayniteLibraryCapturedItem(PlayniteLibraryCapturedQuery query, int index, PlayniteLibraryItem item)
    { Query = query; Index = index; Item = item; }
    internal PlayniteLibraryCapturedQuery Query { get; }
    internal int Index { get; }
    internal PlayniteLibraryItem Item { get; }
}
