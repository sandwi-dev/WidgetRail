using System.Text.Json;
using System.Text.Json.Nodes;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.RemoteMediaTransport;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal async Task<JsonObject> ReadDiscordPosterProbeAsync(string? currentWidget, CancellationToken token)
    {
        if (retired || currentWidget is null) return BrowserSurface.PosterProbeResult("waiting");
        var candidates = entries.Where(pair => pair.Key.Widget == currentWidget && pair.Value.Hosted is not null &&
            pair.Value.Surface.IsBackendSession && Uri.TryCreate(pair.Value.Surface.CurrentUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme == "https" && uri.Host == "discord.com").Take(2).ToArray();
        if (candidates.Length == 0) return BrowserSurface.PosterProbeResult("waiting");
        if (candidates.Length != 1) return BrowserSurface.PosterProbeResult("backend_ambiguous");
        var pair = candidates[0];
        // A hosted backend remains valid while native controls are displayed;
        // it need not be a WebBrowser node in the current presentation tree.
        return await pair.Value.Surface.ReadDiscordPosterProbeAsync(token);
    }
}

internal sealed partial class BrowserSurface
{
    private JsonObject? discordPosterProbeResult;

    internal static JsonObject PosterProbeResult(string category, string? format = null, int bytes = 0, int width = 0, int height = 0) =>
        new() { ["category"] = category, ["format"] = format, ["bytes"] = bytes, ["width"] = width, ["height"] = height };

    internal async Task<JsonObject> ReadDiscordPosterProbeAsync(CancellationToken token)
    {
        if (discordPosterProbeResult is not null) return (JsonObject)discordPosterProbeResult.DeepClone();
        if (retired || core is null || !IsBackendSession || !Uri.TryCreate(core.Source, UriKind.Absolute, out var page) ||
            page.Scheme != "https" || page.Host != "discord.com") return PosterProbeResult("waiting");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            var cancellation = deadline.Token;
            var arguments = JsonSerializer.Serialize(new { expression = DiscordPosterProbeScript, returnByValue = true });
            using var response = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", arguments)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(3), cancellation));
            if (!response.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("value", out var value) ||
                value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("category", out var state) || state.ValueKind != JsonValueKind.String)
                return Complete("probe_unavailable");
            var category = state.GetString();
            if (category == "waiting") return PosterProbeResult("waiting");
            if (category != "candidate") return Complete(category is "no_candidate" or "history_failed" or "expired" or "retired" ? category : "probe_unavailable");
            // The private CDP return is consumed in memory only. Neither this URL,
            // its signing query nor response/exception text enters probe output.
            if (!value.TryGetProperty("posterUrl", out var candidate) || candidate.ValueKind != JsonValueKind.String ||
                !TryValidateDiscordPosterUri(candidate.GetString(), out var uri)) return Complete("destination_rejected");
            if (!await DiscordPosterProbeCurrentAsync(cancellation)) return Complete("retired");
            var content = await NativePublicMediaTransport.Client.DownloadAsync(uri!, PublicMediaPolicy.SameOrigin(uri!),
                2 * 1024 * 1024, cancellation);
            var format = PublicMediaFormat.Identify(content.Bytes.Span) ?? "unknown";
            var bytes = content.Bytes.Length;
            if (format != "image/jpeg") return Complete("response_format_rejected", format, bytes);
            if (!await DiscordPosterProbeCurrentAsync(cancellation)) return Complete("retired", format, bytes);
            var decoded = await NativeArtworkDecoder.DecodeAsync(new(content.Bytes, IsPublicMedia: true), new(512, 512),
                ImageFit.Contain, cancellation, autoPlay: false);
            if (decoded is null || decoded.Image.PixelWidth <= 0 || decoded.Image.PixelHeight <= 0)
                return Complete("decode_failed", format, bytes);
            if (!await DiscordPosterProbeCurrentAsync(cancellation)) return Complete("retired", format, bytes);
            var width = decoded.Source.Width; var height = decoded.Source.Height;
            return Complete(width is > 0 and <= 512 && height is > 0 and <= 512 ? "success" : "dimensions_exceeded", format, bytes, width, height);
        }
        catch (PublicMediaException error)
        {
            return Complete(error.Failure switch
            {
                PublicMediaFailure.DestinationRejected => "destination_rejected",
                PublicMediaFailure.Timeout => "timeout",
                PublicMediaFailure.TooLarge => "too_large",
                PublicMediaFailure.Busy => "busy",
                PublicMediaFailure.ResponseRejected => "response_rejected",
                _ => "network_failed"
            });
        }
        catch (OperationCanceledException) { return Complete(token.IsCancellationRequested || lifetime.IsCancellationRequested ? "cancelled" : "timeout"); }
        catch (TimeoutException) { return Complete("timeout"); }
        catch (Exception) { return Complete("probe_failed"); }

        JsonObject Complete(string category, string? format = null, int bytes = 0, int width = 0, int height = 0)
        {
            discordPosterProbeResult = PosterProbeResult(category, format, bytes, width, height);
            return (JsonObject)discordPosterProbeResult.DeepClone();
        }
    }

    private async Task<bool> DiscordPosterProbeCurrentAsync(CancellationToken token)
    {
        if (retired || core is null) return false;
        const string expression = "globalThis.__widgetRailDiscordPosterProbe?.current?.()===true";
        using var response = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression, returnByValue = true })).AsTask().WaitAsync(TimeSpan.FromSeconds(3), token));
        return !retired && response.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.True;
    }

    private static bool TryValidateDiscordPosterUri(string? value, out Uri? uri)
    {
        uri = null;
        if (value is not { Length: > 0 and <= 4096 } || value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character) || character == '\\') ||
            !value.StartsWith("https://media.discordapp.net/attachments/", StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || parsed.IdnHost != "media.discordapp.net" ||
            !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Fragment.Length != 0) return false;
        var path = parsed.AbsolutePath.Split('/');
        if (path.Length != 5 || path[1] != "attachments" || path[2].Length is < 1 or > 32 || path[3].Length is < 1 or > 32 ||
            !path[2].All(char.IsAsciiDigit) || !path[3].All(char.IsAsciiDigit) || path[4].Length == 0) return false;
        var found = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var field in parsed.Query.TrimStart('?').Split('&'))
            {
                var parts = field.Split('=', 2);
                var name = Uri.UnescapeDataString(parts[0]);
                var lower = name.ToLowerInvariant();
                if (lower is "size" or "quality" or "animated" or "fit") return false;
                if (lower is not ("format" or "width" or "height")) continue;
                if (name != lower) return false;
                if (!found.Add(name) || parts.Length != 2 || Uri.UnescapeDataString(parts[1]) != (name == "format" ? "jpeg" : "512")) return false;
            }
        }
        catch (UriFormatException) { return false; }
        if (found.Count != 3) return false;
        uri = parsed; return true;
    }

    // One opt-in observer of an already-requested native history call. Its raw
    // source stays inside this closure until one native read consumes it. No
    // authenticated request, message write, media fetch or page scrape is made.
    private const string DiscordPosterProbeScript = """
        (()=>{
          const key='__widgetRailDiscordPosterProbe';
          if(globalThis[key])return globalThis[key].take();
          const adapter=globalThis.WidgetRailDiscordPrototype,wp=globalThis.WidgetRailWebWebpack;
          const compatible=api=>api&&typeof api==='object'&&!Array.isArray(api)&&
            ['get','post','put','patch','del'].every(name=>typeof api[name]==='function')&&
            !['Request','Response','agent'].some(name=>typeof api[name]==='function');
          const api=wp?.findByProps?.('get','post',compatible);
          if(typeof adapter?.invoke!=='function'||!compatible(api))return {category:'waiting'};
          const validId=value=>typeof value==='string'&&/^[1-9][0-9]{0,31}$/.test(value);
          const me=()=>wp?.findStore?.('UserStore')?.getCurrentUser?.()?.id;
          const originalInvoke=adapter.invoke,originalGet=api.get;
          let state='waiting',active=null,chosen=null,scope=null,attempted=false;
          const current=()=>scope!==null&&globalThis.WidgetRailDiscordPrototype===adapter&&me()===scope.account;
          const detach=()=>{if(api.get===get)api.get=originalGet;if(adapter.invoke===invoke)adapter.invoke=originalInvoke;};
          const stop=category=>{state=category;chosen=null;scope=null;detach();};
          const poster=value=>{
            if(typeof value!=='string'||value.length>2048||/[\s\\\u0000-\u001f\u007f-\u009f]/.test(value)||
              !value.startsWith('https://media.discordapp.net/attachments/'))return null;
            try{
              const uri=new URL(value);
              if(uri.origin!=='https://media.discordapp.net'||uri.username||uri.password||uri.port||uri.hash||
                !/^\/attachments\/[1-9][0-9]{0,31}\/[1-9][0-9]{0,31}\/[^/]+$/.test(uri.pathname))return null;
              const at=value.indexOf('?'),base=at<0?value:value.slice(0,at);
              const fields=at<0?[]:value.slice(at+1).split('&').filter(field=>{
                const name=decodeURIComponent(field.split('=',1)[0]).toLowerCase();
                return !['format','width','height','size','quality','animated','fit'].includes(name);
              });
              fields.push('format=jpeg','width=512','height=512');
              const result=base+'?'+fields.join('&');return result.length<=4096?result:null;
            }catch{return null;}
          };
          const select=(rows,request)=>{
            if(!Array.isArray(rows)||rows.length>50)return null;
            for(const row of rows){
              if(!validId(row?.id)||row.channel_id!==request.channel||!Array.isArray(row.attachments)||row.attachments.length>10)continue;
              for(const file of row.attachments){
                if(!validId(file?.id)||!['video/mp4','video/webm'].includes(file.content_type)||typeof file.filename!=='string'||
                  file.filename.startsWith('SPOILER_')||!Number.isSafeInteger(file.size)||file.size<1||
                  file.flags!=null&&(!Number.isSafeInteger(file.flags)||file.flags<0||(file.flags&8)!==0))continue;
                const candidate=poster(file.proxy_url??file.proxyURL);
                if(candidate&&typeof file.url==='string')return {url:candidate,message:row.id,file:file.id,original:file.url,size:file.size};
              }
            }
            return null;
          };
          function get(...args){
            const request=active,options=args[0];
            const match=request&&options?.url==='/channels/'+request.channel+'/messages'&&
              Number.isSafeInteger(options.query?.limit)&&options.query.limit>0&&options.query.limit<=50;
            const pending=Reflect.apply(originalGet,this,args);
            if(match)Promise.resolve(pending).then(response=>{
              if(current()&&state==='waiting'&&response?.ok!==false&&response?.status>=200&&response.status<300)
                request.candidate=select(response.body,request);
            },()=>{}).catch(()=>{});
            return pending;
          }
          function invoke(...args){
            const call=args[0],parameters=call?.args;
            if(attempted||call?.method!=='messageHistory'||!validId(parameters?.account)||!validId(parameters?.channelId)||me()!==parameters.account)
              return Reflect.apply(originalInvoke,this,args);
            attempted=true;scope={account:parameters.account,channel:parameters.channelId,candidate:null};
            active=scope;
            let pending;
            try{pending=Reflect.apply(originalInvoke,this,args);}catch(error){stop('history_failed');throw error;}finally{active=null;}
            const request=scope;
            Promise.resolve(pending).then(response=>{
              if(state!=='waiting')return;
              if(!current()){stop('retired');return;}
              if(response?.ok!==true||response.value?.account!==request.account||response.value?.channelId!==request.channel){stop('history_failed');return;}
              const raw=request.candidate;
              const message=Array.isArray(response.value.items)?response.value.items.find(item=>item.id===raw?.message):null;
              const file=Array.isArray(message?.attachments)?message.attachments.find(item=>item.id===raw?.file):null;
              let original=null;try{original=raw?new URL(raw.original).href:null;}catch{}
              if(!file||file.isSpoiler===true||typeof file.url!=='string'||original===null||file.url!==original||file.byteLength!==raw.size||!['video/mp4','video/webm'].includes(file.contentType)){
                stop('no_candidate');return;
              }
              chosen=raw.url;state='candidate';request.candidate=null;detach();
            },()=>stop('history_failed')).catch(()=>stop('history_failed'));
            return pending;
          }
          const probe={current,take(){
            if(state==='candidate'){
              if(!current()){stop('retired');return {category:'retired'};}
              const posterUrl=chosen;chosen=null;state='consumed';return {category:'candidate',posterUrl};
            }
            return {category:state==='consumed'?'retired':state};
          }};
          try{
            api.get=get;adapter.invoke=invoke;
            if(api.get!==get||adapter.invoke!==invoke){detach();return {category:'probe_unavailable'};}
            globalThis[key]=probe;
            setTimeout(()=>stop('expired'),900000);
          }catch{detach();return {category:'probe_unavailable'};}
          return {category:'waiting'};
        })()
        """;
}
