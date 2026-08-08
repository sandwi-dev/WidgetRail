using System.Net;
using System.Net.Sockets;
using System.Text;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WindowsCommunityProvider;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Credential targets are package-authority scoped across updates", CredentialTargetsAreScoped),
    ("Windows Credential Manager stores replaces and deletes a bounded secret", CredentialRoundTrip),
    ("Loopback provider injects Bearer without following redirects", BearerAndRedirectPolicy),
    ("Loopback provider atomically invalidates a rejected Bearer on the lifecycle lease", UnauthorizedBearerInvalidation),
    ("Loopback provider normalizes non-JSON errors and rejects non-JSON success", JsonResponsePolicy),
    ("Loopback provider caps streaming responses and request duration", ResponseAndTimeoutBounds),
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
