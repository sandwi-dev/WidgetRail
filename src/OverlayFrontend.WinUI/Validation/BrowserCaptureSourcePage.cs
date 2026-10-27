using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Generated browser-rendered motion and optional tone. No network, account, microphone, or user content.</summary>
internal sealed partial class BrowserCaptureSourcePage : Page, IAsyncDisposable
{
    private readonly WebView2 web = new();
    private readonly string output;
    private readonly bool audio;
    private readonly bool stationary;
    private Task? initializing;
    private bool disposed, readyWritten;
    private readonly string processCreated;
    private const string Origin = "https://capture-source.fixture.invalid";
    private const string PageUrl = Origin + "/index.html";
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource pageReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string lastStage = "created";
    private int stageCount;
    private sealed record SourceGeometry(int Width,int Height,int OuterWidth,int OuterHeight,string Measurement="DWM extended frame bounds");
    private SourceGeometry? geometry;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left,Top,Right,Bottom; }
    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint window,uint attribute,out NativeRect bounds,uint size);

    private SourceGeometry ReadGeometry()
    {
        var window=App.Window??throw new InvalidOperationException("Missing synthetic source window.");
        Marshal.ThrowExceptionForHR(DwmGetWindowAttribute(WinRT.Interop.WindowNative.GetWindowHandle(window),9,out var bounds,(uint)Marshal.SizeOf<NativeRect>()));
        var size=window.AppWindow.Size;
        var value=new SourceGeometry(checked(bounds.Right-bounds.Left),checked(bounds.Bottom-bounds.Top),size.Width,size.Height);
        if(value.Width is <2 or >8192||value.Height is <2 or >8192)throw new InvalidOperationException("Invalid source-owned capturable bounds.");
        return value;
    }
    private async Task ConfigureGeometryAsync(CancellationToken token)
    {
        var arguments=Environment.GetCommandLineArgs();
        var requestedWidth=Shell.FrontendArguments.Value(arguments,"--capture-source-width");
        var requestedHeight=Shell.FrontendArguments.Value(arguments,"--capture-source-height");
        if(requestedWidth is null){geometry=ReadGeometry();return;}
        if(!int.TryParse(requestedWidth,out var width)||!int.TryParse(requestedHeight,out var height)||width is <2 or >8192||height is <2 or >8192)
            throw new InvalidOperationException("Invalid explicit source dimensions.");
        // AppWindow.Size includes invisible resize borders, while WGC captures
        // the DWM extended frame bounds. Measure the source independently and
        // size that region exactly; never infer expected dimensions from output.
        for(var attempt=0;attempt<4;attempt++)
        {
            geometry=ReadGeometry();if(geometry.Width==width&&geometry.Height==height)return;
            App.Window!.AppWindow.Resize(new(geometry.OuterWidth+width-geometry.Width,geometry.OuterHeight+height-geometry.Height));
            await Task.Delay(100,token);
        }
        geometry=ReadGeometry();
        if(geometry.Width!=width||geometry.Height!=height)throw new InvalidOperationException("Synthetic capturable bounds did not reach the requested dimensions.");
    }

    internal BrowserCaptureSourcePage(string output)
    {
        this.output = Path.GetFullPath(output);
        audio = Environment.GetCommandLineArgs().Contains("--capture-source-audio");
        stationary = Environment.GetCommandLineArgs().Contains("--capture-source-static");
        using var process = Process.GetCurrentProcess();
        processCreated = process.StartTime.ToUniversalTime().ToFileTimeUtc().ToString("X16");
        Content = web;
        Loaded += (_, _) => initializing ??= InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(output);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            await ConfigureGeometryAsync(deadline.Token);
            var profile = Path.Combine(output, "profile");
            if (Directory.Exists(profile)) throw new InvalidOperationException("The synthetic browser source requires a fresh profile.");
            Progress("environment-start");
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, profile,
                new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" }).AsTask().WaitAsync(deadline.Token);
            if (disposed) return;
            Progress("controller-start");
            await web.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(deadline.Token);
            if (disposed) return;
            Progress("controller-ready");
            var core = web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, args) =>
            {
                var generatedAutoplay = args.PermissionKind == CoreWebView2PermissionKind.Autoplay && core.Source == PageUrl &&
                    Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.GetLeftPart(UriPartial.Authority) == Origin;
                args.Handled = true;
                args.State = generatedAutoplay ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
                args.SavesInProfile = false;
                Progress("permission", args.PermissionKind + ":" + args.State);
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            ulong? navigationId = null;
            core.NavigationStarting += (_, args) =>
            { args.Cancel = args.Uri != PageUrl; if (!args.Cancel) navigationId = args.NavigationId; Progress("navigation-start", new { args.Uri, args.Cancel }); };
            core.NavigationCompleted += (_, args) =>
            {
                if (args.NavigationId != navigationId) return;
                Progress("navigation-complete", new { args.IsSuccess, Error = args.WebErrorStatus.ToString() });
                if (!args.IsSuccess) WriteReady("Synthetic document navigation failed: " + args.WebErrorStatus);
            };
            var html = Encoding.UTF8.GetBytes(Html.Replace("/*CONFIG*/", $"const withAudio={(audio ? "true" : "false")},stationary={(stationary ? "true" : "false")};", StringComparison.Ordinal));
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, args) =>
            {
                var document = args.Request.Uri == PageUrl && args.ResourceContext == CoreWebView2WebResourceContext.Document;
                Progress("resource", new { Uri = args.Request.Uri, Context = args.ResourceContext.ToString(), Served = document });
                args.Response = environment.CreateWebResourceResponse(new MemoryStream(document ? html : Array.Empty<byte>()).AsRandomAccessStream(),
                    document ? 200 : 403, document ? "OK" : "Blocked", document ? "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store" : "Content-Type: text/plain");
            };
            core.WebMessageReceived += (_, args) =>
            {
                if (disposed || args.Source != PageUrl || args.WebMessageAsJson.Length > 4096) return;
                using var message = JsonDocument.Parse(args.WebMessageAsJson);
                var value = message.RootElement;
                if (value.GetProperty("kind").GetString() == "ready")
                    WriteReady(value.GetProperty("audioState").GetString() == (audio ? "running" : "off") ? null : "Synthetic audio did not start.");
                else if (value.GetProperty("kind").GetString() == "error") WriteReady("Synthetic page initialization failed.");
                else if (value.GetProperty("kind").GetString() == "stage") Progress("script", value.GetProperty("stage").GetString());
                else if (value.GetProperty("kind").GetString() == "status")
                    TryWriteStatus(JsonSerializer.Serialize(new { processId = Environment.ProcessId, processCreated, audio, stationary, geometry=ReadGeometry(), source = value }));
            };
            Progress("navigate-generated-document");
            core.Navigate(PageUrl);
            await pageReady.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var stage = lastStage;
            try
            {
                if (web.CoreWebView2 is { } core) Progress("failure-document", await core.ExecuteScriptAsync(
                    "JSON.stringify({url:location.href,ready:document.readyState,title:document.title,source:typeof syntheticSource,state:globalThis.syntheticSource?.status()})")
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
            }
            catch (Exception diagnostic) when (diagnostic is not OutOfMemoryException) { }
            WriteReady(error.GetType().Name + " during " + stage);
        }
    }

    private void Progress(string stage, object? detail = null)
    {
        lastStage = stage;
        if (stageCount++ >= 64) return;
        try { File.AppendAllText(Path.Combine(output, "source-progress.jsonl"), JsonSerializer.Serialize(new { stage, detail, time = DateTimeOffset.UtcNow }) + Environment.NewLine); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void WriteReady(string? error)
    {
        if (readyWritten) return;
        var path = Path.Combine(output, "source-ready.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { ready = error is null, error,
            processId = Environment.ProcessId, processCreated, sourceKind = "browser", audio, stationary, geometry }));
        File.Move(path + ".tmp", path);
        readyWritten = true;
        Progress(error is null ? "ready" : "failed", error);
        pageReady.TrySetResult();
    }
    private void TryWriteStatus(string json)
    {
        // Diagnostics must not interrupt animation if a concurrent evidence reader
        // temporarily holds the previous snapshot without delete sharing.
        var path = Path.Combine(output, "source-status.json");
        try { File.WriteAllText(path + ".tmp", json); File.Move(path + ".tmp", path, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        if (initializing is not null) await initializing;
        try { if (web.CoreWebView2 is { } core) await core.ExecuteScriptAsync("globalThis.syntheticSource?.stop()").AsTask().WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception error) when (error is not OutOfMemoryException) { }
        web.Close();
        lifetime.Dispose();
    }

    private const string Html = """
        <!doctype html><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'none'">
        <title>Generated browser capture source</title>
        <style>html,body{margin:0;overflow:hidden;background:#101625}canvas{display:block;width:100vw;height:100vh}</style>
        <canvas></canvas><script>
        (()=>{'use strict';/*CONFIG*/
          const canvas=document.querySelector('canvas'),ctx=canvas.getContext('2d',{alpha:false});
          let frame=0,draws=0,last=0,raf=0,audio=null,oscillator=null,timer=0,dirty=true;
          function resize(){const scale=Math.min(devicePixelRatio,2);canvas.width=Math.round(innerWidth*scale);canvas.height=Math.round(innerHeight*scale);dirty=true;}
          function draw(now){
            if((!stationary||dirty)&&now-last>=1000/60){last=now-(now-last)%(1000/60);draws++;dirty=false;
              const w=canvas.width,h=canvas.height,t=stationary?0:now/1000;
              ctx.fillStyle='#101625';ctx.fillRect(0,0,w,h);
              for(let y=0;y<8;y++)for(let x=0;x<14;x++){
                ctx.fillStyle=`hsl(${(x*23+y*17+t*90)%360} 65% ${35+(x+y)%3*10}%)`;
                ctx.fillRect(x*w/14,y*h/8,w/14-3,h/8-3);
              }
              ctx.fillStyle='#ffffff';ctx.beginPath();ctx.arc(w*(.5+.35*Math.sin(t*2.3)),h*(.5+.3*Math.cos(t*1.7)),h*.08,0,Math.PI*2);ctx.fill();
              ctx.fillStyle='#101625';ctx.fillRect(16,16,w*.65,72);
              ctx.fillStyle='#ffffff';ctx.font='bold 26px sans-serif';ctx.fillText('Synthetic browser pixels · no user content',28,48);
              ctx.font='20px monospace';ctx.fillText(stationary?'Static fixture':`Frame ${frame++}`,28,76);
            }
            raf=requestAnimationFrame(draw);
          }
          async function start(){
            chrome.webview.postMessage({kind:'stage',stage:'start'});
            resize();addEventListener('resize',resize);raf=requestAnimationFrame(draw);
            if(withAudio){audio=new AudioContext({sampleRate:48000});oscillator=audio.createOscillator();const gain=audio.createGain();
              oscillator.frequency.value=440;gain.gain.value=.025;oscillator.connect(gain).connect(audio.destination);oscillator.start();
              chrome.webview.postMessage({kind:'stage',stage:'audio-resume'});await audio.resume();chrome.webview.postMessage({kind:'stage',stage:'audio-running'});}
            await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
            chrome.webview.postMessage({kind:'ready',audioState:audio?.state??'off'});
            timer=setInterval(()=>chrome.webview.postMessage({kind:'status',draws,sampleTime:performance.now(),width:canvas.width,height:canvas.height,
              audioState:audio?.state??'off',audioTime:audio?.currentTime??0,visibility:document.visibilityState}),1000);
          }
          globalThis.syntheticSource={status(){return{draws,audioState:audio?.state??'off',visibility:document.visibilityState};},
            stop(){cancelAnimationFrame(raf);clearInterval(timer);oscillator?.stop();audio?.close();}};
          start().catch(()=>chrome.webview.postMessage({kind:'error'}));
        })();</script>
        """;
}
