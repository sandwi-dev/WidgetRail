using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
namespace WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
internal sealed record CaptureTransportStatus(WebCaptureStreamPhase Phase,string? Reason,bool WorkerExited,bool ForcedTermination);
internal interface IWebViewCaptureTransport:IAsyncDisposable
{
    bool IsRunning {get;}
    bool HasEnded {get;}
    object Statistics {get;}
    Task StartAsync(NativeWindowPreviewTarget target,bool includeAudio,LiveCaptureQuality? requestedQuality=null,CancellationToken cancellationToken=default);
    Task StopAsync();
}
internal static class CaptureSessionIdentity
{
    private static long next;
    internal static long Next()
    {
        var value=Interlocked.Increment(ref next);
        return value<=9007199254740991L?value:throw new InvalidOperationException("Capture session identity exhausted.");
    }
}
