using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace WidgetRail.Samples.PobCompanion;

internal enum PobBuildComparisonCategory { Statistics, Equipment, Skills, Passives, Configuration }
internal sealed record PobBuildComparisonEntry(string Key, string Label, string Value, string Details);
internal sealed record PobBuildComparisonContext(string Name, string Loadout, string MainSkill, string Class, string Level,
    string Tree, string Equipment, string Skills, string Configuration, string WeaponSet, string TreeVersion,
    string EngineVersion, string CalculationSkill, string SkillOptions);
internal sealed record PobBuildProjection(PobBuildComparisonContext Context,
    IReadOnlyDictionary<PobBuildComparisonCategory, IReadOnlyList<PobBuildComparisonEntry>> Entries, long EstimatedBytes);
internal sealed record PobBuildDifference(PobBuildComparisonCategory Category, string Key, string Label,
    PobBuildComparisonEntry? Current, PobBuildComparisonEntry? Other)
{
    internal string Kind => Current is null ? "Only in comparison" : Other is null ? "Only in current" : "Changed";
}
internal sealed record PobBuildImpactLine(string Text, string Tone);
internal sealed record PobBuildImpact(string Title, IReadOnlyList<PobBuildImpactLine> Lines);
internal sealed record PobBuildComparison(string InputKey, PobBuildProjection Current, PobBuildProjection Other,
    IReadOnlyList<PobBuildDifference> Differences, IReadOnlyList<PobBuildImpact> Impact);
internal sealed record PobBuildComparisonInput(string Identity, string Name, string Xml, string? LoadoutName = null);

/// <summary>Bounded read-only projection cache; every calculation uses the existing serialized engine.</summary>
internal sealed class PobBuildComparisonService
{
    internal const int MaximumCachedComparisons = 4;
    internal const long MaximumCacheBytes = 8 * 1024 * 1024;
    private readonly LinkedList<(PobBuildComparison Value, long Bytes)> cache = new();
    private long cachedBytes;
    private readonly SemaphoreSlim serial = new(1, 1);
    internal int CachedCount => cache.Count;
    internal long CachedBytes => cachedBytes;

    internal async Task<PobBuildComparison> CompareAsync(PobResult current, string currentIdentity, string currentName,
        PobBuildComparisonInput other, Func<object, CancellationToken, string, Task<PobResult>> project,
        CancellationToken token)
    {
        await serial.WaitAsync(token).ConfigureAwait(false);
        try { return await CompareCoreAsync(current, currentIdentity, currentName, other, project, token).ConfigureAwait(false); }
        finally { serial.Release(); }
    }
    private async Task<PobBuildComparison> CompareCoreAsync(PobResult current, string currentIdentity, string currentName,
        PobBuildComparisonInput other, Func<object, CancellationToken, string, Task<PobResult>> project, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(current.Xml) > BuildDocument.MaximumBytes ||
            Encoding.UTF8.GetByteCount(other.Xml) > BuildDocument.MaximumBytes)
            throw new IOException("This build is too large to compare.");
        var key = Identity(currentIdentity, currentName, current.Xml, Text(current.State, "engineVersion"),
            other.Identity, other.Name, other.Xml, other.LoadoutName ?? "");
        var node = cache.First;
        while (node is not null)
        {
            if (node.Value.Value.InputKey == key)
            { var found = node.Value.Value; cache.Remove(node); cache.AddFirst((found, Estimate(found))); return found; }
            node = node.Next;
        }
        object command = other.LoadoutName is { } loadout
            ? new { op = "loadout", name = loadout }
            : new { op = "load", xml = other.Xml, name = other.Name };
        if (other.LoadoutName is not null && other.Xml != current.Xml)
            throw new IOException("Choose a loadout from the current build.");
        var calculated = await project(command, token, current.Xml).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        BuildDocument.VerifySkillPreservation(other.Xml, calculated.Xml);
        var before = Project(current, currentName);
        var after = Project(calculated, other.Name);
        var differences = Enum.GetValues<PobBuildComparisonCategory>().SelectMany(category =>
        {
            var left = before.Entries[category].ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            var right = after.Entries[category].ToDictionary(entry => entry.Key, StringComparer.Ordinal);
            return left.Keys.Union(right.Keys, StringComparer.Ordinal).Select(key =>
                new PobBuildDifference(category, key, right.GetValueOrDefault(key)?.Label ?? left[key].Label,
                    left.GetValueOrDefault(key), right.GetValueOrDefault(key)))
                // Preserve upstream statistic, slot, skill and configuration order.
                // Alphabetical sorting would promote secondary values above damage/life.
                .Where(entry => entry.Current is null || entry.Other is null || entry.Current.Value != entry.Other.Value);
        }).ToArray();
        var impact = Array(calculated.State, "comparison", 8).Select(group => new PobBuildImpact(Text(group, "title"),
            Array(group, "lines", 512).Select(line => new PobBuildImpactLine(Text(line, "text", 4096),
                Array(line, "runs", 128).Select(run => Text(run, "tone")).FirstOrDefault(tone => tone is "green" or "warning" or "blue" or "gold" or "orange") ?? "text")).ToArray())).ToArray();
        var result = new PobBuildComparison(key, before, after, differences, impact);
        var bytes = Estimate(result);
        token.ThrowIfCancellationRequested();
        if (bytes <= MaximumCacheBytes)
        {
            while (cache.Count >= MaximumCachedComparisons || cachedBytes + bytes > MaximumCacheBytes)
            { cachedBytes -= cache.Last!.Value.Bytes; cache.RemoveLast(); }
            cache.AddFirst((result, bytes)); cachedBytes += bytes;
        }
        return result;
    }
    private static long Estimate(PobBuildComparison value) => value.Current.EstimatedBytes + value.Other.EstimatedBytes +
        value.Differences.Count * 128L + value.Impact.Sum(group => group.Lines.Sum(line => (long)line.Text.Length * 2 + 64));
    internal static string Identity(params string[] fields)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var field in fields)
        {
            var bytes = Encoding.UTF8.GetBytes(field);
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    internal static PobBuildProjection Project(PobResult result, string name)
    {
        var state = result.State;
        var entries = Enum.GetValues<PobBuildComparisonCategory>().ToDictionary(category => category,
            _ => (IReadOnlyList<PobBuildComparisonEntry>)System.Array.Empty<PobBuildComparisonEntry>());
        string Active(string field) => Array(state, field, 512).Where(item => Flag(item, "active"))
            .Select(item => Text(item, "title")).FirstOrDefault() ?? "Independent / not reported";
        var context = new PobBuildComparisonContext(Bounded(name, 512), Active("loadouts"), Text(state, "mainSkill") +
            (Flag(state, "mainSkillDisabled") ? " · Disabled: " + Text(state, "mainSkillDisabledReason") :
                Flag(state, "mainSkillShowsAverage") ? " · Average damage" : ""),
            Text(state, "class") + " · " + Text(state, "ascendancy"), Text(state, "level"), Active("trees"), Active("itemSets"),
            Active("skillSets"), Active("configSets"), WeaponSet(result.Xml), Text(state, "treeVersion"), Text(state, "engineVersion"),
            state.TryGetProperty("breakdownContext", out var calculation) ? Text(calculation, "skill") + " · " + Text(calculation, "mode") : "Not reported",
            string.Join("\n", Array(state, "skillOptions", 128).Select(option => Text(option, "id") + ": " +
                (Text(option, "kind") == "number" ? Text(option, "value") :
                    string.Join(", ", Array(option, "options", 512).Where(item => Flag(item, "active")).Select(item => Text(item, "title")))))));
        entries[PobBuildComparisonCategory.Statistics] = Array(state, "stats", 128).Select(stat =>
        {
            if (!stat.TryGetProperty("value", out var value) || !value.TryGetDouble(out var number) || !double.IsFinite(number))
                throw new IOException("PoB returned an invalid comparison statistic.");
            return new PobBuildComparisonEntry(Text(stat, "name"), Text(stat, "name"), number.ToString("R", CultureInfo.InvariantCulture), "Calculated by PoB for this build's main skill and configuration.");
        }).ToArray();
        entries[PobBuildComparisonCategory.Equipment] = Array(state, "items", 128).Select(item =>
            new PobBuildComparisonEntry(Text(item, "slot"), Text(item, "slot"), Text(item, "raw", 65536).Replace("\r\n", "\n").Trim(),
                Text(item, "name") + "\n" + Text(item, "base") + "\n" + Text(item, "raw", 65536))).ToArray();
        entries[PobBuildComparisonCategory.Passives] = Array(state, "nodes", 20000).Where(node => Flag(node, "allocated") || Flag(node, "granted"))
            .Select(node => new PobBuildComparisonEntry(context.TreeVersion + "/" + Text(node, "id"), Text(node, "name") + " · " + Text(node, "id"),
                Text(node, "description", 16384) + "\n" + (Flag(node, "granted") ? "Granted" : "Allocated") + " · " + Allocation(node) + "\n" + Text(node, "socketedItem"),
                Text(node, "description", 16384) + "\n" + (Flag(node, "granted") ? "Granted" : "Allocated") + " · " + Allocation(node) +
                (Text(node, "socketedItem").Length > 0 ? "\nSocketed: " + Text(node, "socketedItem") : ""))).ToArray();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        entries[PobBuildComparisonCategory.Skills] = Array(state, "skills", 512).Select(group =>
        {
            var identity = Identity(Text(group, "label"), Text(group, "slot"), Text(group, "sourceKind"), Text(group, "sourceName"));
            var occurrence = occurrences.GetValueOrDefault(identity) + 1; occurrences[identity] = occurrence;
            var text = $"{(Flag(group, "enabled") ? "Enabled" : "Disabled")} · {(Flag(group, "main") ? "Main skill" : "Other group")} · Full DPS {(Flag(group, "fullDps") ? "included" : "excluded")}\n" +
                "Slot: " + Text(group, "slot") + " · Group count: " + Text(group, "groupCount") + "\n" +
                string.Join("\n", Array(group, "weaponSets", 2).Select(set => "Weapon set " + Text(set, "id") + ": " + (Flag(set, "enabled") ? "Enabled" : "Disabled"))) + "\n" +
                string.Join("\n", Array(group, "gems", 64).Select(gem => Text(gem, "name") + " · Level " + Text(gem, "level") + " · Quality " + Text(gem, "quality") +
                    " · Count " + Text(gem, "count") + (Flag(gem, "enabled") ? "" : " · Disabled")));
            return new PobBuildComparisonEntry(identity + "/" + occurrence, Text(group, "title") + " · Group " + Text(group, "id"), text,
                Text(group, "sourceName").Length > 0 ? "Source: " + Text(group, "sourceName") + "\n" + text : text);
        }).ToArray();
        var configuration = result.Configuration.Options.Select(option =>
        {
            var value = option.Raw.GetProperty("value");
            var label = Array(option.Raw, "options", 512).FirstOrDefault(choice => choice.TryGetProperty("value", out var candidate) && candidate.GetRawText() == value.GetRawText());
            var display = label.ValueKind == JsonValueKind.Object ? Text(label, "label") : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() ? "On" : "Off" : value.ToString();
            return new PobBuildComparisonEntry(option.Id, option.Title, value.GetRawText() + "|" + Flag(option.Raw, "applicable"),
                option.Section + "\n" + display + (Flag(option.Raw, "applicable") ? "" : "\nNot currently applicable") +
                (option.Description.Length > 0 ? "\n\n" + Bounded(option.Description, 16384) : ""));
        }).ToList();
        foreach (var block in Array(state, "customModGroups", 64))
        {
            var value = (Flag(block, "enabled") ? "Enabled" : "Disabled") + "\n" + Text(block, "text", 65536);
            configuration.Add(new("custom-block/" + Text(block, "id"), "Custom modifiers · " + Text(block, "title"), value, value));
        }
        if (Text(state, "customMods", 65536) is { Length: > 0 } custom && !configuration.Any(entry => entry.Key == "customMods"))
            configuration.Add(new("customMods", "Custom modifiers", custom, custom));
        entries[PobBuildComparisonCategory.Configuration] = configuration;
        foreach (var values in entries.Values)
            if (values.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() != values.Count || values.Count > 4096)
                throw new IOException("PoB returned ambiguous or excessive build comparison data.");
        var bytes = entries.Values.Sum(values => values.Sum(entry => (long)(entry.Key.Length + entry.Label.Length + entry.Value.Length + entry.Details.Length) * 2 + 128));
        if (bytes > 4 * 1024 * 1024) throw new IOException("This build has too much detailed data to compare safely.");
        return new(context, entries, bytes);
    }
    private static string Allocation(JsonElement node) => Text(node, "allocationMode") switch { "1" => "Weapon set 1", "2" => "Weapon set 2", _ => "Shared allocation" };
    private static string WeaponSet(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = BuildDocument.MaximumBytes });
        var root = XDocument.Load(reader).Root;
        var items = root?.Element("Items");
        var active = (string?)items?.Attribute("activeItemSet");
        var selected = items?.Elements("ItemSet").FirstOrDefault(set => (string?)set.Attribute("id") == active) ?? items;
        return (string?)selected?.Attribute("useSecondWeaponSet") == "true" ? "Weapon set 2" : "Weapon set 1";
    }
    private static JsonElement[] Array(JsonElement value, string key, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out var array)) return [];
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum) throw new IOException("PoB returned excessive or invalid comparison data.");
        return array.EnumerateArray().ToArray();
    }
    private static bool Flag(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var flag) && flag.ValueKind == JsonValueKind.True;
    private static string Text(JsonElement value, string key, int maximum = 2048) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var text) ? Bounded(text.ValueKind == JsonValueKind.String ? text.GetString()! : text.ToString(), maximum) : "";
    private static string Bounded(string value, int maximum) => value.Length <= maximum ? value : throw new IOException("PoB returned excessive comparison text.");
}
