using System.Net;
using System.Reflection;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WidgetRail.Samples.PlayniteLibrary;

namespace PlayniteLibraryCommunityApplication.Tests;

[TestClass]
public sealed class PlayniteBridgeTests
{
    [TestMethod]
    public async Task FixedProbeUsesProtectedCredentialAndMapsClosedResponses()
    {
        var credentials = new FakeCredentialStore { Secret = "fake-playnite-token-one" };
        var transport = new FakeTransport
        {
            Response = new(200,
                "{\"total\":7,\"offset\":0,\"limit\":1,\"games\":[]}"),
        };
        using var client = new PlayniteBridgeClient(transport, credentials);

        var result = await client.ProbeAsync(CancellationToken.None);

        Assert.AreEqual(PlayniteBridgeConnectionKind.Connected, result.Kind);
        Assert.AreEqual(credentials.Secret, transport.ObservedBearer);
        Assert.AreEqual(1, transport.Calls);
        Assert.AreEqual(PlayniteBridgeCommandKind.Probe, transport.ObservedCommand!.Kind);

        transport.Response = new(401, "{}");
        Assert.AreEqual(PlayniteBridgeConnectionKind.AuthenticationRequired,
            (await client.ProbeAsync(CancellationToken.None)).Kind);
        Assert.AreEqual(1, credentials.DeleteCalls,
            "A rejected credential must be removed exactly once by the package client.");
        Assert.IsNull(credentials.Secret);

        credentials.Secret = "fake-playnite-token-two";
        transport.Response = new(404, "{}");
        Assert.AreEqual(PlayniteBridgeConnectionKind.Incompatible,
            (await client.ProbeAsync(CancellationToken.None)).Kind);

        foreach (var body in new[]
                 {
                     "{}",
                     "{\"total\":0,\"offset\":0,\"limit\":1,\"games\":{}}",
                     "{\"total\":0,\"offset\":0,\"limit\":2,\"games\":[]}",
                     "{\"total\":0,\"offset\":0,\"limit\":1,\"games\":[]",
                 })
        {
            transport.Response = new(200, body);
            Assert.AreEqual(PlayniteBridgeConnectionKind.Malformed,
                (await client.ProbeAsync(CancellationToken.None)).Kind, body);
        }
    }

    [TestMethod]
    public async Task CredentialReplacementRemovalBoundsAndCancellationStayPackageScoped()
    {
        var credentials = new FakeCredentialStore();
        var transport = new FakeTransport
        {
            Response = new(200,
                "{\"total\":0,\"offset\":0,\"limit\":1,\"games\":[]}"),
        };
        using var client = new PlayniteBridgeClient(transport, credentials);
        const string first = "fake-playnite-token-one";
        const string replacement = "fake-playnite-token-two";

        Assert.AreEqual(PlayniteBridgeConnectionKind.NotConfigured,
            (await client.ProbeAsync(CancellationToken.None)).Kind);
        await client.SaveCredentialAsync(first, CancellationToken.None);
        await client.SaveCredentialAsync(replacement, CancellationToken.None);
        Assert.AreEqual(2, credentials.SaveCalls);
        Assert.AreEqual(replacement, credentials.Secret);
        await client.DeleteCredentialAsync(CancellationToken.None);
        Assert.IsNull(credentials.Secret);
        Assert.AreEqual(1, credentials.DeleteCalls);

        foreach (var invalid in new[]
                 {
                     "short",
                     new string('x', PlayniteBridgeClient.MaximumTokenCharacters + 1),
                     "fake token with spaces",
                     "fake-token-with-newline\n",
                 })
            await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
                await client.SaveCredentialAsync(invalid, CancellationToken.None));
        Assert.AreEqual(2, credentials.SaveCalls);

        credentials.Secret = replacement;
        transport.WaitForCancellation = true;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await client.ProbeAsync(cancellation.Token));
    }

    [TestMethod]
    public void TransportAndClientSurfacesAreFixedAndProhibitedRoutesCannotBeConstructed()
    {
        using var handler = PlayniteBridgeHttpTransport.CreateHandler();
        Assert.IsFalse(handler.AllowAutoRedirect);
        Assert.IsFalse(handler.UseProxy);
        Assert.IsNull(handler.Proxy);
        Assert.IsFalse(handler.UseCookies);
        Assert.AreEqual(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.IsFalse(handler.PreAuthenticate);
        Assert.IsNull(handler.Credentials);
        Assert.AreEqual(1, handler.MaxConnectionsPerServer);
        Assert.AreEqual(16, handler.MaxResponseHeadersLength);
        var maximumJsonBytes = (int)typeof(PlayniteBridgeHttpTransport).GetField(
            nameof(PlayniteBridgeHttpTransport.MaximumJsonBytes),
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Assert.AreEqual(96 * 1024, maximumJsonBytes);
        var maximumArtworkBytes = (int)typeof(PlayniteBridgeHttpTransport).GetField(
            nameof(PlayniteBridgeHttpTransport.MaximumArtworkBytes),
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Assert.AreEqual(256 * 1024, maximumArtworkBytes);
        var fixedOrigin = (Uri)typeof(PlayniteBridgeHttpTransport).GetField(
            "Origin", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.AreEqual("http://127.0.0.1:19821/", fixedOrigin.AbsoluteUri);

        var clientMethods = typeof(PlayniteBridgeClient).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(new[]
        {
            nameof(PlayniteBridgeClient.CreateCategoryAsync),
            nameof(PlayniteBridgeClient.DeleteCredentialAsync),
            nameof(IDisposable.Dispose),
            nameof(IAsyncDisposable.DisposeAsync),
            nameof(PlayniteBridgeClient.LaunchAsync),
            nameof(PlayniteBridgeClient.ListCategoriesAsync),
            nameof(PlayniteBridgeClient.ListCompletionStatusesAsync),
            nameof(PlayniteBridgeClient.ProbeAsync),
            nameof(PlayniteBridgeClient.QueryGamesAsync),
            nameof(PlayniteBridgeClient.ResolveArtworkAsync),
            nameof(PlayniteBridgeClient.ResolveGameAsync),
            nameof(PlayniteBridgeClient.SaveCredentialAsync),
            nameof(PlayniteBridgeClient.SetCategoriesAsync),
            nameof(PlayniteBridgeClient.SetCompletionStatusAsync),
            nameof(PlayniteBridgeClient.SetFavoriteAsync),
            nameof(PlayniteBridgeClient.SetHiddenAsync),
        }, clientMethods);
        var transportMethod = typeof(IPlayniteBridgeTransport).GetMethod(
            nameof(IPlayniteBridgeTransport.SendAsync))!;
        CollectionAssert.AreEqual(new[]
        {
            typeof(PlayniteBridgeCommand), typeof(string), typeof(CancellationToken),
        },
            transportMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        var compatibilityPath = (string)typeof(PlayniteBridgeClient).GetField(
            nameof(PlayniteBridgeClient.CompatibilityPath),
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Assert.AreEqual("/api/games?installed=true&limit=1&offset=0",
            compatibilityPath);
        var credentialTarget = (string)typeof(WindowsCredentialPlayniteTokenStore).GetField(
            nameof(WindowsCredentialPlayniteTokenStore.Target),
            BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
        Assert.AreEqual(
            "WidgetRail/PlayniteLibrary/PlayniteBridge/v1/widgetrail.samples.playnite-library",
            credentialTarget);
        foreach (var prohibited in new[]
                 {
                     "eval", "delete", "rotate", "addons", "config", "install", "uninstall",
                 })
            Assert.IsFalse(compatibilityPath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => string.Equals(
                    segment, prohibited, StringComparison.OrdinalIgnoreCase)), prohibited);
    }

    [TestMethod]
    public void ManifestAndPackageSourceContainNoCapabilityOrCredentialValue()
    {
        var root = RepositoryRoot();
        var manifestPath = Path.Combine(root, "samples", "PlayniteLibraryWidget", "manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var value = manifest.RootElement;
        Assert.AreEqual(0, value.GetProperty("permissions").GetArrayLength());
        Assert.AreEqual(0, value.GetProperty("optionalPermissions").GetArrayLength());
        Assert.AreEqual("0.2.4", value.GetProperty("version").GetString());
        var manifestText = File.ReadAllText(manifestPath);
        Assert.IsFalse(manifestText.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifestText.Contains("token", StringComparison.OrdinalIgnoreCase));

        var clientSource = File.ReadAllText(Path.Combine(root, "samples",
            "PlayniteLibraryWidget", "PlayniteBridgeClient.cs"));
        Assert.IsFalse(clientSource.Contains("Environment.", StringComparison.Ordinal));
        Assert.IsFalse(clientSource.Contains("/api/eval", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(clientSource.Contains("/api/auth/rotate", StringComparison.OrdinalIgnoreCase));

        var packagePath = Path.Combine(root, "artifacts", "community-addons",
            "playnite-library", "widgetrail.samples.playnite-library-0.2.4.wrwidget");
        Assert.IsTrue(File.Exists(packagePath), "The validated package artifact is missing.");
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries.Where(item => item.Length != 0))
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var content = Encoding.Latin1.GetString(buffer.ToArray());
            Assert.IsFalse(content.Contains("fake-playnite-token", StringComparison.Ordinal),
                entry.FullName);
            Assert.IsFalse(content.Contains("/api/eval", StringComparison.OrdinalIgnoreCase),
                entry.FullName);
            Assert.IsFalse(content.Contains("/api/auth/rotate", StringComparison.OrdinalIgnoreCase),
                entry.FullName);
        }
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !(File.Exists(Path.Combine(current.FullName, "README.md")) &&
                 File.Exists(Path.Combine(current.FullName, "docs", "README.md")) &&
                 File.Exists(Path.Combine(current.FullName, "global.json"))))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException(
            "Repository root is unavailable.");
    }

    private sealed class FakeCredentialStore : IPlayniteCredentialStore
    {
        internal string? Secret { get; set; }
        internal int SaveCalls { get; private set; }
        internal int DeleteCalls { get; private set; }

        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Secret);
        }

        public ValueTask SaveAsync(string token, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Secret = token;
            SaveCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DeleteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Secret = null;
            DeleteCalls++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeTransport : IPlayniteBridgeTransport
    {
        internal PlayniteBridgeResponse Response { get; set; } = new(503, "{}");
        internal string? ObservedBearer { get; private set; }
        internal PlayniteBridgeCommand? ObservedCommand { get; private set; }
        internal int Calls { get; private set; }
        internal bool WaitForCancellation { get; set; }

        public async ValueTask<PlayniteBridgeResponse> SendAsync(
            PlayniteBridgeCommand command,
            string bearerToken,
            CancellationToken cancellationToken)
        {
            ObservedCommand = command;
            ObservedBearer = bearerToken;
            Calls++;
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Response;
        }

        public void Dispose() { }
    }
}
