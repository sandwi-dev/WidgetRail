using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private PlayniteAchievementBrowse? achievementBrowse;
    private WidgetIndexedCollection<PlayniteAchievementBrowse, PlayniteAchievementEntry>? achievementRows;
    private WidgetFocusPreviewSource<PlayniteAchievementBrowse>? achievementPreviews;

    private WidgetElement? PrepareAchievements(PlayniteLibraryRenderState local)
    {
        if (local.DetailsExtras.Achievements is not { Available: true }) return null;
        if (achievementBrowse?.SameInputs(local.DetailsGeneration, local.DetailsExtras) != true)
        {
            var next = new PlayniteAchievementBrowse(local.DetailsGeneration, local.DetailsExtras);
            if (achievementRows is null)
                achievementRows = CreateIndexedCollection<PlayniteAchievementBrowse, PlayniteAchievementEntry>(
                    "playnite-library.achievement-rows", next, next.Rows.Count, new()
                    {
                        ReadRange = (query, start, count, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            return ValueTask.FromResult<IReadOnlyList<PlayniteAchievementEntry>>(query.Rows.Skip(start).Take(count).ToArray());
                        },
                        ItemKey = entry => new(entry.Key),
                        OccurrenceKeys = query => query.Rows.Count <= IndexedCollectionLimits.MaximumContinuityItems
                            ? query.Rows.Select(entry => new WidgetCollectionItemKey(entry.Key)).ToArray() : null,
                        RenderItem = (_, entry, ids) => PlayniteAchievementPresentation.Row(entry, ids.Id("row")),
                        OnAction = (query, entry, action, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            if (action.ActionId == PlayniteAchievementBrowse.Prefix + "achievement.open." + entry.Key &&
                                query.Generation == _model.Value.DetailsGeneration)
                                InspectAchievement(entry.Key);
                            return ValueTask.CompletedTask;
                        },
                        ResolveArtworkSource = (query, entry, handle, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(AchievementArtwork(query, entry, handle));
                        },
                    });
            else if (achievementBrowse!.Rows.Select(entry => entry.Key).SequenceEqual(next.Rows.Select(entry => entry.Key)))
                achievementRows.UpdateContent(next);
            else achievementRows.PublishQuery(next, next.Rows.Count);
            if (achievementPreviews is null)
                achievementPreviews = CreateFocusPreviewSource("playnite-library.achievement-previews", next, new WidgetFocusPreviewOptions<PlayniteAchievementBrowse>
                {
                    Resolve = (query, key, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!query.ByKey.TryGetValue(key, out var entry)) throw new ArgumentException("Unknown achievement preview.");
                        return ValueTask.FromResult(PlayniteAchievementPresentation.Detail(entry, full: false));
                    },
                    ResolveArtworkSource = (query, key, handle, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        return ValueTask.FromResult(query.ByKey.TryGetValue(key, out var entry) ? AchievementArtwork(query, entry, handle) : null);
                    },
                });
            else achievementPreviews.Publish(next);
            achievementBrowse = next;
        }
        return UI.CollectionList(PlayniteAchievementBrowse.ListId, achievementRows!, 68, "Achievements")
            .Classes("playnite-library-achievement-list");
    }

    private static WidgetArtworkSource? AchievementArtwork(PlayniteAchievementBrowse query, PlayniteAchievementEntry entry, WidgetArtworkHandle handle) =>
        !entry.Secret && handle == PlayniteAchievementBrowse.IconHandle(entry.Key) && entry.Item.Icon is { } path && ArtworkFileContract.IsLocalPath(path)
            ? new WidgetFileArtwork(path, query.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;

    private WidgetArtworkSource? InspectedAchievementArtwork(WidgetArtworkHandle handle)
    {
        var state = _model.Value;
        return state.DetailsItem is not null && state.DetailsExtras.InspectedAchievement is { } key &&
            achievementBrowse is { } query && query.Generation == state.DetailsGeneration && query.ByKey.TryGetValue(key, out var entry)
            ? AchievementArtwork(query, entry, handle) : null;
    }

    private void InspectAchievement(string key)
    {
        var state = _model.Value;
        if (LifecycleState != WidgetLifecycleState.Interactive || state.DetailsItem is null ||
            achievementBrowse?.ByKey.ContainsKey(key) != true) return;
        UpdateDetails(state.DetailsGeneration, value => value with { InspectedAchievement = key, AchievementReturnKey = key, AchievementReturnRequest = 0 });
    }

    private void ReturnToAchievements()
    {
        _model.Update(state => state with
        {
            AchievementReturnSequence = state.AchievementReturnSequence + 1,
            DetailsExtras = state.DetailsExtras with { InspectedAchievement = null, AchievementReturnRequest = state.AchievementReturnSequence + 1 },
        });
    }

    private WidgetView ApplyAchievementReturn(WidgetView page, PlayniteDetailsExtras state)
    {
        if (state.AchievementReturnRequest > 0 && !state.ConfirmUninstall && state.InspectedAchievement is null && state.Tab == PlayniteDetailsTab.Achievements &&
            achievementRows is { } source && achievementBrowse is { } query && state.AchievementReturnKey is { } key)
        {
            var index = query.Rows.ToList().FindIndex(entry => entry.Key == key);
            if (index >= 0) return page with { FocusGroupEntryRequest = source.Enter(PlayniteAchievementBrowse.ListId,
                state.AchievementReturnRequest, source.FocusTarget(PlayniteAchievementBrowse.ListId, new(key), index)) };
        }
        return page;
    }
}
