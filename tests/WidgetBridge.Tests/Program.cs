using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipes;
using System.Text.Json;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

if (args.Contains("--widget-pipe", StringComparer.Ordinal))
    return await RunWorkerAsync(args);

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bridge framing rejects oversized messages", OversizedFrameIsRejected),
    ("Strict catalog rejects unknown properties", StrictCatalogRejectsUnknownProperties),
    ("Bridge startup scopes an explicit development installed catalog", DevelopmentCatalogRootIsScoped),
    ("Settings reviews the same catalog selected by the bridge", SettingsUsesSelectedCatalog),
    ("Catalog owns bounded worker memory policy", CatalogMemoryPolicyIsTrusted),
    ("Catalog widget icons use the closed WidgetGlyph set with a safe fallback", CatalogGlyphIsClosed),
    ("Catalog rejects invalid GBSS with safe diagnostics", InvalidThemeIsRejected),
    ("Catalog rejects style paths outside package root", UnsafeStylePathIsRejected),
    ("Catalog enumeration does not launch workers", EnumerationIsLazy),
    ("Enabled installed widgets join the bridge catalog without eager launch", InstalledWidgetsJoinCatalog),
    ("Installed widget residency policies reach the generic supervisor", InstalledResidencyPolicyIsCarried),
    ("Known installed capabilities load lazily and unknown capabilities fail closed", InstalledCapabilityDeclarationsAreClosed),
    ("Tampered installed catalogs fail soft to trusted widgets", TamperedInstalledCatalogFailsSoft),
    ("Invalid installed styles fail soft to trusted widgets", InvalidInstalledStyleFailsSoft),
    ("Catalog monitor retains invalid trusted state and fails closed on installed state", CatalogMonitorIsRevisionedAndLastGood),
    ("Installed package tamper retires the live worker and cannot relaunch it", InstalledPackageTamperRetiresLiveWorker),
    ("Catalog monitor closes the startup notification window", CatalogMonitorStartupCatchUp),
    ("Catalog reconciliation preserves compatible workers and retires changed workers", CatalogReconciliationPreservesCompatibleWorkers),
    ("Platform appearance is bounded and does not launch workers", PlatformAppearanceIsLazy),
    ("Private diagnostics attach only to the exact trusted Settings identity", DiagnosticsAreSettingsOnly),
    ("User theme layers override widget selectors", UserThemeOverridesWidgetStyles),
    ("Appearance reload publishes revisions and retains last good state", AppearanceReloadIsLastGood),
    ("Widget lifecycle is explicit, lazy, and idempotent through the bridge", LifecycleIsExplicit),
    ("Suspend-when-hidden blocks work and serves only a cached view", SuspendWhenHiddenIsLogical),
    ("Idle unload is cancellable cached and lazily resumable", IdleUnloadIsPolicyDriven),
    ("Bridge rejects runtime-owned lifecycle states", RuntimeOwnedLifecycleStatesAreRejected),
    ("Snapshots and hover quick actions cross bridge", SnapshotAndQuickAction),
    ("Protocol-v2 scroll nodes resolve bridge render roles", ScrollRenderRole),
    ("Dashboard-owned controller buttons are rejected", DashboardButtonsStayHostOwned),
    ("Worker failures surface without killing bridge", WorkerFailureIsSurfaced),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task<int> RunWorkerAsync(string[] arguments)
{
    var pipe = RequiredValue(arguments, "--widget-pipe");
    var instance = RequiredValue(arguments, "--widget-instance");
    var maximumBytes = int.Parse(
        RequiredValue(arguments, "--max-message-bytes"),
        System.Globalization.CultureInfo.InvariantCulture);
    await new WidgetWorkerServer(new BridgeTestWidget(), instance, pipe, maximumBytes).RunAsync();
    return 0;
}

static async Task OversizedFrameIsRejected()
{
    var bytes = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, 1025);
    await using var stream = new MemoryStream(bytes);
    var channel = new BridgeFrameChannel(stream, 1024);
    await Assert.ThrowsAsync<BridgeProtocolException>(() =>
        channel.ReadAsync(CancellationToken.None).AsTask());
}

static Task StrictCatalogRejectsUnknownProperties()
{
    using var catalog = TemporaryCatalog.Create(addUnknownProperty: true);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(catalog.Path));
    return Task.CompletedTask;
}

static Task DevelopmentCatalogRootIsScoped()
{
    using var temporary = new TemporaryDirectory("gba-bridge-dev-root");
    var settings = Path.Combine(temporary.Path, "settings");
    var development = Path.Combine(temporary.Path, "development-catalog");
    Assert.Equal(Path.GetFullPath(development),
        GameBarAlternative.WidgetBridge.Program.ResolveInstalledCatalogRoot(
            ["--installed-catalog-root", development], settings));
    Assert.Equal(Path.Combine(Path.GetFullPath(settings), "widgets"),
        GameBarAlternative.WidgetBridge.Program.ResolveInstalledCatalogRoot([], settings));
    Assert.Throws<ArgumentException>(() =>
        GameBarAlternative.WidgetBridge.Program.ResolveInstalledCatalogRoot(
            ["--installed-catalog-root", development, "--installed-catalog-root", development], settings));
    return Task.CompletedTask;
}

static async Task SettingsUsesSelectedCatalog()
{
    using var trusted = TemporaryCatalog.Create(
        id: "settings",
        packageId: "org.gbar.firstparty.settings",
        publisherId: "org.gbar.firstparty");
    using var temporary = new TemporaryDirectory("gba-settings-selected-catalog");
    var selected = Path.Combine(temporary.Path, "selected-widgets");
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, selected, Environment.ProcessPath!);
    var settings = load.Catalog.GetConfigured("settings");
    var index = settings.WorkerArguments.ToList().IndexOf("--installed-widget-catalog-root");
    Assert.True(index >= 0 && index + 1 < settings.WorkerArguments.Count,
        "Settings was not given the bridge-selected package catalog.");
    Assert.Equal(Path.GetFullPath(selected), settings.WorkerArguments[index + 1]);
    Assert.True(!settings.RequiresAppContainer,
        "Catalog alignment must not change the exact trusted Settings worker policy.");
}

static Task CatalogMemoryPolicyIsTrusted()
{
    using var valid = TemporaryCatalog.Create(memoryLimitMb: 48);
    var configured = BridgeCatalog.Load(valid.Path).GetConfigured("test-widget");
    Assert.Equal(48, configured.MemoryLimitMb);
    Assert.True(!configured.RequiresAppContainer,
        "Trusted built-in catalog workers must remain explicitly host-owned.");
    using var tooSmall = TemporaryCatalog.Create(memoryLimitMb: 15);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(tooSmall.Path));
    using var tooLarge = TemporaryCatalog.Create(memoryLimitMb: 257);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(tooLarge.Path));
    return Task.CompletedTask;
}

static async Task DiagnosticsAreSettingsOnly()
{
    var exact = DiagnosticCandidate();
    Assert.True(WidgetBridgeServer.IsTrustedSettings(exact),
        "The exact built-in Settings identity did not receive diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with { Id = "settings-copy" }),
        "A different widget ID received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with { PackageId = "dev.example.settings" }),
        "A package spoofing the Settings widget ID received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with { PublisherId = "dev.example" }),
        "A publisher spoofing the Settings identity received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with
    {
        DeclaredCapabilities = ["audio.sessions.read"],
    }), "A capability-bearing worker received private diagnostics.");
    Assert.True(!WidgetBridgeServer.IsTrustedSettings(exact with
    {
        RequiresAppContainer = true,
        IsolationKey = "spoofed-settings",
    }), "An installed isolated worker received private diagnostics.");

    await using var companion = new DiagnosticsWidgetProcessCompanion(
        _ => ValueTask.FromResult(GameBarAlternative.PlatformDiagnostics.PlatformDiagnosticsSnapshot.Unavailable()),
        new WidgetProcessCompanionContext(
            WidgetWorkerIsolationPolicy.HostTrustedJobOnly, null, null));
    var pidIndex = companion.WorkerArguments.ToList().IndexOf("--diagnostics-server-pid");
    Assert.True(pidIndex >= 0 && pidIndex + 1 < companion.WorkerArguments.Count,
        "Diagnostics companion omitted its kernel-verifiable server PID.");
    Assert.Equal(
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        companion.WorkerArguments[pidIndex + 1]);
}

static ConfiguredWidget DiagnosticCandidate() => new()
{
    Id = "settings",
    PackageId = "org.gbar.firstparty.settings",
    PublisherId = "org.gbar.firstparty",
    Name = "Settings",
    InstanceId = "settings",
    WorkerExecutable = Environment.ProcessPath
        ?? throw new InvalidOperationException("Test process path is unavailable."),
};

static Task CatalogGlyphIsClosed()
{
    using var fallback = TemporaryCatalog.Create(icon: null);
    Assert.Equal(WidgetGlyph.Connection, BridgeCatalog.Load(fallback.Path).Widgets.Single().Icon);
    using var invalid = TemporaryCatalog.Create(icon: "arbitrary-svg");
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(invalid.Path));
    return Task.CompletedTask;
}

static Task InvalidThemeIsRejected()
{
    using var catalog = TemporaryCatalog.Create(invalidStyle: true);
    var exception = Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(catalog.Path));
    Assert.True(exception.Message.Contains("invalid_value", StringComparison.Ordinal),
        "Expected a stable GBSS diagnostic code.");
    Assert.True(!exception.Message.Contains(System.IO.Path.GetTempPath(), StringComparison.OrdinalIgnoreCase),
        "Catalog diagnostics must not disclose absolute package paths.");
    return Task.CompletedTask;
}

static Task UnsafeStylePathIsRejected()
{
    using var catalog = TemporaryCatalog.Create(styleFile: "../outside.gbss");
    var exception = Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(catalog.Path));
    Assert.True(exception.Message.Contains("package-relative", StringComparison.Ordinal),
        "Expected the catalog boundary diagnostic.");
    return Task.CompletedTask;
}

static async Task EnumerationIsLazy()
{
    await using var harness = await BridgeHarness.StartAsync();
    var response = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(BridgeMessageTypes.Widgets, response.Type);
    var widgets = response.Payload.GetProperty("widgets");
    Assert.Equal(1, widgets.GetArrayLength());
    var descriptor = widgets[0];
    Assert.SequenceEqual(["icon", "id", "instanceId", "name", "presentationGeneration", "quickActions", "runtimeGeneration"],
        descriptor.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("test-widget", descriptor.GetProperty("id").GetString());
    Assert.Equal("Test Widget", descriptor.GetProperty("name").GetString());
    Assert.Equal("test.instance", descriptor.GetProperty("instanceId").GetString());
    Assert.Equal(32, descriptor.GetProperty("runtimeGeneration").GetString()!.Length);
    Assert.Equal(32, descriptor.GetProperty("presentationGeneration").GetString()!.Length);
    Assert.Equal("music", descriptor.GetProperty("icon").GetString());
    var quickAction = descriptor.GetProperty("quickActions")[0];
    Assert.SequenceEqual(["actionId", "controllerButton", "id", "label", "sourceElementId"],
        quickAction.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("hover-refresh", quickAction.GetProperty("id").GetString());
    Assert.Equal("Refresh", quickAction.GetProperty("label").GetString());
    Assert.Equal("refresh", quickAction.GetProperty("actionId").GetString());
    Assert.Equal("hover.refresh", quickAction.GetProperty("sourceElementId").GetString());
    Assert.Equal("x", quickAction.GetProperty("controllerButton").GetString());
    Assert.False(descriptor.TryGetProperty("workerExecutable", out _),
        "Native descriptors must not expose worker paths.");
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task InstalledWidgetsJoinCatalog()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-installed");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.enabled", enabled: true,
        icon: WidgetGlyph.Music);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.disabled", enabled: false);

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.Equal(0, load.Warnings.Count);
    Assert.SequenceEqual(["test-widget", "dev.example.enabled"],
        load.Catalog.Widgets.Select(widget => widget.Id));
    var installed = load.Catalog.GetConfigured("dev.example.enabled");
    var installedVersion = (await catalog.DiscoverAsync()).Widgets
        .Single(widget => widget.Id == "dev.example.enabled")
        .ActiveVersion;
    var installedManifest = installedVersion.Manifest;
    Assert.Equal(64, installed.MemoryLimitMb);
    Assert.Equal(WidgetGlyph.Music, installed.Icon);
    Assert.Equal(Environment.ProcessPath, installed.WorkerExecutable);
    Assert.Equal("styles/default.gbss", installed.StyleFile);
    Assert.True(installed.RequiresAppContainer,
        "Installed community workers must require AppContainer isolation.");
    Assert.True(!string.IsNullOrWhiteSpace(installed.IsolationKey),
        "Installed community workers need a stable host-owned isolation identity.");
    Assert.Equal(
        InstalledWidgetAuthority.PublisherId(installedVersion),
        installed.PublisherId);
    Assert.True(!string.Equals(
            installedManifest.Publisher,
            installed.PublisherId,
            StringComparison.Ordinal),
        "Installed authority trusted the manifest publisher label directly.");
    Assert.SequenceEqual([installed.WorkerArguments[1]], installed.ReadOnlyPaths);
    Assert.SequenceEqual(
        ["--package-root", installed.WorkerArguments[1], "--widget-assembly",
         installed.WorkerArguments[3], "--widget-type", "Example.EnabledWidget"],
        installed.WorkerArguments);
    Assert.True(Path.IsPathFullyQualified(installed.WorkerArguments[1]),
        "Installed package root must be canonical before worker launch.");
    Assert.True(Path.IsPathFullyQualified(installed.WorkerArguments[3]),
        "Installed assembly path must be canonical before worker launch.");
    Assert.Equal(WidgetResidencyPolicies.KeepAlive, installed.ResidencyPolicy.Mode);
}

static async Task InstalledResidencyPolicyIsCarried()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-installed-residency");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.idle-unload",
        enabled: true,
        residencyPolicy: new WidgetResidencyPolicy
        {
            Mode = WidgetResidencyPolicies.UnloadAfterIdle,
            IdleSeconds = 300,
        });
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    var configured = load.Catalog.GetConfigured("dev.example.idle-unload");
    Assert.Equal(WidgetResidencyPolicies.UnloadAfterIdle, configured.ResidencyPolicy.Mode);
    Assert.Equal(300, configured.ResidencyPolicy.IdleSeconds);
    Assert.True(configured.RequiresAppContainer,
        "Residency metadata must not weaken installed worker isolation.");
}

static async Task InstalledCapabilityDeclarationsAreClosed()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-capabilities");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.audio",
        enabled: true,
        permissions: [PlatformCapabilities.AudioSessionsReadV1]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.unknown",
        enabled: true,
        permissions: ["system.unsupported.control.v1"]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.apps",
        enabled: true,
        permissions:
        [
            PlatformCapabilities.AppLibraryReadV1,
            PlatformCapabilities.AppLibraryLaunchV1,
        ]);
    await InstallWidgetAsync(
        catalog,
        temporary.Path,
        "dev.example.companion",
        enabled: true,
        permissions:
        [
            "network.loopback:13091",
            PlatformCapabilities.PrivateSecretsV1,
        ]);

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);

    Assert.SequenceEqual(
        ["test-widget", "dev.example.audio", "dev.example.apps", "dev.example.companion"],
        load.Catalog.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual(
        [PlatformCapabilities.AudioSessionsReadV1],
        load.Catalog.GetConfigured("dev.example.audio").DeclaredCapabilities);
    Assert.SequenceEqual(
        [PlatformCapabilities.AppLibraryLaunchV1, PlatformCapabilities.AppLibraryReadV1],
        load.Catalog.GetConfigured("dev.example.apps").DeclaredCapabilities);
    Assert.SequenceEqual(
        ["network.loopback:13091", PlatformCapabilities.PrivateSecretsV1],
        load.Catalog.GetConfigured("dev.example.companion").DeclaredCapabilities);
    Assert.Equal(1, load.Warnings.Count);
    Assert.True(load.Warnings[0].Contains("unsupported capability", StringComparison.Ordinal),
        "Unknown capabilities need a safe closed-vocabulary warning.");
}

static async Task TamperedInstalledCatalogFailsSoft()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-tampered");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    var installed = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.tampered", enabled: true);
    await File.WriteAllTextAsync(Path.Combine(installed.InstallPath, "manifest.json"), "{}");

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.SequenceEqual(["test-widget"], load.Catalog.Widgets.Select(widget => widget.Id));
    Assert.Equal(1, load.Warnings.Count);
    Assert.True(load.Warnings[0].Contains("invalid_manifest", StringComparison.Ordinal),
        "Expected a bounded stable installed-catalog warning.");
}

static async Task InvalidInstalledStyleFailsSoft()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-invalid-style");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.badstyle", enabled: true,
        styleSource: "button { color: definitely-not-a-color; }");

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.SequenceEqual(["test-widget"], load.Catalog.Widgets.Select(widget => widget.Id));
    Assert.Equal(1, load.Warnings.Count);
    Assert.True(load.Warnings[0].Contains("invalid styles", StringComparison.Ordinal),
        "Expected invalid installed styles to be isolated to their package.");
}

static async Task CatalogMonitorIsRevisionedAndLastGood()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-live-catalog");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.alpha", enabled: true);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.beta", enabled: true);
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    var revisions = new List<long>();
    monitor.Changed += (_, change) => revisions.Add(change.Revision);

    var unchanged = await monitor.ReloadNowAsync();
    Assert.False(unchanged.Published, "An equivalent reload must not publish.");
    Assert.Equal(0L, unchanged.Revision);

    await catalog.SetOrderAsync(["dev.example.beta", "dev.example.alpha"]);
    var reordered = await monitor.ReloadNowAsync();
    Assert.True(reordered.Published, "Installed order change was not published.");
    Assert.Equal(1L, reordered.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.beta", "dev.example.alpha"],
        reordered.Current.Widgets.Select(widget => widget.Id));

    var replay = await monitor.ReloadNowAsync();
    Assert.False(replay.Published, "Equivalent catalog replay advanced the revision.");
    Assert.Equal(1L, replay.Revision);

    await catalog.SetEnabledAsync("dev.example.beta", false);
    var disabled = await monitor.ReloadNowAsync();
    Assert.True(disabled.Published, "Disable change was not published.");
    Assert.Equal(2L, disabled.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.alpha"],
        disabled.Current.Widgets.Select(widget => widget.Id));
    var validState = await File.ReadAllBytesAsync(Path.Combine(catalogRoot, "catalog-state.json"));
    var validTrusted = await File.ReadAllBytesAsync(trusted.Path);

    await File.WriteAllTextAsync(trusted.Path, "{");
    var invalidTrusted = await monitor.ReloadNowAsync();
    Assert.True(invalidTrusted.RetainedLastGood, "Invalid trusted catalog replaced last-good state.");
    Assert.Equal(2L, invalidTrusted.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.alpha"],
        invalidTrusted.Current.Widgets.Select(widget => widget.Id));

    await File.WriteAllBytesAsync(trusted.Path, validTrusted);
    await File.WriteAllTextAsync(Path.Combine(catalogRoot, "catalog-state.json"), "{");
    var invalidInstalled = await monitor.ReloadNowAsync();
    Assert.False(invalidInstalled.RetainedLastGood,
        "Invalid installed state retained stale community package authority.");
    Assert.True(invalidInstalled.Published, "Invalid installed state did not publish trusted-only state.");
    Assert.Equal(3L, invalidInstalled.Revision);
    Assert.SequenceEqual(["test-widget"], invalidInstalled.Current.Widgets.Select(widget => widget.Id));
    await File.WriteAllBytesAsync(Path.Combine(catalogRoot, "catalog-state.json"), validState);

    var restored = await monitor.ReloadNowAsync();
    Assert.True(restored.Published, "Restored installed state was not published.");
    Assert.Equal(4L, restored.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.alpha"],
        restored.Current.Widgets.Select(widget => widget.Id));

    var watched = new TaskCompletionSource<BridgeCatalogChanged>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Changed += (_, change) =>
    {
        if (change.Revision >= 5) watched.TrySetResult(change);
    };
    monitor.Start();
    await catalog.SetEnabledAsync("dev.example.beta", true);
    var fileSystemChange = await watched.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(5L, fileSystemChange.Revision);
    Assert.SequenceEqual(["test-widget", "dev.example.beta", "dev.example.alpha"],
        fileSystemChange.Catalog.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual([1L, 2L, 3L, 4L, 5L], revisions);
}

static async Task InstalledPackageTamperRetiresLiveWorker()
{
    using var trusted = TemporaryCatalog.Create();
    using var temporary = new TemporaryDirectory("gba-bridge-tamper-retire");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var catalog = new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalogRoot);
    var installed = await InstallWidgetAsync(
        catalog, temporary.Path, "dev.example.tamper", enabled: true);
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    var pipeName = $"gba-bridge-tamper-{Guid.NewGuid():N}";
    await using var server = new WidgetBridgeServer(
        pipeName, initial.Catalog, 64 * 1024, catalogMonitor: monitor);
    var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
    await using var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
    try
    {
        var started = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("dev.example.tamper", WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Acknowledged, started.Type);
        Assert.Equal(1, server.RunningWorkerCount);

        await File.AppendAllTextAsync(
            Path.Combine(installed.InstallPath, "payload", "Widget.dll"), "tampered");
        var failedClosed = await monitor.ReloadNowAsync();
        Assert.True(failedClosed.Published, "Tampered package did not publish trusted-only state.");
        Assert.False(failedClosed.RetainedLastGood, "Tampered package retained stale authority.");
        Assert.SequenceEqual(["test-widget"],
            failedClosed.Current.Widgets.Select(widget => widget.Id));
        Assert.Equal(0, server.RunningWorkerCount);

        var changed = await client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
        Assert.Equal(failedClosed.Revision, changed.Payload.GetProperty("revision").GetInt64());
        var listed = await client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
        Assert.SequenceEqual(["test-widget"], listed.Payload.GetProperty("widgets")
            .EnumerateArray().Select(widget => widget.GetProperty("id").GetString()!));
        var relaunch = await client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("dev.example.tamper", WidgetLifecycleState.Visible));
        Assert.Equal(BridgeMessageTypes.Error, relaunch.Type);
        Assert.Equal(0, server.RunningWorkerCount);
    }
    finally
    {
        await client.RequestAsync(BridgeMessageTypes.Stop, new { });
        await serverTask.WaitAsync(TimeSpan.FromSeconds(3));
    }
}

static async Task CatalogMonitorStartupCatchUp()
{
    using var trusted = TemporaryCatalog.Create(name: "Before Watch");
    using var changed = TemporaryCatalog.Create(name: "Changed Before Watch");
    using var temporary = new TemporaryDirectory("gba-bridge-catalog-catch-up");
    var catalogRoot = Path.Combine(temporary.Path, "catalog");
    var initial = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    await using var monitor = new BridgeCatalogMonitor(
        trusted.Path, catalogRoot, Environment.ProcessPath!, initial.Catalog);
    await File.WriteAllBytesAsync(trusted.Path, await File.ReadAllBytesAsync(changed.Path));

    var published = new TaskCompletionSource<BridgeCatalogChanged>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    monitor.Changed += (_, change) => published.TrySetResult(change);
    monitor.Start();
    var catchUp = await published.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(1L, catchUp.Revision);
    Assert.Equal("Changed Before Watch", catchUp.Catalog.Widgets.Single().Name);
}

static async Task CatalogReconciliationPreservesCompatibleWorkers()
{
    await using var harness = await BridgeHarness.StartAsync();
    var started = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, started.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var initial = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    var initialDescriptor = initial.Payload.GetProperty("widgets")[0];
    var initialRuntime = initialDescriptor.GetProperty("runtimeGeneration").GetString();
    var initialPresentation = initialDescriptor.GetProperty("presentationGeneration").GetString();

    using var renamedSource = TemporaryCatalog.Create(
        name: "Renamed Widget",
        styleSource: "button { color: #ffffff; font-size: 23px; }");
    var renamed = BridgeCatalog.Load(renamedSource.Path);
    harness.Server.ApplyCatalog(renamed, revision: 1);
    var renamedEvent = await harness.Client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
    Assert.Equal(1L, renamedEvent.Payload.GetProperty("revision").GetInt64());
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var renamedList = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    var renamedDescriptor = renamedList.Payload.GetProperty("widgets")[0];
    Assert.Equal("Renamed Widget", renamedDescriptor.GetProperty("name").GetString());
    Assert.Equal(initialRuntime, renamedDescriptor.GetProperty("runtimeGeneration").GetString());
    Assert.True(initialPresentation != renamedDescriptor.GetProperty("presentationGeneration").GetString(),
        "Descriptor-only change did not advance presentation generation.");
    var refreshedSnapshot = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(23D, refreshedSnapshot.Payload.GetProperty("renderStyles")
        .GetProperty("button").GetProperty("base").GetProperty("font-size")
        .GetProperty("number").GetDouble());
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    harness.Server.ApplyCatalog(renamed, revision: 1);
    _ = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(0, harness.Client.PendingEventCount);
    using var staleSource = TemporaryCatalog.Create(instanceId: "stale.instance");
    harness.Server.ApplyCatalog(BridgeCatalog.Load(staleSource.Path), revision: 0);
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    using var capabilitySource = TemporaryCatalog.Create(
        name: "Renamed Widget",
        declaredCapabilities: [PlatformCapabilities.AudioSessionsReadV1]);
    var capabilityChanged = BridgeCatalog.Load(capabilitySource.Path);
    harness.Server.ApplyCatalog(capabilityChanged, revision: 2);
    var changedEvent = await harness.Client.ReadEventAsync(BridgeMessageTypes.CatalogChanged);
    Assert.Equal(2L, changedEvent.Payload.GetProperty("revision").GetInt64());
    Assert.Equal(0, harness.Server.RunningWorkerCount);
    var changedList = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(0, harness.Server.RunningWorkerCount);
    Assert.True(initialRuntime != changedList.Payload.GetProperty("widgets")[0]
        .GetProperty("runtimeGeneration").GetString(),
        "Worker-affecting declaration change did not advance runtime generation.");
}

static async Task<InstalledWidgetVersion> InstallWidgetAsync(
    GameBarAlternative.WidgetCatalog.WidgetCatalog catalog,
    string packageDirectory,
    string id,
    bool enabled,
    string styleSource = "button { color: #abcdef; }",
    IReadOnlyList<string>? permissions = null,
    WidgetResidencyPolicy? residencyPolicy = null,
    WidgetGlyph icon = WidgetGlyph.Connection)
{
    var packagePath = Path.Combine(packageDirectory, $"{id}.gbarwidget");
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = "dev.example",
        Name = id.EndsWith("enabled", StringComparison.Ordinal) ? "Enabled Widget" : "Test Widget",
        Version = "1.0.0",
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint(
            "dotnet-worker", "payload/Widget.dll", "Example.EnabledWidget"),
        Presentation = new WidgetPresentation(icon),
        Permissions = permissions ?? [],
        OptionalPermissions = [],
        BackgroundPolicy = residencyPolicy is null ? "none" : null,
        ResidencyPolicy = residencyPolicy,
        ResourceRequest = new WidgetResourceRequest(256, 60),
        Architectures = [System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
            System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64"],
    };
    await using (var stream = new FileStream(packagePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
    {
        WriteArchiveEntry(archive, "manifest.json", ManifestJson.Serialize(manifest));
        WriteArchiveEntry(archive, "payload/Widget.dll", [0x4d, 0x5a]);
        WriteArchiveEntry(archive, "styles/default.gbss",
            System.Text.Encoding.UTF8.GetBytes(styleSource));
    }
    var installed = await catalog.InstallAsync(packagePath);
    if (enabled) await catalog.SetEnabledAsync(id, true);
    return installed;
}

static void WriteArchiveEntry(ZipArchive archive, string path, ReadOnlySpan<byte> content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
    using var output = entry.Open();
    output.Write(content);
}

static async Task PlatformAppearanceIsLazy()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var response = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(BridgeMessageTypes.PlatformAppearance, response.Type);
    Assert.SequenceEqual(
        ["backdropOpacity", "boldText", "contrast", "interfaceScale", "motion", "revision", "shellStyles", "textScale", "themeId", "themeVersion", "transparency"],
        response.Payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("dev.example.bridge", response.Payload.GetProperty("themeId").GetString());
    Assert.Equal("1.0.0", response.Payload.GetProperty("themeVersion").GetString());
    Assert.Equal(1.1D, response.Payload.GetProperty("interfaceScale").GetDouble());
    Assert.Equal(1.2D, response.Payload.GetProperty("textScale").GetDouble());
    Assert.Equal(0.7D, response.Payload.GetProperty("backdropOpacity").GetDouble());
    Assert.Equal("reduced", response.Payload.GetProperty("motion").GetString());
    Assert.Equal("high", response.Payload.GetProperty("contrast").GetString());
    Assert.Equal(true, response.Payload.GetProperty("boldText").GetBoolean());
    Assert.Equal("reduced", response.Payload.GetProperty("transparency").GetString());
    var shellStyles = response.Payload.GetProperty("shellStyles");
    Assert.Equal(12, shellStyles.EnumerateObject().Count());
    Assert.True(shellStyles.GetProperty("tray-item:focused")
        .TryGetProperty("outline-color", out _), "Focused tray style was not resolved.");
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task UserThemeOverridesWidgetStyles()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, response.Type);
    var button = response.Payload.GetProperty("renderStyles").GetProperty("button").GetProperty("base");
    Assert.Equal("#2468ac", button.GetProperty("color").GetProperty("text").GetString());
    Assert.Equal(0.55D, button.GetProperty("opacity").GetProperty("number").GetDouble());
}

static async Task AppearanceReloadIsLastGood()
{
    await using var harness = await BridgeHarness.StartAsync(withAppearance: true);
    var before = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    var revision = before.Payload.GetProperty("revision").GetInt64();
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    await harness.Appearance!.WriteThemeAsync("button { color: definitely-not-a-color; }");
    var invalid = await harness.Appearance.Service.ReloadNowAsync();
    Assert.False(invalid.Published, "Invalid appearance unexpectedly replaced the last-good snapshot.");
    Assert.Equal(revision, invalid.Current.Revision);
    var retained = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(revision, retained.Payload.GetProperty("revision").GetInt64());

    await harness.Appearance.WriteThemeAsync("title { color: #abcdef; } button { color: #13579b; }");
    var valid = await harness.Appearance.Service.ReloadNowAsync();
    Assert.True(valid.Published, "Valid appearance reload did not publish.");
    var changed = await harness.Client.ReadEventAsync(BridgeMessageTypes.AppearanceChanged);
    Assert.Equal(valid.Current.Revision, changed.Payload.GetProperty("revision").GetInt64());
    var after = await harness.Client.RequestAsync(BridgeMessageTypes.GetPlatformAppearance, new { });
    Assert.Equal(valid.Current.Revision, after.Payload.GetProperty("revision").GetInt64());
    Assert.Equal("#abcdef", after.Payload.GetProperty("shellStyles").GetProperty("title")
        .GetProperty("color").GetProperty("text").GetString());
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task LifecycleIsExplicit()
{
    await using var harness = await BridgeHarness.StartAsync();
    var inactive = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    Assert.Equal(BridgeMessageTypes.Acknowledged, inactive.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    var active = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, active.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var visible = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, visible.Type);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var deactivated = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    Assert.Equal(BridgeMessageTypes.Acknowledged, deactivated.Type);
}

static async Task SuspendWhenHiddenIsLogical()
{
    await using var harness = await BridgeHarness.StartAsync(residencyPolicy:
        new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden });
    var cold = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Error, cold.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    var visibleResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var visible = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        visibleResponse.Payload.GetProperty("snapshot").GetRawText()));
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));

    var hiddenAction = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("refresh", "button")));
    Assert.Equal(BridgeMessageTypes.Error, hiddenAction.Type);
    var cachedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var cached = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        cachedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(visible.Sequence, cached.Sequence);
    Assert.Equal(1, harness.Server.RunningWorkerCount);
}

static async Task IdleUnloadIsPolicyDriven()
{
    var policy = new WidgetResidencyPolicy
    {
        Mode = WidgetResidencyPolicies.UnloadAfterIdle,
        IdleSeconds = WidgetResidencyPolicies.MinimumIdleSeconds,
    };
    await using var harness = await BridgeHarness.StartAsync(residencyPolicy: policy);
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    var firstResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var first = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        firstResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    await Task.Delay(TimeSpan.FromMilliseconds(250));
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    await Task.Delay(TimeSpan.FromMilliseconds(250));
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
    await WaitUntilAsync(() => harness.Server.RunningWorkerCount == 0,
        TimeSpan.FromSeconds(WidgetResidencyPolicies.MinimumIdleSeconds + 3));

    var cachedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var cached = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        cachedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(first.Sequence, cached.Sequence);
    Assert.Equal(0, harness.Server.RunningWorkerCount);

    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(1, harness.Server.RunningWorkerCount);
    var resumedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var resumed = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        resumedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(first.WidgetInstanceId, resumed.WidgetInstanceId);
    // Leave a fresh idle timer pending. Harness disposal must cancel it,
    // serialize with teardown, and release the resumed worker without waiting
    // for the manifest duration.
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Background));
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline)
            throw new TimeoutException("Condition was not reached before the timeout.");
        await Task.Delay(50);
    }
}

static async Task RuntimeOwnedLifecycleStatesAreRejected()
{
    await using var harness = await BridgeHarness.StartAsync();
    foreach (var state in new[] { WidgetLifecycleState.Created, WidgetLifecycleState.Destroying })
    {
        var response = await harness.Client.RequestAsync(
            BridgeMessageTypes.SetWidgetLifecycle,
            new BridgeWidgetLifecycleRequest("test-widget", state));
        Assert.Equal(BridgeMessageTypes.Error, response.Type);
    }
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static async Task SnapshotAndQuickAction()
{
    await using var harness = await BridgeHarness.StartAsync();
    var snapshotResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    Assert.Equal(BridgeMessageTypes.Snapshot, snapshotResponse.Type);
    var snapshotJson = snapshotResponse.Payload.GetProperty("snapshot").GetRawText();
    var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(snapshotJson));
    Assert.Equal("test.instance", snapshot.WidgetInstanceId);
    var renderStyles = snapshotResponse.Payload.GetProperty("renderStyles");
    Assert.Equal(5, renderStyles.EnumerateObject().Count());
    var buttonStyles = renderStyles.GetProperty("button");
    var fontSize = buttonStyles.GetProperty("base").GetProperty("font-size");
    Assert.Equal("length", fontSize.GetProperty("kind").GetString());
    Assert.Equal("18px", fontSize.GetProperty("text").GetString());
    Assert.Equal(18D, fontSize.GetProperty("number").GetDouble());
    Assert.Equal("px", fontSize.GetProperty("unit").GetString());
    var focusedScale = buttonStyles.GetProperty("focused").GetProperty("scale");
    Assert.Equal("number", focusedScale.GetProperty("kind").GetString());
    Assert.Equal(1.1D, focusedScale.GetProperty("number").GetDouble());
    Assert.Equal(JsonValueKind.Null, focusedScale.GetProperty("unit").ValueKind);
    Assert.Equal("3px", buttonStyles.GetProperty("base").GetProperty("border-width").GetProperty("text").GetString());
    Assert.Equal("3px", buttonStyles.GetProperty("focused").GetProperty("border-width").GetProperty("text").GetString());
    var disabledStyles = renderStyles.GetProperty("disabled-button");
    Assert.Equal(0.4D, disabledStyles.GetProperty("base").GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal(0.4D, disabledStyles.GetProperty("focused").GetProperty("opacity").GetProperty("number").GetDouble());
    var busyStyles = renderStyles.GetProperty("busy-button");
    Assert.Equal(0.7D, busyStyles.GetProperty("base").GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal(0.7D, busyStyles.GetProperty("focused").GetProperty("opacity").GetProperty("number").GetDouble());
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    var activation = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Visible));
    Assert.Equal(BridgeMessageTypes.Acknowledged, activation.Type);

    var acknowledgement = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 7,
            MonotonicTimestampMicroseconds: 1000,
            SnapshotSequence: snapshot.Sequence)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, acknowledgement.Type);
    Assert.True(acknowledgement.Payload.GetProperty("handled").GetBoolean(),
        "Expected dashboard quick action to be handled.");
    var invalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal("test-widget", invalidation.Payload.GetProperty("widgetId").GetString());
    Assert.Equal(1L, invalidation.Payload.GetProperty("revision").GetInt64());

    var replay = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 7,
            SnapshotSequence: snapshot.Sequence)));
    Assert.Equal(BridgeMessageTypes.Error, replay.Type);
    var stale = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 8,
            SnapshotSequence: snapshot.Sequence + 1)));
    Assert.Equal(BridgeMessageTypes.Error, stale.Type);

    var interactive = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, interactive.Type);

    var shortcut = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.RightBumper,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "button",
            ActiveInputScopeId: snapshot.ActiveInputScopeId,
            SnapshotSequence: snapshot.Sequence)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, shortcut.Type);
    Assert.True(shortcut.Payload.GetProperty("handled").GetBoolean(),
        "Expected focused shortcut to be handled.");
    var secondInvalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal(2L, secondInvalidation.Payload.GetProperty("revision").GetInt64());

    var sliderChange = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.DPadRight,
            ControllerEventPhase.Repeated,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "volume",
            ActiveInputScopeId: snapshot.ActiveInputScopeId,
            SnapshotSequence: snapshot.Sequence,
            RequestedValue: 0.6)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, sliderChange.Type);
    Assert.True(sliderChange.Payload.GetProperty("handled").GetBoolean(),
        "Expected absolute Slider target to cross the bridge.");
    var sliderInvalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal(3L, sliderInvalidation.Payload.GetProperty("revision").GetInt64());
    var updatedResponse = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var updated = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(
        updatedResponse.Payload.GetProperty("snapshot").GetRawText()));
    Assert.Equal(0.6D, FindNode(updated.Root, "volume").Value);
}

static ViewNode FindNode(ViewNode node, string id)
{
    if (node.Id == id) return node;
    foreach (var child in node.Children)
    {
        var found = FindNodeOrNull(child, id);
        if (found is not null) return found;
    }
    throw new InvalidOperationException($"Node '{id}' was not found.");

    static ViewNode? FindNodeOrNull(ViewNode candidate, string target)
    {
        if (candidate.Id == target) return candidate;
        foreach (var child in candidate.Children)
        {
            var nested = FindNodeOrNull(child, target);
            if (nested is not null) return nested;
        }
        return null;
    }
}

static async Task DashboardButtonsStayHostOwned()
{
    await using var harness = await BridgeHarness.StartAsync();
    foreach (var button in new[]
             {
                 ControllerButton.A, ControllerButton.B, ControllerButton.Y,
                 ControllerButton.DPadUp, ControllerButton.DPadDown,
                 ControllerButton.DPadLeft, ControllerButton.DPadRight,
             })
    {
        var response = await harness.Client.RequestAsync(
            BridgeMessageTypes.ControllerInput,
            new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
                button,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction)));
        Assert.Equal(BridgeMessageTypes.Error, response.Type);
    }
    Assert.Equal(0, harness.Server.RunningWorkerCount);
}

static Task ScrollRenderRole()
{
    var snapshot = new WidgetView(
        UI.VerticalScroll("sessions",
            UI.Button("Game", "mute", "session.game.mute")),
        InitialFocusId: "session.game.mute",
        Surface: new WidgetSurfaceHints { Mode = WidgetSurfaceMode.Compact })
        .CreateSnapshot("bridge.scroll", 1);
    var styles = BridgeRenderStyleResolver.Resolve(snapshot, theme: null);
    Assert.Equal(2, styles.Count);
    Assert.True(styles.ContainsKey("sessions"), "Scroll role was omitted from bridge styles.");
    Assert.Equal(ProtocolConstants.ScrollContainerVersion, snapshot.ProtocolVersion);
    return Task.CompletedTask;
}

static async Task WorkerFailureIsSurfaced()
{
    await using var harness = await BridgeHarness.StartAsync();
    _ = await harness.Client.RequestAsync(
        BridgeMessageTypes.GetSnapshot, new WidgetIdRequest("test-widget"));
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.Action,
        new BridgeActionRequest("test-widget", new WidgetActionEvent("crash", "button")));
    Assert.Equal(BridgeMessageTypes.Error, response.Type);
    var failure = await harness.Client.ReadEventAsync(BridgeMessageTypes.Failure);
    Assert.Equal("test-widget", failure.Payload.GetProperty("widgetId").GetString());

    var widgets = await harness.Client.RequestAsync(BridgeMessageTypes.ListWidgets, new { });
    Assert.Equal(BridgeMessageTypes.Widgets, widgets.Type);
}

static string RequiredValue(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length) throw new ArgumentException($"Missing {name}.");
    return values[index + 1];
}

file sealed class BridgeTestWidget : Widget
{
    private double _volume = 0.5;

    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Refresh", "refresh", "button")
                .Selected()
                .Shortcut(ControllerButton.RightBumper).Classes("primary"),
            UI.Button("Unavailable", "disabled", "disabled-button")
                .Disabled().Classes("disabled"),
            UI.Button("Saving", "busy", "busy-button")
                .Busy().Classes("busy"),
            UI.Slider(_volume, 0, 1, 0.1, "volume.changed", "volume",
                "Volume", $"{_volume:P0}")),
        "button",
        [new WidgetQuickAction(ControllerButton.X, "refresh", "Refresh")]);

    public override ValueTask OnActionAsync(
        WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "refresh")
            Invalidate();
        else if (action is { ActionId: "volume.changed", RequestedValue: { } requested })
        {
            _volume = requested;
            Invalidate();
        }
        else if (action.ActionId == "crash")
            Environment.Exit(31);
        return ValueTask.CompletedTask;
    }
}

file sealed class TemporaryCatalog : IDisposable
{
    private readonly string _directory;
    public string Path { get; }

    private TemporaryCatalog(string directory, string path)
    {
        _directory = directory;
        Path = path;
    }

    public static TemporaryCatalog Create(
        bool addUnknownProperty = false,
        bool invalidStyle = false,
        string styleFile = "styles/default.gbss",
        string? icon = "music",
        int? memoryLimitMb = null,
        string name = "Test Widget",
        string instanceId = "test.instance",
        string id = "test-widget",
        string packageId = "dev.test.widget",
        string publisherId = "dev.test",
        IReadOnlyList<string>? declaredCapabilities = null,
        string? styleSource = null,
        WidgetResidencyPolicy? residencyPolicy = null)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gba-bridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "widgets.json");
        var stylesDirectory = System.IO.Path.Combine(directory, "styles");
        Directory.CreateDirectory(stylesDirectory);
        File.WriteAllText(System.IO.Path.Combine(stylesDirectory, "default.gbss"), invalidStyle
            ? "button { background: url(https://example.test/evil.png); }"
            : styleSource ?? "stack { gap: 12px; } button { color: #ffffff; font-size: 18px; } #button { opacity: 0.8; } .primary:selected { border-width: 3px; } .primary:focused { outline-color: #8b7cff; scale: 1.1; } .disabled:disabled { opacity: 0.4; } .busy:busy { opacity: 0.7; }");
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.");
        var json = JsonSerializer.Serialize(new
        {
            catalogVersion = 1,
            widgets = new[]
            {
                new
                {
                    id,
                    packageId,
                    publisherId,
                    name,
                    instanceId,
                    icon,
                    workerExecutable = executable,
                    styleFile,
                    workerArguments = Array.Empty<string>(),
                    declaredCapabilities = declaredCapabilities ?? Array.Empty<string>(),
                    memoryLimitMb,
                    residencyPolicy,
                    quickActions = new[]
                    {
                        new
                        {
                            id = "hover-refresh",
                            label = "Refresh",
                            actionId = "refresh",
                            sourceElementId = "hover.refresh",
                            controllerButton = "x",
                        },
                    },
                },
            },
            unknown = addUnknownProperty ? true : (bool?)null,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        File.WriteAllText(path, json);
        return new TemporaryCatalog(directory, path);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class BridgeHarness : IAsyncDisposable
{
    private readonly TemporaryCatalog _temporaryCatalog;
    private readonly TemporaryAppearance? _appearance;
    private readonly Task _serverTask;
    public WidgetBridgeServer Server { get; }
    public BridgeTestClient Client { get; }
    public TemporaryAppearance? Appearance => _appearance;

    private BridgeHarness(
        TemporaryCatalog temporaryCatalog,
        TemporaryAppearance? appearance,
        WidgetBridgeServer server,
        BridgeTestClient client,
        Task serverTask)
    {
        _temporaryCatalog = temporaryCatalog;
        _appearance = appearance;
        Server = server;
        Client = client;
        _serverTask = serverTask;
    }

    public static async Task<BridgeHarness> StartAsync(
        bool withAppearance = false,
        WidgetResidencyPolicy? residencyPolicy = null)
    {
        var temporary = TemporaryCatalog.Create(residencyPolicy: residencyPolicy);
        TemporaryAppearance? appearance = null;
        try
        {
            if (withAppearance) appearance = await TemporaryAppearance.CreateAsync();
            var catalog = BridgeCatalog.Load(temporary.Path);
            var pipeName = $"gba-bridge-test-{Guid.NewGuid():N}";
            var server = new WidgetBridgeServer(pipeName, catalog, 64 * 1024, appearance?.Service);
            var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
            var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
            return new BridgeHarness(temporary, appearance, server, client, serverTask);
        }
        catch
        {
            if (appearance is not null) await appearance.DisposeAsync();
            temporary.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Client.RequestAsync(BridgeMessageTypes.Stop, new { });
            await _serverTask.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
            if (_appearance is not null) await _appearance.DisposeAsync();
            _temporaryCatalog.Dispose();
        }
    }
}

file sealed class TemporaryAppearance : IAsyncDisposable
{
    private readonly string _directory;
    private readonly string _themeFile;
    public PlatformAppearanceService Service { get; }

    private TemporaryAppearance(string directory, string themeFile, PlatformAppearanceService service)
    {
        _directory = directory;
        _themeFile = themeFile;
        Service = service;
    }

    public static async Task<TemporaryAppearance> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gba-bridge-appearance-{Guid.NewGuid():N}");
        var paths = new PlatformSettingsPaths(directory);
        var themeDirectory = Path.Combine(paths.ThemesDirectory, "dev.example.bridge", "1.0.0");
        Directory.CreateDirectory(themeDirectory);
        var themeFile = Path.Combine(themeDirectory, "theme.gbss");
        await File.WriteAllTextAsync(Path.Combine(themeDirectory, "theme.json"),
            "{\"schemaVersion\":1,\"id\":\"dev.example.bridge\",\"name\":\"Bridge Test\",\"version\":\"1.0.0\",\"entryFile\":\"theme.gbss\"}");
        await File.WriteAllTextAsync(themeFile,
            "button { color: #2468ac; opacity: 0.55; } title { color: #fedcba; }");
        var store = new PlatformSettingsStore(paths);
        await store.ReplaceAsync(new PlatformSettingsDocument
        {
            SchemaVersion = PlatformSettingsDocument.CurrentSchemaVersion,
            Appearance = AppearanceSettings.Default with
            {
                ThemeId = "dev.example.bridge",
                ThemeVersion = "1.0.0",
                InterfaceScale = 1.1,
                TextScale = 1.2,
                BackdropOpacity = 0.7,
                Motion = MotionPreference.Reduced,
                Contrast = ContrastPreference.High,
                BoldText = true,
                Transparency = TransparencyPreference.Reduced,
            },
        });
        var service = new PlatformAppearanceService(paths, new ThemeManager(store, new ThemeCatalog(paths)));
        var result = await service.ReloadNowAsync();
        Assert.True(result.Published, "Initial bridge appearance did not load.");
        return new TemporaryAppearance(directory, themeFile, service);
    }

    public Task WriteThemeAsync(string source) => File.WriteAllTextAsync(_themeFile, source);

    public async ValueTask DisposeAsync()
    {
        await Service.DisposeAsync();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; }

    public TemporaryDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file sealed class BridgeTestClient : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly BridgeFrameChannel _channel;
    private readonly Queue<BridgeEnvelope> _events = new();
    private long _requestId;
    public int PendingEventCount => _events.Count;

    private BridgeTestClient(NamedPipeClientStream pipe, BridgeFrameChannel channel)
    {
        _pipe = pipe;
        _channel = channel;
    }

    public static async Task<BridgeTestClient> ConnectAsync(string pipeName, int maximumBytes)
    {
        var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(3000);
        var client = new BridgeTestClient(pipe, new BridgeFrameChannel(pipe, maximumBytes));
        var hello = await client.RequestAsync(
            BridgeMessageTypes.Hello, new BridgeHello("WidgetBridge.Tests"));
        Assert.Equal(BridgeMessageTypes.HelloAccepted, hello.Type);
        return client;
    }

    public async Task<BridgeEnvelope> RequestAsync<T>(string type, T payload)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        await _channel.WriteAsync(new BridgeEnvelope
        {
            Type = type,
            RequestId = requestId,
            Payload = BridgeJson.ToElement(payload),
        }, CancellationToken.None);
        while (true)
        {
            var response = await _channel.ReadAsync(CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(4));
            if (response.RequestId == 0)
            {
                _events.Enqueue(response);
                continue;
            }
            if (response.RequestId != requestId)
                throw new InvalidOperationException("Received response for a different request.");
            return response;
        }
    }

    public async Task<BridgeEnvelope> ReadEventAsync(string type)
    {
        var count = _events.Count;
        for (var index = 0; index < count; index++)
        {
            var message = _events.Dequeue();
            if (message.Type == type) return message;
            _events.Enqueue(message);
        }
        while (true)
        {
            var message = await _channel.ReadAsync(CancellationToken.None).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(4));
            if (message.RequestId != 0)
                throw new InvalidOperationException("Expected an event, received a response.");
            if (message.Type == type) return message;
            _events.Enqueue(message);
        }
    }

    public async ValueTask DisposeAsync() => await _pipe.DisposeAsync();
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
