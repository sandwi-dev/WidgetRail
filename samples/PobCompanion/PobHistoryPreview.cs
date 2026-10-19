using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobHistoryChange(string Label, string Before, string After);
internal sealed record PobHistorySummary(string Title, PobHistoryChange[] Changes, int OtherChanges = 0);

/// <summary>Bounded descriptions of saved state, never a calculation or a second history store.</summary>
internal static class PobHistoryPreview
{
    private sealed record Fact(string Category, string Label, string Value, bool Selection = false, string Kind = "");
    internal static PobHistorySummary Describe(string current, string target, JsonElement context = default)
    {
        try
        {
            var before = Facts(current, context); var after = Facts(target, context);
            var changes = before.Keys.Union(after.Keys).Select(key =>
            {
                before.TryGetValue(key, out var a); after.TryGetValue(key, out var b);
                return (Before: a, After: b);
            }).Where(pair => pair.Before?.Value != pair.After?.Value).ToArray();
            var selections = changes.Where(pair => pair.After?.Selection == true || pair.Before?.Selection == true).ToArray();
            // Active-set changes explain a loadout switch without claiming all linked sets share a name.
            var ordered = selections.Concat(changes.Except(selections)).ToArray();
            var categories = changes.Select(pair => (pair.After ?? pair.Before)!.Category).Distinct().ToArray();
            var title = selections.Length > 1 ? "Switch loadout / sets" : selections.Length == 1 ? "Switch " + (selections[0].After ?? selections[0].Before)!.Label.ToLowerInvariant()
                : categories.Length == 1 ? categories[0] + " changes" : "Build changes";
            if (changes.Length == 0) return new("Restore saved build state", []);
            return new(title, ordered.Take(4).Select(pair => Change(pair.Before, pair.After, context)).ToArray(), Math.Max(0, ordered.Length - 4));
        }
        catch (Exception error) when (error is XmlException or InvalidOperationException or ArgumentException)
        {
            // A description must never prevent restoring a valid engine-owned snapshot.
            return new("Restore saved build state", []);
        }
    }

    private static PobHistoryChange Change(Fact? before, Fact? after, JsonElement context)
    {
        var fact = (after ?? before)!;
        var from = before?.Value ?? "Not set"; var to = after?.Value ?? "Not set";
        if (fact.Kind == "item")
        {
            var removed = RemovedLines(before?.Value ?? "", after?.Value ?? "");
            var added = RemovedLines(after?.Value ?? "", before?.Value ?? "");
            from = removed.Length == 0 ? "No removed lines" : string.Join("; ", removed);
            to = added.Length == 0 ? "No added lines" : string.Join("; ", added);
        }
        if (fact.Kind == "nodes")
        {
            var oldNodes = (before?.Value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var newNodes = (after?.Value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var names = ContextArray(context, "nodes").Where(node => node.ValueKind == JsonValueKind.Object && node.TryGetProperty("id", out _))
                .GroupBy(node => node.GetProperty("id").ToString()).ToDictionary(group => group.Key,
                    group => ContextText(group.First(), "name"));
            string DescribeNodes(string[] nodes, IEnumerable<string> difference) => nodes.Length + " allocated" +
                (difference.ToArray() is { Length: > 0 } changed ? ": " + string.Join(", ", changed.Take(3).Select(id => names.GetValueOrDefault(id, "Passive " + id))) +
                    (changed.Length > 3 ? " +" + (changed.Length - 3) + " more" : "") : "");
            from = DescribeNodes(oldNodes, oldNodes.Except(newNodes)); to = DescribeNodes(newNodes, newNodes.Except(oldNodes));
        }
        return new(Clip(fact.Label, 100), Clip(from, 160), Clip(to, 160));
    }

    private static Dictionary<string, Fact> Facts(string xml, JsonElement context)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new() { DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null, MaxCharactersInDocument = BuildDocument.MaximumBytes });
        var root = XDocument.Load(reader).Root ?? throw new XmlException("Missing build root.");
        var facts = new Dictionary<string, Fact>(StringComparer.Ordinal);
        var conditionNames = ContextArray(context, "conditions").GroupBy(value => ContextText(value, "id"))
            .ToDictionary(group => group.Key, group => ContextText(group.First(), "title"));
        var sections = new Dictionary<string, string> { ["Build"] = "Character", ["Tree"] = "Passive tree", ["Items"] = "Equipment",
            ["Skills"] = "Skills", ["Config"] = "Configuration", ["Notes"] = "Notes", ["Calcs"] = "Calculations", ["Party"] = "Party" };
        foreach (var section in root.Elements())
        {
            if (section.Name.LocalName is "TreeView" or "Import") continue; // Saved browsing/export UI, not build edits.
            var category = sections.GetValueOrDefault(section.Name.LocalName, Words(section.Name.LocalName));
            Visit(section, section.Name.LocalName, category, category);
        }
        return facts;

        void Visit(XElement element, string path, string category, string label)
        {
            var tag = element.Name.LocalName;
            if (tag is "PlayerStat" or "MinionStat" or "FullDPSSkill") return; // Recomputed output.
            var title = (string?)element.Attribute("title");
            if (!string.IsNullOrWhiteSpace(title)) label = category + " · " + title;
            if (tag == "Skill") label += " · " + ((string?)element.Attribute("label") is { Length: > 0 } groupName ? groupName
                : (string?)element.Element("Gem")?.Attribute("nameSpec") ?? "Skill group");
            if (tag == "Gem") label = category + " · " + ((string?)element.Parent?.Parent?.Attribute("title") ?? "Default") + " · " + ((string?)element.Attribute("nameSpec") ?? "Gem");
            if (tag == "Slot") label += " · " + (string?)element.Attribute("name");
            if (tag == "Input")
            {
                var name = (string?)element.Attribute("name") ?? "Option";
                var display = conditionNames.GetValueOrDefault(name, "");
                label += " · " + (display.Length > 0 ? display : Words(name));
            }
            if (tag == "Item")
            {
                var name = ItemName(element.Value);
                // Keep individual modifier changes legible; raw metadata is never a preview body.
                facts[path + "/name"] = new(category, "Item name", name);
                var lines = ItemLines(element.Value).Where(line => !line.StartsWith("Rarity:", StringComparison.Ordinal)).ToArray();
                facts[path + "/text"] = new(category, name + " · Item text", string.Join("\n", lines), Kind: "item");
            }
            foreach (var attribute in element.Attributes())
            {
                var key = attribute.Name.LocalName;
                if (key is "id" or "viewMode" || tag == "Input" && key == "name" || tag == "Slot" && key == "name") continue;
                var value = attribute.Value;
                var field = Words(key);
                var selection = key is "activeSpec" or "activeItemSet" or "activeSkillSet" or "activeConfigSet";
                if (selection)
                {
                    var childName = key == "activeSpec" ? "Spec" : key == "activeItemSet" ? "ItemSet" : key == "activeSkillSet" ? "SkillSet" : "ConfigSet";
                    var sets = element.Elements(childName).ToArray();
                    var selected = key == "activeSpec" && int.TryParse(value, out var position)
                        ? sets.ElementAtOrDefault(position - 1) : sets.FirstOrDefault(set => (string?)set.Attribute("id") == value);
                    value = (string?)selected?.Attribute("title") ?? (selected is null ? "Set " + value : "Default");
                    field = key == "activeSpec" ? "Passive tree" : category + " set";
                }
                else if (key == "itemId")
                {
                    var item = root.Element("Items")?.Elements("Item").FirstOrDefault(item => (string?)item.Attribute("id") == value);
                    value = item is null ? value == "0" ? "Empty" : "Item " + value : ItemName(item.Value);
                    field = "Equipped item";
                }
                else if (tag == "Gem" && key is "gemId" or "skillId")
                {
                    value = (string?)element.Attribute("nameSpec") ?? "Gem";
                    field = "Gem";
                }
                else if (key == "nodes")
                {
                    // Record identities rather than only the count; equal-sized tree changes still differ.
                    field = "Allocated passives";
                    value = string.Join(", ", value.Split(',', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
                }
                else if (value is "true" or "false") value = value == "true" ? "On" : "Off";
                facts[path + "/@" + key] = new(category, selection ? field : tag == "Input" && key is "boolean" or "number" or "string" ? label : label + " · " + field,
                    value, selection, key == "nodes" ? "nodes" : "");
            }
            if (!element.HasElements && tag != "Item" && !string.IsNullOrWhiteSpace(element.Value))
                facts[path + "/text"] = new(category, label, element.Value.Trim());
            var occurrences = new Dictionary<string, int>();
            foreach (var child in element.Elements())
            {
                var identity = child.Name.LocalName + ":" + ((string?)child.Attribute("id") ?? (string?)child.Attribute("name") ?? "");
                var occurrence = occurrences.GetValueOrDefault(identity); occurrences[identity] = occurrence + 1;
                Visit(child, path + "/" + identity + ":" + occurrence, category, label);
            }
        }
    }

    private static string[] ItemLines(string text) => text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
    private static string ItemName(string text)
    {
        var lines = ItemLines(text); var rarity = Array.FindIndex(lines, line => line.StartsWith("Rarity:", StringComparison.Ordinal));
        return rarity >= 0 && rarity + 1 < lines.Length ? lines[rarity + 1] : "Item";
    }
    private static string[] RemovedLines(string from, string to)
    {
        var remaining = ItemLines(to).ToList(); var removed = new List<string>();
        foreach (var line in ItemLines(from)) { if (!remaining.Remove(line)) removed.Add(line); }
        return removed.ToArray();
    }
    private static string Words(string text)
    {
        text = Regex.Replace(text, "([a-z0-9])([A-Z])", "$1 $2").Replace('_', ' ');
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
    private static IEnumerable<JsonElement> ContextArray(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray() : [];
    private static string ContextText(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : "";
    private static string Clip(string text, int maximum)
    {
        text = Regex.Replace(text, @"\^[0-9]|\^x[0-9a-fA-F]{6}", "");
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= maximum ? text : text[..(char.IsHighSurrogate(text[maximum - 1]) ? maximum - 1 : maximum)] + "…";
    }
}
