using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>One synthetic notification and cancelled display requests. No display is captured.</summary>
internal sealed partial class WebAppCapabilitiesValidationPage : Page, IAsyncDisposable
{
    private readonly BrowserSurface browser;
    internal WebAppCapabilitiesValidationPage(string result, string profileRoot)
    {
        Task<CoreWebView2Environment>? environment = null;
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Capability fixture</title><body>Local capability fixture</body>";
        browser = new("fixture.capabilities", new("capabilities", 1, "https://capabilities.fixture.invalid/", "Capability fixture")
        { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = WebBrowserOptions.WebApp },
            () => environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, profileRoot, null).AsTask())
        { IsInteractionFocused = () => true };
        Content = browser;
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null; string? notification = null; var screenPrompts = 0;
            try
            {
                browser.Choose = (_, title, message, _, _, _) =>
                {
                    if (!message.Contains("https://capabilities.fixture.invalid", StringComparison.Ordinal)) throw new InvalidOperationException("Missing capability origin.");
                    if (title == "Share your screen?") { screenPrompts++; return Task.FromResult<string?>(null); }
                    return Task.FromResult<string?>("once");
                };
                browser.SetPresentation(true, true);
                await Until("document.title==='Capability fixture'");
                await browser.EvaluateGestureFixtureAsync("window.screenResult='pending';navigator.mediaDevices.getDisplayMedia({video:true}).then(s=>{s.getTracks().forEach(t=>t.stop());window.screenResult='unexpected-stream';},e=>window.screenResult=e.name)");
                await Until("window.screenResult!=='pending'");
                Check(screenPrompts == 1 && await Eval("window.screenResult==='NotAllowedError'"), "Native screen-share request identifies widget/origin and respects cancellation");
                browser.SetPresentation(false, false);
                await browser.EvaluateGestureFixtureAsync("window.screenResult='pending';navigator.mediaDevices.getDisplayMedia({video:true}).then(s=>{s.getTracks().forEach(t=>t.stop());window.screenResult='unexpected-stream';},e=>window.screenResult=e.name)");
                await Until("window.screenResult!=='pending'");
                Check(screenPrompts == 1 && await Eval("window.screenResult!=='unexpected-stream'"), "Hidden page cannot summon screen-share consent");
                browser.SetPresentation(true, true);
                await browser.EvaluateGestureFixtureAsync("window.permissionResult='pending';Notification.requestPermission().then(p=>window.permissionResult=p)");
                await Until("window.permissionResult==='granted'");
                await browser.EvaluateFixtureAsync("window.notificationResult='pending';window.notice=new Notification('WidgetRail local test',{body:'Synthetic webapp notification. No account data.'});notice.onshow=()=>{window.notificationResult='shown';notice.close();};notice.onerror=()=>window.notificationResult='error';");
                await Until("window.notificationResult!=='pending'");
                notification = await browser.EvaluateFixtureAsync("window.notificationResult");
                Check(notification == "\"shown\"", "Default WebView2 notification path reports native display to the website");
                var prompting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var cancelled = false;
                browser.Choose = async (_, _, _, _, token, _) =>
                {
                    prompting.TrySetResult();
                    try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { cancelled = true; throw; }
                    return null;
                };
                await browser.EvaluateGestureFixtureAsync("navigator.mediaDevices.getDisplayMedia({video:true}).catch(()=>{})");
                await prompting.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await browser.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
                Check(cancelled, "Disposal cancels and drains pending screen-share consent before closing the controller");
            }
            catch (Exception failure) { error = failure.ToString(); }
            await DisposeAsync();
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = error is null, checks, screenPrompts, notification, error }));
            async Task<bool> Eval(string script) => browser.IsReady && await browser.EvaluateFixtureAsync(script) == "true";
            async Task Until(string script)
            {
                var end = DateTime.UtcNow.AddSeconds(15);
                while (!await Eval(script))
                { if (DateTime.UtcNow > end) throw new TimeoutException("Capability condition not reached: " + script); await Task.Delay(50); }
            }
            void Check(bool passed, string name)
            { if (!passed) throw new InvalidOperationException(name); checks.Add(name); File.WriteAllText(result + ".progress", string.Join('\n', checks)); }
        };
    }
    public ValueTask DisposeAsync() => browser.DisposeAsync();
}
