using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal BrowserOwner? BrowserOwner { get; set; }
    private BrowserSlot? FocusedBrowser => bindings.Values.Select(binding => binding.Element).OfType<BrowserSlot>()
        .FirstOrDefault(slot => slot.AcceptsInput && slot.Surface?.HasDialog == true) ??
        (FocusedBinding() is { Element: BrowserSlot slot } binding && Navigable(binding) ? slot : null);
    private void UpdateBrowserSlot(BrowserSlot slot, ViewNode node, string scope)
    {
        slot.IsProviderDocument = node.WebBrowser?.ProviderDocument is not null;
        slot.Active = presentationActive && !disposed && !presentationOnly;
        slot.InteractionChanged = NotifyControllerGuideChanged;
        slot.AcceptsInput = slot.Active && presentationInputEnabled && scope == activeScope && node.IsDisabled != true && !HasTransientControl;
        slot.Surface?.SetPresentation(slot.CanDisplay, slot.AcceptsInput);
        slot.RefreshFocusPresentation();
        if (slot.Active && frame is { } displayed && node.WebBrowser is not null)
            BrowserOwner?.Bind(displayed, node.Id, slot, presentation?.Selection, presentation?.Projection);
        else BrowserOwner?.Unbind(slot);
    }
    private void RefreshBrowsers()
    {
        foreach (var binding in bindings.Values)
            if (binding.Element is BrowserSlot slot && declarations.TryGetValue(binding.Identity.Id, out var declaration))
                UpdateBrowserSlot(slot, declaration.Node, binding.Identity.Scope);
    }
    internal bool MoveBrowserPointer(short x, short y)
    {
        return presentationInputEnabled && FocusedBrowser?.MovePointer(x, y) == true;
    }
}
