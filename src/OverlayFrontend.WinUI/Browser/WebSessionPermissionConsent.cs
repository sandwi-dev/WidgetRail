using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal enum SitePermissionPreference { Default, Allow, Deny }

/// <summary>One document's consent. This never touches a device or writes browser preferences.
/// A remembered choice is applied by WebView2 only when this same document requests that permission.</summary>
internal sealed class WebSessionPermissionConsent
{
    private (string Origin,long Document,bool Remember)? grant;
    private CancellationTokenSource? pending;

    internal void Retire()
    { grant=null;pending?.Cancel(); }

    internal void Grant(string origin,long document,bool remember) => grant=(origin,document,remember);

    internal bool IsGranted(string origin,long document,out bool remember)
    {
        remember=false;
        if(grant is not { } current || current.Origin!=origin || current.Document!=document)return false;
        remember=current.Remember;return true;
    }

    internal async Task<WebSessionPermissionResult> RequestAsync(string origin,long document,Func<bool> current,
        Func<CancellationToken,Task<SitePermissionPreference>> readPreference,
        Func<CancellationToken,Task<string?>> choose,CancellationToken token)
    {
        if(pending is not null || !current())return new(WebSessionPermissionState.Unavailable);
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
        pending=cancellation;
        try
        {
            var saved=await readPreference(cancellation.Token).WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(!current())return new(WebSessionPermissionState.Cancelled);
            if(saved==SitePermissionPreference.Deny){grant=null;return new(WebSessionPermissionState.Denied,true);}
            if(saved==SitePermissionPreference.Allow){Grant(origin,document,true);return new(WebSessionPermissionState.Granted,true);}
            if(IsGranted(origin,document,out var remembered))return new(WebSessionPermissionState.Granted,remembered);
            var answer=await choose(cancellation.Token).WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(!current())return new(WebSessionPermissionState.Cancelled);
            if(answer is not ("once" or "allow"))return new(WebSessionPermissionState.Cancelled);
            Grant(origin,document,answer=="allow");
            return new(WebSessionPermissionState.Granted,answer=="allow");
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { return new(WebSessionPermissionState.Cancelled); }
        finally { if(ReferenceEquals(pending,cancellation))pending=null; }
    }
}
