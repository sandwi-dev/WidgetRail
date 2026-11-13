using System.Globalization;
using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.DiscordWebView;

public sealed partial class DiscordWebViewWidget
{
    internal Task WaitForShareDetailsAsync(CancellationToken token=default)=>Operations.WhenIdleAsync("discord.share.details",token);
    private static string ShareDetailsAction(DiscordState data,ViewState selected,WebSessionReference? session)=>
        DomainAction("stream-info",session?.Id,data.Account,data.ChannelId,selected.Outgoing?.Owner.Attempt.ToString(CultureInfo.InvariantCulture));

    private void ReadShareDetails(DiscordState data,ViewState selected,WebSessionReference? session)
    {
        if(selected.Outgoing is not { } capture||client is null||session is null||!OwnsCall(capture.Owner,data))return;
        Operations.RunLatest("discord.share.details",async context=>
        {
            bool Current()=>context.IsCurrent&&client.Reference==session&&view.Value.Outgoing?.Ticket==capture.Ticket&&OwnsCall(capture.Owner);
            if(!Current())return;
            view.Update(value=>value with{Notice="Reading stream details…"});
            try
            {
                var result=await client.InvokeAsync("streamDiagnostics",new{account=data.Account,channelId=data.ChannelId},context.CancellationToken);
                if(Current())view.Update(value=>value with{Notice=FormatShareDetails(result)});
            }
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested)
            {if(Current())view.Update(value=>value with{Notice="Stream details are unavailable in this Discord version."});}
        },WidgetOperationLifetime.Active);
    }

    internal static string FormatShareDetails(JsonElement value)
    {
        if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty("source",out var source)||source.ValueKind!=JsonValueKind.Object||
            !value.TryGetProperty("outbound",out var outbound)||outbound.ValueKind!=JsonValueKind.Array||outbound.GetArrayLength()>8||
            !value.TryGetProperty("advertised",out var advertised)||advertised.ValueKind!=JsonValueKind.Array||advertised.GetArrayLength()>8)
            throw new InvalidDataException("Invalid stream details.");
        static double? Number(JsonElement item,string name,double maximum)
        {
            if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty(name,out var property)||property.ValueKind==JsonValueKind.Null)return null;
            if(property.ValueKind!=JsonValueKind.Number||!property.TryGetDouble(out var number)||!double.IsFinite(number)||number<0||number>maximum)
                throw new InvalidDataException("Invalid stream detail number.");
            return number;
        }
        static string Size(JsonElement item)=>Number(item,"width",16384) is >0 and var width&&Number(item,"height",16384) is >0 and var height
            ?$"{width:0} × {height:0}":"not available";
        static double Area(JsonElement item)=>(Number(item,"width",16384)??0)*(Number(item,"height",16384)??0);
        static string Rate(JsonElement item,string name)=>Number(item,name,240) is { } rate?$" · {rate:0.#} FPS":"";
        var encoded=outbound.EnumerateArray().OrderByDescending(Area).FirstOrDefault();
        var announced=advertised.EnumerateArray().OrderByDescending(Area).FirstOrDefault();
        var lines=new List<string>{$"Capture: {Size(source)}",$"Sent: {Size(encoded)}{Rate(encoded,"framesPerSecond")}",
            $"Advertised: {Size(announced)}{Rate(announced,"frameRate")}"};
        static string? Text(JsonElement item,string name)=>item.ValueKind==JsonValueKind.Object&&item.TryGetProperty(name,out var property)&&property.ValueKind==JsonValueKind.String
            ?property.GetString():null;
        var codec=Text(encoded,"codec");
        if(codec is not("VP8" or "VP9" or "H264" or "AV1" or "H265"))codec=null;
        var limitation=Text(encoded,"qualityLimitationReason") switch
        {"none"=>"no reported limit","cpu"=>"CPU limited","bandwidth"=>"bandwidth limited","other"=>"other limit",_=>null};
        if(codec is not null||limitation is not null)lines.Add("Encoder: "+string.Join(" · ",new[]{codec,limitation}.Where(part=>part is not null)));
        if(value.TryGetProperty("sender",out var sender)&&sender.ValueKind==JsonValueKind.Object)
        {
            var preference=Text(sender,"contentHint") switch{"detail"=>"Clarity","motion"=>"Smoothness","text"=>"Text",_=>null};
            var degradation=Text(sender,"degradationPreference") switch
            {"maintain-framerate"=>"preserve frame rate","maintain-resolution"=>"preserve resolution","balanced"=>"balanced adaptation",_=>null};
            var policy=new List<string>();
            if(preference is not null)policy.Add(preference);
            if(degradation is not null)policy.Add(degradation);
            if(sender.TryGetProperty("encodings",out var encodings)&&encodings.ValueKind==JsonValueKind.Array&&encodings.GetArrayLength()==1)
            {
                var encoding=encodings[0];
                if(Number(encoding,"maxBitrate",100000000) is >0 and var bitrate)policy.Add($"{bitrate/1000000:0.#} Mb/s max");
                if(Number(encoding,"scaleResolutionDownBy",64) is >=1 and var scale)policy.Add($"{scale:0.#}× scale");
            }
            if(policy.Count>0)lines.Add("Policy: "+string.Join(" · ",policy));
        }
        if(value.TryGetProperty("attachment",out var attachment)&&attachment.ValueKind==JsonValueKind.Object)
        {
            var path=new List<string>();
            if(Number(attachment,"nativeFrames",1000000000000) is {} frames)path.Add($"{frames:0} captured frames");
            if(attachment.TryGetProperty("videoSupported",out var supported)&&supported.ValueKind is JsonValueKind.True or JsonValueKind.False)
                path.Add(supported.GetBoolean()?"video supported":"video disabled");
            foreach(var (field,label) in new[]{("inputTrack","input"),("desktopTrack","desktop"),("senderTrack","sender")})
                path.Add(label+" "+(Text(attachment,field) is "live" or "ended"?Text(attachment,field):"unavailable"));
            var peer=Text(attachment,"peerState");
            if(peer is "new" or "connecting" or "connected" or "disconnected" or "failed" or "closed")path.Add("peer "+peer);
            if(attachment.TryGetProperty("qualityBinding",out var binding)&&binding.ValueKind==JsonValueKind.Object&&
                binding.TryGetProperty("applied",out var applied)&&applied.ValueKind is JsonValueKind.True or JsonValueKind.False)
                path.Add(applied.GetBoolean()?"quality applied":"quality pending");
            else path.Add("quality hook unavailable");
            lines.Add("Video path: "+string.Join(" · ",path));
            var qualityCheck=Text(attachment,"qualityCheck");
            if(qualityCheck is "attachment-timeout" or "provider-exception" or "source-owner" or "stream-owner" or "rtc-identity" or
                "rtc-owner" or "rtc-connection" or "rtc-context" or "connection-live" or "selected-source" or "resolution-mode" or
                "quality-manager" or "primary-encoding" or "quality-eligibility" or "quality-preference" or "quality-apply" or "quality-readback" or "quality-advertisement")
                lines.Insert(0,"Quality check: "+qualityCheck);
        }
        return string.Join('\n',lines);
    }
}
