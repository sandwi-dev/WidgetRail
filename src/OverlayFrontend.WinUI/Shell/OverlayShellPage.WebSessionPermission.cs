using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    private async Task<WebSessionPermissionResult> RequestWebSessionPermissionAsync(BrowserSurface browser, WebSessionPermissionKind kind, CancellationToken token)
    {
        if(retired || !visible || switching || intentCancellation is not null || HostChoiceActive ||
            LocalInstallActive || SystemFilePickerOpen || owner is null || !browser.IsBackendSession)
            return new(WebSessionPermissionState.Unavailable);
        var requestOwner=owner;
        var source=requestOwner.Session.GetState(browser.WidgetId)?.LastGood;
        if(source is null)return new(WebSessionPermissionState.Unavailable);
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
        if(!Current())return new(WebSessionPermissionState.Unavailable);
        // Reuse the host presentation lane's cancellation on hide, foreground loss,
        // selection change, pin exit and teardown. This never delegates consent to a page.
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);
        intentCancellation=cancellation;intentPinnedTarget=sourcePin;
        try
        {
            async Task<string?> Choose(string origin,CancellationToken decisionToken)
            {
                if(!Current())return null;
                return await ShowHostChoiceAsync("Website permission",
                    $"{source.Descriptor.Name}\n{origin}\n\nAllow this website to {BrowserSurface.BackendPermissionDescription(kind)}?",
                    (HostDialogChoice[])[new("once","Allow for this session"),new("allow","Remember for this profile")],decisionToken,pinnedOwner:sourcePin);
            }
            return await browser.RequestBackendPermissionAsync(kind, Choose, Current, cancellation.Token);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested)
        { return new(WebSessionPermissionState.Cancelled); }
        finally
        {
            if(ReferenceEquals(intentCancellation,cancellation)){intentCancellation=null;intentPinnedTarget=null;}
        }
    }
}
