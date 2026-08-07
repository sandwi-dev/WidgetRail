using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameBarAlternative.GbarCli;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Help describes the complete workflow", HelpWorks),
    ("New scaffolds a token-free controller widget", NewScaffolds),
    ("New rejects invalid package identity before writing", NewRejectsIdentity),
    ("Validate accepts a scaffolded widget", ValidateScaffold),
    ("Validate rejects unsafe GBSS", ValidateRejectsUnsafeGbss),
    ("Validate rejects malformed manifest", ValidateRejectsManifest),
    ("Render previews a valid snapshot", RenderSnapshot),
    ("Controller replay follows focus and shortcuts", ReplayFocusAndActions),
    ("Pack produces reproducible catalog-valid archives", PackIsReproducible),
    ("Install list disable and enable form a local distribution workflow", LocalDistributionWorkflow),
    ("Pack rejects invalid identity without publishing an archive", PackRejectsInvalidManifest),
    ("Pack rejects source reparse points", PackRejectsReparsePoints),
    ("Install rejects traversal archives through the CLI", InstallRejectsTraversal),
    ("Remote install verifies a pinned package and leaves it disabled", RemoteDistributionWorkflow),
    ("Remote updates require explicit disable and preserve enabled versions on failure", RemoteUpdateRequiresDisable),
    ("GitHub shorthand resolves directly to a release asset", GitHubShorthandResolves),
    ("Remote install requires a valid SHA-256 pin before network access", RemoteRequiresHash),
    ("Remote sources reject unsafe schemes authorities and literals", RemoteRejectsUnsafeSources),
    ("Remote downloader follows bounded HTTPS redirects", RemoteRedirectsAreBounded),
    ("Remote downloader enforces declared and streamed byte limits", RemoteDownloadIsBounded),
    ("Remote downloader rejects encoded payloads", RemoteRejectsContentEncoding),
    ("Remote installer reports malformed archives without crashing", RemoteRejectsMalformedArchive),
    ("Remote downloader removes temporary files after integrity failure", RemoteHashMismatchCleansUp),
    ("Remote package remains write-locked until installation completes", RemotePackageHasIntegrityGuard),
    ("Remote downloader enforces response and overall timeouts", RemoteTimeoutsAreBounded),
    ("Remote request failures redact signed URL secrets", RemoteFailuresRedactSecrets),
    ("Caller cancellation stops remote installation", RemoteCancellationIsBounded),
    ("Catalog state commands report missing widgets", StateCommandRejectsMissingWidget),
    ("Unknown commands return usage errors", UnknownCommand),
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

static async Task HelpWorks()
{
    var result = await RunCli("help");
    Assert.Equal(0, result.Code);
    foreach (var command in new[] { "new", "validate", "render", "replay", "pack", "install", "list", "enable", "disable" })
        Assert.Contains(command, result.Output);
}

static async Task NewScaffolds()
{
    using var temp = new TemporaryDirectory();
    var destination = Path.Combine(temp.Path, "MediaDeck");
    var result = await RunCli("new", "widget", "MediaDeck", "--output", destination,
        "--id", "dev.test.media-deck", "--publisher", "dev.test");
    Assert.Equal(0, result.Code);
    Assert.True(File.Exists(Path.Combine(destination, "MediaDeck.csproj")), "Project was not created.");
    Assert.True(File.Exists(Path.Combine(destination, "src", "MediaDeck.cs")), "Widget source was not created.");
    var allText = string.Join('\n', Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
        .Select(File.ReadAllText));
    Assert.DoesNotContain("{{", allText);
    Assert.Contains("dev.test.media-deck", allText);
    Assert.Contains("ControllerButton.LeftBumper", allText);
    Assert.Contains("ProjectReference", File.ReadAllText(Path.Combine(destination, "MediaDeck.csproj")));
}

static async Task NewRejectsIdentity()
{
    using var temp = new TemporaryDirectory();
    var destination = Path.Combine(temp.Path, "InvalidWidget");
    var result = await RunCli("new", "widget", "InvalidWidget", "--output", destination,
        "--publisher", "Not-A.Namespace");
    Assert.Equal(2, result.Code);
    Assert.True(!Directory.Exists(destination), "An invalid scaffold must not leave a partial directory.");
}

static async Task ValidateScaffold()
{
    using var temp = new TemporaryDirectory();
    var destination = Path.Combine(temp.Path, "QuickPanel");
    Assert.Equal(0, (await RunCli("new", "widget", "QuickPanel", "--output", destination)).Code);
    var result = await RunCli("validate", destination);
    Assert.Equal(0, result.Code);
    Assert.Contains("2 file(s) checked", result.Output);
}

static async Task ValidateRejectsUnsafeGbss()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "bad.gbss");
    await File.WriteAllTextAsync(path, "button { mystery: 3; background: url(https://bad.example/x); }");
    var result = await RunCli("validate", path);
    Assert.Equal(1, result.Code);
    Assert.Contains("unknown_property", result.Error);
    Assert.Contains("unsafe_value", result.Error);
}

static async Task ValidateRejectsManifest()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "manifest.json");
    await File.WriteAllTextAsync(path, "{ \"manifestVersion\": 1, \"surprise\": true }");
    var result = await RunCli("validate", path);
    Assert.Equal(1, result.Code);
    Assert.Contains("invalid_json", result.Error);
}

static async Task RenderSnapshot()
{
    using var temp = new TemporaryDirectory();
    var snapshotPath = Path.Combine(temp.Path, "snapshot.json");
    var snapshot = BuildSnapshot();
    await File.WriteAllBytesAsync(snapshotPath, SnapshotJson.Serialize(snapshot));
    var result = await RunCli("render", snapshotPath);
    Assert.Equal(0, result.Code);
    Assert.Contains("Widget test.instance", result.Output);
    Assert.Contains("▶ Button #apply", result.Output);
    Assert.Contains("RightBumper:raise", result.Output);
}

static Task ReplayFocusAndActions()
{
    var replay = new InputReplay
    {
        InitialFocusId = "apply",
        Events =
        [
            new() { Button = ControllerButton.DPadLeft },
            new() { Button = ControllerButton.A },
            new() { Button = ControllerButton.RightBumper },
            new() { Button = ControllerButton.X },
        ],
    };
    var steps = ControllerReplay.Run(BuildSnapshot(), replay);
    Assert.Equal("lower", steps[0].FocusAfter);
    Assert.Equal("lower", steps[1].ActionId);
    Assert.Equal("raise", steps[2].ActionId);
    Assert.Equal("apply", steps[3].ActionId);
    return Task.CompletedTask;
}

static async Task PackIsReproducible()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "dev.test.repro", "dev.test", "1.2.3");
    var first = Path.Combine(temp.Path, "first.gbarwidget");
    var second = Path.Combine(temp.Path, "second.gbarwidget");

    var firstResult = await RunCli("pack", source, "--output", first);
    Assert.Equal(0, firstResult.Code);
    Assert.Contains("dev.test.repro 1.2.3", firstResult.Output);
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-7));
    Assert.Equal(0, (await RunCli("pack", source, "--output", second)).Code);
    Assert.SequenceEqual(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));

    using var archive = ZipFile.OpenRead(first);
    var paths = archive.Entries.Select(entry => entry.FullName).ToArray();
    Assert.SequenceEqual(paths.Order(StringComparer.Ordinal), paths);
    Assert.True(archive.Entries.All(entry => entry.LastWriteTime.DateTime == new DateTime(1980, 1, 1, 0, 0, 0)),
        "Archive timestamps must be fixed for reproducibility.");
    Assert.SequenceEqual(["manifest.json", "payload/Widget.dll", "styles/default.gbss"], paths);
}

static async Task LocalDistributionWorkflow()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "dev.test.local", "dev.test", "2.0.0");
    var package = Path.Combine(temp.Path, "local.gbarwidget");
    var catalog = Path.Combine(temp.Path, "catalog");
    Assert.Equal(0, (await RunCli("pack", source, "--output", package)).Code);

    var install = await RunCli("install", package, "--catalog", catalog);
    Assert.Equal(0, install.Code);
    Assert.Contains("Installed dev.test.local 2.0.0", install.Output);
    Assert.True(File.Exists(Path.Combine(catalog, "packages", "dev.test.local", "2.0.0", "payload", "Widget.dll")),
        "Package payload was not installed.");

    var listed = await RunCli("list", "--catalog", catalog);
    Assert.Equal(0, listed.Code);
    Assert.Contains("disabled  dev.test.local  2.0.0", listed.Output);
    Assert.Equal(0, (await RunCli("enable", "dev.test.local", "--catalog", catalog)).Code);
    Assert.Contains("enabled   dev.test.local", (await RunCli("list", "--catalog", catalog)).Output);
    Assert.Equal(0, (await RunCli("disable", "dev.test.local", "--catalog", catalog)).Code);
    Assert.Contains("disabled  dev.test.local", (await RunCli("list", "--catalog", catalog)).Output);
    Assert.Equal(0, (await RunCli("enable", "dev.test.local", "--catalog", catalog)).Code);
    Assert.Contains("enabled   dev.test.local", (await RunCli("list", "--catalog", catalog)).Output);
}

static async Task PackRejectsInvalidManifest()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "org.other.bad", "dev.test", "1.0.0");
    var package = Path.Combine(temp.Path, "invalid.gbarwidget");
    var result = await RunCli("pack", source, "--output", package);
    Assert.Equal(1, result.Code);
    Assert.Contains("identity_mismatch", result.Error);
    Assert.True(!File.Exists(package), "Invalid packages must not be published.");
}

static async Task PackRejectsReparsePoints()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "dev.test.link", "dev.test", "1.0.0");
    var outside = Path.Combine(temp.Path, "outside.txt");
    await File.WriteAllTextAsync(outside, "must not be packed");
    var link = Path.Combine(source, "payload", "link.txt");
    try { File.CreateSymbolicLink(link, outside); }
    catch (Exception exception) when (exception is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
    {
        return; // The platform cannot create the attack fixture; catalog archive-link coverage still runs separately.
    }

    var package = Path.Combine(temp.Path, "link.gbarwidget");
    var result = await RunCli("pack", source, "--output", package);
    Assert.Equal(1, result.Code);
    Assert.Contains("reparse_point", result.Error);
    Assert.True(!File.Exists(package), "Reparse-containing packages must not be published.");
}

static async Task InstallRejectsTraversal()
{
    using var temp = new TemporaryDirectory();
    var package = Path.Combine(temp.Path, "traversal.gbarwidget");
    using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "manifest.json", ManifestJson.Serialize(BuildManifest("dev.test.traversal", "dev.test", "1.0.0")));
        WriteArchiveEntry(archive, "payload/Widget.dll", "not executable"u8.ToArray());
        WriteArchiveEntry(archive, "../escaped.txt", "escape"u8.ToArray());
    }
    var result = await RunCli("install", package, "--catalog", Path.Combine(temp.Path, "catalog"));
    Assert.Equal(1, result.Code);
    Assert.Contains("invalid_path", result.Error);
    Assert.True(!File.Exists(Path.Combine(temp.Path, "escaped.txt")), "Traversal archive escaped the catalog.");
}

static async Task RemoteDistributionWorkflow()
{
    using var temp = new TemporaryDirectory();
    var package = await CreatePackedPackageAsync(temp.Path, "dev.test.remote", "3.1.4");
    var payload = await File.ReadAllBytesAsync(package);
    var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var requests = new List<string>();
    using var handler = new StubHttpHandler((request, _) =>
    {
        requests.Add(request.RequestUri!.AbsoluteUri);
        Assert.Contains("identity", string.Join(',', request.Headers.AcceptEncoding.Select(value => value.Value)));
        return Task.FromResult(Response(HttpStatusCode.OK, payload));
    });

    var catalog = Path.Combine(temp.Path, "catalog");
    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/release.gbarwidget",
        "--sha256", hash.ToUpperInvariant(), "--catalog", catalog);
    Assert.Equal(0, result.Code);
    Assert.Contains("Installed dev.test.remote 3.1.4", result.Output);
    Assert.Contains("(disabled)", result.Output);
    Assert.Contains($"Downloaded SHA-256: {hash}", result.Output);
    Assert.SequenceEqual(["https://widgets.example/release.gbarwidget"], requests);
    var listed = await RunCli("list", "--catalog", catalog);
    Assert.Contains("disabled  dev.test.remote", listed.Output);
}

static async Task RemoteUpdateRequiresDisable()
{
    using var temp = new TemporaryDirectory();
    var catalog = Path.Combine(temp.Path, "catalog");
    var first = await CreatePackedPackageAsync(temp.Path, "dev.test.remote-update", "1.0.0");
    Assert.Equal(0, (await RunCli("install", first, "--catalog", catalog)).Code);
    Assert.Equal(0, (await RunCli("enable", "dev.test.remote-update", "--catalog", catalog)).Code);

    var update = await CreatePackedPackageAsync(temp.Path, "dev.test.remote-update", "2.0.0");
    var payload = await File.ReadAllBytesAsync(update);
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromResult(Response(HttpStatusCode.OK, payload)));

    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/update.gbarwidget",
        "--sha256", hash, "--catalog", catalog);
    Assert.Equal(1, result.Code);
    Assert.Contains("Disable it before installing a remote update", result.Error);
    var preserved = await RunCli("list", "--catalog", catalog);
    Assert.Contains("enabled   dev.test.remote-update  1.0.0", preserved.Output);

    Assert.Equal(0, (await RunCli("disable", "dev.test.remote-update", "--catalog", catalog)).Code);
    var retry = await RunCliWithHandler(handler, "install", "https://widgets.example/update.gbarwidget",
        "--sha256", hash, "--catalog", catalog);
    Assert.Equal(0, retry.Code);
    var updated = await RunCli("list", "--catalog", catalog);
    Assert.Contains("disabled  dev.test.remote-update  2.0.0", updated.Output);
}

static async Task GitHubShorthandResolves()
{
    using var temp = new TemporaryDirectory();
    var package = await CreatePackedPackageAsync(temp.Path, "dev.test.github", "1.0.0");
    var payload = await File.ReadAllBytesAsync(package);
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    string? requested = null;
    using var handler = new StubHttpHandler((request, _) =>
    {
        requested = request.RequestUri!.AbsoluteUri;
        return Task.FromResult(Response(HttpStatusCode.OK, payload));
    });
    var result = await RunCliWithHandler(handler, "install", "github:sample-org/game-bar-widget@v1.0.0/music.gbarwidget",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "catalog"));
    Assert.Equal(0, result.Code);
    Assert.Equal(
        "https://github.com/sample-org/game-bar-widget/releases/download/v1.0.0/music.gbarwidget",
        requested);

    var malformed = await RunCliWithHandler(handler, "install", "github:owner/repo@latest",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "unused"));
    Assert.Equal(2, malformed.Code);
    Assert.Contains("github:owner/repository@tag/asset.gbarwidget", malformed.Error);
}

static async Task RemoteRequiresHash()
{
    var requests = 0;
    using var handler = new StubHttpHandler((_, _) =>
    {
        requests++;
        return Task.FromResult(Response(HttpStatusCode.OK, []));
    });
    var missing = await RunCliWithHandler(handler, "install", "https://widgets.example/x.gbarwidget");
    Assert.Equal(2, missing.Code);
    Assert.Contains("requires --sha256", missing.Error);
    var malformed = await RunCliWithHandler(handler, "install", "https://widgets.example/x.gbarwidget", "--sha256", "1234");
    Assert.Equal(2, malformed.Code);
    Assert.Contains("exactly 64 hexadecimal", malformed.Error);
    Assert.Equal(0, requests);
}

static async Task RemoteRejectsUnsafeSources()
{
    using var handler = new StubHttpHandler((_, _) => throw new InvalidOperationException("Unsafe URL reached the network."));
    var hash = new string('0', 64);
    var unsafeSources = new[]
    {
        "http://widgets.example/x.gbarwidget",
        "ftp://widgets.example/x.gbarwidget",
        "https://user:secret@widgets.example/x.gbarwidget",
        "https://widgets.example/x.gbarwidget#fragment",
        "https://widgets.example:8443/x.gbarwidget",
        "https://localhost/x.gbarwidget",
        "https://127.0.0.1/x.gbarwidget",
        "https://10.1.2.3/x.gbarwidget",
        "https://169.254.1.1/x.gbarwidget",
        "https://172.20.1.1/x.gbarwidget",
        "https://192.168.1.1/x.gbarwidget",
        "https://[::1]/x.gbarwidget",
        "https://[fe80::1]/x.gbarwidget",
        "https://[fd00::1]/x.gbarwidget",
    };
    foreach (var source in unsafeSources)
    {
        var result = await RunCliWithHandler(handler, "install", source, "--sha256", hash);
        Assert.Equal(2, result.Code);
    }
}

static async Task RemoteRedirectsAreBounded()
{
    var calls = 0;
    using var redirectHandler = new StubHttpHandler((_, _) =>
    {
        calls++;
        var response = Response(HttpStatusCode.Found, []);
        response.Headers.Location = new Uri($"https://cdn.example/{calls}.gbarwidget");
        return Task.FromResult(response);
    });
    using var downloader = new RemotePackageDownloader(redirectHandler, new RemoteDownloadOptions
    {
        MaximumRedirects = 2,
    });
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/start.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("2-redirect limit", exception.Message);
    Assert.Equal(3, calls);

    using var unsafeRedirectHandler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.Found, []);
        response.Headers.Location = new Uri("http://cdn.example/package.gbarwidget");
        return Task.FromResult(response);
    });
    using var unsafeDownloader = new RemotePackageDownloader(unsafeRedirectHandler);
    var unsafeException = await Assert.ThrowsAsync<CliUsageException>(() => unsafeDownloader.DownloadAsync(
        new Uri("https://widgets.example/start.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("absolute HTTPS", unsafeException.Message);
}

static async Task RemoteDownloadIsBounded()
{
    using var temp = new TemporaryDirectory();
    using var headerHandler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.OK, [1]);
        response.Content.Headers.ContentLength = 9;
        return Task.FromResult(response);
    });
    using var headerDownloader = new RemotePackageDownloader(headerHandler, TestDownloadOptions(temp.Path, 8));
    var headerException = await Assert.ThrowsAsync<CliOperationException>(() => headerDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("Content-Length: 9", headerException.Message);

    using var streamHandler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(new RepeatingReadStream(9)),
    }));
    using var streamDownloader = new RemotePackageDownloader(streamHandler, TestDownloadOptions(temp.Path, 8));
    var streamException = await Assert.ThrowsAsync<CliOperationException>(() => streamDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("8-byte download limit", streamException.Message);

    using var emptyHandler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, [])));
    using var emptyDownloader = new RemotePackageDownloader(emptyHandler, TestDownloadOptions(temp.Path, 8));
    var emptyException = await Assert.ThrowsAsync<CliOperationException>(() => emptyDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("response was empty", emptyException.Message);
    Assert.True(!Directory.EnumerateFileSystemEntries(temp.Path).Any(), "Bounded downloads leaked temporary files.");
}

static async Task RemoteRejectsContentEncoding()
{
    using var handler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.OK, [1, 2, 3]);
        response.Content.Headers.ContentEncoding.Add("gzip");
        return Task.FromResult(response);
    });
    using var downloader = new RemotePackageDownloader(handler);
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("identity content encoding", exception.Message);
}

static async Task RemoteRejectsMalformedArchive()
{
    var payload = "not a widget archive"u8.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromResult(Response(HttpStatusCode.OK, payload)));
    using var temp = new TemporaryDirectory();

    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/broken.gbarwidget",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "catalog"));
    Assert.Equal(1, result.Code);
    Assert.Contains("invalid_archive", result.Error);
}

static async Task RemoteHashMismatchCleansUp()
{
    using var temp = new TemporaryDirectory();
    using var handler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, [1, 2, 3])));
    using var downloader = new RemotePackageDownloader(handler, TestDownloadOptions(temp.Path, 32));
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), new byte[32], CancellationToken.None));
    Assert.Contains("SHA-256 mismatch", exception.Message);
    Assert.True(!Directory.EnumerateFileSystemEntries(temp.Path).Any(), "Integrity failure leaked a package or directory.");
}

static async Task RemotePackageHasIntegrityGuard()
{
    using var temp = new TemporaryDirectory();
    var bytes = new byte[] { 1, 2, 3 };
    using var handler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, bytes)));
    using var downloader = new RemotePackageDownloader(handler, TestDownloadOptions(temp.Path, 32));
    var downloaded = await downloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), SHA256.HashData(bytes), CancellationToken.None);
    Assert.True(File.Exists(downloaded.PackagePath), "Downloaded package is missing.");
    await Assert.ThrowsAsync<IOException>(async () =>
    {
        await using var write = new FileStream(downloaded.PackagePath, FileMode.Open, FileAccess.Write, FileShare.Read);
    });
    await downloaded.DisposeAsync();
    Assert.True(!File.Exists(downloaded.PackagePath), "Disposed download was not deleted.");
    Assert.True(!Directory.EnumerateFileSystemEntries(temp.Path).Any(), "Disposed download directory was not deleted.");
}

static async Task RemoteTimeoutsAreBounded()
{
    using var responseHandler = new StubHttpHandler(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Response(HttpStatusCode.OK, []);
    });
    using var responseDownloader = new RemotePackageDownloader(responseHandler, new RemoteDownloadOptions
    {
        ResponseTimeout = TimeSpan.FromMilliseconds(20),
        OverallTimeout = TimeSpan.FromSeconds(2),
    });
    var responseException = await Assert.ThrowsAsync<CliOperationException>(() => responseDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("response timeout", responseException.Message);

    using var overallHandler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(new BlockingReadStream()),
    }));
    using var overallDownloader = new RemotePackageDownloader(overallHandler, new RemoteDownloadOptions
    {
        ResponseTimeout = TimeSpan.FromSeconds(2),
        OverallTimeout = TimeSpan.FromMilliseconds(20),
    });
    var overallException = await Assert.ThrowsAsync<CliOperationException>(() => overallDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("overall timeout", overallException.Message);
}

static async Task RemoteFailuresRedactSecrets()
{
    const string secret = "super-secret-token";
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException(
            $"GET https://widgets.example/release.gbarwidget?token={secret} failed")));
    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/release.gbarwidget",
        "--sha256", new string('0', 64));
    Assert.Equal(1, result.Code);
    Assert.Contains("Remote package request failed", result.Error);
    Assert.DoesNotContain(secret, result.Error);
    Assert.DoesNotContain("?token=", result.Error);
}

static async Task RemoteCancellationIsBounded()
{
    using var handler = new StubHttpHandler(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Response(HttpStatusCode.OK, []);
    });
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await CliApplication.RunAsync(
        ["install", "https://widgets.example/release.gbarwidget", "--sha256", new string('0', 64)],
        output,
        error,
        handler,
        cancellation.Token);
    Assert.Equal(130, code);
    Assert.Contains("operation cancelled", error.ToString());
}

static async Task StateCommandRejectsMissingWidget()
{
    using var temp = new TemporaryDirectory();
    var catalog = Path.Combine(temp.Path, "catalog");
    var empty = await RunCli("list", "--catalog", catalog);
    Assert.Equal(0, empty.Code);
    Assert.Contains("No widgets installed", empty.Output);
    var missing = await RunCli("disable", "dev.test.missing", "--catalog", catalog);
    Assert.Equal(1, missing.Code);
    Assert.Contains("not installed", missing.Error);
}

static async Task UnknownCommand()
{
    var result = await RunCli("explode");
    Assert.Equal(2, result.Code);
    Assert.Contains("Unknown command", result.Error);
}

static ViewSnapshot BuildSnapshot() => new WidgetView(
    UI.Row("root",
        UI.Button("Lower", "lower", "lower")
            .FocusRight("apply")
            .Shortcut(ControllerButton.LeftBumper),
        UI.Button("Apply", "apply", "apply")
            .FocusLeft("lower")
            .FocusRight("raise")
            .Shortcut(ControllerButton.X),
        UI.Button("Raise", "raise", "raise")
            .FocusLeft("apply")
            .Shortcut(ControllerButton.RightBumper)),
    "apply").CreateSnapshot("test.instance", 7);

static string CreatePackageSource(string root, string id, string publisher, string version)
{
    var source = Path.Combine(root, $"source-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(source, "payload"));
    Directory.CreateDirectory(Path.Combine(source, "styles"));
    File.WriteAllBytes(Path.Combine(source, "manifest.json"), ManifestJson.Serialize(BuildManifest(id, publisher, version)));
    File.WriteAllBytes(Path.Combine(source, "payload", "Widget.dll"), "intentionally-not-an-assembly"u8.ToArray());
    File.WriteAllText(Path.Combine(source, "styles", "default.gbss"), "text { color: #ffffff; }");
    return source;
}

static async Task<string> CreatePackedPackageAsync(string root, string id, string version)
{
    var source = CreatePackageSource(root, id, "dev.test", version);
    var package = Path.Combine(root, $"{id}-{version}-{Guid.NewGuid():N}.gbarwidget");
    var result = await RunCli("pack", source, "--output", package);
    Assert.Equal(0, result.Code);
    return package;
}

static HttpResponseMessage Response(HttpStatusCode status, byte[] content) => new(status)
{
    Content = new ByteArrayContent(content),
};

static RemoteDownloadOptions TestDownloadOptions(string root, long maximumBytes) => new()
{
    MaximumBytes = maximumBytes,
    TemporaryDirectoryRoot = root,
};

static WidgetManifest BuildManifest(string id, string publisher, string version) => new()
{
    Id = id,
    Publisher = publisher,
    Name = "CLI Test Widget",
    Version = version,
    HostApi = new HostApiRange("1.0", 1),
    Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/Widget.dll", "Example.Widget"),
    Permissions = [],
};

static void WriteArchiveEntry(ZipArchive archive, string path, byte[] content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
    using var stream = entry.Open();
    stream.Write(content);
}

static async Task<CliResult> RunCli(params string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await CliApplication.RunAsync(args, output, error);
    return new CliResult(code, output.ToString(), error.ToString());
}

static async Task<CliResult> RunCliWithHandler(HttpMessageHandler handler, params string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await CliApplication.RunAsync(args, output, error, handler);
    return new CliResult(code, output.ToString(), error.ToString());
}

file sealed record CliResult(int Code, string Output, string Error);

file sealed class StubHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

file sealed class RepeatingReadStream : Stream
{
    private readonly long _length;
    private long _remaining;

    public RepeatingReadStream(long length)
    {
        _length = length;
        _remaining = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = (int)Math.Min(count, _remaining);
        Array.Fill(buffer, (byte)0x5A, offset, read);
        _remaining -= read;
        return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = (int)Math.Min(buffer.Length, _remaining);
        buffer.Span[..read].Fill(0x5A);
        _remaining -= read;
        return ValueTask.FromResult(read);
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

file sealed class BlockingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gbar-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected output to contain '{expected}'. Actual: {actual}");
    }

    public static void DoesNotContain(string expected, string actual)
    {
        if (actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected output not to contain '{expected}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}");
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
