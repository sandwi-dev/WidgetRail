using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static partial class SettingsPresentation
{
    private static ContainerElement ArrangeSettingsRows(ContainerElement page)
    {
        var form = page.Id is "appearance.page" or "overlay.page" or "accessibility.page" or "controllers.page" or "controllers.exclusive.page";
        var children = new List<WidgetElement>();
        for (var index = 0; index < page.Children.Count; index++)
        {
            var child = page.Children[index];
            string? label = null;
            WidgetElement control = child;
            if (form)
            {
                switch (child)
                {
                    case SelectElement select:
                        label = select.Label;
                        control = (select with { ShowLabel = false }).AddClasses("setting-control");
                        break;
                    case ButtonElement button when button.StyleClasses.Contains("wrail-switch"):
                        var separator = button.Label.LastIndexOf("  ", StringComparison.Ordinal);
                        if (separator > 0)
                        {
                            label = button.Label[..separator];
                            control = (button with { Label = button.Label[(separator + 2)..] }).AddClasses("setting-control");
                        }
                        break;
                    case ButtonElement button when button.Label.Contains(": ", StringComparison.Ordinal):
                        var colon = button.Label.IndexOf(": ", StringComparison.Ordinal);
                        label = button.Label[..colon];
                        control = (button with { Label = button.Label[(colon + 2)..] + "  ›",
                            AccessibilityLabel = button.AccessibilityLabel ?? button.Label }).AddClasses("setting-control");
                        break;
                    case RowElement stepper when stepper.StyleClasses.Contains("wrail-stepper"):
                        label = ((TextElement)stepper.Children[0]).Text;
                        control = stepper with { Children = stepper.Children.Skip(1).ToArray() };
                        break;
                }
            }
            if (label is null) { children.Add(child); continue; }
            string? description = null;
            if (index + 1 < page.Children.Count && page.Children[index + 1] is TextElement help && help.StyleClasses.Contains("page-help") && help.Id != "settings.display")
            {
                description = help.Text;
                index++;
            }
            children.Add(UI.SettingsField(child.Id + ".row", label, control, description));
        }
        // Link rows, not individual buttons: a stepper is one vertical stop and
        // keeps the selected minus/plus column when moving to another stepper.
        var groups = children.Select(Focusable).Where(group => group.Count != 0).ToArray();
        var links = new Dictionary<string, (string? Up, string? Down)>();
        for (var row = 0; row < groups.Length; row++)
            for (var column = 0; column < groups[row].Count; column++)
                links[groups[row][column]] = (
                    row == 0 ? "settings.back" : groups[row - 1][Math.Min(column, groups[row - 1].Count - 1)],
                    row + 1 == groups.Length ? null : groups[row + 1][Math.Min(column, groups[row + 1].Count - 1)]);
        WidgetElement Link(WidgetElement element)
        {
            if (links.TryGetValue(element.Id, out var link))
            {
                if (element is ButtonElement button) return button with { FocusNeighbors = (button.FocusNeighbors ?? new()) with { Up = link.Up, Down = link.Down } };
                if (element is SelectElement select) return select with { FocusNeighbors = (select.FocusNeighbors ?? new()) with { Up = link.Up, Down = link.Down } };
                if (element is ActionSurfaceElement action) return action with { FocusNeighbors = (action.FocusNeighbors ?? new()) with { Up = link.Up, Down = link.Down } };
            }
            if (element is ContainerElement container) return container with { Children = container.Children.Select(Link).ToArray() };
            return element;
        }
        return page with { Children = children.Select(Link).ToArray() };
    }

    private static List<string> Focusable(WidgetElement element) => element switch
    {
        ButtonElement or SelectElement or ActionSurfaceElement => [element.Id],
        ContainerElement container => container.Children.SelectMany(Focusable).ToList(),
        _ => [],
    };
}
