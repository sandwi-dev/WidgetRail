using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsAppLibraryProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Actual BrowserSurface capture using only a process this fixture creates.</summary>
internal sealed partial class WebSessionCaptureMediaValidationPage : Page, IAsyncDisposable
{
    private Task? running;
    internal WebSessionCaptureMediaValidationPage(string output)
    {
        Content = new TextBlock { Text = "Testing native web-session capture with generated media…", Margin = new(24) };
        Loaded += (_, _) => running ??= RunAsync(Path.GetFullPath(output));
    }
    private async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>(); var samples = new List<object>(); string? error = null;
        try
        {
            if(Environment.GetCommandLineArgs().Contains("--capture-track-constraints"))
                await ExerciseTrackConstraintsAsync(output,checks,samples);
            else if(Environment.GetCommandLineArgs().Contains("--capture-renderer-stall-matrix"))
                await ExerciseRendererStallsAsync(output,checks,samples);
            else await ExerciseAsync(output,checks,samples);
        }
        catch (Exception failure) { error = failure.ToString(); }
        finally { BrowserSurface.FixturePage = null; }
        var result = Path.Combine(output, "result.json");
        await File.WriteAllTextAsync(result + ".tmp", JsonSerializer.Serialize(new { checks, samples, error }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(result + ".tmp", result, true);
        ((TextBlock)Content).Text = error ?? $"Passed {checks.Count} capture integration checks.";
    }
    private static async Task ExerciseAsync(string output, List<string> checks, List<object> samples)
    {
        var sourceExe = Path.Combine(AppContext.BaseDirectory, "WindowCaptureFixture.exe");
        if (!File.Exists(sourceExe)) throw new InvalidOperationException("Stage the repository's synthetic source beside the validation candidate.");
        using var source = Process.Start(new ProcessStartInfo(sourceExe, "--large --audio") { UseShellExecute = false })
            ?? throw new InvalidOperationException("Synthetic source did not start.");
        BrowserSurface? browser = null;
        using var backing = new WebSessionBackgroundHost();
        try
        {
            await backing.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            const string origin = "https://capture-media.fixture.invalid";
            BrowserSurface.FixturePage = _ => "<!doctype html><title>Generated capture receiver</title><body>Synthetic receiver only.</body>";
            var environment = CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(output, "profile"), new()).AsTask();
            var definition = new WebSessionDefinition("capture", "Synthetic capture", WebSessionSource.Remote(origin + "/first"))
            { NativeCapture = true, PageAdapter = new() { ScriptAsset = "capture.js", Origins = new[] { origin }, Mode = WebSessionPageAdapterMode.SessionBackend } };
            browser = new BrowserSurface("fixture.capture-media", new("capture", 1, definition.Source.Location, definition.AccessibleName)
                { Options = definition.Browser, InteractionMode = BrowserInteractionMode.ActivateToInteract }, () => environment);
            var script = Encoding.UTF8.GetBytes(Adapter);
            browser.ConfigureHostedSession(definition, new[] { new BridgeWebSessionResource("capture.js", "text/javascript",
                Convert.ToHexString(SHA256.HashData(script)), Convert.ToBase64String(script)) });
            backing.Root.Children.Add(browser); browser.PlacementHost = backing.Root; backing.Root.UpdateLayout();
            await browser.StartHostedSessionAsync(default);
            await WaitForCaptureDocumentAsync(browser,definition.Source.Location);
            await Request(browser, "status");
            var windows = new WindowsTaskWindowCatalog(); NativeWindowPreviewTarget? target = null;
            await Until(async () =>
            {
                target = (await windows.GetWindowsAsync(default)).FirstOrDefault(window => window.PreviewTarget?.ProcessId == source.Id)?.PreviewTarget;
                return target is not null;
            });
            var session = new WebSessionReference(Guid.NewGuid().ToString("N"));
            WebCaptureStreamStatus? last = null;
            browser.HostedCaptureChanged += status => last = status;
            async Task<WebCaptureStreamTicket> Start(bool audio)
            {
                var ticket = new WebCaptureStreamTicket(session, Guid.NewGuid().ToString("N"));
                var prepared = await browser.PrepareHostedCaptureAsync(ticket, new(), _ =>
                    Task.FromResult<WebCaptureSourceSelection?>(new(target!, "Generated fixture", audio, new())), default);
                Check(prepared.Phase == WebCaptureStreamPhase.Prepared, "Host preparation arms only the selected synthetic source");
                await Request(browser, "consume");
                await Until(() => Task.FromResult(last?.Ticket == ticket && last.Phase == WebCaptureStreamPhase.Capturing));
                return ticket;
            }
            var first = await Start(true);
            await Task.Delay(1000);
            var before = await Request(browser, "status"); var started = Stopwatch.GetTimestamp();
            await Task.Delay(8000);
            var after = await Request(browser, "status");
            var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
            var fps = (after.GetProperty("frames").GetInt64() - before.GetProperty("frames").GetInt64()) / seconds;
            samples.Add(new { framesPerSecond = fps, seconds, audioSamples = after.GetProperty("audioSamples").GetInt64(), peak = after.GetProperty("peak").GetDouble() });
            Check(fps >= 25.5, "Moving synthetic source sustains the 30-FPS target within measurement tolerance");
            Check(after.GetProperty("audioSamples").GetInt64() > 48000 && after.GetProperty("peak").GetDouble() > 0.0001,
                "Selected application process audio reaches generated browser tracks");
            browser.SetPresentation(false, false);
            var hiddenBefore = after.GetProperty("frames").GetInt64();
            var hiddenStarted = Stopwatch.GetTimestamp();
            // Cross the actual 15-second hidden-memory transition. A one-second
            // check proves neither delayed policy nor sustained background media.
            await Task.Delay(BrowserSurface.HiddenMemoryDelay + TimeSpan.FromSeconds(15));
            var hiddenAfter = await Request(browser, "status");
            var hiddenSeconds = Stopwatch.GetElapsedTime(hiddenStarted).TotalSeconds;
            var hiddenFps = (hiddenAfter.GetProperty("frames").GetInt64() - hiddenBefore) / hiddenSeconds;
            samples.Add(new { phase = "hidden_after_memory_delay", framesPerSecond = hiddenFps, seconds = hiddenSeconds,
                audioSamples = hiddenAfter.GetProperty("audioSamples").GetInt64() - after.GetProperty("audioSamples").GetInt64() });
            Check(hiddenFps >= 25.5 && last?.Phase == WebCaptureStreamPhase.Capturing &&
                hiddenAfter.GetProperty("audioSamples").GetInt64() > after.GetProperty("audioSamples").GetInt64() + 48000,
                "Hidden backend sustains video and application audio beyond the delayed memory policy");
            Check((await browser.StopHostedCaptureAsync(first)).Phase == WebCaptureStreamPhase.Stopped, "Explicit stop confirms local capture teardown");
            var second = await Start(false);
            await browser.StopHostedCaptureAsync(first);
            var secondBefore = (await Request(browser, "status")).GetProperty("frames").GetInt64(); await Task.Delay(700);
            Check((await Request(browser, "status")).GetProperty("frames").GetInt64() > secondBefore && last?.Ticket == second,
                "An old ticket cannot stop the replacement source");
            await NavigateCaptureDocumentAsync(browser,origin+"/second");
            await Until(() => Task.FromResult(last?.Ticket == second && last.Phase == WebCaptureStreamPhase.Stopped));
            await Request(browser, "status");
            Check(await browser.EvaluateFixtureAsync("WidgetRailNativeMedia.status().active") == "false", "Navigation stops capture without arming the replacement document");
            Check(WebViewCaptureWorker.IsActiveHeartbeat("{\"active\":true,\"frames\":1}")&&
                new string?[]{null,"", "false", "null", "{}", "{\"active\":false}", "{\"active\":1}",
                    "{\"active\":\"true\"}", "not-json", new string(' ',2049),
                    "{\"active\":true,\"nested\":[[[[[]]]]]}"}.All(value=>!WebViewCaptureWorker.IsActiveHeartbeat(value)),
                "Capture heartbeat accepts only bounded active JSON acknowledgements");
            var invalidHeartbeats=new[]{
                (Name:"disposed",Script:"WidgetRailNativeMedia.dispose()"),
                (Name:"missing",Script:"delete globalThis.WidgetRailNativeMedia"),
                (Name:"inactive",Script:"WidgetRailNativeMedia.keep=()=>({active:false})")};
            for(var index=0;index<invalidHeartbeats.Length;index++)
            {
                var test=invalidHeartbeats[index];var invalid=await Start(false);
                await browser.EvaluateFixtureAsync(test.Script);
                await Until(()=>Task.FromResult(last?.Ticket==invalid&&last.Phase==WebCaptureStreamPhase.Stopped));
                Check(last?.Reason=="receiver_heartbeat_invalid",
                    $"{test.Name} generated-media runtime retires its exact native worker without navigation");
                await NavigateCaptureDocumentAsync(browser,origin+"/heartbeat-"+index);
                await Request(browser,"status");
            }
            var third = await Start(false);
            var retiringBrowser = new BrowserSurface("fixture.capture-retry", new("capture", 1, definition.Source.Location, definition.AccessibleName)
                { Options = definition.Browser, InteractionMode = BrowserInteractionMode.ActivateToInteract }, () => environment);
            var retiringTicket = new WebCaptureStreamTicket(new(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"));
            try
            {
                retiringBrowser.ConfigureHostedSession(definition, new[] { new BridgeWebSessionResource("capture.js", "text/javascript",
                    Convert.ToHexString(SHA256.HashData(script)), Convert.ToBase64String(script)) });
                backing.Root.Children.Add(retiringBrowser); retiringBrowser.PlacementHost = backing.Root; backing.Root.UpdateLayout();
                await retiringBrowser.StartHostedSessionAsync(default);
                await WaitForCaptureDocumentAsync(retiringBrowser,definition.Source.Location);
                await Request(retiringBrowser, "status");
                await retiringBrowser.PrepareHostedCaptureAsync(retiringTicket, new(), _ =>
                    Task.FromResult<WebCaptureSourceSelection?>(new(target!, "Generated fixture", false, new())), default);
                await Request(retiringBrowser, "consume");
                await Until(async () => (await Request(retiringBrowser, "status")).GetProperty("frames").GetInt64() > 10);
                var evaluate = retiringBrowser.CaptureDocumentEvaluatorForValidation();
                retiringBrowser.FixtureBeforeCaptureStop = () => Task.FromException(new IOException("Synthetic native stop uncertainty."));
                var failedStop = false;
                try { await retiringBrowser.DisposeAsync(); }
                catch (InvalidOperationException) { failedStop = true; }
                var closed = false;
                try { await evaluate(); }
                catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException) { closed = true; }
                Check(failedStop && closed && retiringBrowser.FixtureRetainedCaptureTicket == retiringTicket,
                    "Unconfirmed active capture retains its ticket while the real browser closes");
                retiringBrowser.FixtureBeforeCaptureStop = null;
                Check((await retiringBrowser.StopHostedCaptureAsync(retiringTicket)).Phase == WebCaptureStreamPhase.Stopped,
                    "Actual native transport cleanup succeeds after its browser controller closes");
            }
            finally
            {
                retiringBrowser.FixtureBeforeCaptureStop = null;
                await retiringBrowser.DisposeAsync(); backing.Root.Children.Remove(retiringBrowser);
            }
            source.CloseMainWindow();
            await Until(() => Task.FromResult(last?.Ticket == third && last.Phase == WebCaptureStreamPhase.Stopped));
            Check(last?.Reason is "source_closed" or "source_ended" or "capture_ended", "Source loss retires capture without selecting another window");
            await browser.DisposeAsync();
            Check(true, "Browser surface teardown completes after repeated capture lifetimes");
        }
        finally
        {
            if (browser is not null) { await browser.DisposeAsync(); backing.Root.Children.Remove(browser); }
            if (!source.HasExited) { source.Kill(); await source.WaitForExitAsync(); }
        }
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
    }
    private static Task WaitForCaptureDocumentAsync(BrowserSurface browser,string destination,long? afterEpoch=null)=>
        Until(()=>Task.FromResult(browser.FixtureCaptureDocumentReady(destination,afterEpoch)));
    private static async Task NavigateCaptureDocumentAsync(BrowserSurface browser,string destination)
    {
        // Remote navigation acknowledges admission, not document or RPC readiness.
        // Require an actual new document epoch plus exact URL/adapter readiness;
        // do not retry a request that may have entered an old execution context.
        var previous=browser.FixtureCaptureDocumentEpoch;
        await browser.NavigateHostedAsync(destination,default);
        await WaitForCaptureDocumentAsync(browser,destination,previous);
    }
    private static async Task<JsonElement> Request(BrowserSurface browser, string method)
    {
        var id = Guid.NewGuid().ToString("N");
        await browser.PostHostedMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { widgetRail = 1, kind = "request", id, method, payload = new { } }))));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            using var value = JsonDocument.Parse(Convert.FromBase64String(await browser.HostedMessages.ReadAsync(timeout.Token)));
            if (!value.RootElement.TryGetProperty("id", out var key) || key.GetString() != id || value.RootElement.GetProperty("kind").GetString() != "response") continue;
            if (value.RootElement.TryGetProperty("error", out var failure)) throw new InvalidOperationException(failure.ToString());
            return value.RootElement.GetProperty("payload").Clone();
        }
    }
    private static async Task Until(Func<Task<bool>> test)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (!await test()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Capture integration did not settle."); await Task.Delay(50); }
    }
    public async ValueTask DisposeAsync() { if (running is not null) await running; }
    private const string Adapter = """
        let frames=0,audioSamples=0,peak=0;
        async function read(track,audio){
          const reader=new MediaStreamTrackProcessor({track}).readable.getReader();
          try{for(;;){const next=await reader.read();if(next.done)break;const frame=next.value;
            try{if(audio){audioSamples+=frame.numberOfFrames;const data=new Float32Array(frame.numberOfFrames);
              frame.copyTo(data,{planeIndex:0,format:'f32-planar'});for(const n of data)peak=Math.max(peak,Math.abs(n));}
              else frames++;}finally{frame.close();}}}catch{}finally{reader.releaseLock();}
        }
        host.onRequest(async method=>{
          if(method==='consume'){frames=audioSamples=peak=0;const stream=await navigator.mediaDevices.getDisplayMedia({video:true,audio:true});
            for(const track of stream.getTracks())void read(track,track.kind==='audio');return {started:true};}
          return {frames,audioSamples,peak};
        });
        """;
}
