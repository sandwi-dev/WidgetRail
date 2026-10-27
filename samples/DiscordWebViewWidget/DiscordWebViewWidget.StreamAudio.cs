using System.Text.Json;
using WidgetRail.Samples.DiscordPrototype;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    private DiscordStream? WatchedStream(DiscordState? data,ViewState selected,WebSessionReference? session)=>
        streamPin.IsSelected && selected.Receiver is { } receiver && data is { Ready:true } &&
        data.Account==selected.Account && data.ChannelId==selected.Channel && session==selected.StreamSession &&
        receiver.Account==data.Account && receiver.Channel==data.ChannelId
            ?data.Streams.FirstOrDefault(stream=>stream.Key==selected.StreamKey):null;

    private static string StreamAudioAction(string method,DiscordState data,DiscordStream stream,ViewState selected,WebSessionReference? session)=>
        DomainAction(method,session?.Id,data.Account,data.ChannelId,stream.Key,stream.UserId,selected.Receiver?.Lease,
            method=="streamMute"?stream.Audio?.Muted.ToString():null);

    private static double StreamVolumeThumb(double observed)
    {
        // Discord snaps a request near its default back to roughly 54.2%.
        // Align the thumb with this slider's 5% steps so the next deliberate
        // Right goes from 55 to 60 instead of requesting 55 indefinitely.
        // Labels and state retain the actual observation. Never strand a real
        // endpoint behind a rounded 0/100 thumb.
        var thumb=Math.Round(observed/5,MidpointRounding.AwayFromZero)*5;
        return thumb==0&&observed>0||thumb==100&&observed<100?observed:thumb;
    }

    private WidgetElement StreamAudioControls(DiscordState data,DiscordStream stream,ViewState selected,string id)
    {
        var audio=stream.Audio;
        if(audio is not { Available:true,Volume:not null })
            return UI.Text("Audio controls unavailable",id+".unavailable").Classes("discord-secondary");
        var label=data.Deafened?"Stream volume · Deafened":"Stream volume";
        var volume=audio.Volume.Value;
        var controls=UI.Row(id+".controls",
            UI.IconButton(audio.Muted?WidgetGlyph.Muted:WidgetGlyph.Volume,StreamAudioAction("streamMute",data,stream,selected,client?.Reference),id+".mute",
                audio.Muted?"Unmute stream":"Mute stream",size:IconButtonSize.Small).Selected(audio.Muted).Classes("discord-audio-mute"),
            UI.Slider(StreamVolumeThumb(volume),0,100,5,StreamAudioAction("streamVolume",data,stream,selected,client?.Reference),id+".volume",label,$"{volume:0}%")
                .RequireControllerActivation().Classes("discord-audio-volume"),
            UI.Text($"{volume:0}%",id+".value").Classes("discord-audio-value")).Classes("discord-audio-row");
        var content=new List<WidgetElement>{controls};
        if(data.Deafened)content.Add(UI.Text("Undeafen to hear audio.",id+".deafened").Classes("discord-secondary"));
        return UI.Stack(id,content.ToArray()).Classes("discord-stream-audio");
    }

    private bool TryStreamAudioAction(WidgetActionEvent action,DiscordState? data,ViewState selected,
        WebSessionReference? session,CancellationToken token,out ValueTask task)
    {
        task=ValueTask.CompletedTask;
        if(!action.ActionId.StartsWith("streamMute.",StringComparison.Ordinal)&&!action.ActionId.StartsWith("streamVolume.",StringComparison.Ordinal))return false;
        var stream=WatchedStream(data,selected,session);
        if(stream?.Audio is not {Available:true,Volume:not null} audio || data is null)return true;
        if(action.ActionId==StreamAudioAction("streamMute",data,stream,selected,session))
            task=SetStreamAudioAsync("streamMute",!audio.Muted,data,stream,selected,session,token);
        else if(action.ActionId==StreamAudioAction("streamVolume",data,stream,selected,session) &&
            action.RequestedValue is { } volume && double.IsFinite(volume) && volume is >=0 and <=100)
            task=SetStreamAudioAsync("streamVolume",volume,data,stream,selected,session,token);
        return true;
    }

    // Await the provider write in the SDK's serialized input lane. Its existing
    // slider coalescing retains the newest pending value; no widget queue/retry
    // or cancellation-based overlapping external writes are needed here.
    private async ValueTask SetStreamAudioAsync(string method,object value,DiscordState data,DiscordStream stream,
        ViewState selected,WebSessionReference? session,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bool Current()=>client?.Reference==session && view.Value.Receiver?.Lease==selected.Receiver?.Lease &&
            WatchedStream(CurrentState(),view.Value,client?.Reference)?.Key==stream.Key;
        if(!Current())return;
        view.Update(current=>current with{Notice=""});
        try
        {
            var response=await client!.InvokeAsync(method,new{account=data.Account,channelId=data.ChannelId,key=stream.Key,
                userId=stream.UserId,lease=selected.Receiver!.Lease,value},token);
            var audio=response.GetProperty("audio").Deserialize<DiscordStreamAudio>(DiscordWebSessionClient.Json);
            if(audio is not {Available:true,Volume:{ } volume} || !double.IsFinite(volume) || volume is <0 or >100)
                throw new InvalidDataException("Discord did not confirm stream audio.");
            if(!Current())return;
            // A small, confirmed provider observation must not wait behind a
            // broad catalog/status read. Merge under the resource's publication
            // lock so unrelated newer fields survive and older polls are fenced.
            state.TryPublishUpdate(latest=>
            {
                if(!latest.Ready || latest.Account!=data.Account || latest.ChannelId!=data.ChannelId ||
                    !latest.Streams.Any(item=>item.Key==stream.Key&&item.UserId==stream.UserId))return(false,latest);
                return(true,latest with{Streams=latest.Streams.Select(item=>item.Key==stream.Key&&item.UserId==stream.UserId?item with{Audio=audio}:item).ToArray()});
            });
        }
        catch(Exception) when(!token.IsCancellationRequested)
        {
            if(Current())view.Update(current=>current with{Notice="Discord could not confirm the audio change. Try again."});
            if(LifecycleState!=WidgetLifecycleState.Destroying)state.Refresh();
        }
    }
}
