using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task<WebSessionMicrophoneResult> RequestWebSessionMicrophoneAsync(BrowserSurface browser,CancellationToken token)
    {
        if(retired || !visible || switching || intentCancellation is not null || HostChoiceActive ||
            LocalInstallActive || SystemFilePickerOpen || owner is null || !browser.IsBackendSession)
            return new(WebSessionMicrophoneState.Unavailable);
        var requestOwner=owner;
        var source=requestOwner.Session.GetState(browser.WidgetId)?.LastGood;
        if(source is null)return new(WebSessionMicrophoneState.Unavailable);
        var authority=source.Authority;
        var sourcePin=PinnedInputActive && pinned?.WidgetId==browser.WidgetId ? pinned:null;
        var visibleSession=visibleSince;
        var selected=selectionVersion;
        bool Current()=>!retired&&visible&&ReferenceEquals(owner,requestOwner)&&visibleSince==visibleSession&&
            selectionVersion==selected&&!switching&&
            (sourcePin is null ? foreground&&interactive&&activeWidget==browser.WidgetId&&requestedWidget==browser.WidgetId :
                ReferenceEquals(pinned,sourcePin)&&sourcePin.IsCurrent&&sourcePin.HasForeground&&sourcePin.Window.Interactive)&&
            requestOwner.Session.GetState(browser.WidgetId)?.LastGood is { } latest&&
            latest.Authority.WidgetInstanceId==authority.WidgetInstanceId&&latest.Authority.RuntimeGeneration==authority.RuntimeGeneration&&
            latest.Authority.SessionGeneration==authority.SessionGeneration&&latest.Authority.WorkerRun==authority.WorkerRun;
        if(!Current())return new(WebSessionMicrophoneState.Unavailable);
        // Reuse the host presentation lane's cancellation on hide, foreground loss,
        // selection change, pin exit and teardown. This never delegates consent to a page.
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        intentCancellation=cancellation;intentPinnedTarget=sourcePin;
        try
        {
            return await browser.RequestBackendMicrophoneAsync(async (origin,decisionToken)=>
            {
                if(!Current())return null;
                return await ShowHostChoiceAsync("Microphone access",$"{source.Descriptor.Name}\n{origin}\n\nAllow this website to use your microphone?",
                    (HostDialogChoice[])[new("once","Allow for this session"),new("allow","Remember for this profile")],decisionToken,pinnedOwner:sourcePin);
            },Current,cancellation.Token);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { return new(WebSessionMicrophoneState.Cancelled); }
        finally
        {
            if(ReferenceEquals(intentCancellation,cancellation)){intentCancellation=null;intentPinnedTarget=null;}
        }
    }
}
