using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeClientRegistryScenarios
{
    internal static async Task BackgroundBrowserRetention()
    {
        var delay = new ManualRegistryDelay();
        var configured = Widget("webapp", worker: 'w', catalog: 'w', residency: new WidgetResidencyPolicy
        { Mode = WidgetResidencyPolicies.UnloadAfterIdle, IdleSeconds = WidgetResidencyPolicies.MinimumIdleSeconds });
        await using var fixture = new RegistryFixture(Catalog(configured), delay: delay.InvokeAsync,
            configure: (_, client) => client.SnapshotFactory = sequence => new WidgetView(UI.Stack("root",
                Enumerable.Range(0, 5).Select(i => UI.WebBrowser(new("app." + i, 1, "https://example.com", "App")
                { Options = WebBrowserOptions.WebApp with { Background = BrowserBackgroundMode.KeepAlive } },
                    BrowserInteractionMode.InteractOnFocus, "page." + i)).ToArray())).CreateSnapshot(configured.InstanceId, sequence));
        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var first = await fixture.GetSnapshotAsync(configured.Id);
        var request = new BridgeBrowserRetention(configured.Id, first.WorkerRun!, "app.0", true);
        foreach (var type in new[] { typeof(BridgeBrowserRetention), typeof(BridgeBrowserRetentionResult) })
            RegistryAssert.True(BridgeJsonContext.Default.GetTypeInfo(type) is not null, "Retention messages require generated metadata.");
        var json = System.Text.Json.JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeBrowserRetention);
        RegistryAssert.Equal(request, System.Text.Json.JsonSerializer.Deserialize(json, BridgeJsonContext.Default.BridgeBrowserRetention));
        RegistryAssert.Equal(BridgeRequestKind.BrowserRetention, BridgeRequestClassifier.Classify(new()
        { Type = BridgeMessageTypes.BrowserRetention, RequestId = 2, Payload = BridgeJson.ToElement(request) }).Kind);

        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Background);
        var idle = await delay.NextAsync();
        RegistryAssert.True(await fixture.Registry.SetBrowserRetentionAsync(request, default, default));
        await idle.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        idle.Release();
        RegistryAssert.Equal(1, fixture.Registry.BackgroundBrowserCount);
        RegistryAssert.Equal(0, fixture.Clients[0].UnloadCount);
        using (var hidden = fixture.Registry.TryAdmitHostEffect(configured.Id, configured.WorkerFingerprint))
            RegistryAssert.True(hidden is null, "Retained browser must not grant foreground host authority.");
        RegistryAssert.False(await fixture.Registry.SetBrowserRetentionAsync(request with { DocumentId = "undeclared" }, default, default));
        RegistryAssert.False(await fixture.Registry.SetBrowserRetentionAsync(request with { Retain = false }, default, default));
        (await delay.NextAsync()).Release();
        await fixture.Clients[0].Unloaded.WaitAsync(TimeSpan.FromSeconds(2));

        await fixture.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Visible);
        var second = await fixture.GetSnapshotAsync(configured.Id);
        var current = request with { WorkerRun = second.WorkerRun! };
        RegistryAssert.True(current.WorkerRun != request.WorkerRun);
        RegistryAssert.True(await fixture.Registry.SetBrowserRetentionAsync(current, default, default));
        RegistryAssert.False(await fixture.Registry.SetBrowserRetentionAsync(request with { Retain = false }, default, default));
        RegistryAssert.Equal(1, fixture.Registry.BackgroundBrowserCount);
        for (var i = 1; i < 4; i++)
            RegistryAssert.True(await fixture.Registry.SetBrowserRetentionAsync(current with { DocumentId = "app." + i }, default, default));
        RegistryAssert.False(await fixture.Registry.SetBrowserRetentionAsync(current with { DocumentId = "app.4" }, default, default));
        RegistryAssert.Equal(4, fixture.Registry.BackgroundBrowserCount);
        fixture.Registry.ApplyCatalog(Catalog(configured with { WorkerFingerprint = Fingerprint('z'), CatalogFingerprint = Fingerprint('z') }), 1);
        RegistryAssert.Equal(0, fixture.Registry.BackgroundBrowserCount);
    }

    internal static async Task BrowserRetentionPipe()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var pipe = "browser-retention-" + Guid.NewGuid().ToString("N");
        await using var server = new WidgetBridgeServer(pipe, new([]));
        var serving = server.RunAsync(TimeSpan.FromSeconds(3), deadline.Token);
        await using var session = await WidgetRail.WidgetPresentationSession.WidgetPresentationSession.ConnectAsync(pipe, cancellationToken: deadline.Token);
        var retired = new WidgetRail.WidgetPresentationSession.WidgetPresentationAuthority("retired", "runtime", "presentation", 1, "instance", 1, "root")
        { WorkerRun = new(1, 1) };
        RegistryAssert.False(await session.SetWebBrowserRetentionAsync(retired, "document", false, deadline.Token));
        _ = await session.TakeCaptureAsync(deadline.Token);
        _ = await session.TakePermissionAsync(deadline.Token);
        await session.DisposeAsync();
        await serving.WaitAsync(deadline.Token);
    }
}
