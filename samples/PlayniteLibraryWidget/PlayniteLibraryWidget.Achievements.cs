using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private PlayniteAchievementBrowse? achievementBrowse;
    private WidgetLoadedCollection<PlayniteAchievementBrowse, PlayniteAchievementEntry>? achievementRows;

    private void RequestAchievementSearch(bool clear) => _model.Update(state =>
    {
        var request = CreateFocusRequest().Id;
        return state with
        {
            DetailsExtras = state.DetailsExtras with
            {
                AchievementSearchRequest = request,
                AchievementSearch = clear ? "" : state.DetailsExtras.AchievementSearch,
            },
        };
    });

    private WidgetElement? PrepareAchievements(PlayniteLibraryRenderState local)
    {
        if (local.DetailsExtras.Achievements is not { Available: true }) return null;
        if (achievementBrowse?.SameInputs(local.DetailsGeneration, local.DetailsExtras) != true)
        {
            var next = new PlayniteAchievementBrowse(local.DetailsGeneration, local.DetailsExtras);
            if (achievementRows is null)
                achievementRows = CreateLoadedCollection<PlayniteAchievementBrowse, PlayniteAchievementEntry>(
                    "playnite-library.achievement-rows", next.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), next, next.Rows, new()
                    {
                        PreserveOccurrences = true,
                        ItemKey = entry => new(entry.Key),
                        RenderItem = (_, entry, ids) => PlayniteAchievementPresentation.Row(entry, ids.Id("row")),
                        OnAction = async (query, entry, action, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            if (query.Generation != _model.Value.DetailsGeneration) return;
                            await HandleActionAsync(action, null, token);
                        },
                        ResolveArtworkSource = (query, entry, handle, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            return ValueTask.FromResult(AchievementArtwork(query, entry, handle));
                        },
                    });
            else achievementRows.Publish(next.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), next, next.Rows);
            achievementBrowse = next;
        }
        return UI.CollectionList(PlayniteAchievementBrowse.ListId, achievementRows!, 104, "Achievements")
            .Classes("playnite-library-achievement-list");
    }

    private static WidgetArtworkSource? AchievementArtwork(PlayniteAchievementBrowse query, PlayniteAchievementEntry entry, WidgetArtworkHandle handle) =>
        !entry.Secret && handle == PlayniteAchievementBrowse.IconHandle(entry.Key) && entry.Item.Icon is { } path && ArtworkFileContract.IsLocalPath(path)
            ? new WidgetFileArtwork(path, query.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;

    private WidgetView ApplyAchievementSearch(WidgetView page, PlayniteDetailsExtras state) =>
        state.AchievementSearchRequest > 0 && LifecycleState == WidgetLifecycleState.Interactive &&
        !state.ConfirmUninstall && state.Tab == PlayniteDetailsTab.Achievements && state.Achievements is { Available: true }
            ? page with { FocusGroupEntryRequest = new() { RequestId = state.AchievementSearchRequest, GroupId = PlayniteAchievementBrowse.SearchGroupId } }
            : page;

    private ValueTask<TextSuggestionResult> SuggestAchievements(TextSuggestionQuery query, CancellationToken token)
    {
        PlayniteAchievementBrowse? captured;
        lock (_gate)
        {
            captured = achievementBrowse;
            if (LifecycleState != WidgetLifecycleState.Interactive || _model.Value.DetailsItem is null ||
                _model.Value.DetailsExtras.Tab != PlayniteDetailsTab.Achievements || captured is null ||
                !ReferenceEquals(captured.State.Achievements, WithDetailsResources(_model.Value).DetailsExtras.Achievements) ||
                query.SourceKey != captured.SuggestionSource)
                return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
        }
        var result = captured.Suggestions.Search(query, token);
        lock (_gate)
            return ValueTask.FromResult(LifecycleState == WidgetLifecycleState.Interactive && ReferenceEquals(captured, achievementBrowse) &&
                ReferenceEquals(captured.State.Achievements, WithDetailsResources(_model.Value).DetailsExtras.Achievements) && _model.Value.DetailsItem is not null &&
                _model.Value.DetailsExtras.Tab == PlayniteDetailsTab.Achievements ? result : TextSuggestionResult.NotAvailable);
    }
}
