using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryItem(
    WidgetAppLibraryItem Value,
    WidgetCollectionItemKey Key,
    string? ArtworkPngBase64 = null)
{
    internal static PlayniteLibraryItem From(
        WidgetAppLibraryItem item,
        string? artworkPngBase64 = null) =>
        new(item, PlayniteLibraryIdentity.Key(item.SavedId), artworkPngBase64);

    internal PlayniteLibraryItem WithValue(WidgetAppLibraryItem item) =>
        From(item, ArtworkPngBase64);

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

internal enum PlayniteLibraryRoute
{
    Library,
    Details,
    AddGames,
    Running,
    Hidden,
    Categories,
    Category,
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

internal enum PlayniteLibraryVariantActionResult
{
    Rejected,
    Started,
    Completed,
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
