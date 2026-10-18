using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SpotifyWidget;

public sealed partial class SpotifyWidget
{
    private readonly List<string> _recentSearches = [];
    private readonly List<string> _loadedSuggestionNames = [];
    private TextSuggestionCatalog _suggestionCatalog = new([]);
    private string _suggestionSource = "spotify.search.initial";

    private void RememberLoadedNames(IEnumerable<string> names)
    {
        lock (_gate)
        {
            var changed = false;
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 96 || name.Any(char.IsControl) || _loadedSuggestionNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                _loadedSuggestionNames.Add(name); changed = true;
                if (_loadedSuggestionNames.Count > 500) _loadedSuggestionNames.RemoveAt(0);
            }
            if (changed) RefreshSuggestionCatalogLocked();
        }
    }
    private void RefreshSuggestionCatalogLocked()
    {
        _suggestionCatalog = new(_recentSearches.Concat(_loadedSuggestionNames).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new TextSuggestion("spotify.name." + SpotifyCollectionIdentity.Media(name).Value, name, name))
            .Where(item => TextSuggestionContract.IsValid(new([item]), 1, ProtocolConstants.MaximumTextEntryLength)));
        _suggestionSource = "spotify.search." + Guid.NewGuid().ToString("N");
    }
    public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken token)
    {
        TextSuggestionCatalog catalog;
        lock (_gate)
        {
            if (!IsActive || _viewState != SpotifyWidgetViewState.Ready || _navigation.Value.Route != SpotifyRoute.Search ||
                query.FieldId != "spotify.search.query" || query.SourceKey != _suggestionSource) return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
            catalog = _suggestionCatalog;
        }
        return ValueTask.FromResult(catalog.Search(query, token));
    }
}
