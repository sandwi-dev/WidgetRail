using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.WidgetProtocol;

/// <summary>Package declarations only. These do not authorize delivery or grant platform capabilities.</summary>
public sealed record WidgetIntentDeclarations
{
    public IReadOnlyList<WidgetIntentContract> Requests { get; init; } = [];
    public IReadOnlyList<WidgetIntentHandler> Handles { get; init; } = [];
}

public record WidgetIntentContract(string Id, int Version, JsonElement PayloadSchema);

/// <summary>Receiver policy for one contract/version mapping, not for the widget as a whole.</summary>
public sealed record WidgetIntentHandler : WidgetIntentContract
{
    [JsonConstructor]
    public WidgetIntentHandler(string id, int version, JsonElement payloadSchema)
        : base(id, version, payloadSchema) { }

    public WidgetIntentHandler(WidgetIntentContract contract)
        : this(contract.Id, contract.Version, contract.PayloadSchema) { }

    /// <summary>Allows host-validated delivery into an existing passive surface without taking focus.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SupportsPassiveDelivery { get; init; }

    /// <summary>Query this handler's current readiness before presenting or delivering an intent.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasDynamicAvailability { get; init; }
}

public static class WidgetIntentContracts
{
    public const int MaximumDeclarations = 16;
    public const string OpenWebPage = "widgetrail.web.open";
    public const string OpenVideo = "widgetrail.video.open";
    public const string SearchVideo = "widgetrail.video.search";
    public const string OpenWidgetSettings = "widgetrail.settings.widget.open";
    public const string OpenWidgetPermissions = "widgetrail.settings.permissions.open";
    public const string OpenMarketplace = "widgetrail.marketplace.open";
    public const string OpenAppUpdates = "widgetrail.settings.updates.open";
    public const string OpenAppearance = "widgetrail.settings.appearance.open";
    public static WidgetIntentContract Appearance { get; } = new(OpenAppearance, 1, Schema("""
        {"type":"object","additionalProperties":false,"properties":{}}
        """));
    public static WidgetIntentContract AppUpdates { get; } = new(OpenAppUpdates, 1, Schema("""
        {"type":"object","additionalProperties":false,"properties":{}}
        """));
    public static WidgetIntentContract Marketplace { get; } = new(OpenMarketplace,1,Schema("""
        {"type":"object","additionalProperties":false,"properties":{}}
        """));

    public static WidgetIntentContract WidgetSettings { get; } = new(OpenWidgetSettings, 1, Schema("""
        {"type":"object","additionalProperties":false,"properties":{
          "packageId":{"type":"string","minLength":3,"maxLength":128}
        },"required":["packageId"]}
        """));
    public static WidgetIntentContract WidgetPermissions { get; } = new(OpenWidgetPermissions, 1, WidgetSettings.PayloadSchema);

    public static WidgetIntentContract VideoSearch { get; } = new(SearchVideo, 1, Schema("""
        {"type":"object","additionalProperties":false,"properties":{
          "provider":{"type":"string","minLength":1,"maxLength":64},
          "query":{"type":"string","minLength":1,"maxLength":240}
        },"required":["provider","query"]}
        """));

    public static WidgetIntentContract Web { get; } = new(OpenWebPage, 1, Schema("""
        {"type":"object","additionalProperties":false,"properties":{
          "url":{"type":"string","minLength":1,"maxLength":2048}
        },"required":["url"]}
        """));
    /// <summary>Version 2 preserves long generated links such as weighted trade searches. Version 1 remains unchanged.</summary>
    public static WidgetIntentContract WebV2 { get; } = new(OpenWebPage, 2, Schema("""
        {"type":"object","additionalProperties":false,"properties":{
          "url":{"type":"string","minLength":1,"maxLength":8192}
        },"required":["url"]}
        """));
    public static WidgetIntentContract ForWebUrl(string url) => url.Length > 2048 ? WebV2 : Web;

    public static WidgetIntentContract Video { get; } = new(OpenVideo, 1, Schema("""
        {"type":"object","additionalProperties":false,"properties":{
          "provider":{"type":"string","minLength":1,"maxLength":64},
          "videoId":{"type":"string","minLength":1,"maxLength":256},
          "startTimeSeconds":{"type":"number","minimum":0,"maximum":604800}
        },"required":["provider","videoId"]}
        """));

    public static bool IsValidId(string? value) => value is { Length: >= 3 and <= 128 } &&
        value.Contains('.') && value.Split('.').All(part => part.Length > 0 &&
            part[0] is >= 'a' and <= 'z' &&
            part.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'));

    public static void ValidateDeclarations(WidgetIntentDeclarations? declarations,
        Action<string, string, string> add)
    {
        if (declarations is null) return;
        if (declarations.Requests is null || declarations.Handles is null)
        {
            add("$.intents", "required", "Intent request and handler lists cannot be null.");
            return;
        }
        if ((long)declarations.Requests.Count + declarations.Handles.Count > MaximumDeclarations)
        {
            add("$.intents", "too_many_intents", "Too many intent declarations.");
            return;
        }
        var contracts = new Dictionary<(string, int), string>();
        Check(declarations.Requests, "requests");
        Check(declarations.Handles, "handles");

        void Check(IReadOnlyList<WidgetIntentContract> entries, string kind)
        {
            var seen = new HashSet<(string, int)>();
            for (var i = 0; i < entries.Count; i++)
            {
                var path = $"$.intents.{kind}[{i}]";
                try
                {
                    var compiled = CompiledWidgetIntentContract.Create(entries[i]);
                    var key = (compiled.Id, compiled.Version);
                    if (!seen.Add(key)) add(path, "duplicate_intent", "Intent is declared more than once.");
                    if (contracts.TryGetValue(key, out var digest) && digest != compiled.SchemaDigest)
                        add(path, "conflicting_intent", "The same intent version has conflicting schemas.");
                    contracts[key] = compiled.SchemaDigest;
                    var standard = compiled.Id switch { OpenWebPage => compiled.Version == 2 ? WebV2 : Web, OpenVideo => Video, SearchVideo => VideoSearch,
                        OpenWidgetSettings => WidgetSettings, OpenWidgetPermissions => WidgetPermissions, OpenMarketplace => Marketplace, OpenAppUpdates => AppUpdates, OpenAppearance => Appearance, _ => null };
                    if (compiled.Id.StartsWith("widgetrail.", StringComparison.Ordinal) &&
                        (standard is null || compiled.Version != standard.Version ||
                         compiled.SchemaDigest != CompiledWidgetIntentContract.Create(standard).SchemaDigest))
                        add(path, "reserved_intent", "This host-owned contract is unknown or differs from its definition.");
                }
                catch (ArgumentException)
                {
                    add(path, "invalid_intent", "Intent identity or bounded payload schema is invalid.");
                }
            }
        }
    }

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
