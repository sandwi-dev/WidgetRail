using WidgetRail.WidgetSdk;
using System.Security.Cryptography;
using System.Text;

namespace WidgetRail.FirstPartyWidgets.GamesApps;

internal sealed record GamesAppsLibraryState(
    int Version,
    IReadOnlyList<string> SavedIds,
    string? SelectedSavedId)
{
    public IReadOnlyList<string> AutoGameSavedIds { get; init; } = [];
    public IReadOnlyList<string> ExcludedGameSavedIds { get; init; } = [];
    public IReadOnlyList<GamesAppsPersistedDisplayItem> DisplayItems { get; init; } = [];
    public IReadOnlyList<string> RunningRegistrationSavedIds { get; init; } = [];
    public IReadOnlyList<string> PendingRunningRegistrationSavedIds { get; init; } = [];
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

internal enum GamesAppsLibraryMutationRejection
{
    None,
    LibraryFull,
    ExclusionStorageFull,
}

internal sealed record GamesAppsLibraryMutation(
    GamesAppsLibraryState State,
    GamesAppsLibraryMutationRejection Rejection)
{
    internal bool Accepted => Rejection == GamesAppsLibraryMutationRejection.None;
}

internal sealed record GamesAppsLibraryProjection(
    IReadOnlyList<WidgetAppLibraryItem> Items,
    IReadOnlySet<string> ResolvedSavedIds);

/// <summary>
/// Pure, bounded user-policy reconciliation for the trusted app catalog.
/// Provider classification and launch authority remain outside this type.
/// </summary>
internal static class GamesAppsLibraryPolicy
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
            RunningRegistrationSavedIds = state.RunningRegistrationSavedIds.ToArray(),
            PendingRunningRegistrationSavedIds =
                state.PendingRunningRegistrationSavedIds.ToArray(),
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
            if (GamesAppsAppLibraryPresentation.Kind(item) != WidgetAppLibraryKind.Game ||
                excluded.Contains(item.SavedId) || !savedSet.Add(item.SavedId))
                continue;
            savedIds.Add(item.SavedId);
            automaticIds.Add(item.SavedId);
            automaticSet.Add(item.SavedId);
            added.Add(item.SavedId);
        }
        var visible = savedIds.Where(savedId =>
                bySaved.TryGetValue(savedId, out var item) &&
                (!automaticSet.Contains(savedId) ||
                    GamesAppsAppLibraryPresentation.Kind(item) == WidgetAppLibraryKind.Game))
            .Select(savedId => bySaved[savedId]).ToArray();
        var displayBySaved = state.DisplayItems.ToDictionary(
            item => item.SavedId, StringComparer.Ordinal);
        foreach (var savedId in savedIds)
        {
            if (!bySaved.TryGetValue(savedId, out var item)) continue;
            if (automaticSet.Contains(savedId) &&
                GamesAppsAppLibraryPresentation.Kind(item) != WidgetAppLibraryKind.Game)
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
            RunningRegistrationSavedIds = state.RunningRegistrationSavedIds
                .Where(savedSet.Contains).ToArray(),
            PendingRunningRegistrationSavedIds =
                state.PendingRunningRegistrationSavedIds,
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
        if (addedSaved.Any(id => !mergedSaved.Contains(id, StringComparer.Ordinal)))
            return new GamesAppsLibraryMergeResult(latest, Accepted: false);

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
        var mergedRunningRegistrations = MergeListByDelta(
                baseline.RunningRegistrationSavedIds,
                desired.RunningRegistrationSavedIds,
                latest.RunningRegistrationSavedIds,
                MaximumCuratedItems)
            .Where(mergedSavedSet.Contains).ToArray();
        var mergedPendingRegistrations = MergeListByDelta(
                baseline.PendingRunningRegistrationSavedIds,
                desired.PendingRunningRegistrationSavedIds,
                latest.PendingRunningRegistrationSavedIds,
                MaximumCuratedItems);
        var requestedRegistrationAdditions = desired.RunningRegistrationSavedIds
            .Where(id => !baseline.RunningRegistrationSavedIds.Contains(
                id, StringComparer.Ordinal)).ToArray();
        var requestedPendingAdditions = desired.PendingRunningRegistrationSavedIds
            .Where(id => !baseline.PendingRunningRegistrationSavedIds.Contains(
                id, StringComparer.Ordinal)).ToArray();
        if (requestedRegistrationAdditions.Any(id =>
                !mergedRunningRegistrations.Contains(id, StringComparer.Ordinal)) ||
            requestedPendingAdditions.Any(id =>
                !mergedPendingRegistrations.Contains(id, StringComparer.Ordinal)))
            return new GamesAppsLibraryMergeResult(latest, Accepted: false);
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
                RunningRegistrationSavedIds = mergedRunningRegistrations,
                PendingRunningRegistrationSavedIds = mergedPendingRegistrations,
            }),
            Accepted: true);
    }

    internal static GamesAppsLibraryMutation Toggle(
        GamesAppsLibraryState persisted,
        WidgetAppLibraryItem item,
        bool isVisibleMember,
        IReadOnlyList<string> visibleSavedIds)
    {
        ArgumentNullException.ThrowIfNull(item);
        var state = Normalize(persisted);
        if (isVisibleMember)
            return Remove(state, item, visibleSavedIds);
        if (state.SavedIds.Count >= MaximumCuratedItems)
            return new GamesAppsLibraryMutation(
                state, GamesAppsLibraryMutationRejection.LibraryFull);

        var saved = state.SavedIds.ToList();
        var automatic = state.AutoGameSavedIds.ToList();
        var excluded = state.ExcludedGameSavedIds.ToList();
        excluded.Remove(item.SavedId);
        automatic.Remove(item.SavedId);
        if (!saved.Contains(item.SavedId, StringComparer.Ordinal)) saved.Add(item.SavedId);
        return new GamesAppsLibraryMutation(
            state with
            {
                SavedIds = saved,
                SelectedSavedId = item.SavedId,
                AutoGameSavedIds = automatic,
                ExcludedGameSavedIds = excluded,
            },
            GamesAppsLibraryMutationRejection.None);
    }

    internal static GamesAppsLibraryMutation Remove(
        GamesAppsLibraryState persisted,
        WidgetAppLibraryItem item,
        IReadOnlyList<string> visibleSavedIds)
    {
        ArgumentNullException.ThrowIfNull(item);
        var state = Normalize(persisted);
        if (!state.SavedIds.Contains(item.SavedId, StringComparer.Ordinal))
            return new GamesAppsLibraryMutation(
                state, GamesAppsLibraryMutationRejection.None);

        var excluded = state.ExcludedGameSavedIds.ToList();
        if (GamesAppsAppLibraryPresentation.Kind(item) == WidgetAppLibraryKind.Game &&
            !TryAddExcludedGame(excluded, item.SavedId))
            return new GamesAppsLibraryMutation(
                state, GamesAppsLibraryMutationRejection.ExclusionStorageFull);

        var saved = state.SavedIds.Where(id => !string.Equals(
            id, item.SavedId, StringComparison.Ordinal)).ToArray();
        var automatic = state.AutoGameSavedIds.Where(id => !string.Equals(
            id, item.SavedId, StringComparison.Ordinal)).ToArray();
        var display = state.DisplayItems.Where(candidate => !string.Equals(
            candidate.SavedId, item.SavedId, StringComparison.Ordinal)).ToArray();
        return new GamesAppsLibraryMutation(
            state with
            {
                SavedIds = saved,
                SelectedSavedId = NearestSurvivingSavedId(
                    item.SavedId, saved, visibleSavedIds),
                AutoGameSavedIds = automatic,
                ExcludedGameSavedIds = excluded,
                DisplayItems = display,
                RunningRegistrationSavedIds = state.RunningRegistrationSavedIds
                    .Where(id => !string.Equals(id, item.SavedId, StringComparison.Ordinal))
                    .ToArray(),
                PendingRunningRegistrationSavedIds =
                    state.PendingRunningRegistrationSavedIds.Where(id => !string.Equals(
                        id, item.SavedId, StringComparison.Ordinal)).ToArray(),
            },
            GamesAppsLibraryMutationRejection.None);
    }

    internal static GamesAppsLibraryState MoveToFront(
        GamesAppsLibraryState persisted,
        string savedId)
    {
        var state = Normalize(persisted);
        var saved = state.SavedIds.ToList();
        if (!saved.Remove(savedId)) return state;
        saved.Insert(0, savedId);
        return state with { SavedIds = saved, SelectedSavedId = savedId };
    }

    internal static GamesAppsLibraryMutation BeginRunningRegistration(
        GamesAppsLibraryState persisted,
        string savedId)
    {
        var state = Normalize(persisted);
        if (!IsOpaqueId(savedId))
            return new GamesAppsLibraryMutation(
                state, GamesAppsLibraryMutationRejection.LibraryFull);
        if (state.SavedIds.Contains(savedId, StringComparer.Ordinal) ||
            state.PendingRunningRegistrationSavedIds.Contains(savedId, StringComparer.Ordinal))
            return new GamesAppsLibraryMutation(state, GamesAppsLibraryMutationRejection.None);
        if (state.SavedIds.Count >= MaximumCuratedItems ||
            state.PendingRunningRegistrationSavedIds.Count >= MaximumCuratedItems)
            return new GamesAppsLibraryMutation(
                state, GamesAppsLibraryMutationRejection.LibraryFull);
        return new GamesAppsLibraryMutation(
            state with
            {
                PendingRunningRegistrationSavedIds =
                    state.PendingRunningRegistrationSavedIds.Append(savedId).ToArray(),
            },
            GamesAppsLibraryMutationRejection.None);
    }

    internal static GamesAppsLibraryMutation CompleteRunningRegistration(
        GamesAppsLibraryState persisted,
        WidgetAppLibraryItem item,
        IReadOnlyList<string> visibleSavedIds)
    {
        var state = Normalize(persisted);
        var mutation = state.SavedIds.Contains(item.SavedId, StringComparer.Ordinal)
            ? new GamesAppsLibraryMutation(state, GamesAppsLibraryMutationRejection.None)
            : Toggle(state, item, isVisibleMember: false, visibleSavedIds);
        if (!mutation.Accepted) return mutation;
        state = mutation.State;
        return mutation with
        {
            State = state with
            {
                RunningRegistrationSavedIds = state.RunningRegistrationSavedIds
                    .Append(item.SavedId).Distinct(StringComparer.Ordinal).ToArray(),
                PendingRunningRegistrationSavedIds =
                    state.PendingRunningRegistrationSavedIds.Where(id => !string.Equals(
                        id, item.SavedId, StringComparison.Ordinal)).ToArray(),
            },
        };
    }

    internal static GamesAppsLibraryState ClearPendingRunningRegistration(
        GamesAppsLibraryState persisted,
        string savedId)
    {
        var state = Normalize(persisted);
        return state with
        {
            PendingRunningRegistrationSavedIds =
                state.PendingRunningRegistrationSavedIds.Where(id => !string.Equals(
                    id, savedId, StringComparison.Ordinal)).ToArray(),
        };
    }

    internal static GamesAppsLibraryMutation BeginRunningRegistrationRemoval(
        GamesAppsLibraryState persisted,
        string savedId)
    {
        var state = Normalize(persisted);
        if (!state.SavedIds.Contains(savedId, StringComparer.Ordinal) ||
            !state.RunningRegistrationSavedIds.Contains(savedId, StringComparer.Ordinal))
            return new GamesAppsLibraryMutation(state, GamesAppsLibraryMutationRejection.None);
        if (state.PendingRunningRegistrationSavedIds.Contains(savedId, StringComparer.Ordinal))
            return new GamesAppsLibraryMutation(state, GamesAppsLibraryMutationRejection.None);
        if (state.PendingRunningRegistrationSavedIds.Count >= MaximumCuratedItems)
            return new GamesAppsLibraryMutation(
                state, GamesAppsLibraryMutationRejection.LibraryFull);
        return new GamesAppsLibraryMutation(
            state with
            {
                PendingRunningRegistrationSavedIds =
                    state.PendingRunningRegistrationSavedIds.Append(savedId).ToArray(),
            },
            GamesAppsLibraryMutationRejection.None);
    }

    internal static GamesAppsLibraryState CompleteRunningRegistrationRemoval(
        GamesAppsLibraryState persisted,
        string savedId)
    {
        var state = Normalize(persisted);
        var saved = state.SavedIds.Where(id => !string.Equals(
            id, savedId, StringComparison.Ordinal)).ToArray();
        return state with
        {
            SavedIds = saved,
            SelectedSavedId = string.Equals(
                state.SelectedSavedId, savedId, StringComparison.Ordinal)
                ? saved.FirstOrDefault()
                : state.SelectedSavedId,
            AutoGameSavedIds = state.AutoGameSavedIds.Where(id => !string.Equals(
                id, savedId, StringComparison.Ordinal)).ToArray(),
            DisplayItems = state.DisplayItems.Where(item => !string.Equals(
                item.SavedId, savedId, StringComparison.Ordinal)).ToArray(),
            RunningRegistrationSavedIds = state.RunningRegistrationSavedIds
                .Where(id => !string.Equals(id, savedId, StringComparison.Ordinal)).ToArray(),
            PendingRunningRegistrationSavedIds =
                state.PendingRunningRegistrationSavedIds.Where(id => !string.Equals(
                    id, savedId, StringComparison.Ordinal)).ToArray(),
        };
    }

    internal static IReadOnlyList<GamesAppsPersistedDisplayItem> BuildDisplayItems(
        GamesAppsLibraryState baseline,
        IReadOnlyList<WidgetAppLibraryItem>? candidates,
        GamesAppsLibraryState desired)
    {
        baseline = Normalize(baseline);
        desired = Normalize(desired);
        var saved = desired.SavedIds.ToHashSet(StringComparer.Ordinal);
        var automatic = desired.AutoGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var excluded = desired.ExcludedGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var display = baseline.DisplayItems
            .Where(item => saved.Contains(item.SavedId) && !excluded.Contains(item.SavedId))
            .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        foreach (var item in candidates ?? [])
        {
            if (!saved.Contains(item.SavedId) || excluded.Contains(item.SavedId)) continue;
            if (automatic.Contains(item.SavedId) &&
                GamesAppsAppLibraryPresentation.Kind(item) != WidgetAppLibraryKind.Game)
                display.Remove(item.SavedId);
            else
                display[item.SavedId] = ToDisplayItem(item);
        }
        return desired.SavedIds.Where(display.ContainsKey)
            .Select(savedId => display[savedId])
            .ToArray();
    }

    internal static GamesAppsLibraryProjection Project(
        GamesAppsLibraryState persisted,
        IReadOnlyList<WidgetAppLibraryItem> candidates)
    {
        var state = Normalize(persisted);
        var automatic = state.AutoGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var excluded = state.ExcludedGameSavedIds.ToHashSet(StringComparer.Ordinal);
        var bySaved = candidates
            .DistinctBy(item => item.SavedId, StringComparer.Ordinal)
            .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var displayBySaved = state.DisplayItems
            .ToDictionary(item => item.SavedId, StringComparer.Ordinal);
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var items = state.SavedIds.Where(savedId =>
                !excluded.Contains(savedId) &&
                (bySaved.TryGetValue(savedId, out var item)
                    ? !automatic.Contains(savedId) ||
                        GamesAppsAppLibraryPresentation.Kind(item) == WidgetAppLibraryKind.Game
                    : displayBySaved.ContainsKey(savedId)))
            .Select(savedId =>
            {
                if (bySaved.TryGetValue(savedId, out var item))
                {
                    resolved.Add(savedId);
                    return item;
                }
                return ProjectedItem(displayBySaved[savedId]);
            })
            .ToArray();
        return new GamesAppsLibraryProjection(items, resolved);
    }

    private static GamesAppsLibraryState EmptyState() => new(3, [], null);

    private static bool IsValidCurrentState(GamesAppsLibraryState? state)
    {
        if (state is null || state.Version != 3 || state.SavedIds is null ||
            state.AutoGameSavedIds is null || state.ExcludedGameSavedIds is null ||
            state.DisplayItems is null || state.RunningRegistrationSavedIds is null ||
            state.PendingRunningRegistrationSavedIds is null ||
            state.SavedIds.Count > MaximumCuratedItems ||
            state.AutoGameSavedIds.Count > MaximumCuratedItems ||
            state.ExcludedGameSavedIds.Count > MaximumExcludedGames ||
            state.DisplayItems.Count > MaximumCuratedItems ||
            state.RunningRegistrationSavedIds.Count > MaximumCuratedItems ||
            state.PendingRunningRegistrationSavedIds.Count > MaximumCuratedItems)
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
        var registered = new HashSet<string>(StringComparer.Ordinal);
        if (state.RunningRegistrationSavedIds.Any(id =>
                !saved.Contains(id) || !registered.Add(id))) return false;
        var pending = new HashSet<string>(StringComparer.Ordinal);
        if (state.PendingRunningRegistrationSavedIds.Any(id =>
                !IsOpaqueId(id) || !pending.Add(id) ||
                saved.Contains(id) && !registered.Contains(id))) return false;

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
            NormalizeDisplayName(GamesAppsAppLibraryPresentation.DisplayName(item)),
            GamesAppsAppLibraryPresentation.Kind(item));

    internal static IReadOnlyList<WidgetAppLibraryItem> NormalizeResolved(
        IReadOnlyList<WidgetAppLibraryItem>? items,
        IReadOnlyList<string> requestedSavedIds)
    {
        var requested = requestedSavedIds.ToHashSet(StringComparer.Ordinal);
        var seenApp = new HashSet<string>(StringComparer.Ordinal);
        var seenSaved = new HashSet<string>(StringComparer.Ordinal);
        var bySaved = (items ?? [])
            .Where(item => item is not null && IsOpaqueId(item.AppId) &&
                IsOpaqueId(item.SavedId) && requested.Contains(item.SavedId) &&
                !string.IsNullOrWhiteSpace(item.Presentation.DisplayName) &&
                seenApp.Add(item.AppId) &&
                seenSaved.Add(item.SavedId))
            .ToDictionary(item => item.SavedId, item => item, StringComparer.Ordinal);
        return requestedSavedIds.Where(bySaved.ContainsKey).Select(savedId =>
        {
            var item = bySaved[savedId];
            var name = item.Presentation.DisplayName.Trim();
            name = name.Length > 120 ? name[..120] : name;
            return item with
            {
                Presentation = item.Presentation with { DisplayName = name },
            };
        }).ToArray();
    }

    private static bool TryAddExcludedGame(List<string> excludedSavedIds, string savedId)
    {
        if (excludedSavedIds.Contains(savedId, StringComparer.Ordinal)) return true;
        if (excludedSavedIds.Count == MaximumExcludedGames) return false;
        excludedSavedIds.Add(savedId);
        return true;
    }

    private static string? NearestSurvivingSavedId(
        string removedSavedId,
        IReadOnlyList<string> survivingSavedIds,
        IReadOnlyList<string> visibleSavedIds)
    {
        var removedIndex = visibleSavedIds.ToList().FindIndex(id => string.Equals(
            id, removedSavedId, StringComparison.Ordinal));
        var surviving = visibleSavedIds.Where(id => survivingSavedIds.Contains(
                id, StringComparer.Ordinal))
            .ToArray();
        if (surviving.Length == 0) return survivingSavedIds.FirstOrDefault();
        return surviving[Math.Clamp(removedIndex, 0, surviving.Length - 1)];
    }

    private static WidgetAppLibraryItem ProjectedItem(GamesAppsPersistedDisplayItem item) => new(
        PendingAppId(item.SavedId),
        item.SavedId,
        new WidgetAppLibraryPresentation(
            item.DisplayName,
            item.Kind,
            new WidgetAppLibrarySourceReference("source-retained", "Saved library"),
            new WidgetAppLibraryAvailability(
                WidgetAppLibraryAvailabilityState.StaleSource,
                false, "revalidation_required"),
            new WidgetAppLibraryArtworkSet([]),
            Metadata: null,
            new WidgetAppLibraryCapabilitySet([]),
            ActiveOperation: null));

    private static string PendingAppId(string savedId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(savedId));
        return "pending." + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

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
        (raw.RunningRegistrationSavedIds ?? []).SequenceEqual(
            desired.RunningRegistrationSavedIds, StringComparer.Ordinal) &&
        (raw.PendingRunningRegistrationSavedIds ?? []).SequenceEqual(
            desired.PendingRunningRegistrationSavedIds, StringComparer.Ordinal) &&
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
