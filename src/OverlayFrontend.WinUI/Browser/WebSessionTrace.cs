using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

// Explicit diagnostic mode. Never records message payloads, media URLs, tokens or titles.
// The UI thread only enqueues bounded metadata; all file I/O is on the writer task.
internal static class WebSessionTrace
{
    internal static readonly bool Enabled = Environment.GetCommandLineArgs().Contains("--trace-web-sessions");
    private static readonly Channel<string> Lines = Channel.CreateBounded<string>(new BoundedChannelOptions(1024) { SingleReader=true, FullMode=BoundedChannelFullMode.Wait });
    static WebSessionTrace() { if (Enabled) _ = Task.Run(WriteAsync); }
    internal static long Dropped;
    internal static void Record(string widget, string stage, string fields)
    {
        if (!Enabled) return;
        var line = $"{DateTimeOffset.UtcNow:O} widget={widget} stage={stage} {fields}";
        if (!Lines.Writer.TryWrite(line)) Interlocked.Increment(ref Dropped);
    }
    private static async Task WriteAsync()
    {
        try
        {
            var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WidgetRail","WinUI","diagnostics");
            Directory.CreateDirectory(directory);
            await using var writer = new StreamWriter(new FileStream(Path.Combine(directory,$"web-sessions-{Environment.ProcessId}.log"),FileMode.Create,FileAccess.Write,FileShare.Read,4096,true)) { AutoFlush=true };
            long bytes=0;
            await foreach(var line in Lines.Reader.ReadAllAsync())
            {
                if((bytes+=line.Length)>4*1024*1024){Interlocked.Increment(ref Dropped);continue;}
                await writer.WriteLineAsync(line);
            }
        }
        catch { /* Diagnostics must never interrupt playback. */ }
    }
}

internal sealed partial class BrowserSurface
{
    private readonly Dictionary<string,(long Started,long Posted,bool SdkClock)> tracedRequests = new();
    private readonly List<CoreWebView2DevToolsProtocolEventReceiver> hostedTraceReceivers = [];
    private string TraceHostedRequest(string json)
    {
        if (!WebSessionTrace.Enabled) return json;
        try
        {
            using var document=JsonDocument.Parse(json);
            var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("kind",out var kind)||kind.ValueKind!=JsonValueKind.String||kind.GetString()!="request"||!root.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.String||id.GetString() is not {Length:32} key)return json;
            var now=Stopwatch.GetTimestamp(); var started=now;var sdkClock=false;
            if(root.TryGetProperty("clockFrequency",out var frequency)&&frequency.TryGetInt64(out var freq)&&freq==Stopwatch.Frequency &&
               root.TryGetProperty("sentAtTicks",out var sent)&&sent.TryGetInt64(out var stamp)&&stamp<=now&&now-stamp<60*Stopwatch.Frequency) {started=stamp;sdkClock=true;}
            if(tracedRequests.Count>=64)tracedRequests.Remove(tracedRequests.MinBy(pair=>pair.Value.Posted).Key);
            tracedRequests[key]=(started,now,sdkClock);
            var method = root.TryGetProperty("method",out var m) && m.ValueKind==JsonValueKind.String && WidgetRail.WidgetProtocol.WebSessionContract.IsKey(m.GetString()!) ? m.GetString() : "unknown";
            WebSessionTrace.Record(widgetId,"dispatch",$"id={key} method={method} sdkClock={sdkClock} fromWidgetMs={(sdkClock?Ms(started,now):"unavailable")} audio={core!.IsDocumentPlayingAudio} muted={core.IsMuted} memory={core.MemoryUsageTargetLevel}");
            using var stream=new MemoryStream();using(var writer=new Utf8JsonWriter(stream))
            { writer.WriteStartObject();foreach(var property in root.EnumerateObject())if(property.Name!="trace")property.WriteTo(writer);writer.WriteBoolean("trace",true);writer.WriteEndObject(); }
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException){return json;}
    }
    private bool TraceHostedReply(string json)
    {
        if(!WebSessionTrace.Enabled)return false;
        try
        {
            using var document=JsonDocument.Parse(json);var root=document.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("kind",out var kind)||kind.ValueKind!=JsonValueKind.String||!root.TryGetProperty("id",out var id)||id.ValueKind!=JsonValueKind.String||id.GetString() is not {} key)return false;
            var name=kind.GetString();if(name is not ("received" or "response"))return false;
            if(tracedRequests.TryGetValue(key,out var timing))
            {
                var now=Stopwatch.GetTimestamp();
                var outcome="";
                if(root.TryGetProperty("payload",out var envelope)&&envelope.ValueKind==JsonValueKind.Object&&
                   envelope.TryGetProperty("type",out var type)&&type.ValueKind==JsonValueKind.String&&
                   type.GetString() is "command_completed" or "command_failed" or "current_state" or "volume")
                    outcome=" result="+type.GetString();
                WebSessionTrace.Record(widgetId,name,$"id={key} fromWidgetMs={(timing.SdkClock?Ms(timing.Started,now):"unavailable")} fromPostMs={Ms(timing.Posted,now)} error={root.TryGetProperty("error",out _)} audio={core!.IsDocumentPlayingAudio} muted={core.IsMuted}{outcome}");
                if(name=="response")tracedRequests.Remove(key);
            }
            return name=="received";
        }
        catch(Exception error)when(error is JsonException or InvalidOperationException or System.Runtime.InteropServices.COMException){return false;}
    }
    private static string Ms(long start,long end)=>((end-start)*1000d/Stopwatch.Frequency).ToString("F3",CultureInfo.InvariantCulture);

    private async Task ConfigureHostedTraceAsync()
    {
        if(!WebSessionTrace.Enabled||!IsHostedPackage||core is null)return;
        // Observe receipt even for already installed packages with the older runtime.
        // Document-created scripts do not simulate a user gesture or execute a command.
        await core.AddScriptToExecuteOnDocumentCreatedAsync("""
            chrome.webview.addEventListener('message', ({data:r}) => {
              if(r?.widgetRail===1 && r.kind==='request' && r.trace===true && /^[a-f0-9]{32}$/.test(r.id))
                chrome.webview.postMessage({widgetRail:1,kind:'received',id:r.id});
            });
            """);
        core.WebResourceResponseReceived+=(_,args)=>
        {
            if(args.Response.StatusCode>=400)WebSessionTrace.Record(widgetId,"http-error",$"status={args.Response.StatusCode}");
        };
        core.IsDocumentPlayingAudioChanged+=(_,_)=>{if(core is {} page)WebSessionTrace.Record(widgetId,"audio",$"playing={page.IsDocumentPlayingAudio} muted={page.IsMuted}");};
        var events=core.GetDevToolsProtocolEventReceiver("Media.playerEventsAdded"); hostedTraceReceivers.Add(events);
        events.DevToolsProtocolEventReceived+=(_,args)=>
        {
            try
            {
                using var json=JsonDocument.Parse(args.ParameterObjectAsJson);
                if(!json.RootElement.TryGetProperty("events",out var entries))return;
                foreach(var entry in entries.EnumerateArray())
                {
                    using var value=JsonDocument.Parse(entry.GetProperty("value").GetString()!);
                    var root=value.RootElement;
                    var label=root.TryGetProperty("event",out var e)?e.GetString():null;
                    if(label is "kPlay" or "kPause" or "kEnded" or "kSuspended" or "kWebMediaPlayerDestroyed" or "kBufferingStateChanged")
                        WebSessionTrace.Record(widgetId,"media",$"event={label}");
                    foreach(var property in root.EnumerateObject())
                        if(property.Name is "audio_buffering_state" or "video_buffering_state" or "pipeline_state")
                        {
                            var state=property.Value.ValueKind==JsonValueKind.Object&&property.Value.TryGetProperty("state",out var st)?st.GetString():property.Value.ValueKind==JsonValueKind.String?property.Value.GetString():null;
                            if(state is "BUFFERING_HAVE_ENOUGH" or "BUFFERING_HAVE_NOTHING" or "kPlaying" or "kSuspended" or "kStopped")WebSessionTrace.Record(widgetId,"media-state",$"kind={property.Name} value={state}");
                        }
                }
            }
            catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException){}
        };
        var errors=core.GetDevToolsProtocolEventReceiver("Media.playerErrorsRaised"); hostedTraceReceivers.Add(errors);
        errors.DevToolsProtocolEventReceived+=(_,args)=>
        {
            try
            {
                using var json=JsonDocument.Parse(args.ParameterObjectAsJson);
                if(json.RootElement.TryGetProperty("errors",out var values))foreach(var error in values.EnumerateArray())
                    if(error.TryGetProperty("code",out var code)&&code.ValueKind==JsonValueKind.Number&&code.TryGetInt32(out var number))WebSessionTrace.Record(widgetId,"media-error",$"code={number}");
            }
            catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException){}
        };
        try { await core.CallDevToolsProtocolMethodAsync("Media.enable","{}"); }
        catch (System.Runtime.InteropServices.COMException) { WebSessionTrace.Record(widgetId,"media-trace-unavailable",""); }
    }
}
