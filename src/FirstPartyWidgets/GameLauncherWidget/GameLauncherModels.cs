using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed record GameLauncherItem(
    WidgetAppLibraryItem Value,
    WidgetCollectionItemKey Key)
{
    internal static GameLauncherItem From(WidgetAppLibraryItem item) =>
        new(item, GameLauncherIdentity.Key(item.SavedId));

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
    Experiences,
    AddGames,
    Running,
    Hidden,
    Categories,
    Category,
}

internal enum GameLauncherExperience
{
    HeroRail,
    CoverWall,
    Carousel,
    CompactGrid,
}

internal static class GameLauncherExperienceIdentity
{
    internal const string HeroRail = "hero-rail";

    internal static string Id(GameLauncherExperience experience) => experience switch
    {
        GameLauncherExperience.CoverWall => "cover-wall",
        GameLauncherExperience.Carousel => "carousel",
        GameLauncherExperience.CompactGrid => "compact-grid",
        _ => HeroRail,
    };

    internal static string Label(GameLauncherExperience experience) => experience switch
    {
        GameLauncherExperience.CoverWall => "Cover Wall",
        GameLauncherExperience.Carousel => "Carousel",
        GameLauncherExperience.CompactGrid => "Compact Grid",
        _ => "Hero Rail",
    };

    internal static GameLauncherExperience Parse(string? value) => value switch
    {
        "cover-wall" => GameLauncherExperience.CoverWall,
        "carousel" => GameLauncherExperience.Carousel,
        "compact-grid" => GameLauncherExperience.CompactGrid,
        _ => GameLauncherExperience.HeroRail,
    };

    internal static bool IsValid(string? value) => value is
        HeroRail or "cover-wall" or "carousel" or "compact-grid";
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
