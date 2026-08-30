using System.Text;
using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

internal static class PlayniteLibraryTitlePolicy
{
    internal static bool RequiresReset(PlayniteLibraryPrivateState? state)
    {
        if (state?.Items is null) return false;
        var display = new Dictionary<string, PlayniteLibraryDisplayItem>(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (item?.SavedId is null || !display.TryAdd(item.SavedId, item)) return false;
        _ = Normalize(state.TitleOverrides, display, out var reset);
        return reset || state.TitleOverrides is { Count: > 0 } &&
            JsonSerializer.SerializeToUtf8Bytes(state).Length >
                WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes;
    }

    internal static IReadOnlyList<PlayniteLibraryTitleOverride> Normalize(
        IReadOnlyList<PlayniteLibraryTitleOverride>? overrides,
        IReadOnlyDictionary<string, PlayniteLibraryDisplayItem> display,
        out bool reset)
    {
        reset = false;
        if (overrides is null ||
            overrides.Count > PlayniteLibraryPrivateState.MaximumTitleValidationItems)
            return Reset(out reset);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var result = new PlayniteLibraryTitleOverride[overrides.Count];
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
        return normalized.Length is > 0 and <= PlayniteLibraryPrivateState.MaximumTitleLength &&
            !normalized.Any(char.IsControl) ? normalized : null;
    }

    internal static PlayniteLibraryStateMutation Set(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem providerDisplay,
        string? title)
    {
        state = PlayniteLibraryOrganizationPolicy.Normalize(state);
        var normalized = NormalizeTitle(title);
        var existing = state.TitleOverrides.FirstOrDefault(candidate =>
            candidate.SavedId == providerDisplay.SavedId);
        var clear = normalized is null || string.Equals(
            normalized, providerDisplay.DisplayName, StringComparison.Ordinal);
        if (clear && existing is null || !clear && existing?.Title == normalized)
            return PlayniteLibraryStateMutation.Reject(state);
        var retained = PlayniteLibraryOrganizationPolicy.RetainDisplay(state, providerDisplay);
        if (!retained.Items.Any(item => item.SavedId == providerDisplay.SavedId))
            return PlayniteLibraryStateMutation.Reject(state);
        var overrides = retained.TitleOverrides
            .Where(candidate => candidate.SavedId != providerDisplay.SavedId).ToList();
        if (!clear)
            overrides.Add(new(providerDisplay.SavedId, normalized!));
        var candidate = retained with { TitleOverrides = overrides };
        return JsonSerializer.SerializeToUtf8Bytes(candidate).Length <=
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes
            ? PlayniteLibraryStateMutation.Apply(candidate)
            : PlayniteLibraryStateMutation.Reject(state);
    }

    internal static string DisplayName(
        PlayniteLibraryPrivateState state,
        string savedId,
        string providerTitle) => state.TitleOverrides.FirstOrDefault(candidate =>
            candidate.SavedId == savedId)?.Title ?? providerTitle;

    internal static WidgetAppLibraryItem Project(
        PlayniteLibraryPrivateState state,
        WidgetAppLibraryItem item)
    {
        var title = DisplayName(state, item.SavedId, item.Presentation.DisplayName);
        return title == item.Presentation.DisplayName ? item : item with
        {
            Presentation = item.Presentation with { DisplayName = title },
        };
    }

    internal static PlayniteLibraryPrivateState Project(PlayniteLibraryPrivateState state) =>
        state with
        {
            Items = state.Items.Select(item => item with
            {
                DisplayName = DisplayName(state, item.SavedId, item.DisplayName),
            }).ToArray(),
        };

    internal static IReadOnlyList<string> SearchMatches(
        PlayniteLibraryPrivateState state,
        string? query) => string.IsNullOrWhiteSpace(query)
            ? []
            : state.TitleOverrides.Where(candidate => candidate.Title.Contains(
                    query, StringComparison.OrdinalIgnoreCase))
                .Select(candidate => candidate.SavedId).ToArray();

    private static IReadOnlyList<PlayniteLibraryTitleOverride> Reset(out bool reset)
    {
        reset = true;
        return [];
    }
}
