using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using WidgetRail.Playnite;
using WidgetRail.Samples.PlayniteLibrary;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PlayniteLocalConnectionTests
{
    [TestMethod]
    public async Task TypedEventsStartAtCurrentCursorAndProviderRefreshIsOneCommand()
    {
        using var fixture = new LocalFixture();
        using var client = PlayniteBridgeClient.CreateLocal(fixture, new Credentials());
        var initial = await client.ReadEventsAsync(-1, default);
        Assert.IsNotNull(initial);
        Assert.IsTrue(initial.ResetRequired);
        Assert.AreEqual(fixture.Binding, initial.ConnectionId);
        Assert.AreEqual(7, initial.NextSequence);
        var next = await client.ReadEventsAsync(initial.NextSequence, default);
        Assert.AreEqual("achievement-unlocked", next!.Changes.Single().Kind);
        Assert.AreEqual(PlayniteProviderRefreshResult.Completed, await client.RefreshAchievementsAsync(LocalFixture.GameId, default));
        Assert.AreEqual(1, fixture.Refreshes);
        fixture.OperationRunning = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await client.RefreshAchievementsAsync(LocalFixture.GameId, cancel.Token));
        Assert.AreEqual(2, fixture.Refreshes, "Canceling the wait cannot resubmit or cancel the provider command.");
    }

    [TestMethod]
    public async Task TypedClientUsesDiscoveredPortAndNeverEval()
    {
        using var fixture = new LocalFixture();
        var credentials = new Credentials();
        using var client = PlayniteBridgeClient.CreateLocal(fixture, credentials);
        Assert.AreEqual(PlayniteBridgeConnectionKind.Connected, (await client.ProbeAsync(default)).Kind);
        var page = await client.QueryGamesAsync(new(0, 64, false), default);
        Assert.AreEqual("snapshot", page.Revision); Assert.AreEqual(1, page.Total);
        var artwork = await client.ResolveArtworkFileAsync(LocalFixture.GameId, PlayniteBridgeArtworkKind.Cover, default);
        Assert.IsNotNull(artwork); StringAssert.EndsWith(artwork.Revision, ":file-revision");
        var achievements = await client.GetAchievementsAsync(LocalFixture.GameId, default);
        Assert.IsTrue(achievements.Available); Assert.AreEqual(PlayniteAchievementProvider.PlayniteAchievements, achievements.Provider);
        Assert.IsFalse(fixture.Paths.Any(path => path.Contains("eval", StringComparison.Ordinal)));
        Assert.AreEqual(0, credentials.Reads, "Connected requests acquire the endpoint and its own credential as one profile-bound context.");
        Assert.AreEqual(0, credentials.Deletes);
    }

    [TestMethod]
    public async Task ProfileReplacementCannotRedirectAnExistingClient()
    {
        using var fixture = new LocalFixture();
        using var transport = new PlayniteBridgeHttpTransport(fixture);
        await transport.SendConnectedAsync(new(PlayniteBridgeCommandKind.Probe), default);
        var before = fixture.Paths.Count;
        fixture.Binding = Guid.NewGuid().ToString("D");
        await Assert.ThrowsAsync<PlayniteBridgeTransportException>(async () =>
            await transport.SendConnectedAsync(new(PlayniteBridgeCommandKind.SetFavorite, LocalFixture.GameId, JsonBody: "{\"favorite\":true}"), default));
        Assert.AreEqual(before, fixture.Paths.Count);
        transport.ResetConnection();
        Assert.AreEqual(200, (await transport.SendConnectedAsync(new(PlayniteBridgeCommandKind.Probe), default))!.StatusCode);
    }

    [TestMethod]
    public async Task LateReadFromRetiredProfileIsRejected()
    {
        using var fixture = new LocalFixture();
        using var transport = new PlayniteBridgeHttpTransport(fixture);
        await transport.SendConnectedAsync(new(PlayniteBridgeCommandKind.Probe), default);
        fixture.BlockGameRead = true;
        var read = transport.SendConnectedAsync(new(PlayniteBridgeCommandKind.ResolveGame, LocalFixture.GameId), default).AsTask();
        await fixture.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Binding = Guid.NewGuid().ToString("D"); fixture.ReleaseRead.SetResult();
        await Assert.ThrowsAsync<PlayniteBridgeTransportException>(async () => await read);
    }

    [TestMethod]
    public void PortableCopiesWithSameProfileUuidHaveDifferentCredentialTargets()
    {
        var id = Guid.NewGuid().ToString("D");
        var first = new PlayniteLocalBinding(1, Guid.NewGuid().ToString("D"), id, @"C:\Playnite", @"C:\Playnite\Data");
        var second = first with { DataDirectory = @"C:\Playnite Copy\Data" };
        Assert.AreNotEqual(PlayniteLocalConnectionStore.CredentialTarget(first), PlayniteLocalConnectionStore.CredentialTarget(second));
        Assert.AreEqual(PlayniteLocalConnectionStore.CredentialTarget(first), PlayniteLocalConnectionStore.CredentialTarget(first with { BindingId = Guid.NewGuid().ToString("D") }));
        Assert.AreNotEqual(WindowsCredentialPlayniteTokenStore.Target, PlayniteLocalConnectionStore.CredentialTarget(first));
    }

    private sealed class Credentials : IPlayniteCredentialStore
    {
        internal int Reads, Deletes;
        public ValueTask<string?> ReadAsync(CancellationToken token) { Reads++; return ValueTask.FromResult<string?>("not-the-local-token"); }
        public ValueTask SaveAsync(string credential, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask DeleteAsync(CancellationToken token) { Deletes++; return ValueTask.CompletedTask; }
    }
    private sealed class LocalFixture : IPlayniteLocalConnectionSource, ILocalApiDispatcher, IDisposable
    {
        internal const string GameId = "00000000-0000-0000-0000-000000000001";
        private readonly IntegrationCredentials credentials = IntegrationCredentials.Create();
        private readonly ConnectionDescriptor descriptor;
        private readonly LocalApiServer server;
        internal readonly List<string> Paths = [];
        internal string Binding = Guid.NewGuid().ToString("D");
        internal bool BlockGameRead;
        internal int Refreshes;
        internal bool OperationRunning;
        private string? operation;
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LocalFixture()
        {
            using var reserve = new TcpListener(IPAddress.Loopback, 0); reserve.Start();
            var port = ((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            descriptor = ConnectionDescriptor.Create(credentials.ProfileId, port);
            server = new LocalApiServer(credentials.Token, descriptor.InstanceId, this); server.Start([port]);
        }
        public ValueTask<PlayniteLocalConnection?> OpenAsync(CancellationToken token)
        {
            var binding = Binding;
            return ValueTask.FromResult<PlayniteLocalConnection?>(new(binding, descriptor, credentials.Token, () => binding == Binding));
        }
        public async Task<LocalApiResponse> DispatchAsync(LocalApiRequest request, CancellationToken token)
        {
            Paths.Add(request.Path);
            if (request.Path.EndsWith("/achievements/refresh", StringComparison.Ordinal))
            {
                Refreshes++; operation = request.CommandId;
                return new LocalApiResponse(202, JsonSerializer.SerializeToUtf8Bytes(new { operationId = operation, state = "accepted" }));
            }
            object game = new { id = GameId, name = "Fixture", isInstalled = true, favorite = false, hidden = false,
                categories = Array.Empty<string>(), genres = Array.Empty<string>(), platforms = Array.Empty<string>(), playtime = 0 };
            object response;
            if (request.Path == "/v1/connection") response = new { pluginId = IntegrationIdentity.PluginId, profileId = descriptor.ProfileId,
                instanceId = descriptor.InstanceId, protocolVersion = 1, capabilities = IntegrationIdentity.RequiredCapabilities };
            else if (request.Path == "/v1/games") response = new { total = 1, offset = 0, limit = 64, revision = "snapshot", games = new[] { game } };
            else if (request.Path == "/v1/artwork") response = new { total = 1, offset = 0, limit = 128, revision = "snapshot",
                artwork = new[] { new { id = GameId, cover = @"C:\fixture\cover.png", background = (string?)null, coverRevision = "file-revision" } } };
            else if (request.Path.StartsWith("/v1/operations/", StringComparison.Ordinal)) response = new { id = operation, state = OperationRunning ? "running" : "completed" };
            else if (request.Path == "/v1/events") response = new { instanceId = descriptor.InstanceId, resetRequired = request.Query["after"] == "now",
                nextSequence = request.Query["after"] == "now" ? 7 : 8,
                events = request.Query["after"] == "now" ? Array.Empty<object>() : new object[] { new { sequence = 8, kind = "achievement-unlocked", gameId = GameId, achievementId = "fixture", observedAt = 1000 } } };
            else if (request.Path.EndsWith("/achievements", StringComparison.Ordinal)) response = new { gameId = GameId, installed = true, provider = "playnite-achievements", canRefresh = true, total = 0, unlocked = 0, achievements = Array.Empty<object>() };
            else
            {
                if (BlockGameRead) { ReadStarted.TrySetResult(); await ReleaseRead.Task.WaitAsync(token); }
                response = game;
            }
            return new LocalApiResponse(200, JsonSerializer.SerializeToUtf8Bytes(response));
        }
        public void Dispose() => server.Dispose();
    }
}
