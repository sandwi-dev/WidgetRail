using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.YtMusicWidget.Standalone;

public sealed partial class StandaloneMusicWidget
{
    private readonly List<string> _recentSearches = [];
    private string _suggestionSource = "music.search.initial";
    private TextSuggestionCatalog _suggestions = new([]);

    private void RefreshSuggestions()
    {
        // Rebuilt only after catalogue changes, not on keystrokes or playback ticks.
        // No provider autocomplete requests; every candidate is already local.
        var names = _recentSearches.Concat(_sections.Values.SelectMany(section => section.Items.Select(entry => entry.Item.Title)))
            .Concat(_service.State.Queue.Select(item => item.Title))
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length <= 96 && !name.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(TextSuggestionCatalog.MaximumEntries);
        _suggestions = new(names.Select((name, index) => new TextSuggestion("music.name." + index, name, name))
            .Where(item => TextSuggestionContract.IsValid(new([item]), 1, 96)));
        _suggestionSource = "music.search." + Guid.NewGuid().ToString("N");
    }

    public override ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken cancellationToken)
    {
        TextSuggestionCatalog catalog;
        lock (_gate)
        {
            if (!IsActive || _panel is not null || _tab != "search" || query.FieldId != "search" || query.SourceKey != _suggestionSource)
                return ValueTask.FromResult(TextSuggestionResult.NotAvailable);
            catalog = _suggestions;
        }
        return ValueTask.FromResult(catalog.Search(query, cancellationToken));
    }
}
