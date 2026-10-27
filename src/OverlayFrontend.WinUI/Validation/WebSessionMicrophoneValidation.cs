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
        var bytes=Encoding.UTF8.GetBytes("host.onRequest(()=>({ready:true}));");
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
        using var firstDocument = browser.CaptureDocumentLease(default) ?? throw new InvalidOperationException("Ready backend must grant a document lease.");
        Check(firstDocument.IsCurrent, "A backend document lease starts current");
        Progress("requesting synthetic microphone before consent");
        Check(await Probe()=="NotAllowedError","Hidden backend microphone is denied before explicit consent");
        Progress("reading profile decisions after transient denial");
        Check(!await HasSavedDecision(),"A transient hidden denial does not become a profile decision");
        var prompts=0;
        Task<string?> ChooseOnce(string _,CancellationToken token){token.ThrowIfCancellationRequested();prompts++;return Task.FromResult<string?>("once");}
        Progress("requesting native synthetic allow-once");
        var allowed=await browser.RequestBackendMicrophoneAsync(ChooseOnce,()=>true,default).WaitAsync(TimeSpan.FromSeconds(8));
        Check(allowed.State==WebSessionMicrophoneState.Granted,"Native fake chooser grants the synthetic backend document");
        Check(await Probe()=="granted","A previous microphone denial recovers after explicit allow-once");
        Check(!await HasSavedDecision(),"Allow-once does not save microphone permission to the profile");
        await browser.RequestBackendMicrophoneAsync(ChooseOnce,()=>true,default).WaitAsync(TimeSpan.FromSeconds(8));
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
            var pending=browser.RequestBackendMicrophoneAsync((_,_)=>{entered.TrySetResult();return answer.Task;},()=>true,cancellation.Token);
            if(await Task.WhenAny(entered.Task,pending).WaitAsync(TimeSpan.FromSeconds(5))==pending)
                throw new InvalidOperationException("Pending-choice fixture returned before opening: "+(await pending).State);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();
            Check((await pending.WaitAsync(TimeSpan.FromSeconds(2))).State==WebSessionMicrophoneState.Cancelled,"Cancelling a pending native microphone decision returns promptly");
            answer.SetResult("allow");
        }
        Check(!await HasSavedDecision(),"Late fake consent cannot save a profile grant");
        Check((await browser.RequestBackendMicrophoneAsync(ChooseOnce,()=>false,default).WaitAsync(TimeSpan.FromSeconds(8))).State==WebSessionMicrophoneState.Unavailable,
            "A hidden backend cannot request consent without a current native widget owner");
        var remember=await browser.RequestBackendMicrophoneAsync((_,_)=>Task.FromResult<string?>("allow"),()=>true,default).WaitAsync(TimeSpan.FromSeconds(8));
        Check(remember.State==WebSessionMicrophoneState.Granted&&remember.Remembered&&!await HasSavedDecision(),
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
