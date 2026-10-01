using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>
/// Final Browse membership over a captured provider query. Construct with the
/// same authoritative presentation organization used by the page. Only lightweight
/// identities/presentation metadata are prepared here; demanded ranges project rows.
/// Home's manual, title-match and unavailable fixed rows belong to its separate rail.
/// </summary>
internal sealed class PlayniteLibraryBrowseQuery
{
    internal static PlayniteLibraryBrowseQuery Empty { get; } = new(new(new(false),
        new(PlayniteLibraryQueryScope.Library), [], PlayniteLibraryAuthorityProjection.Empty,
        [], "empty", 0, false, _ => throw new InvalidOperationException("The empty query has no rows."),
        (_, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null)), PlayniteLibraryPrivateState.Empty, false);
    private sealed record Entry(int ProviderIndex, string SavedId, WidgetCollectionItemKey Key,
        string Title, string Source, bool Favorite, bool Preferred, int GroupSize);
    private readonly PlayniteLibraryCapturedQuery source;
    private readonly Entry[] entries;
    private readonly Dictionary<WidgetCollectionItemKey, int> indices;

    internal PlayniteLibraryBrowseQuery(PlayniteLibraryCapturedQuery source,
        PlayniteLibraryPrivateState organization, bool favoriteOnly)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(organization);
        if (source.Context.Scope is not (PlayniteLibraryQueryScope.Library or
            PlayniteLibraryQueryScope.Category or PlayniteLibraryQueryScope.RecentlyPlayed))
            throw new ArgumentException("Browse requires a Library, Category or RecentlyPlayed capture.", nameof(source));
        this.source = source;
        var titles = organization.TitleOverrides.ToDictionary(value => value.SavedId, value => value.Title, StringComparer.Ordinal);
        var favorites = organization.FavoriteSavedIds.ToHashSet(StringComparer.Ordinal);
        var excluded = organization.ExcludedSavedIds.ToHashSet(StringComparer.Ordinal);
        var queryIds = source.Query.FavoriteSavedIds.ToHashSet(StringComparer.Ordinal);
        var preferred = organization.VariantGroups.Select(group => group.PreferredSavedId).ToHashSet(StringComparer.Ordinal);
        var groupSizes = organization.VariantGroups.SelectMany(group => group.SavedIds.Select(id => (Id: id, Count: group.SavedIds.Count)))
            .ToDictionary(value => value.Id, value => value.Count, StringComparer.Ordinal);
        var result = new List<Entry>();
        for (var index = 0; index < source.Count; ++index)
        {
            var game = source.Games[index];
            var title = titles.GetValueOrDefault(game.Id, game.Name);
            // Apply title, source, favorite and exclusion predicates once to exact membership.
            // Provider ordering is retained even when a displayed title changes.
            if (excluded.Contains(game.Id) || favoriteOnly && !favorites.Contains(game.Id) ||
                source.Query.SearchText is { } search && !title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                source.Query.SourceAttribution is { } attribution && !string.Equals(game.Source, attribution, StringComparison.OrdinalIgnoreCase) ||
                queryIds.Count != 0 && !queryIds.Contains(game.Id)) continue;
            result.Add(new(index, game.Id, PlayniteLibraryIdentity.Key(game.Id), title, game.Source,
                favorites.Contains(game.Id), preferred.Contains(game.Id), groupSizes.GetValueOrDefault(game.Id)));
        }
        entries = result.ToArray();
        indices = entries.Select((entry, index) => (entry.Key, Index: index)).ToDictionary(value => value.Key, value => value.Index);
        Categories = Array.AsReadOnly(organization.Categories.Select(category => category with
            { SavedIds = Array.AsReadOnly(category.SavedIds.ToArray()) }).ToArray());
    }

    internal int Count => entries.Length;
    internal PlayniteLibraryCapturedQuery Source => source;
    internal IReadOnlyList<PlayniteLibraryCategory> Categories { get; }
    internal bool TryGetIndex(WidgetCollectionItemKey key, out int index) => indices.TryGetValue(key, out index);
    internal WidgetCollectionItemKey KeyAt(int index) => entries[index].Key;
    internal bool SameMembership(PlayniteLibraryBrowseQuery other) =>
        Count == other.Count && entries.Select(entry => entry.Key).SequenceEqual(other.entries.Select(entry => entry.Key));
    internal bool SameProjection(PlayniteLibraryBrowseQuery other) => ReferenceEquals(source, other.source) &&
        entries.SequenceEqual(other.entries) && Categories.Count == other.Categories.Count &&
        Categories.Zip(other.Categories).All(pair => pair.First.Id == pair.Second.Id &&
            pair.First.Name == pair.Second.Name && pair.First.SavedIds.SequenceEqual(pair.Second.SavedIds));

    internal PlayniteLibraryBrowseItem? Find(WidgetCollectionItemKey key) =>
        TryGetIndex(key, out var index) ? ReadRange(index, 1)[0] : null;

    internal IReadOnlyList<PlayniteLibraryBrowseItem> ReadRange(int start, int count, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (start < 0 || count is < 1 or > PlayniteBridgeClient.MaximumPageSize || start > Count - count)
            throw new ArgumentOutOfRangeException(nameof(start), "The range must fit final Browse membership and contain 1–64 items.");
        var result = new PlayniteLibraryBrowseItem[count];
        for (var offset = 0; offset < count;)
        {
            token.ThrowIfCancellationRequested();
            var first = entries[start + offset];
            var run = 1;
            while (offset + run < count && entries[start + offset + run].ProviderIndex == first.ProviderIndex + run) ++run;
            var captured = source.ReadRange(first.ProviderIndex, run, token);
            for (var i = 0; i < run; ++i)
            {
                var entry = entries[start + offset + i];
                var provider = captured[i];
                var item = provider.Item;
                if (item.Value.SavedId != entry.SavedId || item.Key != entry.Key)
                    throw new InvalidOperationException("A captured provider row changed its exact identity.");
                if (!string.Equals(item.Presentation.DisplayName, entry.Title, StringComparison.Ordinal))
                    item = item.WithProjectedValue(item.Value with
                        { Presentation = item.Presentation with { DisplayName = entry.Title } });
                var row = new PlayniteLibraryHeroRailItem(item, new(entry.SavedId, entry.Title, entry.Source),
                    entry.Favorite, entry.Preferred, entry.GroupSize, CollectionItem: true);
                result[offset + i] = new(this, start + offset + i, provider, row);
            }
            offset += run;
        }
        return Array.AsReadOnly(result);
    }

    internal ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(PlayniteLibraryBrowseItem item,
        WidgetArtworkHandle handle, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        token.ThrowIfCancellationRequested();
        return ReferenceEquals(item.Query, this) ? source.ResolveArtworkAsync(item.ProviderItem, handle, token)
            : ValueTask.FromResult<WidgetEncodedArtwork?>(null);
    }
}

internal sealed class PlayniteLibraryBrowseItem
{
    internal PlayniteLibraryBrowseItem(PlayniteLibraryBrowseQuery query, int index,
        PlayniteLibraryCapturedItem providerItem, PlayniteLibraryHeroRailItem row)
    { Query = query; Index = index; ProviderItem = providerItem; Row = row; }
    internal PlayniteLibraryBrowseQuery Query { get; }
    internal int Index { get; }
    internal PlayniteLibraryCapturedItem ProviderItem { get; }
    internal PlayniteLibraryHeroRailItem Row { get; }
    internal PlayniteLibraryItem Item => Row.Current!;
    internal WidgetCollectionItemKey Key => Item.Key;
}
