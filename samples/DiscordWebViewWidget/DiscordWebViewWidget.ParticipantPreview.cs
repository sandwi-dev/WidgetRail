using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;
public sealed partial class DiscordWebViewWidget
{
    private void ReconcileParticipantContext(DiscordState next)
    {
        var channel=next.Ready?next.ChannelId:null;
        var members=string.Join("|",next.Participants.Select(person=>person.Id));
        var session=client?.Reference;
        voiceControls.Update(current=>current.Participants is { } previous&&previous.Account==next.Account&&
            previous.Channel==channel&&previous.Session==session&&previous.Members==members?current:
            current with{Participants=new(next.Account,channel,session,members,Guid.NewGuid().ToString("N"))});
    }
    private bool ParticipantPreviewCurrent(ParticipantContext context,string personId) =>
        navigation.Value.Route==Route.Home && context.Channel is not null && context.Session is not null &&
        client?.Reference==context.Session && voiceControls.Value.Participants?.Epoch==context.Epoch &&
        CurrentState() is {Ready:true} data && data.Account==context.Account && data.ChannelId==context.Channel &&
        personId!=data.Account && data.Participants.Any(person=>person.Id==personId);

    private static string ParticipantPreviewAction(string method,ParticipantContext context,DiscordParticipant person,string? intent=null) =>
        DomainAction(method,context.Epoch,person.Id,intent);

    private static WidgetElement ParticipantAudioPreview(ParticipantContext context,DiscordParticipant person,WidgetIndexedItemContext ids)
    {
        var root=ids.Id("audio-preview");
        var children=new List<WidgetElement>
        {
            UI.Row(root+".heading",Avatar(person.AvatarUrl,root+".avatar",person.Name),
                UI.Text(person.Name,root+".name").Classes("discord-row-title")).Classes("discord-participant-preview-heading")
        };
        if(person.Audio is {Available:true,Volume:{ } volume} audio)
        {
            children.Add(UI.Button(audio.Muted?"Unmute for me":"Mute for me",
                ParticipantPreviewAction("participantMute",context,person,audio.Muted.ToString()),root+".mute")
                .Icon(audio.Muted?WidgetGlyph.Muted:WidgetGlyph.Volume).Selected(audio.Muted).Classes("discord-control"));
            children.Add(VoiceVolumeField(root+".volume","Voice volume",volume,ParticipantPreviewAction("participantVolume",context,person)));
        }
        else children.Add(UI.Text("Voice audio controls are unavailable in this Discord version.",root+".unavailable").Classes("discord-secondary"));
        if(context.NoticeUser==person.Id&&context.Notice.Length>0)
            children.Add(UI.Text(context.Notice,root+".notice").Classes("discord-notice"));
        return UI.Stack(root,children.ToArray()).Classes("discord-participant-preview");
    }
    private async ValueTask HandleParticipantPreviewAsync(ParticipantContext context,DiscordParticipant person,WidgetActionEvent action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!ParticipantPreviewCurrent(context,person.Id) || person.Audio is not {Available:true,Volume:not null} before) return;
        var current=CurrentState()!.Participants.First(item=>item.Id==person.Id);
        if(current.Audio is not {Available:true,Volume:not null} observed)return;
        string method;object value;
        if(action.ActionId==ParticipantPreviewAction("participantMute",context,person,before.Muted.ToString())&&observed.Muted==before.Muted)
        {method="voiceMute";value=!before.Muted;}
        else if(action.ActionId==ParticipantPreviewAction("participantVolume",context,person)&&action.RequestedValue is { } requested&&
            SliderMath.IsValidRequestedValue(requested,0,100,5))
        {method="voiceVolume";value=requested;}
        else return;
        Notice("");
        try
        {
            var result=await client!.InvokeAsync(method,new{account=context.Account,channelId=context.Channel,userId=person.Id,value},token);
            token.ThrowIfCancellationRequested();
            var audio=result.GetProperty("audio").Deserialize<DiscordStreamAudio>(DiscordWebSessionClient.Json);
            if(audio is not {Available:true,Volume:{ } volume}||!double.IsFinite(volume)||volume is <0 or >100)
                throw new InvalidDataException("Invalid participant audio acknowledgment.");
            // Acknowledgment is not a full current observation. Reuse the bounded
            // post-invalidation read, so late replies cannot overwrite newer audio,
            // a reopened preview, or a replacement call. Never replay the write.
            if(client?.Reference==context.Session)ProviderInvalidated(context.Session!);
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {Notice("Discord could not confirm the audio change. Try again.");}
        void Notice(string message)
        {
            if(!ParticipantPreviewCurrent(context,person.Id))return;
            voiceControls.Update(state=>state.Participants?.Epoch==context.Epoch &&
                !(message.Length==0&&state.Participants.Notice.Length==0)?state with
            {Participants=state.Participants with{NoticeUser=person.Id,Notice=message}}:state);
        }
    }
}
