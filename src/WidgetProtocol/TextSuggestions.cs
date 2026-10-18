namespace WidgetRail.WidgetProtocol;

/// <summary>Optional, whole-field completion. Selecting an entry never submits the field.</summary>
public sealed record TextSuggestionSource(string Key, int MinimumLength = 1, int MaximumResults = 6)
{
    public int DebounceMilliseconds { get; init; } = TextSuggestionContract.DebounceMilliseconds;
    public int MinimumRequestIntervalMilliseconds { get; init; } = TextSuggestionContract.MinimumRequestIntervalMilliseconds;
    public int TimeoutMilliseconds { get; init; } = TextSuggestionContract.TimeoutMilliseconds;
}

public sealed record TextSuggestion(string Id, string Label, string Value, string? Description = null);

/// <summary>Transient editor text, separate from committed actions. Never used for sensitive fields.</summary>
public sealed record TextSuggestionQuery(string FieldId, string SourceKey, string SessionId, long Revision,
    string Text, int MaximumResults = 6, int MaximumValueLength = TextSuggestionContract.MaximumQueryLength)
{
    public int TimeoutMilliseconds { get; init; } = TextSuggestionContract.TimeoutMilliseconds;
}

public sealed record TextSuggestionResult(IReadOnlyList<TextSuggestion> Items, bool Unavailable = false)
{
    public static TextSuggestionResult Empty { get; } = new(Array.Empty<TextSuggestion>());
    public static TextSuggestionResult NotAvailable { get; } = new(Array.Empty<TextSuggestion>(), true);
}

public static class TextSuggestionContract
{
    public const int MaximumResults = 8;
    public const int MaximumQueryLength = 512;
    public const int MaximumLabelLength = 96;
    public const int MaximumDescriptionLength = 128;
    public const int DebounceMilliseconds = 180;
    public const int MinimumRequestIntervalMilliseconds = 250;
    public const int TimeoutMilliseconds = 2000;
    public const int MaximumDebounceMilliseconds = 1000;
    public const int MaximumRequestIntervalMilliseconds = 2000;
    public const int MinimumTimeoutMilliseconds = 500;
    public const int MaximumTimeoutMilliseconds = 5000;
    public const int TransportGraceMilliseconds = 1000;

    public static bool IsValid(TextSuggestionSource? source) => source is not null && Key(source.Key) &&
        source.MinimumLength is >= 0 and <= 64 && source.MaximumResults is >= 1 and <= MaximumResults &&
        source.DebounceMilliseconds is >= 0 and <= MaximumDebounceMilliseconds &&
        source.MinimumRequestIntervalMilliseconds is >= MinimumRequestIntervalMilliseconds and <= MaximumRequestIntervalMilliseconds &&
        ValidTimeout(source.TimeoutMilliseconds);

    public static bool UsesCustomTiming(TextSuggestionSource source) =>
        source.DebounceMilliseconds != DebounceMilliseconds ||
        source.MinimumRequestIntervalMilliseconds != MinimumRequestIntervalMilliseconds || source.TimeoutMilliseconds != TimeoutMilliseconds;

    private static bool ValidTimeout(int value) => value is >= MinimumTimeoutMilliseconds and <= MaximumTimeoutMilliseconds;

    public static bool IsValid(TextSuggestionQuery? query) => query is not null && Key(query.FieldId) &&
        Key(query.SourceKey) && Guid.TryParseExact(query.SessionId, "N", out _) && query.Revision > 0 &&
        SafeText(query.Text, MaximumQueryLength) && query.MaximumResults is >= 1 and <= MaximumResults &&
        query.MaximumValueLength is >= 1 and <= MaximumQueryLength && ValidTimeout(query.TimeoutMilliseconds);

    public static bool IsValid(TextSuggestionResult? result, int maximumResults, int maximumLength)
    {
        if (result?.Items is null || result.Items.Count > maximumResults || maximumResults is < 1 or > MaximumResults ||
            result.Unavailable && result.Items.Count != 0) return false;
        var count = result.Items.Count;
        if (count > maximumResults) return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var item = result.Items[index];
            if (item is null || !Key(item.Id) || !ids.Add(item.Id) || string.IsNullOrWhiteSpace(item.Label) ||
                !SafeText(item.Label, MaximumLabelLength) || string.IsNullOrWhiteSpace(item.Value) ||
                !SafeText(item.Value, Math.Min(maximumLength, MaximumQueryLength)) ||
                item.Description is not null && !SafeText(item.Description, MaximumDescriptionLength)) return false;
        }
        return true;
    }

    public static bool Matches(ViewNode? node, TextSuggestionQuery query) =>
        node is { Kind: ViewNodeKind.TextEntry, IsDisabled: not true, IsBusy: not true } &&
        node.TextEntryInputKind is not TextEntryInputKind.Sensitive && node.TextEntrySuggestions is { } source &&
        IsValid(source) && source.Key == query.SourceKey && node.Id == query.FieldId &&
        query.TimeoutMilliseconds == source.TimeoutMilliseconds && query.MaximumResults <= source.MaximumResults &&
        query.MaximumValueLength == Math.Min(node.TextEntryMaximumLength ?? 0, MaximumQueryLength) && query.Text.Length >= source.MinimumLength &&
        query.Text.Length <= node.TextEntryMaximumLength;

    private static bool Key(string? value) => value is not null && value.Length <= 128 &&
        ProtocolValidationIdentifierContext.IsSafeIdentifier(value);

    private static bool SafeText(string? value, int maximum) => value is not null && value.Length <= maximum &&
        !value.Any(char.IsControl) && WellFormed(value);

    private static bool WellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i >= value.Length || !char.IsLowSurrogate(value[i])))
                return false;
        return true;
    }
}
