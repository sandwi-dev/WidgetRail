using WidgetRail.WidgetSdk;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.Samples.PlayniteLibrary;

internal enum PlayniteLibraryCollectionKind
{
    AllInstalled,
    RecentlyPlayed,
    Favorites,
    Manual,
    Source,
}

internal sealed record PlayniteLibraryCollectionSelection(
    PlayniteLibraryCollectionKind Kind,
    string? SourceAttribution = null);

internal sealed record PlayniteLibraryCollectionState(
    WidgetAppLibraryQuery Query,
    PlayniteLibraryCollectionSelection? Selected = null,
    bool FavoriteOnly = false,
    bool RecentlyPlayedOnly = false)
{
    internal PlayniteLibraryCollectionSelection Selection =>
        Selected ?? PlayniteLibraryCollectionPolicy.AllInstalled;
    internal bool RecentlyPlayed => RecentlyPlayedOnly || Selection.Kind ==
        PlayniteLibraryCollectionKind.RecentlyPlayed;
    internal bool FavoriteFilter => FavoriteOnly || Selection.Kind ==
        PlayniteLibraryCollectionKind.Favorites;
    internal bool ManualFilter => Selection.Kind ==
        PlayniteLibraryCollectionKind.Manual;

    internal PlayniteLibraryCollectionState Select(
        PlayniteLibraryCollectionSelection selection,
        WidgetAppLibraryQuery installedGames) => new(
            installedGames with
            {
                SearchText = Query.SearchText,
                Sort = Query.Sort,
                SourceAttribution = selection.Kind == PlayniteLibraryCollectionKind.Source
                    ? selection.SourceAttribution
                    : null,
            }, selection,
            selection.Kind == PlayniteLibraryCollectionKind.Favorites,
            selection.Kind == PlayniteLibraryCollectionKind.RecentlyPlayed);

    internal PlayniteLibraryCollectionState Reset(WidgetAppLibraryQuery query) => new(query);

    internal PlayniteLibraryCollectionState ClearQuery(
        WidgetAppLibraryQuery installedGames)
    {
        var selection = Selection;
        return new(
            installedGames with
            {
                SourceAttribution = selection.Kind == PlayniteLibraryCollectionKind.Source
                    ? selection.SourceAttribution
                    : null,
            }, selection);
    }

    internal PlayniteLibraryCollectionState ToggleFavorites(
        WidgetAppLibraryQuery installedGames) => this with
        {
            Query = Query with { },
            Selected = Selection.Kind == PlayniteLibraryCollectionKind.Favorites
                ? PlayniteLibraryCollectionPolicy.AllInstalled
                : Selected,
            FavoriteOnly = !FavoriteFilter,
        };

    internal PlayniteLibraryCollectionState ToggleRecentlyPlayed(
        WidgetAppLibraryQuery installedGames) => this with
        {
            Query = Query with { },
            Selected = Selection.Kind == PlayniteLibraryCollectionKind.RecentlyPlayed
                ? PlayniteLibraryCollectionPolicy.AllInstalled
                : Selected,
            RecentlyPlayedOnly = !RecentlyPlayed,
        };
}

internal sealed record PlayniteLibraryCollectionOption(
    string ActionId,
    string Label,
    PlayniteLibraryCollectionSelection Selection,
    bool Selected);

internal static class PlayniteLibrarySourceCatalog
{
    internal static IReadOnlyList<WidgetAppLibrarySource> RetainObservations(
        IReadOnlyList<WidgetAppLibrarySource> current,
        IReadOnlyList<WidgetAppLibrarySource> incoming) =>
        current.SequenceEqual(incoming) ? current : incoming.ToArray();

    internal static string[] Normalize(IReadOnlyList<string>? sources)
    {
        if (sources is null || sources.Count > PlayniteLibraryPrivateState.MaximumProvenSources)
            return [];
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            if (source is not { Length: > 0 and <= PlayniteLibraryPrivateState.MaximumSourceNameLength } ||
                string.IsNullOrWhiteSpace(source) ||
                source.Any(char.IsControl) || !normalized.Add(source))
                return [];
        return normalized.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string[] Reconcile(
        IReadOnlyList<string> persisted,
        PlayniteLibraryCollectionState requested,
        WidgetCursorDirection? direction,
        IReadOnlyList<PlayniteLibraryItem> items,
        IReadOnlyList<WidgetAppLibrarySource> observations,
        bool completeCatalog)
    {
        var sources = Normalize(persisted).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (direction is null && requested.Selection.Kind ==
                PlayniteLibraryCollectionKind.AllInstalled &&
            string.IsNullOrWhiteSpace(requested.Query.SearchText))
        {
            var authoritative = (completeCatalog
                    ? items.Select(item => item.Value.Presentation.Source.DisplayName)
                    : observations.Select(source => source.DisplayName))
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            sources.RemoveWhere(source => !authoritative.Contains(source));
        }
        foreach (var source in observations.Select(observation => observation.DisplayName)
                 .Concat(items.Select(item =>
                     item.Value.Presentation.Source.DisplayName))
                 .Where(source => !string.IsNullOrWhiteSpace(source)))
        {
            if (sources.Count >= PlayniteLibraryPrivateState.MaximumProvenSources &&
                !sources.Contains(source)) continue;
            sources.Add(source);
        }
        return sources.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal static class PlayniteLibraryCollectionPolicy
{
    internal const string ActionPrefix = "playnite-library.collection.select.";
    internal static PlayniteLibraryCollectionSelection AllInstalled { get; } =
        new(PlayniteLibraryCollectionKind.AllInstalled);

    internal static IReadOnlyList<PlayniteLibraryCollectionOption> Options(
        PlayniteLibraryPrivateState organization,
        IReadOnlyList<string> provenSources,
        PlayniteLibraryCollectionSelection current)
    {
        var options = new List<PlayniteLibraryCollectionOption>
        {
            Option("all", "All installed", AllInstalled, current),
        };
        options.Add(Option("recent", "Recently played",
            new(PlayniteLibraryCollectionKind.RecentlyPlayed), current));
        if (organization.FavoriteSavedIds.Count != 0)
            options.Add(Option("favorites",
                $"Favorites ({organization.FavoriteSavedIds.Count})",
                new(PlayniteLibraryCollectionKind.Favorites), current));
        if (organization.ManualSavedIds.Count != 0)
            options.Add(Option("manual", $"Manual ({organization.ManualSavedIds.Count})",
                new(PlayniteLibraryCollectionKind.Manual), current));
        foreach (var source in provenSources
                     .Where(source => !string.IsNullOrWhiteSpace(source))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                     .Take(PlayniteLibraryWidget.MaximumKnownSourcesForCollections))
        {
            var selection = new PlayniteLibraryCollectionSelection(
                PlayniteLibraryCollectionKind.Source, source);
            options.Add(Option("source." + Hash(source), source, selection, current));
        }
        return options;
    }

    internal static PlayniteLibraryCollectionSelection? Resolve(
        string actionId,
        IReadOnlyList<PlayniteLibraryCollectionOption> options) =>
        options.FirstOrDefault(option => string.Equals(
            option.ActionId, actionId, StringComparison.Ordinal))?.Selection;

    private static PlayniteLibraryCollectionOption Option(
        string suffix,
        string label,
        PlayniteLibraryCollectionSelection selection,
        PlayniteLibraryCollectionSelection current) =>
        new(ActionPrefix + suffix, label, selection, selection == current);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 8))
        .ToLowerInvariant();
}
