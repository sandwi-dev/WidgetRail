using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal static class GameLauncherTitlePolicy
{
    internal static bool RequiresReset(GameLauncherPrivateState? state)
    {
        if (state?.Items is null) return false;
        var display = new Dictionary<string, GameLauncherDisplayItem>(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (item?.SavedId is null || !display.TryAdd(item.SavedId, item)) return false;
        _ = Normalize(state.TitleOverrides, display, out var reset);
        return reset || state.TitleOverrides is { Count: > 0 } &&
            JsonSerializer.SerializeToUtf8Bytes(state).Length >
                WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes;
    }

    internal static IReadOnlyList<GameLauncherTitleOverride> Normalize(
        IReadOnlyList<GameLauncherTitleOverride>? overrides,
        IReadOnlyDictionary<string, GameLauncherDisplayItem> display,
        out bool reset)
    {
        reset = false;
        if (overrides is null ||
            overrides.Count > GameLauncherPrivateState.MaximumTitleOverrides)
            return Reset(out reset);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new GameLauncherTitleOverride[overrides.Count];
        for (var index = 0; index < overrides.Count; index++)
        {
            var candidate = overrides[index];
            var title = NormalizeTitle(candidate?.Title);
            if (candidate is null || !display.ContainsKey(candidate.SavedId) ||
                !ids.Add(candidate.SavedId) || title is null ||
                !string.Equals(title, candidate.Title, StringComparison.Ordinal))
                return Reset(out reset);
            result[index] = candidate;
        }
        return result;
    }

    internal static string? NormalizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length is > 0 and <= GameLauncherPrivateState.MaximumTitleLength &&
            !normalized.Any(char.IsControl) ? normalized : null;
    }

    internal static GameLauncherStateMutation Set(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem providerDisplay,
        string? title)
    {
        state = GameLauncherOrganizationPolicy.Normalize(state);
        var normalized = NormalizeTitle(title);
        var existing = state.TitleOverrides.FirstOrDefault(candidate =>
            candidate.SavedId == providerDisplay.SavedId);
        var clear = normalized is null || string.Equals(
            normalized, providerDisplay.DisplayName, StringComparison.Ordinal);
        if (clear && existing is null || !clear && existing?.Title == normalized)
            return GameLauncherStateMutation.Reject(state);
        if (!clear && existing is null &&
            state.TitleOverrides.Count >= GameLauncherPrivateState.MaximumTitleOverrides)
            return GameLauncherStateMutation.Reject(state);
        var retained = GameLauncherOrganizationPolicy.RetainDisplay(state, providerDisplay);
        if (!retained.Items.Any(item => item.SavedId == providerDisplay.SavedId))
            return GameLauncherStateMutation.Reject(state);
        var overrides = retained.TitleOverrides
            .Where(candidate => candidate.SavedId != providerDisplay.SavedId).ToList();
        if (!clear)
            overrides.Add(new(providerDisplay.SavedId, normalized!));
        var candidate = retained with { TitleOverrides = overrides };
        return JsonSerializer.SerializeToUtf8Bytes(candidate).Length <=
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes
            ? GameLauncherStateMutation.Apply(candidate)
            : GameLauncherStateMutation.Reject(state);
    }

    internal static string DisplayName(
        GameLauncherPrivateState state,
        string savedId,
        string providerTitle) => state.TitleOverrides.FirstOrDefault(candidate =>
            candidate.SavedId == savedId)?.Title ?? providerTitle;

    internal static WidgetAppLibraryItem Project(
        GameLauncherPrivateState state,
        WidgetAppLibraryItem item)
    {
        var title = DisplayName(state, item.SavedId, item.Presentation.DisplayName);
        return title == item.Presentation.DisplayName ? item : item with
        {
            Presentation = item.Presentation with { DisplayName = title },
        };
    }

    internal static GameLauncherPrivateState Project(GameLauncherPrivateState state) =>
        state with
        {
            Items = state.Items.Select(item => item with
            {
                DisplayName = DisplayName(state, item.SavedId, item.DisplayName),
            }).ToArray(),
        };

    internal static IReadOnlyList<string> SearchMatches(
        GameLauncherPrivateState state,
        string? query) => string.IsNullOrWhiteSpace(query)
            ? []
            : state.TitleOverrides.Where(candidate => candidate.Title.Contains(
                    query, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.SavedId).ToArray();

    private static IReadOnlyList<GameLauncherTitleOverride> Reset(out bool reset)
    {
        reset = true;
        return [];
    }
}
