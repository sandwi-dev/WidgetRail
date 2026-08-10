using GameBarAlternative.WidgetSdk;

namespace GameBarAlternative.FirstPartyWidgets.GamesApps;

internal sealed record GamesAppsLibraryState(
    int Version,
    IReadOnlyList<string> SavedIds,
    string? SelectedSavedId)
{
    public IReadOnlyList<string> AutoGameSavedIds { get; init; } = [];
    public IReadOnlyList<string> ExcludedGameSavedIds { get; init; } = [];
}

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
    // With 128-character opaque IDs, 64 saved IDs, and the 64-entry automatic
    // provenance subset, 320 exclusions keep schema-v2 state below 64 KiB.
    internal const int MaximumExcludedGames = 320;

    internal static GamesAppsLibraryState Normalize(GamesAppsLibraryState? state)
    {
        if (state is null || state.Version is not (1 or 2) || state.SavedIds is null)
            return new GamesAppsLibraryState(2, [], null);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var saved = state.SavedIds
            .Where(id => IsOpaqueId(id) && seen.Add(id))
            .Take(MaximumCuratedItems)
            .ToArray();
        var excludedSeen = new HashSet<string>(StringComparer.Ordinal);
        var excluded = (state.ExcludedGameSavedIds ?? [])
            .Where(id => IsOpaqueId(id) && excludedSeen.Add(id))
            .Take(MaximumExcludedGames)
            .ToArray();
        var excludedSet = excluded.ToHashSet(StringComparer.Ordinal);
        saved = saved.Where(id => !excludedSet.Contains(id)).ToArray();
        var savedSet = saved.ToHashSet(StringComparer.Ordinal);
        var autoSeen = new HashSet<string>(StringComparer.Ordinal);
        var automatic = state.Version == 1
            ? Array.Empty<string>()
            : (state.AutoGameSavedIds ?? [])
                .Where(id => savedSet.Contains(id) && autoSeen.Add(id))
                .Take(MaximumCuratedItems)
                .ToArray();
        var selected = state.SelectedSavedId is { } candidate &&
                       saved.Contains(candidate, StringComparer.Ordinal)
            ? candidate
            : saved.FirstOrDefault();
        return new GamesAppsLibraryState(2, saved, selected)
        {
            AutoGameSavedIds = automatic,
            ExcludedGameSavedIds = excluded,
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
        var liveIds = visible.Select(item => item.SavedId).ToHashSet(StringComparer.Ordinal);
        var selected = liveSelectedSavedId is not null && liveIds.Contains(liveSelectedSavedId)
            ? liveSelectedSavedId
            : state.SelectedSavedId is not null && liveIds.Contains(state.SelectedSavedId)
                ? state.SelectedSavedId
                : visible.FirstOrDefault()?.SavedId;
        var desired = new GamesAppsLibraryState(2, savedIds, selected)
        {
            AutoGameSavedIds = automaticIds,
            ExcludedGameSavedIds = state.ExcludedGameSavedIds,
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
        var selected = !string.Equals(
                baseline.SelectedSavedId, desired.SelectedSavedId, StringComparison.Ordinal)
            ? desired.SelectedSavedId
            : latest.SelectedSavedId;
        return new GamesAppsLibraryMergeResult(
            Normalize(new GamesAppsLibraryState(2, mergedSaved, selected)
            {
                AutoGameSavedIds = mergedAutomatic,
                ExcludedGameSavedIds = mergedExcluded,
            }),
            Accepted: true);
    }

    internal static bool IsOpaqueId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '.' or '_' or '-');

    private static bool EquivalentRawState(
        GamesAppsLibraryState? raw,
        GamesAppsLibraryState desired) =>
        raw?.Version == 2 && raw.SavedIds is not null &&
        raw.SavedIds.SequenceEqual(desired.SavedIds, StringComparer.Ordinal) &&
        (raw.AutoGameSavedIds ?? []).SequenceEqual(
            desired.AutoGameSavedIds, StringComparer.Ordinal) &&
        (raw.ExcludedGameSavedIds ?? []).SequenceEqual(
            desired.ExcludedGameSavedIds, StringComparer.Ordinal) &&
        string.Equals(raw.SelectedSavedId, desired.SelectedSavedId, StringComparison.Ordinal);

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
