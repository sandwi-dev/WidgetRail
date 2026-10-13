namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    // Children share their root's profile and lifetime, but consume native
    // resources separately. Reserve before asynchronous controller creation.
    private readonly HashSet<BrowserSurface> popupOwners = [];
    private bool ReservePopup(BrowserSurface parent) => !retired &&
        entries.Values.Any(entry => ReferenceEquals(entry.Surface, parent)) &&
        popupOwners.Count < 2 && popupOwners.Add(parent);
}
