using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    public override WidgetView Render()
    {
        var data=state.Snapshot.HasValue?state.Snapshot.Value:null;
        var current=view.Value;var route=navigation.Value;
        var ready=data?.Ready==true;var busy=Operations.IsBusy("discord.command");
        WidgetElement content;string first;
        if(route.Route==Route.Account)
        {
            content=UI.Stack("discord.account",
                UI.Row("discord.account.header",UI.Text("Discord","discord.account.title").Classes("discord-title","discord-copy"),
                    UI.Button("Back to controls",route.BackActionId!,"discord.account.back").Classes("discord-control")),
                client?.Reference is { } reference?UI.WebView(reference,"discord.account.browser","Discord sign-in and account setup",BrowserInteractionMode.ActivateToInteract).Classes("discord-body"):
                    Empty("discord.account.wait","Connecting to Discord","Go back and try Refresh.",DiscordIcons.Account)).Classes("discord-page");
            first="discord.account.back";
        }
        else
        {
            var headerControls=new List<WidgetElement>
            {
                UI.Icon(DiscordIcons.Voice,"discord.brand","Discord").Classes("discord-brand"),
                UI.Text("Discord","discord.title").Classes("discord-title","discord-copy"),
                UI.IconButton(DiscordIcons.Refresh,"refresh","discord.refresh","Refresh Discord",size:IconButtonSize.Small).Classes("discord-toolbar-button")
            };
            if(route.Route==Route.Home&&ready&&data!.ChannelId is null)
                headerControls.Add(UI.Button("Friends","friends","discord.friends.open").Classes("discord-control","discord-quiet")
                    .VisibleWhen(ResponsiveVisibility.CompactOnly));
            headerControls.Add(UI.Button("Open Discord","account","discord.account.open").Icon(DiscordIcons.Account)
                .Disabled(client?.Reference is null).Classes("discord-control","discord-quiet"));
            var header=UI.Row("discord.header",headerControls.ToArray()).Classes("discord-header");
            WidgetElement body;
            if(!ready)
            {
                body=UI.VerticalScroll("discord.connecting",
                    route.Route is Route.Channels or Route.Friends?UI.Button("Back",route.BackActionId!,"discord.picker.back").Classes("discord-control","discord-quiet"):
                        UI.Spacer("discord.connecting.header"),
                    Empty("discord.connection",state.Snapshot.Error is not null?"Discord is unavailable":data is null?"Connecting to Discord…":"Sign in to Discord",
                        state.Snapshot.Error?.Message??(data is null?"":"Open Discord to sign in."),DiscordIcons.Account),
                    UI.Button("Open Discord","account","discord.setup.open").Disabled(client?.Reference is null).Classes("discord-control","discord-primary"),
                    current.Outgoing is not null||current.PreparingShare is not null?SharingControls(data):UI.Spacer("discord.share.inactive")).Classes("discord-page-scroll");
                first=client?.Reference is not null?"discord.setup.open":"discord.refresh";
            }
            else if(route.Route==Route.Friends)
            {
                PublishActivities(data!);
                body=UI.Stack("discord.friends.page",
                    UI.Button("Back",route.BackActionId!,"discord.friends.back").Classes("discord-control","discord-quiet"),
                    ActivityPanel(data!,"discord.activities",false).Classes("discord-page")).Classes("discord-page");
                first="discord.friends.back";
            }
            else if(route.Route==Route.Channels)
            {
                body=UI.Stack("discord.picker.page",
                    UI.Row("discord.picker.header",UI.Text("Servers & voice channels","discord.picker.title").Classes("discord-section-title","discord-copy"),
                        UI.Button("Back",route.BackActionId!,"discord.picker.back").Classes("discord-control","discord-quiet")),
                    PickerPage(data!)).Classes("discord-page-scroll");
                first=PickerInitialFocusId(data!);
            }
            else
            {
                PublishActivities(data!);
                WidgetElement primary;
                if(data!.ChannelId is null)
                {
                    var picker=PickerPage(data);
                    primary=CanStopSharing(current,data)
                        ?UI.Stack("discord.primary",SharingControls(data),picker)
                        :UI.Stack("discord.primary",picker);
                }
                else
                {
                    primary=UI.VerticalScroll("discord.primary",CallPage(data,busy).AddClasses("discord-main-content"),
                        ActivityPanel(data,"discord.activities.compact",true).VisibleWhen(ResponsiveVisibility.CompactOnly));
                }
                body=UI.Row("discord.unified",primary.AddClasses("discord-primary-pane"),
                    ActivityPanel(data,"discord.activities",false).VisibleWhen(ResponsiveVisibility.ExpandedOnly)).Classes("discord-body","discord-columns");
                first=data.ChannelId is null?PickerInitialFocusId(data):data.Capabilities.Mute?"discord.main.mute":"discord.main.servers";
            }
            var children=new List<WidgetElement>{header};
            if (route.Route == Route.Home && data is { Ready: true, NotificationPermission: "default" or "denied" } && client?.Reference is { } session)
                children.Add(UI.Button("Enable notifications", DomainAction("notifications", session.Id, data.Account), "discord.notifications.enable")
                    .Classes("discord-control", "discord-quiet"));
            if(current.Notice.Length>0)children.Add(UI.Text(current.Notice,"discord.notice").Classes("discord-notice"));
            children.Add(body);
            content=UI.Stack("discord.home",children.ToArray()).Classes("discord-page");
        }
        var bodyRoot=UI.Stack("discord.shell",content).Classes("discord-root");
        // A dialog route retains the Home background scope. WithModal supplies
        // the dialog scope registered with the navigator, so Back and indexed
        // return focus use the same SDK-owned visit without duplicate scopes.
        ContainerElement root=route.Route is Route.ParticipantAudio or Route.VoiceSettings or Route.ConnectionDetails?bodyRoot.InputScope("discord.root"):navigation.Scope(route,bodyRoot);
        if(route.InitialFocusId is { } restored&&WidgetFocusTargetLookup.Resolve(root,restored,route.InputScopeId).IsEnabled)first=restored;
        if(route.Route==Route.Home&&ready&&data!.ChannelId is not null)
        {
            if(data.Capabilities.Mute)root=root.Shortcut(ControllerButton.X,CallAction("mute",data),data.Muted?"Unmute":"Mute");
            if(CanStartSharing(data,current))root=root.Shortcut(ControllerButton.Y,SharingAction(false,data,current,client?.Reference),"Share game");
            root=root.Shortcut(ControllerButton.LeftStick,"server-picker","Servers & channels");
            var commands=new List<WidgetContextAction>{
                new("server-picker","Servers & channels"),
                new(CallAction("deafen",data),data.Deafened?"Undeafen":"Deafen",IsDisabled:!data.Capabilities.Deafen),
                new(CallAction("pin-call",data),"Pin voice controls"),
                new(VoiceSettingsAction(data,client?.Reference),"Voice settings"),
                new(ConnectionDetailsAction(data,client?.Reference),"Connection details"),
                new("account","Open Discord"),new("refresh","Refresh")};
            if(current.Outgoing is not null&&data.Sharing)commands.Insert(0,new(ShareDetailsAction(data,current,client?.Reference),"Stream details"));
            root=root.ContextMenu(ControllerButton.Menu,commands.ToArray());
        }
        else if(route.Route==Route.Home&&ready)
            root=root.ContextMenu(ControllerButton.Menu,new(VoiceSettingsAction(data!,client?.Reference),"Voice settings"),new("account","Open Discord"),new("refresh","Refresh"));
        var pin=current.Receiver is { } receiver?(WidgetElement)UI.WebView(receiver.Receiver.Reference,"discord.pin.video","Discord shared screen",BrowserInteractionMode.ActivateToInteract).Classes("discord-video"):
            current.ReceiverFailure is { } failure?Empty("discord.pin.status","Screen unavailable",failure.Message,DiscordIcons.Screen).Classes("discord-pin"):
            Empty("discord.pin.status",current.StreamName is null?"No screen selected":"Connecting to "+current.StreamName,
                "Choose a shared screen in the call.",DiscordIcons.Screen).Classes("discord-pin");
        pin=UI.Stack("discord.stream.pin",current.Receiver is not null?pin.Classes("discord-video-pane"):pin).Classes("discord-stream-pin");
        var returnFocus=route.CollectionReturnFocus;
        var collectionFocus=returnFocus is not null&&WidgetFocusTargetLookup.Resolve(root,returnFocus.Item.CollectionId,route.InputScopeId).IsEnabled&&
            CollectionSource(returnFocus.Item.CollectionId) is { } source
            ?returnFocus.Resolve(returnFocus.Item.CollectionId,source):null;
        var result=new WidgetView(root,first,ActiveInputScopeId:route.InputScopeId,
            Surface:new(){PreferredWidth=1040,PreferredHeight=680,MinimumWidth=420,MinimumHeight=360})
        {FocusGroupEntryRequest=collectionFocus??route.FocusGroupEntryRequest,PinnedLayouts=[streamPin.Present(pin),callPin.Present(VoicePin(data,busy),
            ready&&data!.ChannelId is not null?data.Capabilities.Mute?"discord.pin.mute":data.Capabilities.Deafen?"discord.pin.deafen":"discord.pin.leave":null)]};
        return ApplyConnectionDetails(ApplyVoiceDialog(result,data),data);
    }

    private WidgetElement CallPage(DiscordState data,bool busy)
    {
        participants.Publish(data.Account+"\u001f"+data.ChannelId+"\u001f"+client?.Reference?.Id,new(data.Account,data.ChannelId,client?.Reference),data.Participants);
        var guild=data.Channels.FirstOrDefault(channel=>channel.Id==data.ChannelId)?.Guild;
        var rows=new List<WidgetElement>{
            UI.Stack("discord.main.call",
                UI.Row("discord.main.heading",
                    UI.Icon(DiscordIcons.Voice,"discord.main.voice-icon","Voice call").Classes("discord-call-emblem"),
                    UI.Stack("discord.main.heading.copy",
                        UI.Text(data.ChannelName,"discord.main.channel").Classes("discord-call-title"),
                        UI.Row("discord.main.meta",UI.Text(guild??"Voice call","discord.main.guild").Classes("discord-secondary"),
                            UI.Text(busy?"Updating…":data.Participants.Length+" in call","discord.main.state").Classes("discord-secondary"))).Classes("discord-copy","discord-call-heading"),
                    UI.Row("discord.main.tools",
                        UI.IconButton(DiscordIcons.Pin,CallAction("pin-call",data),"discord.voice.pin","Pin voice controls",size:IconButtonSize.Small).Classes("discord-toolbar-button"),
                        UI.IconButton(DiscordIcons.Channels,"server-picker","discord.main.servers","Choose a server or voice channel",size:IconButtonSize.Small).Classes("discord-toolbar-button")).Classes("discord-actions"))
                    .Classes("discord-call-header"),
                UI.Button(VoiceConnectionLabel(data),ConnectionDetailsAction(data,client?.Reference),"discord.main.connected")
                    .Classes("discord-connection-link",data.VoiceConnection=="connected"?"discord-connected":"discord-connecting"),
                UI.ResponsiveGrid("discord.main.actions",132,4,
                UI.Button(data.Muted?"Unmute":"Mute",CallAction("mute",data),"discord.main.mute").Icon(data.Muted?DiscordIcons.Muted:DiscordIcons.Microphone)
                    .Disabled(!data.Capabilities.Mute).Selected(data.Muted).Classes("discord-control","discord-call-toggle"),
                UI.Button(data.Deafened?"Undeafen":"Deafen",CallAction("deafen",data),"discord.main.deafen").Icon(DiscordIcons.Voice)
                    .Disabled(!data.Capabilities.Deafen).Selected(data.Deafened).Classes("discord-control","discord-call-toggle"),
                ShareButton(data),
                UI.Button("Leave",CallAction("leave",data),"discord.main.leave").Icon(DiscordIcons.Leave).Classes("discord-control","discord-leave"))
                    .Classes("discord-call-actions")).Classes("discord-call-panel"),
            UI.CollectionList("discord.participants",participants,176,"Call participants",ScrollAxis.Horizontal).Classes("discord-participants")};
        if(data.Streams.Length>0)
        {
            rows.Add(SectionTitle("discord.streams.heading","Screens in this call",data.Streams.Length.ToString()));
            foreach(var stream in data.Streams)
            {
                var id="discord.stream."+stream.UserId;var selected=view.Value.StreamKey==stream.Key;
                var card=new List<WidgetElement>{UI.ActionSurface(WatchAction(data,stream),id,"Watch "+stream.Name,ActionSurfaceOrientation.Horizontal,
                    UI.Icon(DiscordIcons.Screen,id+".icon","Screen share").Classes("discord-stream-icon"),
                    UI.Stack(id+".copy",UI.Text(stream.Name,id+".name").Classes("discord-row-title"),
                        UI.Text(selected?"Pinned":"Live",id+".state").Classes("discord-secondary","discord-connected")).Classes("discord-copy"),
                    UI.Text(selected?"Watch again":"Watch & pin",id+".action").Classes("discord-row-state","discord-activity-action"))
                    .Disabled(!data.Capabilities.Watch).Classes("discord-stream-action")};
                if(WatchedStream(data,view.Value,client?.Reference) is { } watched&&watched.Key==stream.Key)
                    card.Add(StreamAudioControls(data,watched,view.Value,"discord.streams.audio"));
                if(selected)card.Add(UI.Button("Stop watching",UnwatchAction(view.Value),"discord.streams.stop").Classes("discord-control","discord-quiet"));
                rows.Add(UI.Stack(id+".card",card.ToArray()).Classes("discord-stream-card"));
            }
        }
        if(view.Value.StreamKey is { } key&&!data.Streams.Any(stream=>stream.Key==key))
            rows.Add(UI.Button("Stop watching",UnwatchAction(view.Value),"discord.streams.stop").Classes("discord-control"));
        if(CanStopSharing(view.Value,data)||view.Value.InterruptedCapture is not null||data.ShareError is not null)
            rows.Add(SharingDetails(data));
        return UI.Stack("discord.call.page",rows.ToArray()).Classes("discord-call-content");
    }

    private void PublishActivities(DiscordState data)
    {
        var ordered=data.Activities.OrderBy(row=>row.CanWatch?0:row.CanJoinVoice?1:2)
            .ThenBy(row=>row.Name,StringComparer.OrdinalIgnoreCase).ThenBy(row=>row.UserId,StringComparer.Ordinal).ToArray();
        activities.Publish(data.Account,data.Account,ordered);
    }

    private WidgetElement ActivityPanel(DiscordState data,string listId,bool compact)
    {
        var children=new List<WidgetElement>{SectionTitle(listId+".heading","Friends active now","")};
        if(data.ActivityAvailability!="ready")children.Add(UI.Text("Activity unavailable",listId+".unavailable").Classes("discord-secondary"));
        else if(data.Activities.Length==0)children.Add(UI.Text("No friends active",listId+".empty").Classes("discord-secondary"));
        else children.Add(UI.CollectionList(listId,activities,104,"Friends active now").Classes(compact?"discord-activity-compact-list":"discord-list"));
        if(data.ActivitiesTruncated)children.Add(UI.Text("Showing 50 active friends",listId+".limit").Classes("discord-secondary"));
        return UI.Stack(listId+".panel",children.ToArray()).Classes(compact?"discord-activity-compact":"discord-activity-pane");
    }

    private static RowElement SectionTitle(string id,string title,string count)=>UI.Row(id,
        UI.Text(title,id+".title").Classes("discord-section-title","discord-copy"),
        UI.Text(count,id+".count").Classes("discord-secondary")).Classes("discord-section-header");
    private static StackElement Empty(string id,string title,string detail,WidgetIcon icon)=>UI.Stack(id,
        UI.Icon(icon,id+".icon",title).Classes("discord-empty-icon"),UI.Text(title,id+".title").Classes("discord-row-title"),
        UI.Text(detail,id+".detail").Classes("discord-secondary")).Classes("discord-empty");
    private static string ParticipantStatus(DiscordParticipant participant)=>participant.Audio is {Available:true,Muted:true}?"Muted for you":
        participant.Deafened?"Deafened":participant.Muted?"Muted":participant.Speaking?"Speaking":"Listening";
    private static WidgetElement Avatar(string? url,string id,string name)=>url is null?UI.Icon(DiscordIcons.Person,id,name).Classes("discord-avatar"):UI.Image(url,id,name).Classes("discord-avatar");

    private StackElement VoicePin(DiscordState? data,bool busy)
    {
        const string id="discord.pin";var inCall=data?.Ready==true&&data.ChannelId is not null;
        var guild=inCall?data!.Channels.FirstOrDefault(channel=>channel.Id==data.ChannelId)?.Guild:null;
        var summary=new List<WidgetElement>{
            UI.Row(id+".heading",UI.Icon(DiscordIcons.Voice,id+".icon","Voice chat").Classes("discord-voice-icon"),
                UI.Stack(id+".heading.copy",UI.Text(inCall?data!.ChannelName:"Not in voice",id+".channel").Classes("discord-voice-title"),
                    UI.Text(guild??"Voice chat",id+".guild").Classes("discord-voice-guild")).Classes("discord-copy"),
                UI.Text(busy?"Updating…":inCall?data!.VoiceConnection=="connected"?data.Participants.Length+" in call":data.VoiceConnection switch
                    {"connecting"=>"Connecting…","reconnecting"=>"Reconnecting…","disconnected"=>"Disconnected",_=>"Status unavailable"}:"",id+".state").Classes("discord-voice-count")).Classes("discord-voice-heading")};
        WidgetElement controls;
        if(inCall)
        {
            // Keep provider order stable while speaking changes. Only the small
            // visible set requests artwork; decorative people are not focus stops.
            var shown=data!.Participants.Take(3).ToArray();
            var avatars=shown.Select(person=>UI.Stack(id+".person."+person.Id,
                Avatar(person.AvatarUrl,id+".avatar."+person.Id,person.Name+", "+ParticipantStatus(person)).AddClasses("discord-voice-avatar"))
                .Classes("discord-voice-avatar-frame",person.Speaking&&!person.Muted&&!person.Deafened?"discord-voice-speaking":"discord-voice-listening")).ToArray();
            summary.Add(UI.Row(id+".people.row",UI.Row(id+".avatars",avatars).Classes("discord-voice-avatars"),
                UI.Text(string.Join(" · ",shown.Select(person=>person.Name)),id+".people").Classes("discord-voice-names","discord-copy"),
                UI.Text(data.Participants.Length>shown.Length?"+"+(data.Participants.Length-shown.Length):"",id+".more").Classes("discord-voice-count"))
                .Classes("discord-voice-people"));
            controls=UI.Grid(id+".actions",[],[GridTrack.Star(),GridTrack.Star(),GridTrack.Star()],
                UI.Button(data!.Muted?"Unmute":"Mute",CallAction("mute",data),id+".mute").Icon(data.Muted?DiscordIcons.Muted:DiscordIcons.Microphone).Disabled(!data.Capabilities.Mute).Selected(data.Muted).Classes("discord-control","discord-voice-button","discord-voice-toggle"),
                UI.Button(data.Deafened?"Undeafen":"Deafen",CallAction("deafen",data),id+".deafen").Icon(DiscordIcons.Voice).Disabled(!data.Capabilities.Deafen).Selected(data.Deafened).Classes("discord-control","discord-voice-button","discord-voice-toggle").InGrid(column:1),
                UI.Button("Leave",CallAction("leave",data),id+".leave").Icon(DiscordIcons.Leave).Classes("discord-control","discord-voice-button","discord-leave").InGrid(column:2))
                .Spacing(0,8).Classes("discord-voice-actions");
        }
        else controls=UI.Text("Open Discord to join a channel.",id+".idle").Classes("discord-secondary");
        return UI.Stack(id+".controls",UI.Stack(id+".summary",summary.ToArray()).Classes("discord-voice-summary"),controls).Classes("discord-voice-pin");
    }

    private static WidgetElement ActivityRow(DiscordFriendActivity activity,string id)
    {
        var action=activity.CanWatch?"activity.watch":activity.CanJoinVoice?"activity.join":null;
        var activityLabel=activity.Kind switch{"playing"=>"Playing","streaming"=>"Streaming","listening"=>"Listening to","watching"=>"Watching","competing"=>"Competing in",_=>"In voice"};
        var destination=string.Join(" · ",new[]{activity.ChannelName,activity.GuildName}.Where(value=>!string.IsNullOrWhiteSpace(value)));
        var detail=activity.Kind=="voice"?"Voice"+(destination.Length>0?" · "+destination:""):activityLabel+(activity.Title.Length==0?"":" "+activity.Title);
        if(activity.Kind!="voice")
        {
            if(activity.CanJoinVoice&&destination.Length>0)detail+="\nVoice · "+destination;
            else if(activity.Details.Length>0)detail+=" · "+activity.Details;
        }
        var presence=activity.Status switch{"idle"=>"Away","dnd"=>"Do not disturb","offline"=>"Offline",_=>"Online"};
        var children=new List<WidgetElement>{UI.Row(id+".header",Avatar(activity.AvatarUrl,id+".avatar",activity.Name),
            UI.Text(activity.Name,id+".name").Classes("discord-row-title","discord-copy")).Classes("discord-actions","discord-activity-header"),UI.Text(detail,id+".detail").Classes("discord-secondary","discord-activity-copy")};
        if(action is not null)children.Add(UI.Text(action=="activity.watch"?"Watch & pin":"Join voice",id+".state").Classes("discord-activity-action","discord-activity-copy"));
        var label=activity.Name+", "+detail+", "+presence;
        return (action is null?UI.ReadingSurface(id,label,ActionSurfaceOrientation.Vertical,children.ToArray()):
            UI.ActionSurface(action,id,label+", "+(action=="activity.watch"?"watch stream":"join voice"),ActionSurfaceOrientation.Vertical,children.ToArray())).Classes("discord-activity-row");
    }
}
