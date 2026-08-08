using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.PlatformDiagnostics;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bound worker receives a validated sanitized snapshot", AuthenticatedRoundTrip),
    ("Client authenticates the kernel-reported server before sending its nonce", FakeServerRejectedBeforeNonce),
    ("Pre-created first pipe instance rejects a squatted endpoint", SquattedEndpointFailsClosed),
    ("Peer withholding delivery receipt is evicted without poisoning recovery", MissingReceiptRecovers),
    ("Wrong nonce loses one connection without poisoning recovery", WrongNonceRecovers),
    ("Client timeout must be finite positive and bounded", ClientTimeoutValidation),
    ("Stalled hello is evicted and the accept loop recovers", StalledHelloRecovers),
    ("Stalled provider is bounded and the accept loop recovers", StalledProviderRecovers),
    ("Invalid provider snapshots fail closed", InvalidSnapshotFailsClosed),
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
        failures.Add($"FAIL {test.Name}: {exception.Message}");
        Console.Error.WriteLine(failures[^1]);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task AuthenticatedRoundTrip()
{
    var expected = HealthySnapshot(11);
    await using var harness = new DiagnosticsHarness(_ => ValueTask.FromResult(expected));
    var observed = await harness.Client.GetSnapshotAsync();
    Assert.Equal(11L, observed.Revision);
    Assert.Equal(PlatformDiagnosticState.Healthy, observed.Catalog.State);
    Assert.Equal("audio-mixer", observed.Workers.Single().WidgetId);
}

static async Task WrongNonceRecovers()
{
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(12)));
    for (var iteration = 0; iteration < 16; iteration++)
    {
        var wrong = new PlatformDiagnosticsPipeClient(
            harness.PipeName, new string('0', 64), Environment.ProcessId,
            TimeSpan.FromSeconds(1));
        var rejection = await Assert.ThrowsAsync<PlatformDiagnosticsException>(
            () => wrong.GetSnapshotAsync().AsTask());
        Assert.Equal("authentication_failed", rejection.Code);
        PlatformDiagnosticsSnapshot recovered;
        try
        {
            recovered = await harness.Client.GetSnapshotAsync();
        }
        catch (PlatformDiagnosticsException exception)
        {
            throw new InvalidOperationException(
                $"Recovery failed at iteration {iteration} with '{exception.Code}': " +
                $"{exception.InnerException?.GetType().Name}: {exception.InnerException?.Message}",
                exception);
        }
        Assert.Equal(12L, recovered.Revision);
    }
}

static async Task MissingReceiptRecovers()
{
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(17)),
        serverTimeout: TimeSpan.FromMilliseconds(100),
        clientTimeout: TimeSpan.FromSeconds(1));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await using var peer = new NamedPipeClientStream(
        ".", harness.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await peer.ConnectAsync(timeout.Token);
    await WriteTestFrame(peer, new { nonce = harness.ChannelNonce }, timeout.Token);
    using (var acknowledgement = await ReadTestFrame(peer, timeout.Token))
        Assert.Equal(true, acknowledgement.RootElement.GetProperty("accepted").GetBoolean());
    await WriteTestFrame(peer, new { operation = "snapshot" }, timeout.Token);
    using (var snapshot = await ReadTestFrame(peer, timeout.Token))
        Assert.Equal(17L, snapshot.RootElement.GetProperty("revision").GetInt64());

    // Do not send the delivery receipt. The server's existing request bound
    // must evict this peer, rearm the sole reserved instance, and allow the
    // authenticated worker to recover without a timing sleep or request replay.
    var recovered = await harness.Client.GetSnapshotAsync(timeout.Token);
    Assert.Equal(17L, recovered.Revision);
}

static async Task<JsonDocument> ReadTestFrame(Stream stream, CancellationToken cancellationToken)
{
    var header = new byte[4];
    await stream.ReadExactlyAsync(header, cancellationToken);
    var length = BinaryPrimitives.ReadInt32LittleEndian(header);
    if (length is <= 0 or > 64 * 1024)
        throw new InvalidOperationException($"Unexpected test frame length {length}.");
    var payload = new byte[length];
    await stream.ReadExactlyAsync(payload, cancellationToken);
    return JsonDocument.Parse(payload);
}

static async Task WriteTestFrame<T>(
    Stream stream, T value, CancellationToken cancellationToken)
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
    var payload = JsonSerializer.SerializeToUtf8Bytes(value, options);
    var header = new byte[4];
    BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
    await stream.WriteAsync(header, cancellationToken);
    await stream.WriteAsync(payload, cancellationToken);
    await stream.FlushAsync(cancellationToken);
}

static async Task FakeServerRejectedBeforeNonce()
{
    var pipeName = $"gba-diagnostics-fake-{Guid.NewGuid():N}";
    await using var fake = new System.IO.Pipes.NamedPipeServerStream(
        pipeName, System.IO.Pipes.PipeDirection.InOut, 1,
        System.IO.Pipes.PipeTransmissionMode.Byte,
        System.IO.Pipes.PipeOptions.Asynchronous |
        System.IO.Pipes.PipeOptions.CurrentUserOnly |
        System.IO.Pipes.PipeOptions.FirstPipeInstance);
    var accepting = fake.WaitForConnectionAsync();
    var client = new PlatformDiagnosticsPipeClient(
        pipeName, new string('A', 64), int.MaxValue, TimeSpan.FromSeconds(1));

    var failure = await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => client.GetSnapshotAsync().AsTask());
    Assert.Equal("server_identity_mismatch", failure.Code);
    await accepting.WaitAsync(TimeSpan.FromSeconds(1));

    var oneByte = new byte[1];
    var received = await fake.ReadAsync(oneByte).AsTask().WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(0, received);
}

static Task SquattedEndpointFailsClosed()
{
    var pipeName = $"gba-diagnostics-squatted-{Guid.NewGuid():N}";
    using var squatter = new System.IO.Pipes.NamedPipeServerStream(
        pipeName, System.IO.Pipes.PipeDirection.InOut, 1,
        System.IO.Pipes.PipeTransmissionMode.Byte,
        System.IO.Pipes.PipeOptions.Asynchronous |
        System.IO.Pipes.PipeOptions.CurrentUserOnly |
        System.IO.Pipes.PipeOptions.FirstPipeInstance);
    Assert.Throws<IOException>(() =>
    {
        _ = new PlatformDiagnosticsPipeServer(
            pipeName, _ => ValueTask.FromResult(HealthySnapshot(1)));
    });
    return Task.CompletedTask;
}

static Task ClientTimeoutValidation()
{
    const string pipeName = "gba-diagnostics-timeout-validation";
    var nonce = new string('A', 64);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        _ = new PlatformDiagnosticsPipeClient(
            pipeName, nonce, Environment.ProcessId, TimeSpan.Zero));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        _ = new PlatformDiagnosticsPipeClient(
            pipeName, nonce, Environment.ProcessId, Timeout.InfiniteTimeSpan));
    Assert.Throws<ArgumentOutOfRangeException>(() =>
        _ = new PlatformDiagnosticsPipeClient(
            pipeName, nonce, Environment.ProcessId, TimeSpan.FromSeconds(11)));
    return Task.CompletedTask;
}

static async Task StalledHelloRecovers()
{
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(21)),
        serverTimeout: TimeSpan.FromMilliseconds(100));
    await using (var stalled = new System.IO.Pipes.NamedPipeClientStream(
                     ".", harness.PipeName, System.IO.Pipes.PipeDirection.InOut,
                     System.IO.Pipes.PipeOptions.Asynchronous))
    {
        await stalled.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(200);
    }

    var recovered = await harness.Client.GetSnapshotAsync();
    Assert.Equal(21L, recovered.Revision);
}

static async Task StalledProviderRecovers()
{
    var calls = 0;
    var never = new TaskCompletionSource<PlatformDiagnosticsSnapshot>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    ValueTask<PlatformDiagnosticsSnapshot> Provider(CancellationToken _)
    {
        return Interlocked.Increment(ref calls) == 1
            ? new ValueTask<PlatformDiagnosticsSnapshot>(never.Task)
            : ValueTask.FromResult(HealthySnapshot(22));
    }

    await using var harness = new DiagnosticsHarness(
        Provider, serverTimeout: TimeSpan.FromMilliseconds(100));
    await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => harness.Client.GetSnapshotAsync().AsTask());
    var recovered = await harness.Client.GetSnapshotAsync();
    Assert.Equal(22L, recovered.Revision);
}

static async Task InvalidSnapshotFailsClosed()
{
    var invalid = HealthySnapshot(13) with
    {
        Workers = Enumerable.Range(0, PlatformDiagnosticsSnapshot.MaximumWorkers + 1)
            .Select(index => new PlatformWorkerDiagnostic(
                $"widget-{index}", "Widget", false, 0, null, false))
            .ToArray(),
    };
    await using var harness = new DiagnosticsHarness(_ => ValueTask.FromResult(invalid));
    await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => harness.Client.GetSnapshotAsync().AsTask());
}

static PlatformDiagnosticsSnapshot HealthySnapshot(long revision) => new(
    PlatformDiagnosticsSnapshot.CurrentSchemaVersion,
    revision,
    PlatformDiagnosticsSnapshot.Area("bridge", "Bridge", PlatformDiagnosticState.Healthy,
        "Native host session is connected"),
    PlatformDiagnosticsSnapshot.Area("catalog", "Widget catalog", PlatformDiagnosticState.Healthy,
        "Revision 1; 1 widgets validated"),
    PlatformDiagnosticsSnapshot.Area("appearance", "Appearance", PlatformDiagnosticState.Healthy,
        "Revision 1; active theme validated"),
    PlatformDiagnosticsSnapshot.Area("providers", "Platform providers", PlatformDiagnosticState.Healthy,
        "Audio and network providers are available on demand"),
    PlatformDiagnosticsSnapshot.Area("consent", "Permissions", PlatformDiagnosticState.Healthy,
        "Revision 1; 0 decisions; 0 denied"),
    PlatformDiagnosticsSnapshot.Area("overlay", "Overlay host", PlatformDiagnosticState.Unavailable,
        "Host telemetry is not reported by this build"),
    PlatformDiagnosticsSnapshot.Area("guide", "Guide input", PlatformDiagnosticState.Unavailable,
        "Host telemetry is not reported by this build"),
    [new PlatformWorkerDiagnostic("audio-mixer", "Audio Mixer", true, 1, null, false)]);

file sealed class DiagnosticsHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PlatformDiagnosticsPipeServer _server;
    private readonly Task _run;

    public DiagnosticsHarness(
        Func<CancellationToken, ValueTask<PlatformDiagnosticsSnapshot>> provider,
        TimeSpan? serverTimeout = null,
        TimeSpan? clientTimeout = null)
    {
        PipeName = $"gba-diagnostics-test-{Guid.NewGuid():N}";
        _server = new PlatformDiagnosticsPipeServer(PipeName, provider, serverTimeout);
        _server.BindExpectedClientProcess(Environment.ProcessId);
        Client = new PlatformDiagnosticsPipeClient(
            PipeName, _server.ChannelNonce, Environment.ProcessId,
            clientTimeout ?? TimeSpan.FromSeconds(2));
        _run = _server.RunAsync(_shutdown.Token);
    }

    public string PipeName { get; }
    public string ChannelNonce => _server.ChannelNonce;
    public PlatformDiagnosticsPipeClient Client { get; }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        await _server.DisposeAsync();
        try { await _run.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }
}

file static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
