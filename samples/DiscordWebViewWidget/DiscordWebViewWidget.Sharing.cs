using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private const string ShareStartOperation="discord.share.start";
    private const string ShareStopOperation="discord.share.stop";
    private const string CaptureSetupTimedOut="Sharing setup timed out. Choose Share game to open the picker again.";
    private static readonly TimeSpan ShareConfirmationTimeout=TimeSpan.FromSeconds(15);
    private sealed record SharePreparation(string Account,string Channel,WebSessionReference Session,long Attempt=0);
    private sealed record CaptureInterruption(SharePreparation Owner,WebCaptureStreamTicket Ticket,string? Reason)
    { internal string Summary=>CaptureInterruptionSummary(Reason); }
    private sealed record OutgoingCapture(SharePreparation Owner,WebCaptureStreamTicket Ticket,WebCaptureStreamOptions Quality,
        string Application,bool ApplicationAudio,WebCaptureStreamPhase Phase=WebCaptureStreamPhase.Prepared,
        long? ProviderRequestedAt=null,bool ProviderObserved=false,bool StopUnconfirmed=false);
    internal bool IsPreparingSharing=>Operations.IsBusy(ShareStartOperation);
    internal Task WaitForStateReadAsync(CancellationToken token=default)=>state.WhenIdleAsync(token);
    internal Task WaitForCallCommandAsync(CancellationToken token=default)=>Operations.WhenIdleAsync("discord.command",token);

    private bool CanStartSharing(DiscordState? data,ViewState current)=>data is {Ready:true,ChannelId:not null,Capabilities.Share:true,Sharing:false,SharePending:false}&&
        data.ShareQualities.Length>0&&current.Outgoing is null&&current.PreparingShare is null;

    private ButtonElement ShareButton(DiscordState? data,string id="discord.share.start",string startLabel="Share game")
    {
        var current=view.Value;var canStop=CanStopSharing(current,data);
        return UI.Button(canStop?(IsPreparingSharing||data?.SharePending==true?"Cancel sharing":"Stop sharing"):startLabel,SharingAction(canStop,data,current,client?.Reference),id)
            .Icon(canStop?DiscordIcons.Leave:DiscordIcons.Share).Disabled(!canStop&&!CanStartSharing(data,current)).Classes("discord-control","discord-primary");
    }

    private WidgetElement SharingControls(DiscordState? data)=>UI.Stack("discord.share.controls",SharingDetails(data),ShareButton(data)).Classes("discord-card");

    private WidgetElement SharingDetails(DiscordState? data)
    {
        var current=view.Value;var capture=current.Outgoing;
        var preparing=Operations.IsBusy(ShareStartOperation);
        var stopping=Operations.IsBusy(ShareStopOperation);
        var interruption=current.InterruptedCapture is { } ended && OwnsCall(ended.Owner,data)?ended:null;
        // Do not disable the initiating control merely because its operation is
        // pending. The operation gate owns duplicate admission and preserves focus.
        var status=capture?.StopUnconfirmed==true?"Could not confirm that local capture stopped. Try Stop sharing again.":
            stopping?"Stopping sharing…":preparing?"Choose an application, audio and quality in WidgetRail.":
            capture is not null && data?.Sharing==true && capture.Phase==WebCaptureStreamPhase.Capturing
                ?$"Sharing {capture.Application} · up to {capture.Quality.Height}p at {capture.Quality.FrameRate} FPS"+(capture.ApplicationAudio?" · application audio":" · no application audio"):
            capture is not null?"Waiting for Discord to confirm sharing…":
            interruption is not null?interruption.Summary+(data?.Sharing==true
                ?" Discord still reports a stream. Select Stop sharing before starting again."
                :data?.SharePending==true?" Discord is still starting its stream. Select Cancel sharing."
                :" Choose Share game to select an application again."):
            data?.SharePending==true?"Discord is starting a stream…":
            data?.Sharing==true?"Discord reports an active stream.":"";
        var content=new List<WidgetElement>{UI.Text("Your stream","discord.share.heading").Classes("discord-section-title"),
            UI.Text(status,"discord.share.status").Classes("discord-secondary")};
        if(data?.ShareError is {Length:>0} error)
            content.Add(UI.Text(ShareErrorMessage(error),"discord.share.error").Classes("discord-notice"));
        if(data?.ShareQualityWarning is {Length:>0} warning&&capture is not null&&OwnsCall(capture.Owner,data))
            content.Add(UI.Text(ShareErrorMessage(warning),"discord.share.quality-warning").Classes("discord-notice"));
        return UI.Stack("discord.share.details",content.ToArray()).Classes("discord-stream-card");
    }

    private static string ShareErrorMessage(string error)=>error switch
    {
        "share_quality_unconfirmed"=>"Discord could not confirm the selected stream quality. Check Stream details.",
        "share_content_preference_unconfirmed"=>"Discord could not preserve the clarity setting. Sharing is still active.",
        "share_quality_unavailable"=>"This Discord version could not apply the selected stream quality.",
        "share_quality_unsupported"=>"That stream quality is no longer available for this call.",
        "share_picker_timeout"=>"Sharing setup timed out. Choose Share game to try again.",
        _=>"Discord could not start sharing. Check the connection and try again."
    };

    private static bool CanStopSharing(ViewState current,DiscordState? data)=>current.Outgoing is not null||
        current.PreparingShare is not null||data?.Sharing==true||data?.SharePending==true;
    // Provider pending/active transitions do not change which capture an explicit
    // Stop owns. The SDK's admitted attempt identity also survives chooser return
    // before the native ticket arrives; it is not a provider broadcast identity.
    private static string SharingAction(bool canStop,DiscordState? data,ViewState current,WebSessionReference? session)=>DomainAction(canStop?"stopShare":"share",
        session?.Id,data?.Account,data?.ChannelId,(current.Outgoing?.Owner??current.PreparingShare)?.Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private bool OwnsCall(SharePreparation intent,DiscordState? current=null)=>
        (current??CurrentState()) is { Ready:true } data && data.Account==intent.Account && data.ChannelId==intent.Channel && client?.Reference==intent.Session;

    private void BeginSharing(DiscordState? data,ViewState selected,WebSessionReference? session)
    {
        if(LifecycleState!=WidgetLifecycleState.Interactive || data?.ChannelId is null || session is null || client?.Reference!=session ||
            !data.Capabilities.Share || data.ShareQualities.Length==0 || data.Sharing || data.SharePending || selected.Outgoing is not null ||
            Operations.IsBusy(ShareStartOperation) || Operations.IsBusy(ShareStopOperation) || Operations.IsBusy("discord.command"))return;
        Operations.RunSingleFlight(ShareStartOperation,async context=>
        {
            var intent=new SharePreparation(data.Account,data.ChannelId,session,context.Generation);
            OutgoingCapture? capture=null;var retained=false;var providerAttempted=false;
            view.Update(current=>current with{PreparingShare=intent,InterruptedCapture=null,Notice=""});
            try
            {
                if(!context.IsCurrent || !OwnsCall(intent))return;
                var permission=await HostServices.Permissions.RequestAsync([WidgetCaptureCapabilities.Permission],context.CancellationToken);
                if(permission!=WidgetPermissionOutcome.Granted)
                {
                    if(context.IsCurrent && OwnsCall(intent) && permission==WidgetPermissionOutcome.Cancelled)
                        PublishFeedback("Sharing cancelled. Nothing was shared.");
                    else if(context.IsCurrent && OwnsCall(intent))view.Update(current=>current with{Notice=permission switch
                    {WidgetPermissionOutcome.Unavailable=>"Application capture access is unavailable on this host.",
                     _=>"Application capture access was not granted. Nothing was shared."}});
                    return;
                }
                if(!context.IsCurrent || !OwnsCall(intent))return;
                if(CurrentState() is not { } provider || !OwnsCall(intent,provider))return;
                var allowed=provider.ShareQualities.Select(q=>new WebCaptureStreamOptions(q.Resolution,q.FrameRate)).ToArray();
                if(allowed.Length==0)return;
                // Discord owns the persisted preference. Do not erase a saved
                // quality just because this account/call cannot currently use it.
                var preferred=provider.PreferredShareQuality is { } saved?new WebCaptureStreamOptions(saved.Resolution,saved.FrameRate):new WebCaptureStreamOptions();
                if(!allowed.Contains(preferred))preferred=allowed.Contains(new WebCaptureStreamOptions())?new():allowed[0];
                preferred=preferred with{ContentPreference=WebCaptureContentPreference.Detail};
                if(!context.IsCurrent||!OwnsCall(intent))return;
                var prepared=await HostServices.Capture.WebStreams.PrepareAsync(intent.Session,preferred,allowed,
                    allowContentPreferenceSelection:false,context.CancellationToken);
                if(prepared.Ticket is { } ticket)
                    capture=new(intent,ticket,prepared.Quality??preferred,prepared.ApplicationName??"Selected application",prepared.ApplicationAudio,prepared.Phase);
                // Capture may already have started while the preparation reply
                // was in transit. Both phases confirm the user's source choice;
                // neither proves that Discord has begun broadcasting.
                if(prepared.Phase is not (WebCaptureStreamPhase.Prepared or WebCaptureStreamPhase.Capturing))
                {
                    if(context.IsCurrent && OwnsCall(intent) && prepared.Phase==WebCaptureStreamPhase.Cancelled)
                        PublishFeedback("Sharing cancelled. Nothing was shared.");
                    else if(context.IsCurrent && OwnsCall(intent))view.Update(current=>current with{Notice="Application sharing is unavailable on this host."});
                    return;
                }
                capture??=new(intent,prepared.Ticket!,prepared.Quality!,prepared.ApplicationName!,prepared.ApplicationAudio);
                if(!context.IsCurrent || !OwnsCall(intent) || CurrentState() is not { Sharing:false,SharePending:false } latest)return;
                if(!latest.ShareQualities.Contains(new(capture.Quality.Height,capture.Quality.FrameRate)))
                {view.Update(current=>current with{Notice="That quality is no longer available. Choose Share game to select another."});return;}
                view.Update(current=>current with{Outgoing=capture});
                providerAttempted=true;
                var accepted=await client!.InvokeAsync("share",new{account=intent.Account,channelId=intent.Channel,
                    captureQuality=new{resolution=capture.Quality.Height,frameRate=capture.Quality.FrameRate}},context.CancellationToken);
                if(!accepted.TryGetProperty("requested",out var requested)||requested.ValueKind!=System.Text.Json.JsonValueKind.True)
                    throw new InvalidDataException("Discord did not acknowledge the sharing request.");
                if(!context.IsCurrent || !OwnsCall(intent))return;
                var requestedAt=time.GetTimestamp();
                retained=view.Update(current=>current.Outgoing?.Ticket==capture.Ticket
                    ?(current with{Outgoing=current.Outgoing with{ProviderRequestedAt=requestedAt}},true):(current,false)).Result;
            }
            catch(OperationCanceledException) when(context.CancellationToken.IsCancellationRequested) { }
            catch(WidgetCapabilityException error) when(!providerAttempted)
            {if(context.IsCurrent)view.Update(current=>current with{Notice=error.ErrorCode switch
                {"session_timeout"=>CaptureSetupTimedOut,
                 "request_canceled" or "lifecycle_denied"=>"Sharing setup was cancelled. Choose Share game to try again.",
                 _=>"Application capture is unavailable. Check capture access and host support."}});}
            catch(TimeoutException) when(!providerAttempted)
            {if(context.IsCurrent)view.Update(current=>current with{Notice=CaptureSetupTimedOut});}
            catch(Exception)
            {if(context.IsCurrent)view.Update(current=>current with{Notice="Discord could not confirm sharing. The start was not retried."});}
            finally
            {
                if(!retained)
                {
                    // This lane remains single-flight until cleanup finishes, so
                    // cancelPendingShare cannot cancel a newer widget start.
                    if(providerAttempted && OwnsCall(intent))await CancelPendingShareAsync(intent);
                    if(capture is not null)await StopCaptureAsync(capture,retainUnpublishedOnFailure:!providerAttempted);
                }
                view.Update(current=>ReferenceEquals(current.PreparingShare,intent)?current with{PreparingShare=null}:current);
                if(LifecycleState!=WidgetLifecycleState.Destroying)state.Refresh();
            }
        },WidgetOperationLifetime.Widget);
    }

    private void StopSharing(DiscordState? requested,ViewState selected,WebSessionReference? session)
    {
        var current=view.Value;var expectedOwner=selected.Outgoing?.Owner??selected.PreparingShare;
        if((current.Outgoing?.Owner??current.PreparingShare)!=expectedOwner||client?.Reference!=session)return;
        var capture=current.Outgoing;var preparation=current.PreparingShare;
        Operations.Cancel(ShareStartOperation);
        Operations.RunSingleFlight(ShareStopOperation,async context=>
        {
            try
            {
                if(capture is not null)await StopCaptureAsync(capture);
                var pendingOwner=capture?.Owner??preparation;
                if(pendingOwner is null && requested is {SharePending:true,ChannelId:not null} && session is not null)
                    pendingOwner=new(requested.Account,requested.ChannelId,session);
                if(pendingOwner is not null && OwnsCall(pendingOwner))await CancelPendingShareAsync(pendingOwner);
                // This is an explicit user stop, unlike automatic cleanup. It
                // may stop the current stream started through Open Discord too.
                if(client?.Reference==session && requested is { Ready:true,ChannelId:not null } &&
                    CurrentState() is { Ready:true } latest && latest.Account==requested.Account && latest.ChannelId==requested.ChannelId &&
                    (requested.Sharing || capture?.ProviderObserved==true))
                {
                    if(requested.Capabilities.StopShare)
                        await client!.InvokeAsync("stopShare",new{account=requested.Account,channelId=requested.ChannelId},context.CancellationToken);
                    else if(view.Value.Outgoing is null)view.Update(current=>current with{Notice=capture is null?
                        "Stop this stream in Discord.":"Local capture stopped. Stop the remaining stream in Discord."});
                }
            }
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested)
            {view.Update(current=>current with{Notice="Discord could not confirm the stop. Check its current state before trying again."});}
            finally{if(LifecycleState!=WidgetLifecycleState.Destroying)state.Refresh();}
        },WidgetOperationLifetime.Widget);
    }

    private async ValueTask ReconcileOutgoingAsync(DiscordState next,long readStarted,CancellationToken token)
    {
        var current=view.Value;
        if(current.InterruptedCapture is { } interruption && !OwnsCall(interruption.Owner,next))
            view.Update(value=>ReferenceEquals(value.InterruptedCapture,interruption)?value with{InterruptedCapture=null}:value);
        if(current.PreparingShare is { } preparing && !OwnsCall(preparing,next))Operations.Cancel(ShareStartOperation);
        var capture=current.Outgoing;
        if(capture is null || Operations.IsBusy(ShareStopOperation))return;
        if(!OwnsCall(capture.Owner,next))
        {await StopCaptureAsync(capture,"Sharing stopped because the account or voice channel changed.");return;}
        WebCaptureStreamStatus native;
        try{native=await HostServices.Capture.WebStreams.GetStatusAsync(capture.Ticket,token);}
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch(Exception)
        {await StopCaptureAsync(capture,"Local capture status is unavailable. Sharing needs attention.");return;}
        token.ThrowIfCancellationRequested();
        if(native.Phase is WebCaptureStreamPhase.Stopped or WebCaptureStreamPhase.Failed or WebCaptureStreamPhase.Cancelled or WebCaptureStreamPhase.Unavailable)
        {
            // A local terminal state cannot identify Discord's current broadcast.
            // Its account/channel stream key can be reused by a newer stream.
            // Preserve the provider snapshot and offer an explicit Stop action.
            await StopCaptureAsync(capture,terminalKnown:true,
                interruption:new(capture.Owner,capture.Ticket,native.Reason));
            return;
        }
        if(native.Quality!=capture.Quality || native.ApplicationAudio!=capture.ApplicationAudio)
        {await StopCaptureAsync(capture,"The selected capture options changed. Start sharing again to choose them.");return;}
        var observed=capture.ProviderObserved;
        if(capture.ProviderRequestedAt is { } requestedAt && readStarted>=requestedAt)
        {
            observed|=next.Sharing;
            var ended=observed && !next.Sharing && !next.SharePending;
            var neverConfirmed=!observed && !next.Sharing && time.GetElapsedTime(requestedAt)>=ShareConfirmationTimeout;
            if(next.ShareError is not null || ended || neverConfirmed)
            {await StopCaptureAsync(capture,neverConfirmed?"Discord did not confirm sharing. Local capture was stopped.":"Discord ended or could not start this stream.");return;}
        }
        view.Update(value=>value.Outgoing?.Ticket==capture.Ticket?value with{Outgoing=value.Outgoing with{Phase=native.Phase,ProviderObserved=observed}}:value);
    }

    private static string CaptureInterruptionSummary(string? reason)=>reason switch
    {
        "source_closed"=>"The selected application window is no longer available.",
        "worker_disconnected" or "worker_failed" or "worker_heartbeat_failed"=>"The capture connection ended unexpectedly.",
        "browser_process_failed" or "browser_heartbeat_failed" or "receiver_heartbeat_invalid"=>"Discord's browser connection was interrupted.",
        "navigation" or "origin_changed" or "document_retired" or "session_closed"=>"The Discord page changed or closed, so capture stopped.",
        "receiver_error" or "buffer_publish_failed"=>"Video delivery to Discord stopped.",
        "selection_cancelled" or "request_cancelled"=>"Application capture was cancelled.",
        "stopped"=>"Application capture stopped.",
        _=>"Application capture ended unexpectedly."
    };

    private async Task StopCaptureAsync(OutgoingCapture capture,string? notice=null,bool terminalKnown=false,
        CaptureInterruption? interruption=null,bool retainUnpublishedOnFailure=false)
    {
        var unretainedPreparation=retainUnpublishedOnFailure&&ReferenceEquals(view.Value.PreparingShare,capture.Owner)&&view.Value.Outgoing is null;
        var stopped=terminalKnown;
        try
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(4));
            await HostServices.Capture.WebStreams.StopAsync(capture.Ticket,cleanup.Token);stopped=true;
        }
        catch(Exception){ }
        if(LifecycleState==WidgetLifecycleState.Destroying)return;
        view.Update(current=>
        {
            if(current.Outgoing?.Ticket!=capture.Ticket &&
                !(unretainedPreparation&&current.Outgoing is null&&ReferenceEquals(current.PreparingShare,capture.Owner)))return current;
            return stopped?current with{Outgoing=null,InterruptedCapture=interruption??current.InterruptedCapture,Notice=notice??current.Notice}:
                current with{Outgoing=(current.Outgoing??capture) with{StopUnconfirmed=true},Notice="Could not confirm that local capture stopped. Try Stop sharing again."};
        });
    }

    private async Task CancelPendingShareAsync(SharePreparation intent)
    {
        try
        {
            using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client!.InvokeAsync("cancelPendingShare",new{account=intent.Account},cleanup.Token);
        }
        catch(Exception){/* Stopping the native ticket still removes this capture's pixels/audio. */}
    }
}
