using System.Text.Json;
using System.Globalization;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private sealed record VoiceDialog(WidgetNavigationSnapshot<Route> Visit,string Account,WebSessionReference Session,
        string? Channel=null,string? Participant=null,string? Name=null);
    private sealed record VoiceControlsState(VoiceDialog? Dialog=null,string Notice="");
    private sealed record VoiceSettingsObservation(VoiceDialog Owner,DiscordVoiceSettings Settings);
    private WidgetModel<VoiceControlsState> voiceControls=null!;
    private WidgetResource<VoiceSettingsObservation> voiceSettings=null!;

    private void InitializeVoiceControls()
    {
        voiceControls=CreateModel(new VoiceControlsState());
        voiceSettings=CreateResource("discord.voice-settings",new WidgetResourceOptions<VoiceSettingsObservation>
        {
            Load=ReadVoiceSettingsAsync,Lifetime=WidgetOperationLifetime.Active,RetainLastGoodValue=false,
            MapError=_=>new("discord.voice-settings.unavailable","Voice settings are unavailable. Open Discord to change them, or refresh devices.")
        });
    }

    private static string VoiceConnectionLabel(DiscordState data)=>data.VoiceConnection switch
    {
        "connected"=>"Voice connected","connecting"=>"Connecting to voice…","reconnecting"=>"Reconnecting to voice…",
        "disconnected"=>"Voice disconnected",_=>"Voice status unavailable"
    };

    private static string VoiceSettingsAction(DiscordState data,WebSessionReference? session)=>DomainAction("voice-settings",session?.Id,data.Account);
    private static string VoiceAction(string method,VoiceDialog dialog,string? intent=null)=>
        DomainAction(method,dialog.Session.Id,dialog.Account,dialog.Channel,dialog.Participant,dialog.Visit.Revision.ToString(CultureInfo.InvariantCulture),intent);
    private static string VoiceCloseAction(WidgetNavigationSnapshot<Route> route)=>
        DomainAction("voiceClose",route.InputScopeId,route.Revision.ToString(CultureInfo.InvariantCulture));

    private WidgetElement ParticipantRow(ParticipantContext context,DiscordParticipant participant,WidgetIndexedItemContext ids)
    {
        var copy=new List<WidgetElement>{UI.Text(participant.Name,ids.Id("name")).Classes("discord-row-title")};
        if(participant.Id==context.Account)copy.Add(UI.Text("You",ids.Id("self")).Classes("discord-person-self"));
        var content=new WidgetElement[]{
            UI.Stack(ids.Id("avatar-frame"),Avatar(participant.AvatarUrl,ids.Id("avatar"),participant.Name))
                .Classes(participant.Speaking&&!participant.Muted&&!participant.Deafened?"discord-avatar-speaking":"discord-avatar-frame"),
            UI.Stack(ids.Id("copy"),copy.ToArray()).Classes("discord-copy"),
            participant.Muted||participant.Deafened||participant.Audio is {Available:true,Muted:true}?UI.Icon(participant.Deafened?DiscordIcons.Voice:DiscordIcons.Muted,ids.Id("status"),ParticipantStatus(participant)).Classes("discord-person-state"):
                UI.Spacer(ids.Id("status"))};
        var label=participant.Name+", "+ParticipantStatus(participant);
        return (participant.Id==context.Account?UI.ReadingSurface(ids.Id("participant"),label,ActionSurfaceOrientation.Horizontal,content):
            UI.ActionSurface("voice-audio",ids.Id("participant"),label+", voice audio controls",ActionSurfaceOrientation.Horizontal,content)).Classes("discord-person");
    }

    private ValueTask OpenParticipantAudioAsync(ParticipantContext context,DiscordParticipant participant,WidgetActionEvent action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var data=CurrentState();
        if(action.ActionId!="voice-audio"||navigation.Value.Route!=Route.Home||client?.Reference is not { } session||session!=context.Session||
            data is null||data.Account!=context.Account||data.ChannelId!=context.Channel||participant.Id==data.Account||
            !data.Participants.Any(person=>person.Id==participant.Id))return ValueTask.CompletedTask;
        PushRoute(Route.ParticipantAudio,action);
        if(navigation.Value.Route==Route.ParticipantAudio)
            voiceControls.Update(_=>new(new(navigation.Value,data.Account,session,data.ChannelId,participant.Id,participant.Name)));
        return ValueTask.CompletedTask;
    }

    private bool VoiceDialogCurrent(VoiceDialog dialog,DiscordState? data=null)=>
        ReferenceEquals(navigation.Value,dialog.Visit)&&!dialog.Visit.RouteCancellationToken.IsCancellationRequested&&client?.Reference==dialog.Session&&
        (data??CurrentState()) is {Ready:true} current&&current.Account==dialog.Account&&
        (dialog.Participant is null||current.ChannelId==dialog.Channel&&current.Participants.Any(person=>person.Id==dialog.Participant));

    private void ReconcileVoiceDialog(DiscordState next)
    {
        if(voiceControls.Value.Dialog is not { } dialog||VoiceDialogCurrent(dialog,next))return;
        if(ReferenceEquals(navigation.Value,dialog.Visit))navigation.Back();
        voiceSettings.Reset();voiceControls.Update(_=>new());
    }

    private void ResumeVoiceControls()
    {
        if(voiceControls.Value.Dialog is {Participant:null} dialog&&VoiceDialogCurrent(dialog))voiceSettings.EnsureLoaded(forceRefresh:true);
    }

    private async ValueTask<VoiceSettingsObservation> ReadVoiceSettingsAsync(CancellationToken token)
    {
        var dialog=voiceControls.Value.Dialog;
        if(dialog is null||dialog.Participant is not null||!VoiceDialogCurrent(dialog))throw new OperationCanceledException();
        var result=await client!.InvokeAsync("voiceSettings",new{account=dialog.Account},token);
        token.ThrowIfCancellationRequested();
        if(!VoiceDialogCurrent(dialog))throw new OperationCanceledException();
        var settings=result.Deserialize<DiscordVoiceSettings>(DiscordWebSessionClient.Json)??throw new InvalidDataException("Missing voice settings.");
        settings.Validate();return new(dialog,settings);
    }

    internal Task WaitForVoiceSettingsAsync(CancellationToken token=default)=>voiceSettings.WhenIdleAsync(token);

    private bool TryVoiceControlAction(WidgetActionEvent action,CancellationToken token,out ValueTask task)
    {
        task=ValueTask.CompletedTask;
        var dialog=voiceControls.Value.Dialog;var data=CurrentState();
        if(action.ActionId.StartsWith("voiceClose.",StringComparison.Ordinal))
        {
            var route=navigation.Value;
            if(route.Route is Route.ParticipantAudio or Route.VoiceSettings&&action.ActionId==VoiceCloseAction(route)&&action.InputScopeId==route.InputScopeId)
            {navigation.Back(action.FocusedElementId);voiceSettings.Reset();voiceControls.Update(_=>new());}
            return true;
        }
        if(action.ActionId.StartsWith("voice-settings.",StringComparison.Ordinal))
        {
            if(data is null||client?.Reference is not { } session||navigation.Value.Route!=Route.Home||action.ActionId!=VoiceSettingsAction(data,session))return true;
            PushRoute(Route.VoiceSettings,action);
            if(navigation.Value.Route==Route.VoiceSettings)
            {
                voiceSettings.Reset();voiceControls.Update(_=>new(new(navigation.Value,data.Account,session)));
                voiceSettings.EnsureLoaded();
            }
            return true;
        }
        if(!new[]{"voiceMute.","voiceVolume.","voiceInputDevice.","voiceOutputDevice.","voiceInputVolume.","voiceRefresh.","voiceOpen."}
            .Any(prefix=>action.ActionId.StartsWith(prefix,StringComparison.Ordinal)))return false;
        if(dialog is null||!VoiceDialogCurrent(dialog)||data is null)return true;
        if(action.ActionId==VoiceAction("voiceOpen",dialog))
        {
            navigation.Back();voiceSettings.Reset();voiceControls.Update(_=>new());navigation.Push(Route.Account,"discord.account.open");return true;
        }
        if(dialog.Participant is not null)
        {
            var person=data.Participants.First(person=>person.Id==dialog.Participant);
            if(person.Audio is not {Available:true,Volume:not null} audio)return true;
            if(action.ActionId==VoiceAction("voiceMute",dialog,audio.Muted.ToString()))
                task=WriteParticipantAudioAsync(dialog,"voiceMute",!audio.Muted,token);
            else if(action.ActionId==VoiceAction("voiceVolume",dialog)&&action.RequestedValue is { } volume&&double.IsFinite(volume)&&volume is >=0 and <=100)
                task=WriteParticipantAudioAsync(dialog,"voiceVolume",volume,token);
            return true;
        }
        if(action.ActionId==VoiceAction("voiceRefresh",dialog))
        {voiceControls.Update(current=>current with{Notice=""});voiceSettings.EnsureLoaded(forceRefresh:true);return true;}
        if(voiceSettings.Snapshot is not {HasValue:true,Value: { } observed}||!ReferenceEquals(observed.Owner,dialog))return true;
        var settings=observed.Settings;
        foreach(var (method,devices,available) in new[]{("voiceInputDevice",settings.InputDevices,settings.InputAvailable),("voiceOutputDevice",settings.OutputDevices,settings.OutputAvailable)})
        {
            if(!available)continue;
            var device=devices.FirstOrDefault(device=>action.ActionId==VoiceAction(method,dialog,DeviceIntent(settings,method,device.Id)));
            if(device is not null){task=WriteVoiceSettingsAsync(dialog,method,device.Id,token);return true;}
        }
        if(settings.InputVolumeAvailable&&action.ActionId==VoiceAction("voiceInputVolume",dialog)&&
            action.RequestedValue is { } inputVolume&&double.IsFinite(inputVolume)&&inputVolume is >=0 and <=100)
            task=WriteVoiceSettingsAsync(dialog,"voiceInputVolume",inputVolume,token);
        return true;
    }

    // Bind device choices to the observed device set as well as the dialog visit.
    // Re-enumeration cannot retarget an old native Select option by its row index.
    private static string DeviceIntent(DiscordVoiceSettings settings,string method,string id)=>
        JsonSerializer.Serialize(new{id,selected=method=="voiceInputDevice"?settings.InputDeviceId:settings.OutputDeviceId,
            devices=(method=="voiceInputDevice"?settings.InputDevices:settings.OutputDevices).Select(device=>device.Id)});

    private async ValueTask WriteParticipantAudioAsync(VoiceDialog dialog,string method,object value,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!VoiceDialogCurrent(dialog))return;
        voiceControls.Update(current=>current with{Notice=""});
        try
        {
            var result=await client!.InvokeAsync(method,new{account=dialog.Account,channelId=dialog.Channel,userId=dialog.Participant,value},token);
            var audio=result.GetProperty("audio").Deserialize<DiscordStreamAudio>(DiscordWebSessionClient.Json);
            if(audio is not {Available:true,Volume:{ } volume}||!double.IsFinite(volume)||volume is <0 or >100)
                throw new InvalidDataException("Invalid participant audio acknowledgment.");
            if(!VoiceDialogCurrent(dialog))return;
            state.TryPublishUpdate(current=>current.Ready&&current.Account==dialog.Account&&current.ChannelId==dialog.Channel&&
                current.Participants.Any(person=>person.Id==dialog.Participant)
                ?(true,current with{Participants=current.Participants.Select(person=>person.Id==dialog.Participant?person with{Audio=audio}:person).ToArray()}):(false,current));
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {if(VoiceDialogCurrent(dialog))voiceControls.Update(current=>current with{Notice="Discord could not confirm the voice audio change. Try again."});}
    }

    private async ValueTask WriteVoiceSettingsAsync(VoiceDialog dialog,string method,object value,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!VoiceDialogCurrent(dialog))return;
        var before=voiceSettings.Snapshot.Value;
        if(before is null||!ReferenceEquals(before.Owner,dialog))return;
        voiceControls.Update(current=>current with{Notice=""});
        try
        {
            var result=await client!.InvokeAsync(method,new{account=dialog.Account,value},token);
            var settings=result.Deserialize<DiscordVoiceSettings>(DiscordWebSessionClient.Json)??throw new InvalidDataException("Missing voice settings acknowledgment.");
            settings.Validate();
            if(!VoiceDialogCurrent(dialog))return;
            // A device refresh may observe unplug/replacement while a write's
            // reply is in flight. Never replace that newer inventory with an
            // old full snapshot; reconcile once without replaying the write.
            if(!voiceSettings.TryPublishUpdate(current=>ReferenceEquals(current,before)?(true,new VoiceSettingsObservation(dialog,settings)):(false,current)))
                voiceSettings.EnsureLoaded(forceRefresh:true);
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {if(VoiceDialogCurrent(dialog))voiceControls.Update(current=>current with{Notice="Discord could not confirm the device change. Refresh devices and try again."});}
    }

    private WidgetView ApplyVoiceDialog(WidgetView result,DiscordState? data)
    {
        var route=navigation.Value;
        if(route.Route is not(Route.ParticipantAudio or Route.VoiceSettings))return result;
        var modalId=route.Route==Route.VoiceSettings?"discord.voice-settings-dialog":"discord.participant-dialog";
        // Session replacement can precede the next provider read. Keep a valid
        // modal scope with only Close while reconciliation retires this visit;
        // never publish the old account's controls or an absent active scope.
        if(voiceControls.Value.Dialog is not { } dialog||!VoiceDialogCurrent(dialog,data))
            return result.WithModal(new(modalId,"Voice controls",UI.Text("The account or voice channel changed. Close to return to Discord controls.",modalId+".changed"),modalId+".close",VoiceCloseAction(route)));
        const string id="discord.voice-dialog";
        var content=new List<WidgetElement>();var first=modalId+".close";
        var title=dialog.Participant is null?"Voice settings":dialog.Name??"Participant audio";
        if(dialog.Participant is not null)
        {
            var person=data!.Participants.First(person=>person.Id==dialog.Participant);
            if(person.Audio is {Available:true,Volume:{ } volume} audio)
            {
                first=id+".mute";
                content.Add(UI.Button(audio.Muted?"Unmute for me":"Mute for me",VoiceAction("voiceMute",dialog,audio.Muted.ToString()),id+".mute")
                    .Icon(audio.Muted?WidgetGlyph.Muted:WidgetGlyph.Volume).Selected(audio.Muted).Classes("discord-control"));
                content.Add(VoiceVolumeField(id+".volume", "Voice volume",volume,VoiceAction("voiceVolume",dialog)));
                if(data.Deafened)content.Add(UI.Text("Undeafen to hear voice audio.",id+".deafened").Classes("discord-secondary"));
            }
            else content.Add(UI.Text("Voice audio controls are unavailable in this Discord version.",id+".unavailable").Classes("discord-secondary"));
        }
        else
        {
            var snapshot=voiceSettings.Snapshot;
            if(snapshot is {HasValue:true,Value:{ } observed}&&ReferenceEquals(observed.Owner,dialog))
            {
                var settings=observed.Settings;
                AddDevices("input","Microphone","voiceInputDevice",settings.InputAvailable,settings.InputDevices,settings.InputDeviceId);
                AddDevices("output","Output device","voiceOutputDevice",settings.OutputAvailable,settings.OutputDevices,settings.OutputDeviceId);
                if(settings.InputVolumeAvailable&&settings.InputVolume is { } volume)
                {content.Add(VoiceVolumeField(id+".input-volume","Input volume",volume,VoiceAction("voiceInputVolume",dialog)));if(first==modalId+".close")first=id+".input-volume";}
                else content.Add(UI.Text("Input volume is unavailable in this Discord version.",id+".input-volume.unavailable").Classes("discord-secondary"));
                void AddDevices(string key,string label,string method,bool available,DiscordAudioDevice[] devices,string? selected)
                {
                    if(!available){content.Add(UI.Text(label+" selection is unavailable in this Discord version.",id+"."+key+".unavailable").Classes("discord-secondary"));return;}
                    content.Add(UI.Select(label,devices.Select((device,index)=>new SelectOption("device-"+index,device.Name.Length==0?"Unnamed device":device.Name,
                        VoiceAction(method,dialog,DeviceIntent(settings,method,device.Id)),device.Id==selected)).ToArray(),id+"."+key,label).Classes("discord-voice-select"));
                    if(first==modalId+".close")first=id+"."+key;
                }
            }
            else content.Add(UI.Text(snapshot.Error?.Message??"Loading voice settings…",id+".status").Classes("discord-secondary"));
            content.Add(UI.Row(id+".settings-actions",
                UI.Button("Refresh devices",VoiceAction("voiceRefresh",dialog),id+".refresh").Busy(voiceSettings.IsBusy).Classes("discord-control"),
                UI.Button("Open Discord",VoiceAction("voiceOpen",dialog),id+".open").Classes("discord-control","discord-quiet")).Classes("discord-actions"));
        }
        if(voiceControls.Value.Notice.Length>0)content.Add(UI.Text(voiceControls.Value.Notice,id+".notice").Classes("discord-notice"));
        return result.WithModal(new(modalId,title,UI.Stack(id+".content",content.ToArray()).Classes("discord-voice-dialog"),first,VoiceCloseAction(dialog.Visit))
        {
            // Initial-content sizing intentionally does not expand after async
            // loading. Reserve space for device controls; participant data is
            // already present and can use the compact measured presentation.
            AnchoredPresentation=new(new(ElementId:"discord.main.heading"),Width:440,Height:420)
                {SizeToContentHeight=dialog.Participant is not null}
        });
    }

    private static WidgetElement VoiceVolumeField(string id,string label,double volume,string action)=>UI.Stack(id+".field",
        UI.Row(id+".heading",UI.Text(label,id+".label").Classes("discord-row-title","discord-copy"),UI.Text($"{volume:0}%",id+".value").Classes("discord-secondary")).Classes("discord-actions"),
        UI.Slider(StreamVolumeThumb(volume),0,100,5,action,id,label,$"{volume:0}%").RequireControllerActivation().Classes("discord-audio-volume"))
        .Classes("discord-voice-volume");
}
