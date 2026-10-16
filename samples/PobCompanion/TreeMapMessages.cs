using System.Text.Json;
using System.Security.Cryptography;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Pure map projection; every message fits the web-session limit.</summary>
public static class TreeMapMessages
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IReadOnlyList<JsonElement> Create(JsonElement state, JsonElement? before, bool preserve, int selected, bool reuseGeometry = false, long revision = 0, JsonElement? previousMapState = null)
    {
        var previousMapNodes = previousMapState?.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("id").GetInt32());
        var previous = before?.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetInt32(), n => n.GetProperty("allocated").GetBoolean());
        var messages = new List<JsonElement> { JsonSerializer.SerializeToElement(new { type = "begin", reuseGeometry, revision }) };
        var nodes = state.GetProperty("nodes").EnumerateArray().Select(n =>
        {
            var id = n.GetProperty("id").GetInt32(); var allocated = n.GetProperty("allocated").GetBoolean();
            var was = previous is not null && previous.TryGetValue(id, out var value) ? value : allocated;
            var name = n.GetProperty("name").GetString() ?? "";
            return JsonSerializer.SerializeToElement(new
            {
                id, x = n.GetProperty("x").GetDouble(), y = n.GetProperty("y").GetDouble(),
                name = name.Length > 100 ? name[..100] : name, allocated, ascendancy = n.GetProperty("ascendancy").GetBoolean(), kind = n.GetProperty("kind").GetString(),
                ascendancyName = n.TryGetProperty("ascendancyName",out var ascendancyName) ? ascendancyName.GetString() : "",
                icon = n.TryGetProperty("icon",out var icon) ? icon.GetString() : "",
                change = allocated == was ? "" : allocated ? "added" : "removed",
            }, Json);
        });
        if (reuseGeometry)
            AddBatches(messages, "states", nodes.Where(node=>node.GetProperty("allocated").GetBoolean() || node.GetProperty("change").GetString()!="" || ChangedLabel(node))
                .Select(node=>JsonSerializer.SerializeToElement(new { id=node.GetProperty("id").GetInt32(), allocated=node.GetProperty("allocated").GetBoolean(), change=node.GetProperty("change").GetString(), name=node.GetProperty("name").GetString(), kind=node.GetProperty("kind").GetString(),icon=node.GetProperty("icon").GetString() })), revision);
        else
        {
            AddBatches(messages, "nodes", nodes, revision);
            AddBatches(messages, "links", state.GetProperty("links").EnumerateArray(), revision);
        }
        messages.Add(JsonSerializer.SerializeToElement(new { type = "end", revision, preserve, selected, preview = before.HasValue,
            ascendancy = state.TryGetProperty("ascendancy",out var ascendancy) ? ascendancy.GetString() : "",
            treeVersion = state.TryGetProperty("treeVersion",out var treeVersion) ? treeVersion.GetString() : "" }));
        return messages;
        bool ChangedLabel(JsonElement node) => previousMapNodes is null || !previousMapNodes.TryGetValue(node.GetProperty("id").GetInt32(),out var old) ||
            old.GetProperty("name").GetString()!=node.GetProperty("name").GetString() || old.GetProperty("kind").GetString()!=node.GetProperty("kind").GetString() ||
            (old.TryGetProperty("icon",out var oldIcon)?oldIcon.GetString():"")!=node.GetProperty("icon").GetString();
    }
    public static string GeometryKey(JsonElement state)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach(var node in state.GetProperty("nodes").EnumerateArray().OrderBy(node=>node.GetProperty("id").GetInt32()))
            {
                writer.WriteStartArray();
                foreach(var field in new[]{"id","x","y","ascendancy"}) node.GetProperty(field).WriteTo(writer);
                if(node.TryGetProperty("ascendancyName",out var ascendancyName))ascendancyName.WriteTo(writer);else writer.WriteNullValue();
                writer.WriteEndArray();
            }
            writer.WriteStartArray();
            foreach(var edge in state.GetProperty("links").EnumerateArray().OrderBy(edge=>edge.GetProperty("a").GetInt32()).ThenBy(edge=>edge.GetProperty("b").GetInt32()))
            {
                writer.WriteStartArray();
                foreach(var field in new[]{"a","b","cx","cy","r","start","sweep"})
                    if(edge.TryGetProperty(field,out var value)) value.WriteTo(writer);else writer.WriteNullValue();
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            writer.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0,checked((int)stream.Length))));
    }
    private static void AddBatches(List<JsonElement> messages, string type, IEnumerable<JsonElement> entries, long revision)
    {
        var chunk = new List<JsonElement>(); var bytes = 80;
        foreach (var entry in entries)
        {
            var length = JsonSerializer.SerializeToUtf8Bytes(entry).Length + 1;
            if (length > 15000) throw new IOException("A tree element exceeds the map message limit.");
            if (chunk.Count > 0 && bytes + length > 15000)
            {
                messages.Add(JsonSerializer.SerializeToElement(new { type, revision, items = chunk }));
                chunk.Clear(); bytes = 80;
            }
            chunk.Add(entry); bytes += length;
        }
        if (chunk.Count > 0) messages.Add(JsonSerializer.SerializeToElement(new { type, revision, items = chunk }));
    }
}
