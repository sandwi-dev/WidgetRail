using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using WidgetRail.Playnite;

namespace WidgetRail.Playnite.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LocalServerTests
{
    [TestMethod]
    public void RawSocketCollisionUsesNextPortAndAllOccupiedFails()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Server.ExclusiveAddressUse = true; occupied.Start();
        var first = ((IPEndPoint)occupied.LocalEndpoint).Port;
        var credentials = IntegrationCredentials.Create();
        using var blocked = new LocalApiServer(credentials.Token, Guid.NewGuid().ToString("D"), new Echo());
        Assert.Throws<IOException>(() => blocked.Start([first]));
        using var available = new LocalApiServer(credentials.Token, Guid.NewGuid().ToString("D"), new Echo());
        var second = Port();
        Assert.AreEqual(second, available.Start([first, second]));
    }

    [TestMethod]
    public async Task BodiesRequireJsonAndReadRequestsCannotCarryCommands()
    {
        var credentials = IntegrationCredentials.Create(); var epoch = Guid.NewGuid().ToString("D");
        var dispatcher = new Echo();
        using var server = new LocalApiServer(credentials.Token, epoch, dispatcher);
        using var client = Client(server.Start([Port()]), credentials.Token, epoch);
        using var text = new StringContent("{}");
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("v1/test", text)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/test") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        Assert.AreEqual(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task CollisionUsesNextPortAndRejectsWrongIdentityAndOrigin()
    {
        var first = Port(); var second = Port();
        while (second == first) second = Port();
        using var occupied = new HttpListener();
        occupied.Prefixes.Add($"http://127.0.0.1:{first}/"); occupied.Start();
        var credential = IntegrationCredentials.Create();
        var epoch = Guid.NewGuid().ToString("D");
        var dispatcher = new Echo();
        using var server = new LocalApiServer(credential.Token, epoch, dispatcher);
        Assert.AreEqual(second, server.Start([first, second]));
        using var client = Client(second);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("v1/connection")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.GetAsync("v1/connection")).StatusCode);
        client.DefaultRequestHeaders.Add("X-WidgetRail-Instance", epoch);
        Assert.AreEqual("{}", await client.GetStringAsync("v1/connection"));
        client.DefaultRequestHeaders.Add("Origin", "https://example.com");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync("v1/connection")).StatusCode);
        Assert.AreEqual(1, dispatcher.Calls);
        await server.StopAsync();
        Assert.AreEqual(0, server.ActiveRequests);
    }

    [TestMethod]
    public async Task OversizedAndFailedRequestsDoNotKillSubsequentRequests()
    {
        var credential = IntegrationCredentials.Create(); var epoch = Guid.NewGuid().ToString("D");
        using var server = new LocalApiServer(credential.Token, epoch, new Echo());
        var port = server.Start([Port()]);
        using var client = Client(port, credential.Token, epoch);
        using var oversized = new ByteArrayContent(new byte[IntegrationIdentity.MaximumRequestBytes + 1]);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("v1/test", oversized)).StatusCode);
        Assert.AreEqual(HttpStatusCode.InternalServerError, (await client.GetAsync("v1/fail")).StatusCode);
        Assert.AreEqual("{}", await client.GetStringAsync("v1/connection"));
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, (await client.DeleteAsync("v1/test")).StatusCode);
    }

    [TestMethod]
    public async Task ShutdownCancelsInflightRequestsAndReleasesListener()
    {
        var credential = IntegrationCredentials.Create(); var epoch = Guid.NewGuid().ToString("D");
        var dispatcher = new Waiting();
        using var server = new LocalApiServer(credential.Token, epoch, dispatcher);
        var port = server.Start([Port()]);
        using var client = Client(port, credential.Token, epoch);
        var request = client.GetAsync("v1/events");
        await dispatcher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await server.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        try { using var response = await request; } catch (HttpRequestException) { }
        Assert.IsTrue(dispatcher.Cancelled);
        using var next = new HttpListener(); next.Prefixes.Add($"http://127.0.0.1:{port}/"); next.Start();
    }

    [TestMethod]
    public void DescriptorRoundTripAndOldInstanceCannotClearNewDescriptor()
    {
        var root = Path.Combine(Path.GetTempPath(), "widgetrail-playnite-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, IntegrationIdentity.DescriptorFile);
        var original = ConnectionDescriptor.Create(Guid.NewGuid().ToString("D"), 19822);
        try
        {
            IntegrationFiles.Write(path, original);
            var read = IntegrationFiles.Read<ConnectionDescriptor>(path)!;
            Assert.IsTrue(read.IsValid()); Assert.AreEqual(original.InstanceId, read.InstanceId);
            var replacement = ConnectionDescriptor.Create(original.ProfileId, 19823);
            IntegrationFiles.Write(path, replacement);
            IntegrationFiles.ClearOwnedDescriptor(path, original.InstanceId);
            Assert.AreEqual(replacement.InstanceId, IntegrationFiles.Read<ConnectionDescriptor>(path)!.InstanceId);
            IntegrationFiles.ClearOwnedDescriptor(path, replacement.InstanceId);
            Assert.IsFalse(File.Exists(path));
            var credentials = IntegrationCredentials.Create();
            IntegrationFiles.Write(Path.Combine(root, "auth.json"), credentials);
            Assert.IsTrue(IntegrationFiles.Read<IntegrationCredentials>(Path.Combine(root, "auth.json"))!.IsValid());
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void PortPolicyIsBoundedAndNeverUsesOriginalBridge()
    {
        var values = IntegrationIdentity.PortCandidates(22000, 19825);
        Assert.AreEqual(22000, values[0]); Assert.AreEqual(19825, values[1]);
        Assert.AreEqual(values.Count, values.Distinct().Count());
        Assert.IsFalse(values.Contains(19821)); Assert.IsTrue(values.Count <= 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => IntegrationIdentity.PortCandidates(19821));
    }

    private static int Port()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
    private static HttpClient Client(int port, string? token = null, string? epoch = null)
    {
        var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
            { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        if (token != null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (epoch != null) client.DefaultRequestHeaders.Add("X-WidgetRail-Instance", epoch);
        return client;
    }
    private sealed class Echo : ILocalApiDispatcher
    {
        public int Calls;
        public Task<LocalApiResponse> DispatchAsync(LocalApiRequest request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            if (request.Path == "/v1/fail") throw new InvalidOperationException("Synthetic failure");
            return Task.FromResult(new LocalApiResponse(200, Encoding.UTF8.GetBytes("{}")));
        }
    }
    private sealed class Waiting : ILocalApiDispatcher
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Cancelled;
        public async Task<LocalApiResponse> DispatchAsync(LocalApiRequest request, CancellationToken token)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return new LocalApiResponse(200, []);
        }
    }
}
