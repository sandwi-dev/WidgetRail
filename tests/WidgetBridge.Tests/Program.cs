using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Pipes;
using System.Text.Json;
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
    ("Catalog owns bounded worker memory policy", CatalogMemoryPolicyIsTrusted),
    ("Catalog widget icons use the closed WidgetGlyph set with a safe fallback", CatalogGlyphIsClosed),
    ("Catalog rejects invalid GBSS with safe diagnostics", InvalidThemeIsRejected),
    ("Catalog rejects style paths outside package root", UnsafeStylePathIsRejected),
    ("Catalog enumeration does not launch workers", EnumerationIsLazy),
    ("Enabled installed widgets join the bridge catalog without eager launch", InstalledWidgetsJoinCatalog),
    ("Tampered installed catalogs fail soft to trusted widgets", TamperedInstalledCatalogFailsSoft),
    ("Invalid installed styles fail soft to trusted widgets", InvalidInstalledStyleFailsSoft),
    ("Platform appearance is bounded and does not launch workers", PlatformAppearanceIsLazy),
    ("User theme layers override widget selectors", UserThemeOverridesWidgetStyles),
    ("Appearance reload publishes revisions and retains last good state", AppearanceReloadIsLastGood),
    ("Widget lifecycle is explicit, lazy, and idempotent through the bridge", LifecycleIsExplicit),
    ("Bridge rejects runtime-owned lifecycle states", RuntimeOwnedLifecycleStatesAreRejected),
    ("Snapshots and hover quick actions cross bridge", SnapshotAndQuickAction),
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

static Task CatalogMemoryPolicyIsTrusted()
{
    using var valid = TemporaryCatalog.Create(memoryLimitMb: 48);
    var configured = BridgeCatalog.Load(valid.Path).GetConfigured("test-widget");
    Assert.Equal(48, configured.MemoryLimitMb);
    using var tooSmall = TemporaryCatalog.Create(memoryLimitMb: 15);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(tooSmall.Path));
    using var tooLarge = TemporaryCatalog.Create(memoryLimitMb: 257);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(tooLarge.Path));
    return Task.CompletedTask;
}

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
    Assert.SequenceEqual(["icon", "id", "instanceId", "name", "quickActions"],
        descriptor.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("test-widget", descriptor.GetProperty("id").GetString());
    Assert.Equal("Test Widget", descriptor.GetProperty("name").GetString());
    Assert.Equal("test.instance", descriptor.GetProperty("instanceId").GetString());
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
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.enabled", enabled: true);
    await InstallWidgetAsync(catalog, temporary.Path, "dev.example.disabled", enabled: false);

    var load = await BridgeCatalog.LoadWithInstalledAsync(
        trusted.Path, catalogRoot, Environment.ProcessPath!);
    Assert.Equal(0, load.Warnings.Count);
    Assert.SequenceEqual(["test-widget", "dev.example.enabled"],
        load.Catalog.Widgets.Select(widget => widget.Id));
    var installed = load.Catalog.GetConfigured("dev.example.enabled");
    Assert.Equal(64, installed.MemoryLimitMb);
    Assert.Equal(Environment.ProcessPath, installed.WorkerExecutable);
    Assert.Equal("styles/default.gbss", installed.StyleFile);
    Assert.SequenceEqual(
        ["--package-root", installed.WorkerArguments[1], "--widget-assembly",
         installed.WorkerArguments[3], "--widget-type", "Example.EnabledWidget"],
        installed.WorkerArguments);
    Assert.True(Path.IsPathFullyQualified(installed.WorkerArguments[1]),
        "Installed package root must be canonical before worker launch.");
    Assert.True(Path.IsPathFullyQualified(installed.WorkerArguments[3]),
        "Installed assembly path must be canonical before worker launch.");
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

static async Task<InstalledWidgetVersion> InstallWidgetAsync(
    GameBarAlternative.WidgetCatalog.WidgetCatalog catalog,
    string packageDirectory,
    string id,
    bool enabled,
    string styleSource = "button { color: #abcdef; }")
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
        Permissions = [],
        OptionalPermissions = [],
        BackgroundPolicy = "none",
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
    var installed = await catalog.CreateInstaller().InstallAsync(packagePath);
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
        ["backdropOpacity", "interfaceScale", "motion", "revision", "shellStyles", "textScale", "themeId", "themeVersion"],
        response.Payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("dev.example.bridge", response.Payload.GetProperty("themeId").GetString());
    Assert.Equal("1.0.0", response.Payload.GetProperty("themeVersion").GetString());
    Assert.Equal(1.1D, response.Payload.GetProperty("interfaceScale").GetDouble());
    Assert.Equal(1.2D, response.Payload.GetProperty("textScale").GetDouble());
    Assert.Equal(0.7D, response.Payload.GetProperty("backdropOpacity").GetDouble());
    Assert.Equal("reduced", response.Payload.GetProperty("motion").GetString());
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
    Assert.Equal(3, renderStyles.EnumerateObject().Count());
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
    Assert.Equal(1, harness.Server.RunningWorkerCount);

    var activation = await harness.Client.RequestAsync(
        BridgeMessageTypes.SetWidgetLifecycle,
        new BridgeWidgetLifecycleRequest("test-widget", WidgetLifecycleState.Interactive));
    Assert.Equal(BridgeMessageTypes.Acknowledged, activation.Type);

    var acknowledgement = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.X,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: 7,
            MonotonicTimestampMicroseconds: 1000)));
    Assert.Equal(BridgeMessageTypes.ControllerInputResult, acknowledgement.Type);
    Assert.True(acknowledgement.Payload.GetProperty("handled").GetBoolean(),
        "Expected dashboard quick action to be handled.");
    var invalidation = await harness.Client.ReadEventAsync(BridgeMessageTypes.Invalidation);
    Assert.Equal("test-widget", invalidation.Payload.GetProperty("widgetId").GetString());
    Assert.Equal(1L, invalidation.Payload.GetProperty("revision").GetInt64());

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
}

static async Task DashboardButtonsStayHostOwned()
{
    await using var harness = await BridgeHarness.StartAsync();
    var response = await harness.Client.RequestAsync(
        BridgeMessageTypes.ControllerInput,
        new BridgeControllerInputRequest("test-widget", new ControllerInputEvent(
            ControllerButton.A,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction)));
    Assert.Equal(BridgeMessageTypes.Error, response.Type);
    Assert.Equal(0, harness.Server.RunningWorkerCount);
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
    public override WidgetView Render() => new(
        UI.Stack("root",
            UI.Button("Refresh", "refresh", "button")
                .Selected()
                .Shortcut(ControllerButton.RightBumper).Classes("primary"),
            UI.Button("Unavailable", "disabled", "disabled-button")
                .Disabled().Classes("disabled")),
        "button",
        [new WidgetQuickAction(ControllerButton.X, "refresh", "Refresh")]);

    public override ValueTask OnActionAsync(
        WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (action.ActionId == "refresh")
            Invalidate();
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
        int? memoryLimitMb = null)
    {
        var directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"gba-bridge-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "widgets.json");
        var stylesDirectory = System.IO.Path.Combine(directory, "styles");
        Directory.CreateDirectory(stylesDirectory);
        File.WriteAllText(System.IO.Path.Combine(stylesDirectory, "default.gbss"), invalidStyle
            ? "button { background: url(https://example.test/evil.png); }"
            : "stack { gap: 12px; } button { color: #ffffff; font-size: 18px; } #button { opacity: 0.8; } .primary:selected { border-width: 3px; } .primary:focused { outline-color: #8b7cff; scale: 1.1; } .disabled:disabled { opacity: 0.4; }");
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Test process path is unavailable.");
        var json = JsonSerializer.Serialize(new
        {
            catalogVersion = 1,
            widgets = new[]
            {
                new
                {
                    id = "test-widget",
                    name = "Test Widget",
                    instanceId = "test.instance",
                    icon,
                    workerExecutable = executable,
                    styleFile,
                    workerArguments = Array.Empty<string>(),
                    memoryLimitMb,
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
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
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

    public static async Task<BridgeHarness> StartAsync(bool withAppearance = false)
    {
        var temporary = TemporaryCatalog.Create();
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
