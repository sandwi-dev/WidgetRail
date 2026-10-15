using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Chromium fake devices and an in-page loopback only: no accounts, real devices, STUN or TURN.</summary>
internal sealed partial class WebAppBackgroundValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    private readonly Grid active = new();
    private readonly Grid parking = new() { Visibility = Visibility.Collapsed };
    internal WebAppBackgroundValidationPage(string result, string profileRoot)
    {
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Background fixture</title><body>Local fake-device loopback fixture</body>";
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--use-fake-device-for-media-stream" };
        browser = new("fixture.background", new("background", 1, "https://background.fixture.invalid/", "Background fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, options).AsTask())
        { IsInteractionFocused = () => true };
        active.Children.Add(browser);
        Content = new Grid { Children = { active, parking } };
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; var permissions = new List<string>();
            try
            {
                browser.Choose = (_, _, message, _, _, _, _) =>
                {
                    if (!message.Contains("https://background.fixture.invalid", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected permission origin.");
                    permissions.Add(message.Contains("microphone", StringComparison.Ordinal) ? "microphone" : message.Contains("camera", StringComparison.Ordinal) ? "camera" : "other");
                    return Task.FromResult<string?>("once");
                };
                browser.SetPresentation(true, true);
                await Until(() => browser.IsReady && !browser.IsNavigationInProgress && browser.CurrentTitle == "Background fixture");
                await browser.EvaluateGestureFixtureAsync("""
                    window.state='starting';
                    (async()=>{
                      window.stream=await navigator.mediaDevices.getUserMedia({audio:true,video:true});
                      if(!stream.getTracks().every(t=>t.label.toLowerCase().includes('fake'))){stream.getTracks().forEach(t=>t.stop());throw new Error('NonFakeDevice');}
                      window.fake=true;window.p1=new RTCPeerConnection({iceServers:[]});window.p2=new RTCPeerConnection({iceServers:[]});
                      stream.getTracks().forEach(t=>p1.addTrack(t,stream));
                      const gathered=pc=>pc.iceGatheringState==='complete'?Promise.resolve():new Promise(r=>pc.addEventListener('icegatheringstatechange',()=>{if(pc.iceGatheringState==='complete')r();}));
                      await p1.setLocalDescription(await p1.createOffer());await gathered(p1);
                      await p2.setRemoteDescription(p1.localDescription);await p2.setLocalDescription(await p2.createAnswer());await gathered(p2);
                      await p1.setRemoteDescription(p2.localDescription);
                      window.readBytes=async()=>{let n=0;for(const s of (await p1.getStats()).values())if(s.type==='outbound-rtp')n+=s.bytesSent||0;window.sent=n;};
                      window.state='ready';
                    })().catch(e=>window.state='error:'+e.name);
                    """);
                await UntilScript("window.state==='ready' && p1.connectionState==='connected' && p2.connectionState==='connected'");
                Check(await Eval("window.fake && stream.getTracks().every(t=>t.readyState==='live')"), "Only fake audio/video devices are active");
                Check(permissions.Contains("microphone") && permissions.Contains("camera"), "Both native device permissions pass through host consent");
                await browser.EvaluateGestureFixtureAsync("readBytes()");
                await UntilScript("window.sent>0");
                var before = long.Parse(await browser.EvaluateFixtureAsync("window.sent"), System.Globalization.CultureInfo.InvariantCulture);
                browser.SetPresentation(false, false);
                active.Children.Remove(browser); parking.Children.Add(browser);
                await Task.Delay(1800);
                await browser.EvaluateGestureFixtureAsync("readBytes()");
                await UntilScript("window.sent>" + before.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Check(await Eval("document.visibilityState==='hidden' && stream.getTracks().every(t=>t.readyState==='live') && p1.connectionState==='connected'"), "Media continues while native surface is hidden and parked");
                parking.Children.Remove(browser); active.Children.Add(browser); browser.SetPresentation(true, true);
                Check(await Eval("p1.connectionState==='connected'"), "Returning to the page preserves the same connection");
                if (Environment.GetCommandLineArgs().Contains("--webapp-memory"))
                {
                    await WebAppMemoryValidation.RunAsync(browser, "stream", result, Check);
                    await WebAppMemoryValidation.VerifyPolicyAsync(browser, Check);
                }
                await browser.EvaluateGestureFixtureAsync("window.onbeforeunload=()=> 'Active session'");
                await browser.StopDocumentAsync(CancellationToken.None);
                Check(browser.SessionStopped && browser.CurrentUrl == WebBrowserDocument.StartPage && await Eval("typeof window.stream==='undefined'"), "Stop unloads the document, including its media and connections");
                browser.RestartStoppedDocument();
                await Until(() => !browser.SessionStopped && !browser.IsNavigationInProgress && browser.CurrentTitle == "Background fixture");
                Check(await Eval("typeof window.stream==='undefined'"), "Restart opens a fresh document without test devices");
                Check(!BrowserSurface.RequiresPageRecovery(CoreWebView2ProcessFailedKind.GpuProcessExited), "A recoverable GPU-process failure does not terminate the page session");
                var failed = false;
                browser.ActivityChanged = () => failed = browser.BackgroundFaulted;
                await browser.CrashRendererFixtureAsync();
                await Until(() => failed && browser.NeedsRecovery);
                Check(!browser.KeepsBackgroundSession, "Renderer crash produces an explicit recovery state and releases background eligibility");
            }
            catch (Exception failure) { error = failure.ToString(); }
            await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, permissions, error }));
            async Task<bool> Eval(string script) => await browser.EvaluateFixtureAsync(script) == "true";
            async Task UntilScript(string script)
            {
                var end = DateTime.UtcNow.AddSeconds(20);
                while (!await Eval(script))
                { if (DateTime.UtcNow > end) throw new TimeoutException("Loopback fixture timed out; state=" + await browser.EvaluateFixtureAsync("window.state")); await Task.Delay(50); }
            }
            void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
        };
    }
    private static async Task Until(Func<bool> ready)
    {
        var end = DateTime.UtcNow.AddSeconds(20);
        while (!ready()) { if (DateTime.UtcNow > end) throw new TimeoutException("Background fixture did not load."); await Task.Delay(50); }
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
