using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Uses Chromium's synthetic audio device in a fresh, intercepted test origin.
/// Never use this helper with an account profile or without its fake-device argument.</summary>
internal static class WebSessionMicrophoneValidation
{
    internal static async Task RunAsync(string evidenceDirectory,List<string> checks)
    {
        const string origin="https://microphone.fixture.invalid";
        var progressPath=Path.Combine(Path.GetFullPath(evidenceDirectory),"microphone-progress.json");
        var stages=new List<object>();
        string? primaryError=null;
        string? profile=null;
        int? namedProfileLength=null;
        void Progress(string stage)
        {
            stages.Add(new{stage,time=DateTimeOffset.UtcNow});
            File.WriteAllText(progressPath+".tmp",JsonSerializer.Serialize(new{stage,checks,stages,primaryError,profile,profileLength=profile?.Length,namedProfileLength,time=DateTimeOffset.UtcNow}));
            File.Move(progressPath+".tmp",progressPath,true);
        }
        Progress("starting synthetic microphone environment");
        if(BrowserSurface.FixturePage is null)throw new InvalidOperationException("Microphone validation requires intercepted synthetic documents.");
        profile=Path.Combine(Path.GetFullPath(evidenceDirectory),"profile");
        if(Directory.Exists(profile))throw new InvalidOperationException("Microphone validation requires a fresh profile.");
        var options=new CoreWebView2EnvironmentOptions{AdditionalBrowserArguments="--use-fake-device-for-media-stream"};
        if(options.AdditionalBrowserArguments!="--use-fake-device-for-media-stream")
            throw new InvalidOperationException("Real microphone access is forbidden in this fixture.");
        var environment=CoreWebView2Environment.CreateWithOptionsAsync(null,profile,options).AsTask();
        using var backing=new WebSessionBackgroundHost();
        await backing.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        Progress("synthetic background backing ready");
        var definition=new WebSessionDefinition("microphone","Synthetic microphone",WebSessionSource.Remote(origin+"/first"))
            {PageAdapter=new(){Mode=WebSessionPageAdapterMode.SessionBackend,ScriptAsset="microphone.js",Origins=new[]{origin}}};
        var bytes=Encoding.UTF8.GetBytes("globalThis.fixtureBindVideo=id=>host.setVideoTrack(id,fixtureTrack);host.onRequest(()=>({ready:true}));");
        var document=new WebBrowserDocument("microphone",1,definition.Source.Location,definition.AccessibleName)
            {Options=definition.Browser,InteractionMode=BrowserInteractionMode.ActivateToInteract};
        namedProfileLength=Path.Combine(profile,"EBWebView",BrowserProfileIdentity.Name("fixture.microphone",document)).Length;
        if(namedProfileLength>230)throw new InvalidOperationException("Choose a shorter evidence directory for the synthetic browser profile.");
        Progress("synthetic named profile path admitted");
        var browser=new BrowserSurface("fixture.microphone",document,()=>environment);
        var dispose=new TraceDisposal(browser,Progress);
        Exception? failure=null;
        try { await ExerciseAsync(); }
        catch(Exception error){failure=error;primaryError=error.ToString();Progress("primary microphone fixture failure");}
        finally { await dispose.DisposeAsync(); }
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(dispose.Failure is not null)throw new InvalidOperationException("Synthetic microphone teardown failed.",dispose.Failure);
        await CheckDisposalAsync();

        async Task CheckVideoViewAsync()
        {
            using var pin = new Shell.PinnedWidgetWindow("WidgetRail synthetic video pin");
            var target = new Microsoft.UI.Xaml.Controls.Grid();
            pin.SetContent(target);
            pin.Place(new(32,32,800,450));
            pin.Show();
            var reference = new WebSessionReference(Guid.NewGuid().ToString("N"));
            var videoDefinition = new WebSessionDefinition("video", "Synthetic video", WebSessionSource.BackendVideo(reference));
            var video = new BrowserSurface("fixture.microphone", document with { Id=reference.Id },
                () => environment, profileName: browser.ProfileName);
            try
            {
                video.ConfigureHostedSession(videoDefinition, []);
                target.Children.Add(video); video.PlacementHost=target; target.UpdateLayout();
                await video.StartBackendVideoAsync(browser, reference, default);
                video.SetPresentation(true,false,passivePin:true);
                const string draw = """
                    const canvas=document.createElement('canvas');canvas.width=1280;canvas.height=720;
                    const ctx=canvas.getContext('2d');let frame=0;
                    function draw(){
                      const step=frame++%64;
                      ctx.fillStyle='#182438';ctx.fillRect(0,0,1280,720);
                      for(let row=0;row<18;row++){
                        ctx.fillStyle=row%2?'#a9c5e2':'#eee';
                        ctx.font='16px sans-serif';ctx.fillText('Moving text and fine detail '.repeat(10),step*4-256,140+row*26);
                      }
                      ctx.fillStyle='#a9c5e2';ctx.fillRect(0,0,640,90);
                    }
                    draw();const timer=setInterval(draw,33);const stream=canvas.captureStream(30);
                    const main=document.createElement('video');main.muted=true;main.srcObject=stream;document.body.append(main);main.play();
                    window.fixtureGenerated=stream;window.fixtureMain=main;window.fixtureTimer=timer;
                    window.fixtureConnect=(async()=>{
                      const send=new RTCPeerConnection({iceServers:[]}),receive=new RTCPeerConnection({iceServers:[]});
                      window.fixturePeers=[send,receive];
                      const track=new Promise(resolve=>receive.ontrack=e=>resolve(e.track));
                      async function description(peer,value){
                        await peer.setLocalDescription(value);
                        for(let i=0;i<100&&peer.iceGatheringState!=='complete';i++)await new Promise(r=>setTimeout(r,25));
                        return peer.localDescription;
                      }
                      const sender=send.addTrack(stream.getVideoTracks()[0]);
                      await receive.setRemoteDescription(await description(send,await send.createOffer()));
                      await send.setRemoteDescription(await description(receive,await receive.createAnswer()));
                      const params=sender.getParameters();params.encodings[0].maxBitrate=12000000;
                      params.degradationPreference='maintain-resolution';await sender.setParameters(params);
                      const original=await track;
                      main.srcObject=new MediaStream([original]);await main.play();window.fixtureTrack=original;
                    })().catch(e=>window.fixtureError=e.name);
                    """;
                await browser.EvaluateFixtureAsync("(()=>{"+draw+"})()");
                var mediaDeadline=Environment.TickCount64+8000;
                while(await browser.EvaluateFixtureAsync("typeof fixtureTrack")!="\"object\""){
                    if(Environment.TickCount64>=mediaDeadline)throw new TimeoutException("Synthetic incoming WebRTC track unavailable: "+await browser.EvaluateFixtureAsync("window.fixtureError"));
                    await Task.Delay(50);
                }
                await browser.FixtureSetVideoTrackAsync(reference.Id);
                var timeout=Environment.TickCount64+6000;
                while (await video.EvaluateFixtureAsync("document.querySelector('video')?.videoWidth")!="1280")
                { if(Environment.TickCount64>=timeout)throw new TimeoutException("Direct video frames never reached the child."); await Task.Delay(35); }
                static string Sample(string element) => "(()=>{const v="+element+
                    ";const c=document.createElement('canvas');c.width=2;c.height=1;const g=c.getContext('2d');g.drawImage(v,10,10,1,1,0,0,1,1);g.drawImage(v,660,680,1,1,1,0,1,1);return [...g.getImageData(0,0,2,1).data]})()";
                var expected=await browser.EvaluateFixtureAsync(Sample("fixtureMain"));
                var actual=await video.EvaluateFixtureAsync(Sample("document.querySelector('video')"));
                Check(expected==actual && expected!="[0,0,0,0,0,0,0,0]", "Original video and passive pin render identical sampled colors");
                async Task<(int Total,int Dropped)> Counts(BrowserSurface surface,string element){
                    using var counts=JsonDocument.Parse(await surface.EvaluateFixtureAsync(
                        "(()=>{const q="+element+".getVideoPlaybackQuality();return [q.totalVideoFrames,q.droppedVideoFrames]})()"));
                    return (counts.RootElement[0].GetInt32(),counts.RootElement[1].GetInt32());
                }
                var sourceFirst=await Counts(browser,"fixtureMain");var pinFirst=await Counts(video,"document.querySelector('video')");
                await Task.Delay(BrowserSurface.HiddenMemoryDelay+TimeSpan.FromSeconds(3));
                var sourceLast=await Counts(browser,"fixtureMain");var pinLast=await Counts(video,"document.querySelector('video')");
                var sourceFrames=sourceLast.Total-sourceFirst.Total;var pinFrames=pinLast.Total-pinFirst.Total;
                var drops=pinLast.Dropped-pinFirst.Dropped;
                Check(sourceFrames>=200 && pinFrames>=sourceFrames*.95 && drops<=pinFrames*.05,
                    $"Passive 720p pin follows original cadence across the idle memory boundary: source={sourceFrames}, pin={pinFrames}, pin drops={drops}");
                await video.DisposeAsync();
                Check(!browser.HasActiveVideoOutput && await browser.EvaluateFixtureAsync("fixtureTrack.readyState")=="\"live\"",
                    "Closing the pin releases its media demand and preserves the original track");
                await browser.EvaluateFixtureAsync("clearInterval(fixtureTimer);fixturePeers.forEach(pc=>pc.close());fixtureGenerated.getTracks().forEach(t=>t.stop())");
            }
            finally { await video.DisposeAsync();target.Children.Remove(video); }
            var next = new BrowserSurface("fixture.microphone", document, () => environment, profileName:browser.ProfileName);
            try
            {
                next.ConfigureHostedSession(videoDefinition,[]);target.Children.Add(next);next.PlacementHost=target;target.UpdateLayout();
                await next.StartBackendVideoAsync(browser,new(Guid.NewGuid().ToString("N")),default);
                await browser.NavigateHostedAsync(origin+"/first",default);await Ready("/first");
                try { await next.HostedMessages.Completion;throw new InvalidOperationException("Source navigation retained its video view"); }
                catch(InvalidOperationException error) when(error.Message=="Video source document retired.") { }
                Check(!browser.HasActiveVideoOutput,"Source navigation closes only its prior document's video output");
            }
            finally { await next.DisposeAsync();target.Children.Remove(next); }
        }

        async Task CheckDisposalAsync()
        {
            var disposableBrowser = new BrowserSurface("fixture.dispose", document, () => environment);
            try
            {
                disposableBrowser.ConfigureHostedSession(definition, new[] { new BridgeWebSessionResource("microphone.js", "text/javascript", Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToBase64String(bytes)) });
                backing.Root.Children.Add(disposableBrowser);
                disposableBrowser.PlacementHost = backing.Root;
                backing.Root.UpdateLayout();
                await disposableBrowser.StartHostedSessionAsync(default);
                var deadline = Environment.TickCount64 + 15000;
                while (!disposableBrowser.FixturePageAdapterReady || disposableBrowser.IsNavigationInProgress)
                { if (Environment.TickCount64 > deadline) throw new TimeoutException("Disposal fixture did not become ready."); await Task.Delay(25); }
                using var lease = disposableBrowser.CaptureDocumentLease(default) ?? throw new InvalidOperationException("Disposal fixture must be ready.");
                var stopping = disposableBrowser.DisposeAsync().AsTask();
                Check(lease.Token.IsCancellationRequested && !lease.IsCurrent && disposableBrowser.CaptureDocumentLease(default) is null,
                    "Surface disposal cancels document work before awaiting cleanup and rejects new leases");
                await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                await disposableBrowser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                backing.Root.Children.Remove(disposableBrowser);
            }
        }

        async Task ExerciseAsync()
        {
        browser.ConfigureHostedSession(definition,new[]{new BridgeWebSessionResource("microphone.js","text/javascript",Convert.ToHexString(SHA256.HashData(bytes)),Convert.ToBase64String(bytes))});
        backing.Root.Children.Add(browser);
        browser.PlacementHost=backing.Root;
        backing.Root.UpdateLayout();
        await browser.StartHostedSessionAsync(default);
        Progress("synthetic browser initialized; waiting for document");
        await Ready("/first");
        await CheckVideoViewAsync();
        using var firstDocument = browser.CaptureDocumentLease(default) ?? throw new InvalidOperationException("Ready backend must grant a document lease.");
        Check(firstDocument.IsCurrent, "A backend document lease starts current");
        Progress("requesting synthetic microphone before consent");
        Check(await Probe()=="NotAllowedError","Hidden backend microphone is denied before explicit consent");
        Progress("reading profile decisions after transient denial");
        Check(!await HasSavedDecision(),"A transient hidden denial does not become a profile decision");
        var prompts=0;
        Task<string?> ChooseOnce(string _,CancellationToken token){token.ThrowIfCancellationRequested();prompts++;return Task.FromResult<string?>("once");}
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.ReceiveNotification = (_, notice) =>
        {
            try { notice.ReportShown(); received.TrySetResult(notice.Title); }
            finally { notice.ReportClosed(); notice.Release(); }
        };
        async Task<bool> SavedNotifications() =>
            (await browser.NativeProfile!.GetNonDefaultPermissionSettingsAsync()).Any(p =>
                p.PermissionKind == CoreWebView2PermissionKind.Notifications && BrowserSiteIdentity.Origin(p.PermissionOrigin) == origin);
        var temporary = await browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Notifications,
            (_, _) => Task.FromResult<string?>("once"), () => true, default);
        Check(temporary == new WebSessionPermissionResult(WebSessionPermissionState.Granted) && !await SavedNotifications() &&
            await browser.EvaluateFixtureAsync("Notification.permission") == "\"granted\"",
            "Temporary notification consent enables the document without writing a profile preference");
        await browser.NavigateHostedAsync(origin+"/notification-profile", default);
        await Ready("/notification-profile");
        var notification = await browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Notifications,
            (_, _) => Task.FromResult<string?>("allow"), () => true, default);
        Check(notification == new WebSessionPermissionResult(WebSessionPermissionState.Granted, true) && await SavedNotifications() &&
            await browser.EvaluateFixtureAsync("Notification.permission") == "\"granted\"",
            "Explicit notification consent reaches the hidden document without a reload");
        await browser.EvaluateFixtureAsync("new Notification('Synthetic backend message')");
        Check(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)) == "Synthetic backend message",
            "Hidden backend delivers the notification to the native host");
        Check(await Probe() == "NotAllowedError", "A notification grant cannot enable the microphone");
        Check((await browser.RequestBackendPermissionAsync((WebSessionPermissionKind)999,
            (_, _) => throw new InvalidOperationException("Unexpected permission prompt"), () => true, default)).State ==
            WebSessionPermissionState.Unavailable, "Unsupported backend permissions cannot open a chooser");
        Progress("requesting native synthetic allow-once");
        var allowed=await browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Microphone, ChooseOnce,()=>true,default).WaitAsync(TimeSpan.FromSeconds(8));
        Check(allowed.State==WebSessionPermissionState.Granted,"Native fake chooser grants the synthetic backend document");
        Check(await Probe()=="granted","A previous microphone denial recovers after explicit allow-once");
        Check(!await HasSavedDecision(),"Allow-once does not save microphone permission to the profile");
        await browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Microphone, ChooseOnce,()=>true,default).WaitAsync(TimeSpan.FromSeconds(8));
        Check(prompts==1,"An unchanged backend document reuses its session grant without another prompt");

        await browser.NavigateHostedAsync(origin+"/second",default);await Ready("/second");
        Check(firstDocument.Token.IsCancellationRequested && !firstDocument.IsCurrent, "Navigation cancels old document work without admitting the replacement document");
        using(var requestCancellation = new CancellationTokenSource())
        using(var requestLease = browser.CaptureDocumentLease(requestCancellation.Token)!)
        {
            requestCancellation.Cancel();
            Check(requestLease.Token.IsCancellationRequested && !requestLease.IsCurrent, "Request cancellation retires the document lease");
        }
        Progress("second document ready; testing cancellation");
        var answer=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var cancellation=new CancellationTokenSource())
        {
            var pending=browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Microphone, (_,_)=>{entered.TrySetResult();return answer.Task;},()=>true,cancellation.Token);
            if(await Task.WhenAny(entered.Task,pending).WaitAsync(TimeSpan.FromSeconds(5))==pending)
                throw new InvalidOperationException("Pending-choice fixture returned before opening: "+(await pending).State);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
            Check((await pending.WaitAsync(TimeSpan.FromSeconds(2))).State==WebSessionPermissionState.Cancelled,"Cancelling a pending native microphone decision returns promptly");
            answer.SetResult("allow");
        }
        Check(!await HasSavedDecision(),"Late fake consent cannot save a profile grant");
        Check((await browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Microphone, ChooseOnce,()=>false,default).WaitAsync(TimeSpan.FromSeconds(8))).State==WebSessionPermissionState.Unavailable,
            "A hidden backend cannot request consent without a current native widget owner");
        var remember=await browser.RequestBackendPermissionAsync(WebSessionPermissionKind.Microphone, (_,_)=>Task.FromResult<string?>("allow"),()=>true,default).WaitAsync(TimeSpan.FromSeconds(8));
        Check(remember.State==WebSessionPermissionState.Granted&&remember.Remembered&&!await HasSavedDecision(),
            "Remembered choice is deferred until actual microphone use");
        await browser.NavigateHostedAsync(origin+"/third",default);await Ready("/third");
        Check(await Probe()=="NotAllowedError"&&!await HasSavedDecision(),"Navigation retires an unused remembered grant before it can be persisted");
        using(var parked = browser.CaptureDocumentLease(default)!)
        {
            var parking = browser.ParkForProfileChangeAsync(default);
            Check(parked.Token.IsCancellationRequested && !parked.IsCurrent, "Profile change retires document work before waiting for navigation");
            await parking;
        }
        browser.RestoreAfterProfileChange(origin+"/fourth"); await Ready("/fourth");
        using(var crashed = browser.CaptureDocumentLease(default)!)
        {
            await browser.CrashRendererFixtureAsync();
            var deadline = Environment.TickCount64 + 5000;
            while(!crashed.Token.IsCancellationRequested && Environment.TickCount64 < deadline) await Task.Delay(25);
            Check(crashed.Token.IsCancellationRequested && !crashed.IsCurrent, "Renderer failure cancels active document work");
        }
        }

        async Task Ready(string path)
        {
            var deadline=Environment.TickCount64+15000;
            while(true)
            {
                if(browser.FixturePageAdapterReady)
                {
                    var documentState=await browser.EvaluateFixtureAsync("[location.pathname,document.readyState]");
                    using var parsed=JsonDocument.Parse(documentState);
                    if(parsed.RootElement.ValueKind==JsonValueKind.Array&&parsed.RootElement[0].GetString()==path&&
                        parsed.RootElement[1].GetString()=="complete"&&browser.FixturePageAdapterReady)return;
                }
                if(Environment.TickCount64>deadline)throw new TimeoutException("Synthetic microphone document did not become ready.");
                await Task.Delay(25);
            }
        }
        async Task<bool> HasSavedDecision()=> (await browser.NativeProfile!.GetNonDefaultPermissionSettingsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
            .Any(p=>p.PermissionKind==CoreWebView2PermissionKind.Microphone&&BrowserSiteIdentity.Origin(p.PermissionOrigin)==origin);
        async Task<string> Probe()
        {
            // This environment was constructed above with fake-device enabled;
            // fake-UI is deliberately absent so the real host permission callback runs.
            await browser.EvaluateFixtureAsync("globalThis.__micProbe='pending'; navigator.mediaDevices.getUserMedia({audio:true}).then(stream=>{stream.getTracks().forEach(track=>track.stop());globalThis.__micProbe='granted';}).catch(error=>globalThis.__micProbe=error.name);");
            var deadline=Environment.TickCount64+10000;
            while(true)
            {
                var result=JsonSerializer.Deserialize<string>(await browser.EvaluateFixtureAsync("globalThis.__micProbe"));
                if(result is not null and not "pending")return result;
                if(Environment.TickCount64>deadline)throw new TimeoutException("Synthetic microphone request did not complete.");
                await Task.Delay(25);
            }
        }
        void Check(bool success,string name){if(!success){Progress("FAILED: "+name);throw new InvalidOperationException(name);}checks.Add(name);Progress(name);}
    }

    private sealed class TraceDisposal(BrowserSurface browser,Action<string> progress):IAsyncDisposable
    {
        internal Exception? Failure { get; private set; }
        public async ValueTask DisposeAsync()
        {
            progress("disposing synthetic microphone browser");
            try { await browser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch(Exception error){Failure=error;progress("microphone teardown failed: "+error.GetType().Name);return;}
            progress("synthetic microphone browser disposed");
        }
    }
}
