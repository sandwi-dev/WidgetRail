using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private Binding? AdjacentScrollPanelTarget(Binding origin, FocusRectangle originBounds,
        FocusDirection direction, IEnumerable<Binding> candidates)
    {
        if (direction is not (FocusDirection.Left or FocusDirection.Right)) return null;
        var entries = new Dictionary<string, Binding>(StringComparer.Ordinal);
        var panels = new Dictionary<string, FocusRectangle?>(StringComparer.Ordinal);
        var choices = new List<FocusPanelCandidate>();
        foreach (var target in candidates)
        {
            if (!entries.TryAdd(target.Identity.Id, target) || !VisibleNavigationTarget(target) ||
                ClippedNavigationBounds(target.Element) is not { } entryBounds) continue;
            // Use the outermost external viewport: nested content must not make
            // a panel containing the origin look like a different destination.
            Binding? panel = null;
            for (var ancestor = declarations[target.Identity.Id]; ancestor is not null;
                ancestor = ancestor.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            {
                if (ancestor.Identity.Scope != activeScope || IsDescendant(origin.Identity.Id, ancestor.Identity.Id)) break;
                if (bindings[ancestor.Identity.Id] is { Element: ScrollViewer } viewport) panel = viewport;
            }
            if (panel is null) continue;
            if (!panels.TryGetValue(panel.Identity.Id, out var bounds))
                panels[panel.Identity.Id] = bounds = ClippedNavigationBounds(panel.Element);
            if (bounds is { } panelBounds) choices.Add(new(panel.Identity.Id, panelBounds, new(target.Identity.Id, entryBounds)));
        }
        var selected = DirectionalFocusPolicy.ChooseAdjacentPanel(originBounds, direction, choices);
        return selected is null ? null : entries[selected];
    }

    private FocusRectangle? ClippedNavigationBounds(FrameworkElement element)
    {
        if (NavigationBounds(element) is not { } bounds) return null;
        // A nested viewport's unscrolled extent is not its visible panel. Clip
        // against every scroll ancestor and the presenter before spatial ranking.
        for (DependencyObject? ancestor = VisualTreeHelper.GetParent(element); ancestor is not null;
            ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is FrameworkElement { Visibility: Visibility.Collapsed }) return null;
            if (ancestor is ScrollViewer || ReferenceEquals(ancestor, this))
            {
                if (NavigationBounds((FrameworkElement)ancestor) is not { } clip) return null;
                var x = Math.Max(bounds.X, clip.X); var y = Math.Max(bounds.Y, clip.Y);
                bounds = new(x, y, Math.Min(bounds.X + bounds.Width, clip.X + clip.Width) - x,
                    Math.Min(bounds.Y + bounds.Height, clip.Y + clip.Height) - y);
                if (!bounds.IsUsable) return null;
            }
            if (ReferenceEquals(ancestor, this)) break;
        }
        return bounds;
    }
}
