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
}

internal sealed record GameLauncherDisplayItem(
    string SavedId,
    string DisplayName,
    string SourceAttribution);

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
