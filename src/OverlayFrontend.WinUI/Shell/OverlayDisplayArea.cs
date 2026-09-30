using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal static class OverlayDisplayArea
{
    internal static DisplayArea? Resolve(WindowId preferred, WindowId owner)
    {
        // A remembered foreground window can be destroyed between overlay shows.
        // Nearest chooses a monitor for a valid window; it does not guarantee a
        // result for a retired WindowId or during display topology changes.
        var display = preferred.Value != 0 ? DisplayArea.GetFromWindowId(preferred, DisplayAreaFallback.Nearest) : null;
        if (display is null && owner.Value != 0 && owner.Value != preferred.Value)
            display = DisplayArea.GetFromWindowId(owner, DisplayAreaFallback.Nearest);
        return display ?? DisplayArea.Primary;
    }
}
