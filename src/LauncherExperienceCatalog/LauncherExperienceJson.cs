using System.Text.Json;

namespace WidgetRail.LauncherExperienceCatalog;

internal static class LauncherExperienceJson
{
    // The JSON reader must admit enough structural object/array layers for the
    // recipe parser to issue the more useful public 16-node-depth diagnostic.
    public const int MaximumDepth = 64;

    public static JsonDocument ParseStrict(ReadOnlyMemory<byte> utf8)
    {
        var document = JsonDocument.Parse(utf8, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = MaximumDepth,
        });
        RejectDuplicates(document.RootElement, "$", 0);
        return document;
    }

    public static void RequireObject(JsonElement value, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
            errors.Add(new(path, "expected_object", "Value must be an object."));
    }

    public static void RejectUnknown(
        JsonElement value,
        string path,
        IReadOnlySet<string> allowed,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name))
                errors.Add(new($"{path}.{property.Name}", "unknown_field", "Field is not part of schema version 1."));
    }

    public static bool TryRequiredProperty(
        JsonElement value,
        string name,
        string path,
        List<LauncherExperienceDiagnostic> errors,
        out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out property)) return true;
        property = default;
        errors.Add(new($"{path}.{name}", "required", "Field is required."));
        return false;
    }

    private static void RejectDuplicates(JsonElement value, string path, int depth)
    {
        if (depth > MaximumDepth) throw new JsonException($"JSON nesting exceeds {MaximumDepth} levels.");
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new LauncherExperienceDuplicateFieldException(
                        $"{path}.{property.Name}", $"Duplicate property '{property.Name}'.");
                RejectDuplicates(property.Value, $"{path}.{property.Name}", depth + 1);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                RejectDuplicates(item, $"{path}[{index++}]", depth + 1);
        }
    }
}

internal sealed class LauncherExperienceDuplicateFieldException(string diagnosticPath, string message)
    : Exception(message)
{
    public string DiagnosticPath { get; } = diagnosticPath;
}
