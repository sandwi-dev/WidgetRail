using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobConfigurationOption(int Index, string Id, string Title, string Section,
    string Description, bool? Changed, JsonElement Raw);

/// <summary>Immutable configuration index. PoB supplies default comparison and eligibility.</summary>
internal sealed class PobConfigurationCatalog
{
    internal static readonly PobConfigurationCatalog Empty = new([]);
    internal IReadOnlyList<PobConfigurationOption> Options { get; }
    internal IReadOnlyDictionary<string, int> ChangedBySection { get; }
    internal int ChangedCount { get; }
    internal bool SupportsChangedFilter { get; }

    private PobConfigurationCatalog(PobConfigurationOption[] options)
    {
        Options = Array.AsReadOnly(options);
        SupportsChangedFilter = options.All(option => option.Changed.HasValue);
        ChangedCount = options.Count(option => option.Changed == true);
        ChangedBySection = options.GroupBy(option => option.Section).ToDictionary(group => group.Key,
            group => group.Count(option => option.Changed == true), StringComparer.Ordinal);
    }

    internal IEnumerable<PobConfigurationOption> Match(string section, string text, bool changedOnly)
    {
        var searching = !string.IsNullOrWhiteSpace(text);
        return Options.Where(option => (!changedOnly || option.Changed == true) &&
            (searching ? (option.Title + " " + option.Section + " " + option.Description).Contains(text, StringComparison.OrdinalIgnoreCase)
                : option.Section == section));
    }

    internal static PobConfigurationCatalog Read(JsonElement state)
    {
        if (state.ValueKind is JsonValueKind.Undefined || !state.TryGetProperty("conditions", out var conditions)) return Empty;
        if (conditions.ValueKind != JsonValueKind.Array) throw new IOException("PoB returned invalid configuration settings.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<PobConfigurationOption>();
        foreach (var condition in conditions.EnumerateArray())
        {
            if (condition.ValueKind != JsonValueKind.Object) throw new IOException("PoB returned an invalid configuration setting.");
            string Text(string key, string? fallback = null)
            {
                if (!condition.TryGetProperty(key, out var value) && fallback is not null) return fallback;
                if (value.ValueKind != JsonValueKind.String) throw new IOException("PoB returned incomplete configuration settings.");
                return value.GetString()!;
            }
            var id = Text("id");
            if (id.Length == 0 || !seen.Add(id)) throw new IOException("PoB returned ambiguous configuration settings.");
            if (!condition.TryGetProperty("value", out var settingValue) || settingValue.ValueKind is not
                (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
                throw new IOException("PoB returned an invalid configuration value.");
            if (settingValue.ValueKind == JsonValueKind.Number && (!settingValue.TryGetDouble(out var number) || !double.IsFinite(number)))
                throw new IOException("PoB returned an invalid configuration number.");
            bool? changed = null;
            if (condition.TryGetProperty("changed", out var flag))
            {
                if (flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new IOException("PoB returned invalid configuration-default information.");
                changed = flag.GetBoolean();
            }
            options.Add(new(options.Count, id, Text("title", id), Text("section", "General"), Text("description", ""), changed, condition));
        }
        return new(options.ToArray());
    }
}
