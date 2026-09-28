using System.Text.Json.Serialization;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetBridge;

public static class BridgeRenderStyleLimits
{
    public const int MaximumNodes = ProtocolConstants.MaximumNodeCount;
    public const int MaximumPropertiesPerState = 64;
    public const int MaximumTotalProperties = 32_768;
    public const int MaximumPropertyNameLength = 128;
    public const int MaximumValueTextLength = ProtocolConstants.MaximumStringLength;
}

public sealed record BridgeComputedStyleValue
{
    public required WrssValueKind Kind { get; init; }
    public required string Text { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public double? Number { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Unit { get; init; }
}

public sealed record BridgeNodeRenderStyles
{
    public required IReadOnlyDictionary<string, BridgeComputedStyleValue> Base { get; init; }
    public required IReadOnlyDictionary<string, BridgeComputedStyleValue> Focused { get; init; }
    public required IReadOnlyDictionary<string, BridgeComputedStyleValue> Pressed { get; init; }
}
