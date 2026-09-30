using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Authored action state is informational; it does not create a native toggle or selection command.</summary>
internal static class WidgetAccessibleState
{
    internal static string ItemStatus(ViewNode? node) => (node?.IsSelected == true, node?.IsBusy == true) switch
    {
        (true, true) => "Selected, Busy",
        (true, false) => "Selected",
        (false, true) => "Busy",
        _ => string.Empty,
    };
}
