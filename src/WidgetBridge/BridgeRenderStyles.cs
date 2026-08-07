using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.WidgetBridge;

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
    public required GbssValueKind Kind { get; init; }
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
}

internal static class BridgeRenderStyleResolver
{
    public static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Resolve(
        ViewSnapshot snapshot,
        GbssTheme? theme)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var nodes = new SortedDictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
        var totalProperties = 0;
        Visit(snapshot.Root);
        return new ReadOnlyDictionary<string, BridgeNodeRenderStyles>(nodes);

        void Visit(ViewNode node)
        {
            if (nodes.Count >= BridgeRenderStyleLimits.MaximumNodes)
                throw new BridgeProtocolException(
                    $"Computed style map exceeds {BridgeRenderStyleLimits.MaximumNodes} nodes.");
            var classes = new HashSet<string>(node.StyleClasses, StringComparer.Ordinal);
            var baseStates = SemanticStates(node);
            var focusedStates = new HashSet<GbssPseudoState>(baseStates)
            {
                GbssPseudoState.Focused,
            };
            var baseStyle = ResolveState(node, classes, baseStates);
            var focusedStyle = ResolveState(node, classes, focusedStates);
            totalProperties += baseStyle.Count + focusedStyle.Count;
            if (totalProperties > BridgeRenderStyleLimits.MaximumTotalProperties)
                throw new BridgeProtocolException(
                    $"Computed style map exceeds {BridgeRenderStyleLimits.MaximumTotalProperties} properties.");
            nodes.Add(node.Id, new BridgeNodeRenderStyles
            {
                Base = baseStyle,
                Focused = focusedStyle,
            });
            foreach (var child in node.Children) Visit(child);
        }

        static HashSet<GbssPseudoState> SemanticStates(ViewNode node)
        {
            var states = new HashSet<GbssPseudoState>();
            if (node.IsSelected is true) states.Add(GbssPseudoState.Selected);
            if (node.IsDisabled is true) states.Add(GbssPseudoState.Disabled);
            if (node.IsBusy is true) states.Add(GbssPseudoState.Busy);
            return states;
        }

        IReadOnlyDictionary<string, BridgeComputedStyleValue> ResolveState(
            ViewNode node,
            IReadOnlySet<string> classes,
            IReadOnlySet<GbssPseudoState> states)
        {
            if (theme is null)
                return new ReadOnlyDictionary<string, BridgeComputedStyleValue>(
                    new SortedDictionary<string, BridgeComputedStyleValue>(StringComparer.Ordinal));
            var resolved = theme.Resolve(new GbssElement(RoleFor(node.Kind), node.Id, classes, states));
            if (resolved.Properties.Count > BridgeRenderStyleLimits.MaximumPropertiesPerState)
                throw new BridgeProtocolException(
                    $"Node '{node.Id}' has too many computed style properties.");
            var values = new SortedDictionary<string, BridgeComputedStyleValue>(StringComparer.Ordinal);
            foreach (var (property, value) in resolved.Properties)
            {
                if (property.Length == 0 || property.Length > BridgeRenderStyleLimits.MaximumPropertyNameLength ||
                    value.Text.Length > BridgeRenderStyleLimits.MaximumValueTextLength ||
                    value.Unit is { Length: > 16 } ||
                    value.Number is { } number && !double.IsFinite(number))
                    throw new BridgeProtocolException($"Node '{node.Id}' produced an invalid computed style value.");
                values.Add(property, new BridgeComputedStyleValue
                {
                    Kind = value.Kind,
                    Text = value.Text,
                    Number = value.Number,
                    Unit = value.Unit,
                });
            }
            return new ReadOnlyDictionary<string, BridgeComputedStyleValue>(values);
        }
    }

    private static string RoleFor(ViewNodeKind kind) => kind switch
    {
        ViewNodeKind.Stack => "stack",
        ViewNodeKind.Row => "row",
        ViewNodeKind.Scroll => "scroll",
        ViewNodeKind.Text => "text",
        ViewNodeKind.Button => "button",
        ViewNodeKind.Progress => "progress",
        ViewNodeKind.Slider => "slider",
        ViewNodeKind.Spacer => "spacer",
        ViewNodeKind.Image => "image",
        ViewNodeKind.Icon => "icon",
        _ => throw new BridgeProtocolException($"Unsupported view node kind '{kind}'."),
    };
}
