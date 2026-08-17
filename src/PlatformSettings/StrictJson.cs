using System.Text.Json;

namespace WidgetRail.PlatformSettings;

internal static class StrictJson
{
    public static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
    {
        using var document = JsonDocument.Parse(utf8.ToArray());
        Visit(document.RootElement, "$", 0);
    }

    private static void Visit(JsonElement element, string path, int depth)
    {
        if (depth > 64)
            throw new JsonException("JSON nesting exceeds the 64-level safety limit.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException($"Duplicate property '{property.Name}' at {path}.");
                Visit(property.Value, $"{path}.{property.Name}", depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                Visit(item, $"{path}[{index++}]", depth + 1);
        }
    }
}
