using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryItem(
    WidgetAppLibraryItem Value,
    WidgetCollectionItemKey Key)
{
    internal static PlayniteLibraryItem From(WidgetAppLibraryItem item) =>
        new(item, PlayniteLibraryIdentity.Key(item.SavedId));

    internal PlayniteLibraryItem WithProjectedValue(WidgetAppLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!string.Equals(Value.SavedId, item.SavedId, StringComparison.Ordinal) ||
            Key != PlayniteLibraryIdentity.Key(item.SavedId))
            throw new InvalidOperationException(
                "A projected Playnite item cannot change its exact saved identity or key.");
        return new(item, Key);
    }

    internal WidgetAppLibraryPresentation Presentation => Value.Presentation;
}

internal sealed record PlayniteLibraryDisplayItem(
    string SavedId,
    string DisplayName,
    string SourceAttribution);

internal sealed record PlayniteLibraryFixedRows(
    IReadOnlyList<PlayniteLibraryItem> Recent,
    IReadOnlyList<PlayniteLibraryItem> Manual,
    IReadOnlyList<PlayniteLibraryItem> TitleMatches)
{
    internal static PlayniteLibraryFixedRows Empty { get; } = new([], [], []);
    internal IEnumerable<PlayniteLibraryItem> All => Recent.Concat(Manual).Concat(TitleMatches);
}

internal sealed record PlayniteLibraryBrowseReload(
    long AttemptId,
    WidgetCursorResourceSnapshot<PlayniteLibraryItem>? RetainedCollection);

internal sealed record PlayniteLibraryQueryCount(long Generation, int? Count);

internal sealed class PlayniteLibraryCategoryFeedback(
    string message,
    bool succeeded,
    string scopeId)
{
    internal string Message { get; } = message;
    internal bool Succeeded { get; } = succeeded;
    internal string ScopeId { get; } = scopeId;
}

internal sealed record PlayniteLibraryRenderState
{
    // Collection is the Home route's independent query/selection model.
    internal required PlayniteLibraryCollectionState Collection { get; init; }
    internal required PlayniteLibraryCollectionState BrowseCollection { get; init; }
    internal required WidgetAppLibraryQuery HiddenQuery { get; init; }
    internal PlayniteLibraryFixedRows FixedRows { get; init; } =
        PlayniteLibraryFixedRows.Empty;
    internal IReadOnlyList<WidgetAppLibrarySource> SourceObservations { get; init; } = [];
    internal string? ActiveCategoryId { get; init; }
    internal long FixedRowsRevision { get; init; }
    internal string? PendingRestoredSavedId { get; init; }
    internal bool PreferLibraryContentFocus { get; init; }
    internal bool SearchExpanded { get; init; }
    internal string? BrowseInitialFocusId { get; init; }
    internal PlayniteLibraryBrowseReload? ActiveBrowseReload { get; init; }
    internal PlayniteLibraryQueryCount? HomeGameCount { get; init; }
    internal PlayniteLibraryQueryCount? BrowseGameCount { get; init; }
    internal string? HeroSavedId { get; init; }
    internal int HeroIndex { get; init; }
    internal string? BrowseHeroSavedId { get; init; }
    internal int BrowseHeroIndex { get; init; }
    internal bool OrganizationBusy { get; init; }
    internal PlayniteLibraryCategoryFeedback? CategoryFeedback { get; init; }
    internal string Status { get; init; } = "Playnite Library loads when visible";
    internal string? LaunchingSavedId { get; init; }
    internal PlayniteBridgeConnectionKind PlayniteKind { get; init; } =
        PlayniteBridgeConnectionKind.NotConfigured;
    internal string PlayniteCode { get; init; } = "credential_missing";
    internal bool PlayniteBusy { get; init; }
    internal PlayniteLibraryConnectionFeedback? PlayniteFeedback { get; init; }

    internal static PlayniteLibraryRenderState Initial(WidgetAppLibraryQuery query) => new()
        {
            Collection = new(query),
            BrowseCollection = new(query),
            HiddenQuery = query,
        };
}

internal enum PlayniteLibraryRoute
{
    // Library is cinematic Home. Browse owns query/filtering; dedicated
    // routes own Categories, Hidden games, and Playnite connection.
    Library,
    Browse,
    Hidden,
    Categories,
    PlayniteConnection,
}

internal enum PlayniteLibraryLaunchState
{
    RequestAccepted,
    LauncherStarted,
    Running,
    Failed,
    Ended,
}

internal static class PlayniteLibraryIdentity
{
    internal static WidgetCollectionItemKey Key(string savedId) =>
        new("game." + Hash(savedId));

    internal static string FocusId(string mode, WidgetCollectionItemKey key) =>
        $"playnite-library.item.{mode}.{key.Value}";

    internal static string GroupId(string first, string second) =>
        "variant." + Hash(string.CompareOrdinal(first, second) <= 0
            ? first + "\0" + second : second + "\0" + first);

    internal static string SourceKey(string source) => "source." + Hash(source);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 10))
        .ToLowerInvariant();
}
