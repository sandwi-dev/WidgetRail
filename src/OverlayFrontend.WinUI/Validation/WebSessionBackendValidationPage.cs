using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Exercises the production browser and background backing without accounts or network traffic.</summary>
internal sealed partial class WebSessionBackendValidationPage : Page, IAsyncDisposable
{
    private readonly List<BrowserSurface> surfaces = [];
    private WebSessionBackgroundHost? backing;
    private bool started;
    internal WebSessionBackendValidationPage(string output)
    {
        Content = new TextBlock { Text = "Testing background page sessions with synthetic documents…", Margin = new(24) };
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>();
            string? error = null;
            try { await RunAsync(Path.GetFullPath(output), checks); }
            catch (Exception e) { error = e.ToString(); }
            finally { await DisposeAsync(); }
            Directory.CreateDirectory(output);
            var resultPath=Path.Combine(output,"result.json");
            await File.WriteAllTextAsync(resultPath+".tmp", JsonSerializer.Serialize(new { checks, error }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(resultPath+".tmp",resultPath,overwrite:true);
            ((TextBlock)Content).Text = error is null ? $"Passed {checks.Count} checks." : error;
        };
    }

    private async Task RunAsync(string output, List<string> checks)
    {
        BrowserSurface.FixturePage = url => new Uri(url).AbsolutePath=="/child" ? "<script>globalThis.fixtureWorld='child';</script>" : """
            <!doctype html><title>Synthetic backend</title>
            <script>globalThis.fixtureWorld='main:'+location.pathname;const until=Date.now()+400;while(Date.now()<until){}</script>
            <body>Local fixture<iframe src="/child"></iframe></body>
            """;
        var environment = CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(output, "profile"), null).AsTask();
        backing = new();
        await backing.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        async Task<BrowserSurface> Create(WebSessionPageAdapterMode mode)
        {
            var key = mode.ToString();
            var definition = new WebSessionDefinition(key, "Synthetic backend", WebSessionSource.Remote("https://backend.fixture.invalid/first"))
            { PageAdapter = new() { ScriptAsset = "adapter.js", Origins = new[] { "https://backend.fixture.invalid" }, Mode = mode } };
            var surface = new BrowserSurface("fixture." + key, new(key, 1, definition.Source.Location, definition.AccessibleName)
                { Options = definition.Browser, InteractionMode = BrowserInteractionMode.ActivateToInteract }, () => environment);
            var bytes = Encoding.UTF8.GetBytes(Adapter);
            surface.ConfigureHostedSession(definition, new[] { new BridgeWebSessionResource("adapter.js", "text/javascript", Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToBase64String(bytes)) });
            surfaces.Add(surface);
            backing.Root.Children.Add(surface);
            await surface.StartHostedSessionAsync(default);
            return surface;
        }

        var backend = await Create(WebSessionPageAdapterMode.SessionBackend);
        var startupId = Guid.NewGuid().ToString("N");
        var admission = Send(backend, startupId, "mutate");
        Check(!admission.IsCompleted, "Startup fixture exercises a request awaiting document readiness", checks);
        await backend.PostHostedMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { widgetRail=1, kind="cancel", id=startupId }))));
        var startupCancelled = false;
        try { await admission; }
        catch (OperationCanceledException) { startupCancelled = true; }
        Check(startupCancelled, "Cancellation retires a request before startup admission", checks);
        // Deliberately no readiness polling or widget-side retry before the first request.
        var first = await Request(backend, "read");
        Check(first.GetProperty("payload").GetString() == "main:/first", "Hidden startup reads the main document without retry", checks);
        Check((await Request(backend, "mutations")).GetProperty("payload").GetInt32()==0, "Cancelled startup request never runs a page mutation", checks);
        var batchId=Guid.NewGuid().ToString("N");
        await Send(backend,batchId,"catalogue");
        var itemCount=0;var nextPart=0;var parts=1;
        while(nextPart<parts)
        {
            var batch=await Read(backend,m=>Kind(m)=="response-batch" && Id(m)==batchId);
            var payload=batch.GetProperty("payload");
            if(payload.GetProperty("index").GetInt32()!=nextPart++)throw new InvalidOperationException("Backend batch order changed.");
            parts=payload.GetProperty("total").GetInt32();
            foreach(var item in payload.GetProperty("items").EnumerateArray())
                if(item.GetProperty("id").GetInt32()!=itemCount++)throw new InvalidOperationException("Backend batch lost an item.");
        }
        Check(itemCount==1000 && parts>1,"Backend returns 1000 ordered items through bounded native batches",checks);
        Check(!Drain(backend).Any(m => m.TryGetProperty("action", out _)), "Backend cannot publish unsolicited page actions", checks);
        for(var i=0;i<18;i++)
        {
            var hangingId=Guid.NewGuid().ToString("N");
            await Send(backend,hangingId,"hang");
            await Read(backend,m=>Kind(m)=="progress"&&Id(m)==hangingId);
            await backend.PostHostedMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{widgetRail=1,kind="cancel",id=hangingId}))));
            await Request(backend,"read");
        }
        Check(true,"Cancellation reclaims request slots even when a handler ignores its signal",checks);
        var pending = Guid.NewGuid().ToString("N");
        await Send(backend, pending, "delay");
        await Read(backend, m => Kind(m) == "progress" && Id(m) == pending);
        await backend.NavigateHostedAsync("https://backend.fixture.invalid/second", default);
        await Read(backend, m => Kind(m) == "navigation");
        var next = await Request(backend, "read");
        Check(next.GetProperty("payload").GetString() == "main:/second", "Navigation retires old work and admits the new document", checks);
        await Task.Delay(800);
        Check(!Drain(backend).Any(m => Id(m) == pending && Kind(m) == "response"), "Retired document cannot deliver late replies", checks);

        var isolated = await Create(WebSessionPageAdapterMode.IsolatedPageActions);
        isolated.SetPresentation(true, true);
        await Until(() => isolated.FixturePageAdapterReady);
        var isolatedReply = await Request(isolated, "read");
        Check(isolatedReply.GetProperty("payload").GetString() == "isolated", "Existing adapters remain isolated from page globals", checks);
        var delayed = Guid.NewGuid().ToString("N");
        await Send(isolated, delayed, "delay");
        await Read(isolated, m => Kind(m) == "progress" && Id(m) == delayed);
        isolated.SetPresentation(false, false);
        var cancelled = await Read(isolated, m => Kind(m) == "response" && Id(m) == delayed);
        Check(cancelled.TryGetProperty("error", out _), "Hiding an isolated adapter cancels its outstanding work", checks);
        var rejected = false;
        try { await Send(isolated, Guid.NewGuid().ToString("N"), "read"); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Hidden isolated adapters still reject new requests", checks);
        var alive = await Request(backend, "read");
        Check(alive.GetProperty("payload").GetString() == "main:/second", "Independent backend remains usable after another adapter hides", checks);

        await backend.NavigateHostedAsync("https://untrusted.fixture.invalid/other", default);
        await Until(async () => (await backend.EvaluateFixtureAsync("location.href")).Contains("untrusted.fixture.invalid",StringComparison.Ordinal));
        var originRejected = false;
        try { await Request(backend,"read"); }
        catch (InvalidOperationException) { originRejected = true; }
        Check(originRejected && !backend.FixturePageAdapterReady, "Unlisted origins cannot execute backend requests", checks);

        await backend.DisposeAsync();
        var retired = false;
        try { await Send(backend, Guid.NewGuid().ToString("N"), "read"); }
        catch (InvalidOperationException) { retired = true; }
        Check(retired, "Disposed backend rejects subsequent requests", checks);
    }

    private static void Check(bool passed, string label, List<string> checks)
    { if (!passed) throw new InvalidOperationException(label); checks.Add(label); }
    private static string? Kind(JsonElement value) => value.TryGetProperty("kind", out var kind) ? kind.GetString() : null;
    private static string? Id(JsonElement value) => value.TryGetProperty("id", out var id) ? id.GetString() : null;
    private static Task Send(BrowserSurface surface, string id, string method) => surface.PostHostedMessageAsync(Convert.ToBase64String(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { widgetRail = 1, kind = "request", id, method, payload = new { } }))));
    private static async Task<JsonElement> Request(BrowserSurface surface, string method)
    {
        var id = Guid.NewGuid().ToString("N");
        await Send(surface, id, method);
        return await Read(surface, m => Kind(m) == "response" && Id(m) == id);
    }
    private static async Task<JsonElement> Read(BrowserSurface surface, Func<JsonElement, bool> match)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            using var json = JsonDocument.Parse(Convert.FromBase64String(await surface.HostedMessages.ReadAsync(timeout.Token)));
            var value = json.RootElement.Clone();
            if (Kind(value) == "page-action") throw new InvalidOperationException("Unsolicited backend action escaped.");
            if (match(value)) return value;
        }
    }
    private static List<JsonElement> Drain(BrowserSurface surface)
    {
        var result = new List<JsonElement>();
        while (surface.HostedMessages.TryRead(out var encoded))
        { using var json = JsonDocument.Parse(Convert.FromBase64String(encoded)); result.Add(json.RootElement.Clone()); }
        return result;
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!condition()) await Task.Delay(50, timeout.Token);
    }
    private static async Task Until(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }
    public async ValueTask DisposeAsync()
    {
        foreach (var surface in surfaces) await surface.DisposeAsync();
        surfaces.Clear();backing?.Dispose();backing = null;BrowserSurface.FixturePage = null;
    }
    private const string Adapter = """
        let mutations=0;
        host.onRequest(async (method,payload,request)=>{
          // Attempt to bypass the backend contract; the host must reject this.
          if(globalThis.fixtureWorld)deliver(JSON.stringify({action:'unsolicited',payload:{}}));
          if(method==='mutate')return ++mutations;
          if(method==='mutations')return mutations;
          if(method==='catalogue')return host.batchResult({version:1},Array.from({length:1000},(_,id)=>({id,text:'x'.repeat(1000)})));
          if(method==='hang'){request.progress({waiting:true});return new Promise(()=>{});}
          if(method==='delay'){
            request.progress({waiting:true});
            await new Promise((resolve,reject)=>{
              const timer=setTimeout(resolve,600);
              request.signal.addEventListener('abort',()=>{clearTimeout(timer);reject(new Error('cancelled'));},{once:true});
            });
          }
          return globalThis.fixtureWorld??'isolated';
        });
        """;
}
