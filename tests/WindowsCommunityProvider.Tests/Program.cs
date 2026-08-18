using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using WidgetRail.PlatformBroker;
using WidgetRail.WindowsCommunityProvider;

if (args is ["--private-state-write", var stateRoot, var publisher, var package])
{
    try
    {
        var childStore = new WindowsPrivateStateStore(stateRoot);
        var result = await childStore.WriteAsync(
            new BrokerWidgetIdentity(package, publisher, "child"),
            new WritePrivateStateRequest(CanonicalBase64("{\"owner\":\"child\"}"), 0),
            default);
        Console.WriteLine(result.Revision);
        Environment.ExitCode = 0;
    }
    catch (BrokerException exception) when (exception.Code == "state_conflict")
    {
        Console.Error.WriteLine(exception.Code);
        Environment.ExitCode = 2;
    }
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Credential targets are package-authority scoped across updates", CredentialTargetsAreScoped),
    ("Windows Credential Manager stores replaces and deletes a bounded secret", CredentialRoundTrip),
    ("Loopback provider injects Bearer without following redirects", BearerAndRedirectPolicy),
    ("Loopback provider atomically invalidates a rejected Bearer on the lifecycle lease", UnauthorizedBearerInvalidation),
    ("Loopback provider normalizes non-JSON errors and rejects non-JSON success", JsonResponsePolicy),
    ("Loopback provider caps streaming responses and request duration", ResponseAndTimeoutBounds),
    ("Private state isolates authorities persists tombstones and enforces CAS", PrivateStatePersistenceAndCas),
    ("Private state serializes CAS across host processes", PrivateStateCrossProcessCas),
    ("Private state rejects corruption reparse paths and quota violations", PrivateStateFailsClosed),
    ("Private state write burst and cancellation are bounded", PrivateStateRateAndCancellation),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}
if (failures != 0) Environment.Exit(1);
Console.WriteLine($"WindowsCommunityProvider.Tests passed ({tests.Length} tests)");

static Task CredentialTargetsAreScoped()
{
    var first = new BrokerWidgetIdentity("dev.test.package", "dev.publisher", "version-1");
    var updated = first with { InstanceId = "version-2" };
    var otherPackage = first with { PackageId = "dev.test.other" };
    var otherPublisher = first with { PublisherId = "dev.other" };
    var target = WindowsCredentialPrivateSecretStore.BuildTarget(first, "session");
    Assert.Equal(target, WindowsCredentialPrivateSecretStore.BuildTarget(updated, "session"));
    Assert.NotEqual(target, WindowsCredentialPrivateSecretStore.BuildTarget(otherPackage, "session"));
    Assert.NotEqual(target, WindowsCredentialPrivateSecretStore.BuildTarget(otherPublisher, "session"));
    Assert.NotEqual(target, WindowsCredentialPrivateSecretStore.BuildTarget(first, "other"));
    Assert.True(!target.Contains(first.PackageId, StringComparison.Ordinal));
    Assert.True(!target.Contains(first.PublisherId, StringComparison.Ordinal));
    Assert.Throws<BrokerException>(() =>
        WindowsCredentialPrivateSecretStore.BuildTarget(first, "../escape"), "invalid_payload");
    return Task.CompletedTask;
}

static async Task CredentialRoundTrip()
{
    var store = new WindowsCredentialPrivateSecretStore();
    var identity = new BrokerWidgetIdentity(
        "dev.test.credential", "dev.test", $"test-{Guid.NewGuid():N}");
    var slot = $"test-{Guid.NewGuid():N}";
    try
    {
        var absent = await store.GetMetadataAsync(identity, slot, default);
        Assert.True(!absent.Exists);
        await store.SaveAsync(identity, slot, "first-secret", default);
        var metadata = await store.GetMetadataAsync(identity, slot, default);
        Assert.True(metadata.Exists);
        Assert.True(metadata.LastWrittenUnixMilliseconds > 0);
        Assert.Equal("first-secret", await store.ReadForHostUseAsync(identity, slot, default));
        await store.SaveAsync(identity, slot, "replacement", default);
        Assert.Equal("replacement", await store.ReadForHostUseAsync(identity, slot, default));
        Assert.Throws<BrokerException>(() => store.SaveAsync(identity, slot,
            new string('x', CommunityPlatformLimits.MaximumPrivateSecretUtf8Bytes + 1), default)
            .GetAwaiter().GetResult(), "invalid_payload");
    }
    finally
    {
        await store.DeleteAsync(identity, slot, default);
    }
    Assert.True(!(await store.GetMetadataAsync(identity, slot, default)).Exists);
}

static async Task BearerAndRedirectPolicy()
{
    var secrets = new FakeSecretStore("host-only-token");
    await using var backend = new WindowsCommunityPlatformBackend(secrets);
    await using var server = new OneShotHttpServer(request =>
    {
        Assert.Contains("GET /state HTTP/1.1", request);
        Assert.Contains("Authorization: Bearer host-only-token", request);
        return "HTTP/1.1 302 Found\r\nLocation: http://example.com/stolen\r\n" +
            "Set-Cookie: should-not-cross=1\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
    });
    var response = await backend.SendLoopbackJsonAsync(Identity(), server.Port, false,
        Request("/state", bearerSlot: "session"), default);
    Assert.Equal(302, response.StatusCode);
    Assert.Equal("{\"error\":\"non_json_response\"}", response.JsonBody);
    Assert.True(response.Headers.All(header =>
        !header.Name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)));
    await server.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    Assert.Equal(1, server.AcceptCount);
    Assert.Equal(1, secrets.ReadCalls);
}

static async Task JsonResponsePolicy()
{
    await using var backend = new WindowsCommunityPlatformBackend(new FakeSecretStore("unused"));
    await using (var errorServer = new OneShotHttpServer(_ =>
        "HTTP/1.1 500 Internal Server Error\r\nContent-Type: text/plain\r\n" +
        "Content-Length: 12\r\nConnection: close\r\n\r\nprivate path"))
    {
        var response = await backend.SendLoopbackJsonAsync(Identity(), errorServer.Port, false,
            Request("/error"), default);
        Assert.Equal(500, response.StatusCode);
        Assert.Equal("{\"error\":\"non_json_response\"}", response.JsonBody);
        Assert.True(!response.JsonBody.Contains("private path", StringComparison.Ordinal));
    }
    await using (var successServer = new OneShotHttpServer(_ =>
        "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n" +
        "Content-Length: 2\r\nConnection: close\r\n\r\nok"))
    {
        await Assert.ThrowsAsync<BrokerException>(() => backend.SendLoopbackJsonAsync(
            Identity(), successServer.Port, false, Request("/bad"), default), "invalid_response");
    }
}

static async Task UnauthorizedBearerInvalidation()
{
    var secrets = new FakeSecretStore("rejected-token", deleteDelay: TimeSpan.FromMilliseconds(300));
    await using var backend = new WindowsCommunityPlatformBackend(secrets);
    await using (var unauthorized = new OneShotHttpServer(_ =>
        "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\n" +
        "Content-Length: 2\r\nConnection: close\r\n\r\n{}"))
    {
        var response = await backend.SendLoopbackJsonAsync(
            Identity(), unauthorized.Port, false,
            Request("/state", "session", timeout: 150, invalidateUnauthorizedBearer: true),
            default);
        Assert.Equal(401, response.StatusCode);
        Assert.Equal(1, secrets.DeleteCalls);
        Assert.True(secrets.Deleted);
    }

    var canceledSecrets = new FakeSecretStore(
        "rejected-token", deleteDelay: TimeSpan.FromSeconds(2));
    await using var canceledBackend = new WindowsCommunityPlatformBackend(canceledSecrets);
    await using var canceledServer = new OneShotHttpServer(_ =>
        "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\n" +
        "Content-Length: 2\r\nConnection: close\r\n\r\n{}");
    using var lifetime = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    await Assert.ThrowsAsync<OperationCanceledException>(() => canceledBackend.SendLoopbackJsonAsync(
            Identity(), canceledServer.Port, false,
            Request("/state", "session", timeout: 1_000, invalidateUnauthorizedBearer: true),
            lifetime.Token), "");
    Assert.True(!canceledSecrets.Deleted);

    await using var invalidBackend = new WindowsCommunityPlatformBackend(
        new FakeSecretStore("unused"));
    await Assert.ThrowsAsync<BrokerException>(() => invalidBackend.SendLoopbackJsonAsync(
        Identity(), 13_091, false,
        Request("/state", invalidateUnauthorizedBearer: true), default), "invalid_payload");
}

static async Task ResponseAndTimeoutBounds()
{
    await using var backend = new WindowsCommunityPlatformBackend(new FakeSecretStore("unused"));
    await using (var oversized = new OneShotHttpServer(_ =>
        $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
        $"Content-Length: {CommunityPlatformLimits.MaximumLoopbackResponseBodyUtf8Bytes + 1}\r\n" +
        "Connection: close\r\n\r\n"))
    {
        await Assert.ThrowsAsync<BrokerException>(() => backend.SendLoopbackJsonAsync(
            Identity(), oversized.Port, false, Request("/large"), default), "response_too_large");
    }
    await using (var slow = new OneShotHttpServer(async (_, cancellationToken) =>
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        return "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}";
    }))
    {
        await Assert.ThrowsAsync<BrokerException>(() => backend.SendLoopbackJsonAsync(
            Identity(), slow.Port, false, Request("/slow", timeout: 50), default), "loopback_timeout");
    }
}

static async Task PrivateStatePersistenceAndCas()
{
    using var temp = new TemporaryDirectory("private-state-persistence");
    var clock = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(10_000));
    var first = new WindowsPrivateStateStore(temp.Path, clock);
    var second = new WindowsPrivateStateStore(temp.Path, clock);
    var identity = Identity();
    var updated = identity with { InstanceId = "updated-version" };

    var absent = await first.ReadAsync(identity, default);
    Assert.True(!absent.Exists);
    Assert.Equal(0L, absent.Revision);
    var firstWrite = await first.WriteAsync(identity,
        new WritePrivateStateRequest(CanonicalBase64("{\"a\":1,\"z\":2}"), 0), default);
    Assert.Equal(1L, firstWrite.Revision);
    var persisted = await second.ReadAsync(updated, default);
    Assert.True(persisted.Exists);
    Assert.Equal("{\"a\":1,\"z\":2}", Decode(persisted.CanonicalJsonBase64!));
    Assert.Equal(1L, persisted.Revision);

    Assert.Throws<BrokerException>(() => second.WriteAsync(identity,
        new WritePrivateStateRequest(CanonicalBase64("{}"), 0), default)
        .GetAwaiter().GetResult(), "state_conflict");
    Assert.Equal(2L, (await second.WriteAsync(updated,
        new WritePrivateStateRequest(CanonicalBase64("{\"next\":true}"), 1), default)).Revision);
    Assert.Equal(3L, (await first.ClearAsync(identity,
        new ClearPrivateStateRequest(2), default)).Revision);

    var restarted = new WindowsPrivateStateStore(temp.Path, clock);
    var tombstone = await restarted.ReadAsync(updated, default);
    Assert.True(!tombstone.Exists);
    Assert.Equal(3L, tombstone.Revision);
    Assert.Equal(0L, (await restarted.ReadAsync(
        identity with { PublisherId = "dev.other" }, default)).Revision);
    Assert.Equal(0L, (await restarted.ReadAsync(
        identity with { PackageId = "dev.other.package" }, default)).Revision);

    var names = Directory.GetFiles(temp.Path).Select(Path.GetFileName).ToArray();
    Assert.True(names.All(name => name is not null &&
        !name.Contains(identity.PublisherId, StringComparison.Ordinal) &&
        !name.Contains(identity.PackageId, StringComparison.Ordinal)));
}

static async Task PrivateStateCrossProcessCas()
{
    using var temp = new TemporaryDirectory("private-state-process-cas");
    var executable = Environment.ProcessPath ??
        throw new InvalidOperationException("Test process path was unavailable.");
    Process Start()
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--private-state-write");
        start.ArgumentList.Add(temp.Path);
        start.ArgumentList.Add("dev.publisher");
        start.ArgumentList.Add("dev.test.package");
        return Process.Start(start) ?? throw new InvalidOperationException("Child process failed to start.");
    }

    using var first = Start();
    using var second = Start();
    await Task.WhenAll(first.WaitForExitAsync(), second.WaitForExitAsync())
        .WaitAsync(TimeSpan.FromSeconds(10));
    var exitCodes = new[] { first.ExitCode, second.ExitCode }.Order().ToArray();
    Assert.Equal(0, exitCodes[0]);
    Assert.Equal(2, exitCodes[1]);
    var store = new WindowsPrivateStateStore(temp.Path);
    Assert.Equal(1L, (await store.ReadAsync(Identity(), default)).Revision);
}

static async Task PrivateStateFailsClosed()
{
    using var quota = new TemporaryDirectory("private-state-quota");
    var store = new WindowsPrivateStateStore(quota.Path);
    var maximum = "{\"x\":\"" +
        new string('x', CommunityPlatformLimits.MaximumPrivateStateUtf8Bytes - 8) + "\"}";
    Assert.Equal(CommunityPlatformLimits.MaximumPrivateStateUtf8Bytes,
        Encoding.UTF8.GetByteCount(maximum));
    await store.WriteAsync(Identity(),
        new WritePrivateStateRequest(CanonicalBase64(maximum), 0), default);
    var tooLarge = "{\"x\":\"" +
        new string('x', CommunityPlatformLimits.MaximumPrivateStateUtf8Bytes - 7) + "\"}";
    Assert.Throws<BrokerException>(() => store.WriteAsync(Identity(),
        new WritePrivateStateRequest(Convert.ToBase64String(Encoding.UTF8.GetBytes(tooLarge)), 1),
        default).GetAwaiter().GetResult(), "invalid_payload");
    Assert.Throws<BrokerException>(() => store.WriteAsync(Identity(),
        new WritePrivateStateRequest(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(" {\"a\":1}")), 1), default)
        .GetAwaiter().GetResult(), "invalid_payload");

    var dataPath = Directory.GetFiles(quota.Path, "*.json").Single();
    await File.WriteAllTextAsync(dataPath, "{\"revision\":1,\"revision\":2}");
    Assert.Throws<BrokerException>(() => new WindowsPrivateStateStore(quota.Path)
        .ReadAsync(Identity(), default).GetAwaiter().GetResult(), "state_corrupt");

    if (OperatingSystem.IsWindows())
    {
        using var unsafeRoot = new TemporaryDirectory("private-state-reparse");
        var target = Path.Combine(unsafeRoot.Path, "target");
        var link = Path.Combine(unsafeRoot.Path, "link");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Assert.Throws<BrokerException>(() => new WindowsPrivateStateStore(link)
                .ReadAsync(Identity(), default).GetAwaiter().GetResult(), "unsafe_state_store");
        }
        catch (UnauthorizedAccessException)
        {
            // Developer Mode may be disabled; production code is still covered
            // by the existing-reparse check when the OS permits test creation.
        }
    }
}

static async Task PrivateStateRateAndCancellation()
{
    using var temp = new TemporaryDirectory("private-state-rate");
    var clock = new ManualTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(20_000));
    var store = new WindowsPrivateStateStore(temp.Path, clock);
    for (var index = 0; index < WindowsPrivateStateStore.WriteBurstCapacity; index++)
        await store.WriteAsync(Identity(),
            new WritePrivateStateRequest(CanonicalBase64($"{{\"value\":{index}}}"), null),
            default);
    Assert.Throws<BrokerException>(() => store.WriteAsync(Identity(),
        new WritePrivateStateRequest(CanonicalBase64("{}"), null), default)
        .GetAwaiter().GetResult(), "state_rate_limited");
    clock.Advance(TimeSpan.FromSeconds(1));
    Assert.Equal(WindowsPrivateStateStore.WriteBurstCapacity + 1L,
        (await store.WriteAsync(Identity(),
            new WritePrivateStateRequest(CanonicalBase64("{}"), null), default)).Revision);

    using var canceledRoot = new TemporaryDirectory("private-state-cancel");
    var canceledStore = new WindowsPrivateStateStore(canceledRoot.Path);
    using var canceled = new CancellationTokenSource();
    canceled.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => canceledStore.WriteAsync(
        Identity(), new WritePrivateStateRequest(CanonicalBase64("{}"), 0), canceled.Token), "");
    Assert.Equal(0L, (await canceledStore.ReadAsync(Identity(), default)).Revision);

    // Hold the durable authority lock from this process and prove a separate
    // store instance observes cancellation while waiting rather than writing.
    var lockPath = Directory.GetFiles(canceledRoot.Path, "*.lock").Single();
    await using (var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite,
                     FileShare.None))
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new WindowsPrivateStateStore(canceledRoot.Path).WriteAsync(
                Identity(), new WritePrivateStateRequest(CanonicalBase64("{}"), 0), wait.Token), "");
    }
    Assert.Equal(0L, (await canceledStore.ReadAsync(Identity(), default)).Revision);
}

static string CanonicalBase64(string json) =>
    Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

static string Decode(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));

static BrokerWidgetIdentity Identity() => new("dev.test.package", "dev.publisher", "default");

static LoopbackJsonRequest Request(
    string path,
    string? bearerSlot = null,
    int timeout = 1_000,
    bool invalidateUnauthorizedBearer = false) =>
    new(path, [], bearerSlot, null, timeout, invalidateUnauthorizedBearer);

file sealed class FakeSecretStore(
    string secret,
    TimeSpan? deleteDelay = null) : IPrivateSecretStore
{
    public int ReadCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public bool Deleted { get; private set; }
    public Task<PrivateSecretMetadataSummary> GetMetadataAsync(
        BrokerWidgetIdentity identity, string slot, CancellationToken cancellationToken) =>
        Task.FromResult(new PrivateSecretMetadataSummary(true, 1));
    public Task SaveAsync(BrokerWidgetIdentity identity, string slot, string secret,
        CancellationToken cancellationToken) => Task.CompletedTask;
    public async Task DeleteAsync(BrokerWidgetIdentity identity, string slot,
        CancellationToken cancellationToken)
    {
        DeleteCalls++;
        if (deleteDelay is { } delay) await Task.Delay(delay, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Deleted = true;
    }
    public Task<string> ReadForHostUseAsync(BrokerWidgetIdentity identity, string slot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCalls++;
        return Task.FromResult(secret);
    }
}

file sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset _utcNow = initial;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void Advance(TimeSpan duration) => _utcNow += duration;
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string name)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "wrail-community-provider-tests", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

file sealed class OneShotHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Func<string, CancellationToken, Task<string>> _response;
    private readonly CancellationTokenSource _lifetime = new();
    public OneShotHttpServer(Func<string, string> response) :
        this((request, _) => Task.FromResult(response(request))) { }
    public OneShotHttpServer(Func<string, CancellationToken, Task<string>> response)
    {
        _response = response;
        _listener.Start(1);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Completion = RunAsync(_lifetime.Token);
    }
    public int Port { get; }
    public int AcceptCount { get; private set; }
    public Task Completion { get; }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
        AcceptCount++;
        await using var stream = client.GetStream();
        var request = await ReadHeadersAsync(stream, cancellationToken);
        var response = await _response(request, cancellationToken);
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static async Task<string> ReadHeadersAsync(
        NetworkStream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];
        while (bytes.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(buffer, cancellationToken) == 0) break;
            bytes.Add(buffer[0]);
            var count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' &&
                bytes[count - 2] == '\r' && bytes[count - 1] == '\n') break;
        }
        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _listener.Stop();
        try { await Completion.WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (Exception exception) when (exception is OperationCanceledException or
            ObjectDisposedException or SocketException or TimeoutException) { }
        _lifetime.Dispose();
    }
}

file static class Assert
{
    public static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
    public static void NotEqual<T>(T unexpected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(unexpected, actual))
            throw new InvalidOperationException($"Did not expect {unexpected}.");
    }
    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected '{expected}' in '{actual}'.");
    }
    public static void Throws<T>(Action action, string code) where T : Exception
    {
        try { action(); }
        catch (T exception)
        {
            if (exception is BrokerException broker && broker.Code != code)
                throw new InvalidOperationException($"Expected {code}, got {broker.Code}.");
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    public static async Task ThrowsAsync<T>(Func<Task> action, string code) where T : Exception
    {
        try { await action(); }
        catch (T exception)
        {
            if (exception is BrokerException broker && broker.Code != code)
                throw new InvalidOperationException($"Expected {code}, got {broker.Code}.");
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
