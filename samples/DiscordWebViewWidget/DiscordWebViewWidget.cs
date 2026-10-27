using System.Text.Json;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget : Widget
{
    private enum Route { Home, Channels, Account, ParticipantAudio, VoiceSettings }
    private sealed record ViewState(string Query="",string Notice="",string? Account=null,string? Channel=null,
        string? StreamKey=null,string? StreamName=null,Relay? Receiver=null,
        SharePreparation? PreparingShare=null,OutgoingCapture? Outgoing=null,
        CaptureInterruption? InterruptedCapture=null,string? StreamActivityUserId=null,ReceiverProblem? ReceiverFailure=null,
        WebSessionReference? StreamSession=null,ChannelSuggestions? Suggestions=null);
    private sealed record Relay(string Account,string Channel,string Lease,IDiscordReceiver Receiver);
    private sealed record ReceiverProblem(string Message);
    private sealed record ParticipantContext(string Account,string? Channel,WebSessionReference? Session=null);
    private readonly WidgetModel<ViewState> view;
    private readonly WidgetResource<DiscordState> state;
    private readonly WidgetNavigator<Route> navigation;
    private readonly WidgetLoadedCollection<ParticipantContext,DiscordParticipant> participants;
    private readonly WidgetLoadedCollection<string,DiscordFriendActivity> activities;
    private readonly PinnedLayoutHandle streamPin,callPin;
    private IDiscordSession? client;
    private readonly TimeProvider time;
    private readonly TimeSpan refreshInterval;

    public DiscordWebViewWidget():this(null,TimeProvider.System,TimeSpan.FromSeconds(2)){ }
    internal DiscordWebViewWidget(IDiscordSession? client,TimeProvider? time=null,TimeSpan? refreshInterval=null)
    {
        this.client=client;this.time=time??TimeProvider.System;this.refreshInterval=refreshInterval??TimeSpan.FromSeconds(2);
        view=CreateModel(new ViewState());
        navigation=CreateNavigatorWithOptions("discord.navigation",Route.Home,new()
        {SharedRootScopeId="discord.root",RootRoutes=[Route.Home],RouteScopeIds=new Dictionary<Route,string>{{Route.Account,"discord.account.scope"},{Route.Channels,"discord.channels.scope"},
            {Route.ParticipantAudio,"discord.participant-dialog.scope"},{Route.VoiceSettings,"discord.voice-settings-dialog.scope"}}});
        state=CreateResource("discord.state",new WidgetResourceOptions<DiscordState>
        {Load=ReadAsync,MapError=_=>new("discord.unavailable","Discord is unavailable. Open Discord to check the connection, then Refresh."),
            RetainLastGoodValue=false,Lifetime=WidgetOperationLifetime.Widget,CacheDuration=TimeSpan.FromSeconds(1)});
        InitializePicker();
        InitializeVoiceControls();
        participants=CreateLoadedCollection<ParticipantContext,DiscordParticipant>("discord.participants.source","",new("",null),[],new()
        {
            ItemKey=participant=>new("participant."+participant.Id),PreserveOccurrences=true,
            RenderItem=(context,participant,ids)=>ParticipantRow(context,participant,ids),
            OnAction=OpenParticipantAudioAsync
        });
        activities=CreateLoadedCollection<string,DiscordFriendActivity>("discord.activity.source","","",[],new()
        {
            ItemKey=activity=>new("friend."+activity.UserId),PreserveOccurrences=true,
            RenderItem=(_,activity,ids)=>ActivityRow(activity,ids.Id("row")),
            OnAction=(account,activity,action,token)=>ActivateActivityAsync(account,activity,action,token)
        });
        streamPin=CreatePinnedLayoutHandle("discord.stream","Shared screen",new(){PreferredWidth=560,PreferredHeight=340,MinimumWidth=300,MinimumHeight=200});
        callPin=CreatePinnedLayoutHandle("discord.call","Voice chat",new(){PreferredWidth=400,PreferredHeight=180,MinimumWidth=320,MinimumHeight=180});
    }

    protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        client??=new DiscordWebSessionClient(HostServices.WebSessions);
        Operations.RunSingleFlight("discord.poll",async context=>
        {
            while(!context.CancellationToken.IsCancellationRequested)
            {
                await state.Refresh().Completion;
                var current=view.Value;
                if(current.Receiver is { } relay && streamPin.IsSelected)
                    try{await client.InvokeAsync("keep",new{account=relay.Account,lease=relay.Lease},context.CancellationToken);}
                    catch(Exception) when(!context.CancellationToken.IsCancellationRequested){ }
                var delay=LifecycleState==WidgetLifecycleState.Background && !streamPin.IsSelected && !callPin.IsSelected &&
                    current.Outgoing is null && current.PreparingShare is null?TimeSpan.FromSeconds(5):refreshInterval;
                await Task.Delay(delay,time,context.CancellationToken);
            }
        },WidgetOperationLifetime.Widget);
        return ValueTask.CompletedTask;
    }
    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime){state.EnsureLoaded();ResumeVoiceControls();return ValueTask.CompletedTask;}

    private async ValueTask<DiscordState> ReadAsync(CancellationToken token)
    {
        var readStarted=time.GetTimestamp();
        var next=await client!.ReadAsync(token);next.Validate();token.ThrowIfCancellationRequested();
        await ReconcileOutgoingAsync(next,readStarted,token);
        ReconcileVoiceDialog(next);
        var selected=view.Value;
        if(selected.StreamKey is not null && (!next.Ready || selected.Account!=next.Account || selected.Channel!=next.ChannelId || selected.StreamSession!=client.Reference ||
            !next.Streams.Any(stream=>stream.Key==selected.StreamKey)))
        {
            var cleared=view.Update(current=>current.StreamKey==selected.StreamKey&&current.StreamSession==selected.StreamSession&&current.Account==selected.Account&&current.Channel==selected.Channel
                ?(current with{StreamKey=null,StreamName=null,StreamSession=null,StreamActivityUserId=null,ReceiverFailure=null,Notice="The selected stream ended or the call changed."},true):(current,false)).Result;
            if(cleared)RestartReceiver();
        }
        RefreshChannelSuggestions(next);
        return next;
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken cancellationToken=default)
    {
        if(TryVoiceControlAction(action,cancellationToken,out var voiceAction))return voiceAction;
        if(navigation.TryHandleBack(action,action.FocusedElementId))return ValueTask.CompletedTask;
        if(TryPickerAction(action,cancellationToken,out var pickerAction))return pickerAction;
        switch(action.ActionId)
        {
            case "refresh":state.Refresh();return ValueTask.CompletedTask;
            case "account":if(client?.Reference is not null)PushRoute(Route.Account,action);return ValueTask.CompletedTask;
            case "server-picker":
                if(navigation.Value.Route==Route.Home&&CurrentState()?.ChannelId is not null)
                    PushRoute(Route.Channels,action);
                return ValueTask.CompletedTask;
        }
        var selected=view.Value;var data=CurrentState();var session=client?.Reference;
        if(selected.Outgoing is not null&&data is {ChannelId:not null,Sharing:true}&&action.ActionId==ShareDetailsAction(data,selected,session))
        {ReadShareDetails(data,selected,session);return ValueTask.CompletedTask;}
        if(TryStreamAudioAction(action,data,selected,session,cancellationToken,out var audioAction))return audioAction;
        if(selected.StreamKey is not null && action.ActionId==UnwatchAction(selected,session))
        {
            var cleared=view.Update(current=>current.Account==selected.Account&&current.Channel==selected.Channel&&
                current.StreamKey==selected.StreamKey&&current.StreamSession==selected.StreamSession&&current.Receiver?.Lease==selected.Receiver?.Lease
                ?(current with{StreamKey=null,StreamName=null,StreamSession=null,StreamActivityUserId=null,ReceiverFailure=null,Notice=""},true):(current,false)).Result;
            if(cleared)RestartReceiver();return ValueTask.CompletedTask;
        }
        var canStop=CanStopSharing(selected,data);
        if(action.ActionId==SharingAction(canStop,data,selected,session))
        {if(canStop)StopSharing(data,selected,session);else BeginSharing(data,selected,session);return ValueTask.CompletedTask;}
        if(data is null)return ValueTask.CompletedTask;
        if(action.ActionId==CallAction("pin-call",data,session)&&data.ChannelId is not null)return RequestPinAsync(callPin,cancellationToken);
        if(Operations.IsBusy("discord.command"))return ValueTask.CompletedTask;
        if(action.ActionId.StartsWith("watch.",StringComparison.Ordinal))
        {
            var stream=data.Streams.FirstOrDefault(item=>WatchAction(data,item,session)==action.ActionId);
            if(stream is null||!data.Capabilities.Watch||data.ChannelId is null)return ValueTask.CompletedTask;
            view.Update(current=>current with{Account=data.Account,Channel=data.ChannelId,StreamKey=stream.Key,StreamName=stream.Name,StreamSession=session,StreamActivityUserId=null,ReceiverFailure=null,Notice=""});
            if(streamPin.IsSelected)RestartReceiver();
            return RequestPinAsync(streamPin,cancellationToken);
        }
        if(data.ChannelId is null)return ValueTask.CompletedTask;
        if(action.ActionId==CallAction("mute",data,session)&&data.Capabilities.Mute)
            Command("mute",new{account=data.Account,channelId=data.ChannelId,value=!data.Muted},session);
        else if(action.ActionId==CallAction("deafen",data,session)&&data.Capabilities.Deafen)
            Command("deafen",new{account=data.Account,channelId=data.ChannelId,value=!data.Deafened},session);
        else if(action.ActionId==CallAction("leave",data,session))
            Command("leave",new{account=data.Account,channelId=data.ChannelId},session);
        return ValueTask.CompletedTask;
    }
    // Ordinary actions are opaque IDs, so bind domain intent into that ID. A
    // later snapshot must not retarget a delayed click to another account/call
    // or turn an intended Mute into Unmute. Hashing keeps long stream IDs within
    // the protocol's 128-character action limit without retaining callbacks.
    private static string DomainAction(string method,params string?[] identity)=>method+"."+
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(identity)));
    private string CallAction(string method,DiscordState data)=>CallAction(method,data,client?.Reference);
    private static string CallAction(string method,DiscordState data,WebSessionReference? session)=>DomainAction(method,session?.Id,data.Account,data.ChannelId,
        method=="mute"?data.Muted.ToString():method=="deafen"?data.Deafened.ToString():null);
    private string WatchAction(DiscordState data,DiscordStream stream)=>WatchAction(data,stream,client?.Reference);
    private static string WatchAction(DiscordState data,DiscordStream stream,WebSessionReference? session)=>DomainAction("watch",session?.Id,data.Account,data.ChannelId,stream.UserId,stream.Key);
    private string UnwatchAction(ViewState selected)=>UnwatchAction(selected,client?.Reference);
    private static string UnwatchAction(ViewState selected,WebSessionReference? session)=>DomainAction("unwatch",session?.Id,selected.Account,selected.Channel,selected.StreamKey,selected.Receiver?.Lease);
    private ValueTask ActivateActivityAsync(string account,DiscordFriendActivity activity,WidgetActionEvent action,CancellationToken token)
    {
        var current=CurrentState();
        if(current?.Account!=account||Operations.IsBusy("discord.command"))return ValueTask.CompletedTask;
        var latest=current.Activities.FirstOrDefault(row=>row.UserId==activity.UserId);
        if(action.ActionId=="activity.join" && activity.CanJoinVoice && latest?.CanJoinVoice==true && latest.ChannelId==activity.ChannelId)
            return JoinAsync(account,activity.ChannelId!,token,activity.UserId);
        if(action.ActionId=="activity.watch" && activity.CanWatch && latest?.CanWatch==true && latest.StreamKey==activity.StreamKey &&
            current.Streams.Any(stream=>stream.Key==activity.StreamKey&&stream.UserId==activity.UserId))
        {
            view.Update(value=>value with{Account=account,Channel=current.ChannelId,StreamKey=activity.StreamKey,StreamName=activity.Name,StreamSession=client?.Reference,StreamActivityUserId=activity.UserId,ReceiverFailure=null,Notice=""});
            if(streamPin.IsSelected)RestartReceiver();
            return RequestPinAsync(streamPin,token);
        }
        return ValueTask.CompletedTask;
    }
    private DiscordState? CurrentState()=>state.Snapshot is {HasValue:true,Value.Ready:true,Status:not WidgetResourceStatus.Error} snapshot?snapshot.Value:null;
    private IndexedCollectionDescriptor? CollectionSource(string id)=>id switch
    {
        "discord.participants"=>participants.Descriptor,
        "discord.activities" or "discord.activities.compact"=>activities.Descriptor,
        "discord.channels"=>pickerItems.Descriptor,
        _=>null
    };
    private void PushRoute(Route destination,WidgetActionEvent action)
    {
        if(action.FocusedCollectionItem is { } item&&CollectionSource(item.CollectionId) is { } source)
            navigation.PushFromCollectionAction(destination,action,source);
        else navigation.PushFromAction(destination,action,action.FocusedElementId??"discord.account.open");
    }
    private async ValueTask RequestPinAsync(PinnedLayoutHandle layout,CancellationToken token)
    {
        try{await HostServices.Pinning.RequestOpenAsync(layout,token);}
        catch(Exception error) when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        {view.Update(current=>current with{Notice="This host could not accept the pin request."});}
    }

    private ValueTask JoinAsync(string account,string target,CancellationToken token,string? activityUserId=null)
    {
        token.ThrowIfCancellationRequested();
        bool Available(DiscordState data)=>activityUserId is null?data.Channels.Any(channel=>channel.Id==target):
            data.Activities.Any(row=>row.UserId==activityUserId&&row.ChannelId==target&&row.CanJoinVoice);
        var before=CurrentState();var session=client?.Reference;var origin=navigation.Value;
        if(before is null||before.Account!=account||!before.Capabilities.Voice||!Available(before)||Operations.IsBusy("discord.command")||before.ChannelId==target)
            return ValueTask.CompletedTask;
        Operations.RunSingleFlight("discord.command",async context=>
        {
            view.Update(current=>current with{Notice=""});
            try
            {
                var permission=await client!.RequestMicrophoneAsync(context.CancellationToken);
                if(permission.State!=WebSessionMicrophoneState.Granted)
                {
                    if(context.IsCurrent && permission.State!=WebSessionMicrophoneState.Cancelled)
                        view.Update(current=>current with{Notice=permission.State==WebSessionMicrophoneState.Unavailable?
                            "Microphone setup is unavailable on this host.":"Microphone access was not granted. You have not joined the channel."});
                    return;
                }
                var current=CurrentState();
                if(!context.IsCurrent||client.Reference!=session||current is null||current.Account!=account||current.ChannelId!=before.ChannelId||!Available(current))return;
                if(activityUserId is null)await client.InvokeAsync("join",new{account,target,channelId=current.ChannelId},context.CancellationToken);
                else await client.InvokeAsync("joinActivity",new{account,target,userId=activityUserId,channelId=current.ChannelId},context.CancellationToken);
            }
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested)
            {if(context.IsCurrent)view.Update(current=>current with{Notice="Discord could not join the channel. Check microphone setup and connection, then try again."});return;}
            finally{if(LifecycleState!=WidgetLifecycleState.Destroying)await state.Refresh().Completion;}
            if(context.IsCurrent&&client!.Reference==session&&CurrentState() is { } joined&&joined.Account==account&&joined.ChannelId==target&&
                origin.Route==Route.Channels&&ReferenceEquals(origin,navigation.Value))navigation.Back();
        },WidgetOperationLifetime.Widget);
        return ValueTask.CompletedTask;
    }
    private void Command(string method,object args,WebSessionReference? session)
    {
        Operations.RunSingleFlight("discord.command",async context=>
        {
            if(client?.Reference!=session)return;
            view.Update(current=>current with{Notice=""});
            try{await client!.InvokeAsync(method,args,context.CancellationToken);}
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested)
            {if(context.IsCurrent)view.Update(current=>current with{Notice="Discord could not confirm the action. Check its current state before trying again."});}
            finally{state.Refresh();}
        },WidgetOperationLifetime.Widget);
    }

    public override ValueTask OnPinnedLayoutSelectionChangedAsync(string? layoutId,CancellationToken cancellationToken=default)
    {RestartReceiver();return ValueTask.CompletedTask;}
    private void RestartReceiver()=>Operations.RunLatest("discord.receiver",async context=>
    {
        await CloseReceiverAsync();
        if(context.IsCurrent)view.Update(current=>current with{ReceiverFailure=null});
        var current=view.Value;var data=CurrentState();
        if(!context.IsCurrent||!streamPin.IsSelected||current.StreamKey is null||data is null||data.Account!=current.Account||data.ChannelId!=current.Channel||client?.Reference!=current.StreamSession)return;
        using var selected=CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken,streamPin.SelectionCancellationToken);
        var token=selected.Token;var lease=Guid.NewGuid().ToString("N");IDiscordReceiver? receiver=null;
        try
        {
            receiver=await client!.OpenReceiverAsync(token);
            token.ThrowIfCancellationRequested();
            if(client.Reference!=current.StreamSession)return;
            var offer=current.StreamActivityUserId is { } userId
                ?await client.InvokeAsync("watchActivity",new{account=data.Account,channelId=data.ChannelId,target=data.ChannelId,userId,key=current.StreamKey,lease},token)
                :await client.InvokeAsync("watch",new{account=data.Account,channelId=data.ChannelId,key=current.StreamKey,lease},token);
            if(client.Reference!=current.StreamSession)return;
            if(offer.GetProperty("lease").GetString()!=lease)throw new InvalidDataException("Incoming offer belongs to another selection.");
            var answer=await receiver.AcceptAsync(offer,token);
            if(answer.GetProperty("lease").GetString()!=lease)throw new InvalidDataException("Incoming answer belongs to another selection.");
            if(client.Reference!=current.StreamSession)return;
            await client.InvokeAsync("answer",new{account=data.Account,channelId=data.ChannelId,lease,answer=answer.GetProperty("answer")},token);
            var readyRelay=new Relay(data.Account,data.ChannelId!,lease,receiver);
            var published=view.Update(p=>context.IsCurrent&&streamPin.IsSelected&&!token.IsCancellationRequested&&client.Reference==current.StreamSession&&p.StreamSession==current.StreamSession&&p.Account==current.Account&&p.Channel==current.Channel&&p.StreamKey==current.StreamKey
                ?(p with{Receiver=readyRelay,Notice=""},true):(p,false)).Result;
            if(published){receiver=null;MonitorReceiver(readyRelay);}
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {if(context.IsCurrent)view.Update(p=>p with{ReceiverFailure=new("Select the shared screen again to retry."),Notice="The shared screen could not connect. Select it again to retry."});}
        finally
        {
            if(receiver is not null)await receiver.DisposeAsync();
            if(view.Value.Receiver?.Lease!=lease)await ReleaseLeaseAsync(data.Account,lease);
        }
    },WidgetOperationLifetime.Widget);
    private async Task CloseReceiverAsync()
    {
        var old=view.Update(current=>(current with{Receiver=null},current)).Result;
        if(old.Receiver is { } relay)
        {await relay.Receiver.DisposeAsync();await ReleaseLeaseAsync(relay.Account,relay.Lease);}
    }
    private async Task ReleaseLeaseAsync(string account,string lease)
    {try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3));await client!.InvokeAsync("unwatch",new{account,lease},cleanup.Token);}catch(Exception){/* Provider expiry also bounds abandoned relays. */}}
    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        try{await CloseReceiverAsync();if(view.Value.Outgoing is { } outgoing)await StopCaptureAsync(outgoing);}
        finally{if(client is not null)await client.DisposeAsync();}
    }
}
