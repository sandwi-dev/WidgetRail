using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    internal BrowserOwner? BrowserOwner { get; set; }
    private long browserPointerAt;
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
        var now = Environment.TickCount64;
        if (FocusedBrowser is not { IsBrowsing: true } slot || slot.Surface?.HasDialog == true || !presentationInputEnabled)
        { browserPointerAt = now; return false; }
        var elapsed = browserPointerAt == 0 ? 0 : Math.Clamp(now - browserPointerAt, 0, 50);
        browserPointerAt = now;
        slot.Surface!.MovePointer(Axis(x) * elapsed * .0006, -Axis(y) * elapsed * .0006);
        return true;
        static double Axis(short value) => Math.Abs((int)value) < 7849 ? 0 : value / 32768d;
    }
}
