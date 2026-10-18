using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Isolated real-WebView tree renderer probe. Does not open user profiles or contact services.</summary>
internal sealed partial class TreeArtworkValidationPage : Page, IAsyncDisposable
{
    private readonly WebView2 view = new() { Width = 1000, Height = 650, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock status = new() { Text = "Checking passive-tree rendering…" };
    private readonly List<string> checks = [];
    private readonly List<object> measurements = [];
    private readonly List<string> blocked = [];
    private readonly List<string> missing = [];
    private Task? running;
    private readonly string output, source, art, fixture;
    private CoreWebView2Environment? environment;
    private const string Origin = "https://tree-fixture.example/";
    internal TreeArtworkValidationPage(string output, string source, string art, string fixture)
    {
        this.output=Path.GetFullPath(output);this.source=Path.GetFullPath(source);this.art=Path.GetFullPath(art);this.fixture=Path.GetFullPath(fixture);
        var panel=new StackPanel();panel.Children.Add(status);panel.Children.Add(view);Content=panel;
        Loaded+=(_,_)=>running??=RunAsync();
    }
    private async Task RunAsync()
    {
        string? error=null;
        try
        {
            Directory.CreateDirectory(output);
            if(!File.Exists(Path.Combine(source,"web-session.js")))throw new FileNotFoundException("Stage the real WebSessionRuntime.js as web-session.js beside tree.html before running this fixture.");
            environment=await CoreWebView2Environment.CreateWithOptionsAsync(null,Path.Combine(output,"isolated-profile"),null);
            await view.EnsureCoreWebView2Async(environment);
            var core=view.CoreWebView2;
            core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
            core.WebResourceRequested+=Resource;
            core.Settings.AreDefaultContextMenusEnabled=false;
            await core.AddScriptToExecuteOnDocumentCreatedAsync("""
                window.treeProbe={frames:0,times:[],images:[],errors:[],messages:[],resolutions:[],resolutionEvents:0,resizeEvents:0};
                const media=window.matchMedia.bind(window);
                window.matchMedia=query=>{const result=media(query);if(query.includes('resolution')){treeProbe.resolutions.push(result);result.addEventListener('change',()=>treeProbe.resolutionEvents++);}return result;};
                window.addEventListener('resize',()=>treeProbe.resizeEvents++);
                const raf=window.requestAnimationFrame.bind(window);
                window.requestAnimationFrame=callback=>raf(t=>{const start=performance.now();try{callback(t);}finally{if(callback.name==='draw'){treeProbe.frames++;treeProbe.times.push(performance.now()-start);}}});
                const ImageType=window.Image;
                window.Image=function(...args){const image=new ImageType(...args);treeProbe.images.push(image);return image;};
                window.addEventListener('error',e=>treeProbe.errors.push(e.message));
                window.addEventListener('unhandledrejection',e=>treeProbe.errors.push(String(e.reason)));
                """);
            var ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.WebMessageReceived+=(_,args)=>
            {
                using var message=JsonDocument.Parse(args.WebMessageAsJson);
                if(message.RootElement.TryGetProperty("type",out var type)&&type.GetString()=="ready")ready.TrySetResult(true);
            };
            core.Navigate(Origin+"tree.html");await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var data=JsonDocument.Parse(await File.ReadAllTextAsync(fixture));
            var root=data.RootElement;
            var nodes=root.GetProperty("nodes").EnumerateArray().Select(node=>
            {
                var copy=JsonNode.Parse(node.GetRawText())!;copy["allocated"]=false;
                return JsonSerializer.SerializeToElement(copy);
            }).ToArray();
            var asc=nodes.First(n=>n.GetProperty("ascendancy").GetBoolean()).GetProperty("ascendancyName").GetString();
            core.PostWebMessageAsJson("{\"type\":\"begin\",\"revision\":1}");
            foreach(var chunk in nodes.Chunk(50))Post(new{type="nodes",revision=1,items=chunk});
            foreach(var chunk in root.GetProperty("links").EnumerateArray().Chunk(100))Post(new{type="links",revision=1,items=chunk});
            foreach(var chunk in root.GetProperty("backgrounds").EnumerateArray().Chunk(50))Post(new{type="artwork",revision=1,items=chunk});
            Post(new{type="end",revision=1,selected=0,treeVersion=root.GetProperty("treeVersion").GetString(),ascendancy=asc});
            await SettleAsync();
            Check(await ValueAsync<bool>("!document.querySelector('#status').textContent.includes('Loading')"),"Complete snapshot replaces loading state");
            Check(await ValueAsync<int>("treeProbe.images.filter(i=>i.naturalWidth>0).length")>0,"Native WebView decodes packaged artwork");
            Check(missing.Count==0&&blocked.Count==0,"Every requested resource is local and available");
            Check(await ValueAsync<int>("document.querySelectorAll('.node').length")<=100,"Accessible node targets stay bounded");
            await ThemeAsync(false);await MeasureAsync("dark-root");await CaptureAsync("dark-root");
            var target=nodes.First(n=>n.GetProperty("interactive").GetBoolean()&&!n.GetProperty("ascendancy").GetBoolean());
            Post(new{type="select",id=target.GetProperty("id").GetInt32()});await SettleAsync();
            Check(await ValueAsync<bool>("!document.querySelector('#selection-marker').hidden"),"Locate selected retains a visible marker");
            await MeasureAsync("dark-detail");await CaptureAsync("dark-detail");
            await core.ExecuteScriptAsync("document.querySelector('#ascendancy').click()");await SettleAsync();
            await MeasureAsync("dark-ascendancy");await CaptureAsync("dark-ascendancy");
            await ThemeAsync(true);await MeasureAsync("light-ascendancy");await CaptureAsync("light-ascendancy");
            view.Width=560;view.Height=420;await SettleAsync();
            Check(await ValueAsync<bool>("document.documentElement.scrollWidth<=innerWidth && document.documentElement.scrollHeight<=innerHeight"),"Constrained tree has no page overflow");
            await CaptureAsync("light-constrained");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[{\"name\":\"forced-colors\",\"value\":\"active\"}]}");
            await core.ExecuteScriptAsync("window.dispatchEvent(new Event('widgetrailthemechange'))");await SettleAsync();
            Check(await ValueAsync<bool>("matchMedia('(forced-colors: active)').matches"),"Forced-colors native media query activates");
            await CaptureAsync("high-contrast");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia","{\"features\":[]}");
            view.Width=1000;view.Height=650;await ThemeAsync(false);
            var originalPosition=App.Window.AppWindow.Position;
            try
            {
                var displays=Microsoft.UI.Windowing.DisplayArea.FindAll();
                for(var displayIndex=0;displayIndex<Math.Min(2,displays.Count);displayIndex++)
                {
                    var display=displays[displayIndex];
                    App.Window.AppWindow.Move(new(display.WorkArea.X+32,display.WorkArea.Y+32));
                    await Task.Delay(350);await SettleAsync();
                    var state=await ValueAsync<JsonElement>("({dpr:devicePixelRatio,canvas:document.querySelector('canvas').width,map:document.querySelector('#map').clientWidth})");
                    measurements.Add(new{name="native-monitor-scale",state});
                    Check(await ValueAsync<bool>("Math.abs(document.querySelector('canvas').width-document.querySelector('#map').clientWidth*devicePixelRatio)<=1"),"Canvas backing resolution follows actual monitor placement");
                }
            }
            finally { App.Window.AppWindow.Move(originalPosition); }
            foreach(var dpr in new[]{1d,1.5d,2d})
            {
                await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",JsonSerializer.Serialize(new{width=1000,height=650,deviceScaleFactor=dpr,mobile=false}));
                // CDP emulation can change the exposed ratio without delivering
                // native resize/resolution events. Exercise real navigation at
                // that ratio; actual monitor notification is checked above.
                await core.ExecuteScriptAsync("document.querySelector('#right').click()");
                await SettleAsync();
                measurements.Add(new{name="scale-state-"+dpr,state=await ValueAsync<JsonElement>("({dpr:devicePixelRatio,canvas:document.querySelector('canvas').width,map:document.querySelector('#map').clientWidth,frames:treeProbe.frames,resize:treeProbe.resizeEvents,changes:treeProbe.resolutionEvents,queries:treeProbe.resolutions.map(q=>({query:q.media,matches:q.matches}))})")});
                Check(await ValueAsync<bool>("Math.abs(document.querySelector('canvas').width-document.querySelector('#map').clientWidth*devicePixelRatio)<=1"),"Canvas rendering uses emulated scale "+dpr);
                await MeasureAsync("scale-"+dpr);
            }
            await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride","{}");await SettleAsync();
            var frames=await ValueAsync<int>("treeProbe.frames");await Task.Delay(600);
            Check(await ValueAsync<int>("treeProbe.frames")==frames,"No continuous idle redraws after assets settle");
            var decoded=await ValueAsync<long>("treeProbe.images.filter(i=>i.src&&i.naturalWidth).reduce((s,i)=>s+i.naturalWidth*i.naturalHeight*4,0)");
            Check(decoded<=52*1024*1024,"Decoded current-tree textures stay within 52 MiB ceiling");
            Check(missing.Count==0&&blocked.Count==0,"Zoom and ascendancy resources remain local and available");
            Check(await ValueAsync<bool>("treeProbe.images.every(i=>!i.src||i.naturalWidth>0)"),"Every requested artwork image decodes successfully");
            Check(await ValueAsync<int>("treeProbe.errors.length")==0,"No JavaScript errors during interaction, themes and resize");
            measurements.Add(new{decodedImageBytes=decoded,loadedImages=await ValueAsync<int>("treeProbe.images.filter(i=>i.naturalWidth).length")});
        }
        catch(Exception exception){error=exception.ToString();}
        finally
        {
            status.Text=error is null?"Tree artwork checks passed":"Tree artwork checks failed";
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new{passed=error is null,checks,measurements,missing,blocked,error,scope="Native WebView renderer with isolated local resources; not complete widget session routing"},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private void Post(object message)=>view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
    private void Resource(CoreWebView2 sender,CoreWebView2WebResourceRequestedEventArgs args)
    {
        var uri=new Uri(args.Request.Uri);byte[] bytes=[];var statusCode=404;var mime="text/plain";
        if(uri.Host!="tree-fixture.example")blocked.Add(uri.GetLeftPart(UriPartial.Authority));
        else
        {
            var relative=Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
            var directory=relative.StartsWith("art/",StringComparison.Ordinal)?art:source;
            var local=Path.GetFullPath(Path.Combine(directory,relative.StartsWith("art/",StringComparison.Ordinal)?relative[4..]:relative));
            if(local.StartsWith(Path.TrimEndingDirectorySeparator(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&File.Exists(local))
            {
                bytes=File.ReadAllBytes(local);statusCode=200;
                mime=Path.GetExtension(local) switch{".html"=>"text/html",".js"=>"text/javascript",".css"=>"text/css",".json"=>"application/json",".webp"=>"image/webp",_=>"application/octet-stream"};
            }
            else missing.Add(relative);
        }
        args.Response=environment!.CreateWebResourceResponse(new MemoryStream(bytes).AsRandomAccessStream(),statusCode,statusCode==200?"OK":"Blocked","Content-Type: "+mime+"\r\nCache-Control: no-store");
    }
    private async Task<T> ValueAsync<T>(string expression)
    {
        using var result=JsonDocument.Parse(await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",JsonSerializer.Serialize(new{expression,returnByValue=true,awaitPromise=true})));
        if(result.RootElement.TryGetProperty("exceptionDetails",out var error))throw new InvalidOperationException(error.ToString());
        return result.RootElement.GetProperty("result").GetProperty("value").Deserialize<T>()!;
    }
    private async Task SettleAsync()
    {
        for(var i=0;i<60;i++)
        {
            await Task.Delay(100);
            if(await ValueAsync<bool>("treeProbe.images.every(i=>!i.src||i.complete)"))
            { var before=await ValueAsync<int>("treeProbe.frames");await Task.Delay(120);if(before==await ValueAsync<int>("treeProbe.frames"))return; }
        }
        throw new TimeoutException("Tree image decoding/layout did not settle.");
    }
    private async Task ThemeAsync(bool light)
    {
        await view.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.colorScheme="+JsonSerializer.Serialize(light?"light":"dark")+";window.dispatchEvent(new Event('widgetrailthemechange'));");await SettleAsync();
    }
    private async Task MeasureAsync(string name)
    {
        var sample=await ValueAsync<JsonElement>("""
            (async()=>{treeProbe.times=[];for(let i=0;i<12;i++){document.querySelector(i%2?'#left':'#right').click();await new Promise(r=>requestAnimationFrame(r));}const times=treeProbe.times.slice().sort((a,b)=>a-b);return {frames:times.length,p50:times[Math.floor(times.length*.5)],p95:times[Math.floor(times.length*.95)],viewport:[innerWidth,innerHeight],dpr:devicePixelRatio};})()
            """);
        measurements.Add(new{name,sample});await SettleAsync();
    }
    private async Task CaptureAsync(string name)
    {
        var rect=await ValueAsync<JsonElement>("(()=>{const r=document.querySelector('#map').getBoundingClientRect();return {x:r.x,y:r.y,width:r.width,height:r.height,scale:1};})()");
        using var result=JsonDocument.Parse(await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",JsonSerializer.Serialize(new{format="png",clip=rect,fromSurface=true})));
        await File.WriteAllBytesAsync(Path.Combine(output,name+".png"),Convert.FromBase64String(result.RootElement.GetProperty("data").GetString()!));
    }
    private void Check(bool passed,string description){if(!passed)throw new InvalidOperationException(description);checks.Add(description);}
    public async ValueTask DisposeAsync(){if(running is not null)await running;view.Close();}
}
