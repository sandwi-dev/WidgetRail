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

    internal PlayniteLibraryItem WithValue(WidgetAppLibraryItem item) =>
        From(item);

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

internal sealed record PlayniteLibraryRenderState(
    PlayniteLibraryCollectionState Collection,
    PlayniteLibraryFixedRows FixedRows,
    IReadOnlyList<WidgetAppLibrarySource> SourceObservations,
    string? ActiveCategoryId,
    long FixedRowsRevision,
    string? PendingRestoredSavedId,
    bool PreferLibraryContentFocus,
    bool SearchExpanded,
    string? HeroSavedId,
    int HeroIndex,
    bool OrganizationBusy,
    string Status,
    string? LaunchingSavedId,
    PlayniteBridgeConnectionKind PlayniteKind,
    string PlayniteCode,
    bool PlayniteBusy)
{
    internal static PlayniteLibraryRenderState Initial(WidgetAppLibraryQuery query) => new(
        new(query),
        PlayniteLibraryFixedRows.Empty,
        [],
        null,
        0,
        null,
        false,
        false,
        null,
        0,
        false,
        "Playnite Library loads when visible",
        null,
        PlayniteBridgeConnectionKind.NotConfigured,
        "credential_missing",
        false);
}

internal enum PlayniteLibraryRoute
{
    // Library is cinematic Home. Browse owns query/filtering; Management owns
    // secondary library and connection controls.
    Library,
    Browse,
    Hidden,
    Categories,
    Category,
    Management,
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

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 10))
        .ToLowerInvariant();
}
