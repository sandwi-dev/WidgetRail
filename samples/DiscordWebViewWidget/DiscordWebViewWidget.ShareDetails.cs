using System.Diagnostics;
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

    private static string ShareSampleAction(DiscordState data,ViewState selected,WebSessionReference? session)=>
        DomainAction("stream-sample",session?.Id,data.Account,data.ChannelId,selected.Outgoing?.Owner.Attempt.ToString(CultureInfo.InvariantCulture));

    private void SampleShareCadence(DiscordState data,ViewState selected,WebSessionReference? session)
    {
        if(selected.Outgoing is not { } capture||client is not { } provider||session is null||!OwnsCall(capture.Owner,data))return;
        Operations.RunSingleFlight("discord.share.sample",async context=>
        {
            bool Current()=>context.IsCurrent&&provider.Reference==session&&view.Value.Outgoing?.Ticket==capture.Ticket&&OwnsCall(capture.Owner,CurrentState());
            if(!Current())return;
            view.Update(value=>value with{Notice="Measuring the stream for 12 seconds. Return to the game and move the camera."});
            try
            {
                ShareCadenceSample? first=null,previous=null;
                double? lowestSentFps=null;
                var start=Stopwatch.GetTimestamp();
                for(var i=0;i<=12&&Current();i++)
                {
                    var delay=TimeSpan.FromSeconds(i)-Stopwatch.GetElapsedTime(start);
                    if(delay>TimeSpan.Zero)await Task.Delay(delay,context.CancellationToken);
                    if(!Current())return;
                    var result=await provider.InvokeAsync("streamDiagnostics",new{account=data.Account,channelId=data.ChannelId},context.CancellationToken);
                    if(!Current())return;
                    var sample=ReadShareCadence(result,Stopwatch.GetElapsedTime(start).TotalSeconds);
                    first??=sample;
                    if(previous is not null&&ShareRate(previous,sample,value=>value.Sent) is { } fps)
                        lowestSentFps=lowestSentFps is { } lowest?Math.Min(lowest,fps):fps;
                    previous=sample;
                    DiscordReadDiagnostics.Log.RecordStreamSample(sample);
                }
                if(Current()&&first is not null&&previous is not null)
                    view.Update(value=>value with{Notice=FormatShareCadence(first,previous,lowestSentFps)});
            }
            catch(Exception) when(!context.CancellationToken.IsCancellationRequested)
            {if(Current())view.Update(value=>value with{Notice="Stream measurements are unavailable for this share."});}
        },WidgetOperationLifetime.Widget);
    }

    internal sealed record ShareCadenceSample(double Seconds,long ObservedAtUnixMilliseconds,double? Captured,double? Source,double? Encoded,double? Sent,
        double? Keyframes,double? PictureLossRequests,double? FullIntraRequests,double? NegativeAcknowledgements,
        double? BytesSent,double? PacketsSent,double? RetransmittedBytes,double? EncodeSeconds,double? PacketSendDelaySeconds,
        double? Width,double? Height,double? TargetBitrate,double? AvailableOutgoingBitrate,
        bool? PowerEfficientEncoder,string? Limitation,double? ExternalAllocation=null,double? ConfiguredBitrate=null,
        double? EncodedBitrate=null,double? SenderCeiling=null);

    private static ShareCadenceSample ReadShareCadence(JsonElement result,double seconds)
    {
        static JsonElement Field(JsonElement value,string name)=>value.ValueKind==JsonValueKind.Object&&
            value.TryGetProperty(name,out var field)?field:default;
        static double? Number(JsonElement value,string name)=>Field(value,name) is {ValueKind:JsonValueKind.Number} field&&
            field.TryGetDouble(out var number)&&double.IsFinite(number)&&number>=0&&number<=9_007_199_254_740_991?number:null;
        var outbound=Field(result,"outbound");
        var video=outbound.ValueKind==JsonValueKind.Array&&outbound.GetArrayLength()<=4
            ?outbound.EnumerateArray().OrderByDescending(value=>(Number(value,"width")??0)*(Number(value,"height")??0)).FirstOrDefault():default;
        var cadence=Field(video,"cadence");
        var attachment=Field(result,"attachment");
        var external=Field(result,"externalEncoder");
        var efficient=Field(cadence,"powerEfficientEncoder");
        var reason=Field(video,"qualityLimitationReason");
        var limitation=reason.ValueKind==JsonValueKind.String?reason.GetString():null;
        return new(seconds,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Number(attachment,"nativeFrames"),Number(cadence,"sourceFrames"),Number(cadence,"framesEncoded"),
            Number(cadence,"framesSent"),Number(cadence,"keyFramesEncoded"),Number(cadence,"pliCount"),Number(cadence,"firCount"),
            Number(cadence,"nackCount"),Number(cadence,"bytesSent"),Number(cadence,"packetsSent"),Number(cadence,"retransmittedBytesSent"),
            Number(cadence,"totalEncodeTime"),Number(cadence,"totalPacketSendDelay"),Number(video,"width"),Number(video,"height"),
            Number(video,"targetBitrate"),Number(video,"availableOutgoingBitrate"),
            efficient.ValueKind is JsonValueKind.True or JsonValueKind.False?efficient.GetBoolean():null,
            limitation is "none" or "cpu" or "bandwidth" or "other"?limitation:null,
            Number(external,"allocated"),Number(external,"configured"),Number(external,"encodedBitrate"),Number(external,"ceiling"));
    }

    private static double? ShareDelta(ShareCadenceSample first,ShareCadenceSample last,Func<ShareCadenceSample,double?> read)=>
        read(last)-read(first) is >=0 and var change?change:null;

    private static double? ShareRate(ShareCadenceSample first,ShareCadenceSample last,Func<ShareCadenceSample,double?> read)=>
        last.Seconds>first.Seconds?ShareDelta(first,last,read)/(last.Seconds-first.Seconds):null;

    private static string FormatShareCadence(ShareCadenceSample first,ShareCadenceSample last,double? lowestSentFps)
    {
        static string Count(double? number)=>number?.ToString("0",CultureInfo.CurrentCulture)??"—";
        static string Rate(double? number)=>number?.ToString("0.#",CultureInfo.CurrentCulture)??"—";
        var transmitted=ShareRate(first,last,value=>value.BytesSent)*8;
        var bitrate=transmitted is { } bits?$"\nSent {bits/1_000_000:0.##} Mb/s average":"";
        if(last.ExternalAllocation is { } allocation && last.ConfiguredBitrate is { } target)
            bitrate+=$"\nLatest allocation {allocation/1_000_000:0.##} · configured {target/1_000_000:0.##}"+
                (last.EncodedBitrate is { } encoded?$" · encoded {encoded/1_000_000:0.##}":"")+" Mb/s";
        return $"Stream sample · {last.Seconds-first.Seconds:0.#} s\n"+
            $"Submitted {Rate(ShareRate(first,last,value=>value.Captured))} FPS · "+
            $"encoded {Rate(ShareRate(first,last,value=>value.Encoded))} FPS · "+
            $"sent {Rate(ShareRate(first,last,value=>value.Sent))} FPS (low {Rate(lowestSentFps)})\n"+
            $"{Count(ShareDelta(first,last,value=>value.Keyframes))} keyframes · "+
            $"{Count(ShareDelta(first,last,value=>value.PictureLossRequests))} PLI · "+
            $"{Count(ShareDelta(first,last,value=>value.FullIntraRequests))} FIR"+bitrate;
    }

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
        var lines=new List<string>{$"Capture: {Size(source)}{Rate(encoded,"sourceFps")}",$"Sent: {Size(encoded)}{Rate(encoded,"framesPerSecond")}",
            $"Advertised: {Size(announced)}{Rate(announced,"frameRate")}"};
        if(Number(value,"viewerPixels",268435456) is >0 and var viewerPixels)
            lines.Add($"Viewer requested {viewerPixels:N0} pixels.");
        if(value.TryGetProperty("presetBitrateActive",out var presetActive)&&presetActive.ValueKind==JsonValueKind.True)
            lines.Add("Preview bitrate protection is keeping the selected preset’s ceiling.");
        var encoderPath=Text(value,"encoderPath");
        if(encoderPath=="webrtc")
        {
            var fallback=Text(value,"encoderFallback") switch
            {
                "external_pending"=>"preparing external encoder",
                "negotiated_format"=>"negotiated video format",
                "provider_scaling"=>"provider requested a smaller video format",
                "runtime"=>"external encoding unavailable",
                "encoder_failed"=>"external encoder recovered to WebRTC",
                _=>null
            };
            lines.Add(fallback is null?"Encoder path: WebRTC":$"Encoder path: WebRTC · {fallback}");
        }
        else if(encoderPath=="starting")lines.Add("Encoder path: starting external encoder.");
        var bitrates=new List<string>();
        foreach(var (field,label) in new[]{("targetBitrate","encoder target"),("availableOutgoingBitrate","estimated available"),
            ("localCodecMax","local codec hint"),("remoteCodecMax","remote codec hint"),("remoteMediaMax","video bandwidth limit")})
            if(Number(encoded,field,100000000) is >0 and var bitrate)
                bitrates.Add($"{label} {bitrate/1000000:0.##} Mb/s");
        if(bitrates.Count>0)lines.Add("Bitrate: "+string.Join(" · ",bitrates));
        if(value.TryGetProperty("externalEncoder",out var external)&&external.ValueKind==JsonValueKind.Object)
        {
            var rates=new List<string>();
            foreach(var (field,label) in new[]{("allocated","allocation"),("configured","configured"),("encodedBitrate","encoded"),("ceiling","sender maximum")})
                if(Number(external,field,9_007_199_254_740_991) is { } bits)
                    rates.Add($"{label} {bits/1_000_000:0.##} Mb/s");
            var path=Text(external,"encoderPath") switch {"media-foundation"=>"Windows hardware H.264","webcodecs"=>"WebCodecs H.264",_=>"External encoder"};
            if(Text(external,"nativeFallback") is "host" or "timeout" or "profile" or "backpressure" or "missing_allocation" or "bitrate")
                path+=" (fallback)";
            lines.Add(rates.Count>0?path+" · "+string.Join(" · ",rates):path);
        }
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
