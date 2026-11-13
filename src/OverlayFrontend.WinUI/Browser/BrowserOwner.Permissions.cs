using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal Func<BrowserSurface, WebSessionPermissionKind, CancellationToken, Task<WebSessionPermissionResult>>? RequestPermission { get; set; }
    private readonly Dictionary<string,CancellationTokenSource> hostedDecisionRequests=[];
}
