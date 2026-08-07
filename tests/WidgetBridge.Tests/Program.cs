using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
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
    ("Catalog rejects invalid GBSS with safe diagnostics", InvalidThemeIsRejected),
    ("Catalog rejects style paths outside package root", UnsafeStylePathIsRejected),
    ("Catalog enumeration does not launch workers", EnumerationIsLazy),
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
    Assert.SequenceEqual(["id", "instanceId", "name", "quickActions"],
        descriptor.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    Assert.Equal("test-widget", descriptor.GetProperty("id").GetString());
    Assert.Equal("Test Widget", descriptor.GetProperty("name").GetString());
    Assert.Equal("test.instance", descriptor.GetProperty("instanceId").GetString());
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
        string styleFile = "styles/default.gbss")
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
                    workerExecutable = executable,
                    styleFile,
                    workerArguments = Array.Empty<string>(),
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
    private readonly Task _serverTask;
    public WidgetBridgeServer Server { get; }
    public BridgeTestClient Client { get; }

    private BridgeHarness(
        TemporaryCatalog temporaryCatalog,
        WidgetBridgeServer server,
        BridgeTestClient client,
        Task serverTask)
    {
        _temporaryCatalog = temporaryCatalog;
        Server = server;
        Client = client;
        _serverTask = serverTask;
    }

    public static async Task<BridgeHarness> StartAsync()
    {
        var temporary = TemporaryCatalog.Create();
        try
        {
            var catalog = BridgeCatalog.Load(temporary.Path);
            var pipeName = $"gba-bridge-test-{Guid.NewGuid():N}";
            var server = new WidgetBridgeServer(pipeName, catalog, 64 * 1024);
            var serverTask = server.RunAsync(TimeSpan.FromSeconds(3));
            var client = await BridgeTestClient.ConnectAsync(pipeName, 64 * 1024);
            return new BridgeHarness(temporary, server, client, serverTask);
        }
        catch
        {
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
            _temporaryCatalog.Dispose();
        }
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
