using System.Text.Json;
using System.Security.Cryptography;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Pure map projection; every message fits the web-session limit.</summary>
public static class TreeMapMessages
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IReadOnlyList<JsonElement> Create(JsonElement state, JsonElement? before, bool preserve, int selected, bool reuseGeometry = false, long revision = 0, JsonElement? previousMapState = null)
    {
        var previousMapNodes = previousMapState?.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("id").GetInt32());
        var previous = before?.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetInt32(), n => n.GetProperty("allocated").GetBoolean());
        var oldDescriptions=before?.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("id").GetInt32(),n=>n.GetProperty("description").GetString());
        var oldModes=before?.GetProperty("nodes").EnumerateArray().ToDictionary(n=>n.GetProperty("id").GetInt32(),Mode);
        var messages = new List<JsonElement> { JsonSerializer.SerializeToElement(new { type = "begin", reuseGeometry, revision }) };
        var visibleIds=state.GetProperty("nodes").EnumerateArray().Where(n=>Flag(n,"visible",true)).Select(n=>n.GetProperty("id").GetInt32()).ToHashSet();
        var nodes = state.GetProperty("nodes").EnumerateArray().Where(n=>visibleIds.Contains(n.GetProperty("id").GetInt32())).Select(n =>
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
                interactive=Flag(n,"interactive",true),decoration=Flag(n,"decoration"),landmark=Flag(n,"landmark"),granted=Flag(n,"granted"),dimmed=Flag(n,"dimmed"),
                radius=n.TryGetProperty("radius",out var radius)?radius.GetDouble():50,
                allocationMode=Mode(n),
                socketedItem=n.TryGetProperty("socketedItem",out var jewel)?jewel.GetString():"",
                change = allocated != was ? allocated ? "added" : "removed" : allocated&&
                    ((oldDescriptions is not null&&oldDescriptions.TryGetValue(id,out var oldText)&&oldText!=n.GetProperty("description").GetString())||
                     (oldModes is not null&&oldModes.TryGetValue(id,out var oldMode)&&oldMode!=Mode(n)))?"changed":"",
            }, Json);
        });
        if (reuseGeometry)
            AddBatches(messages, "states", nodes.Where(node=>node.GetProperty("allocated").GetBoolean() || Flag(node,"granted") || node.GetProperty("change").GetString()!="" || ChangedLabel(node)), revision);
        else
        {
            AddBatches(messages, "nodes", nodes, revision);
            AddBatches(messages, "links", state.GetProperty("links").EnumerateArray().Where(edge=>visibleIds.Contains(edge.GetProperty("a").GetInt32())&&visibleIds.Contains(edge.GetProperty("b").GetInt32())), revision);
        }
        messages.Add(JsonSerializer.SerializeToElement(new { type = "end", revision, preserve, selected, preview = before.HasValue,
            weaponTreeModes=Flag(state,"weaponTreeModes"),
            ascendancy = state.TryGetProperty("ascendancy",out var ascendancy) ? ascendancy.GetString() : "",
            treeVersion = state.TryGetProperty("treeVersion",out var treeVersion) ? treeVersion.GetString() : "" }));
        return messages;
        bool ChangedLabel(JsonElement node) => previousMapNodes is null || !previousMapNodes.TryGetValue(node.GetProperty("id").GetInt32(),out var old) ||
            old.GetProperty("name").GetString()!=node.GetProperty("name").GetString() || old.GetProperty("kind").GetString()!=node.GetProperty("kind").GetString() ||
            new[]{"icon","socketedItem","radius","interactive","decoration","landmark","granted","dimmed"}.Any(key=>
                (old.TryGetProperty(key,out var oldValue)?oldValue.GetRawText():null)!=(node.TryGetProperty(key,out var newValue)?newValue.GetRawText():null));
    }
    private static bool Flag(JsonElement node,string key,bool fallback=false)=>node.TryGetProperty(key,out var value)?value.ValueKind==JsonValueKind.True:fallback;
    private static int Mode(JsonElement node)=>node.TryGetProperty("allocationMode",out var mode)?mode.GetInt32():0;
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
                writer.WriteBooleanValue(Flag(node,"visible",true));
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
        messages.AddRange(WebSessionMessageBatcher.Create(entries,
            chunk => JsonSerializer.SerializeToElement(new { type, revision, items = chunk })));
    }
}
