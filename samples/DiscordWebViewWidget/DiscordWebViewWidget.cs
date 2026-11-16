using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget : Widget
{
    private enum Route { Home, Channels, Account, VoiceSettings, ConnectionDetails, Friends }
    private sealed record ViewState(string Query="",string Notice="",SharePreparation? PreparingShare=null,OutgoingCapture? Outgoing=null,
        CaptureInterruption? InterruptedCapture=null,ChannelSuggestions? Suggestions=null);
    private sealed record WatchState(string? Account=null,string? Channel=null,string? StreamKey=null,string? StreamName=null,
        Relay? Receiver=null,string? StreamActivityUserId=null,ReceiverProblem? ReceiverFailure=null,WebSessionReference? StreamSession=null)
    {
        public string Revision { get; init; }=Guid.NewGuid().ToString("N");
        public bool Closed { get; init; }
        public bool Ended { get; init; }
    }
    private sealed record WatchSlot(int Number,PinnedLayoutHandle Pin,WidgetModel<WatchState> State)
    {
        public string ControlSuffix => Number==1?"":"."+Number;
    }
    private readonly WatchSlot[] watches;
    private sealed record Relay(string Account,string Channel,string Lease,IDiscordReceiver Receiver);
    private sealed record ReceiverProblem(string Message);
    private sealed record ParticipantContext(string Account,string? Channel,WebSessionReference? Session=null,
        string Members="",string Epoch="",string? NoticeUser=null,string Notice="");
    private readonly WidgetModel<ViewState> view;
    private readonly WidgetResource<DiscordState> state;
    private readonly WidgetNavigator<Route> navigation;
    private readonly WidgetLoadedCollection<ParticipantContext,DiscordParticipant> participants;
    private readonly WidgetLoadedCollection<string,DiscordFriendActivity> activities;
    private readonly PinnedLayoutHandle streamPin,callPin,peoplePin;
    private bool HasVisiblePin => watches.Any(slot=>slot.Pin.IsSelected) || callPin.IsSelected || peoplePin.IsSelected;
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
            {Route.VoiceSettings,"discord.voice-settings-dialog.scope"},{Route.ConnectionDetails,"discord.connection-dialog.scope"},
            {Route.Friends,"discord.friends.scope"}}});
        state=CreateResource("discord.state",new WidgetResourceOptions<DiscordState>
        {Load=ReadAsync,MapError=_=>new("discord.unavailable","Discord is unavailable. Open Discord to check the connection, then Refresh."),
            RetainLastGoodValue=false,Lifetime=WidgetOperationLifetime.Widget,CacheDuration=TimeSpan.FromSeconds(1)});
        InitializePicker();
        InitializeVoiceControls();
        InitializeConnectionDetails();
        participants=CreateLoadedCollection<ParticipantContext,DiscordParticipant>("discord.participants.source","",new("",null),[],new()
        {
            ItemKey=participant=>new("participant."+participant.Id),PreserveOccurrences=true,
            RenderItem=(context,participant,ids)=>ParticipantRow(context,participant,ids),
            OnAction=HandleParticipantPreviewAsync
        });
        activities=CreateLoadedCollection<string,DiscordFriendActivity>("discord.activity.source","","",[],new()
        {
            ItemKey=activity=>new("friend."+activity.UserId),PreserveOccurrences=true,
            RenderItem=(_,activity,ids)=>ActivityRow(activity,ids.Id("row")),
            OnAction=(account,activity,action,token)=>ActivateActivityAsync(account,activity,action,token)
        });
        streamPin=CreatePinnedLayoutHandle("discord.stream","Shared screen",new(){PreferredWidth=560,PreferredHeight=340,MinimumWidth=300,MinimumHeight=200},PinnedInteractionMode.Passive);
        var secondStreamPin=CreatePinnedLayoutHandle("discord.stream.2","Shared screen 2",streamPin.Surface,PinnedInteractionMode.Passive);
        var thirdStreamPin=CreatePinnedLayoutHandle("discord.stream.3","Shared screen 3",streamPin.Surface,PinnedInteractionMode.Passive);
        watches=[new(1,streamPin,CreateModel(new WatchState())),new(2,secondStreamPin,CreateModel(new WatchState())),
            new(3,thirdStreamPin,CreateModel(new WatchState()))];
        callPin=CreatePinnedLayoutHandle("discord.call","Voice chat",new(){PreferredWidth=400,PreferredHeight=340,MinimumWidth=320,MinimumHeight=340,Appearance=WidgetSurfaceAppearance.DesktopAcrylic});
        peoplePin=CreatePinnedLayoutHandle("discord.people","Voice participants",new(){PreferredWidth=280,PreferredHeight=224,
            MinimumWidth=260,MinimumHeight=224,Appearance=WidgetSurfaceAppearance.Transparent},PinnedInteractionMode.Passive);
    }

    protected override ValueTask OnCreatedAsync(CancellationToken widgetLifetime)
    {
        client??=new DiscordWebSessionClient(HostServices.WebSessions);
        client.Invalidated+=ProviderInvalidated;
        Operations.RunSingleFlight("discord.poll",async context=>
        {
            while(!context.CancellationToken.IsCancellationRequested)
            {
                await state.Refresh().Completion;
                var current=view.Value;
                foreach(var slot in watches)
                if(slot.State.Value.Receiver is { } relay && slot.Pin.IsSelected)
                    try{await client.InvokeAsync("keep",new{account=relay.Account,lease=relay.Lease},context.CancellationToken);}
                    catch(Exception) when(!context.CancellationToken.IsCancellationRequested){ }
                var delay=LifecycleState==WidgetLifecycleState.Background && !HasVisiblePin &&
                    current.Outgoing is null && current.PreparingShare is null?TimeSpan.FromSeconds(5):refreshInterval;
                await Task.Delay(delay,time,context.CancellationToken);
            }
        },WidgetOperationLifetime.Widget);
        return ValueTask.CompletedTask;
    }
    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime){state.EnsureLoaded();ResumeVoiceControls();ResumeConnectionDetails();return ValueTask.CompletedTask;}

    private void ProviderInvalidated(WebSessionReference session)
    {
        var current=view.Value;
        if(LifecycleState==WidgetLifecycleState.Background && !HasVisiblePin &&
            current.Outgoing is null && current.PreparingShare is null)return;
        Operations.RunLatest("discord.provider-change",async context =>
        {
            // A change observed during a read must cause a subsequent read, rather
            // than merely joining a snapshot that could predate the notification.
            await state.WhenIdleAsync(context.CancellationToken);
            if(context.IsCurrent && client?.Reference==session)await state.Refresh().Completion;
        },WidgetOperationLifetime.Widget);
    }

    private async ValueTask<DiscordState> ReadAsync(CancellationToken token)
    {
        var readStarted=time.GetTimestamp();
        var observedWatches=watches.Select(slot=>(Slot:slot,State:slot.State.Value)).ToArray();
        var next=await client!.ReadAsync(token);next.Validate();token.ThrowIfCancellationRequested();
        await ReconcileOutgoingAsync(next,readStarted,token);
        ReconcileParticipantContext(next);
        ReconcileVoiceDialog(next);
        ReconcileConnectionDetails(next);
        foreach(var observed in observedWatches)
        {
            var slot=observed.Slot;
            var selected=observed.State;
            if(selected.StreamKey is null || next.Ready && selected.Account==next.Account && selected.Channel==next.ChannelId &&
                selected.StreamSession==client.Reference && next.Streams.Any(stream=>stream.Key==selected.StreamKey))continue;
            var cleared=slot.State.Update(current=>current.Revision==selected.Revision
                ? (new WatchState(Receiver:current.Receiver){Ended=true},true):(current,false)).Result;
            if(cleared){view.Update(current=>current with{Notice="The selected stream ended or the call changed."});RestartReceiver(slot);}
        }
        RefreshChannelSuggestions(next);
        return next;
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action,CancellationToken cancellationToken=default)
    {
        if(TryConnectionDetailsAction(action,cancellationToken,out var connectionAction))return connectionAction;
        if(TryVoiceControlAction(action,cancellationToken,out var voiceAction))return voiceAction;
        if(navigation.TryHandleBack(action,action.FocusedElementId))return ValueTask.CompletedTask;
        if(TryPickerAction(action,cancellationToken,out var pickerAction))return pickerAction;
        switch(action.ActionId)
        {
            case "refresh":state.Refresh();return ValueTask.CompletedTask;
            case "friends":
                if(navigation.Value.Route==Route.Home&&CurrentState() is {ChannelId:null})
                    PushRoute(Route.Friends,action);
                return ValueTask.CompletedTask;
            case "account":if(client?.Reference is not null)PushRoute(Route.Account,action);return ValueTask.CompletedTask;
            case "server-picker":
                if(navigation.Value.Route==Route.Home&&CurrentState()?.ChannelId is not null)
                    PushRoute(Route.Channels,action);
                return ValueTask.CompletedTask;
        }
        var selected=view.Value;var data=CurrentState();var session=client?.Reference;
        if (navigation.Value.Route == Route.Home && NeedsNotificationSetup(data) &&
            session is not null && action.ActionId == DomainAction("notifications", session.Id, data.Account))
        {
            EnableNotifications(data.Account, session);
            return ValueTask.CompletedTask;
        }
        if(selected.Outgoing is not null&&data is {ChannelId:not null,Sharing:true}&&action.ActionId==ShareDetailsAction(data,selected,session))
        {ReadShareDetails(data,selected,session);return ValueTask.CompletedTask;}
        if(TryStreamAudioAction(action,data,session,cancellationToken,out var audioAction))return audioAction;
        foreach(var slot in watches)
        {
            var watch=slot.State.Value;
            if(watch.StreamKey is null || action.ActionId!=UnwatchAction(watch,session))continue;
            var cleared=slot.State.Update(current=>current==watch?(new WatchState(Receiver:current.Receiver){Ended=true},true):(current,false)).Result;
            if(cleared){view.Update(current=>current with{Notice=""});RestartReceiver(slot);}
            return ValueTask.CompletedTask;
        }
        var canStop=CanStopSharing(selected,data);
        if(action.ActionId==SharingAction(canStop,data,selected,session))
        {if(canStop)StopSharing(data,selected,session);else BeginSharing(data,selected,session);return ValueTask.CompletedTask;}
        if(data is null)return ValueTask.CompletedTask;
        if(data.ChannelId is not null)
        {
            foreach(var layout in watches.Select(slot=>slot.Pin).Append(callPin).Append(peoplePin))
                if(layout.Selection is { } target)
                {
                    if(action.ActionId==ClosePinAction(target,data,session))return ClosePinAsync(target,cancellationToken);
                    if(action.ActionId==ManagePinAction(target,data,session))return ManagePinAsync(target,cancellationToken);
                }
            if(callPin.Selection is null && action.ActionId==CallAction("pin-call",data,session))return RequestPinAsync(callPin,cancellationToken);
            if(peoplePin.Selection is null && action.ActionId==CallAction("pin-people",data,session))return RequestPinAsync(peoplePin,cancellationToken);
        }
        if(Operations.IsBusy("discord.command"))return ValueTask.CompletedTask;
        if(action.ActionId.StartsWith("watch.",StringComparison.Ordinal))
        {
            var stream=data.Streams.FirstOrDefault(item=>WatchAction(data,item,session)==action.ActionId);
            if(stream is null||!data.Capabilities.Watch||data.ChannelId is null)return ValueTask.CompletedTask;
            return WatchAsync(data,stream,null,cancellationToken);
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
    private string UnwatchAction(WatchState selected)=>UnwatchAction(selected,client?.Reference);
    private static string UnwatchAction(WatchState selected,WebSessionReference? session)=>DomainAction("unwatch",session?.Id,selected.Account,selected.Channel,selected.StreamKey,selected.Receiver?.Lease,selected.Revision);
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
            return WatchAsync(current,current.Streams.Single(stream=>stream.Key==activity.StreamKey),activity.UserId,token);
        }
        return ValueTask.CompletedTask;
    }
    private ValueTask WatchAsync(DiscordState data,DiscordStream stream,string? activityUser,CancellationToken token)
    {
        var slot=watches.FirstOrDefault(item=>!item.State.Value.Closed && item.State.Value.StreamKey==stream.Key) ??
            watches.FirstOrDefault(item=>item.State.Value.Closed || item.State.Value.StreamKey is null) ??
            watches.FirstOrDefault(item=>!item.Pin.IsSelected);
        if(slot is null)
        {
            view.Update(current=>current with{Notice="All three screen slots are in use. Unpin a screen to watch another."});
            return ValueTask.CompletedTask;
        }
        slot.State.Update(current=>new WatchState(data.Account,data.ChannelId,stream.Key,stream.Name,current.Receiver,activityUser,
            StreamSession:client?.Reference));
        view.Update(current=>current with{Notice=""});
        if(slot.Pin.IsSelected)RestartReceiver(slot);
        return RequestPinAsync(slot.Pin,token);
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
    private string ClosePinAction(WidgetPinCloseRequest target,DiscordState data,WebSessionReference? session)=>
        DomainAction("close-pin",session?.Id,data.Account,data.ChannelId,target.LayoutId,target.SelectionId,
            watches.FirstOrDefault(slot=>slot.Pin.Id==target.LayoutId)?.State.Value.Revision);
    private WidgetContextAction VoicePinCommand(PinnedLayoutHandle layout,DiscordState data,bool people=false)=>
        layout.Selection is { } target ? new(ClosePinAction(target,data,client?.Reference),people?"Unpin voice participants":"Unpin voice controls") :
            new(CallAction(people?"pin-people":"pin-call",data),people?"Pin voice participants":"Pin voice controls");
    private string ManagePinAction(WidgetPinCloseRequest target,DiscordState data,WebSessionReference? session)=>
        DomainAction("manage-pin",session?.Id,data.Account,data.ChannelId,target.LayoutId,target.SelectionId,
            watches.FirstOrDefault(slot=>slot.Pin.Id==target.LayoutId)?.State.Value.Revision);
    private WidgetElement ManagePinControl(PinnedLayoutHandle layout,DiscordState data,string id)=>
        layout.Selection is {} target
            ?UI.IconButton(DiscordIcons.ManagePin,ManagePinAction(target,data,client?.Reference),id,
                "Manage "+layout.Name+" pin",size:IconButtonSize.Small).Classes("discord-toolbar-button")
            :UI.Spacer(id);
    private async ValueTask ManagePinAsync(WidgetPinCloseRequest target,CancellationToken token)
    {
        try{await HostServices.Pinning.RequestManageAsync(target,token);}
        catch(Exception error) when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        {view.Update(current=>current with{Notice="Pin management requires a supporting WidgetRail host."});}
    }

    private async ValueTask ClosePinAsync(WidgetPinCloseRequest target,CancellationToken token)
    {
        try{await HostServices.Pinning.RequestCloseAsync(target,token);}
        catch(Exception error) when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        {view.Update(current=>current with{Notice="This host could not close the pin."});}
    }

    private async ValueTask RequestPinAsync(PinnedLayoutHandle layout,CancellationToken token)
    {
        try{await HostServices.Pinning.RequestOpenAsync(layout,token);}
        catch(Exception error) when(error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
        {view.Update(current=>current with{Notice="This host could not accept the pin request."});}
    }

    private void EnableNotifications(string account, WebSessionReference session)
    {
        Operations.RunSingleFlight("discord.notifications", async context =>
        {
            bool Current() => context.IsCurrent && client?.Reference == session && CurrentState()?.Account == account;
            if (!Current()) return;
            try
            {
                var result = await client!.RequestNotificationsAsync(context.CancellationToken);
                if (!Current()) return;
                var notice = result.State switch
                {
                    WebSessionPermissionState.Granted => "Permission granted. In Discord, open User Settings → Notifications and turn on Enable Desktop Notifications.",
                    WebSessionPermissionState.Denied => "Notifications are blocked. Open Discord and change Notifications in Website settings.",
                    WebSessionPermissionState.Unavailable => "Notification setup is unavailable on this host.",
                    _ => ""
                };
                if(result.State==WebSessionPermissionState.Granted)
                {
                    try
                    {
                        var enabled=await client.InvokeAsync("enableNotifications",new{account},context.CancellationToken);
                        if(!Current())return;
                        if(enabled.TryGetProperty("state",out var desktop)&&desktop.GetString()=="enabled")
                            notice="Discord desktop notifications enabled.";
                    }
                    catch(Exception error) when(error is not OutOfMemoryException&&!context.CancellationToken.IsCancellationRequested)
                    { /* Permission alone is not sufficient. Keep the concrete manual setup instruction. */ }
                }
                if(!Current())return;
                view.Update(current => current with { Notice = notice });
            }
            catch (Exception error) when (error is not OutOfMemoryException && !context.CancellationToken.IsCancellationRequested)
            {
                if (Current()) view.Update(current => current with { Notice = "Could not enable notifications. Open Discord to check Website settings." });
            }
            finally { if (Current()) state.Refresh(); }
        }, WidgetOperationLifetime.Active);
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
                origin.Route is (Route.Channels or Route.Friends)&&ReferenceEquals(origin,navigation.Value))navigation.Back();
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
    {
        foreach(var slot in watches)RestartReceiver(slot);
        return ValueTask.CompletedTask;
    }
    public override ValueTask OnPinnedSurfaceChangedAsync(string layoutId,bool selected,CancellationToken cancellationToken=default)
    {
        if(watches.FirstOrDefault(item=>item.Pin.Id==layoutId) is { } slot)
        {
            // Retain history for Reconnect, while making the observed closed slot available.
            slot.State.Update(current=>selected
                ?current.Closed?new WatchState(Receiver:current.Receiver):current
                :current with {Closed=true});
            RestartReceiver(slot);
        }
        return ValueTask.CompletedTask;
    }
    private void RestartReceiver(WatchSlot slot)=>Operations.RunLatest(slot.Pin.Id+".receiver",async context=>
    {
        await CloseReceiverAsync(slot);
        if(context.IsCurrent)slot.State.Update(current=>current with{ReceiverFailure=null});
        var current=slot.State.Value;var data=CurrentState();
        if(!context.IsCurrent||!slot.Pin.IsSelected||current.StreamKey is null||data is null||data.Account!=current.Account||data.ChannelId!=current.Channel||client?.Reference!=current.StreamSession)return;
        using var selected=CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken,slot.Pin.SelectionCancellationToken);
        var token=selected.Token;var lease=Guid.NewGuid().ToString("N");IDiscordReceiver? receiver=null;
        try
        {
            receiver=await client!.OpenReceiverAsync(token);
            token.ThrowIfCancellationRequested();
            if(client.Reference!=current.StreamSession)return;
            var attached=current.StreamActivityUserId is { } userId
                ?await client.InvokeAsync("watchActivity",new{account=data.Account,channelId=data.ChannelId,target=data.ChannelId,userId,key=current.StreamKey,lease,slot=slot.Number,videoView=receiver.Reference.Id},token)
                :await client.InvokeAsync("watch",new{account=data.Account,channelId=data.ChannelId,key=current.StreamKey,lease,slot=slot.Number,videoView=receiver.Reference.Id},token);
            if(client.Reference!=current.StreamSession)return;
            if(attached.GetProperty("lease").GetString()!=lease || attached.GetProperty("videoView").GetString()!=receiver.Reference.Id)
                throw new InvalidDataException("Video attachment belongs to another selection.");
            receiver.Bind(lease);
            var readyRelay=new Relay(data.Account,data.ChannelId!,lease,receiver);
            var published=slot.State.Update(p=>context.IsCurrent&&slot.Pin.IsSelected&&!token.IsCancellationRequested&&client.Reference==current.StreamSession&&p.StreamSession==current.StreamSession&&p.Account==current.Account&&p.Channel==current.Channel&&p.StreamKey==current.StreamKey
                ?(p with{Receiver=readyRelay},true):(p,false)).Result;
            if(published){receiver=null;MonitorReceiver(slot,readyRelay);}
        }
        catch(Exception error) when(!token.IsCancellationRequested)
        {
            if(context.IsCurrent){
                var message=error is DiscordCommandException command ? command.Code switch
                {
                    "watch_close_pending"=>"Discord is still closing the previous watch. Wait a moment, then select the screen again.",
                    "video_unavailable"=>"Discord did not provide an incoming video track. Open Discord to check the stream, then retry.",
                    "watch_demand_unavailable" or "watch_demand_replaced"=>"Discord's incoming video controls are unavailable. Open Discord to watch this stream.",
                    "watch_ownership_unavailable"=>"Discord's stream subscription status is unavailable. Open Discord to check the stream.",
                    _=>"The shared screen could not connect. Select it again to retry."
                }:"The shared screen could not connect. Select it again to retry.";
                slot.State.Update(p=>p with{ReceiverFailure=new(message)});
                view.Update(p=>p with{Notice=message});
            }
        }
        finally
        {
            if(receiver is not null)await receiver.DisposeAsync();
            if(slot.State.Value.Receiver?.Lease!=lease)await ReleaseLeaseAsync(data.Account,lease);
        }
    },WidgetOperationLifetime.Widget);
    private async Task CloseReceiverAsync(WatchSlot slot)
    {
        var old=slot.State.Update(current=>(current with{Receiver=null},current)).Result;
        if(old.Receiver is { } relay)
        {await relay.Receiver.DisposeAsync();await ReleaseLeaseAsync(relay.Account,relay.Lease);}
    }
    private async Task ReleaseLeaseAsync(string account,string lease)
    {try{using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(3));await client!.InvokeAsync("unwatch",new{account,lease},cleanup.Token);}catch(Exception){/* Provider expiry also bounds abandoned relays. */}}
    protected override async ValueTask OnDestroyingAsync(CancellationToken shutdownToken)
    {
        if(client is not null)client.Invalidated-=ProviderInvalidated;
        try{foreach(var slot in watches)await CloseReceiverAsync(slot);if(view.Value.Outgoing is { } outgoing)await StopCaptureAsync(outgoing);}
        finally{if(client is not null)await client.DisposeAsync();}
    }
}
