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

internal sealed record GameLauncherPrivateState(
    int Version,
    IReadOnlyList<GameLauncherDisplayItem> Items)
{
    internal const int CurrentVersion = 1;
    internal const int MaximumItems = 96;
    internal static readonly GameLauncherPrivateState Empty = new(CurrentVersion, []);

    internal static GameLauncherPrivateState Normalize(GameLauncherPrivateState? state)
    {
        if (state is null || state.Version != CurrentVersion || state.Items is null ||
            state.Items.Count > MaximumItems) return Empty;
        var saved = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<GameLauncherDisplayItem>(state.Items.Count);
        foreach (var item in state.Items)
        {
            if (item is null || item.SavedId is not { Length: > 0 and <= 128 } ||
                !item.SavedId.StartsWith("saved-", StringComparison.Ordinal) ||
                item.DisplayName is not { Length: > 0 and <= 120 } ||
                item.SourceAttribution is not { Length: > 0 and <= 64 } ||
                !saved.Add(item.SavedId)) return Empty;
            result.Add(item);
        }
        return new(CurrentVersion, result.ToArray());
    }

    internal static GameLauncherPrivateState FromPage(IReadOnlyList<GameLauncherItem> items) =>
        new(CurrentVersion, items.Take(MaximumItems).Select(item =>
            new GameLauncherDisplayItem(
                item.Value.SavedId,
                item.Value.DisplayName.Length <= 120
                    ? item.Value.DisplayName : item.Value.DisplayName[..120],
                item.Value.SourceAttribution.Length <= 64
                    ? item.Value.SourceAttribution : item.Value.SourceAttribution[..64]))
            .ToArray());
}

internal static class GameLauncherIdentity
{
    internal static WidgetCollectionItemKey Key(string savedId) =>
        new("game." + Hash(savedId));

    internal static string FocusId(string mode, WidgetCollectionItemKey key) =>
        $"game-launcher.item.{mode}.{key.Value}";

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 10))
        .ToLowerInvariant();
}
