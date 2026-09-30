using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal (object Identity, bool Available)? CaptureHeldAction(ControllerButton button)
    {
        if (!presentationInputEnabled || !presentationActive || disposed || applying || presentationOnly ||
            presentation is not { IsCurrent: true } displayed || HasTransientControl || textEntryPopup is not null) return null;
        var focused = FocusedBinding();
        var path = ControllerFocusPath(includeUnavailable: true);
        if (path.Count == 0) return null;
        var resolved = ControllerShortcutResolver.ResolvePath(path, button, ControllerEventPhase.Pressed);
        if (resolved.Shortcut is not { RepeatPolicy: ControllerActionRepeatPolicy.WhileHeld } shortcut || resolved.Owner is not { } owner)
            return null;
        var a = displayed.Frame.Authority;
        object? rowIdentity = focused?.Element is WidgetIndexedCollectionView indexed && indexed.CaptureContextRow() is { } row
            ? (row.Row.Owner, row.Row.Item.Key) : null;
        return ((this, a.WidgetId, a.WidgetInstanceId, a.RuntimeGeneration, a.PresentationGeneration, a.SessionGeneration,
            displayed.Selection, displayed.Scope, focused?.Identity, rowIdentity, owner.Id, shortcut.ActionId, button),
            resolved.Status == ControllerShortcutResolutionStatus.Resolved);
    }

    private List<ViewNode> ControllerFocusPath(bool includeUnavailable = false)
    {
        var focused = FocusedBinding();
        var path = new List<ViewNode>();
        var declaration = focused is not null && (includeUnavailable || Eligible(focused) || ReferenceEquals(adjustingSlider, focused)) ? declarations.GetValueOrDefault(focused.Identity.Id) :
            declarations.Values.FirstOrDefault(value => value.Identity.Scope == activeScope &&
                (value.ParentId is null || declarations[value.ParentId].Identity.Scope != activeScope));
        for (; declaration is not null && declaration.Identity.Scope == activeScope;
            declaration = declaration.ParentId is { } parent ? declarations.GetValueOrDefault(parent) : null)
            path.Add(declaration.Node);
        path.Reverse();
        if (focused?.Element is WidgetIndexedCollectionView indexed)
        {
            if (indexed.CaptureContextRow() is not { } row) return [];
            path.Add(row.Row.Item.Root);
        }
        return path;
    }
}
