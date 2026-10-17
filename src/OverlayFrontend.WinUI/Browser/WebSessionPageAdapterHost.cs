using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>One top-level document's isolated adapter; no page credentials or arbitrary CDP are exposed.</summary>
internal sealed class WebSessionPageAdapterHost : IDisposable
{
    private readonly CoreWebView2 core;
    private readonly WebSessionPageAdapter definition;
    private readonly string script;
    private readonly Func<bool> canInvoke;
    private readonly Action<string> publish;
    private readonly List<(CoreWebView2DevToolsProtocolEventReceiver Receiver, Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2DevToolsProtocolEventReceivedEventArgs> Handler)> receivers = [];
    private readonly Dictionary<string, (long Epoch, string Url, int Limit)> responses = [];
    private readonly string binding = "widgetRailAction" + Guid.NewGuid().ToString("N");
    private long epoch;
    private int? context;
    private string? loader, frame;
    private bool disposed, reportedFailure;
    private int reading;
    internal string LastAction { get; private set; }="none";
    private string buttonStyle="background:Canvas;color:CanvasText;";
    internal void SetStyle(string style)
    {
        if(buttonStyle==style)return;buttonStyle=style;
        if(context is not null)_=RefreshStyleAsync(epoch);
    }
    private async Task RefreshStyleAsync(long current)
    {
        try{await Evaluate("globalThis.__widgetRailStyle?.("+JsonSerializer.Serialize(buttonStyle)+")",current);}
        catch(Exception error)when(error is not OutOfMemoryException){/* Navigation retires this style target. */}
    }

    internal WebSessionPageAdapterHost(CoreWebView2 core, WebSessionPageAdapter definition, string script,
        Func<bool> canInvoke, Action<string> publish)
    { this.core=core;this.definition=definition;this.script=script;this.canInvoke=canInvoke;this.publish=publish; }

    internal async Task StartAsync()
    {
        core.NavigationStarting += Starting;
        core.ContentLoading += Loading;
        Listen("Runtime.bindingCalled", Bound);
        Listen("Network.responseReceived", Response);
        Listen("Network.loadingFinished", Finished);
        Listen("Network.loadingFailed", Failed);
        await Call("Network.enable", new() { ["maxTotalBufferSize"]=2*1024*1024, ["maxResourceBufferSize"]=WebSessionResponseRule.MaximumResponseBytes });
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
        epoch++;context=null;loader=frame=null;responses.Clear();reportedFailure=false;
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
            using var world=JsonDocument.Parse(await Call("Page.createIsolatedWorld",new(){["frameId"]=frame,["worldName"]=binding}));
            if(!Current(current))return;
            context=world.RootElement.GetProperty("executionContextId").GetInt32();
            await Call("Runtime.addBinding",new(){["name"]=binding,["executionContextId"]=context.Value});
            if(!Current(current))return;
            var source="(()=>{const deliver=globalThis["+JsonSerializer.Serialize(binding)+"];delete globalThis["+JsonSerializer.Serialize(binding)+"];"+Prelude+"\n"+script+"\n})()";
            await Evaluate(source,current);
            await RefreshStyleAsync(current);
        }
        catch(Exception error)when(error is not OutOfMemoryException){if(Current(current))Report("adapter_unavailable");}
    }
    private void Bound(JsonElement value)
    {
        LastAction="received";
        if(context is null||!canInvoke()||!Current(epoch)||value.GetProperty("name").GetString()!=binding||
            value.GetProperty("executionContextId").GetInt32()!=context){LastAction="not-current-or-interactive";return;}
        var text=value.GetProperty("payload").GetString();
        if(text is null||Encoding.UTF8.GetByteCount(text)>WebSessionContract.MaximumMessageBytes-2048){Report("result_too_large");return;}
        using var data=JsonDocument.Parse(text,new(){MaxDepth=14});
        var action=data.RootElement.GetProperty("action").GetString();
        if(!WebSessionContract.IsKey(action)){Report("invalid_page_action");return;}
        publish(new JsonObject { ["kind"]="page-action",["action"]=action,["document"]=core.Source,
            ["payload"]=JsonNode.Parse(data.RootElement.GetProperty("payload").GetRawText()) }.ToJsonString());
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
        if(Current(expected)&&result.RootElement.TryGetProperty("exceptionDetails",out _))Report("adapter_failed");
    }
    private Task<string> Call(string method,JsonObject arguments)=>core.CallDevToolsProtocolMethodAsync(method,arguments.ToJsonString()).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    private void Report(string code)
    {
        if(disposed||reportedFailure)return;
        reportedFailure=true;
        publish(new JsonObject{["kind"]="page-action-error",["error"]=code}.ToJsonString());
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;epoch++;responses.Clear();
        core.NavigationStarting-=Starting;core.ContentLoading-=Loading;
        foreach(var (receiver,handler) in receivers)receiver.DevToolsProtocolEventReceived-=handler;
        receivers.Clear();
    }
    private const string Prelude="""
        let receive=()=>{};
        let actionStyle='';
        const baseStyle='display:block;margin:8px 0;padding:10px 14px;border:2px solid currentColor;border-radius:6px;background:Canvas;color:CanvasText;font:600 14px system-ui;cursor:pointer;';
        globalThis.__widgetRailStyle=css=>{actionStyle=css;for(const b of document.querySelectorAll('button[data-widgetrail-action]'))b.style.cssText=baseStyle+css;};
        const host=Object.freeze({
          onResponse(callback){receive=callback;},
          addAction(parent,key,label,getPayload){
            const button=document.createElement('button');
            button.type='button';button.textContent=label;button.setAttribute('aria-label','WidgetRail: '+label);
            const brand=document.createElement('span');brand.textContent='WidgetRail';brand.style.cssText='display:block;font:10px system-ui;opacity:.8';button.append(brand);
            button.dataset.widgetrailAction=key;
            button.style.cssText=baseStyle+actionStyle;
            button.addEventListener('click',event=>{
              if(!event.isTrusted)return;
              event.preventDefault();event.stopPropagation();
              try {deliver(JSON.stringify({action:key,payload:getPayload()}));}
              catch {button.textContent='Item unavailable — reload results';}
            });
            parent.append(button);return button;
          }
        });
        globalThis.__widgetRailResponse=value=>receive(value);
        """;
}
