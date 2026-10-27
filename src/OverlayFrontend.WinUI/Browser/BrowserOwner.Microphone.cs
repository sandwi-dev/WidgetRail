using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal Func<BrowserSurface,CancellationToken,Task<WebSessionMicrophoneResult>>? RequestMicrophone { get; set; }
    private readonly Dictionary<string,CancellationTokenSource> hostedDecisionRequests=[];
}
