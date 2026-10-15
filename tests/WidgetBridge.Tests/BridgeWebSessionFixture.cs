using System.Security.Cryptography;
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
            let count=0;
            WidgetRailWebSession.handle('increment', value => ({count:count+=value.amount}));
            WidgetRailWebSession.handle('wait', async (_, {signal}) => {
                await new Promise((resolve,reject)=>signal.addEventListener('abort',()=>{ window.wasCanceled=true; reject(new Error('cancel')); },{once:true}));
            });
            </script></body></html>
            """);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "WebSessionRuntime.js"), Path.Combine(root, "runtime.js"), true);
        var files = Directory.GetFiles(root).ToDictionary(file => Path.GetFileName(file), file =>
            new VerifiedPackageFile(Path.GetFileName(file), new FileInfo(file).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()), StringComparer.Ordinal);
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
            UI.Button("Open", "open", "open"), UI.Button("Increment", "increment", "increment"),
            UI.Button("Toggle view", "view", "view"), UI.Button("Cancel request", "cancel", "cancel"), UI.Button("Close", "close", "close")),
        session is { } active && visible ? UI.WebView(active.Reference, "page") : UI.Text("Background session", "hidden")), "open");
    public override async ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken token = default)
    {
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
    public sealed record Counter(int Amount, int Count);
}
