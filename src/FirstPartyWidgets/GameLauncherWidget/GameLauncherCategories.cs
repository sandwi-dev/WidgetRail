using System.Text;
using System.Text.Json;
using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal enum GameLauncherCollectionDirection
{
    Previous,
    Next,
}

internal static class GameLauncherCategoryPolicy
{
    internal static bool RequiresReset(GameLauncherPrivateState? state)
    {
        if (state is null || state.Items is null) return false;
        var display = new Dictionary<string, GameLauncherDisplayItem>(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (item?.SavedId is null || !display.TryAdd(item.SavedId, item)) return false;
        _ = Normalize(state.Categories, display, out var reset);
        return reset || state.Categories is { Count: > 0 } &&
            JsonSerializer.SerializeToUtf8Bytes(state).Length >
                WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes;
    }

    internal static IReadOnlyList<GameLauncherCategory> Normalize(
        IReadOnlyList<GameLauncherCategory>? categories,
        IReadOnlyDictionary<string, GameLauncherDisplayItem> display,
        out bool reset)
    {
        reset = false;
        if (categories is null || categories.Count > GameLauncherPrivateState.MaximumCategories)
            return Reset(out reset);

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var result = new GameLauncherCategory[categories.Count];
        for (var index = 0; index < categories.Count; index++)
        {
            var category = categories[index];
            if (category is null || !ValidId(category.Id) || !ids.Add(category.Id) ||
                NormalizeName(category.Name) is not { } normalizedName ||
                !string.Equals(normalizedName, category.Name, StringComparison.Ordinal) ||
                !names.Add(normalizedName) || category.SavedIds is null ||
                category.SavedIds.Count > GameLauncherPrivateState.MaximumCategoryMembers)
                return Reset(out reset);

            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (var savedId in category.SavedIds)
                if (!display.ContainsKey(savedId) || !members.Add(savedId))
                    return Reset(out reset);
            total += members.Count;
            if (total > GameLauncherPrivateState.MaximumCategoryMemberships)
                return Reset(out reset);
            result[index] = category with { SavedIds = category.SavedIds.ToArray() };
        }
        return result;
    }

    internal static string NewId() => "category." + Guid.NewGuid().ToString("N");

    internal static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length is > 0 and <= GameLauncherPrivateState.MaximumCategoryNameLength &&
            !normalized.Any(char.IsControl) ? normalized : null;
    }

    internal static GameLauncherStateMutation Create(
        GameLauncherPrivateState state,
        string id,
        string name)
    {
        state = GameLauncherOrganizationPolicy.Normalize(state);
        var normalized = NormalizeName(name);
        if (!ValidId(id) || normalized is null ||
            state.Categories.Count >= GameLauncherPrivateState.MaximumCategories ||
            state.Categories.Any(category => string.Equals(
                category.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            return GameLauncherStateMutation.Reject(state);
        return ApplyIfFits(state, state with
        {
            Categories = state.Categories.Append(
                new GameLauncherCategory(id, normalized, [])).ToArray(),
        });
    }

    internal static GameLauncherStateMutation Rename(
        GameLauncherPrivateState state,
        string id,
        string name)
    {
        state = GameLauncherOrganizationPolicy.Normalize(state);
        var normalized = NormalizeName(name);
        if (normalized is null || state.Categories.Any(category =>
                category.Id != id && string.Equals(
                    category.Name, normalized, StringComparison.OrdinalIgnoreCase)))
            return GameLauncherStateMutation.Reject(state);
        var changed = false;
        var categories = state.Categories.Select(category =>
        {
            if (category.Id != id || category.Name == normalized) return category;
            changed = true;
            return category with { Name = normalized };
        }).ToArray();
        return changed
            ? ApplyIfFits(state, state with { Categories = categories })
            : GameLauncherStateMutation.Reject(state);
    }

    internal static GameLauncherStateMutation Delete(
        GameLauncherPrivateState state,
        string id)
    {
        state = GameLauncherOrganizationPolicy.Normalize(state);
        var categories = state.Categories.Where(category => category.Id != id).ToArray();
        return categories.Length == state.Categories.Count
            ? GameLauncherStateMutation.Reject(state)
            : GameLauncherStateMutation.Apply(state with { Categories = categories });
    }

    internal static GameLauncherStateMutation SetMembership(
        GameLauncherPrivateState state,
        string id,
        GameLauncherDisplayItem display,
        bool included)
    {
        state = GameLauncherOrganizationPolicy.Normalize(state);
        var category = state.Categories.FirstOrDefault(candidate => candidate.Id == id);
        if (category is null) return GameLauncherStateMutation.Reject(state);
        var members = category.SavedIds.ToList();
        var contains = members.Contains(display.SavedId, StringComparer.Ordinal);
        if (contains == included) return GameLauncherStateMutation.Reject(state);
        if (included && (members.Count >= GameLauncherPrivateState.MaximumCategoryMembers ||
            state.Categories.Sum(candidate => candidate.SavedIds.Count) >=
                GameLauncherPrivateState.MaximumCategoryMemberships))
            return GameLauncherStateMutation.Reject(state);
        if (included) members.Add(display.SavedId); else members.Remove(display.SavedId);
        var withDisplay = WithDisplay(state, display);
        return ApplyIfFits(state, withDisplay with
        {
            Categories = withDisplay.Categories.Select(candidate => candidate.Id == id
                ? candidate with { SavedIds = members.ToArray() }
                : candidate).ToArray(),
        });
    }

    internal static GameLauncherCategory? Find(GameLauncherPrivateState state, string? id) =>
        id is null ? null : state.Categories.FirstOrDefault(category => category.Id == id);

    internal static bool Contains(GameLauncherCategory category, string savedId) =>
        category.SavedIds.Contains(savedId, StringComparer.Ordinal);

    internal static string? Cycle(
        IReadOnlyList<GameLauncherCategory> categories,
        string? currentCategoryId,
        GameLauncherCollectionDirection direction)
    {
        ArgumentNullException.ThrowIfNull(categories);
        if (categories.Count == 0) return null;
        var current = 0;
        if (currentCategoryId is not null)
            for (var index = 0; index < categories.Count; index++)
                if (categories[index].Id == currentCategoryId)
                {
                    current = index + 1;
                    break;
                }
        var count = categories.Count + 1;
        var next = direction == GameLauncherCollectionDirection.Next
            ? (current + 1) % count
            : (current + count - 1) % count;
        return next == 0 ? null : categories[next - 1].Id;
    }

    private static GameLauncherPrivateState WithDisplay(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem display)
    {
        var items = state.Items.Where(item => item.SavedId != display.SavedId)
            .Prepend(display).ToList();
        while (items.Count > GameLauncherPrivateState.MaximumItems)
        {
            var referenced = GameLauncherOrganizationPolicy.ReferencedSavedIds(state)
                .Append(display.SavedId).ToHashSet(StringComparer.Ordinal);
            var removable = items.FindLastIndex(item => !referenced.Contains(item.SavedId));
            if (removable < 0) return state;
            items.RemoveAt(removable);
        }
        return state with { Items = items };
    }

    private static IReadOnlyList<GameLauncherCategory> Reset(out bool reset)
    {
        reset = true;
        return [];
    }

    private static GameLauncherStateMutation ApplyIfFits(
        GameLauncherPrivateState baseline,
        GameLauncherPrivateState candidate) =>
        JsonSerializer.SerializeToUtf8Bytes(candidate).Length <=
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes
            ? GameLauncherStateMutation.Apply(candidate)
            : GameLauncherStateMutation.Reject(baseline);

    private static bool ValidId(string? value) => value is { Length: 41 } &&
        value.StartsWith("category.", StringComparison.Ordinal) &&
        value.AsSpan(9).IndexOfAnyExcept("0123456789abcdef") < 0;
}
