using System.Collections.ObjectModel;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetBridge;

internal static class BridgeRenderStyleResolver
{
    public static IReadOnlyDictionary<string, BridgeNodeRenderStyles> Resolve(
        ViewSnapshot snapshot,
        WrssTheme? theme)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ResolveRoots(EnumerateRoots(), theme);

        IEnumerable<(ViewNode Root, string Prefix)> EnumerateRoots()
        {
            yield return (snapshot.Root, string.Empty);
            foreach (var layout in snapshot.PinnedLayouts)
                if (layout.Root is { } root) yield return (root, layout.Id + "/");
        }
    }

    internal static IReadOnlyDictionary<string, BridgeNodeRenderStyles> ResolveRange(
        IndexedCollectionRange range, WrssTheme? theme)
    {
        ArgumentNullException.ThrowIfNull(range);
        return ResolveRoots(range.Items.Select(item => (item.Root, string.Empty)), theme);
    }

    private static IReadOnlyDictionary<string, BridgeNodeRenderStyles> ResolveRoots(
        IEnumerable<(ViewNode Root, string Prefix)> roots, WrssTheme? theme)
    {
        var nodes = new SortedDictionary<string, BridgeNodeRenderStyles>(StringComparer.Ordinal);
        var totalProperties = 0;
        foreach (var (root, prefix) in roots) Visit(root, prefix);
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
            var headerStyle = node.Kind == ViewNodeKind.IndexedCollection && node.IndexedGroups is not null
                ? ResolveState(new ViewNode { Id = node.Id + ".group-header", Kind = ViewNodeKind.Text },
                    new HashSet<string>(["wrail-section-header__title"], StringComparer.Ordinal), new HashSet<WrssPseudoState>()) : null;
            totalProperties += baseStyle.Count + focusedStyle.Count + pressedStyle.Count + (headerStyle?.Count ?? 0);
            if (totalProperties > BridgeRenderStyleLimits.MaximumTotalProperties)
                throw new BridgeProtocolException(
                    $"Computed style map exceeds {BridgeRenderStyleLimits.MaximumTotalProperties} properties.");
            nodes.Add(prefix + node.Id, new BridgeNodeRenderStyles
            {
                Base = baseStyle,
                Focused = focusedStyle,
                Pressed = pressedStyle,
                GroupHeader = headerStyle,
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
        ViewNodeKind.Scroll or ViewNodeKind.IndexedCollection => "scroll",
        ViewNodeKind.Text => "text",
        ViewNodeKind.Button => "button",
        ViewNodeKind.Progress => "progress",
        ViewNodeKind.Slider => "slider",
        ViewNodeKind.Spacer => "spacer",
        ViewNodeKind.Image => "image",
        ViewNodeKind.Icon => "icon",
        ViewNodeKind.ModalLayer => "modalLayer",
        ViewNodeKind.ControllerGlyph => "controllerGlyph",
        ViewNodeKind.LoadingIndicator => "loadingIndicator",
        ViewNodeKind.ActionSurface => "actionSurface",
        ViewNodeKind.Grid => "grid",
        ViewNodeKind.TextEntry => "textEntry",
        ViewNodeKind.MediaViewport => "mediaViewport",
        ViewNodeKind.BackgroundSurface => "backgroundSurface",
        ViewNodeKind.FocusPresentationSurface => "focusPresentationSurface",
        ViewNodeKind.Select => "select",
        ViewNodeKind.WindowPreview => "windowPreview",
        _ => throw new BridgeProtocolException($"Unsupported view node kind '{kind}'."),
    };
}
