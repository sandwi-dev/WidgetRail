using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.OverlayFrontend.WinUI.Browser.Capture;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Negative capture admission/lifetime checks. Never opens a native source or user window.</summary>
internal static class WebSessionCaptureValidation
{
    internal static async Task RunAsync(string output, List<string> checks)
    {
        if (BrowserSurface.FixturePage is null) throw new InvalidOperationException("Capture validation requires intercepted synthetic pages.");
        const string origin = "https://capture.fixture.invalid";
        var profile = Path.Combine(Path.GetFullPath(output), "cap");
        if (Directory.Exists(profile)) throw new InvalidOperationException("Capture validation requires a fresh profile.");
        var environment = CoreWebView2Environment.CreateWithOptionsAsync(null, profile, new()).AsTask();
        var script = Encoding.UTF8.GetBytes("host.onRequest(()=>({ready:true}));");
        var resource = new BridgeWebSessionResource("capture.js", "text/javascript", Convert.ToHexString(SHA256.HashData(script)), Convert.ToBase64String(script));
        var definition = new WebSessionDefinition("capture", "Synthetic capture", WebSessionSource.Remote(origin + "/first"))
        { PageAdapter = new() { Mode = WebSessionPageAdapterMode.SessionBackend, ScriptAsset = "capture.js", Origins = new[] { origin } } };
        using var backing = new WebSessionBackgroundHost();
        await backing.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        var ordinary = await OpenAsync(definition, "fixture.cap-off");
        try
        {
            Check(LiveCaptureQuality.Supported.Count==12&&LiveCaptureQuality.Default.Options==new WebCaptureStreamOptions(720,30)&&
                LiveCaptureQuality.Allowed(null).Select(quality=>quality.Options).SequenceEqual(WebCaptureStreamOptions.LegacyQualities),
                "Transport exposes twelve bounded qualities but legacy requests retain four and default720p30");
            var restricted=(WebCaptureStreamOptions[])[new(1080,60),new(720,30),new(2160,60)];
            Check(LiveCaptureQuality.Allowed(restricted).Select(quality=>quality.Options).ToHashSet().SetEquals(new[]{new WebCaptureStreamOptions(1080,60),new(720,30)}),
                "Chooser intersects provider restrictions with transport support without inventing provider entitlement");
            Check(await ordinary.EvaluateFixtureAsync("typeof globalThis.WidgetRailNativeMedia") == "\"undefined\"",
                "Ordinary backend sessions do not install native capture hooks");
            var prompted = false;
            var unavailable = await ordinary.PrepareHostedCaptureAsync(Ticket(), new(), _ =>
            { prompted = true; return Task.FromResult<WebCaptureSourceSelection?>(null); }, default);
            Check(unavailable.Phase == WebCaptureStreamPhase.Unavailable && !prompted,
                "A session without capture opt-in cannot open the native source chooser");
        }
        finally { await ordinary.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); backing.Root.Children.Remove(ordinary); }
        var browser = await OpenAsync(definition with { NativeCapture = true }, "fixture.cap");
        var published = new List<WebCaptureStreamPhase>();
        browser.HostedCaptureChanged += status => published.Add(status.Phase);
        try
        {
            var qualityPrompted=false;
            var legacyHigh=await browser.PrepareHostedCaptureAsync(Ticket(),new(1440,60),_=>
                {qualityPrompted=true;return Task.FromResult<WebCaptureSourceSelection?>(null);},default);
            Check(legacyHigh.Phase==WebCaptureStreamPhase.Unavailable&&!qualityPrompted,"Legacy capture cannot silently expose higher-quality selection");
            var rejected=await browser.PrepareHostedCaptureAsync(Ticket(),new(),_=>Task.FromResult<WebCaptureSourceSelection?>(
                new(new("1",1,"1","SyntheticUnopenedWindow"),"Synthetic",false,new(1440,60))),default,(WebCaptureStreamOptions[])[new(720,30)]);
            Check(rejected.Phase==WebCaptureStreamPhase.Failed&&rejected.Reason=="quality_not_allowed"&&await NoMedia(),
                "A chooser result outside the admitted quality set is rejected before worker/source startup");
            var wrongContent=await browser.PrepareHostedCaptureAsync(Ticket(),new(),_=>Task.FromResult<WebCaptureSourceSelection?>(
                new(new("1",1,"1","SyntheticUnopenedWindow"),"Synthetic",false,new(){ContentPreference=WebCaptureContentPreference.Motion})),
                default,(WebCaptureStreamOptions[])[new(720,30)],fixedContentPreference:true);
            Check(wrongContent.Phase==WebCaptureStreamPhase.Failed&&wrongContent.Reason=="quality_not_allowed"&&!browser.HasHostedCaptureActivity,
                "Fixed content policy rejects a mismatched chooser response before native capture or activity begins");
            Check(await browser.EvaluateFixtureAsync("typeof globalThis.WidgetRailNativeMedia") == "\"object\"",
                "Explicit native-capture backend installs the media hook");
            await browser.EvaluateFixtureAsync("globalThis.captureProbe='pending';navigator.mediaDevices.getDisplayMedia({video:true}).then(s=>{s.getTracks().forEach(t=>t.stop());globalThis.captureProbe='unexpected';},e=>globalThis.captureProbe=e.name+':'+e.message);");
            await Until(async () => JsonSerializer.Deserialize<string>(await browser.EvaluateFixtureAsync("globalThis.captureProbe")) != "pending");
            Check(JsonSerializer.Deserialize<string>(await browser.EvaluateFixtureAsync("globalThis.captureProbe")) ==
                "NotAllowedError:Choose an application in WidgetRail first.", "Display media before native source consent rejects without a fallback chooser");

            var cancelled = await browser.PrepareHostedCaptureAsync(Ticket(), new(), _ => Task.FromResult<WebCaptureSourceSelection?>(null), default);
            Check(cancelled.Phase == WebCaptureStreamPhase.Cancelled && await NoMedia(), "Cancelled source selection creates no media stream");

            var navigation = PendingChoice();
            var navigationRequest = browser.PrepareHostedCaptureAsync(Ticket(), new(), navigation.Choose, default);
            await navigation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await browser.NavigateHostedAsync(origin + "/second", default);
            var navigationResult = await navigationRequest.WaitAsync(TimeSpan.FromSeconds(5));
            await Ready(browser, "/second");
            Check(navigationResult.Phase == WebCaptureStreamPhase.Cancelled && await NoMedia(), "Navigation cancels pending capture consent without preparing a replacement document");

            var stoppedChoice = PendingChoice();
            var stopTicket = Ticket();
            var stoppedRequest = browser.PrepareHostedCaptureAsync(stopTicket, new(), stoppedChoice.Choose, default);
            await stoppedChoice.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopResult = await browser.StopHostedCaptureAsync(stopTicket).WaitAsync(TimeSpan.FromSeconds(5));
            Check(stopResult.Phase == WebCaptureStreamPhase.Stopped &&
                (await stoppedRequest.WaitAsync(TimeSpan.FromSeconds(5))).Phase == WebCaptureStreamPhase.Cancelled && await NoMedia(),
                "Explicit stop cancels pending source consent and leaves no prepared media");

            var disposal = PendingChoice();
            var disposalRequest = browser.PrepareHostedCaptureAsync(Ticket(), new(), disposal.Choose, default);
            await disposal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = browser.DisposeAsync().AsTask();
            Check((await disposalRequest.WaitAsync(TimeSpan.FromSeconds(5))).Phase == WebCaptureStreamPhase.Cancelled,
                "Surface disposal cancels pending capture consent before browser teardown");
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            Check(!published.Any(phase => phase is WebCaptureStreamPhase.Prepared or WebCaptureStreamPhase.Capturing),
                "Denied and retired source choices never publish prepared or capturing state");
        }
        finally { await browser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); backing.Root.Children.Remove(browser); }
        await VerifyUnconfirmedStopAsync();

        async Task VerifyUnconfirmedStopAsync()
        {
            var stoppingBrowser = await OpenAsync(definition with { NativeCapture = true }, "fixture.cap-retry");
            var observed = new List<WebCaptureStreamPhase>();
            stoppingBrowser.HostedCaptureChanged += status => observed.Add(status.Phase);
            try
            {
                var evaluate = stoppingBrowser.CaptureDocumentEvaluatorForValidation();
                _ = await evaluate();
                using var documentLease = stoppingBrowser.CaptureDocumentLease(default)!;
                stoppingBrowser.FixtureBeforeCaptureStop = () => Task.FromException(new IOException("Synthetic unconfirmed worker exit."));
                var choice = PendingChoice();
                var ticket = Ticket();
                var preparing = stoppingBrowser.PrepareHostedCaptureAsync(ticket, new(), choice.Choose, default);
                await choice.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var disposalFailed = false;
                try { await stoppingBrowser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (InvalidOperationException) { disposalFailed = true; }
                try { await preparing.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (InvalidOperationException) { /* Capture cleanup remains unconfirmed, including preparation cancellation. */ }
                var microphone = await stoppingBrowser.RequestBackendPermissionAsync(WebSessionPermissionKind.Microphone, (_, _) =>
                    throw new InvalidOperationException("A retired browser must not reopen microphone consent."), () => true, default);
                Check(disposalFailed && documentLease.Token.IsCancellationRequested && !documentLease.IsCurrent &&
                    !stoppingBrowser.IsReady && stoppingBrowser.CaptureDocumentLease(default) is null && microphone.State == WebSessionPermissionState.Unavailable,
                    "Unconfirmed capture stop still revokes browser activity and document authority");
                var controllerClosed = false;
                try { await evaluate(); }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException)
                { controllerClosed = true; }
                Check(controllerClosed, "Real WebView controller is closed even when native capture exit is unconfirmed");
                Check(stoppingBrowser.FixtureRetainedCaptureTicket == ticket && !observed.Contains(WebCaptureStreamPhase.Stopped),
                    "Unconfirmed native cleanup retains the exact ticket without publishing stopped");
                stoppingBrowser.FixtureBeforeCaptureStop = null;
                var stopped = await stoppingBrowser.StopHostedCaptureAsync(ticket).WaitAsync(TimeSpan.FromSeconds(5));
                await stoppingBrowser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                Check(stopped.Phase == WebCaptureStreamPhase.Stopped && stopped.Ticket == ticket &&
                    stoppingBrowser.FixtureRetainedCaptureTicket is null && !stoppingBrowser.IsReady,
                    "Exact-ticket cleanup can succeed after browser closure without reviving its document");
            }
            finally
            {
                stoppingBrowser.FixtureBeforeCaptureStop = null;
                await stoppingBrowser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                backing.Root.Children.Remove(stoppingBrowser);
            }
        }

        async Task<bool> NoMedia() => await browser.EvaluateFixtureAsync("globalThis.WidgetRailNativeMedia.status().active") == "false";
        async Task<BrowserSurface> OpenAsync(WebSessionDefinition session, string widget)
        {
            var document = new WebBrowserDocument("capture", 1, session.Source.Location, session.AccessibleName)
            { Options = session.Browser, InteractionMode = BrowserInteractionMode.ActivateToInteract };
            var surface = new BrowserSurface(widget, document, () => environment);
            try
            {
                surface.ConfigureHostedSession(session, new[] { resource });
                backing.Root.Children.Add(surface); surface.PlacementHost = backing.Root; backing.Root.UpdateLayout();
                await surface.StartHostedSessionAsync(default); await Ready(surface, "/first");
                return surface;
            }
            catch { await surface.DisposeAsync(); backing.Root.Children.Remove(surface); throw; }
        }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
    }
    private static WebCaptureStreamTicket Ticket() => new(new(Guid.NewGuid().ToString("N")), Guid.NewGuid().ToString("N"));
    private static (TaskCompletionSource Entered, Func<CancellationToken, Task<WebCaptureSourceSelection?>> Choose) PendingChoice()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return (entered, async token => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return null; });
    }
    private static async Task Ready(BrowserSurface surface, string path) => await Until(async () =>
        surface.FixturePageAdapterReady && !surface.IsNavigationInProgress &&
        JsonSerializer.Deserialize<string>(await surface.EvaluateFixtureAsync("location.pathname")) == path);
    private static async Task Until(Func<Task<bool>> ready)
    {
        var deadline = Environment.TickCount64 + 15000;
        while (!await ready()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Synthetic capture document did not become ready."); await Task.Delay(25); }
    }
}
