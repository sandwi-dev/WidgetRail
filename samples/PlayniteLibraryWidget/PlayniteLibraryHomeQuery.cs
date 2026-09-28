using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>Frozen Home membership. Provider rows are projected only for demanded ranges;
/// bounded manual/title-match rows and unavailable saved displays retain their rail order.</summary>
internal sealed class PlayniteLibraryHomeQuery
{
    private sealed record Entry(int? ProviderIndex, PlayniteLibraryItem? Fixed,
        PlayniteLibraryDisplayItem Display, bool Favorite, bool Preferred, int GroupSize, bool CollectionItem)
    { internal WidgetCollectionItemKey Key => PlayniteLibraryIdentity.Key(Display.SavedId); }
    private readonly Entry[] entries;
    private readonly Dictionary<WidgetCollectionItemKey, int> indices;
    internal PlayniteLibraryCapturedQuery Source { get; }
    internal PlayniteLibraryFixedRows FixedRows { get; }
    internal IReadOnlyList<PlayniteLibraryCategory> Categories { get; }
    internal int Count => entries.Length;
    internal static PlayniteLibraryHomeQuery Empty { get; } = new(new(new(true),
        new(PlayniteLibraryQueryScope.Home), [], PlayniteLibraryAuthorityProjection.Empty, [], "empty", 0, false,
        _ => throw new InvalidOperationException("The empty query has no rows."),
        (_, _, _) => ValueTask.FromResult<WidgetEncodedArtwork?>(null)),
        PlayniteLibraryPrivateState.Empty, PlayniteLibraryFixedRows.Empty, false);

    internal PlayniteLibraryHomeQuery(PlayniteLibraryCapturedQuery source, PlayniteLibraryPrivateState organization,
        PlayniteLibraryFixedRows fixedRows, bool favoriteOnly)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(fixedRows);
        if (source.Context.Scope is not (PlayniteLibraryQueryScope.Home or PlayniteLibraryQueryScope.RecentlyPlayed))
            throw new ArgumentException("Home requires a Home or RecentlyPlayed capture.", nameof(source));
        Source = source;
        FixedRows = new(Array.AsReadOnly(fixedRows.Recent.ToArray()), Array.AsReadOnly(fixedRows.Manual.ToArray()),
            Array.AsReadOnly(fixedRows.TitleMatches.ToArray()));
        var favorites = organization.FavoriteSavedIds.ToHashSet(StringComparer.Ordinal);
        var excluded = organization.ExcludedSavedIds.ToHashSet(StringComparer.Ordinal);
        var titles = organization.TitleOverrides.ToDictionary(value => value.SavedId, value => value.Title, StringComparer.Ordinal);
        var preferred = organization.VariantGroups.Select(group => group.PreferredSavedId).ToHashSet(StringComparer.Ordinal);
        var groups = organization.VariantGroups.SelectMany(group => group.SavedIds.Select(id => (id, group.SavedIds.Count)))
            .ToDictionary(value => value.id, value => value.Count, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Entry>();
        bool Matches(PlayniteLibraryDisplayItem display) => !excluded.Contains(display.SavedId) &&
            (!favoriteOnly || favorites.Contains(display.SavedId)) &&
            (source.Query.SearchText is not { } search || display.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (source.Query.SourceAttribution is not { } attribution || string.Equals(display.SourceAttribution, attribution, StringComparison.OrdinalIgnoreCase)) &&
            (source.Query.FavoriteSavedIds.Count == 0 || source.Query.FavoriteSavedIds.Contains(display.SavedId, StringComparer.Ordinal));
        Entry Make(int? index, PlayniteLibraryItem? item, PlayniteLibraryDisplayItem display, bool collection) =>
            new(index, item, display, favorites.Contains(display.SavedId), preferred.Contains(display.SavedId),
                groups.GetValueOrDefault(display.SavedId), collection);
        PlayniteLibraryDisplayItem Display(PlayniteLibraryItem item) => new(item.Value.SavedId,
            titles.GetValueOrDefault(item.Value.SavedId, item.Presentation.DisplayName), item.Presentation.Source.DisplayName);
        var resolved = fixedRows.All.DistinctBy(item => item.Value.SavedId).ToDictionary(item => item.Value.SavedId, StringComparer.Ordinal);
        var stored = organization.Items.Select(item => item with
            { DisplayName = titles.GetValueOrDefault(item.SavedId, item.DisplayName) }).ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var manual = organization.ManualSavedIds.Select(id =>
        {
            var current = resolved.GetValueOrDefault(id);
            var display = current is null ? stored.GetValueOrDefault(id) : Display(current);
            return display is null || current?.Presentation.Kind == WidgetAppLibraryKind.Game ? null : Make(null, current, display, false);
        }).OfType<Entry>().Where(entry => Matches(entry.Display));
        manual = source.Query.Sort == WidgetAppLibrarySortOrder.SourceThenDisplayName
            ? manual.OrderBy(entry => entry.Display.SourceAttribution, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Display.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Display.SavedId, StringComparer.Ordinal)
            : source.Query.Sort == WidgetAppLibrarySortOrder.DisplayNameDescending
                ? manual.OrderByDescending(entry => entry.Display.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Display.SavedId, StringComparer.Ordinal)
                : manual.OrderBy(entry => entry.Display.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(entry => entry.Display.SavedId, StringComparer.Ordinal);
        foreach (var entry in manual.Take(PlayniteLibraryPrivateState.MaximumManualItems))
            if (used.Add(entry.Display.SavedId)) result.Add(entry);
        foreach (var item in fixedRows.TitleMatches)
        {
            var display = Display(item);
            if (Matches(display) && used.Add(display.SavedId)) result.Add(Make(null, item, display, false));
        }
        for (var index = 0; index < source.Count; index++)
        {
            var game = source.Games[index];
            var display = new PlayniteLibraryDisplayItem(game.Id, titles.GetValueOrDefault(game.Id, game.Name), game.Source);
            if (Matches(display) && used.Add(game.Id)) result.Add(Make(index, null, display, true));
        }
        var live = source.Games.Select(game => game.Id).Concat(fixedRows.All.Select(item => item.Value.SavedId)).ToHashSet(StringComparer.Ordinal);
        var referenced = PlayniteLibraryOrganizationPolicy.ReferencedSavedIds(organization).ToHashSet(StringComparer.Ordinal);
        foreach (var display in organization.Items.Select(item => stored[item.SavedId]).Where(item => referenced.Contains(item.SavedId) &&
                     !organization.ManualSavedIds.Contains(item.SavedId, StringComparer.Ordinal) && !live.Contains(item.SavedId) && Matches(item))
                     .Take(WidgetAppLibraryService.MaximumSavedItems))
            if (used.Add(display.SavedId)) result.Add(Make(null, null, display, false));
        entries = result.ToArray();
        indices = entries.Select((entry, index) => (entry.Key, index)).ToDictionary(value => value.Key, value => value.index);
        Categories = Array.AsReadOnly(organization.Categories.Select(category => category with
            { SavedIds = Array.AsReadOnly(category.SavedIds.ToArray()) }).ToArray());
    }

    internal bool SameMembership(PlayniteLibraryHomeQuery other) => entries.Select(entry => entry.Key).SequenceEqual(other.entries.Select(entry => entry.Key));
    internal bool SameProjection(PlayniteLibraryHomeQuery other) => ReferenceEquals(Source, other.Source) && entries.SequenceEqual(other.entries) &&
        Categories.Count == other.Categories.Count && Categories.Zip(other.Categories).All(pair => pair.First.Id == pair.Second.Id &&
            pair.First.Name == pair.Second.Name && pair.First.SavedIds.SequenceEqual(pair.Second.SavedIds));
    internal PlayniteLibraryHomeItem? Find(WidgetCollectionItemKey key) => indices.TryGetValue(key, out var index) ? ReadRange(index, 1)[0] : null;
    internal IReadOnlyList<PlayniteLibraryHomeItem> ReadRange(int start, int count, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (start < 0 || count is < 1 or > PlayniteBridgeClient.MaximumPageSize || start > Count - count)
            throw new ArgumentOutOfRangeException(nameof(start));
        var result = new PlayniteLibraryHomeItem[count];
        for (var offset = 0; offset < count; offset++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[start + offset];
            var captured = entry.ProviderIndex is { } index ? Source.ReadRange(index, 1, token)[0] : null;
            var current = captured?.Item ?? entry.Fixed;
            if (current is not null)
            {
                if (current.Key != entry.Key) throw new InvalidOperationException("A captured Home row changed its identity.");
                current = current.WithProjectedValue(current.Value with { Presentation = current.Presentation with { DisplayName = entry.Display.DisplayName } });
            }
            result[offset] = new(this, captured, new(current, entry.Display, entry.Favorite, entry.Preferred, entry.GroupSize, entry.CollectionItem));
        }
        return Array.AsReadOnly(result);
    }
    internal ValueTask<WidgetEncodedArtwork?> ResolveArtworkAsync(PlayniteLibraryHomeItem item, WidgetArtworkHandle handle,
        Func<WidgetArtworkHandle, CancellationToken, ValueTask<WidgetEncodedArtwork?>> fixedResolver, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(item.Query, this)) return ValueTask.FromResult<WidgetEncodedArtwork?>(null);
        return item.Provider is { } provider ? Source.ResolveArtworkAsync(provider, handle, token)
            : item.Row.Current?.Presentation.Artwork.Items.Any(artwork => artwork.Handle == handle.Value) == true
                ? fixedResolver(handle, token) : ValueTask.FromResult<WidgetEncodedArtwork?>(null);
    }
}

internal sealed record PlayniteLibraryHomeItem(PlayniteLibraryHomeQuery Query, PlayniteLibraryCapturedItem? Provider,
    PlayniteLibraryHeroRailItem Row)
{ internal WidgetCollectionItemKey Key => Row.Key; }
