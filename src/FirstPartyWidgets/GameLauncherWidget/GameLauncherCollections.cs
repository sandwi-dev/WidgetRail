using WidgetRail.WidgetSdk;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.FirstPartyWidgets.GameLauncher;

internal enum GameLauncherCollectionKind
{
    AllInstalled,
    Recent,
    Favorites,
    Manual,
    Source,
}

internal sealed record GameLauncherCollectionSelection(
    GameLauncherCollectionKind Kind,
    string? SourceAttribution = null);

internal sealed record GameLauncherCollectionState(
    WidgetAppLibraryQuery Query,
    GameLauncherCollectionSelection? Selected = null)
{
    internal GameLauncherCollectionSelection Selection =>
        Selected ?? GameLauncherCollectionPolicy.AllInstalled;
    internal GameLauncherRecentMode RecentMode => Selection.Kind ==
        GameLauncherCollectionKind.Recent
            ? GameLauncherRecentMode.RecentOnly
            : GameLauncherRecentMode.Off;
    internal bool FavoriteFilter => Selection.Kind ==
        GameLauncherCollectionKind.Favorites;
    internal bool ManualFilter => Selection.Kind ==
        GameLauncherCollectionKind.Manual;

    internal GameLauncherCollectionState Select(
        GameLauncherCollectionSelection selection,
        WidgetAppLibraryQuery installedGames) => new(
            installedGames with
            {
                SearchText = Query.SearchText,
                Sort = Query.Sort,
                SourceAttribution = selection.Kind == GameLauncherCollectionKind.Source
                    ? selection.SourceAttribution
                    : null,
            }, selection);

    internal GameLauncherCollectionState Reset(WidgetAppLibraryQuery query) => new(query);

    internal GameLauncherCollectionState ClearQuery(
        WidgetAppLibraryQuery installedGames)
    {
        var selection = Selection;
        return new(
            installedGames with
            {
                SourceAttribution = selection.Kind == GameLauncherCollectionKind.Source
                    ? selection.SourceAttribution
                    : null,
            }, selection);
    }

    internal GameLauncherCollectionState ToggleFavorites(
        WidgetAppLibraryQuery installedGames) => Select(
            Selection.Kind == GameLauncherCollectionKind.Favorites
                ? GameLauncherCollectionPolicy.AllInstalled
                : new(GameLauncherCollectionKind.Favorites), installedGames);

    internal GameLauncherCollectionState CycleRecent(
        WidgetAppLibraryQuery installedGames) => Select(
            Selection.Kind == GameLauncherCollectionKind.Recent
                ? GameLauncherCollectionPolicy.AllInstalled
                : new(GameLauncherCollectionKind.Recent), installedGames);
}

internal sealed record GameLauncherCollectionOption(
    string ActionId,
    string Label,
    GameLauncherCollectionSelection Selection,
    bool Selected);

internal static class GameLauncherSourceCatalog
{
    internal static string[] Normalize(IReadOnlyList<string>? sources)
    {
        if (sources is null || sources.Count > GameLauncherPrivateState.MaximumProvenSources)
            return [];
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            if (source is not { Length: > 0 and <= GameLauncherPrivateState.MaximumSourceNameLength } ||
                string.IsNullOrWhiteSpace(source) ||
                source.Any(char.IsControl) || !normalized.Add(source))
                return [];
        return normalized.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static string[] Reconcile(
        IReadOnlyList<string> persisted,
        GameLauncherCollectionState requested,
        WidgetCursorDirection? direction,
        IReadOnlyList<GameLauncherItem> items,
        IReadOnlyList<WidgetAppLibrarySource> observations,
        bool completeCatalog)
    {
        var sources = Normalize(persisted).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (direction is null && requested.Selection.Kind ==
                GameLauncherCollectionKind.AllInstalled &&
            string.IsNullOrWhiteSpace(requested.Query.SearchText))
        {
            var authoritative = (completeCatalog
                    ? items.Select(item => item.Value.Presentation.Source.DisplayName)
                    : observations.Select(source => source.DisplayName))
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            sources.RemoveWhere(source => !authoritative.Contains(source));
        }
        foreach (var source in items.Select(item =>
                     item.Value.Presentation.Source.DisplayName)
                 .Where(source => !string.IsNullOrWhiteSpace(source)))
        {
            if (sources.Count >= GameLauncherPrivateState.MaximumProvenSources &&
                !sources.Contains(source)) continue;
            sources.Add(source);
        }
        return sources.OrderBy(source => source, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal static class GameLauncherCollectionPolicy
{
    internal const string ActionPrefix = "game-launcher.collection.select.";
    internal static GameLauncherCollectionSelection AllInstalled { get; } =
        new(GameLauncherCollectionKind.AllInstalled);

    internal static IReadOnlyList<GameLauncherCollectionOption> Options(
        GameLauncherPrivateState organization,
        IReadOnlyList<string> provenSources,
        GameLauncherCollectionSelection current)
    {
        var options = new List<GameLauncherCollectionOption>
        {
            Option("all", "All installed", AllInstalled, current),
        };
        if (organization.RecentSavedIds.Count != 0)
            options.Add(Option("recent", $"Continue ({organization.RecentSavedIds.Count})",
                new(GameLauncherCollectionKind.Recent), current));
        if (organization.FavoriteSavedIds.Count != 0)
            options.Add(Option("favorites",
                $"Favorites ({organization.FavoriteSavedIds.Count})",
                new(GameLauncherCollectionKind.Favorites), current));
        if (organization.ManualSavedIds.Count != 0)
            options.Add(Option("manual", $"Manual ({organization.ManualSavedIds.Count})",
                new(GameLauncherCollectionKind.Manual), current));
        foreach (var source in provenSources
                     .Where(source => !string.IsNullOrWhiteSpace(source))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                     .Take(GameLauncherWidget.MaximumKnownSourcesForCollections))
        {
            var selection = new GameLauncherCollectionSelection(
                GameLauncherCollectionKind.Source, source);
            options.Add(Option("source." + Hash(source), source, selection, current));
        }
        return options;
    }

    internal static GameLauncherCollectionSelection? Resolve(
        string actionId,
        IReadOnlyList<GameLauncherCollectionOption> options) =>
        options.FirstOrDefault(option => string.Equals(
            option.ActionId, actionId, StringComparison.Ordinal))?.Selection;

    private static GameLauncherCollectionOption Option(
        string suffix,
        string label,
        GameLauncherCollectionSelection selection,
        GameLauncherCollectionSelection current) =>
        new(ActionPrefix + suffix, label, selection, selection == current);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 8))
        .ToLowerInvariant();
}
