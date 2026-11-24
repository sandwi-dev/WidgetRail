using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>One top-level document's package adapter; runtime authority never leaves the host.</summary>
internal sealed partial class WebSessionPageAdapterHost : IDisposable
{
    private readonly CoreWebView2 core;
    private readonly WebSessionPageAdapter definition;
    private readonly string script;
    private readonly Func<bool> canInvoke;
    private readonly Func<bool> canRequest;
    private readonly Action<string> publish;
    private readonly List<(CoreWebView2DevToolsProtocolEventReceiver Receiver, Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2DevToolsProtocolEventReceivedEventArgs> Handler)> receivers = [];
    private readonly Dictionary<string, (long Epoch, string Url, int Limit)> responses = [];
    private readonly string binding = "widgetRailAction" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string,int> documentContexts = new(StringComparer.Ordinal);
    private (string Frame, long Epoch, TaskCompletionSource<int> Ready)? contextWaiter;
    private TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ulong? navigationId;
    private bool installed, documentLoaded;
    internal bool IsReady => installed && documentLoaded && context is not null && Current(epoch);
    internal bool AllowsBackgroundRequests => definition.Mode == WebSessionPageAdapterMode.SessionBackend;
    private string RequestEndpoint => AllowsBackgroundRequests ? binding + "Request" : "__widgetRailRequest";
    private string CancelEndpoint => AllowsBackgroundRequests ? binding + "Cancel" : "__widgetRailCancelRequests";
    private long epoch;
    private int? context;
    private string? loader, frame;
    private bool disposed, reportedFailure;
    private int reading;
    // Events are JSON transport, never HTML or executable script. Preserve UTF-8
    // so a bounded Unicode payload does not expand past its wire budget.
    private static readonly JsonSerializerOptions EventJson=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    internal string LastAction { get; private set; }="none";
    private string buttonStyle="background:Canvas;color:CanvasText;";
    internal void SetStyle(string style)
    {
        if(buttonStyle==style)return;buttonStyle=style;
        if(!AllowsBackgroundRequests && context is not null)_=RefreshStyleAsync(epoch);
    }
    private async Task RefreshStyleAsync(long current)
    {
        try{await Evaluate("globalThis.__widgetRailStyle?.("+JsonSerializer.Serialize(buttonStyle, BrowserJsonContext.Default.String)+")",current);}
        catch(Exception error)when(error is not OutOfMemoryException){/* Navigation retires this style target. */}
    }

    internal WebSessionPageAdapterHost(CoreWebView2 core, WebSessionPageAdapter definition, string script,
        Func<bool> canInvoke, Action<string> publish, Func<bool>? canRequest=null)
    { this.core=core;this.definition=definition;this.script=script;this.canInvoke=canInvoke;this.publish=publish;this.canRequest=canRequest??canInvoke; }

    internal async Task StartAsync()
    {
        core.NavigationStarting += Starting;
        core.ContentLoading += Loading;
        core.NavigationCompleted += Completed;
        core.SourceChanged += LocationChanged;
        if (AllowsBackgroundRequests)
        {
            Listen("Runtime.executionContextCreated", CreatedContext);
            Listen("Runtime.executionContextDestroyed", DestroyedContext);
            Listen("Runtime.executionContextsCleared", _ => { documentContexts.Clear(); RetireContext(); });
            await Call("Runtime.enable", new());
        }
        Listen("Runtime.bindingCalled", Bound);
        if(!AllowsBackgroundRequests)
        {
            Listen("Network.responseReceived", Response);
            Listen("Network.loadingFinished", Finished);
            Listen("Network.loadingFailed", Failed);
            await Call("Network.enable", new() { ["maxTotalBufferSize"]=2*1024*1024, ["maxResourceBufferSize"]=WebSessionResponseRule.MaximumResponseBytes });
        }
    }
    private void CreatedContext(JsonElement value)
    {
        var created = value.GetProperty("context");
        if (!created.TryGetProperty("auxData", out var data) ||
            !data.TryGetProperty("isDefault", out var isDefault) || isDefault.ValueKind != JsonValueKind.True ||
            !data.TryGetProperty("frameId", out var frameId) || frameId.GetString() is not { } id) return;
        if (documentContexts.Count < 128 || documentContexts.ContainsKey(id)) documentContexts[id] = created.GetProperty("id").GetInt32();
        if(contextWaiter is { } waiter && waiter.Frame == id && Current(waiter.Epoch))
            waiter.Ready.TrySetResult(created.GetProperty("id").GetInt32());
    }
    private void DestroyedContext(JsonElement value)
    {
        var destroyed = value.GetProperty("executionContextId").GetInt32();
        foreach (var key in documentContexts.Where(p => p.Value == destroyed).Select(p => p.Key).ToArray()) documentContexts.Remove(key);
        if (context == destroyed) RetireContext();
    }
    private void RetireContext()
    {
        if(context is null)return;
        RetireInvalidations();
        RetireRequests();context=null;installed=false;epoch++;
        ready.TrySetCanceled();ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private void Listen(string name, Action<JsonElement> action)
    {
        var receiver=core.GetDevToolsProtocolEventReceiver(name);
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler=(_,args)=>
        {
            if(disposed)return;
            try {using var json=JsonDocument.Parse(args.ParameterObjectAsJson);action(json.RootElement);}
            catch(Exception error)when(error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException){Report("invalid_page_data");}
        };
        receiver.DevToolsProtocolEventReceived+=handler;receivers.Add((receiver,handler));
    }
    private void Starting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if(args.Cancel)return;
        if(navigationId is not null && navigationId!=args.NavigationId)
        { ready.TrySetCanceled();ready=new(TaskCreationOptions.RunContinuationsAsynchronously); }
        navigationId=args.NavigationId;installed=false;documentLoaded=false;
        RetireInvalidations();
        RetireRequests();
        contextWaiter?.Ready.TrySetCanceled();contextWaiter=null;documentContexts.Clear();
        epoch++;context=null;loader=frame=null;responses.Clear();reportedFailure=false;
    }
    private void Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if(disposed || args.NavigationId!=navigationId)return;
        documentLoaded=args.IsSuccess;
        if(!args.IsSuccess || !definition.AllowsPage(core.Source))ready.TrySetException(new InvalidOperationException("Adapter navigation is unavailable."));
        else if(installed)ready.TrySetResult();
    }
    private void LocationChanged(CoreWebView2 sender,CoreWebView2SourceChangedEventArgs args)
    {
        if(disposed||!definition.AllowsPage(core.Source)||Encoding.UTF8.GetByteCount(core.Source)>48000)return;
        publish(new JsonObject{["kind"]="page-location",["url"]=core.Source}.ToJsonString());
    }
    private async void Loading(CoreWebView2 sender, CoreWebView2ContentLoadingEventArgs args)
    {
        var current=epoch;
        if(!definition.AllowsPage(core.Source))return;
        try
        {
            using var tree=JsonDocument.Parse(await Call("Page.getFrameTree",new()));
            if(!Current(current))return;
            var root=tree.RootElement.GetProperty("frameTree").GetProperty("frame");
            frame=root.GetProperty("id").GetString();loader=root.GetProperty("loaderId").GetString();
            if (AllowsBackgroundRequests)
            {
                // Bind only the default context belonging to the verified top
                // frame. Child frames and previous documents cannot reply.
                if(!documentContexts.TryGetValue(frame!,out var documentContext))
                {
                    var ready=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    contextWaiter=(frame!,current,ready);
                    try{documentContext=await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));}
                    finally{if(contextWaiter?.Ready==ready)contextWaiter=null;}
                }
                if(!Current(current))return;
                context=documentContext;
            }
            else
            {
                using var world=JsonDocument.Parse(await Call("Page.createIsolatedWorld",new(){["frameId"]=frame,["worldName"]=binding}));
                if(!Current(current))return;
                context=world.RootElement.GetProperty("executionContextId").GetInt32();
            }
            await Call("Runtime.addBinding",new(){["name"]=binding,["executionContextId"]=context.Value});
            if(!Current(current))return;
            var source="(()=>{const deliver=globalThis["+JsonSerializer.Serialize(binding, BrowserJsonContext.Default.String)+"];delete globalThis["+JsonSerializer.Serialize(binding, BrowserJsonContext.Default.String)+"];"+
                "const requestEndpoint="+JsonSerializer.Serialize(RequestEndpoint,BrowserJsonContext.Default.String)+",cancelEndpoint="+JsonSerializer.Serialize(CancelEndpoint,BrowserJsonContext.Default.String)+";"+
                "const maximumBatchItems="+(AllowsBackgroundRequests?WebSessionContract.MaximumBackendBatchItems:WebSessionContract.MaximumPageActionBatchItems).ToString(System.Globalization.CultureInfo.InvariantCulture)+";"+
                (AllowsBackgroundRequests ? "const videoEndpoint="+JsonSerializer.Serialize(binding+"Video", BrowserJsonContext.Default.String)+";"+VideoViewPrelude : "")+
                BatchPrelude+"\n"+(AllowsBackgroundRequests?BackendPrelude:Prelude)+"\n"+RequestsPrelude+"\n"+script+"\n})()";
            await Evaluate(source,current);
            if(!Current(current))return;
            installed=true;if(documentLoaded)ready.TrySetResult();
            if(!AllowsBackgroundRequests)await RefreshStyleAsync(current);
        }
        catch(Exception error)when(error is not OutOfMemoryException){if(Current(current))Report("adapter_unavailable");}
    }
    private void Bound(JsonElement value)
    {
        LastAction="received";
        if(context is null||!Current(epoch)||value.GetProperty("name").GetString()!=binding||
            value.GetProperty("executionContextId").GetInt32()!=context){LastAction="not-current-or-interactive";return;}
        var text=value.GetProperty("payload").GetString();
        if(text is null||Encoding.UTF8.GetByteCount(text)>WebSessionContract.MaximumMessageBytes-2048){Report("result_too_large");return;}
        // A bounded action may wrap already-validated response metadata in a
        // batch envelope. Match the ordinary web-session JSON depth budget.
        using var data=JsonDocument.Parse(text,new(){MaxDepth=WebSessionContract.MaximumMessageDepth});
        if(AllowsBackgroundRequests && data.RootElement.ValueKind==JsonValueKind.Object &&
            data.RootElement.EnumerateObject().Count()==1 && data.RootElement.TryGetProperty("observeMainView",out var observe) &&
            observe.ValueKind==JsonValueKind.True)
        {
            if(!observesMainView){observesMainView=true;PublishMainViewVisibility();}
            return;
        }
        if(AllowsBackgroundRequests && data.RootElement.ValueKind==JsonValueKind.Object &&
            data.RootElement.EnumerateObject().Count()==1 && data.RootElement.TryGetProperty("invalidate",out var changed) &&
            changed.ValueKind==JsonValueKind.Number && changed.TryGetInt32(out var invalidationVersion) && invalidationVersion==1)
        {InvalidateBackend();return;}
        if(data.RootElement.TryGetProperty("widgetRail",out _)){ReceiveReply(data.RootElement);return;}
        if(AllowsBackgroundRequests){LastAction="unsolicited-action-denied";return;}
        if(!canInvoke()){LastAction="not-interactive";return;}
        var action=data.RootElement.GetProperty("action").GetString();
        if(!WebSessionContract.IsKey(action)){Report("invalid_page_action");return;}
        var envelope=new JsonObject { ["kind"]="page-action",["action"]=action,["document"]=core.Source,
            ["payload"]=JsonNode.Parse(data.RootElement.GetProperty("payload").GetRawText()) }.ToJsonString(EventJson);
        if(Encoding.UTF8.GetByteCount(envelope)>WebSessionContract.MaximumMessageBytes){Report("result_too_large");return;}
        publish(envelope);
        LastAction="delivered";
    }
    private void Response(JsonElement value)
    {
        if(context is null||!Current(epoch)||value.GetProperty("loaderId").GetString()!=loader||
            !value.TryGetProperty("frameId",out var sourceFrame)||sourceFrame.GetString()!=frame)return;
        var type=value.GetProperty("type").GetString();
        if(type is not ("XHR" or "Fetch"))return;
        var response=value.GetProperty("response");var url=response.GetProperty("url").GetString()!;
        var rule=definition.Responses.FirstOrDefault(rule=>rule.Matches(url));
        if(rule is null||response.GetProperty("status").GetInt32()!=200||
            !response.GetProperty("mimeType").GetString()!.Contains("json",StringComparison.OrdinalIgnoreCase))return;
        if(responses.Count>=8){Report("response_limit");return;}
        responses[value.GetProperty("requestId").GetString()!]=(epoch,url,rule.MaximumBytes);
    }
    private void Failed(JsonElement value)=>responses.Remove(value.GetProperty("requestId").GetString()!);
    private void Finished(JsonElement value)
    {
        var id=value.GetProperty("requestId").GetString()!;
        if(!responses.Remove(id,out var response)||!Current(response.Epoch))return;
        if(reading>=2){Report("response_limit");return;}
        _=ReadResponseAsync(id,response);
    }
    private async Task ReadResponseAsync(string id,(long Epoch,string Url,int Limit) response)
    {
        reading++;
        try
        {
            using var result=JsonDocument.Parse(await Call("Network.getResponseBody",new(){["requestId"]=id}));
            if(!Current(response.Epoch))return;
            var body=result.RootElement.GetProperty("body").GetString()!;
            if(body.Length>response.Limit*4/3+4){Report("response_too_large");return;}
            var bytes=result.RootElement.GetProperty("base64Encoded").GetBoolean()?Convert.FromBase64String(body):Encoding.UTF8.GetBytes(body);
            if(bytes.Length>response.Limit){Report("response_too_large");return;}
            using var json=JsonDocument.Parse(bytes,new(){MaxDepth=32});
            var envelope=new JsonObject{["url"]=response.Url,["body"]=JsonNode.Parse(json.RootElement.GetRawText())};
            await Evaluate("globalThis.__widgetRailResponse("+envelope.ToJsonString()+")",response.Epoch);
        }
        catch(Exception error)when(error is not OutOfMemoryException){if(Current(response.Epoch))Report("response_unavailable");}
        finally{reading--;}
    }
    private bool Current(long expected)=>!disposed&&expected==epoch&&definition.AllowsPage(core.Source);
    private async Task Evaluate(string source,long expected)
    {
        if(!Current(expected)||context is null)return;
        using var result=JsonDocument.Parse(await Call("Runtime.evaluate",new(){["expression"]=source,["contextId"]=context.Value}));
        if(Current(expected)&&result.RootElement.TryGetProperty("exceptionDetails",out _))
        {Report("adapter_failed");throw new InvalidOperationException("Page adapter evaluation failed.");}
    }
    private Task<string> Call(string method,JsonObject arguments)=>core.CallDevToolsProtocolMethodAsync(method,arguments.ToJsonString()).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    private void Report(string code)
    {
        LastAction=code;
        if(disposed||reportedFailure)return;
        reportedFailure=true;
        publish(new JsonObject{["kind"]="page-action-error",["error"]=code}.ToJsonString());
    }
    public void Dispose()
    {
        if(disposed)return;RetireInvalidations();RetireRequests();disposed=true;epoch++;responses.Clear();
        if(invalidationTimer is not null){invalidationTimer.Tick-=PublishInvalidation;invalidationTimer=null;}
        contextWaiter?.Ready.TrySetCanceled();contextWaiter=null;documentContexts.Clear();
        ready.TrySetCanceled();
        core.NavigationStarting-=Starting;core.ContentLoading-=Loading;core.NavigationCompleted-=Completed;
        core.SourceChanged-=LocationChanged;
        foreach(var (receiver,handler) in receivers)receiver.DevToolsProtocolEventReceived-=handler;
        receivers.Clear();
    }
    private const string BatchPrelude="""
        function batchPayloads(value) {
          if (!value || !Array.isArray(value.items) || value.items.length < 1 || value.items.length > maximumBatchItems) throw new Error('Invalid batch');
          const context=value.context??{};
          const size=x=>new TextEncoder().encode(JSON.stringify(x)).length;
          const limit=Math.min(48000,60000-size(globalThis.location?.href??''));
          const chunks=[];let current=[];let bytes=0;
          for (const item of value.items) {
            if(size({context,items:[item]})>limit)throw new Error('Item too large');
            if(size({context,items:[...current,item]})>limit){chunks.push(current);current=[];}
            current.push(item);bytes+=size(item);
          }
          if(current.length)chunks.push(current);
          if(chunks.length>48||bytes>2*1024*1024)throw new Error('Batch too large');
          const batchId=crypto.randomUUID();
          return chunks.map((items,index)=>({batchId,index,total:chunks.length,context,items}));
        }
        """;
#if ENABLE_WIDGET_VALIDATION
    internal Task FixtureSetVideoTrackAsync(string id) =>
        Evaluate($"globalThis.fixtureBindVideo('{id}')", epoch);
#endif

    internal async Task<bool> VideoViewAsync(string id, string name, bool open, CancellationToken token)
    {
        if (!AllowsBackgroundRequests || !IsReady || context is not { } execution) return false;
        var expected = epoch;
        // id and name are host-generated hex identifiers. Invoke only in this adapter's top document.
        var expression = $"globalThis['{binding}Video']('{id}','{name}',{(open ? "true" : "false")})";
        using var result = JsonDocument.Parse(await Call("Runtime.evaluate", new()
        { ["expression"] = expression, ["contextId"] = execution, ["userGesture"] = open, ["returnByValue"] = true }).WaitAsync(token));
        return Current(expected) && result.RootElement.GetProperty("result").TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.True;
    }

    // Only the adapter receives the track binding function. The host supplies a fresh
    // blank child and keeps the original browser document and media track alive.
    private const string VideoViewPrelude="""
        const videoViews=new Map(), openVideoWindow=window.open.bind(window);
        function setVideoTrack(id,track,restart=false){
          const entry=videoViews.get(id);
          if(!entry || entry.child.closed || (track!==null && !(track instanceof MediaStreamTrack && track.kind==='video' && track.readyState==='live')))return false;
          if(!track){
            if(entry.video)entry.video.srcObject=null;
            entry.track=null;entry.child.close();return true;
          }
          const doc=entry.child.document;
          let video=entry.video;
          if(!video){
            video=doc.createElement('video');video.muted=true;video.autoplay=true;video.playsInline=true;
            doc.documentElement.style.background='transparent';doc.body.style.margin='0';doc.body.style.background='transparent';
            Object.assign(video.style,{display:'block',width:'100vw',height:'100vh',objectFit:'contain'});
            doc.body.replaceChildren(video);entry.video=video;
          }
          if(entry.track===track&&!restart)return true;
          entry.track=track;entry.failed=false;
          const source=new MediaStream([track]);
          video.srcObject=source;
          video.play().catch(()=>{if(video.srcObject===source){entry.failed=true;entry.child.close();}});
          return true;
        }
        function videoPlayback(id){
          const entry=videoViews.get(id),v=entry?.video;
          if(!entry||entry.child.closed||entry.failed)return null;
          const q=v?.getVideoPlaybackQuality();
          return {width:v?.videoWidth??0,height:v?.videoHeight??0,paused:v?.paused??true,readyState:v?.readyState??0,
              totalFrames:q?.totalVideoFrames??0,droppedFrames:q?.droppedVideoFrames??0};
        }
        Object.defineProperty(globalThis,videoEndpoint,{value:(id,name,open)=>{
          if(!open){const entry=videoViews.get(id);if(entry){videoViews.delete(id);entry.track=null;if(entry.video)entry.video.srcObject=null;entry.child.close();}return true;}
          if(videoViews.size>=4||videoViews.has(id))return false;
          const child=openVideoWindow('about:blank',name);
          if(!child)return false;
          videoViews.set(id,{child,video:null,track:null,failed:false});return true;
        }});
        """;

    private const string BackendPrelude="""
        const host=Object.freeze({
          onRequest(callback){requestHandler=callback;},
          setVideoTrack, videoPlayback,
          observeMainView(){deliver('{"observeMainView":true}');},
          invalidate(){deliver('{"invalidate":1}');},
          batchResult(context,items){return{[batchResultKey]:true,context,items};}
        });
        """;
    private const string Prelude="""
        let receive=()=>{};
        let actionStyle='';
        const baseStyle='display:block;margin:8px 0;padding:10px 14px;border:2px solid currentColor;border-radius:6px;background:Canvas;color:CanvasText;font:600 14px system-ui;cursor:pointer;';
        globalThis.__widgetRailStyle=css=>{actionStyle=css;for(const b of document.querySelectorAll('button[data-widgetrail-action]'))b.style.cssText=baseStyle+css;};
        const host=Object.freeze({
          onRequest(callback){requestHandler=callback;},
          batchResult(context,items){return{[batchResultKey]:true,context,items};},
          onResponse(callback){receive=callback;},
          addBatchAction(parent,key,label,getPayload){return host.addAction(parent,key,label,getPayload,true);},
          addAction(parent,key,label,getPayload,batch=false){
            const button=document.createElement('button');
            button.type='button';button.textContent=label;button.setAttribute('aria-label','WidgetRail: '+label);
            const brand=document.createElement('span');brand.textContent='WidgetRail';brand.style.cssText='display:block;font:10px system-ui;opacity:.8';button.append(brand);
            button.dataset.widgetrailAction=key;
            button.style.cssText=baseStyle+actionStyle;
            button.addEventListener('click',event=>{
              if(!event.isTrusted)return;
              event.preventDefault();event.stopPropagation();
              try {
                const value=getPayload();
                for(const payload of batch?batchPayloads(value):[value])deliver(JSON.stringify({action:key,payload}));
              }
              catch {button.textContent='Action unavailable — reload results';}
            });
            parent.append(button);return button;
          }
        });
        globalThis.__widgetRailResponse=value=>receive(value);
        """;
}
