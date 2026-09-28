using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal event Action? ControllerGuideChanged;
    private void NotifyControllerGuideChanged() => ControllerGuideChanged?.Invoke();

    internal IReadOnlyList<ControllerGuideHint> CaptureControllerGuide()
    {
        if (disposed || applying || presentationOnly || !presentationActive || presentation is null) return [];
        if (HasTransientControl) return (ControllerGuideHint[])[new(ControllerPrompt.A, "Select", ControllerButton.A)];
        var focused = FocusedBinding();
        var path = new List<ViewNode>();
        var declaration = focused is not null && Eligible(focused) ? declarations.GetValueOrDefault(focused.Identity.Id) :
            declarations.Values.FirstOrDefault(value => value.Identity.Scope == activeScope &&
                (value.ParentId is null || declarations[value.ParentId].Identity.Scope != activeScope));
        for (; declaration is not null && declaration.Identity.Scope == activeScope;
            declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            path.Add(declaration.Node);
        path.Reverse();
        if (focused is { Element: WidgetIndexedCollectionView indexed })
        {
            // No placeholder, stale lease, or detached row lends action authority.
            if (indexed.CaptureContextRow() is not { } row) return [];
            path.Add(row.Row.Item.Root);
        }
        var leaf = focused is null || path.Count == 0 ? null : path[^1];
        var activation = leaf is { IsDisabled: not true, IsBusy: not true } &&
            (leaf.Kind is ViewNodeKind.Button or ViewNodeKind.ActionSurface or ViewNodeKind.Slider && leaf.ActionId is not null ||
             leaf.Kind == ViewNodeKind.Select && leaf.SelectOptions.Any(option => !option.IsDisabled && !option.IsBusy) ||
             leaf.Kind == ViewNodeKind.TextEntry && leaf.ActionId is not null ||
             leaf.Kind == ViewNodeKind.Slider && leaf.SliderInteractionMode == SliderInteractionMode.ActivateToAdjust);
        var context = new HashSet<ControllerButton>();
        var unavailableContext = new HashSet<ControllerButton>();
        foreach (var button in new[] { ControllerButton.Menu, ControllerButton.X, ControllerButton.Y })
            if (FindContextTarget(button) is { } target)
            {
                if (HasAvailableActions(target.Node)) context.Add(button);
                else unavailableContext.Add(button);
            }
        return ControllerGuideModel.Resolve(path, context, activation, unavailableContext);
    }
}
