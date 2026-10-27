using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserSurface
{
    private readonly WebSessionMicrophoneConsent backendMicrophone = new();
    private readonly HashSet<Task> backendMicrophoneOperations=[];
    internal bool IsBackendSession => hostedDefinition?.PageAdapter?.Mode==WebSessionPageAdapterMode.SessionBackend;

    private void InitializeBackendMicrophone()
    {
        if(!IsBackendSession || core is null)return;
        core.NavigationStarting+=(_,args)=>{if(!args.Cancel)backendMicrophone.Retire();};
        core.ProcessFailed+=(_,args)=>{if(RequiresPageRecovery(args.ProcessFailedKind))backendMicrophone.Retire();};
    }

    internal async Task<WebSessionMicrophoneResult> RequestBackendMicrophoneAsync(
        Func<string,CancellationToken,Task<string?>> choose,Func<bool> admitted,CancellationToken token)
    {
        if(CurrentBackendDocumentOrigin is not { } origin || core is not { } browser || !admitted())
            return new(WebSessionMicrophoneState.Unavailable);
        var documentEpoch=pageEpoch;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        bool Current()=>!linked.IsCancellationRequested&&ReferenceEquals(core,browser)&&pageEpoch==documentEpoch&&
            CurrentBackendDocumentOrigin==origin&&admitted();
        async Task<MicrophonePreference> Read(CancellationToken cancellation)
        {
            var saved=await browser.Profile.GetNonDefaultPermissionSettingsAsync().AsTask().WaitAsync(cancellation);
            var permission=saved.FirstOrDefault(p=>p.PermissionKind==CoreWebView2PermissionKind.Microphone&&BrowserSiteIdentity.Origin(p.PermissionOrigin)==origin);
            return permission?.PermissionState switch
            { CoreWebView2PermissionState.Allow=>MicrophonePreference.Allow,CoreWebView2PermissionState.Deny=>MicrophonePreference.Deny,_=>MicrophonePreference.Default };
        }
        var request=backendMicrophone.RequestAsync(origin,documentEpoch,Current,Read,cancellation=>choose(origin,cancellation),linked.Token);
        backendMicrophoneOperations.Add(request);
        try { return await request; }
        finally { backendMicrophoneOperations.Remove(request); }
    }

    private bool TryBackendMicrophonePermission(CoreWebView2PermissionRequestedEventArgs args)
    {
        if(!IsBackendSession || args.PermissionKind!=CoreWebView2PermissionKind.Microphone)return false;
        var origin=BrowserSiteIdentity.Origin(args.Uri);
        if(origin is not null && CurrentBackendDocumentOrigin==origin && backendMicrophone.IsGranted(origin,pageEpoch,out var remember))
        {
            args.Handled=true;args.State=CoreWebView2PermissionState.Allow;args.SavesInProfile=remember;
            return true;
        }
        // A visible browser retains its normal website-permission UI. A hidden
        // backend cannot provoke prompts; its native widget must request consent first.
        if(CanRequestSiteDecision)return false;
        args.Handled=true;args.State=CoreWebView2PermissionState.Deny;args.SavesInProfile=false;
        return true;
    }
}
