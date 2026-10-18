using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Immutable upstream search metadata in the engine's existing eligibility and ranking order.</summary>
internal sealed class PobGemSearchCatalog
{
    internal sealed record Entry(int Index, string Name, bool Support, string Tags, string SearchTags)
    {
        internal bool Matches(string text) => Name.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            Tags.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase) || SearchTags.Contains(text.Trim(), StringComparison.OrdinalIgnoreCase);
        internal string Description => (Support ? "Support gem" : "Skill gem") + (Tags.Length == 0 ? "" : " · " + Tags);
    }

    internal IReadOnlyList<Entry> Entries { get; }

    internal PobGemSearchCatalog(JsonElement[] source)
    {
        if (source.Length > 10000) throw new InvalidDataException("PoB returned too many gem search entries.");
        Entries = Array.AsReadOnly(source.Select((gem, index) =>
        {
            if (gem.ValueKind != JsonValueKind.Object || !gem.TryGetProperty("support", out var support) ||
                support.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("PoB returned invalid gem search metadata.");
            var name = ReadText(gem, "name", 512, required: true);
            return new Entry(index, name, support.GetBoolean(), ReadText(gem, "tagString", 2048), ReadText(gem, "searchTags", 4096));
        }).ToArray());
    }

    private static string ReadText(JsonElement gem, string key, int maximum, bool required = false)
    {
        if (!gem.TryGetProperty(key, out var value))
        {
            if (!required) return "";
            throw new InvalidDataException("PoB omitted a gem name.");
        }
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("PoB returned invalid gem " + key + " metadata.");
        var text = value.GetString()!;
        if (text.Length > maximum || text.Any(char.IsControl) || required && string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("PoB returned invalid gem " + key + " metadata.");
        return text;
    }
}
