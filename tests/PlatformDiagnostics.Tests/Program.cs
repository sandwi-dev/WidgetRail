using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameBarAlternative.PlatformDiagnostics;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bound worker receives a validated sanitized snapshot", AuthenticatedRoundTrip),
    ("Authenticated worker retries only the exact recovery confirmation token", AuthenticatedRecoveryRetry),
    ("Trusted worker inspects and clears only exact widget local data", WidgetLocalDataRoundTrip),
    ("Trusted worker uninstalls only an exact path-free package identity", WidgetPackageUninstallRoundTrip),
    ("Recovery retry reports stale and refused outcomes without mutation ambiguity", RecoveryRetryStaleAndRefused),
    ("Recovery retry timeout and caller cancellation fail closed", RecoveryRetryCancellationIsBounded),
    ("Recovery retry results enforce closed bounded diagnostics", RecoveryRetryResultIsBounded),
    ("Malformed and unsupported recovery requests fail one connection closed", MalformedRecoveryRequestsRecover),
    ("Recovery diagnostics enforce bounds and reject authority evidence leakage", RecoveryDiagnosticsAreBoundedAndSanitized),
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

static async Task AuthenticatedRecoveryRetry()
{
    var token = ConfirmationToken('A');
    var calls = 0;
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(31) with
        {
            AuthorityRecoveries = [RecoveryDiagnostic('1', token)],
        }),
        retry: (observed, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(token, observed);
            Interlocked.Increment(ref calls);
            return ValueTask.FromResult(new PlatformAuthorityRecoveryRetryResult(
                PlatformAuthorityRecoveryRetryStatus.Recovered,
                "recovered"));
        });

    var snapshot = await harness.Client.GetSnapshotAsync();
    Assert.Equal(token, snapshot.AuthorityRecoveries.Single().ConfirmationToken);
    var result = await harness.Client.RetryAuthorityRecoveryAsync(token);
    Assert.Equal(PlatformAuthorityRecoveryRetryStatus.Recovered, result.Status);
    Assert.Equal("recovered", result.Code);
    Assert.Equal(1, calls);

    var wrongNonce = new PlatformDiagnosticsPipeClient(
        harness.PipeName, new string('0', 64), Environment.ProcessId,
        TimeSpan.FromSeconds(1));
    var rejection = await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => wrongNonce.RetryAuthorityRecoveryAsync(token).AsTask());
    Assert.Equal("authentication_failed", rejection.Code);
    Assert.Equal(1, calls);

    await Assert.ThrowsAsync<ArgumentException>(() =>
        harness.Client.RetryAuthorityRecoveryAsync(token.ToLowerInvariant()).AsTask());
    Assert.Equal(1, calls);
}

static async Task WidgetLocalDataRoundTrip()
{
    var token = ConfirmationToken('7', legacy: true);
    var inspected = 0;
    var cleared = 0;
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(51)),
        inspect: (widgetId, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("game-launcher", widgetId);
            Interlocked.Increment(ref inspected);
            return ValueTask.FromResult(new PlatformWidgetLocalDataInspection(
                widgetId, "Game Launcher", true, "local_data_present", token));
        },
        clear: (widgetId, observed, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("game-launcher", widgetId);
            Assert.Equal(token, observed);
            Interlocked.Increment(ref cleared);
            return ValueTask.FromResult(new PlatformWidgetLocalDataClearResult(
                PlatformWidgetLocalDataClearStatus.Cleared, "cleared"));
        });

    var result = await harness.Client.InspectWidgetLocalDataAsync("game-launcher");
    Assert.True(result.Exists);
    Assert.Equal(token, result.ConfirmationToken);
    var clear = await harness.Client.ClearWidgetLocalDataAsync("game-launcher", token);
    Assert.Equal(PlatformWidgetLocalDataClearStatus.Cleared, clear.Status);
    Assert.Equal(1, inspected);
    Assert.Equal(1, cleared);
    await Assert.ThrowsAsync<PlatformDiagnosticsException>(() =>
        harness.Client.InspectWidgetLocalDataAsync("missing/widget").AsTask());
    await Assert.ThrowsAsync<ArgumentException>(() =>
        harness.Client.ClearWidgetLocalDataAsync("game-launcher", "bad").AsTask());
    Assert.Equal(1, inspected);
    Assert.Equal(1, cleared);
}

static async Task WidgetPackageUninstallRoundTrip()
{
    var token = ConfirmationToken('6', legacy: true);
    var inspected = 0;
    var uninstalled = 0;
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(52)),
        uninstallInspect: (widgetId, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("community.widget", widgetId);
            Interlocked.Increment(ref inspected);
            return ValueTask.FromResult(new PlatformWidgetPackageUninstallInspection(
                widgetId, "Community Widget", "installed.ABCD", "2.0.0", 2,
                true, "ready", token));
        },
        uninstall: (widgetId, publisherId, version, observed, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("community.widget", widgetId);
            Assert.Equal("installed.ABCD", publisherId);
            Assert.Equal("2.0.0", version);
            Assert.Equal(token, observed);
            Interlocked.Increment(ref uninstalled);
            return ValueTask.FromResult(new PlatformWidgetPackageUninstallResult(
                PlatformWidgetPackageUninstallStatus.Uninstalled, "uninstalled"));
        });

    var inspection = await harness.Client
        .InspectWidgetPackageUninstallAsync("community.widget");
    Assert.Equal(token, inspection.ConfirmationToken);
    var result = await harness.Client.UninstallWidgetPackageAsync(
        inspection.WidgetId, inspection.PublisherId, inspection.ActiveVersion, token);
    Assert.Equal(PlatformWidgetPackageUninstallStatus.Uninstalled, result.Status);
    Assert.Equal(1, inspected);
    Assert.Equal(1, uninstalled);

    await Assert.ThrowsAsync<PlatformDiagnosticsException>(() =>
        harness.Client.InspectWidgetPackageUninstallAsync("bad/path").AsTask());
    await Assert.ThrowsAsync<PlatformDiagnosticsException>(() =>
        harness.Client.UninstallWidgetPackageAsync(
            "community.widget", "bad/publisher", "2.0.0", token).AsTask());
    await Assert.ThrowsAsync<ArgumentException>(() =>
        harness.Client.UninstallWidgetPackageAsync(
            "community.widget", "installed.ABCD", "2.0.0", "bad").AsTask());
    Assert.Equal(1, inspected);
    Assert.Equal(1, uninstalled);
}

static async Task RecoveryRetryStaleAndRefused()
{
    var stale = ConfirmationToken('B');
    var refused = ConfirmationToken('C', legacy: true);
    var unavailable = ConfirmationToken('8');
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(32)),
        retry: (token, _) => ValueTask.FromResult(token switch
        {
            var value when string.Equals(value, stale, StringComparison.Ordinal) =>
                new PlatformAuthorityRecoveryRetryResult(
                    PlatformAuthorityRecoveryRetryStatus.Stale, "confirmation_stale"),
            var value when string.Equals(value, refused, StringComparison.Ordinal) =>
                PlatformAuthorityRecoveryRetryResult.Refused("recovery_refused"),
            var value when string.Equals(value, unavailable, StringComparison.Ordinal) =>
                new PlatformAuthorityRecoveryRetryResult(
                    PlatformAuthorityRecoveryRetryStatus.Unavailable,
                    "recovery_unavailable"),
            _ => throw new InvalidOperationException("Unexpected retry token."),
        }));

    var staleResult = await harness.Client.RetryAuthorityRecoveryAsync(stale);
    Assert.Equal(PlatformAuthorityRecoveryRetryStatus.Stale, staleResult.Status);
    Assert.Equal("confirmation_stale", staleResult.Code);
    var refusedResult = await harness.Client.RetryAuthorityRecoveryAsync(refused);
    Assert.Equal(PlatformAuthorityRecoveryRetryStatus.Refused, refusedResult.Status);
    Assert.Equal("recovery_refused", refusedResult.Code);
    var unavailableResult = await harness.Client.RetryAuthorityRecoveryAsync(unavailable);
    Assert.Equal(PlatformAuthorityRecoveryRetryStatus.Unavailable, unavailableResult.Status);
    Assert.Equal("recovery_unavailable", unavailableResult.Code);

    await using var unsupported = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(33)));
    var unsupportedResult = await unsupported.Client.RetryAuthorityRecoveryAsync(stale);
    Assert.Equal(PlatformAuthorityRecoveryRetryStatus.Refused, unsupportedResult.Status);
    Assert.Equal("retry_unsupported", unsupportedResult.Code);
}

static async Task RecoveryRetryCancellationIsBounded()
{
    var token = ConfirmationToken('9');
    var entered = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelled = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(41)),
        serverTimeout: TimeSpan.FromMilliseconds(100),
        clientTimeout: TimeSpan.FromSeconds(1),
        retry: async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Cancelled retry unexpectedly resumed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult();
                throw;
            }
        });

    var timeout = await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => harness.Client.RetryAuthorityRecoveryAsync(token).AsTask());
    Assert.True(timeout.Code is "diagnostics_unavailable" or "diagnostics_timeout");
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(1));
    await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(41L, (await harness.Client.GetSnapshotAsync()).Revision);

    using var callerCancellation = new CancellationTokenSource();
    callerCancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() =>
        harness.Client.RetryAuthorityRecoveryAsync(token, callerCancellation.Token)
            .AsTask());
}

static async Task RecoveryRetryResultIsBounded()
{
    var invalid = ConfirmationToken('D');
    var valid = ConfirmationToken('E');
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(40)),
        retry: (token, _) => ValueTask.FromResult(
            string.Equals(token, invalid, StringComparison.Ordinal)
                ? new PlatformAuthorityRecoveryRetryResult(
                    PlatformAuthorityRecoveryRetryStatus.StillPending,
                    "C:\\private\\authority.pending.json")
                : new PlatformAuthorityRecoveryRetryResult(
                    PlatformAuthorityRecoveryRetryStatus.Recovered,
                    "recovered")));

    var rejection = await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => harness.Client.RetryAuthorityRecoveryAsync(invalid).AsTask());
    Assert.True(rejection.Code is "diagnostics_unavailable" or "invalid_retry_result");
    var recovered = await harness.Client.RetryAuthorityRecoveryAsync(valid);
    Assert.Equal(PlatformAuthorityRecoveryRetryStatus.Recovered, recovered.Status);
}

static async Task MalformedRecoveryRequestsRecover()
{
    var retries = 0;
    await using var harness = new DiagnosticsHarness(
        _ => ValueTask.FromResult(HealthySnapshot(34)),
        retry: (_, _) =>
        {
            Interlocked.Increment(ref retries);
            return ValueTask.FromResult(new PlatformAuthorityRecoveryRetryResult(
                PlatformAuthorityRecoveryRetryStatus.Recovered, "recovered"));
        });

    await SendRejectedRequestAsync(
        harness, new { operation = "retry-authority-recovery" });
    await SendRejectedRequestAsync(
        harness, new
        {
            operation = "retry-authority-recovery",
            confirmationToken = new string('a', 64),
        });
    foreach (var malformed in new[]
             {
                 new string('A', 31),
                 new string('A', 33),
                 new string('A', 63),
                 new string('A', 65),
                 new string('G', 32),
             })
        await SendRejectedRequestAsync(
            harness, new
            {
                operation = "retry-authority-recovery",
                confirmationToken = malformed,
            });
    await SendRejectedRequestAsync(
        harness, new { operation = "unknown-operation" });
    Assert.Equal(0, retries);
    Assert.Equal(34L, (await harness.Client.GetSnapshotAsync()).Revision);
}

static async Task RecoveryDiagnosticsAreBoundedAndSanitized()
{
    var valid = HealthySnapshot(35) with
    {
        AuthorityRecoveries =
        [
            RecoveryDiagnostic('2', ConfirmationToken('D')),
        ],
    };
    await using (var harness = new DiagnosticsHarness(_ => ValueTask.FromResult(valid)))
    {
        using var raw = await ReadRawSnapshotAsync(harness);
        var json = raw.RootElement.GetRawText();
        Assert.True(!json.Contains("C:\\\\private", StringComparison.Ordinal));
        Assert.True(!json.Contains("S-1-15-2", StringComparison.Ordinal));
        Assert.True(!json.Contains("D:(A;;", StringComparison.Ordinal));
        Assert.True(!json.Contains("accessDescriptor", StringComparison.Ordinal));
        Assert.True(!json.Contains("objectIdentity", StringComparison.Ordinal));
    }

    var tooMany = HealthySnapshot(36) with
    {
        AuthorityRecoveries = Enumerable.Range(
                0, PlatformDiagnosticsSnapshot.MaximumAuthorityRecoveries + 1)
            .Select(index => RecoveryDiagnosticWithId(
                index.ToString("X32"), (index + 1).ToString("X32")))
            .ToArray(),
    };
    await AssertSnapshotRejectedAsync(tooMany);
    await AssertSnapshotRejectedAsync(HealthySnapshot(37) with
    {
        AuthorityRecoveries =
        [
            RecoveryDiagnostic('3', ConfirmationToken('E')) with
            {
                DisplayName = new string('x', 161),
            },
        ],
    });
    await AssertSnapshotRejectedAsync(HealthySnapshot(37) with
    {
        AuthorityRecoveries =
        [
            RecoveryDiagnostic('3', ConfirmationToken('E')) with
            {
                DisplayName = "C:\\private\\authority.pending.json",
            },
        ],
    });
    await AssertSnapshotRejectedAsync(HealthySnapshot(37) with
    {
        AuthorityRecoveries =
        [
            RecoveryDiagnostic('3', ConfirmationToken('E')) with
            {
                DisplayName = "S-1-15-2-unsafe-profile",
            },
        ],
    });
    await AssertSnapshotRejectedAsync(HealthySnapshot(38) with
    {
        AuthorityRecoveries =
        [
            RecoveryDiagnostic('4', ConfirmationToken('F')) with
            {
                StatusCode = "D:(A;;raw-descriptor)",
            },
        ],
    });
    await AssertSnapshotRejectedAsync(HealthySnapshot(39) with
    {
        SchemaVersion = PlatformDiagnosticsSnapshot.CurrentSchemaVersion - 1,
    });
}

static PlatformAuthorityRecoveryDiagnostic RecoveryDiagnostic(
    char recoveryIdCharacter,
    string confirmationToken) => RecoveryDiagnosticWithId(
        new string(recoveryIdCharacter, PlatformDiagnosticsSnapshot.RecoveryIdLength),
        confirmationToken);

static PlatformAuthorityRecoveryDiagnostic RecoveryDiagnosticWithId(
    string recoveryId,
    string confirmationToken) => new(
        recoveryId,
        "Example Widget",
        PlatformAuthorityRecoveryState.Blocked,
        "identity_mismatch",
        CanRetry: true,
        confirmationToken);

static string ConfirmationToken(char character, bool legacy = false) => new(
    character,
    legacy
        ? PlatformDiagnosticsSnapshot.LegacyConfirmationTokenLength
        : PlatformDiagnosticsSnapshot.ConfirmationTokenLength);

static async Task SendRejectedRequestAsync<T>(DiagnosticsHarness harness, T request)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await using var peer = new NamedPipeClientStream(
        ".", harness.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await peer.ConnectAsync(timeout.Token);
    await WriteTestFrame(peer, new { nonce = harness.ChannelNonce }, timeout.Token);
    using (var acknowledgement = await ReadTestFrame(peer, timeout.Token))
        Assert.Equal(true, acknowledgement.RootElement.GetProperty("accepted").GetBoolean());
    await WriteTestFrame(peer, request, timeout.Token);
    var oneByte = new byte[1];
    try
    {
        var received = await peer.ReadAsync(oneByte, timeout.Token);
        Assert.Equal(0, received);
    }
    catch (IOException)
    {
        // A rejected named-pipe request can surface either EOF or a broken-pipe
        // IOException depending on which endpoint observes disconnect first.
    }
}

static async Task<JsonDocument> ReadRawSnapshotAsync(DiagnosticsHarness harness)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    await using var peer = new NamedPipeClientStream(
        ".", harness.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await peer.ConnectAsync(timeout.Token);
    await WriteTestFrame(peer, new { nonce = harness.ChannelNonce }, timeout.Token);
    using (var acknowledgement = await ReadTestFrame(peer, timeout.Token))
        Assert.Equal(true, acknowledgement.RootElement.GetProperty("accepted").GetBoolean());
    await WriteTestFrame(peer, new { operation = "snapshot" }, timeout.Token);
    var snapshot = await ReadTestFrame(peer, timeout.Token);
    await WriteTestFrame(peer, new { operation = "snapshot" }, timeout.Token);
    return snapshot;
}

static async Task AssertSnapshotRejectedAsync(PlatformDiagnosticsSnapshot snapshot)
{
    await using var harness = new DiagnosticsHarness(_ => ValueTask.FromResult(snapshot));
    await Assert.ThrowsAsync<PlatformDiagnosticsException>(
        () => harness.Client.GetSnapshotAsync().AsTask());
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
        TimeSpan? clientTimeout = null,
        Func<string, CancellationToken,
            ValueTask<PlatformAuthorityRecoveryRetryResult>>? retry = null,
        Func<string, CancellationToken,
            ValueTask<PlatformWidgetLocalDataInspection>>? inspect = null,
        Func<string, string, CancellationToken,
            ValueTask<PlatformWidgetLocalDataClearResult>>? clear = null,
        Func<string, CancellationToken,
            ValueTask<PlatformWidgetPackageUninstallInspection>>? uninstallInspect = null,
        Func<string, string, string, string, CancellationToken,
            ValueTask<PlatformWidgetPackageUninstallResult>>? uninstall = null)
    {
        PipeName = $"gba-diagnostics-test-{Guid.NewGuid():N}";
        _server = new PlatformDiagnosticsPipeServer(
            PipeName, provider, serverTimeout, retry, inspect, clear,
            uninstallInspect, uninstall);
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
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected condition to be true.");
    }

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
