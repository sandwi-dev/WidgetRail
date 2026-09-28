using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private void UpdateModalGeometry()
    {
        foreach (var binding in bindings.Values)
        {
            if (binding.Element is not WidgetModalLayer layer) continue;
            var node = declarations[binding.Identity.Id].Node;
            var dialog = node.Children[1];
            layer.Configure(bindings[node.Children[0].Id].Element, bindings[dialog.Id].Element,
                Length(dialog, "width") ?? 760, Length(dialog, "height") ?? 640,
                Length(dialog, "max-width") ?? double.PositiveInfinity,
                Length(dialog, "max-height") ?? double.PositiveInfinity);
        }
    }

    private bool IsModalDialog(Declaration declaration, IReadOnlyDictionary<string, Declaration> plan) =>
        declaration.ParentId is { } parent && plan.TryGetValue(parent, out var owner) &&
        owner.Node.Kind == ViewNodeKind.ModalLayer && owner.Node.Children[1].Id == declaration.Node.Id;

    private bool RejectInactiveFocus(Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        if (applying || frame is null || FindBinding(args.NewFocusedElement) is not { } incoming ||
            incoming.Identity.Scope == frame.Authority.ActiveInputScopeId) return false;
        args.TryCancel();
        return true;
    }
}
