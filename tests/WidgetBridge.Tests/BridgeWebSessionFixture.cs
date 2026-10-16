using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class BridgeWebSessionFixture
{
    internal static async Task ServeAsync(string pipe, string settingsRoot)
    {
        var paths = new PlatformSettingsPaths(Path.GetFullPath(settingsRoot));
        var store = new PlatformSettingsStore(paths);
        await using var appearance = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        await appearance.StartAsync();
        var root = Path.Combine(settingsRoot, "sealed-fixture"); Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), """
            <!doctype html><html><body style="background:#18334b;color:white"><h1>Synthetic hosted application</h1>
            <button>Session control</button><script src="runtime.js"></script><script>
            let count=0, timer;
            WidgetRailWebSession.handle('sample', value => {
                if(value.events) for(let i=0;i<8;i++) WidgetRailWebSession.post({sequence:value.sequence,index:i});
                return {sequence:value.sequence,receivedAt:performance.timeOrigin+performance.now()};
            });
            WidgetRailWebSession.handle('increment', value => ({count:count+=value.amount}));
            WidgetRailWebSession.handle('echo', value => value);
            WidgetRailWebSession.handle('wait', async (_, {signal}) => {
                await new Promise((resolve,reject)=>signal.addEventListener('abort',()=>{ window.wasCanceled=true; reject(new Error('cancel')); },{once:true}));
            });
            </script></body></html>
            """);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "WebSessionRuntime.js"), Path.Combine(root, "runtime.js"), true);
        Directory.CreateDirectory(Path.Combine(root, "web"));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "YouTubeSessionFixture.html"), Path.Combine(root, "web/player.html"), true);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "WebSessionRuntime.js"), Path.Combine(root, "web/web-session.js"), true);
        if (HostedSpotifyFixture.Enabled)
            await File.WriteAllTextAsync(Path.Combine(root,"web/player.html"),HostedSpotifyFixture.Page());
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(file => Path.GetRelativePath(root,file).Replace('\\','/'), file =>
            new VerifiedPackageFile(Path.GetRelativePath(root,file).Replace('\\','/'), new FileInfo(file).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()), StringComparer.Ordinal);
        var widget = new ConfiguredWidget
        {
            Id = "web-session", PackageId = "fixture.web-session", PublisherId = "fixture.publisher", Name = "Hosted session fixture", InstanceId = "web-session.instance",
            WorkerExecutable = Environment.ProcessPath!, WorkerFingerprint = new string('a',64), CatalogFingerprint = new string('a',64),
            PackageRoot = root, VerifiedPackageFiles = files!, ExecutionTrust = WidgetExecutionTrust.FullTrustCurrentUser,
            PinningSupported = true, FullWidgetPinningSupported = true,
        };
        await using var server = new WidgetBridgeServer(pipe, new([widget]), BridgeProtocol.DefaultMaximumMessageBytes, appearance: appearance,
            consentStore: new ConsentStore(Path.Combine(settingsRoot, "consent")), platformBackend: new SimulatedPlatformBrokerBackend());
        await server.RunAsync(TimeSpan.FromSeconds(30), default);
    }
}

internal sealed class HostedSessionFixtureWidget : Widget
{
    private WidgetWebSession? session;
    private bool visible;
    private readonly WidgetModel<string> status;
    internal HostedSessionFixtureWidget() => status = CreateModel("closed");
    public override WidgetView Render() => new(UI.Stack("root",
        UI.Text(status.Value, "status"), UI.Row("commands",
            UI.Button("Open", "open", "open"), UI.Button("Increment", "increment", "increment"), UI.Button("Large message", "large", "large"),
            UI.Button("Toggle view", "view", "view"), UI.Button("Cancel request", "cancel", "cancel"), UI.Button("Close", "close", "close"), UI.Button("Measure", "measure", "measure")),
        session is { } active && visible ? UI.WebView(active.Reference, "page") : UI.Text("Background session", "hidden")), "open");
    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken token = default)
    {
        if (action.ActionId == "measure")
        {
            Operations.RunSingleFlight("measure", async context =>
            {
                var results = new List<object>();
                var definition = new WebSessionDefinition("neighbor", "Load fixture", WebSessionSource.Package("index.html",
                    [new() {Path="index.html",ContentType="text/html"}, new() {Path="runtime.js",ContentType="text/javascript"}]));
                await using var neighbor = await HostServices.WebSessions.OpenAsync(definition, context.CancellationToken);
                using var reading = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                var events = 0;
                async Task Drain(WidgetWebSession page)
                {
                    try { await foreach(var _ in page.ReadMessagesAsync(reading.Token)) Interlocked.Increment(ref events); }
                    catch(OperationCanceledException) when(reading.IsCancellationRequested){}
                }
                var drains = new[] { Drain(session!), Drain(neighbor) };
                try
                {
                    foreach(var phase in File.Exists(Path.Combine(AppContext.BaseDirectory,"music-only.marker")) ? Array.Empty<string>() : new[] {"warm", "events", "concurrent", "idle-low"})
                    {
                        if(phase=="idle-low") await Task.Delay(18000,context.CancellationToken);
                        var samples = new List<double>(); var delivery = new List<double>();
                        for(var i=0;i<105;i++)
                        {
                            var started = Stopwatch.GetTimestamp();
                            var sentAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            var request = session!.RequestAsync<object, SampleReply>("sample", new {sequence=i,events=phase!="warm"}, context.CancellationToken).AsTask();
                            var other = phase=="concurrent" ? neighbor.RequestAsync<object,SampleReply>("sample",new {sequence=i,events=true},context.CancellationToken).AsTask() : Task.FromResult(new SampleReply(i,0));
                            await Task.WhenAll(request,other);
                            var reply = await request;
                            if(reply.Sequence!=i)throw new InvalidOperationException("Mismatched command reply.");
                            if(i>=5 || phase=="idle-low"){samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);delivery.Add(Math.Max(0,reply.ReceivedAt-sentAt));}
                            await Task.Delay(20,context.CancellationToken);
                        }
                        samples.Sort();delivery.Sort();
                        double Percentile(List<double> values,double fraction) => values[(int)Math.Ceiling(values.Count*fraction)-1];
                        results.Add(new {phase,count=samples.Count,p50=Percentile(samples,.50),p95=Percentile(samples,.95),p99=Percentile(samples,.99),max=samples[^1],over50=samples.Count(v=>v>50),deliveryP95=Percentile(delivery,.95),deliveryMax=delivery[^1]});
                    }
                    var music = HostedSpotifyFixture.Enabled
                        ? await HostedSpotifyFixture.RunAsync(HostServices.WebSessions,context.CancellationToken)
                        : await HostedMusicFixture.RunAsync(HostServices.WebSessions,context.CancellationToken,()=>status.Update(_=>"music-ready"));
                    status.Update(_=>"metrics:"+JsonSerializer.Serialize(new {results,events,music}));
                }
                catch(Exception error){status.Update(_=>"metrics-error:"+error);}
                finally{reading.Cancel();await Task.WhenAll(drains);}
            }, WidgetOperationLifetime.Widget);
            return;
        }
        if (action.ActionId == "view") { visible = !visible; Invalidate(); return; }
        if (action.ActionId == "open" && session is null)
        {
            session = await HostServices.WebSessions.OpenAsync(new("fixture", "Fixture page", WebSessionSource.Package("index.html",
                [new() { Path = "index.html", ContentType = "text/html" }, new() { Path = "runtime.js", ContentType = "text/javascript" }])), token);
            status.Update(_ => "ready"); return;
        }
        if (action.ActionId == "increment" && session is { } page)
        {
            var reply = await page.RequestAsync<Counter, Counter>("increment", new(1,0), token);
            status.Update(_ => "count:" + reply.Count);
        }
        if (action.ActionId == "large" && session is { } large)
        {
            var content=new string('x',40000)+" 🎮";
            var reply=await large.RequestAsync<string,string>("echo",content,token);
            status.Update(_=>reply==content?"large-ok":"large-corrupt");
        }
        if (action.ActionId == "cancel" && session is { } waiting)
        {
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token); cancel.CancelAfter(400);
            try { await waiting.RequestAsync<object, object>("wait", new {}, cancel.Token); }
            catch (OperationCanceledException) { status.Update(_ => "canceled"); }
        }
        if (action.ActionId == "close" && Interlocked.Exchange(ref session, null) is { } previous)
        { await previous.DisposeAsync(); status.Update(_ => "closed"); }
    }
    protected override async ValueTask OnDestroyingAsync(CancellationToken token)
    { if (Interlocked.Exchange(ref session, null) is { } previous) await previous.DisposeAsync(); }
    public sealed record SampleReply(int Sequence, double ReceivedAt);
    public sealed record Counter(int Amount, int Count);
}
