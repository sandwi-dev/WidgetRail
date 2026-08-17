using System.Globalization;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace WidgetRail.LauncherExperienceCatalog;

internal static class LauncherExperienceDocumentParser
{
    private static readonly HashSet<string> ManifestFields =
        ["schemaVersion", "id", "publisher", "name", "version", "layoutPreset", "compositionFile", "styleFile", "previewFile", "parameters"];
    private static readonly HashSet<string> ParameterFields =
        ["backgroundMode", "accent", "tileSize", "metadataDensity", "motionIntensity", "showSystemStatus", "focusEffect"];
    private static readonly HashSet<string> RecipeFields = ["schemaVersion", "branches"];
    private static readonly HashSet<string> BranchFields = ["compact", "standard", "wide"];
    private static readonly HashSet<string> BranchDocumentFields = ["root"];
    private static readonly HashSet<string> NodeFields =
        ["type", "slot", "region", "inset", "alignment", "orientation", "surface", "density", "rows", "columns", "children"];
    private static readonly HashSet<string> RegionFields = ["x", "y", "width", "height"];
    private static readonly HashSet<string> InsetFields = ["left", "top", "right", "bottom"];
    private static readonly HashSet<string> AlignmentFields = ["horizontal", "vertical"];

    public static LauncherExperienceManifest? ParseManifest(
        ReadOnlyMemory<byte> bytes,
        List<LauncherExperienceDiagnostic> errors)
    {
        using var document = Parse(bytes, "launcher.json", errors);
        if (document is null) return null;
        var root = document.RootElement;
        LauncherExperienceJson.RequireObject(root, "$", errors);
        LauncherExperienceJson.RejectUnknown(root, "$", ManifestFields, errors);
        if (root.ValueKind != JsonValueKind.Object) return null;

        var schemaVersion = Integer(root, "schemaVersion", "$", errors);
        var id = String(root, "id", "$", errors);
        var publisher = String(root, "publisher", "$", errors);
        var name = String(root, "name", "$", errors);
        var versionText = String(root, "version", "$", errors);
        var presetText = String(root, "layoutPreset", "$", errors);
        var styleFile = String(root, "styleFile", "$", errors);
        var previewFile = String(root, "previewFile", "$", errors);
        var compositionFile = OptionalString(root, "compositionFile", "$", errors);
        var parameters = ParseParameters(root, errors);

        if (schemaVersion is not 1)
            errors.Add(new("$.schemaVersion", "unsupported_version", "Expected launcher manifest schema version 1."));
        if (!LauncherExperienceIdentity.IsValidId(id))
            errors.Add(new("$.id", "invalid_id", "ID must be a lowercase portable identifier of at most 128 characters."));
        if (!LauncherExperienceIdentity.IsValidPublisher(publisher))
            errors.Add(new("$.publisher", "invalid_publisher", "Publisher must be a lowercase reverse-DNS identifier."));
        if (id is not null && publisher is not null &&
            LauncherExperienceIdentity.IsValidId(id) && LauncherExperienceIdentity.IsValidPublisher(publisher) &&
            !LauncherExperienceIdentity.IsOwnedByPublisher(id, publisher))
            errors.Add(new("$.id", "identity_mismatch", "ID must be owned by the publisher namespace."));
        if (name is null || name.Length is < 1 or > 80 || name.Any(char.IsControl))
            errors.Add(new("$.name", "invalid_name", "Name must contain 1 to 80 printable characters."));
        if (!LauncherExperienceIdentity.TryParseCanonicalVersion(versionText, out var version))
            errors.Add(new("$.version", "invalid_version", "Version must use canonical dotted numeric notation."));
        if (!TryEnum(presetText, Presets, out LauncherLayoutPreset preset))
            errors.Add(new("$.layoutPreset", "invalid_preset", "Preset must be hero-rail, cover-wall, carousel, or compact-grid."));
        ValidatePath(styleFile, "$.styleFile", ".wrss", required: true, errors);
        ValidateImagePath(previewFile, "$.previewFile", required: true, errors);
        ValidatePath(compositionFile, "$.compositionFile", ".json", required: false, errors);

        if (errors.Count != 0 || id is null || publisher is null || name is null || version is null ||
            styleFile is null || previewFile is null)
            return null;
        return new LauncherExperienceManifest(
            1, id, publisher, name, version, preset, compositionFile, styleFile, previewFile,
            parameters ?? LauncherExperienceParameters.Empty);
    }

    public static LauncherLayoutRecipe? ParseRecipe(
        ReadOnlyMemory<byte> bytes,
        string sourcePath,
        List<LauncherExperienceDiagnostic> errors)
    {
        using var document = Parse(bytes, sourcePath, errors);
        if (document is null) return null;
        var root = document.RootElement;
        LauncherExperienceJson.RequireObject(root, "$", errors);
        LauncherExperienceJson.RejectUnknown(root, "$", RecipeFields, errors);
        if (root.ValueKind != JsonValueKind.Object) return null;
        var schemaVersion = Integer(root, "schemaVersion", "$", errors);
        if (schemaVersion is not 1)
            errors.Add(new("$.schemaVersion", "unsupported_version", "Expected layout recipe schema version 1."));
        if (!LauncherExperienceJson.TryRequiredProperty(root, "branches", "$", errors, out var branchesElement))
            return null;
        LauncherExperienceJson.RequireObject(branchesElement, "$.branches", errors);
        LauncherExperienceJson.RejectUnknown(branchesElement, "$.branches", BranchFields, errors);
        if (branchesElement.ValueKind != JsonValueKind.Object) return null;

        var branches = new Dictionary<LauncherResponsiveBranch, LauncherLayoutNode>();
        foreach (var property in branchesElement.EnumerateObject())
        {
            if (!TryEnum(property.Name, BranchNames, out LauncherResponsiveBranch branch)) continue;
            LauncherExperienceJson.RequireObject(property.Value, $"$.branches.{property.Name}", errors);
            LauncherExperienceJson.RejectUnknown(property.Value, $"$.branches.{property.Name}", BranchDocumentFields, errors);
            if (!LauncherExperienceJson.TryRequiredProperty(
                    property.Value, "root", $"$.branches.{property.Name}", errors, out var rootElement))
                continue;
            var nodeCount = 0;
            var node = ParseNode(rootElement, $"$.branches.{property.Name}.root", errors, 0, ref nodeCount);
            if (node is not null) branches.Add(branch, node);
        }
        if (branches.Count == 0)
            errors.Add(new("$.branches", "missing_branch", "At least one compact, standard, or wide branch is required."));
        return errors.Count == 0
            ? new LauncherLayoutRecipe(1, new ReadOnlyDictionary<LauncherResponsiveBranch, LauncherLayoutNode>(branches))
            : null;
    }

    private static JsonDocument? Parse(
        ReadOnlyMemory<byte> bytes,
        string sourcePath,
        List<LauncherExperienceDiagnostic> errors)
    {
        try
        {
            return LauncherExperienceJson.ParseStrict(bytes);
        }
        catch (LauncherExperienceDuplicateFieldException exception)
        {
            errors.Add(new(exception.DiagnosticPath, "duplicate_field", "JSON contains a duplicate field."));
            return null;
        }
        catch (JsonException exception)
        {
            errors.Add(new(
                exception.Path ?? sourcePath,
                "invalid_json",
                "JSON is invalid or contains duplicate fields."));
            return null;
        }
    }

    private static LauncherExperienceParameters? ParseParameters(
        JsonElement root,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (!root.TryGetProperty("parameters", out var value)) return LauncherExperienceParameters.Empty;
        LauncherExperienceJson.RequireObject(value, "$.parameters", errors);
        LauncherExperienceJson.RejectUnknown(value, "$.parameters", ParameterFields, errors);
        if (value.ValueKind != JsonValueKind.Object) return null;
        var background = OptionalString(value, "backgroundMode", "$.parameters", errors);
        var accent = OptionalString(value, "accent", "$.parameters", errors);
        var tile = OptionalString(value, "tileSize", "$.parameters", errors);
        var density = OptionalString(value, "metadataDensity", "$.parameters", errors);
        var motion = OptionalString(value, "motionIntensity", "$.parameters", errors);
        var focus = OptionalString(value, "focusEffect", "$.parameters", errors);
        bool? systemStatus = null;
        if (value.TryGetProperty("showSystemStatus", out var status))
        {
            if (status.ValueKind == JsonValueKind.True || status.ValueKind == JsonValueKind.False)
                systemStatus = status.GetBoolean();
            else
                errors.Add(new("$.parameters.showSystemStatus", "expected_boolean", "Value must be true or false."));
        }
        Allowed(background, "$.parameters.backgroundMode", ["global", "pack-asset", "selected-game-artwork"], errors);
        Allowed(tile, "$.parameters.tileSize", ["small", "medium", "large"], errors);
        Allowed(density, "$.parameters.metadataDensity", ["compact", "standard", "rich"], errors);
        Allowed(motion, "$.parameters.motionIntensity", ["none", "reduced", "standard"], errors);
        Allowed(focus, "$.parameters.focusEffect", ["outline", "lift", "scale"], errors);
        if (accent is not null && !IsColor(accent))
            errors.Add(new("$.parameters.accent", "invalid_color", "Accent must be a six- or eight-digit hexadecimal color."));
        return new LauncherExperienceParameters(background, accent, tile, density, motion, systemStatus, focus);
    }

    private static LauncherLayoutNode? ParseNode(
        JsonElement element,
        string path,
        List<LauncherExperienceDiagnostic> errors,
        int depth,
        ref int nodeCount)
    {
        if (depth > 16)
        {
            errors.Add(new(path, "layout_depth", "Layout nesting may not exceed 16 levels."));
            return null;
        }
        nodeCount++;
        if (nodeCount > 128)
        {
            errors.Add(new(path, "too_many_nodes", "A responsive branch may contain at most 128 nodes."));
            return null;
        }
        LauncherExperienceJson.RequireObject(element, path, errors);
        LauncherExperienceJson.RejectUnknown(element, path, NodeFields, errors);
        if (element.ValueKind != JsonValueKind.Object) return null;
        var typeText = String(element, "type", path, errors);
        if (!TryEnum(typeText, PrimitiveNames, out LauncherLayoutPrimitive type))
        {
            errors.Add(new($"{path}.type", "invalid_primitive", "Type must be region, grid, stack, overlay, or inset."));
            return null;
        }
        LauncherSlot? slot = null;
        var slotText = OptionalString(element, "slot", path, errors);
        if (slotText is not null)
        {
            if (TryEnum(slotText, SlotNames, out LauncherSlot parsed)) slot = parsed;
            else errors.Add(new($"{path}.slot", "invalid_slot", "Slot is not in the launcher semantic allowlist."));
        }
        var region = ParseRegion(element, path, errors);
        var insets = ParseInsets(element, path, errors);
        var (horizontal, vertical) = ParseAlignment(element, path, errors);
        LauncherOrientation? orientation = null;
        var orientationText = OptionalString(element, "orientation", path, errors);
        if (orientationText is not null)
        {
            if (TryEnum(orientationText, OrientationNames, out LauncherOrientation parsed)) orientation = parsed;
            else errors.Add(new($"{path}.orientation", "invalid_orientation", "Orientation must be horizontal or vertical."));
        }
        LauncherSurfaceRole? surface = null;
        var surfaceText = OptionalString(element, "surface", path, errors);
        if (surfaceText is not null)
        {
            if (TryEnum(surfaceText, SurfaceNames, out LauncherSurfaceRole parsed)) surface = parsed;
            else errors.Add(new($"{path}.surface", "invalid_surface", "Surface must be solid or glass."));
        }
        LauncherContentDensity? density = null;
        var densityText = OptionalString(element, "density", path, errors);
        if (densityText is not null)
        {
            if (TryEnum(densityText, DensityNames, out LauncherContentDensity parsed)) density = parsed;
            else errors.Add(new($"{path}.density", "invalid_density", "Density must be compact, standard, or expanded."));
        }
        var rows = OptionalInteger(element, "rows", path, errors);
        var columns = OptionalInteger(element, "columns", path, errors);
        var children = new List<LauncherLayoutNode>();
        if (element.TryGetProperty("children", out var childrenElement))
        {
            if (childrenElement.ValueKind != JsonValueKind.Array)
                errors.Add(new($"{path}.children", "expected_array", "Children must be an array."));
            else if (childrenElement.GetArrayLength() > 32)
                errors.Add(new($"{path}.children", "too_many_children", "A node may contain at most 32 children."));
            else
            {
                var index = 0;
                foreach (var childElement in childrenElement.EnumerateArray())
                {
                    var child = ParseNode(childElement, $"{path}.children[{index++}]", errors, depth + 1, ref nodeCount);
                    if (child is not null) children.Add(child);
                }
            }
        }
        ValidateNodeShape(type, slot, orientation, surface, density, rows, columns, children, path, errors);
        return new LauncherLayoutNode(type, region, insets, horizontal, vertical, slot, orientation, rows, columns,
            Array.AsReadOnly(children.ToArray()), surface, density);
    }

    private static void ValidateNodeShape(
        LauncherLayoutPrimitive type,
        LauncherSlot? slot,
        LauncherOrientation? orientation,
        LauncherSurfaceRole? surface,
        LauncherContentDensity? density,
        int? rows,
        int? columns,
        List<LauncherLayoutNode> children,
        string path,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (slot is not null && children.Count != 0)
            errors.Add(new(path, "slot_has_children", "A semantic slot must be a leaf node."));
        if (slot is null && children.Count == 0)
            errors.Add(new(path, "empty_container", "A layout container must contain at least one child."));
        if (type == LauncherLayoutPrimitive.Grid)
        {
            if (rows is null || columns is null)
                errors.Add(new(path, "grid_dimensions_required", "Grid nodes require bounded rows and columns."));
            if (rows is < 1 or > 12) errors.Add(new($"{path}.rows", "grid_out_of_range", "Rows must be between 1 and 12."));
            if (columns is < 1 or > 12) errors.Add(new($"{path}.columns", "grid_out_of_range", "Columns must be between 1 and 12."));
        }
        else if (rows is not null || columns is not null)
            errors.Add(new(path, "unexpected_grid_dimensions", "Only grid nodes may declare rows or columns."));
        if (type == LauncherLayoutPrimitive.Stack && orientation is null)
            errors.Add(new($"{path}.orientation", "required", "Stack nodes require an orientation."));
        if (type != LauncherLayoutPrimitive.Stack && orientation is not null && slot is null)
            errors.Add(new($"{path}.orientation", "unexpected_orientation", "Only stack nodes or semantic slots may declare orientation."));
        if (type == LauncherLayoutPrimitive.Inset && children.Count != 1)
            errors.Add(new(path, "inset_child_count", "Inset nodes require exactly one child."));
        if (surface is not null && slot is not (LauncherSlot.DetailsPanel or LauncherSlot.ControllerHints))
            errors.Add(new($"{path}.surface", "unexpected_surface",
                "Only details-panel and controller-hints may select a surface role."));
        if (density is not null && slot is (null or LauncherSlot.HeroBackground or LauncherSlot.GameRail))
            errors.Add(new($"{path}.density", "unexpected_density",
                "Density applies only to launcher text/status slots."));
    }

    private static LauncherRegion ParseRegion(JsonElement node, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!node.TryGetProperty("region", out var value)) return LauncherRegion.Full;
        LauncherExperienceJson.RequireObject(value, $"{path}.region", errors);
        LauncherExperienceJson.RejectUnknown(value, $"{path}.region", RegionFields, errors);
        if (value.ValueKind != JsonValueKind.Object) return LauncherRegion.Full;
        return new LauncherRegion(
            Number(value, "x", $"{path}.region", errors) ?? 0,
            Number(value, "y", $"{path}.region", errors) ?? 0,
            Number(value, "width", $"{path}.region", errors) ?? 0,
            Number(value, "height", $"{path}.region", errors) ?? 0);
    }

    private static LauncherInsets ParseInsets(JsonElement node, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!node.TryGetProperty("inset", out var value)) return LauncherInsets.None;
        LauncherExperienceJson.RequireObject(value, $"{path}.inset", errors);
        LauncherExperienceJson.RejectUnknown(value, $"{path}.inset", InsetFields, errors);
        if (value.ValueKind != JsonValueKind.Object) return LauncherInsets.None;
        return new LauncherInsets(
            OptionalNumber(value, "left", $"{path}.inset", errors) ?? 0,
            OptionalNumber(value, "top", $"{path}.inset", errors) ?? 0,
            OptionalNumber(value, "right", $"{path}.inset", errors) ?? 0,
            OptionalNumber(value, "bottom", $"{path}.inset", errors) ?? 0);
    }

    private static (LauncherAlignment Horizontal, LauncherAlignment Vertical) ParseAlignment(
        JsonElement node,
        string path,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (!node.TryGetProperty("alignment", out var value))
            return (LauncherAlignment.Stretch, LauncherAlignment.Stretch);
        LauncherExperienceJson.RequireObject(value, $"{path}.alignment", errors);
        LauncherExperienceJson.RejectUnknown(value, $"{path}.alignment", AlignmentFields, errors);
        if (value.ValueKind != JsonValueKind.Object) return (LauncherAlignment.Stretch, LauncherAlignment.Stretch);
        var horizontalText = OptionalString(value, "horizontal", $"{path}.alignment", errors);
        var verticalText = OptionalString(value, "vertical", $"{path}.alignment", errors);
        var horizontal = LauncherAlignment.Stretch;
        var vertical = LauncherAlignment.Stretch;
        if (horizontalText is not null && !TryEnum(horizontalText, AlignmentNames, out horizontal))
            errors.Add(new($"{path}.alignment.horizontal", "invalid_alignment", "Alignment must be start, center, end, or stretch."));
        if (verticalText is not null && !TryEnum(verticalText, AlignmentNames, out vertical))
            errors.Add(new($"{path}.alignment.vertical", "invalid_alignment", "Alignment must be start, center, end, or stretch."));
        return (horizontal, vertical);
    }

    private static int? Integer(JsonElement root, string name, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!LauncherExperienceJson.TryRequiredProperty(root, name, path, errors, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        errors.Add(new($"{path}.{name}", "expected_integer", "Value must be an integer."));
        return null;
    }

    private static int? OptionalInteger(JsonElement root, string name, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        errors.Add(new($"{path}.{name}", "expected_integer", "Value must be an integer."));
        return null;
    }

    private static double? Number(JsonElement root, string name, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!LauncherExperienceJson.TryRequiredProperty(root, name, path, errors, out var value)) return null;
        return ReadNumber(value, $"{path}.{name}", errors);
    }

    private static double? OptionalNumber(JsonElement root, string name, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return ReadNumber(value, $"{path}.{name}", errors);
    }

    private static double? ReadNumber(JsonElement value, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) && double.IsFinite(result))
            return result;
        errors.Add(new(path, "expected_number", "Value must be a finite number."));
        return null;
    }

    private static string? String(JsonElement root, string name, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!LauncherExperienceJson.TryRequiredProperty(root, name, path, errors, out var value)) return null;
        return ReadString(value, $"{path}.{name}", errors);
    }

    private static string? OptionalString(JsonElement root, string name, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return ReadString(value, $"{path}.{name}", errors);
    }

    private static string? ReadString(JsonElement value, string path, List<LauncherExperienceDiagnostic> errors)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } result) return result;
        errors.Add(new(path, "expected_string", "Value must be a string."));
        return null;
    }

    private static void ValidatePath(
        string? value,
        string path,
        string extension,
        bool required,
        List<LauncherExperienceDiagnostic> errors)
    {
        if (value is null)
        {
            if (required) errors.Add(new(path, "required", "Package-relative file path is required."));
            return;
        }
        if (!LauncherExperienceFileGuard.IsSafePackagePath(value) ||
            !Path.GetExtension(value).Equals(extension, StringComparison.OrdinalIgnoreCase))
            errors.Add(new(path, "unsafe_path", $"Path must be a normalized package-relative {extension} file."));
    }

    private static void ValidateImagePath(string? value, string path, bool required, List<LauncherExperienceDiagnostic> errors)
    {
        if (value is null)
        {
            if (required) errors.Add(new(path, "required", "Package-relative image path is required."));
            return;
        }
        var extension = Path.GetExtension(value);
        if (!LauncherExperienceFileGuard.IsSafePackagePath(value) ||
            extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            errors.Add(new(path, "unsafe_asset_path", "Image path must be a normalized package-relative PNG, JPEG, or WebP file."));
    }

    private static void Allowed(string? value, string path, IReadOnlyCollection<string> values, List<LauncherExperienceDiagnostic> errors)
    {
        if (value is not null && !values.Contains(value))
            errors.Add(new(path, "invalid_parameter", $"Value '{value}' is not host-defined for this parameter."));
    }

    private static bool IsColor(string value) =>
        value.Length is 7 or 9 && value[0] == '#' && value.AsSpan(1).ToString().All(Uri.IsHexDigit);

    private static bool TryEnum<T>(string? value, IReadOnlyDictionary<string, T> values, out T result)
        where T : struct, Enum
    {
        result = default;
        return value is not null && values.TryGetValue(value, out result);
    }

    private static readonly IReadOnlyDictionary<string, LauncherLayoutPreset> Presets =
        new Dictionary<string, LauncherLayoutPreset>(StringComparer.Ordinal)
        {
            ["hero-rail"] = LauncherLayoutPreset.HeroRail,
            ["cover-wall"] = LauncherLayoutPreset.CoverWall,
            ["carousel"] = LauncherLayoutPreset.Carousel,
            ["compact-grid"] = LauncherLayoutPreset.CompactGrid,
        };
    private static readonly IReadOnlyDictionary<string, LauncherResponsiveBranch> BranchNames =
        new Dictionary<string, LauncherResponsiveBranch>(StringComparer.Ordinal)
        {
            ["compact"] = LauncherResponsiveBranch.Compact,
            ["standard"] = LauncherResponsiveBranch.Standard,
            ["wide"] = LauncherResponsiveBranch.Wide,
        };
    private static readonly IReadOnlyDictionary<string, LauncherLayoutPrimitive> PrimitiveNames =
        EnumNames<LauncherLayoutPrimitive>(["region", "grid", "stack", "overlay", "inset"]);
    private static readonly IReadOnlyDictionary<string, LauncherSlot> SlotNames =
        EnumNames<LauncherSlot>(["hero-background", "game-rail", "details-panel", "collection-tabs", "source-status", "operation-status", "system-status", "controller-hints"]);
    private static readonly IReadOnlyDictionary<string, LauncherOrientation> OrientationNames =
        EnumNames<LauncherOrientation>(["horizontal", "vertical"]);
    private static readonly IReadOnlyDictionary<string, LauncherAlignment> AlignmentNames =
        EnumNames<LauncherAlignment>(["start", "center", "end", "stretch"]);
    private static readonly IReadOnlyDictionary<string, LauncherSurfaceRole> SurfaceNames =
        EnumNames<LauncherSurfaceRole>(["solid", "glass"]);
    private static readonly IReadOnlyDictionary<string, LauncherContentDensity> DensityNames =
        EnumNames<LauncherContentDensity>(["compact", "standard", "expanded"]);

    private static IReadOnlyDictionary<string, T> EnumNames<T>(IReadOnlyList<string> names) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return names.Select((name, index) => (name, value: values[index]))
            .ToDictionary(item => item.name, item => item.value, StringComparer.Ordinal);
    }
}
