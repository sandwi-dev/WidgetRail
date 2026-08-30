using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameLauncher;

internal sealed record GameLauncherItem(
    WidgetAppLibraryItem Value,
    WidgetCollectionItemKey Key,
    string? ArtworkPngBase64 = null)
{
    internal static GameLauncherItem From(
        WidgetAppLibraryItem item,
        string? artworkPngBase64 = null) =>
        new(item, GameLauncherIdentity.Key(item.SavedId), artworkPngBase64);

    internal GameLauncherItem WithValue(WidgetAppLibraryItem item) =>
        From(item, ArtworkPngBase64);

    internal WidgetAppLibraryPresentation Presentation => Value.Presentation;
}

internal sealed record GameLauncherDisplayItem(
    string SavedId,
    string DisplayName,
    string SourceAttribution);

internal sealed record GameLauncherFixedRows(
    IReadOnlyList<GameLauncherItem> Recent,
    IReadOnlyList<GameLauncherItem> Manual,
    IReadOnlyList<GameLauncherItem> TitleMatches)
{
    internal static GameLauncherFixedRows Empty { get; } = new([], [], []);
    internal IEnumerable<GameLauncherItem> All => Recent.Concat(Manual).Concat(TitleMatches);
}

internal enum GameLauncherRoute
{
    Library,
    Details,
    AddGames,
    Running,
    Hidden,
    Categories,
    Category,
}

internal enum GameLauncherLaunchState
{
    RequestAccepted,
    LauncherStarted,
    Running,
    Failed,
    Ended,
}

internal enum GameLauncherVariantActionResult
{
    Rejected,
    Started,
    Completed,
}

internal static class GameLauncherIdentity
{
    internal static WidgetCollectionItemKey Key(string savedId) =>
        new("game." + Hash(savedId));

    internal static string FocusId(string mode, WidgetCollectionItemKey key) =>
        $"game-launcher.item.{mode}.{key.Value}";

    internal static string GroupId(string first, string second) =>
        "variant." + Hash(string.CompareOrdinal(first, second) <= 0
            ? first + "\0" + second : second + "\0" + first);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 10))
        .ToLowerInvariant();
}
