using WidgetRail.WidgetSdk;
using System.Text.Json;

namespace WidgetRail.Samples.PlayniteLibrary;

internal sealed record PlayniteLibraryVariantGroup(
    string Id,
    IReadOnlyList<string> SavedIds,
    string PreferredSavedId);

internal sealed record PlayniteLibraryCategory(
    string Id,
    string Name,
    IReadOnlyList<string> SavedIds);

internal sealed record PlayniteLibraryTitleOverride(string SavedId, string Title);

internal sealed record PlayniteLibraryPrivateState(
    int Version,
    IReadOnlyList<PlayniteLibraryDisplayItem> Items)
{
    internal const int CurrentVersion = 5;
    internal const int MaximumItems = 1024;
    internal const int MaximumOrganizedItems = 32;
    internal const int MaximumRecentItems = 32;
    internal const int MaximumManualItems = 32;
    internal const int MaximumExcludedItems = 32;
    internal const int MaximumGroups = 16;
    internal const int MaximumVariantsPerGroup = 4;
    internal const int MaximumCategories = 64;
    internal const int MaximumCategoryNameLength = 32;
    internal const int MaximumCategoryMembers = 512;
    internal const int MaximumCategoryMemberships = 2048;
    internal const int MaximumTitleValidationItems = 1024;
    internal const int MaximumTitleLength = 96;
    internal const int MaximumProvenSources = 32;
    internal const int MaximumSourceNameLength = 64;
    internal static readonly PlayniteLibraryPrivateState Empty = new(CurrentVersion, []);

    public IReadOnlyList<string> FavoriteSavedIds { get; init; } = [];
    public IReadOnlyList<PlayniteLibraryVariantGroup> VariantGroups { get; init; } = [];
    public IReadOnlyList<string> RecentSavedIds { get; init; } = [];
    public IReadOnlyList<string> ManualSavedIds { get; init; } = [];
    public IReadOnlyList<string> ExcludedSavedIds { get; init; } = [];
    public IReadOnlyList<PlayniteLibraryCategory> Categories { get; init; } = [];
    public IReadOnlyList<PlayniteLibraryTitleOverride> TitleOverrides { get; init; } = [];
    public IReadOnlyList<string> ProvenSources { get; init; } = [];
}

internal sealed record PlayniteLibraryStateMutation(
    bool Accepted,
    PlayniteLibraryPrivateState State)
{
    internal static PlayniteLibraryStateMutation Apply(PlayniteLibraryPrivateState state) =>
        new(true, state);
    internal static PlayniteLibraryStateMutation Reject(PlayniteLibraryPrivateState state) =>
        new(false, state);
}

internal static class PlayniteLibraryOrganizationPolicy
{
    internal static PlayniteLibraryPrivateState Normalize(PlayniteLibraryPrivateState? state)
    {
        if (state is null || state.Version != PlayniteLibraryPrivateState.CurrentVersion ||
            state.Items is null || state.FavoriteSavedIds is null ||
            state.VariantGroups is null || state.RecentSavedIds is null ||
            state.ManualSavedIds is null || state.ExcludedSavedIds is null ||
            state.Items.Count > PlayniteLibraryPrivateState.MaximumItems ||
            state.FavoriteSavedIds.Count > PlayniteLibraryPrivateState.MaximumOrganizedItems ||
            state.VariantGroups.Count > PlayniteLibraryPrivateState.MaximumGroups ||
            state.RecentSavedIds.Count > PlayniteLibraryPrivateState.MaximumRecentItems ||
            state.ManualSavedIds.Count > PlayniteLibraryPrivateState.MaximumManualItems ||
            state.ExcludedSavedIds.Count > PlayniteLibraryPrivateState.MaximumExcludedItems)
            return PlayniteLibraryPrivateState.Empty;

        var display = new Dictionary<string, PlayniteLibraryDisplayItem>(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (!ValidDisplay(item) || !display.TryAdd(item.SavedId, item))
                return PlayniteLibraryPrivateState.Empty;

        var favorites = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.FavoriteSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !favorites.Add(savedId))
                return PlayniteLibraryPrivateState.Empty;

        var grouped = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in state.VariantGroups)
        {
            if (group is null || !ValidGroupId(group.Id) || !groupIds.Add(group.Id) ||
                group.SavedIds is null || group.SavedIds.Count is < 2 or
                    > PlayniteLibraryPrivateState.MaximumVariantsPerGroup ||
                !group.SavedIds.Contains(group.PreferredSavedId, StringComparer.Ordinal))
                return PlayniteLibraryPrivateState.Empty;
            foreach (var savedId in group.SavedIds)
                if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                    !grouped.Add(savedId))
                    return PlayniteLibraryPrivateState.Empty;
        }

        if (favorites.Union(grouped, StringComparer.Ordinal).Count() >
            PlayniteLibraryPrivateState.MaximumOrganizedItems)
            return PlayniteLibraryPrivateState.Empty;
        var recent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.RecentSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !recent.Add(savedId))
                return PlayniteLibraryPrivateState.Empty;
        var manual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.ManualSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !manual.Add(savedId))
                return PlayniteLibraryPrivateState.Empty;
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.ExcludedSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !excluded.Add(savedId))
                return PlayniteLibraryPrivateState.Empty;
        var provenSources = PlayniteLibrarySourceCatalog.Normalize(state.ProvenSources);
        var categories = PlayniteLibraryCategoryPolicy.Normalize(
            state.Categories, display, out _);
        var titleOverrides = PlayniteLibraryTitlePolicy.Normalize(
            state.TitleOverrides, display, out _);
        var normalized = state with
        {
            Items = state.Items.ToArray(),
            FavoriteSavedIds = state.FavoriteSavedIds.ToArray(),
            VariantGroups = state.VariantGroups.Select(group => group with
            {
                SavedIds = group.SavedIds.ToArray(),
            }).ToArray(),
            RecentSavedIds = state.RecentSavedIds.ToArray(),
            ManualSavedIds = state.ManualSavedIds.ToArray(),
            ExcludedSavedIds = state.ExcludedSavedIds.ToArray(),
            Categories = categories,
            TitleOverrides = titleOverrides,
            ProvenSources = provenSources,
        };
        if (JsonSerializer.SerializeToUtf8Bytes(normalized).Length >
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes)
            normalized = normalized with { TitleOverrides = [] };
        if (JsonSerializer.SerializeToUtf8Bytes(normalized).Length >
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes)
            normalized = normalized with { Categories = [] };
        if (JsonSerializer.SerializeToUtf8Bytes(normalized).Length >
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes)
            normalized = normalized with { ProvenSources = [] };
        return JsonSerializer.SerializeToUtf8Bytes(normalized).Length <=
            WidgetCommunityPlatformLimits.MaximumPrivateStateUtf8Bytes
            ? normalized
            : PlayniteLibraryPrivateState.Empty;
    }

    internal static PlayniteLibraryPrivateState ProjectPage(
        PlayniteLibraryPrivateState state,
        IReadOnlyList<PlayniteLibraryItem> items)
    {
        state = Normalize(state);
        var referenced = ReferencedSavedIds(state).ToHashSet(StringComparer.Ordinal);
        var result = items.Select(Display).ToList();
        var present = result.Select(item => item.SavedId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (referenced.Contains(item.SavedId) && present.Add(item.SavedId))
                result.Add(item);
        for (var index = result.Count - 1;
             index >= 0 && result.Count > PlayniteLibraryPrivateState.MaximumItems;
             index--)
            if (!referenced.Contains(result[index].SavedId)) result.RemoveAt(index);
        return Normalize(state with { Items = result.ToArray() });
    }

    internal static PlayniteLibraryStateMutation SetFavorite(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem display,
        bool favorite)
    {
        state = Normalize(state);
        var favorites = state.FavoriteSavedIds.ToList();
        var contains = favorites.Contains(display.SavedId, StringComparer.Ordinal);
        if (favorite && !contains) favorites.Add(display.SavedId);
        if (!favorite && contains) favorites.Remove(display.SavedId);
        var candidate = RetainDisplay(state, display) with { FavoriteSavedIds = favorites };
        return AcceptIfBounded(state, candidate);
    }

    internal static PlayniteLibraryStateMutation RecordRecent(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem display)
    {
        state = RetainDisplay(Normalize(state), display);
        var recent = state.RecentSavedIds.Where(savedId =>
                !string.Equals(savedId, display.SavedId, StringComparison.Ordinal))
            .Prepend(display.SavedId)
            .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
            .ToArray();
        return AcceptIfBounded(state, state with { RecentSavedIds = recent });
    }

    internal static PlayniteLibraryStateMutation ClearRecent(PlayniteLibraryPrivateState state) =>
        PlayniteLibraryStateMutation.Apply(Normalize(state) with { RecentSavedIds = [] });

    internal static PlayniteLibraryStateMutation RestoreRecent(
        PlayniteLibraryPrivateState state,
        IReadOnlyList<string> savedIds)
    {
        state = Normalize(state);
        var retained = savedIds.Where(savedId => state.Items.Any(item =>
                string.Equals(item.SavedId, savedId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(PlayniteLibraryPrivateState.MaximumRecentItems)
            .ToArray();
        return PlayniteLibraryStateMutation.Apply(state with { RecentSavedIds = retained });
    }

    internal static PlayniteLibraryStateMutation SetManual(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem display,
        bool included)
    {
        state = Normalize(state);
        var manual = state.ManualSavedIds.ToList();
        var contains = manual.Contains(display.SavedId, StringComparer.Ordinal);
        if (included && !contains)
        {
            if (manual.Count >= PlayniteLibraryPrivateState.MaximumManualItems)
                return PlayniteLibraryStateMutation.Reject(state);
            manual.Add(display.SavedId);
        }
        if (!included && contains) manual.Remove(display.SavedId);
        if (included == contains) return PlayniteLibraryStateMutation.Reject(state);
        var candidate = RetainDisplay(state, display) with { ManualSavedIds = manual };
        return AcceptIfBounded(state, candidate);
    }

    internal static PlayniteLibraryStateMutation SetExcluded(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem display,
        bool excluded)
    {
        state = Normalize(state);
        var exclusions = state.ExcludedSavedIds.ToList();
        var contains = exclusions.Contains(display.SavedId, StringComparer.Ordinal);
        if (excluded && !contains)
        {
            if (exclusions.Count >= PlayniteLibraryPrivateState.MaximumExcludedItems)
                return PlayniteLibraryStateMutation.Reject(state);
            exclusions.Add(display.SavedId);
        }
        if (!excluded && contains) exclusions.Remove(display.SavedId);
        if (excluded == contains) return PlayniteLibraryStateMutation.Reject(state);
        var candidate = RetainDisplay(state, display) with { ExcludedSavedIds = exclusions };
        return AcceptIfBounded(state, candidate);
    }

    internal static PlayniteLibraryStateMutation RemoveAutomaticManualGames(
        PlayniteLibraryPrivateState state,
        IReadOnlySet<string> automaticGameSavedIds)
    {
        state = Normalize(state);
        if (automaticGameSavedIds.Count == 0) return PlayniteLibraryStateMutation.Reject(state);
        var retained = state.ManualSavedIds.Where(savedId =>
                !automaticGameSavedIds.Contains(savedId))
            .ToArray();
        return retained.Length == state.ManualSavedIds.Count
            ? PlayniteLibraryStateMutation.Reject(state)
            : PlayniteLibraryStateMutation.Apply(state with { ManualSavedIds = retained });
    }

    internal static PlayniteLibraryStateMutation Pair(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem first,
        PlayniteLibraryDisplayItem second)
    {
        state = Normalize(state);
        if (first.SavedId == second.SavedId) return PlayniteLibraryStateMutation.Reject(state);
        var groups = state.VariantGroups.ToList();
        var involved = groups.Where(group => group.SavedIds.Contains(
            first.SavedId, StringComparer.Ordinal) || group.SavedIds.Contains(
            second.SavedId, StringComparer.Ordinal)).ToArray();
        var members = involved.SelectMany(group => group.SavedIds)
            .Append(first.SavedId).Append(second.SavedId)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (members.Length > PlayniteLibraryPrivateState.MaximumVariantsPerGroup)
            return PlayniteLibraryStateMutation.Reject(state);
        groups.RemoveAll(group => involved.Contains(group));
        var id = involved.Select(group => group.Id).Order(StringComparer.Ordinal).FirstOrDefault() ??
            PlayniteLibraryIdentity.GroupId(first.SavedId, second.SavedId);
        groups.Add(new(id, members, second.SavedId));
        var candidate = RetainDisplay(RetainDisplay(state, first), second) with
        {
            VariantGroups = groups,
        };
        return AcceptIfBounded(state, candidate);
    }

    internal static PlayniteLibraryStateMutation Unmerge(
        PlayniteLibraryPrivateState state,
        string groupId,
        string savedId)
    {
        state = Normalize(state);
        var groups = new List<PlayniteLibraryVariantGroup>(state.VariantGroups.Count);
        var changed = false;
        foreach (var group in state.VariantGroups)
        {
            if (group.Id != groupId || !group.SavedIds.Contains(savedId, StringComparer.Ordinal))
            {
                groups.Add(group);
                continue;
            }
            changed = true;
            var members = group.SavedIds.Where(value => value != savedId).ToArray();
            if (members.Length >= 2)
                groups.Add(group with
                {
                    SavedIds = members,
                    PreferredSavedId = group.PreferredSavedId == savedId
                        ? members[0] : group.PreferredSavedId,
                });
        }
        return changed
            ? PlayniteLibraryStateMutation.Apply(Normalize(state with { VariantGroups = groups }))
            : PlayniteLibraryStateMutation.Reject(state);
    }

    internal static PlayniteLibraryStateMutation Prefer(
        PlayniteLibraryPrivateState state,
        string groupId,
        string savedId)
    {
        state = Normalize(state);
        var changed = false;
        var groups = state.VariantGroups.Select(group =>
        {
            if (group.Id != groupId || !group.SavedIds.Contains(savedId, StringComparer.Ordinal))
                return group;
            changed = true;
            return group with { PreferredSavedId = savedId };
        }).ToArray();
        return changed
            ? PlayniteLibraryStateMutation.Apply(state with { VariantGroups = groups })
            : PlayniteLibraryStateMutation.Reject(state);
    }

    internal static PlayniteLibraryStateMutation Clear(PlayniteLibraryPrivateState state) =>
        PlayniteLibraryStateMutation.Apply(Normalize(state) with
        {
            FavoriteSavedIds = [],
            VariantGroups = [],
        });

    internal static IReadOnlyList<string> ReferencedSavedIds(PlayniteLibraryPrivateState state) =>
        state.FavoriteSavedIds.Concat(state.VariantGroups.SelectMany(group => group.SavedIds))
            .Concat(state.RecentSavedIds)
            .Concat(state.ManualSavedIds)
            .Concat(state.ExcludedSavedIds)
            .Concat(state.Categories.SelectMany(category => category.SavedIds))
            .Concat(state.TitleOverrides.Select(title => title.SavedId))
            .Distinct(StringComparer.Ordinal).ToArray();

    internal static PlayniteLibraryVariantGroup? GroupFor(
        PlayniteLibraryPrivateState state,
        string savedId) => state.VariantGroups.FirstOrDefault(group =>
            group.SavedIds.Contains(savedId, StringComparer.Ordinal));

    internal static PlayniteLibraryStateMutation AcceptIfBounded(
        PlayniteLibraryPrivateState baseline,
        PlayniteLibraryPrivateState candidate)
    {
        var normalized = Normalize(candidate);
        return normalized == PlayniteLibraryPrivateState.Empty && candidate != PlayniteLibraryPrivateState.Empty
            ? PlayniteLibraryStateMutation.Reject(baseline)
            : PlayniteLibraryStateMutation.Apply(normalized);
    }

    internal static PlayniteLibraryPrivateState RetainDisplay(
        PlayniteLibraryPrivateState state,
        PlayniteLibraryDisplayItem display)
    {
        var items = state.Items.Where(item => item.SavedId != display.SavedId).ToList();
        items.Insert(0, display);
        if (items.Count > PlayniteLibraryPrivateState.MaximumItems)
        {
            var referenced = ReferencedSavedIds(state).Append(display.SavedId)
                .ToHashSet(StringComparer.Ordinal);
            for (var index = items.Count - 1;
                 index >= 0 && items.Count > PlayniteLibraryPrivateState.MaximumItems;
                 index--)
                if (!referenced.Contains(items[index].SavedId)) items.RemoveAt(index);
        }
        return state with { Items = items };
    }

    private static PlayniteLibraryDisplayItem Display(PlayniteLibraryItem item) => new(
        item.Value.SavedId,
        item.Presentation.DisplayName.Length <= 96
            ? item.Presentation.DisplayName : item.Presentation.DisplayName[..96],
        item.Presentation.Source.DisplayName.Length <= 64
            ? item.Presentation.Source.DisplayName :
                item.Presentation.Source.DisplayName[..64]);

    private static bool ValidDisplay(PlayniteLibraryDisplayItem? item) => item is not null &&
        ValidSavedId(item.SavedId) && item.DisplayName is { Length: > 0 and <= 96 } &&
        item.SourceAttribution is { Length: > 0 and <= 64 } &&
        !item.DisplayName.Any(char.IsControl) && !item.SourceAttribution.Any(char.IsControl);
    private static bool ValidSavedId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.StartsWith("saved-", StringComparison.Ordinal) && !value.Any(char.IsControl);
    private static bool ValidGroupId(string? value) => value is { Length: 28 } &&
        value.StartsWith("variant.", StringComparison.Ordinal) &&
        value.AsSpan(8).IndexOfAnyExcept("0123456789abcdef") < 0;
}

internal sealed record PlayniteLibraryStateSaveResult(
    bool Saved,
    PlayniteLibraryPrivateState State,
    long Revision);

internal static class PlayniteLibraryStateStore
{
    internal static async Task<PlayniteLibraryStateSaveResult> SaveAsync(
        Func<PlayniteLibraryPrivateState, PlayniteLibraryStateMutation> apply,
        Func<PlayniteLibraryPrivateState, long, CancellationToken,
            ValueTask<WidgetPrivateStateMutation>> writeAsync,
        Func<CancellationToken, ValueTask<WidgetPrivateStateValue<PlayniteLibraryPrivateState>>>
            readAsync,
        PlayniteLibraryPrivateState baseline,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var current = PlayniteLibraryOrganizationPolicy.Normalize(baseline);
        var revision = expectedRevision;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var mutation = apply(current);
            if (!mutation.Accepted) return new(false, current, revision);
            try
            {
                var written = await writeAsync(mutation.State, revision, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return new(true, mutation.State, written.Revision);
            }
            catch (WidgetCapabilityException exception) when (
                exception.ErrorCode == "state_conflict" && attempt == 0)
            {
                var latest = await readAsync(cancellationToken).ConfigureAwait(false);
                current = PlayniteLibraryOrganizationPolicy.Normalize(
                    latest.Exists ? latest.Value : null);
                revision = latest.Revision;
            }
        }
        throw new InvalidOperationException("Private-state retry bound was exceeded.");
    }
}
