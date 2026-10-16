using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class HostedWebSessionValidationPage : Page, IAsyncDisposable
{
    private readonly Grid parking = new() { Visibility = Visibility.Collapsed };
    private readonly Grid viewport = new() { Height = 400 };
    private readonly TextBlock status = new() { Text = "Hosted web session checks" };
    private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(90));
    private readonly List<string> checks = [];
    private BrowserSurface? first, second;
    private Task? running;
    private readonly Web.HostedWebViewEnvironment runtime = new(Path.Combine(Path.GetTempPath(), "WidgetRail-validation", "sessions-" + Guid.NewGuid().ToString("N")),
        new() { AreBrowserExtensionsEnabled = true });
    private const string PageUrl = "https://session-fixture.widgetrail.internal/index.html";
    internal HostedWebSessionValidationPage()
    {
        Content = new StackPanel { Children = { status, parking, viewport } };
        Loaded += (_, _) => running ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            var source = WebSessionSource.Package("index.html", new EmbeddedMediaResource[] { new() { Path = "index.html", ContentType = "text/html" } }) with { AllowAutoplay = true };
            var definition = new WebSessionDefinition("fixture", "Synthetic packaged session", source);
            var html = """
                <!doctype html><html><body><button>Session fixture</button><script>
                const send = value => chrome.webview.postMessage(value);
                const data = new ArrayBuffer(44 + 16000); const view = new DataView(data);
                function text(offset, value) { for(let i=0;i<value.length;i++) view.setUint8(offset+i,value.charCodeAt(i)); }
                text(0,'RIFF'); view.setUint32(4,16036,true);text(8,'WAVE');text(12,'fmt ');view.setUint32(16,16,true);
                view.setUint16(20,1,true);view.setUint16(22,1,true);view.setUint32(24,8000,true);view.setUint32(28,16000,true);
                view.setUint16(32,2,true);view.setUint16(34,16,true);text(36,'data');view.setUint32(40,16000,true);
                window.fixtureAudio = new Audio(URL.createObjectURL(new Blob([data],{type:'audio/wav'})));
                fixtureAudio.loop=true;
                chrome.webview.addEventListener('message',({data})=>{
                    if(data.start) fixtureAudio.play().then(()=>send({ready:true,playing:!fixtureAudio.paused})).catch(error=>send({ready:true,playing:false,error:error.name,reason:error.message}));
                    else send({echo:data});
                });
                </script></body></html>
                """;
            var bytes = Encoding.UTF8.GetBytes(html);
            var resources = new[] { new BridgeWebSessionResource("index.html", "text/html", Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToBase64String(bytes)) };
            first = Create("first", definition, resources); second = Create("second", definition, resources);
            parking.Children.Add(first); parking.Children.Add(second);
            await first.StartHostedSessionAsync(lifetime.Token);
            Check(first.ProfileName.StartsWith("pkg-", StringComparison.Ordinal), "Packaged sessions use an extension-ineligible profile namespace");
            first.PostHostedMessage(Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"start\":true}")));
            using var ready = JsonDocument.Parse(WebSessionContract.DecodeMessage(await first.HostedMessages.ReadAsync(lifetime.Token)));
            Check(ready.RootElement.GetProperty("playing").GetBoolean(), "Packaged audio starts while the surface is hidden: " + ready.RootElement.GetRawText());
            await second.StartHostedSessionAsync(lifetime.Token);
            second.PostHostedMessage(Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"start\":true}")));
            _ = await second.HostedMessages.ReadAsync(lifetime.Token);
            Check(first.NativeBrowserProcessId != 0 && first.NativeBrowserProcessId == second.NativeBrowserProcessId, "Sessions share the host browser process");
            await first.EvaluateFixtureAsync("localStorage.setItem('private','first');document.cookie='private=first;path=/;Secure';");
            Check(await second.EvaluateFixtureAsync("localStorage.getItem('private')===null && !document.cookie.includes('private=first')") == "true", "Widget profiles isolate cookies and storage at the same origin");
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"text\":\"synthetic\"}"));
            first.PostHostedMessage(encoded);
            using var echo = JsonDocument.Parse(WebSessionContract.DecodeMessage(await first.HostedMessages.ReadAsync(lifetime.Token)));
            Check(echo.RootElement.GetProperty("echo").GetProperty("text").GetString() == "synthetic", "Packaged messaging round trips while hidden");
            await first.EvaluateFixtureAsync("window.blocked=null;fetch('http://127.0.0.1:23456/ungranted').then(()=>window.blocked=false,()=>window.blocked=true);");
            await Task.Delay(200, lifetime.Token);
            Check(await first.EvaluateFixtureAsync("window.blocked") == "true", "Undeclared local network requests are blocked");
            parking.Children.Remove(first); viewport.Children.Add(first); first.SetPresentation(true, false);
            await Task.Delay(200, lifetime.Token);
            Check(await first.EvaluateFixtureAsync("!fixtureAudio.paused && localStorage.getItem('private')==='first'") == "true", "Attaching a view preserves the live page and playback");
            viewport.Children.Remove(first); parking.Children.Add(first); first.SetPresentation(false, false);
            await Task.Delay(300, lifetime.Token);
            Check(await first.EvaluateFixtureAsync("!fixtureAudio.paused") == "true", "Detaching the view preserves background playback");
            await Task.Delay(Web.HostedWebViewMemoryPolicy.IdleDelay + TimeSpan.FromMilliseconds(300), lifetime.Token);
            var core = ((WebView2)first.CompositionRoot).CoreWebView2;
            Check(core.MemoryUsageTargetLevel == Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low && !core.IsSuspended &&
                await first.EvaluateFixtureAsync("!fixtureAudio.paused") == "true", "Hidden sessions automatically use Low memory mode without suspending playback");
            await first.DisposeAsync();
            Check(!first.IsReady, "Closing the session releases its controller");
            first = null;
            Check(second.IsReady, "Closing one controller leaves the shared environment usable");
            await VerifyMusicAdaptersAsync();
            Write(true, null);
        }
        catch (Exception error) { Write(false, error.ToString()); }

        BrowserSurface Create(string widget, WebSessionDefinition definition, IReadOnlyList<BridgeWebSessionResource> resources)
        {
            var document = new WebBrowserDocument(widget, 1, PageUrl, "Synthetic session")
            { InteractionMode = BrowserInteractionMode.ActivateToInteract, Options = definition.Browser };
            var surface = new BrowserSurface(widget, document, runtime.GetAsync);
            surface.ConfigureHostedSession(definition, resources);
            return surface;
        }
    }
    private void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    private void Write(bool passed, string? error)
    {
        status.Text = passed ? "Hosted web session checks passed" : "Hosted web session checks failed";
        var path = Shell.FrontendArguments.Value(Environment.GetCommandLineArgs(), "--web-session-result")!;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { passed, checks, error }));
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        if (running is not null) await running;
        if (first is not null) await first.DisposeAsync();
        if (second is not null) await second.DisposeAsync();
        lifetime.Dispose();
    }
}
