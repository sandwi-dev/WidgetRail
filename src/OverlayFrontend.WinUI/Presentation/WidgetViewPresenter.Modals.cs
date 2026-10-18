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
            // Dialog motion uses a compositor timeline. External/native surfaces
            // need the page renderer's synchronized placement contract; until a
            // dialog has that contract, keep it stable instead of fading a live
            // swap chain or moving a WebView on a different clock.
            layer.ApplyAppearance(HasDialogSurface(dialog) ? appearance with { AnimateWidgetModals = false } : appearance, systemAnimationsEnabled);
            layer.Configure(bindings[node.Children[0].Id].LayoutElement, bindings[dialog.Id].LayoutElement,
                Length(dialog, "width") ?? 760, Length(dialog, "height") ?? 640,
                Length(dialog, "max-width") ?? double.PositiveInfinity,
                Length(dialog, "max-height") ?? double.PositiveInfinity, bindings[dialog.Id].Element,
                node.AnchoredPresentation, () => ResolvePresentationAnchor(node.AnchoredPresentation?.Anchor, this));
        }
    }
    private static bool HasDialogSurface(ViewNode node) => node.Kind is ViewNodeKind.MediaPlayer or ViewNodeKind.CapturedMedia or
        ViewNodeKind.MediaViewport or ViewNodeKind.WebBrowser or ViewNodeKind.WindowPreview || node.Children.Any(HasDialogSurface);

    private bool IsModalDialog(Declaration declaration, IReadOnlyDictionary<string, Declaration> plan) =>
        declaration.ParentId is { } parent && plan.TryGetValue(parent, out var owner) &&
        owner.Node.Kind == ViewNodeKind.ModalLayer && owner.Node.Children[1].Id == declaration.Node.Id;

    private bool RejectInactiveFocus(Microsoft.UI.Xaml.Input.GettingFocusEventArgs args)
    {
        if (!presentationInputEnabled && !restoringRetainedFocusPresentation && FindBinding(args.NewFocusedElement) is not null) { args.TryCancel(); return true; }
        if (applying || frame is null || FindBinding(args.NewFocusedElement) is not { } incoming ||
            incoming.Identity.Scope == activeScope) return false;
        args.TryCancel();
        return true;
    }
}
