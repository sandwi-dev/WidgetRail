using WidgetRail.OverlayFrontend.WinUI.Browser;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class NativePopupTheme
{
    internal static IDisposable? BrowserSlot(BrowserSlot slot)
    {
        var owner = Find(slot);
        return owner?.Track(() => slot.ApplyFocusTheme(owner.focus, owner.cornerRadius));
    }
    internal static IDisposable? Browser(BrowserSurface browser)
    {
        var owner = Find(browser);
        return owner?.Track(() => browser.ApplyTheme(owner.surface, owner.ink, owner.muted, owner.selected,
            owner.focus, owner.fontSize, owner.fontWeight, owner.font, owner.cornerRadius));
    }
}
