using GameBarAlternative.WidgetSdk;
using System.Text.Json;

namespace GameBarAlternative.FirstPartyWidgets.GameLauncher;

internal sealed record GameLauncherVariantGroup(
    string Id,
    IReadOnlyList<string> SavedIds,
    string PreferredSavedId);

internal sealed record GameLauncherCategory(
    string Id,
    string Name,
    IReadOnlyList<string> SavedIds);

internal sealed record GameLauncherTitleOverride(string SavedId, string Title);

internal sealed record GameLauncherPrivateState(
    int Version,
    IReadOnlyList<GameLauncherDisplayItem> Items)
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
    internal static readonly GameLauncherPrivateState Empty = new(CurrentVersion, []);

    public IReadOnlyList<string> FavoriteSavedIds { get; init; } = [];
    public IReadOnlyList<GameLauncherVariantGroup> VariantGroups { get; init; } = [];
    public IReadOnlyList<string> RecentSavedIds { get; init; } = [];
    public IReadOnlyList<string> ManualSavedIds { get; init; } = [];
    public IReadOnlyList<string> ExcludedSavedIds { get; init; } = [];
    public IReadOnlyList<GameLauncherCategory> Categories { get; init; } = [];
    public IReadOnlyList<GameLauncherTitleOverride> TitleOverrides { get; init; } = [];
    public string ExperienceId { get; init; } = GameLauncherExperienceIdentity.HeroRail;
    public IReadOnlyList<string> ProvenSources { get; init; } = [];
}

internal sealed record GameLauncherStateMutation(
    bool Accepted,
    GameLauncherPrivateState State)
{
    internal static GameLauncherStateMutation Apply(GameLauncherPrivateState state) =>
        new(true, state);
    internal static GameLauncherStateMutation Reject(GameLauncherPrivateState state) =>
        new(false, state);
}

internal static class GameLauncherOrganizationPolicy
{
    internal static GameLauncherPrivateState Normalize(GameLauncherPrivateState? state)
    {
        if (state is null || state.Version != GameLauncherPrivateState.CurrentVersion ||
            state.Items is null || state.FavoriteSavedIds is null ||
            state.VariantGroups is null || state.RecentSavedIds is null ||
            state.ManualSavedIds is null || state.ExcludedSavedIds is null ||
            state.Items.Count > GameLauncherPrivateState.MaximumItems ||
            state.FavoriteSavedIds.Count > GameLauncherPrivateState.MaximumOrganizedItems ||
            state.VariantGroups.Count > GameLauncherPrivateState.MaximumGroups ||
            state.RecentSavedIds.Count > GameLauncherPrivateState.MaximumRecentItems ||
            state.ManualSavedIds.Count > GameLauncherPrivateState.MaximumManualItems ||
            state.ExcludedSavedIds.Count > GameLauncherPrivateState.MaximumExcludedItems)
            return GameLauncherPrivateState.Empty;

        var display = new Dictionary<string, GameLauncherDisplayItem>(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (!ValidDisplay(item) || !display.TryAdd(item.SavedId, item))
                return GameLauncherPrivateState.Empty;

        var favorites = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.FavoriteSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !favorites.Add(savedId))
                return GameLauncherPrivateState.Empty;

        var grouped = new HashSet<string>(StringComparer.Ordinal);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in state.VariantGroups)
        {
            if (group is null || !ValidGroupId(group.Id) || !groupIds.Add(group.Id) ||
                group.SavedIds is null || group.SavedIds.Count is < 2 or
                    > GameLauncherPrivateState.MaximumVariantsPerGroup ||
                !group.SavedIds.Contains(group.PreferredSavedId, StringComparer.Ordinal))
                return GameLauncherPrivateState.Empty;
            foreach (var savedId in group.SavedIds)
                if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                    !grouped.Add(savedId))
                    return GameLauncherPrivateState.Empty;
        }

        if (favorites.Union(grouped, StringComparer.Ordinal).Count() >
            GameLauncherPrivateState.MaximumOrganizedItems)
            return GameLauncherPrivateState.Empty;
        var recent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.RecentSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !recent.Add(savedId))
                return GameLauncherPrivateState.Empty;
        var manual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.ManualSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !manual.Add(savedId))
                return GameLauncherPrivateState.Empty;
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var savedId in state.ExcludedSavedIds)
            if (!ValidSavedId(savedId) || !display.ContainsKey(savedId) ||
                !excluded.Add(savedId))
                return GameLauncherPrivateState.Empty;
        var experienceId = GameLauncherExperienceIdentity.IsValid(state.ExperienceId)
            ? state.ExperienceId
            : GameLauncherExperienceIdentity.HeroRail;
        var provenSources = GameLauncherSourceCatalog.Normalize(state.ProvenSources);
        var categories = GameLauncherCategoryPolicy.Normalize(
            state.Categories, display, out _);
        var titleOverrides = GameLauncherTitlePolicy.Normalize(
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
            ExperienceId = experienceId,
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
            : GameLauncherPrivateState.Empty;
    }

    internal static GameLauncherStateMutation SelectExperience(
        GameLauncherPrivateState state,
        GameLauncherExperience experience)
    {
        state = Normalize(state);
        var id = GameLauncherExperienceIdentity.Id(experience);
        return string.Equals(state.ExperienceId, id, StringComparison.Ordinal)
            ? GameLauncherStateMutation.Reject(state)
            : GameLauncherStateMutation.Apply(state with { ExperienceId = id });
    }

    internal static GameLauncherPrivateState ProjectPage(
        GameLauncherPrivateState state,
        IReadOnlyList<GameLauncherItem> items)
    {
        state = Normalize(state);
        var referenced = ReferencedSavedIds(state).ToHashSet(StringComparer.Ordinal);
        var result = items.Select(Display).ToList();
        var present = result.Select(item => item.SavedId).ToHashSet(StringComparer.Ordinal);
        foreach (var item in state.Items)
            if (referenced.Contains(item.SavedId) && present.Add(item.SavedId))
                result.Add(item);
        for (var index = result.Count - 1;
             index >= 0 && result.Count > GameLauncherPrivateState.MaximumItems;
             index--)
            if (!referenced.Contains(result[index].SavedId)) result.RemoveAt(index);
        return Normalize(state with { Items = result.ToArray() });
    }

    internal static GameLauncherStateMutation SetFavorite(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem display,
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

    internal static GameLauncherStateMutation RecordRecent(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem display)
    {
        state = RetainDisplay(Normalize(state), display);
        var recent = state.RecentSavedIds.Where(savedId =>
                !string.Equals(savedId, display.SavedId, StringComparison.Ordinal))
            .Prepend(display.SavedId)
            .Take(GameLauncherPrivateState.MaximumRecentItems)
            .ToArray();
        return AcceptIfBounded(state, state with { RecentSavedIds = recent });
    }

    internal static GameLauncherStateMutation ClearRecent(GameLauncherPrivateState state) =>
        GameLauncherStateMutation.Apply(Normalize(state) with { RecentSavedIds = [] });

    internal static GameLauncherStateMutation RestoreRecent(
        GameLauncherPrivateState state,
        IReadOnlyList<string> savedIds)
    {
        state = Normalize(state);
        var retained = savedIds.Where(savedId => state.Items.Any(item =>
                string.Equals(item.SavedId, savedId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .Take(GameLauncherPrivateState.MaximumRecentItems)
            .ToArray();
        return GameLauncherStateMutation.Apply(state with { RecentSavedIds = retained });
    }

    internal static GameLauncherStateMutation SetManual(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem display,
        bool included)
    {
        state = Normalize(state);
        var manual = state.ManualSavedIds.ToList();
        var contains = manual.Contains(display.SavedId, StringComparer.Ordinal);
        if (included && !contains)
        {
            if (manual.Count >= GameLauncherPrivateState.MaximumManualItems)
                return GameLauncherStateMutation.Reject(state);
            manual.Add(display.SavedId);
        }
        if (!included && contains) manual.Remove(display.SavedId);
        if (included == contains) return GameLauncherStateMutation.Reject(state);
        var candidate = RetainDisplay(state, display) with { ManualSavedIds = manual };
        return AcceptIfBounded(state, candidate);
    }

    internal static GameLauncherStateMutation SetExcluded(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem display,
        bool excluded)
    {
        state = Normalize(state);
        var exclusions = state.ExcludedSavedIds.ToList();
        var contains = exclusions.Contains(display.SavedId, StringComparer.Ordinal);
        if (excluded && !contains)
        {
            if (exclusions.Count >= GameLauncherPrivateState.MaximumExcludedItems)
                return GameLauncherStateMutation.Reject(state);
            exclusions.Add(display.SavedId);
        }
        if (!excluded && contains) exclusions.Remove(display.SavedId);
        if (excluded == contains) return GameLauncherStateMutation.Reject(state);
        var candidate = RetainDisplay(state, display) with { ExcludedSavedIds = exclusions };
        return AcceptIfBounded(state, candidate);
    }

    internal static GameLauncherStateMutation RemoveAutomaticManualGames(
        GameLauncherPrivateState state,
        IReadOnlySet<string> automaticGameSavedIds)
    {
        state = Normalize(state);
        if (automaticGameSavedIds.Count == 0) return GameLauncherStateMutation.Reject(state);
        var retained = state.ManualSavedIds.Where(savedId =>
                !automaticGameSavedIds.Contains(savedId))
            .ToArray();
        return retained.Length == state.ManualSavedIds.Count
            ? GameLauncherStateMutation.Reject(state)
            : GameLauncherStateMutation.Apply(state with { ManualSavedIds = retained });
    }

    internal static GameLauncherStateMutation Pair(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem first,
        GameLauncherDisplayItem second)
    {
        state = Normalize(state);
        if (first.SavedId == second.SavedId) return GameLauncherStateMutation.Reject(state);
        var groups = state.VariantGroups.ToList();
        var involved = groups.Where(group => group.SavedIds.Contains(
            first.SavedId, StringComparer.Ordinal) || group.SavedIds.Contains(
            second.SavedId, StringComparer.Ordinal)).ToArray();
        var members = involved.SelectMany(group => group.SavedIds)
            .Append(first.SavedId).Append(second.SavedId)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (members.Length > GameLauncherPrivateState.MaximumVariantsPerGroup)
            return GameLauncherStateMutation.Reject(state);
        groups.RemoveAll(group => involved.Contains(group));
        var id = involved.Select(group => group.Id).Order(StringComparer.Ordinal).FirstOrDefault() ??
            GameLauncherIdentity.GroupId(first.SavedId, second.SavedId);
        groups.Add(new(id, members, second.SavedId));
        var candidate = RetainDisplay(RetainDisplay(state, first), second) with
        {
            VariantGroups = groups,
        };
        return AcceptIfBounded(state, candidate);
    }

    internal static GameLauncherStateMutation Unmerge(
        GameLauncherPrivateState state,
        string groupId,
        string savedId)
    {
        state = Normalize(state);
        var groups = new List<GameLauncherVariantGroup>(state.VariantGroups.Count);
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
            ? GameLauncherStateMutation.Apply(Normalize(state with { VariantGroups = groups }))
            : GameLauncherStateMutation.Reject(state);
    }

    internal static GameLauncherStateMutation Prefer(
        GameLauncherPrivateState state,
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
            ? GameLauncherStateMutation.Apply(state with { VariantGroups = groups })
            : GameLauncherStateMutation.Reject(state);
    }

    internal static GameLauncherStateMutation Clear(GameLauncherPrivateState state) =>
        GameLauncherStateMutation.Apply(Normalize(state) with
        {
            FavoriteSavedIds = [],
            VariantGroups = [],
        });

    internal static IReadOnlyList<string> ReferencedSavedIds(GameLauncherPrivateState state) =>
        state.FavoriteSavedIds.Concat(state.VariantGroups.SelectMany(group => group.SavedIds))
            .Concat(state.RecentSavedIds)
            .Concat(state.ManualSavedIds)
            .Concat(state.ExcludedSavedIds)
            .Concat(state.Categories.SelectMany(category => category.SavedIds))
            .Concat(state.TitleOverrides.Select(title => title.SavedId))
            .Distinct(StringComparer.Ordinal).ToArray();

    internal static GameLauncherVariantGroup? GroupFor(
        GameLauncherPrivateState state,
        string savedId) => state.VariantGroups.FirstOrDefault(group =>
            group.SavedIds.Contains(savedId, StringComparer.Ordinal));

    internal static GameLauncherStateMutation AcceptIfBounded(
        GameLauncherPrivateState baseline,
        GameLauncherPrivateState candidate)
    {
        var normalized = Normalize(candidate);
        return normalized == GameLauncherPrivateState.Empty && candidate != GameLauncherPrivateState.Empty
            ? GameLauncherStateMutation.Reject(baseline)
            : GameLauncherStateMutation.Apply(normalized);
    }

    internal static GameLauncherPrivateState RetainDisplay(
        GameLauncherPrivateState state,
        GameLauncherDisplayItem display)
    {
        var items = state.Items.Where(item => item.SavedId != display.SavedId).ToList();
        items.Insert(0, display);
        if (items.Count > GameLauncherPrivateState.MaximumItems)
        {
            var referenced = ReferencedSavedIds(state).Append(display.SavedId)
                .ToHashSet(StringComparer.Ordinal);
            for (var index = items.Count - 1;
                 index >= 0 && items.Count > GameLauncherPrivateState.MaximumItems;
                 index--)
                if (!referenced.Contains(items[index].SavedId)) items.RemoveAt(index);
        }
        return state with { Items = items };
    }

    private static GameLauncherDisplayItem Display(GameLauncherItem item) => new(
        item.Value.SavedId,
        item.Presentation.DisplayName.Length <= 96
            ? item.Presentation.DisplayName : item.Presentation.DisplayName[..96],
        item.Presentation.Source.DisplayName.Length <= 64
            ? item.Presentation.Source.DisplayName :
                item.Presentation.Source.DisplayName[..64]);

    private static bool ValidDisplay(GameLauncherDisplayItem? item) => item is not null &&
        ValidSavedId(item.SavedId) && item.DisplayName is { Length: > 0 and <= 96 } &&
        item.SourceAttribution is { Length: > 0 and <= 64 } &&
        !item.DisplayName.Any(char.IsControl) && !item.SourceAttribution.Any(char.IsControl);
    private static bool ValidSavedId(string? value) => value is { Length: > 0 and <= 128 } &&
        value.StartsWith("saved-", StringComparison.Ordinal) && !value.Any(char.IsControl);
    private static bool ValidGroupId(string? value) => value is { Length: 28 } &&
        value.StartsWith("variant.", StringComparison.Ordinal) &&
        value.AsSpan(8).IndexOfAnyExcept("0123456789abcdef") < 0;
}

internal sealed record GameLauncherStateSaveResult(
    bool Saved,
    GameLauncherPrivateState State,
    long Revision);

internal static class GameLauncherStateStore
{
    internal static async Task<GameLauncherStateSaveResult> SaveAsync(
        Func<GameLauncherPrivateState, GameLauncherStateMutation> apply,
        Func<GameLauncherPrivateState, long, CancellationToken,
            ValueTask<WidgetPrivateStateMutation>> writeAsync,
        Func<CancellationToken, ValueTask<WidgetPrivateStateValue<GameLauncherPrivateState>>>
            readAsync,
        GameLauncherPrivateState baseline,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var current = GameLauncherOrganizationPolicy.Normalize(baseline);
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
                current = GameLauncherOrganizationPolicy.Normalize(
                    latest.Exists ? latest.Value : null);
                revision = latest.Revision;
            }
        }
        throw new InvalidOperationException("Private-state retry bound was exceeded.");
    }
}
