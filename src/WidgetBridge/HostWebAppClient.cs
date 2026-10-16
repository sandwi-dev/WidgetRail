using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetBridge;

/// <summary>
/// A static, host-authored browser view. There is no widget executable or script
/// to load: website execution stays in the existing WebView host. Start ordinals
/// represent presentation lifetimes, not OS processes.
/// </summary>
internal sealed class HostWebAppClient(ConfiguredWidget configured):IBridgeWidgetClient
{
    private readonly object gate=new();
    private bool running,disposed;
    private int starts;
    private long sequence;
    public event EventHandler<long>? Invalidated {add{}remove{}}
    public event EventHandler<WidgetActionFailure>? ActionFailed {add{}remove{}}
    public event EventHandler<WidgetFailure>? Failed {add{}remove{}}
    public event EventHandler<WidgetProcessLifetimeDiagnostic>? LifetimeChanged {add{}remove{}}
    public bool IsRunning {get{lock(gate)return running;}}
    public int Starts {get{lock(gate)return starts;}}
    private void Start()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(!running){running=true;starts++;}
    }
    public Task<ViewSnapshot> GetSnapshotAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock(gate)
        {
            Start();
            try
            {
            var app=configured.UserWebApp!;
            // Off-origin external navigation is an HTTPS-only policy. HTTP
            // sites (including local tools) retain the browser's in-widget route.
            var options=WebBrowserOptions.WebApp;
            if(new Uri(app.Url).Scheme!="https")options=options with{ExternalLinks=BrowserExternalLinkMode.InWidget};
            var page=new WebBrowserDocument("webapp.session",1,app.Url,"Webapp"){Options=options};
            var view=new WidgetView(UI.Grid("webapp.root",[GridTrack.Star()],[GridTrack.Star()],
                UI.WebBrowser(page,BrowserInteractionMode.InteractOnFocus,"webapp.page")),"webapp.page",Surface:new()
                {Mode=WidgetSurfaceMode.Standard,PreferredWidth=1100,PreferredHeight=760,MinimumWidth=480,MinimumHeight=360});
            return Task.FromResult(view.CreateSnapshot(configured.InstanceId,++sequence));
            }
            catch(Exception error) when(error is not OutOfMemoryException and not OperationCanceledException)
            {
                // Match the ordinary worker failure boundary: a bad authored
                // view must fail this widget rather than close the bridge.
                throw new WidgetProcessException("The local webapp could not be displayed.",error);
            }
        }
    }
    public Task SetLifecycleStateAsync(WidgetLifecycleState state,CancellationToken token)
    {token.ThrowIfCancellationRequested();lock(gate){if(state==WidgetLifecycleState.Destroying)running=false;else Start();}return Task.CompletedTask;}
    public Task<bool> TryRestoreLifecycleStateAsync(WidgetLifecycleState state,int expectedStartOrdinal,CancellationToken token)
    {token.ThrowIfCancellationRequested();lock(gate)return Task.FromResult(!disposed&&running&&starts==expectedStartOrdinal);}
    public Task<WidgetOperationAdmission> AdmitActionAsync(WidgetActionEvent action,CancellationToken token,int? expectedStartOrdinal=null)
    {token.ThrowIfCancellationRequested();return Task.FromResult(WidgetOperationAdmission.RejectedInactive);}
    public Task<bool> SendControllerInputAsync(ControllerInputEvent input,WidgetDashboardGestureAuthority? authority,CancellationToken token,int? expectedStartOrdinal=null)
    {token.ThrowIfCancellationRequested();return Task.FromResult(false);}
    public Task SendEmbeddedMediaPlaybackEventAsync(EmbeddedMediaPlaybackEvent playbackEvent,CancellationToken token,int expectedStartOrdinal)
    {token.ThrowIfCancellationRequested();return Task.CompletedTask;}
    public Task UnloadAsync(CancellationToken token)
    {token.ThrowIfCancellationRequested();lock(gate)running=false;return Task.CompletedTask;}
    public ValueTask DisposeAsync(){lock(gate){running=false;disposed=true;}return ValueTask.CompletedTask;}
}
