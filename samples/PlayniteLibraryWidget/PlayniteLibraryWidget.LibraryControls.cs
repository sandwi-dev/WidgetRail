using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private PlayniteLibraryPrivateState? suggestionOwner;
    private string suggestionSource = "playnite.games.initial";
    private TextSuggestion[] gameSuggestions = [];

    private string PrepareGameSuggestions()
    {
        if (ReferenceEquals(suggestionOwner, _organization)) return suggestionSource;
        suggestionOwner = _organization;
        var hidden = _organization.ExcludedSavedIds.ToHashSet(StringComparer.Ordinal);
        gameSuggestions = PlayniteLibraryTitlePolicy.Project(_organization).Items
            .Where(game => !hidden.Contains(game.SavedId) && !string.IsNullOrWhiteSpace(game.DisplayName))
            .DistinctBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(PlayniteLibraryPrivateState.MaximumItems)
            .Select(game => new TextSuggestion(PlayniteLibraryIdentity.Key(game.SavedId).Value,
                game.DisplayName,
                game.DisplayName)).ToArray();
        suggestionSource = "playnite.games." + Guid.NewGuid().ToString("N");
        return suggestionSource;
    }

    public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TextSuggestion[] catalog;
        lock (_gate)
        {
            if (LifecycleState != WidgetLifecycleState.Interactive || _navigation.Value.Route != PlayniteLibraryRoute.Browse ||
                _model.Value.DetailsItem is not null || _model.Value.FilterDraft is not null ||
                query.FieldId != "playnite-library.search" || query.SourceKey != suggestionSource)
                return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
            catalog = gameSuggestions;
        }
        var text = query.Text.Trim();
        if (text.Length == 0) return ValueTask.FromResult(TextSuggestionResult.Empty);
        var matches = new List<TextSuggestion>(Math.Clamp(query.MaximumResults, 1, TextSuggestionContract.MaximumResults));
        var limit = Math.Clamp(query.MaximumResults, 1, TextSuggestionContract.MaximumResults);
        for (var pass = 0; pass < 2 && matches.Count < limit; ++pass)
            foreach (var game in catalog)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (game.Value.Length > query.MaximumValueLength) continue;
                var prefix = game.Value.StartsWith(text, StringComparison.OrdinalIgnoreCase);
                if (pass == 0 ? !prefix : prefix || !game.Value.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
                matches.Add(game);
                if (matches.Count >= limit) break;
            }
        lock (_gate)
            return ValueTask.FromResult(query.SourceKey == suggestionSource && ReferenceEquals(catalog, gameSuggestions)
                ? new TextSuggestionResult(matches.ToArray()) : TextSuggestionResult.NotAvailable);
    }

    private bool TryHandleLibraryControls(WidgetActionEvent action)
    {
        lock (_gate) return HandleLibraryControlsCore(action);
    }
    private bool HandleLibraryControlsCore(WidgetActionEvent action)
    {
        if (!action.ActionId.StartsWith("playnite-library.filters.", StringComparison.Ordinal)) return false;
        if (LifecycleState != WidgetLifecycleState.Interactive || _navigation.Value.Route != PlayniteLibraryRoute.Browse) return true;
        var local = _model.Value;
        if (action.ActionId == PlayniteLibraryActions.FiltersOpen)
        {
            _model.Update(state => state with { FilterDraft = new(state.BrowseCollection, state.ActiveCategoryId) });
            return true;
        }
        if (action.ActionId == PlayniteLibraryActions.FiltersCancel)
        {
            _model.Update(state => state with { FilterDraft = null });
            return true;
        }
        if (action.ActionId == PlayniteLibraryActions.AllGames)
        {
            ApplyBrowseFilters(new(new(local.BrowseCollection.Query with { InstalledOnly = false, SourceAttribution = null }), null));
            return true;
        }
        if (local.FilterDraft is not { } draft) return true;
        if (action.ActionId == PlayniteLibraryActions.FiltersApply) { ApplyBrowseFilters(draft); return true; }
        var next = action.ActionId switch
        {
            "playnite-library.filters.installed" => draft with { Collection = draft.Collection with
                { Query = draft.Collection.Query with { InstalledOnly = !draft.Collection.Query.InstalledOnly } } },
            "playnite-library.filters.favorites" => draft with { Collection = draft.Collection.ToggleFavorites(BrowseGames) },
            "playnite-library.filters.recent" => draft with { Collection = draft.Collection with
                { Selected = null, RecentlyPlayedOnly = !draft.Collection.RecentlyPlayed } },
            "playnite-library.filters.reset" => new(new(local.BrowseCollection.Query with { InstalledOnly = false, SourceAttribution = null }), null),
            _ => draft,
        };
        const string prefix = "playnite-library.filters.draft.";
        if (action.ActionId.StartsWith(prefix, StringComparison.Ordinal))
        {
            var selected = action.ActionId[prefix.Length..];
            if (selected == PlayniteLibraryActions.CategoryAll) next = draft with { CategoryId = null };
            else if (PlayniteLibraryActions.TryParseCategoryOpen(selected, out var category) &&
                     PresentationOrganizationLocked().Categories.Any(item => item.Id == category))
                next = draft with { CategoryId = category };
            else if (selected.StartsWith(PlayniteLibraryActions.SourceOptionPrefix, StringComparison.Ordinal))
            {
                var sources = PlayniteLibrarySourceCatalog.SelectOptions(_organization.ProvenSources, local.SourceObservations, draft.Collection.Query.SourceAttribution);
                var source = sources.FirstOrDefault(value => PlayniteLibraryActions.SourceOption(value) == selected);
                if (selected == PlayniteLibraryActions.SourceAll || source is not null)
                    next = draft with { Collection = draft.Collection with { Query = draft.Collection.Query with { SourceAttribution = source } } };
            }
        }
        _model.Update(state => state with { FilterDraft = next });
        return true;
    }

    private void ApplyBrowseFilters(PlayniteLibraryFilterDraft draft)
    {
        var current = _model.Value;
        if (draft.CategoryId is not null && !PresentationOrganizationLocked().Categories.Any(category => category.Id == draft.CategoryId))
            draft = draft with { CategoryId = null };
        var changed = !SameCollectionQuery(current.BrowseCollection, draft.Collection) || current.ActiveCategoryId != draft.CategoryId;
        var reload = changed ? CreateBrowseReload() : null;
        _model.Update(state => state with { FilterDraft = null, BrowseCollection = draft.Collection,
            ActiveCategoryId = draft.CategoryId, ActiveBrowseReload = reload ?? state.ActiveBrowseReload,
            BrowseInitialFocusId = changed ? PlayniteLibraryActions.FiltersOpen : state.BrowseInitialFocusId });
        if (changed) ReloadQuery(retainCurrentItems: false);
    }
}
