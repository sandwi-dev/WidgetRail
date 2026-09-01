using System.Collections.ObjectModel;
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

internal static class BridgeRenderStyleResolver
{
    public static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Resolve(
        ViewSnapshot snapshot,
        WrssTheme? theme)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var nodes = new SortedDictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
        var totalProperties = 0;
        Visit(snapshot.Root, string.Empty);
        foreach (var layout in snapshot.PinnedLayouts)
            if (layout.Root is { } root) Visit(root, layout.Id + "/");
        return new ReadOnlyDictionary<string, BridgeNodeRenderStyles>(nodes);

        void Visit(ViewNode node, string prefix)
        {
            if (nodes.Count >= BridgeRenderStyleLimits.MaximumNodes)
                throw new BridgeProtocolException(
                    $"Computed style map exceeds {BridgeRenderStyleLimits.MaximumNodes} nodes.");
            var classes = new HashSet<string>(node.StyleClasses, StringComparer.Ordinal);
            var baseStates = SemanticStates(node);
            var focusedStates = new HashSet<WrssPseudoState>(baseStates)
            {
                WrssPseudoState.Focused,
            };
            var pressedStates = new HashSet<WrssPseudoState>(focusedStates)
            {
                WrssPseudoState.Pressed,
            };
            var baseStyle = ResolveState(node, classes, baseStates);
            var focusedStyle = ResolveState(node, classes, focusedStates);
            var pressedStyle = ResolveState(node, classes, pressedStates);
            totalProperties += baseStyle.Count + focusedStyle.Count + pressedStyle.Count;
            if (totalProperties > BridgeRenderStyleLimits.MaximumTotalProperties)
                throw new BridgeProtocolException(
                    $"Computed style map exceeds {BridgeRenderStyleLimits.MaximumTotalProperties} properties.");
            nodes.Add(prefix + node.Id, new BridgeNodeRenderStyles
            {
                Base = baseStyle,
                Focused = focusedStyle,
                Pressed = pressedStyle,
            });
            if (node.FocusPresentation is { } focusPresentation)
                Visit(focusPresentation, prefix);
            if (node.DefaultFocusPresentation is { } defaultFocusPresentation)
                Visit(defaultFocusPresentation, prefix);
            foreach (var child in node.Children) Visit(child, prefix);
        }

        static HashSet<WrssPseudoState> SemanticStates(ViewNode node)
        {
            var states = new HashSet<WrssPseudoState>();
            if (node.IsSelected is true) states.Add(WrssPseudoState.Selected);
            if (node.IsDisabled is true) states.Add(WrssPseudoState.Disabled);
            if (node.IsBusy is true) states.Add(WrssPseudoState.Busy);
            return states;
        }

        IReadOnlyDictionary<string, BridgeComputedStyleValue> ResolveState(
            ViewNode node,
            IReadOnlySet<string> classes,
            IReadOnlySet<WrssPseudoState> states)
        {
            if (theme is null)
                return new ReadOnlyDictionary<string, BridgeComputedStyleValue>(
                    new SortedDictionary<string, BridgeComputedStyleValue>(StringComparer.Ordinal));
            var resolved = theme.Resolve(new WrssElement(RoleFor(node.Kind), node.Id, classes, states));
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
        ViewNodeKind.LoadingIndicator => "loadingIndicator",
        ViewNodeKind.ActionSurface => "actionSurface",
        ViewNodeKind.Grid => "grid",
        ViewNodeKind.TextEntry => "textEntry",
        ViewNodeKind.MediaViewport => "mediaViewport",
        ViewNodeKind.BackgroundSurface => "backgroundSurface",
        ViewNodeKind.FocusPresentationSurface => "focusPresentationSurface",
        _ => throw new BridgeProtocolException($"Unsupported view node kind '{kind}'."),
    };
}
