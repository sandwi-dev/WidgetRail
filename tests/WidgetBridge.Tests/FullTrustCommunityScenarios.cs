using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using CatalogService = WidgetRail.WidgetCatalog.WidgetCatalog;

internal static class FullTrustCommunityScenarios
{
    private const string SpotifyConfigurationRootEnvironmentVariable =
        "WRAIL_SPOTIFY_CONFIGURATION_ROOT";

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
        var package = Path.Combine(temporary.Path, "missing.wrwidget");
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

    internal static async Task SpotifyUsesTheOrdinaryRuntime()
    {
        var root = RepositoryRoot();
        var applicationOutput = Path.Combine(
            root, "samples", "SpotifyWidget", "Application", "bin", "Release",
            "net8.0", "win-x64");
        var playbackHostOutput = Path.Combine(
            root, "samples", "SpotifyWidget", "PlaybackHost", "bin", "Release",
            "net8.0-windows10.0.19041.0", "win-x64");
        var hostOutput = Path.Combine(root, "src", "OverlayHost", "out", "Release");
        AssertHostRuntimeArtifacts(root, hostOutput);
        Check(File.Exists(Path.Combine(applicationOutput, "SpotifyApplication.exe")),
            "The package-owned Spotify application was not built.");
        Check(File.Exists(Path.Combine(playbackHostOutput, "SpotifyPlaybackHost.exe")),
            "The package-owned Spotify playback host was not built.");
        using var temporary = new ScenarioDirectory();
        var package = CreateSpotifyPackage(
            root, applicationOutput, playbackHostOutput, temporary.Path);
        var betaOutput = FixtureOutput(root, "FullTrustBetaFixture");
        var betaPackage = CreatePackage(
            temporary.Path, betaOutput, "FullTrustBetaFixture.exe",
            "org.independent.beta-utility", "3.2.1");
        var installedRoot = Path.Combine(temporary.Path, "installed");
        var catalog = new CatalogService(installedRoot);
        var installed = await catalog.InstallAsync(
            package, WidgetPackageTrustApproval.FullTrustCurrentUser);
        var beta = await catalog.InstallAsync(
            betaPackage, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await catalog.SetEnabledAsync(
            installed.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await catalog.SetEnabledAsync(
            beta.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);

        var load = await BridgeCatalog.LoadWithInstalledAsync(
            Path.Combine(hostOutput, "widget-catalog.json"),
            installedRoot,
            Path.Combine(hostOutput, "runtime", "WidgetWorkerHost", "WidgetWorkerHost.exe"));
        Check(load.InstalledCatalogValid && load.Warnings.Count == 0,
            "The packaged Spotify application failed ordinary catalog admission.");
        var configured = load.Catalog.GetConfigured(installed.Id);
        var betaConfigured = load.Catalog.GetConfigured(beta.Id);
        AssertFullTrust(configured, "SpotifyApplication.exe");
        AssertFullTrust(betaConfigured, "FullTrustBetaFixture.exe");
        Check(configured.DeclaredCapabilities.Count == 0 &&
              configured.WorkerArguments.Count == 0,
            "Spotify retained a product capability or special host argument.");

        foreach (var widgetId in new[]
                 {
                     "settings", "audio-mixer", "network-controls", "games-apps",
                     "media-sessions",
                 })
        {
            var hostRuntime = load.Catalog.GetConfigured(widgetId);
            await using var hostClient = HostRuntimeClient(hostRuntime);
            await hostClient.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var firstSnapshot = await hostClient.GetSnapshotAsync();
            Check(firstSnapshot.Sequence > 0 &&
                  !string.IsNullOrWhiteSpace(firstSnapshot.Root.Id),
                $"Host runtime '{widgetId}' did not publish its first snapshot.");
            await hostClient.StopAsync();
        }

        var previousRoot = Environment.GetEnvironmentVariable(
            SpotifyConfigurationRootEnvironmentVariable);
        Environment.SetEnvironmentVariable(
            SpotifyConfigurationRootEnvironmentVariable,
            Path.Combine(temporary.Path, "isolated-configuration"));
        try
        {
            using var invalidated = new SemaphoreSlim(0);
            await using var client = Client(configured);
            client.Invalidated += (_, _) => invalidated.Release();
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var snapshot = await client.GetSnapshotAsync();
            Check(snapshot.Sequence > 0,
                "Spotify's first ordinary snapshot did not carry positive authority.");
            for (var attempt = 0; attempt != 4; attempt++)
            {
                if (TryFind(snapshot.Root, "spotify.setup.open") is not null) break;
                await invalidated.WaitAsync(TimeSpan.FromSeconds(5));
                snapshot = await client.GetSnapshotAsync();
            }
            Check(TryFind(snapshot.Root, "spotify.setup.open") is not null &&
                  snapshot.InitialFocusId == "spotify.setup.open",
                "The ordinary full-trust route did not return Spotify's credential-free setup snapshot.");

            await client.StopAsync();
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var reopened = await client.GetSnapshotAsync();
            Check(client.Starts == 2 && reopened.Sequence > 0,
                "Spotify did not complete one bounded ordinary-runtime reopen.");

            await using var betaClient = Client(betaConfigured);
            await betaClient.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var betaSnapshot = await betaClient.GetSnapshotAsync();
            Check(Find(betaSnapshot.Root, "beta-title").Text ==
                  "Independent beta application",
                "A neighboring generic full-trust worker was not usable after Spotify reopened.");
            await betaClient.StopAsync();
            await client.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                SpotifyConfigurationRootEnvironmentVariable, previousRoot);
        }

        await catalog.SetEnabledAsync(installed.Id, false);
        await catalog.SetEnabledAsync(beta.Id, false);
        var removed = await catalog.UninstallAsync(installed.Id);
        var removedBeta = await catalog.UninstallAsync(beta.Id);
        Check(removed.RemovedVersions.Count == 1 &&
              removedBeta.RemovedVersions.Count == 1,
            "The ordinary Spotify package did not disable and remove cleanly.");
    }

    internal static async Task PlayniteLibraryUsesTheOrdinaryRuntime()
    {
        var root = RepositoryRoot();
        var applicationOutput = Path.Combine(
            root, "samples", "PlayniteLibraryWidget", "Application",
            "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64");
        Check(File.Exists(Path.Combine(applicationOutput, "PlayniteLibraryApplication.exe")),
            "The package-owned Playnite Library application was not built.");
        Check(File.Exists(Path.Combine(applicationOutput, "Microsoft.Windows.SDK.NET.dll")) &&
              File.Exists(Path.Combine(applicationOutput, "PlayniteLibraryWidget.dll")),
            "The Playnite Library package graph did not contain the Windows runtime and " +
            "capability-free widget core exclusively.");
        using var temporary = new ScenarioDirectory();
        var package = CreatePlayniteLibraryPackage(root, applicationOutput, temporary.Path);
        var installedRoot = Path.Combine(temporary.Path, "installed");
        var catalog = new CatalogService(installedRoot);
        var installed = await catalog.InstallAsync(
            package, WidgetPackageTrustApproval.FullTrustCurrentUser);
        await catalog.SetEnabledAsync(
            installed.Id, true, WidgetPackageTrustApproval.FullTrustCurrentUser);

        var trustedCatalog = CreateTrustedCatalog(temporary.Path);
        var load = await BridgeCatalog.LoadWithInstalledAsync(
            trustedCatalog.CatalogPath, installedRoot, trustedCatalog.WorkerHostPath);
        Check(load.InstalledCatalogValid && load.Warnings.Count == 0,
            "The packaged Playnite Library application failed ordinary catalog admission.");
        var configured = load.Catalog.GetConfigured(installed.Id);
        AssertFullTrust(configured, "PlayniteLibraryApplication.exe");
        Check(configured.DeclaredCapabilities.Count == 0 &&
              configured.WorkerArguments.Count == 0,
            "Playnite Library retained a product capability or special host argument.");

        var variable = "WRAIL_PLAYNITE_LIBRARY_DATA_ROOT";
        var previousRoot = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, Path.Combine(temporary.Path, "data"));
        try
        {
            using var invalidated = new SemaphoreSlim(0);
            await using var client = Client(configured);
            client.Invalidated += (_, _) => invalidated.Release();
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var snapshot = await client.GetSnapshotAsync();
            for (var attempt = 0; attempt != 12; attempt++)
            {
                if (UsablePlayniteLibraryLibrary(snapshot)) break;
                await invalidated.WaitAsync(TimeSpan.FromSeconds(5));
                snapshot = await client.GetSnapshotAsync();
            }
            Check(UsablePlayniteLibraryLibrary(snapshot),
                $"The ordinary full-trust route did not reach a usable library/source " +
                $"snapshot. Root '{snapshot.Root.Id}', focus " +
                $"'{snapshot.InitialFocusId ?? "<null>"}'.");
            await client.StopAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previousRoot);
        }

        await catalog.SetEnabledAsync(installed.Id, false);
        var removed = await catalog.UninstallAsync(installed.Id);
        Check(removed.RemovedVersions.Count == 1,
            "The ordinary Playnite Library package did not disable and remove cleanly.");
    }

    private static bool UsablePlayniteLibraryLibrary(ViewSnapshot snapshot)
    {
        if (snapshot.InitialFocusId is null ||
            TryFind(snapshot.Root, "playnite-library.retry") is not null)
            return false;
        var focused = TryFind(snapshot.Root, snapshot.InitialFocusId);
        return focused is
            {
                Kind: ViewNodeKind.ActionSurface,
                ActionId: "playnite-library.launch",
            } &&
            focused.Id.StartsWith("playnite-library.item.grid.", StringComparison.Ordinal) &&
            Descendants(snapshot.Root).Any(node =>
                node.Kind == ViewNodeKind.ActionSurface &&
                node.ActionId == "playnite-library.launch" &&
                node.Id.StartsWith("playnite-library.item.grid.", StringComparison.Ordinal));
    }

    private static IEnumerable<ViewNode> Descendants(ViewNode root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }

    private static WidgetProcessClient Client(ConfiguredWidget configured) => new(new WidgetProcessOptions
    {
        ExecutablePath = configured.WorkerExecutable,
        Arguments = configured.WorkerArguments,
        WidgetInstanceId = configured.InstanceId,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5),
        MaximumMessageBytes = WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
        MaximumRestartAttempts = 2,
        ContentLeaseFactory = configured.ContentLeaseFactory,
        IsolationPolicy = WidgetWorkerIsolationPolicy.FullTrustCommunity,
    });

    private static WidgetProcessClient HostRuntimeClient(ConfiguredWidget configured) =>
        new(new WidgetProcessOptions
        {
            ExecutablePath = configured.WorkerExecutable,
            Arguments = configured.WorkerArguments,
            WidgetInstanceId = configured.InstanceId,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
            MaximumMessageBytes = WidgetRuntimeProtocol.DefaultMaximumMessageBytes,
            MaximumRestartAttempts = 0,
            ContentLeaseFactory = configured.ContentLeaseFactory,
            IsolationPolicy = configured.RequiresAppContainer
                ? WidgetWorkerIsolationPolicy.RequireAppContainer
                : WidgetWorkerIsolationPolicy.HostTrustedJobOnly,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
        });

    private static void AssertHostRuntimeArtifacts(string repositoryRoot, string hostOutput)
    {
        var runtimeRoot = Path.Combine(hostOutput, "runtime");
        var expectedDirectories = new[]
        {
            "AudioMixer", "Bridge", "EmbeddedMediaSample", "GamesApps", "MediaSessions",
            "NetworkControls", "Settings", "WidgetWorkerHost",
        };
        var actualDirectories = Directory.EnumerateDirectories(runtimeRoot)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Check(actualDirectories.SequenceEqual(expectedDirectories),
            $"Host runtime inventory was incoherent: {string.Join(",", actualDirectories)}.");

        foreach (var assembly in new[] { "WidgetProtocol.dll", "WidgetRuntime.dll", "WidgetSdk.dll" })
        {
            var source = Path.Combine(
                repositoryRoot, "src", Path.GetFileNameWithoutExtension(assembly),
                "bin", "Release", "net8.0", assembly);
            foreach (var runtime in new[] { "Bridge", "WidgetWorkerHost", "Settings" })
                CheckFilesEqual(source, Path.Combine(runtimeRoot, runtime, assembly));
        }

        foreach (var (runtime, project, assembly) in new[]
                 {
                     ("AudioMixer", "AudioMixerWidget", "AudioMixerWidget.dll"),
                     ("NetworkControls", "NetworkControlsWidget", "NetworkControlsWidget.dll"),
                     ("GamesApps", "GamesAppsWidget", "GamesAppsWidget.dll"),
                     ("MediaSessions", "MediaSessionsWidget", "MediaSessionsWidget.dll"),
                 })
        {
            var payload = Path.Combine(runtimeRoot, runtime, "payload");
            CheckFilesEqual(
                Path.Combine(repositoryRoot, "src", "FirstPartyWidgets", project,
                    "bin", "Release", "net8.0", assembly),
                Path.Combine(payload, assembly));
            Check(!File.Exists(Path.Combine(payload, "WidgetProtocol.dll")) &&
                  !File.Exists(Path.Combine(payload, "WidgetSdk.dll")),
                $"Bundled runtime '{runtime}' shadowed the generic host contracts.");
        }

        CheckFilesEqual(
            Path.Combine(repositoryRoot, "src", "FirstPartyWidgets", "SettingsWidget.Worker",
                "bin", "Release", "net8.0", "SettingsWidget.Worker.dll"),
            Path.Combine(runtimeRoot, "Settings", "SettingsWidget.Worker.dll"));
        CheckFilesEqual(
            Path.Combine(repositoryRoot, "src", "FirstPartyWidgets", "SettingsWidget",
                "bin", "Release", "net8.0", "SettingsWidget.dll"),
            Path.Combine(runtimeRoot, "Settings", "payload", "SettingsWidget.dll"));
    }

    private static void CheckFilesEqual(string expected, string actual)
    {
        Check(File.Exists(expected), $"Expected current build artifact was missing: {expected}.");
        Check(File.Exists(actual), $"Host runtime artifact was missing: {actual}.");
        var expectedHash = SHA256.HashData(File.ReadAllBytes(expected));
        var actualHash = SHA256.HashData(File.ReadAllBytes(actual));
        Check(expectedHash.AsSpan().SequenceEqual(actualHash),
            $"Host runtime artifact was stale: {actual}.");
    }

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
        var package = Path.Combine(destination, $"{id}-{version}.wrwidget");
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

    private static string CreateSpotifyPackage(
        string repositoryRoot,
        string applicationOutput,
        string playbackHostOutput,
        string destination)
    {
        var package = Path.Combine(destination, "widgetrail.samples.spotify-0.3.0.wrwidget");
        using var stream = new FileStream(
            package, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        Write(archive, "manifest.json", File.ReadAllBytes(Path.Combine(
            repositoryRoot, "samples", "SpotifyWidget", "manifest.json")));
        Write(archive, "styles/default.wrss", File.ReadAllBytes(Path.Combine(
            repositoryRoot, "samples", "SpotifyWidget", "styles", "default.wrss")));
        var payload = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        AddGraph(applicationOutput);
        AddGraph(playbackHostOutput);
        foreach (var (path, bytes) in payload) Write(archive, "payload/" + path, bytes);
        return package;

        void AddGraph(string graph)
        {
            var prefix = Path.TrimEndingDirectorySeparator(graph) +
                         Path.DirectorySeparatorChar;
            foreach (var file in Directory.EnumerateFiles(graph, "*", SearchOption.AllDirectories)
                         .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                                        !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                                        !Path.GetFileName(path).Equals(
                                            "Microsoft.Windows.SDK.NET.dll",
                                            StringComparison.Ordinal)))
            {
                var relative = file[prefix.Length..].Replace('\\', '/');
                var bytes = File.ReadAllBytes(file);
                if (payload.TryGetValue(relative, out var current))
                {
                    Check(current.AsSpan().SequenceEqual(bytes),
                        $"The Spotify application graphs disagree on {relative}.");
                    continue;
                }
                payload.Add(relative, bytes);
            }
        }
    }

    private static string CreatePlayniteLibraryPackage(
        string repositoryRoot,
        string applicationOutput,
        string destination)
    {
        var package = Path.Combine(
            destination, "widgetrail.samples.playnite-library-0.2.2.wrwidget");
        using var stream = new FileStream(
            package, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var widgetRoot = Path.Combine(
            repositoryRoot, "samples", "PlayniteLibraryWidget");
        Write(archive, "manifest.json", File.ReadAllBytes(Path.Combine(
            widgetRoot, "manifest.json")));
        Write(archive, "styles/default.wrss", File.ReadAllBytes(Path.Combine(
            widgetRoot, "styles", "default.wrss")));
        foreach (var file in Directory.EnumerateFiles(
                     applicationOutput, "*", SearchOption.AllDirectories)
                 .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                                !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                 .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(applicationOutput, file).Replace('\\', '/');
            Write(archive, "payload/" + relative, File.ReadAllBytes(file));
        }
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

    private static ViewNode? TryFind(ViewNode node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
            if (TryFind(child, id) is { } found) return found;
        return null;
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
                System.IO.Path.GetTempPath(), $"wrail-full-trust-{Guid.NewGuid():N}");
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
