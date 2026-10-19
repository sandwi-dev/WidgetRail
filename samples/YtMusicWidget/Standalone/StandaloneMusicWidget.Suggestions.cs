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
        // Local fallback when the provider is unavailable or has no suggestions.
        var names = _recentSearches.Concat(_sections.Values.SelectMany(section => section.Items.Select(entry => entry.Item.Title)))
            .Concat(_service.State.Queue.Select(item => item.Title))
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length <= 96 && !name.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(TextSuggestionCatalog.MaximumEntries);
        _suggestions = new(names.Select((name, index) => new TextSuggestion("music.name." + index, name, name))
            .Where(item => TextSuggestionContract.IsValid(new([item]), 1, 96)));
        _suggestionSource = "music.search." + Guid.NewGuid().ToString("N");
    }

    public override async ValueTask<TextSuggestionResult> OnSuggestTextAsync(TextSuggestionQuery query, CancellationToken cancellationToken)
    {
        TextSuggestionCatalog catalog;
        long account;
        lock (_gate)
        {
            if (!Current()) return TextSuggestionResult.NotAvailable;
            catalog = _suggestions;
            account = _service.State.RatingGeneration;
        }
        IReadOnlyList<string> remote = [];
        try { remote = await _service.SearchSuggestionsAsync(query.Text, cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or System.Text.Json.JsonException or TimeoutException) { }
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            if (!Current() || account != _service.State.RatingGeneration) return TextSuggestionResult.NotAvailable;
        // Keep provider order and whole-query semantics; music suggestions need not
        // start with the literal query (spelling corrections are useful here).
        var items = remote.Take(32).Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= query.MaximumValueLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((value, index) => new TextSuggestion("music.remote." + index, value, value))
            .Where(item => TextSuggestionContract.IsValid(new([item]), 1, query.MaximumValueLength))
            .Take(query.MaximumResults).ToArray();
        return items.Length > 0 ? new(items) : catalog.Search(query, cancellationToken);

        bool Current() => IsActive && _panel is null && _tab == "search" && query.FieldId == "search" && query.SourceKey == _suggestionSource;
    }
}
