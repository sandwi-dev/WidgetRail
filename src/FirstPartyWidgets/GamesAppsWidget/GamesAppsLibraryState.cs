using GameBarAlternative.WidgetSdk;
using System.Text;

namespace GameBarAlternative.FirstPartyWidgets.GamesApps;

internal sealed record GamesAppsLibraryState(
    int Version,
    IReadOnlyList<string> SavedIds,
    string? SelectedSavedId)
{
    public IReadOnlyList<string> AutoGameSavedIds { get; init; } = [];
    public IReadOnlyList<string> ExcludedGameSavedIds { get; init; } = [];
    public IReadOnlyList<GamesAppsPersistedDisplayItem> DisplayItems { get; init; } = [];
}

internal sealed record GamesAppsPersistedDisplayItem(
    string SavedId,
    string DisplayName,
    WidgetAppLibraryKind Kind);

internal sealed record GamesAppsLibraryReconciliation(
    GamesAppsLibraryState State,
    IReadOnlyList<WidgetAppLibraryItem> VisibleItems,
    IReadOnlyList<string> AddedGameSavedIds,
    bool StateChanged);

internal sealed record GamesAppsLibraryMergeResult(
    GamesAppsLibraryState State,
    bool Accepted);

/// <summary>
/// Pure, bounded user-policy reconciliation for the trusted app catalog.
/// Provider classification and launch authority remain outside this type.
/// </summary>
internal static class GamesAppsLibraryStateReconciler
{
    internal const int MaximumCuratedItems = WidgetAppLibraryService.MaximumSavedItems;
    // Schema v3 retains a bounded display projection. With worst-case
    // 128-character opaque IDs and escaped Unicode display copy, 128 exclusions
    // keep the complete state below the SDK's 64 KiB private-state ceiling.
    internal const int MaximumExcludedGames = 128;
    internal const int MaximumPersistedDisplayRunes = 20;

    internal static GamesAppsLibraryState Normalize(GamesAppsLibraryState? state)
    {
        if (!IsValidCurrentState(state)) return EmptyState();
        return new GamesAppsLibraryState(3, state!.SavedIds.ToArray(), state.SelectedSavedId)
        {
            AutoGameSavedIds = state.AutoGameSavedIds.ToArray(),
            ExcludedGameSavedIds = state.ExcludedGameSavedIds.ToArray(),
            DisplayItems = state.DisplayItems.ToArray(),
        };
    }

    internal static GamesAppsLibraryReconciliation Reconcile(
        GamesAppsLibraryState? persisted,
        IReadOnlyList<WidgetAppLibraryItem> catalog,
        string? liveSelectedSavedId)
    {
        var state = Normalize(persisted);
        var bySaved = catalog.ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var savedIds = state.SavedIds.Take(MaximumCuratedItems).ToList();
        var savedSet = savedIds.ToHashSet(StringComparer.Ordinal);
        var automaticIds = state.AutoGameSavedIds.Where(savedSet.Contains).ToList();
        var automaticSet = automaticIds.ToHashSet(StringComparer.Ordinal);
        var excluded = state.ExcludedGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var added = new List<string>();
        foreach (var item in catalog)
        {
            if (savedIds.Count >= MaximumCuratedItems) break;
            if (item.Kind != WidgetAppLibraryKind.Game ||
                excluded.Contains(item.SavedId) || !savedSet.Add(item.SavedId))
                continue;
            savedIds.Add(item.SavedId);
            automaticIds.Add(item.SavedId);
            automaticSet.Add(item.SavedId);
            added.Add(item.SavedId);
        }
        var visible = savedIds.Where(savedId =>
                bySaved.TryGetValue(savedId, out var item) &&
                (!automaticSet.Contains(savedId) || item.Kind == WidgetAppLibraryKind.Game))
            .Select(savedId => bySaved[savedId]).ToArray();
        var displayBySaved = state.DisplayItems.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        foreach (var savedId in savedIds)
        {
            if (!bySaved.TryGetValue(savedId, out var item)) continue;
            if (automaticSet.Contains(savedId) && item.Kind != WidgetAppLibraryKind.Game)
            {
                displayBySaved.Remove(savedId);
                continue;
            }
            displayBySaved[savedId] = ToDisplayItem(item);
        }
        var display = savedIds.Where(displayBySaved.ContainsKey)
            .Select(savedId => displayBySaved[savedId])
            .ToArray();
        var presentedIds = display.Select(item => item.SavedId)
            .ToHashSet(StringComparer.Ordinal);
        var selected = liveSelectedSavedId is not null && presentedIds.Contains(liveSelectedSavedId)
            ? liveSelectedSavedId
            : state.SelectedSavedId is not null && presentedIds.Contains(state.SelectedSavedId)
                ? state.SelectedSavedId
                : display.FirstOrDefault()?.SavedId;
        var desired = new GamesAppsLibraryState(3, savedIds, selected)
        {
            AutoGameSavedIds = automaticIds,
            ExcludedGameSavedIds = state.ExcludedGameSavedIds,
            DisplayItems = display,
        };
        return new GamesAppsLibraryReconciliation(
            desired,
            visible,
            added,
            !EquivalentRawState(persisted, desired));
    }

    internal static GamesAppsLibraryMergeResult Merge(
        GamesAppsLibraryState baseline,
        GamesAppsLibraryState desired,
        GamesAppsLibraryState latest)
    {
        baseline = Normalize(baseline);
        desired = Normalize(desired);
        latest = Normalize(latest);
        var baselineSaved = baseline.SavedIds.ToHashSet(StringComparer.Ordinal);
        var desiredSaved = desired.SavedIds.ToHashSet(StringComparer.Ordinal);
        var removedSaved = baselineSaved.Where(id => !desiredSaved.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        var addedSaved = desired.SavedIds.Where(id => !baselineSaved.Contains(id)).ToArray();
        var mergedSaved = latest.SavedIds.Where(id => !removedSaved.Contains(id)).ToList();
        var orderChanged = !baseline.SavedIds.Where(desiredSaved.Contains)
            .SequenceEqual(desired.SavedIds.Where(baselineSaved.Contains), StringComparer.Ordinal);
        if (orderChanged)
        {
            var present = mergedSaved.ToHashSet(StringComparer.Ordinal);
            var ordered = desired.SavedIds.Where(present.Contains).ToList();
            ordered.AddRange(mergedSaved.Where(id => !ordered.Contains(id, StringComparer.Ordinal)));
            mergedSaved = ordered;
        }
        foreach (var id in addedSaved)
            if (mergedSaved.Count < MaximumCuratedItems &&
                !mergedSaved.Contains(id, StringComparer.Ordinal))
                mergedSaved.Add(id);

        var mergedAutomatic = MergeListByDelta(
            baseline.AutoGameSavedIds, desired.AutoGameSavedIds,
            latest.AutoGameSavedIds, MaximumCuratedItems);
        var mergedExcluded = MergeListByDelta(
            baseline.ExcludedGameSavedIds, desired.ExcludedGameSavedIds,
            latest.ExcludedGameSavedIds, MaximumExcludedGames);
        var requestedExclusions = desired.ExcludedGameSavedIds
            .Where(id => !baseline.ExcludedGameSavedIds.Contains(id, StringComparer.Ordinal))
            .ToArray();
        if (requestedExclusions.Any(id =>
                !mergedExcluded.Contains(id, StringComparer.Ordinal)))
            return new GamesAppsLibraryMergeResult(latest, Accepted: false);
        var mergedExcludedSet = mergedExcluded.ToHashSet(StringComparer.Ordinal);
        mergedSaved.RemoveAll(mergedExcludedSet.Contains);
        var mergedSavedSet = mergedSaved.ToHashSet(StringComparer.Ordinal);
        mergedAutomatic = mergedAutomatic.Where(mergedSavedSet.Contains).ToArray();
        var selected = !string.Equals(
                baseline.SelectedSavedId, desired.SelectedSavedId, StringComparison.Ordinal)
            ? desired.SelectedSavedId
            : latest.SelectedSavedId;
        if (selected is not null && !mergedSavedSet.Contains(selected))
            selected = mergedSaved.FirstOrDefault();
        return new GamesAppsLibraryMergeResult(
            Normalize(new GamesAppsLibraryState(3, mergedSaved, selected)
            {
                AutoGameSavedIds = mergedAutomatic,
                ExcludedGameSavedIds = mergedExcluded,
                DisplayItems = MergeDisplayItems(
                    baseline, desired, latest, mergedSaved),
            }),
            Accepted: true);
    }

    private static GamesAppsLibraryState EmptyState() => new(3, [], null);

    private static bool IsValidCurrentState(GamesAppsLibraryState? state)
    {
        if (state is null || state.Version != 3 || state.SavedIds is null ||
            state.AutoGameSavedIds is null || state.ExcludedGameSavedIds is null ||
            state.DisplayItems is null || state.SavedIds.Count > MaximumCuratedItems ||
            state.AutoGameSavedIds.Count > MaximumCuratedItems ||
            state.ExcludedGameSavedIds.Count > MaximumExcludedGames ||
            state.DisplayItems.Count > MaximumCuratedItems)
            return false;

        var saved = new HashSet<string>(StringComparer.Ordinal);
        if (state.SavedIds.Any(id => !IsOpaqueId(id) || !saved.Add(id))) return false;
        var automatic = new HashSet<string>(StringComparer.Ordinal);
        if (state.AutoGameSavedIds.Any(id => !saved.Contains(id) || !automatic.Add(id)))
            return false;
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        if (state.ExcludedGameSavedIds.Any(id => !IsOpaqueId(id) ||
                saved.Contains(id) || !excluded.Add(id)))
            return false;
        if (state.SelectedSavedId is { } selected && !saved.Contains(selected)) return false;

        var displayed = new HashSet<string>(StringComparer.Ordinal);
        return state.DisplayItems.All(item => item is not null &&
            saved.Contains(item.SavedId) && displayed.Add(item.SavedId) &&
            IsDisplayName(item.DisplayName) &&
            string.Equals(item.DisplayName, NormalizeDisplayName(item.DisplayName),
                StringComparison.Ordinal) &&
            Enum.IsDefined(item.Kind));
    }

    internal static bool IsOpaqueId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or '-');

    internal static GamesAppsPersistedDisplayItem ToDisplayItem(
        WidgetAppLibraryItem item) => new(
            item.SavedId,
            NormalizeDisplayName(item.DisplayName),
            item.Kind);

    private static bool EquivalentRawState(
        GamesAppsLibraryState? raw,
        GamesAppsLibraryState desired) =>
        raw?.Version == 3 && raw.SavedIds is not null &&
        raw.SavedIds.SequenceEqual(desired.SavedIds, StringComparer.Ordinal) &&
        (raw.AutoGameSavedIds ?? []).SequenceEqual(
            desired.AutoGameSavedIds, StringComparer.Ordinal) &&
        (raw.ExcludedGameSavedIds ?? []).SequenceEqual(
            desired.ExcludedGameSavedIds, StringComparer.Ordinal) &&
        (raw.DisplayItems ?? []).SequenceEqual(desired.DisplayItems) &&
        string.Equals(raw.SelectedSavedId, desired.SelectedSavedId, StringComparison.Ordinal);

    private static IReadOnlyList<GamesAppsPersistedDisplayItem> MergeDisplayItems(
        GamesAppsLibraryState baseline,
        GamesAppsLibraryState desired,
        GamesAppsLibraryState latest,
        IReadOnlyList<string> mergedSavedIds)
    {
        var baselineById = baseline.DisplayItems.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        var desiredById = desired.DisplayItems.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        var latestById = latest.DisplayItems.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        var merged = new List<GamesAppsPersistedDisplayItem>(mergedSavedIds.Count);
        foreach (var savedId in mergedSavedIds)
        {
            baselineById.TryGetValue(savedId, out var baselineItem);
            desiredById.TryGetValue(savedId, out var desiredItem);
            latestById.TryGetValue(savedId, out var latestItem);
            var changed = !Equals(baselineItem, desiredItem);
            var selected = changed ? desiredItem : latestItem;
            if (selected is not null) merged.Add(selected);
        }
        return merged;
    }

    private static bool IsDisplayName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Trim().Length <= 120 &&
        value.All(character => !char.IsControl(character));

    private static string NormalizeDisplayName(string value)
    {
        var normalized = value.Trim();
        var builder = new StringBuilder(normalized.Length);
        foreach (var rune in normalized.EnumerateRunes().Take(MaximumPersistedDisplayRunes))
            builder.Append(rune);
        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<string> MergeListByDelta(
        IReadOnlyList<string> baseline,
        IReadOnlyList<string> desired,
        IReadOnlyList<string> latest,
        int maximum)
    {
        var baselineSet = baseline.ToHashSet(StringComparer.Ordinal);
        var desiredSet = desired.ToHashSet(StringComparer.Ordinal);
        var removed = baselineSet.Where(id => !desiredSet.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        var merged = latest.Where(id => !removed.Contains(id)).ToList();
        foreach (var id in desired.Where(id => !baselineSet.Contains(id)))
            if (merged.Count < maximum && !merged.Contains(id, StringComparer.Ordinal))
                merged.Add(id);
        return merged;
    }
}
