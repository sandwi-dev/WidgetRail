// Opt-in renderer-stall characterization. Default capture qualification is unchanged.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WindowsAppLibraryProvider;

namespace WidgetRail.OverlayFrontend.WinUI.Validation
{
    internal sealed partial class WebSessionCaptureMediaValidationPage
    {
        private static async Task ExerciseRendererStallsAsync(string output,List<string> checks,List<object> samples)
        {
            var sourceExe=Path.Combine(AppContext.BaseDirectory,"WindowCaptureFixture.exe");
            if(!File.Exists(sourceExe))throw new InvalidOperationException("Stage the generated capture source beside the candidate.");
            using var backing=new WebSessionBackgroundHost();
            using var source=Process.Start(new ProcessStartInfo(sourceExe,"--large --audio"){UseShellExecute=false})
                ??throw new InvalidOperationException("Generated source did not start.");
            try
            {
                await File.WriteAllTextAsync(Path.Combine(output,"source-process.json"),JsonSerializer.Serialize(new
                    {pid=source.Id,createdUtcTicks=source.StartTime.ToUniversalTime().Ticks}));
                await backing.Ready.WaitAsync(TimeSpan.FromSeconds(10));
                const string origin="https://capture-stall.fixture.invalid";
                BrowserSurface.FixturePage=_=>"<!doctype html><title>Generated renderer stall</title><body>Synthetic source only.</body>";
                var environment=CoreWebView2Environment.CreateWithOptionsAsync(null,Path.Combine(output,"profile"),new()).AsTask();
                var definition=new WebSessionDefinition("capture","Synthetic capture stall",WebSessionSource.Remote(origin+"/test"))
                {NativeCapture=true,PageAdapter=new(){ScriptAsset="capture.js",Origins=(string[])[origin],Mode=WebSessionPageAdapterMode.SessionBackend}};
                var script=Encoding.UTF8.GetBytes(Adapter);
                var windows=new WindowsTaskWindowCatalog();NativeWindowPreviewTarget? target=null;
                await Until(async()=>{target=(await windows.GetWindowsAsync(default)).FirstOrDefault(w=>w.PreviewTarget?.ProcessId==source.Id)?.PreviewTarget;return target is not null;});
                var caseNumber=0;
                foreach(var audio in new[]{false,true})foreach(var milliseconds in new[]{1500,4000})
                {
                    var name=$"case-{++caseNumber}";
                    var browser=new BrowserSurface("fixture.capture-stall",new(name,1,definition.Source.Location,definition.AccessibleName)
                        {Options=definition.Browser,InteractionMode=BrowserInteractionMode.ActivateToInteract},()=>environment);
                    WebCaptureStreamStatus? last=null;
                    browser.HostedCaptureChanged+=status=>last=status;
                    var ticker=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
                    var previousTick=Stopwatch.GetTimestamp();double maximumUiGapMs=0;var uiTicks=0;
                    ticker.Tick+=(_,_)=>{var now=Stopwatch.GetTimestamp();maximumUiGapMs=Math.Max(maximumUiGapMs,Stopwatch.GetElapsedTime(previousTick,now).TotalMilliseconds);previousTick=now;uiTicks++;};
                    try
                    {
                        browser.ConfigureHostedSession(definition,(BridgeWebSessionResource[])[new BridgeWebSessionResource("capture.js","text/javascript",Convert.ToHexString(SHA256.HashData(script)),Convert.ToBase64String(script))]);
                        backing.Root.Children.Add(browser);browser.PlacementHost=backing.Root;backing.Root.UpdateLayout();
                        await browser.StartHostedSessionAsync(default);await WaitForCaptureDocumentAsync(browser,definition.Source.Location);await Request(browser,"status");
                        var ticket=new WebCaptureStreamTicket(new(Guid.NewGuid().ToString("N")),Guid.NewGuid().ToString("N"));
                        var prepared=await browser.PrepareHostedCaptureAsync(ticket,new(),_=>Task.FromResult<WebCaptureSourceSelection?>(new(target!,"Generated fixture",audio,new())),default);
                        if(prepared.Phase!=WebCaptureStreamPhase.Prepared)throw new InvalidOperationException("Synthetic preparation did not succeed.");
                        await Request(browser,"consume");
                        await Until(()=>Task.FromResult(last?.Ticket==ticket&&last.Phase==WebCaptureStreamPhase.Capturing));
                        await Task.Delay(500);
                        var readStatistics=browser.CaptureStatisticsReaderForStallValidation();
                        var before=JsonSerializer.SerializeToElement(readStatistics());
                        using var child=Process.GetProcessById(checked((int)before.GetProperty("processId").GetUInt32()));
                        _=child.Handle; // Bind the exact incarnation before observing exit.
                        previousTick=Stopwatch.GetTimestamp();ticker.Start();
                        // Stall inside the next real keep call, not at an arbitrary timer phase.
                        // Original keep is restored before blocking and never invokes a provider action.
                        await browser.EvaluateFixtureAsync("(()=>{const api=WidgetRailNativeMedia;const keep=api.keep;globalThis.fixtureStall={entered:false};api.keep=function(id){api.keep=keep;fixtureStall.entered=true;fixtureStall.started=performance.now();const end=performance.now()+"+milliseconds+";while(performance.now()<end){}fixtureStall.ended=performance.now();return keep.call(this,id);};return true;})()");
                        await Task.Delay(milliseconds+4000);
                        ticker.Stop();
                        using var marker=JsonDocument.Parse(await browser.EvaluateFixtureAsync("JSON.stringify(globalThis.fixtureStall)"));
                        using var timing=JsonDocument.Parse(marker.RootElement.GetString()??"null");
                        if(timing.RootElement.ValueKind!=JsonValueKind.Object||!timing.RootElement.GetProperty("entered").GetBoolean())
                            throw new InvalidOperationException("The next native heartbeat did not enter the synthetic stall.");
                        var observedMs=timing.RootElement.GetProperty("ended").GetDouble()-timing.RootElement.GetProperty("started").GetDouble();
                        if(observedMs<milliseconds-10)throw new InvalidOperationException("Renderer stall duration evidence is incomplete.");
                        var hostResponsive=uiTicks>=4&&maximumUiGapMs<750;
                        var recoveryBefore=await Request(browser,"status");var recoveryStarted=Stopwatch.GetTimestamp();
                        await Task.Delay(1000);
                        var recoveryAfter=await Request(browser,"status");
                        var recoverySeconds=Stopwatch.GetElapsedTime(recoveryStarted).TotalSeconds;
                        var recoveredVideo=recoveryAfter.GetProperty("frames").GetInt64()-recoveryBefore.GetProperty("frames").GetInt64();
                        var recoveredAudio=recoveryAfter.GetProperty("audioSamples").GetInt64()-recoveryBefore.GetProperty("audioSamples").GetInt64();
                        var after=JsonSerializer.SerializeToElement(readStatistics());
                        var outcome=!hostResponsive?"inconclusive_host_stall":last?.Phase==WebCaptureStreamPhase.Stopped?"terminated":last?.Phase==WebCaptureStreamPhase.Capturing?"survived":"inconclusive";
                        if(outcome=="survived"&&(child.HasExited||recoveredVideo<15||audio&&recoveredAudio<24000))
                            throw new InvalidOperationException("Capturing phase did not resume actual audio/video delivery after the stall.");
                        samples.Add(new{phase="renderer_stall",audio,requestedStallMs=milliseconds,observedStallMs=observedMs,uiTicks,maximumUiGapMs,hostResponsive,outcome,recoverySeconds,recoveredVideoFrames=recoveredVideo,recoveredAudioFrames=recoveredAudio,
                            recoveryVideoFps=recoveredVideo/recoverySeconds,
                            terminalReason=last?.Reason,nativeReason=after.GetProperty("reason"),nativeResult=after.GetProperty("result"),
                            submittedPackets=after.GetProperty("submitted"),droppedVideo=after.GetProperty("dropped"),workerExited=after.GetProperty("workerExited"),
                            forcedTermination=after.GetProperty("forcedTermination"),processGone=child.HasExited});
                        checks.Add($"Observed {milliseconds}ms renderer stall with application audio {(audio?"on":"off")} with host-responsive={hostResponsive}");
                        if(outcome=="survived")checks.Add($"Real video{(audio?" and application audio":"")} resumed after {milliseconds}ms renderer stall");
                        // Termination during these cases is an observation of current policy,
                        // not a regression assertion that the default suite should fail.
                        var stopped=await browser.StopHostedCaptureAsync(ticket);
                        if(stopped.Phase!=WebCaptureStreamPhase.Stopped||!child.HasExited)throw new InvalidOperationException("Fixture capture teardown was not confirmed.");
                    }
                    finally{ticker.Stop();await browser.DisposeAsync();backing.Root.Children.Remove(browser);}
                }
            }
            finally{if(!source.HasExited){source.Kill();await source.WaitForExitAsync();}}
        }
    }
}
