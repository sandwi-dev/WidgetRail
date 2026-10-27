using WidgetRail.WidgetProtocol;

namespace WidgetRail.WidgetSdk;

/// <summary>The text used for local matching and ordering; display and insertion values are preserved.</summary>
public enum TextSuggestionMatchField { Label, Value }

/// <summary>Immutable, bounded local catalog. Prefix matches precede substring matches; no fuzzy matching or I/O.</summary>
public sealed class TextSuggestionCatalog
{
    public const int MaximumEntries = 10000;
    private readonly TextSuggestion[] entries;
    private readonly TextSuggestionMatchField matchField;

    public TextSuggestionCatalog(IEnumerable<TextSuggestion> suggestions) : this(suggestions, TextSuggestionMatchField.Label) { }

    /// <summary>Choose Value when display labels are abbreviated but the complete inserted text must remain searchable.</summary>
    public TextSuggestionCatalog(IEnumerable<TextSuggestion> suggestions, TextSuggestionMatchField matchField)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        if (!Enum.IsDefined(matchField)) throw new ArgumentOutOfRangeException(nameof(matchField));
        this.matchField = matchField;
        entries = suggestions.Take(MaximumEntries + 1).ToArray();
        if (entries.Length > MaximumEntries) throw new ArgumentException("Suggestion catalog exceeds its entry limit.", nameof(suggestions));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
            if (!TextSuggestionContract.IsValid(new([entry]), 1, TextSuggestionContract.MaximumQueryLength) || !ids.Add(entry.Id))
                throw new ArgumentException("Suggestion catalog contains invalid or duplicate entries.", nameof(suggestions));
        Array.Sort(entries, (a, b) => { var order = StringComparer.OrdinalIgnoreCase.Compare(MatchText(a), MatchText(b)); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Id, b.Id); });
    }

    public TextSuggestionResult Search(TextSuggestionQuery query, CancellationToken cancellationToken = default)
    {
        if (!TextSuggestionContract.IsValid(query)) throw new ArgumentException("Invalid suggestion query.", nameof(query));
        return SearchCore(query.Text, query.MaximumResults, query.MaximumValueLength, cancellationToken);
    }

    public TextSuggestionResult Search(string text, int maximumResults = 6, CancellationToken cancellationToken = default) =>
        SearchCore(text, maximumResults, TextSuggestionContract.MaximumQueryLength, cancellationToken);

    /// <summary>
    /// Returns all matches from this bounded local catalog, in the same order as
    /// keyboard completion. Use this for a result list that must share completion
    /// ranking without inheriting the keyboard's small response limit.
    /// </summary>
    public IReadOnlyList<TextSuggestion> FindMatches(string text,
        int maximumValueLength = TextSuggestionContract.MaximumQueryLength,
        CancellationToken cancellationToken = default)
    {
        ValidateText(text, maximumValueLength);
        return Array.AsReadOnly(Matches(text.Trim(), maximumValueLength, cancellationToken).ToArray());
    }

    private TextSuggestionResult SearchCore(string text, int maximumResults, int maximumValueLength, CancellationToken cancellationToken)
    {
        ValidateText(text, maximumValueLength);
        if (maximumResults is < 1 or > TextSuggestionContract.MaximumResults)
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        return new(Matches(text.Trim(), maximumValueLength, cancellationToken).Take(maximumResults).ToArray());
    }

    private static void ValidateText(string text, int maximumValueLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > TextSuggestionContract.MaximumQueryLength)
            throw new ArgumentOutOfRangeException(nameof(text));
        if (maximumValueLength is < 1 or > TextSuggestionContract.MaximumQueryLength)
            throw new ArgumentOutOfRangeException(nameof(maximumValueLength));
    }

    private IEnumerable<TextSuggestion> Matches(string query, int maximumValueLength, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Two bounded passes avoid sorting or allocating a complete match set on every edit.
        for (var pass = 0; pass < 2; pass++)
            foreach (var item in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Value.Length > maximumValueLength) continue;
                var candidate = MatchText(item);
                var prefix = candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase);
                if (pass == 0 ? prefix : !prefix && candidate.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return item;
            }
    }

    private string MatchText(TextSuggestion item) => matchField == TextSuggestionMatchField.Value ? item.Value : item.Label;
}
