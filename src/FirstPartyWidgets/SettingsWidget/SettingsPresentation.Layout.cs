using WidgetRail.WidgetSdk;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.FirstPartyWidgets.Settings;

internal static partial class SettingsPresentation
{
    internal static GridElement Field(string label, WidgetElement control, string? description = null) =>
        UI.SettingsField(control.Id + ".row", label, control.AddClasses("setting-control"), description);

    internal static GridElement Field(string label, RowElement control, string? description = null) =>
        UI.SettingsField(control.Id + ".row", label, control, description);

    private static ContainerElement LinkSettingsRows(ContainerElement page)
    {
        var children = page.Children;
        // Link rows, not individual buttons: a stepper is one vertical stop and
        // keeps the selected minus/plus column when moving to another stepper.
        var groups = children.SelectMany(FocusRows).Where(group => group.Count != 0).ToArray();
        var links = new Dictionary<string, (string? Up, string? Down)>();
        for (var row = 0; row < groups.Length; row++)
            for (var column = 0; column < groups[row].Count; column++)
                links[groups[row][column]] = (
                    row == 0 ? "settings.back" : groups[row - 1][Math.Min(column, groups[row - 1].Count - 1)],
                    row + 1 == groups.Length ? null : groups[row + 1][Math.Min(column, groups[row + 1].Count - 1)]);
        WidgetElement Link(WidgetElement element)
        {
            // Responsive action grids own their internal spatial navigation;
            // fixed cross-row links would skip buttons after a one-column reflow.
            if (element is GridElement && !element.StyleClasses.Contains("wrail-setting-row")) return element;
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

    private static IEnumerable<List<string>> FocusRows(WidgetElement element)
    {
        if (element is GridElement field && field.StyleClasses.Contains("wrail-setting-row"))
            return field.Children.OfType<StackElement>().Where(child => child.StyleClasses.Contains("wrail-setting-row__control"))
                .SelectMany(FocusRows);
        if (element is StackElement stack) return stack.Children.SelectMany(FocusRows);
        return [Focusable(element)];
    }

    private static List<string> Focusable(WidgetElement element) => element switch
    {
        ButtonElement or SelectElement or ActionSurfaceElement or IndexedCollectionElement => [element.Id],
        ContainerElement container => container.Children.SelectMany(Focusable).ToList(),
        _ => [],
    };
}
