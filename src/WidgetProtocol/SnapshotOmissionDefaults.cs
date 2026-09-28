using System.Text;
using System.Text.Json.Nodes;

namespace WidgetRail.WidgetProtocol;

/// <summary>
/// Preserves optional collection/version initializers when reading compact legacy
/// JSON through generated init-only metadata. Normal SDK snapshots already carry
/// these properties, pass validation, and never take this compatibility path.
/// Explicit nulls, unknown members and authority fields are never rewritten.
/// </summary>
internal static class SnapshotOmissionDefaults
{
    internal static bool TryRestore(ReadOnlySpan<byte> payload, out byte[] compatible)
    {
        compatible = [];
        if (JsonNode.Parse(payload) is not JsonObject root) return false;
        var changed = false;
        Add(root, "protocolVersion", JsonValue.Create(ProtocolConstants.CurrentVersion));
        Add(root, "quickActions", new JsonArray());
        Add(root, "pinnedLayouts", new JsonArray());
        Node(root["root"]);
        if (root["pinnedLayouts"] is JsonArray layouts)
            foreach (var layout in layouts.OfType<JsonObject>()) Node(layout["root"]);
        if (root["embeddedMediaSession"] is JsonObject media)
            foreach (var key in new[] { "resources", "commands", "allowedFrameOrigins", "allowedFrameDomainFamilies", "supportedPresentations" })
                Add(media, key, new JsonArray());
        if (!changed) return false;
        compatible = Encoding.UTF8.GetBytes(root.ToJsonString());
        return true;

        void Add(JsonObject target, string key, JsonNode? value)
        {
            if (target.ContainsKey(key)) return;
            target.Add(key, value); changed = true;
        }
        void Node(JsonNode? value)
        {
            if (value is not JsonObject node) return;
            foreach (var key in new[] { "contextActions", "selectOptions", "styleClasses", "shortcuts", "children" })
                Add(node, key, new JsonArray());
            Node(node["focusPresentation"]);
            Node(node["defaultFocusPresentation"]);
            if (node["children"] is JsonArray children)
                foreach (var child in children) Node(child);
        }
    }
}
