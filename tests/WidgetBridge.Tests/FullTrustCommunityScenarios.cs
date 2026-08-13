using System.IO.Compression;
using System.Text;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

internal static class FullTrustCommunityScenarios
{
    internal static async Task TwoApplicationsUseTheOrdinaryRuntime()
    {
        var root = RepositoryRoot();
        var alphaOutput = FixtureOutput(root, "FullTrustAlphaFixture");
        var betaOutput = FixtureOutput(root, "FullTrustBetaFixture");
        using var temporary = new ScenarioDirectory();
        var catalog = new CatalogService(Path.Combine(temporary.Path, "installed"));
        var alphaPackage = CreatePackage(
            temporary.Path, alphaOutput, "FullTrustAlphaFixture.exe",
            "dev.unrelated.alpha-application", "1.0.0");
        var betaPackage = CreatePackage(
            temporary.Path, betaOutput, "FullTrustBetaFixture.exe",
            "org.independent.beta-utility", "3.2.1");

        await ThrowsCodeAsync("full_trust_approval_required",
            () => catalog.InstallAsync(alphaPackage));
        var alpha = await catalog.InstallAsync(
            alphaPackage, WidgetPackageTrustApproval.FullTrustCurrentUser);
        var beta = await catalog.InstallAsync(
            betaPackage, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await ThrowsCodeAsync("full_trust_approval_required",
            () => catalog.SetEnabledAsync(alpha.Id, true));
        await catalog.SetEnabledAsync(
            alpha.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await catalog.SetEnabledAsync(
            beta.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);

        var trustedCatalog = CreateTrustedCatalog(temporary.Path);
        var load = await BridgeCatalog.LoadWithInstalledAsync(
            trustedCatalog.CatalogPath,
            Path.Combine(temporary.Path, "installed"),
            trustedCatalog.WorkerHostPath);
        Check(load.InstalledCatalogValid, "The installed full-trust catalog failed discovery.");
        Check(load.Warnings.Count == 0, "The full-trust catalog emitted warnings.");
        var alphaConfigured = load.Catalog.GetConfigured(alpha.Id);
        var betaConfigured = load.Catalog.GetConfigured(beta.Id);
        AssertFullTrust(alphaConfigured, "FullTrustAlphaFixture.exe");
        AssertFullTrust(betaConfigured, "FullTrustBetaFixture.exe");

        await using var alphaClient = Client(alphaConfigured);
        await alphaClient.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
        var first = await alphaClient.GetSnapshotAsync();
        var firstResult = Find(first.Root, "alpha-result").Text ?? string.Empty;
        var firstRun = ParseRun(firstResult);
        Check(firstResult.Contains("child=True", StringComparison.Ordinal) &&
              firstResult.Contains("file=True", StringComparison.Ordinal) &&
              firstResult.Contains("database=True", StringComparison.Ordinal) &&
              firstResult.Contains("https=True", StringComparison.Ordinal),
            "The external application did not complete child/file/database/fake-HTTPS work.");

        var failed = new TaskCompletionSource<WidgetFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        alphaClient.Failed += (_, failure) => failed.TrySetResult(failure);
        await alphaClient.SendActionAsync(new WidgetActionEvent("crash", "alpha-crash"));
        var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(failure.CanRestart && failure.Reason is
                WidgetFailureReason.ProcessExited or WidgetFailureReason.TransportFailure,
            "The full-trust crash did not retain ordinary restart authority.");
        var restarted = await alphaClient.GetSnapshotAsync();
        Check(alphaClient.Starts == 2, "The full-trust application did not restart once.");
        Check(ParseRun(Find(restarted.Root, "alpha-result").Text ?? string.Empty) ==
              firstRun + 1,
            "The restarted application did not retain its package-owned database state.");

        await using var betaClient = Client(betaConfigured);
        await betaClient.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        var betaSnapshot = await betaClient.GetSnapshotAsync();
        Check(Find(betaSnapshot.Root, "beta-title").Text ==
              "Independent beta application",
            "The unrelated beta application did not use the same runtime path.");

        await catalog.SetEnabledAsync(alpha.Id, false);
        await alphaClient.StopAsync();
        var alphaV2Package = CreatePackage(
            temporary.Path, alphaOutput, "FullTrustAlphaFixture.exe",
            alpha.Id, "2.0.0");
        var alphaV2 = await catalog.InstallAsync(
            alphaV2Package, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await catalog.SetActiveVersionAsync(alpha.Id, alphaV2.Version);
        await catalog.SetEnabledAsync(
            alpha.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);
        var replacement = await BridgeCatalog.LoadWithInstalledAsync(
            trustedCatalog.CatalogPath,
            Path.Combine(temporary.Path, "installed"),
            trustedCatalog.WorkerHostPath);
        Check(replacement.Catalog.GetConfigured(alpha.Id).WorkerFingerprint !=
              alphaConfigured.WorkerFingerprint,
            "Selecting a replacement package reused the retired generation.");

        await catalog.SetEnabledAsync(alpha.Id, false);
        await catalog.SetEnabledAsync(beta.Id, false);
        await betaClient.StopAsync();
        var removedAlpha = await catalog.UninstallAsync(alpha.Id);
        var removedBeta = await catalog.UninstallAsync(beta.Id);
        Check(removedAlpha.RemovedVersions.Count == 2 &&
              removedBeta.RemovedVersions.Count == 1,
            "Disable/drain/removal did not retire the exact installed versions.");
    }

    internal static async Task MissingEntrypointAndManifestPromotionFailClosed()
    {
        using var temporary = new ScenarioDirectory();
        var package = Path.Combine(temporary.Path, "missing.gbarwidget");
        var manifest = Manifest(
            "net.example.missing-application", "1.0.0", "payload/missing.exe");
        await using (var stream = new FileStream(
            package, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            Write(archive, "manifest.json", ManifestJson.Serialize(manifest));
        var catalog = new CatalogService(Path.Combine(temporary.Path, "installed"));
        await ThrowsCodeAsync("missing_entrypoint", () => catalog.InstallAsync(
            package, WidgetPackageTrustApproval.FullTrustCurrentUser));

        var sandbox = manifest with
        {
            Entrypoint = new WidgetEntrypoint(
                WidgetEntrypointRuntimes.DotNetWorker,
                "payload/worker.dll", "Fixture.Worker"),
        };
        Check(WidgetManifestTrust.Resolve(sandbox) == WidgetExecutionTrust.Sandboxed,
            "A sandboxed manifest was silently promoted to full trust.");
    }

    private static WidgetProcessClient Client(ConfiguredWidget configured) => new(new WidgetProcessOptions
    {
        ExecutablePath = configured.WorkerExecutable,
        Arguments = configured.WorkerArguments,
        WidgetInstanceId = configured.InstanceId,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5),
        MaximumMessageBytes = 64 * 1024,
        MaximumRestartAttempts = 2,
        ContentLeaseFactory = configured.ContentLeaseFactory,
        IsolationPolicy = WidgetWorkerIsolationPolicy.FullTrustCommunity,
    });

    private static void AssertFullTrust(ConfiguredWidget configured, string executableName)
    {
        Check(configured.ExecutionTrust == WidgetExecutionTrust.FullTrustCurrentUser,
            "The manifest runtime did not become host-owned full-trust policy.");
        Check(!configured.RequiresAppContainer && configured.IsolationKey is null,
            "The full-trust application retained AppContainer identity.");
        Check(configured.DeclaredCapabilities.Count == 0 &&
              configured.ReadOnlyPaths.Count == 0,
            "The full-trust application retained sandbox broker authority.");
        Check(configured.ContentLeaseFactory is not null,
            "The exact immutable package generation lost its launch lease.");
        Check(Path.GetFileName(configured.WorkerExecutable) == executableName &&
              configured.WorkerArguments.Count == 0 &&
              !configured.UsesGenericWorkerHost,
            "The supervisor did not start the exact validated package executable.");
    }

    private static string CreatePackage(
        string destination,
        string output,
        string executable,
        string id,
        string version)
    {
        var package = Path.Combine(destination, $"{id}-{version}.gbarwidget");
        var manifest = Manifest(id, version, $"payload/{executable}");
        using var stream = new FileStream(
            package, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(archive, "manifest.json", ManifestJson.Serialize(manifest));
        foreach (var file in Directory.EnumerateFiles(output)
                     .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                     .Order(StringComparer.Ordinal))
            Write(archive, "payload/" + Path.GetFileName(file), File.ReadAllBytes(file));
        return package;
    }

    private static WidgetManifest Manifest(string id, string version, string executable) => new()
    {
        Id = id,
        Publisher = id[..id.LastIndexOf('.')],
        Name = id,
        Version = version,
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint(
            WidgetEntrypointRuntimes.FullTrustApplicationV1,
            Executable: executable),
        Permissions = [],
        OptionalPermissions = [],
        Architectures = ["x64"],
    };

    private static (string CatalogPath, string WorkerHostPath) CreateTrustedCatalog(
        string directory)
    {
        var worker = Path.Combine(directory, "trusted-worker.exe");
        File.Copy(Environment.ProcessPath!, worker);
        var catalog = Path.Combine(directory, "trusted-catalog.json");
        File.WriteAllText(catalog, """
            {
              "catalogVersion": 1,
              "widgets": [],
              "bundledWidgets": [],
              "genericWorkerExecutable": "trusted-worker.exe"
            }
            """);
        return (catalog, worker);
    }

    private static string FixtureOutput(string root, string name) => Path.Combine(
        root, "tests", name, "bin", "Release",
        "net8.0", "win-x64");

    private static string RepositoryRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, "src", "WidgetBridge")))
                return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    private static ViewNode Find(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            try { return Find(child, id); }
            catch (KeyNotFoundException) { }
        }
        throw new KeyNotFoundException(id);
    }

    private static int ParseRun(string evidence)
    {
        var token = evidence.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Single(part => part.StartsWith("run=", StringComparison.Ordinal));
        return int.Parse(token.AsSpan(4), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Write(ZipArchive archive, string path, ReadOnlySpan<byte> bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static async Task ThrowsCodeAsync(string code, Func<Task> action)
    {
        try { await action(); }
        catch (WidgetPackageException exception) when (exception.Code == code) { return; }
        throw new InvalidOperationException($"Expected package failure '{code}'.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioDirectory : IDisposable
    {
        internal ScenarioDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"gba-full-trust-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
