using System.Text.RegularExpressions;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Immutable search index for already-loaded PoB data. Values remain literal search fragments.</summary>
internal sealed class PobSearchSuggestions
{
    private sealed record Entry(TextSuggestion Suggestion, string SearchText);
    private readonly Entry[] entries;

    internal PobSearchSuggestions(IEnumerable<(string Title, string SearchText, string Description)> source, int maximumLength)
    {
        var unique = new Dictionary<string, (string Description, HashSet<string> Aliases)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (title, searchText, description) in source)
        {
            var plain = Clean(title);
            // A first-line prefix also matches multiline/long modifier descriptions in the existing picker.
            var value = Regex.Split(plain, @"[\p{Cc}\p{Zl}\p{Zp}]").FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim() ?? "";
            value = Bound(value, Math.Min(maximumLength, TextSuggestionContract.MaximumLabelLength));
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (!unique.TryGetValue(value, out var item))
            {
                item = (OneLine(description, TextSuggestionContract.MaximumDescriptionLength), new(StringComparer.OrdinalIgnoreCase));
            }
            else if (item.Description != OneLine(description, TextSuggestionContract.MaximumDescriptionLength))
                item.Description = "Multiple matching entries";
            // Duplicate names (especially small passives) share one completion, retaining their searchable effects.
            item.Aliases.Add(Clean(searchText));
            unique[value] = item;
        }
        entries = unique.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase).Select((item, i) =>
            new Entry(new("match." + i, item.Key, item.Key, item.Value.Description), string.Join("\n", item.Value.Aliases))).ToArray();
    }

    internal TextSuggestionResult Search(TextSuggestionQuery query, CancellationToken token)
    {
        var text = query.Text.Trim();
        var matches = new List<TextSuggestion>(query.MaximumResults);
        // Names first, descriptions/aliases second. Never sort a complete result set per keystroke.
        for (var pass = 0; pass < 3 && matches.Count < query.MaximumResults; pass++)
            foreach (var item in entries)
            {
                token.ThrowIfCancellationRequested();
                if (item.Suggestion.Value.Length > query.MaximumValueLength) continue;
                var label = item.Suggestion.Label;
                var rank = label.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 :
                    label.Contains(text, StringComparison.OrdinalIgnoreCase) ? 1 :
                    item.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
                if (rank != pass) continue;
                matches.Add(item.Suggestion);
                if (matches.Count == query.MaximumResults) break;
            }
        return new(matches.ToArray());
    }

    private static string Clean(string text) => Regex.Replace(text, @"\^x[0-9a-fA-F]{6}|\^\d", "");
    private static string OneLine(string text, int maximum) => Bound(Regex.Replace(Clean(text), @"\s+", " ").Trim(), maximum);
    private static string Bound(string text, int maximum)
    {
        if (text.Length <= maximum) return text;
        var length = maximum;
        if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].TrimEnd();
    }
}
