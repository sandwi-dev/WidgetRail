using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal enum MicrophonePreference { Default, Allow, Deny }

/// <summary>One document's consent. This never touches a device or writes browser preferences.
/// A remembered choice is applied by WebView2 only when this same document requests microphone use.</summary>
internal sealed class WebSessionMicrophoneConsent
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

    internal async Task<WebSessionMicrophoneResult> RequestAsync(string origin,long document,Func<bool> current,
        Func<CancellationToken,Task<MicrophonePreference>> readPreference,
        Func<CancellationToken,Task<string?>> choose,CancellationToken token)
    {
        if(pending is not null || !current())return new(WebSessionMicrophoneState.Unavailable);
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token);
        pending=cancellation;
        try
        {
            var saved=await readPreference(cancellation.Token).WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(!current())return new(WebSessionMicrophoneState.Cancelled);
            if(saved==MicrophonePreference.Deny){grant=null;return new(WebSessionMicrophoneState.Denied,true);}
            if(saved==MicrophonePreference.Allow){Grant(origin,document,true);return new(WebSessionMicrophoneState.Granted,true);}
            if(IsGranted(origin,document,out var remembered))return new(WebSessionMicrophoneState.Granted,remembered);
            var answer=await choose(cancellation.Token).WaitAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if(!current())return new(WebSessionMicrophoneState.Cancelled);
            if(answer is not ("once" or "allow"))return new(WebSessionMicrophoneState.Cancelled);
            Grant(origin,document,answer=="allow");
            return new(WebSessionMicrophoneState.Granted,answer=="allow");
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { return new(WebSessionMicrophoneState.Cancelled); }
        finally { if(ReferenceEquals(pending,cancellation))pending=null; }
    }
}
