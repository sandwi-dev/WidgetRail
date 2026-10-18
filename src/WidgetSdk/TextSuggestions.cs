using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>Immutable, bounded local catalog. Prefix matches precede substring matches; no fuzzy matching or I/O.</summary>
public sealed class TextSuggestionCatalog
{
    public const int MaximumEntries = 10000;
    private readonly TextSuggestion[] entries;

    public TextSuggestionCatalog(IEnumerable<TextSuggestion> suggestions)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        entries = suggestions.Take(MaximumEntries + 1).ToArray();
        if (entries.Length > MaximumEntries) throw new ArgumentException("Suggestion catalog exceeds its entry limit.", nameof(suggestions));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (!TextSuggestionContract.IsValid(new([entry]), 1, TextSuggestionContract.MaximumQueryLength) || !ids.Add(entry.Id))
                throw new ArgumentException("Suggestion catalog contains invalid or duplicate entries.", nameof(suggestions));
        Array.Sort(entries, (a, b) => { var order = StringComparer.OrdinalIgnoreCase.Compare(a.Label, b.Label); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id); });
    }

    public TextSuggestionResult Search(TextSuggestionQuery query, CancellationToken cancellationToken = default)
    {
        if (!TextSuggestionContract.IsValid(query)) throw new ArgumentException("Invalid suggestion query.", nameof(query));
        return SearchCore(query.Text, query.MaximumResults, query.MaximumValueLength, cancellationToken);
    }

    public TextSuggestionResult Search(string text, int maximumResults = 6, CancellationToken cancellationToken = default) =>
        SearchCore(text, maximumResults, TextSuggestionContract.MaximumQueryLength, cancellationToken);

    private TextSuggestionResult SearchCore(string text, int maximumResults, int maximumValueLength, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > TextSuggestionContract.MaximumQueryLength || maximumResults is < 1 or > TextSuggestionContract.MaximumResults)
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        var query = text.Trim();
        var result = new List<TextSuggestion>(maximumResults);
        // Two bounded passes avoid sorting or allocating a complete match set on every edit.
        for (var pass = 0; pass < 2 && result.Count < maximumResults; pass++)
            foreach (var item in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Value.Length > maximumValueLength) continue;
                var prefix = item.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                if (pass == 0 ? prefix : !prefix && item.Label.Contains(query, StringComparison.OrdinalIgnoreCase)) result.Add(item);
                if (result.Count == maximumResults) break;
            }
        return new(result.ToArray());
    }
}
