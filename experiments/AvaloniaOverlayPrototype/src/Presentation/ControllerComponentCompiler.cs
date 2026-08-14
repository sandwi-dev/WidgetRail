using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.Threading;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Presentation;

public enum ControllerComponentKind
{
    Page,
    PageHeader,
    Section,
    Card,
    ActionRow,
    Tile,
    Collection,
    Media,
    SliderRow,
    Status,
    BodyText,
    Spacer,
    TextEntry,
    AdvancedSlotSurface,
}

public enum ControllerNavigationZone
{
    None,
    Page,
    Component,
    Collection,
    Slider,
}

public readonly record struct ControllerComponent(
    ControllerComponentKind Kind,
    ControllerNavigationZone NavigationZone,
    string? ThemeResourceKey = null);

/// <summary>
/// Compiles the closed semantic protocol into the prototype's controller-first
/// component vocabulary. It deliberately has no descriptor, widget identity,
/// text, element ID, style class, provider, or ancestor-shape input.
/// </summary>
public static class ControllerComponentCompiler
{
    public static ControllerComponent Compile(ViewNode node, bool isRoot = false)
    {
        if (node.AdvancedPresentationSlot is not null)
            return new(ControllerComponentKind.AdvancedSlotSurface,
                ControllerNavigationZone.Component);
        if (isRoot)
            return new(ControllerComponentKind.Page, ControllerNavigationZone.Page);

        return node.Kind switch
        {
            ViewNodeKind.Stack => new(ControllerComponentKind.Section,
                ControllerNavigationZone.Component),
            ViewNodeKind.Row => new(ControllerComponentKind.ActionRow,
                ControllerNavigationZone.Component),
            ViewNodeKind.Scroll or ViewNodeKind.Grid => new(ControllerComponentKind.Collection,
                ControllerNavigationZone.Collection),
            ViewNodeKind.Text => new(ControllerComponentKind.BodyText,
                ControllerNavigationZone.None),
            ViewNodeKind.Button => new(ControllerComponentKind.Tile,
                ControllerNavigationZone.Component, "ControllerTileTheme"),
            ViewNodeKind.Progress or ViewNodeKind.LoadingIndicator => new(
                ControllerComponentKind.Status, ControllerNavigationZone.None),
            ViewNodeKind.Slider => new(ControllerComponentKind.SliderRow,
                ControllerNavigationZone.Slider),
            ViewNodeKind.Spacer => new(ControllerComponentKind.Spacer,
                ControllerNavigationZone.None),
            ViewNodeKind.Image or ViewNodeKind.Icon => new(ControllerComponentKind.Media,
                ControllerNavigationZone.None),
            ViewNodeKind.ActionSurface => new(ControllerComponentKind.Card,
                ControllerNavigationZone.Component, "ControllerCardTheme"),
            ViewNodeKind.TextEntry => new(ControllerComponentKind.TextEntry,
                ControllerNavigationZone.Component, "ControllerTextEntryTheme"),
            _ => throw new ArgumentOutOfRangeException(nameof(node.Kind)),
        };
    }

    public static void Apply(StyledElement element, ControllerComponent component)
    {
        element.Classes.Add("controller-component");
        element.Classes.Add("component-" + component.Kind.ToString().ToLowerInvariant());
        if (element is Control control)
            control.SetValue(ComponentProperties.NavigationZoneProperty, component.NavigationZone);
        if (component.ThemeResourceKey is not null && element is TemplatedControl templated &&
            Dispatcher.UIThread.CheckAccess() &&
            Application.Current?.TryFindResource(
                component.ThemeResourceKey,
                Application.Current.ActualThemeVariant,
                out var resource) == true && resource is ControlTheme theme)
            templated.Theme = theme;
    }
}

public sealed class ComponentProperties : AvaloniaObject
{
    private ComponentProperties()
    {
    }

    public static readonly AttachedProperty<ControllerNavigationZone> NavigationZoneProperty =
        AvaloniaProperty.RegisterAttached<ComponentProperties, Control, ControllerNavigationZone>(
            "NavigationZone", ControllerNavigationZone.None);
}
