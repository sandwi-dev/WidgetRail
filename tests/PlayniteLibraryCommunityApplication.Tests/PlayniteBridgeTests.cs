using System.Net;
using System.Reflection;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WidgetRail.Samples.PlayniteLibrary;
using WidgetRail.WidgetSdk;

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
        Assert.AreEqual(
            WidgetRail.WidgetProtocol.ProtocolConstants.MaximumEncodedArtworkBytes,
            maximumArtworkBytes);
        var fixedOrigin = (Uri)typeof(PlayniteBridgeHttpTransport).GetField(
            "Origin", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        Assert.AreEqual("http://localhost:19821/", fixedOrigin.AbsoluteUri);
        Assert.AreEqual("localhost", fixedOrigin.Host);
        Assert.AreEqual("localhost:19821", fixedOrigin.Authority,
            "The HTTP/1.1 Host authority must identify the fixed Playnite Bridge listener.");
        Assert.AreEqual(PlayniteBridgeClient.Port, fixedOrigin.Port);

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
    public async Task ArtworkTransportAdmitsTheExactPublicBoundAndRejectsTheNextByte()
    {
        var maximumBytes =
            WidgetRail.WidgetProtocol.ProtocolConstants.MaximumEncodedArtworkBytes;
        using var exactContent = new ByteArrayContent(new byte[maximumBytes]);
        var exact = await PlayniteBridgeHttpTransport.ReadBoundedAsync(
            exactContent, maximumBytes, CancellationToken.None);
        Assert.AreEqual(maximumBytes, exact.Length,
            "The accepted encoded-artwork maximum must pass the bounded reader exactly.");

        using var oversizedContent = new ByteArrayContent([]);
        oversizedContent.Headers.ContentLength = maximumBytes + 1L;
        var overBudget = await Assert.ThrowsExactlyAsync<PlayniteBridgeTransportException>(
            async () => await PlayniteBridgeHttpTransport.ReadBoundedAsync(
                oversizedContent, maximumBytes, CancellationToken.None));
        Assert.IsTrue(overBudget.IsMalformed,
            "An over-budget transport body remains a closed malformed response.");
        Assert.IsTrue(overBudget.IsOverBudget,
            "The bounded reader must classify the size rejection without reading the body.");
        Assert.AreEqual("response-over-budget",
            PlayniteLibraryApplicationService.ArtworkFailureCode(overBudget));
    }

    [TestMethod]
    public async Task ArtworkClassificationUsesEncodedBytesInsteadOfExtensionDerivedHeaders()
    {
        var credentials = new FakeCredentialStore { Secret = "fake-playnite-token-one" };
        var transport = new FakeTransport();
        using var client = new PlayniteBridgeClient(transport, credentials);

        var png = new byte[24];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(png, 0);
        transport.Response = new(200, png, "image/jpeg");
        var pngResult = await client.ResolveArtworkAsync("00000000-0000-0000-0000-000000000001",
            PlayniteBridgeArtworkKind.Cover, CancellationToken.None);
        Assert.IsNotNull(pngResult.Artwork);
        Assert.AreEqual(WidgetArtworkContentType.Png, pngResult.Artwork.ContentType);
        CollectionAssert.AreEqual(png, pngResult.Artwork.Bytes.ToArray());

        var jpeg = new byte[] { 0xff, 0xd8, 0xff, 0xd9 };
        transport.Response = new(200, jpeg, "image/png");
        var jpegResult = await client.ResolveArtworkAsync("00000000-0000-0000-0000-000000000002",
            PlayniteBridgeArtworkKind.Cover, CancellationToken.None);
        Assert.IsNotNull(jpegResult.Artwork);
        Assert.AreEqual(WidgetArtworkContentType.Jpeg, jpegResult.Artwork.ContentType);
        CollectionAssert.AreEqual(jpeg, jpegResult.Artwork.Bytes.ToArray());

        var webp = Convert.FromBase64String(
            "UklGRh4AAABXRUJQVlA4TBEAAAAvAQAAAAdQmWZ0qf+BiOh/AAA=");
        transport.Response = new(200, webp, "image/jpeg");
        var webpResult = await client.ResolveArtworkAsync("00000000-0000-0000-0000-000000000003",
            PlayniteBridgeArtworkKind.Cover, CancellationToken.None);
        Assert.IsNotNull(webpResult.Artwork);
        Assert.AreEqual(WidgetArtworkContentType.WebP, webpResult.Artwork.ContentType);
        CollectionAssert.AreEqual(webp, webpResult.Artwork.Bytes.ToArray());

        var malformedWebp = webp.ToArray();
        malformedWebp[4]++;
        transport.Response = new(200, malformedWebp, "image/webp");
        var malformedResult = await client.ResolveArtworkAsync(
            "00000000-0000-0000-0000-000000000004",
            PlayniteBridgeArtworkKind.Cover, CancellationToken.None);
        Assert.IsNull(malformedResult.Artwork);
        Assert.AreEqual("unsupported-encoded-artwork", malformedResult.Code);
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
        Assert.AreEqual("0.2.45", value.GetProperty("version").GetString());
        var manifestText = File.ReadAllText(manifestPath);
        Assert.IsFalse(manifestText.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(manifestText.Contains("token", StringComparison.OrdinalIgnoreCase));

        var clientSource = File.ReadAllText(Path.Combine(root, "samples",
            "PlayniteLibraryWidget", "PlayniteBridgeClient.cs"));
        Assert.IsFalse(clientSource.Contains("Environment.", StringComparison.Ordinal));
        Assert.IsFalse(clientSource.Contains("/api/eval", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(clientSource.Contains("/api/auth/rotate", StringComparison.OrdinalIgnoreCase));
        var sampleRoot = Path.Combine(root, "samples", "PlayniteLibraryWidget");
        foreach (var sourcePath in Directory.EnumerateFiles(
                     sampleRoot, "*", SearchOption.AllDirectories).Where(path =>
                     new[] { ".cs", ".csproj", ".json", ".md", ".ps1", ".wrss" }
                     .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
                     !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase) &&
                     !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase)))
            Assert.IsFalse(File.ReadAllText(sourcePath).Contains(
                    "127.0.0.1:19821", StringComparison.Ordinal),
                Path.GetRelativePath(root, sourcePath));

        var packagePath = Path.Combine(root, "artifacts", "community-addons",
            "playnite-library", $"{value.GetProperty("id").GetString()}-" +
                                $"{value.GetProperty("version").GetString()}.wrwidget");
        Assert.IsTrue(File.Exists(packagePath), "The validated package artifact is missing.");
        using var archive = ZipFile.OpenRead(packagePath);
        var stylePath = Path.Combine(root, "samples", "PlayniteLibraryWidget",
            "styles", "default.wrss");
        CollectionAssert.AreEqual(File.ReadAllBytes(manifestPath),
            ReadEntry(archive, "manifest.json"));
        CollectionAssert.AreEqual(File.ReadAllBytes(manifestPath),
            ReadEntry(archive, "payload/manifest.json"));
        CollectionAssert.AreEqual(File.ReadAllBytes(stylePath),
            ReadEntry(archive, "styles/default.wrss"));
        CollectionAssert.AreEqual(File.ReadAllBytes(stylePath),
            ReadEntry(archive, "payload/styles/default.wrss"));
        CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(root, "artifacts",
                "community-addons", "playnite-library", "application-publish",
                "PlayniteLibraryWidget.dll")),
            ReadEntry(archive, "payload/PlayniteLibraryWidget.dll"),
            "The sealed managed widget payload must be the exact corrected build output.");
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

    private static byte[] ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        Assert.IsNotNull(entry, $"Package entry {path} is missing.");
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
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
