using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.FirstPartyWidgets.GamesApps;
using GameBarAlternative.FirstPartyWidgets.MediaSessions;
using GameBarAlternative.FirstPartyWidgets.NetworkControls;
using GameBarAlternative.FirstPartyWidgets.Settings;
using GameBarAlternative.Samples.SpotifyWidget;
using GameBarAlternative.Samples.YtMusicWidget;
using GameBarAlternative.GbarCli;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("FirstPartyWidgetConformance.Tests skipped: Windows AppContainer is required.");
    return 0;
}

var evidenceOutputIndex = Array.IndexOf(args, "--evidence-output");
if (evidenceOutputIndex >= 0)
{
    if (evidenceOutputIndex + 1 >= args.Length ||
        string.IsNullOrWhiteSpace(args[evidenceOutputIndex + 1]))
        throw new ArgumentException("--evidence-output requires a directory.");
    await ExportEvidenceAsync(Path.GetFullPath(args[evidenceOutputIndex + 1]));
    return 0;
}

if (args.Contains("--ytmusic-community-acceptance", StringComparer.Ordinal))
{
    var outputIndex = Array.IndexOf(args, "--acceptance-output");
    var output = outputIndex < 0
        ? null
        : outputIndex + 1 < args.Length && !string.IsNullOrWhiteSpace(args[outputIndex + 1])
            ? Path.GetFullPath(args[outputIndex + 1])
            : throw new ArgumentException("--acceptance-output requires a file path.");
    await YtMusicCommunityPackageRunsIsolated(output);
    Console.WriteLine("PASS YT Music isolated Community-addon acceptance");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bundled catalog derives runtime policy from real manifests", BundledCatalogUsesManifests),
    ("Bundled catalog rejects unsafe and ambiguous package sources", BundledCatalogRejectsUnsafeSources),
    ("Real first-party packages merge through the community catalog path", InstalledPackagesMerge),
    ("All first-party packages run through generic AppContainer worker and broker", PackagesRunIsolated),
    ("YT Music community package completes isolated install lifecycle recovery and removal", () => YtMusicCommunityPackageRunsIsolated()),
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
        var failure = $"FAIL {test.Name}: {exception}";
        failures.Add(failure);
        Console.Error.WriteLine(failure);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task BundledCatalogUsesManifests()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: false);
    var catalog = BridgeCatalog.Load(deployment.BundledCatalogPath);
    Assert.SequenceEqual(
        new[] { "media-sessions", "games-apps", "audio-mixer", "network-controls" },
        catalog.Widgets.Select(widget => widget.Id));

    foreach (var package in deployment.Packages)
    {
        var configured = catalog.GetConfigured(package.ShellId);
        Assert.Equal(package.Manifest.Id, configured.PackageId);
        Assert.Equal(package.Manifest.Publisher, configured.PublisherId);
        Assert.Equal(package.Manifest.Name, configured.Name);
        Assert.Equal(package.Icon, configured.Icon);
        Assert.True(configured.RequiresAppContainer,
            $"{package.Manifest.Name} must be AppContainer isolated.");
        Assert.Equal(deployment.BundledWorkerHostPath, configured.WorkerExecutable);
        Assert.SequenceEqual(package.DeclaredCapabilities, configured.DeclaredCapabilities);
        Assert.Equal(package.Manifest.ResourceRequest.MemoryMb, configured.MemoryLimitMb);
        AssertResidency(package.Manifest, configured.ResidencyPolicy);
        Assert.True(configured.ReadOnlyPaths.Count == 1 &&
            Path.GetFullPath(configured.ReadOnlyPaths[0]) == Path.GetFullPath(package.BundleRoot),
            $"{package.Manifest.Name} package root was not the only read-only package grant.");
        AssertWorkerArguments(configured, package.BundleRoot, package.Manifest);
    }
}

static async Task BundledCatalogRejectsUnsafeSources()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: false);
    var directory = Path.GetDirectoryName(deployment.BundledCatalogPath)!;
    var fixture = deployment.Packages[0];

    var escaped = Path.Combine(directory, "escaped-catalog.json");
    await WriteBundledCatalogAsync(escaped, "../outside", "runtime/WidgetWorkerHost/WidgetWorkerHost.exe",
        fixture.Manifest.Id);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(escaped));

    var missingHost = Path.Combine(directory, "missing-host-catalog.json");
    await WriteBundledCatalogAsync(
        missingHost,
        Path.GetRelativePath(directory, fixture.BundleRoot).Replace('\\', '/'),
        "runtime/missing/WidgetWorkerHost.exe",
        fixture.Manifest.Id);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(missingHost));

    var duplicate = Path.Combine(directory, "duplicate-package-catalog.json");
    var relativePackage = Path.GetRelativePath(directory, fixture.BundleRoot).Replace('\\', '/');
    await File.WriteAllTextAsync(duplicate, JsonSerializer.Serialize(new
    {
        catalogVersion = 1,
        genericWorkerExecutable = "runtime/WidgetWorkerHost/WidgetWorkerHost.exe",
        widgets = Array.Empty<object>(),
        bundledWidgets = new[]
        {
            new { id = "first", packageId = fixture.Manifest.Id, instanceId = "first.default", packageRoot = relativePackage, icon = "music", quickActions = Array.Empty<object>() },
            new { id = "second", packageId = fixture.Manifest.Id, instanceId = "second.default", packageRoot = relativePackage, icon = "music", quickActions = Array.Empty<object>() },
        },
    }));
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(duplicate));

    var link = Path.Combine(directory, "linked-package");
    Directory.CreateSymbolicLink(link, fixture.BundleRoot);
    var linked = Path.Combine(directory, "linked-catalog.json");
    await WriteBundledCatalogAsync(
        linked, "linked-package", "runtime/WidgetWorkerHost/WidgetWorkerHost.exe",
        fixture.Manifest.Id);
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(linked));

    var identityMismatch = Path.Combine(directory, "identity-mismatch-catalog.json");
    await WriteBundledCatalogAsync(
        identityMismatch,
        relativePackage,
        "runtime/WidgetWorkerHost/WidgetWorkerHost.exe",
        "org.gbar.firstparty.wrong-package");
    Assert.Throws<BridgeCatalogException>(() => BridgeCatalog.Load(identityMismatch));
}

static Task WriteBundledCatalogAsync(
    string path,
    string packageRoot,
    string workerHost,
    string packageId) =>
    File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
    {
        catalogVersion = 1,
        genericWorkerExecutable = workerHost,
        widgets = Array.Empty<object>(),
        bundledWidgets = new[]
        {
            new
            {
                id = "probe",
                packageId,
                instanceId = "probe.default",
                packageRoot,
                icon = "music",
                quickActions = Array.Empty<object>(),
            },
        },
    }));

static async Task InstalledPackagesMerge()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    Assert.True(load.InstalledCatalogValid, "Installed first-party catalog was rejected.");
    Assert.Equal(0, load.Warnings.Count);
    Assert.Equal(5, load.Catalog.Widgets.Count);
    var installedSnapshot = await new WidgetCatalog(deployment.InstalledCatalogRoot)
        .DiscoverAsync();

    foreach (var package in deployment.Packages)
    {
        var configured = load.Catalog.GetConfigured(package.Manifest.Id);
        var installedVersion = installedSnapshot.Widgets
            .Single(widget => widget.Id == package.Manifest.Id).ActiveVersion;
        Assert.True(configured.RequiresAppContainer,
            $"Installed {package.Manifest.Name} must require AppContainer.");
        Assert.Equal(deployment.WorkerHostPath, configured.WorkerExecutable);
        Assert.Equal(
            InstalledWidgetAuthority.PublisherId(installedVersion),
            configured.PublisherId);
        Assert.SequenceEqual(package.DeclaredCapabilities, configured.DeclaredCapabilities);
        AssertWorkerArguments(configured, configured.ReadOnlyPaths.Single(), package.Manifest);
        AssertResidency(package.Manifest, configured.ResidencyPolicy);
    }
    var community = deployment.YtMusicPackage ??
        throw new InvalidOperationException("YT Music community package was not installed.");
    var ytMusic = load.Catalog.GetConfigured(community.Manifest.Id);
    Assert.True(ytMusic.RequiresAppContainer,
        "YT Music did not use mandatory community AppContainer isolation.");
    Assert.Equal(deployment.WorkerHostPath, ytMusic.WorkerExecutable);
    Assert.Equal(WidgetGlyph.Music, ytMusic.Icon);
    Assert.SequenceEqual(community.DeclaredCapabilities, ytMusic.DeclaredCapabilities);
}

static async Task PackagesRunIsolated()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    await RunCatalogAsync(
        installed.Catalog,
        deployment.Packages,
        package => package.Manifest.Id,
        "installed");

    var bundled = BridgeCatalog.Load(deployment.BundledCatalogPath);
    await RunCatalogAsync(
        bundled,
        deployment.Packages,
        package => package.ShellId,
        "bundled");
}

static async Task YtMusicCommunityPackageRunsIsolated(string? acceptanceOutput = null)
{
    using var deployment = await Deployment.CreateAsync(
        installAsCommunity: true,
        ytMusicOnly: true);
    var phases = new List<string>();
    var package = deployment.YtMusicPackage ??
        throw new InvalidOperationException("YT Music community package was not produced.");
    Assert.True(File.Exists(package.PackagePath),
        "The public gbar pack workflow did not publish a .gbarwidget archive.");
    var packageInspection = await new WidgetCatalog(
            Path.Combine(deployment.RootPath, "validation-only"))
        .CreateInstaller().ValidateAsync(package.PackagePath);
    Assert.Equal(package.Manifest.Id, packageInspection.Id);
    Assert.Equal(package.Manifest.Version, packageInspection.Version.ToString());
    Assert.SequenceEqual(
        ["manifest.json", "payload/YtMusicWidget.dll", "styles/default.gbss"],
        ReadPackagePaths(package.PackagePath));
    phases.Add("clean-public-validate-pack-install");

    var catalog = new WidgetCatalog(deployment.InstalledCatalogRoot);
    var initialCatalog = await catalog.DiscoverAsync();
    Assert.Equal(1, initialCatalog.Widgets.Count);
    Assert.Equal(package.Manifest.Id, initialCatalog.Widgets[0].Id);
    Assert.True(initialCatalog.Widgets[0].Enabled,
        "The isolated package helper did not explicitly enable the reviewed addon.");
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    var configured = installed.Catalog.GetConfigured(package.Manifest.Id);
    Assert.True(configured.RequiresAppContainer,
        "YT Music community addon bypassed AppContainer isolation.");
    Assert.Equal(deployment.WorkerHostPath, configured.WorkerExecutable);
    Assert.SequenceEqual(
        new[] { "network.loopback:13091", "storage.private-secrets.v1" },
        configured.DeclaredCapabilities);
    Assert.Equal(WidgetGlyph.Music, configured.Icon);
    phases.Add("generic-appcontainer-catalog-route");

    var trackGeneration = 0;
    string TrackId() => $"conformance-track-{Volatile.Read(ref trackGeneration)}";
    string TrackState() => JsonSerializer.Serialize(new
    {
        id = TrackId(),
        playing = true,
        liked = false,
        disliked = false,
        shuffle = false,
        repeat = "off",
        uiProgress = 12.5,
        duration = 180,
    });
    string Track() => JsonSerializer.Serialize(new
    {
        video = new
        {
            title = $"Conformance Song {Volatile.Read(ref trackGeneration)}",
            author = "Conformance Artist",
            videoId = TrackId(),
        },
        music = new { album = "Conformance Album" },
        meta = new { thumbnail = "https://img.example/conformance.jpg", duration = 180 },
    });
    var nextCalls = 0;
    var previousCalls = 0;
    var toggleCalls = 0;
    var loopbackPaths = new List<string>();
    var backend = CreateBackend();
    backend.LoopbackHandler = (_, port, isPost, request, cancellationToken) =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(YtmDesktopApiClient.CompanionPort, port);
        lock (loopbackPaths) loopbackPaths.Add(request.Path);
        var response = (isPost, request.Path) switch
        {
            (false, "/") => new LoopbackJsonResponse(200, "{\"authRequired\":true}", []),
            (false, "/track") => new LoopbackJsonResponse(200, Track(), []),
            (false, "/track/state") => new LoopbackJsonResponse(200, TrackState(), []),
            (true, "/auth/requestcode") =>
                new LoopbackJsonResponse(200, "{\"code\":\"739204\"}", []),
            (true, "/auth/request") =>
                new LoopbackJsonResponse(200, "{\"token\":\"conformance-secret\"}", []),
            (true, "/track/next") => NextResponse(),
            (true, "/track/prev") => PreviousResponse(),
            (true, "/track/toggle-play-state") => CountResponse(ref toggleCalls),
            _ when isPost && request.Path.StartsWith("/track/", StringComparison.Ordinal) =>
                new LoopbackJsonResponse(200, "{}", []),
            _ => throw new InvalidOperationException(
                $"Unexpected YTMDesktop2 conformance request {request.Path}.")
        };
        return Task.FromResult(response);

        LoopbackJsonResponse NextResponse()
        {
            Interlocked.Increment(ref nextCalls);
            Interlocked.Increment(ref trackGeneration);
            return new LoopbackJsonResponse(200, "{}", []);
        }

        LoopbackJsonResponse PreviousResponse()
        {
            Interlocked.Increment(ref previousCalls);
            Interlocked.Increment(ref trackGeneration);
            return new LoopbackJsonResponse(200, "{}", []);
        }

        static LoopbackJsonResponse CountResponse(ref int calls)
        {
            Interlocked.Increment(ref calls);
            return new LoopbackJsonResponse(200, "{}", []);
        }
    };

    using var consentRoot = new TemporaryDirectory("gba-ytmusic-community-consent");
    var consent = new ConsentStore(consentRoot.Path);
    var identity = new BrokerWidgetIdentity(
        configured.PackageId,
        configured.PublisherId,
        configured.InstanceId);
    foreach (var capability in configured.DeclaredCapabilities)
        Assert.Equal<ConsentDecision?>(null,
            await consent.GetDecisionAsync(identity, capability));
    foreach (var capability in configured.DeclaredCapabilities)
        await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    Assert.SequenceEqual(configured.DeclaredCapabilities,
        (await consent.LoadAsync()).Entries
            .Where(entry => entry.PackageId == configured.PackageId &&
                            entry.PublisherId == configured.PublisherId)
            .Select(entry => entry.CapabilityId)
            .Order(StringComparer.Ordinal));
    phases.Add("explicit-required-and-optional-consent");

    WidgetProcessClient CreateClient(ConfiguredWidget source, int maximumRestarts = 2) =>
        new(new WidgetProcessOptions
    {
        ExecutablePath = source.WorkerExecutable,
        Arguments = source.WorkerArguments,
        WidgetInstanceId = source.InstanceId,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromSeconds(45),
        MaximumRestartAttempts = maximumRestarts,
        MemoryLimitBytes = source.MemoryLimitMb * 1024L * 1024L,
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = source.IsolationKey,
        ReadOnlyPaths = source.ReadOnlyPaths,
        CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
            source.PackageId,
            source.PublisherId,
            source.InstanceId,
            source.DeclaredCapabilities,
            consent,
            backend,
            context),
    });

    var client = CreateClient(configured);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var pairing = await WaitForSnapshotAsync(client, "Pair device");
    Assert.True(Nodes(pairing.Root).Any(node => node.ActionId == "pair"),
        "Pairing action was not controller reachable.");
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    await client.SendActionAsync(new WidgetActionEvent("pair", "pair"));
    ViewSnapshot connected;
    try
    {
        connected = await WaitForSnapshotAsync(client, "Conformance Song");
    }
    catch (Exception exception)
    {
        string paths;
        lock (loopbackPaths) paths = string.Join(", ", loopbackPaths);
        throw new InvalidOperationException(
            $"{exception.Message} Broker paths: {paths}; secret saves: " +
            $"{backend.PrivateSecretSaveCalls}.", exception);
    }
    Assert.Equal(1, backend.PrivateSecretSaveCalls);
    Assert.True(backend.LastLoopbackRequest?.BearerSecretSlot ==
        YtmDesktopApiClient.BearerSecretSlot,
        "Authenticated companion calls did not use the host-side secret slot.");
    phases.Add("simulated-companion-pairing-and-secret-write");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    connected = await client.GetSnapshotAsync();
    var quickAction = connected.QuickActions.Single(action =>
        action.Button == ControllerButton.RightBumper);
    Assert.Equal("next", quickAction.ActionId);
    Assert.Equal("network.loopback:13091", quickAction.Capability?.CapabilityId);
    Assert.Equal(WidgetLoopbackCapabilities.PostJsonOperation,
        quickAction.Capability?.OperationId);
    var inputSequence = 71L;
    var handled = await client.SendControllerInputAsync(
        new ControllerInputEvent(
            ControllerButton.RightBumper,
            ControllerEventPhase.Pressed,
            ControllerInputContext.DashboardQuickAction,
            Sequence: inputSequence,
            SnapshotSequence: connected.Sequence),
        new WidgetDashboardGestureAuthority(
            quickAction.Capability!.CapabilityId,
            quickAction.Capability.OperationId,
            inputSequence,
            connected.Sequence,
            TimeSpan.FromSeconds(2)));
    Assert.True(handled, "Dashboard RB quick action was not accepted by the addon.");
    await WaitUntilAsync(() => Volatile.Read(ref nextCalls) == 1);
    connected = await WaitForSnapshotAsync(client, "Conformance Song 1");
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);

    var openHandled = await client.SendControllerInputAsync(new ControllerInputEvent(
        ControllerButton.LeftBumper,
        ControllerEventPhase.Pressed,
        ControllerInputContext.OpenWidget,
        FocusedElementId: "play-pause",
        Sequence: 72,
        ActiveInputScopeId: connected.ActiveInputScopeId,
        SnapshotSequence: connected.Sequence));
    Assert.True(openHandled,
        "Open-widget LB was not routed from the play control through the active input scope.");
    await WaitUntilAsync(() => Volatile.Read(ref previousCalls) == 1);
    phases.Add("dashboard-and-open-widget-controller-routing");

    var startsBeforeSuspend = client.Starts;
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.UnloadAsync();
    Assert.True(!client.IsRunning, "Suspend-when-hidden retained a worker process.");
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    _ = await WaitForSnapshotAsync(client, "Conformance Song");
    Assert.Equal(startsBeforeSuspend + 1, client.Starts);
    phases.Add("background-suspend-and-visible-resume");

    var failure = new TaskCompletionSource<WidgetFailure>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    client.Failed += (_, value) => failure.TrySetResult(value);
    var crashedProcessId = client.WorkerProcessId ??
        throw new InvalidOperationException("The YT Music worker process was unavailable.");
    using (var process = Process.GetProcessById(crashedProcessId))
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
    var observedFailure = await failure.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert.True(observedFailure.CanRestart, "A clean crash consumed all restart authority.");
    _ = await WaitForSnapshotAsync(client, "Conformance Song");
    Assert.True(client.WorkerProcessId != crashedProcessId,
        "Crash recovery reused the terminated worker process.");
    phases.Add("worker-crash-and-bounded-restart");

    await client.DisposeAsync();
    client = CreateClient(configured);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    _ = await WaitForSnapshotAsync(client, "Conformance Song");
    Assert.Equal(1, client.Starts);
    phases.Add("host-force-reload-fresh-worker");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.DisposeAsync();

    await catalog.SetEnabledAsync(package.Manifest.Id, false);
    var installedVersion = Version.Parse(package.Manifest.Version);
    if (installedVersion.Build < 0 || installedVersion.Build == int.MaxValue)
        throw new InvalidOperationException(
            "YT Music acceptance requires a canonical three-part version with incrementable patch.");
    var updateVersion = new Version(
        installedVersion.Major,
        installedVersion.Minor,
        installedVersion.Build + 1).ToString(3);
    var updatePackage = await deployment.BuildYtMusicVersionAsync(updateVersion);
    var updateInspection = await catalog.CreateInstaller().ValidateAsync(updatePackage);
    Assert.Equal(updateVersion, updateInspection.Version.ToString());
    var updateInstall = await RunCliAsync(
        "install", updatePackage, "--catalog", deployment.InstalledCatalogRoot);
    Assert.Equal(0, updateInstall.Code);
    var preselection = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.Equal(package.Manifest.Version, preselection.ActiveVersion.Version.ToString());
    Assert.Equal(2, preselection.Versions.Count);
    Assert.Equal(0, (await RunCliAsync(
        "version", "select", package.Manifest.Id, updateVersion,
        "--catalog", deployment.InstalledCatalogRoot)).Code);
    Assert.Equal(0, (await RunCliAsync(
        "enable", package.Manifest.Id,
        "--catalog", deployment.InstalledCatalogRoot)).Code);

    var updatedLoad = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    var updatedConfigured = updatedLoad.Catalog.GetConfigured(package.Manifest.Id);
    Assert.True(!string.Equals(configured.PublisherId, updatedConfigured.PublisherId,
            StringComparison.Ordinal),
        "Changed package bytes inherited the prior unsigned runtime authority.");
    var updatedIdentity = new BrokerWidgetIdentity(
        updatedConfigured.PackageId,
        updatedConfigured.PublisherId,
        updatedConfigured.InstanceId);
    foreach (var capability in updatedConfigured.DeclaredCapabilities)
    {
        Assert.Equal<ConsentDecision?>(null,
            await consent.GetDecisionAsync(updatedIdentity, capability));
        await consent.SetDecisionAsync(updatedIdentity, capability, ConsentDecision.Grant);
    }
    client = CreateClient(updatedConfigured);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    _ = await WaitForSnapshotAsync(client, "Pair device");
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    await client.SendActionAsync(new WidgetActionEvent("pair", "pair"));
    _ = await WaitForSnapshotAsync(client, "Conformance Song");
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.DisposeAsync();
    phases.Add("disabled-update-review-new-authority-and-repair");

    Assert.Equal(0, (await RunCliAsync(
        "disable", package.Manifest.Id,
        "--catalog", deployment.InstalledCatalogRoot)).Code);
    var rollback = await RunCliAsync(
        "version", "rollback", package.Manifest.Id,
        "--catalog", deployment.InstalledCatalogRoot);
    Assert.Equal(0, rollback.Code);
    Assert.Equal(0, (await RunCliAsync(
        "enable", package.Manifest.Id,
        "--catalog", deployment.InstalledCatalogRoot)).Code);
    var rolledBackLoad = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    var rolledBackConfigured = rolledBackLoad.Catalog.GetConfigured(package.Manifest.Id);
    Assert.Equal(configured.PublisherId, rolledBackConfigured.PublisherId);
    foreach (var capability in rolledBackConfigured.DeclaredCapabilities)
        Assert.Equal<ConsentDecision?>(ConsentDecision.Grant,
            await consent.GetDecisionAsync(identity, capability));
    var secretSavesBeforeRollback = backend.PrivateSecretSaveCalls;
    client = CreateClient(rolledBackConfigured);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    _ = await WaitForSnapshotAsync(client, "Conformance Song");
    Assert.Equal(secretSavesBeforeRollback, backend.PrivateSecretSaveCalls);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.DisposeAsync();
    phases.Add("disabled-exact-version-rollback-restores-reviewed-authority");

    Assert.Equal(0, (await RunCliAsync(
        "disable", package.Manifest.Id,
        "--catalog", deployment.InstalledCatalogRoot)).Code);
    var uninstall = await RunCliAsync(
        "uninstall", package.Manifest.Id,
        "--catalog", deployment.InstalledCatalogRoot);
    Assert.Equal(0, uninstall.Code);
    Assert.Equal(0, (await catalog.DiscoverAsync()).Widgets.Count);
    Assert.True(!Directory.Exists(Path.Combine(
            deployment.InstalledCatalogRoot, "packages", package.Manifest.Id)),
        "YT Music package versions remained after uninstall.");
    var staging = Path.Combine(deployment.InstalledCatalogRoot, "staging");
    Assert.True(!Directory.Exists(staging) ||
                !Directory.EnumerateDirectories(staging, ".uninstall-*").Any(),
        "YT Music uninstall retained a retired package tree.");
    consentRoot.Dispose();
    Assert.True(!Directory.Exists(consentRoot.Path),
        "The isolated acceptance consent root was not cleaned up.");
    phases.Add("disable-uninstall-catalog-and-isolated-consent-cleanup");

    var repo = FindRepositoryRootForTest();
    using var productionCatalog = JsonDocument.Parse(File.ReadAllBytes(
        Path.Combine(repo, "src", "OverlayHost", "widget-catalog.json")));
    Assert.True(!productionCatalog.RootElement.GetProperty("widgets")
            .EnumerateArray().Any(widget =>
                widget.TryGetProperty("packageId", out var packageId) &&
                packageId.GetString()?.Contains("ytmusic", StringComparison.OrdinalIgnoreCase) == true),
        "YT Music still has a trusted catalog fallback.");
    var buildScript = File.ReadAllText(Path.Combine(repo, "src", "OverlayHost", "build.ps1"));
    Assert.True(!buildScript.Contains("YtMusicWidget.Worker", StringComparison.Ordinal),
        "OverlayHost still publishes the retired trusted YT Music worker.");
    phases.Add("no-trusted-fallback");

    if (acceptanceOutput is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(acceptanceOutput)!);
        await File.WriteAllTextAsync(acceptanceOutput, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            generatedUtc = DateTimeOffset.UtcNow,
            package = new
            {
                id = package.Manifest.Id,
                version = package.Manifest.Version,
                sha256 = Convert.ToHexString(SHA256.HashData(
                    File.ReadAllBytes(package.PackagePath))).ToLowerInvariant(),
                files = ReadPackagePaths(package.PackagePath),
            },
            isolation = new
            {
                catalog = "unique temporary root supplied explicitly to every catalog command",
                worker = "generic WidgetWorkerHost AppContainer",
                companion = "deterministic simulated broker backend; no live YTMDesktop2 service",
                secrets = "in-memory simulated host secret service; no Credential Manager access",
            },
            phases,
            manualGaps = new[]
            {
                "Real YTMDesktop2 pairing approval and API-version compatibility require the companion application.",
                "Physical controller input, OverlayHost shell composition, and visible transition fidelity require a packaged manual playtest.",
                "Production Credential Manager secret enumeration/purge is not claimed by this auth-free workflow.",
            },
        }, CreateEvidenceJsonOptions()));
    }
}

static string[] ReadPackagePaths(string packagePath)
{
    using var archive = ZipFile.OpenRead(packagePath);
    return archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
}

static async Task<(int Code, string Output, string Error)> RunCliAsync(params string[] arguments)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await CliApplication.RunAsync(arguments, output, error);
    return (code, output.ToString(), error.ToString());
}

static string FindRepositoryRootForTest()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "src", "OverlayHost", "widget-catalog.json")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static async Task ExportEvidenceAsync(string outputDirectory)
{
    Directory.CreateDirectory(outputDirectory);
    var snapshotDirectory = Path.Combine(outputDirectory, "snapshots");
    var traceDirectory = Path.Combine(outputDirectory, "traces");
    var packageDirectory = Path.Combine(outputDirectory, "packages");
    Directory.CreateDirectory(snapshotDirectory);
    Directory.CreateDirectory(traceDirectory);
    Directory.CreateDirectory(packageDirectory);

    using var deployment = await Deployment.CreateAsync(
        installAsCommunity: true,
        includeEvidencePackages: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    Assert.True(installed.InstalledCatalogValid,
        "The installed catalog was rejected while exporting evidence.");

    var exported = new List<EvidenceSnapshotDescriptor>();
    var traces = new List<EvidenceTraceDescriptor>();
    var gaps = new List<EvidenceGapDescriptor>();
    foreach (var package in deployment.Packages.Where(package =>
                 package.Manifest.Id is
                    "org.gbar.firstparty.games-apps" or
                    "org.gbar.firstparty.settings" or
                    "org.gbar.samples.spotify"))
    {
        try
        {
        var configured = installed.Catalog.GetConfigured(package.Manifest.Id);
        var backend = CreateBackend();
        using var consentRoot = new TemporaryDirectory("gba-evidence-consent");
        var consent = new ConsentStore(consentRoot.Path);
        var identity = new BrokerWidgetIdentity(
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId);
        foreach (var capability in configured.DeclaredCapabilities)
            await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);

        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = configured.WorkerExecutable,
            Arguments = configured.WorkerArguments,
            WidgetInstanceId = configured.InstanceId,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = TimeSpan.FromSeconds(8),
            MaximumRestartAttempts = 0,
            MemoryLimitBytes = configured.MemoryLimitMb * 1024L * 1024L,
            IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
            CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
                configured.PackageId,
                configured.PublisherId,
                configured.InstanceId,
                configured.DeclaredCapabilities,
                consent,
                backend,
                context),
        });

        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        var initial = await WaitForSnapshotAsync(client, package.ExpectedText);
        await ExportSnapshotAsync(package, configured, "initial", initial);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);

        if (package.Manifest.Id == "org.gbar.firstparty.games-apps")
        {
            var open = Nodes(initial.Root).Single(node =>
                string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.open-catalog", open.Id));
            var catalog = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App");
            await ExportSnapshotAsync(package, configured, "catalog", catalog);
            var add = Nodes(catalog.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.toggle-curation", add.Id));
            var selected = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true);
            await client.SendActionAsync(new WidgetActionEvent("back", selected.Root.Id));
            var populated = await WaitForActionSnapshotAsync(
                client, "games.launch", "Conformance Library App");
            await ExportSnapshotAsync(package, configured, "populated", populated);
            traces.Add(await WriteTraceAsync("GBA-038-games-apps", new
            {
                issue = "GBA-038",
                authority = "real installed package in AppContainer with simulated broker",
                steps = new[]
                {
                    new { action = "visible", invariant = "root exposes games.open-catalog without catalog enumeration" },
                    new { action = "games.open-catalog", invariant = "catalog exposes Conformance Library App" },
                    new { action = "games.toggle-curation", invariant = "selected state becomes true" },
                    new { action = "back", invariant = "curated root exposes games.launch" },
                },
                observed = new
                {
                    backend.AppLibraryReadCalls,
                    initialNodeCount = Nodes(initial.Root).Count(),
                    catalogNodeCount = Nodes(catalog.Root).Count(),
                    populatedNodeCount = Nodes(populated.Root).Count(),
                },
            }));
        }
        else if (package.Manifest.Id == "org.gbar.samples.spotify")
        {
            var setup = Nodes(initial.Root).Single(node =>
                string.Equals(node.ActionId, "spotify.setup.open", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("spotify.setup.open", setup.Id));
            var setupSnapshot = await WaitForSnapshotAsync(client, "Spotify setup");
            await ExportSnapshotAsync(package, configured, "setup", setupSnapshot);
            traces.Add(await WriteTraceAsync("GBA-042-spotify-auth-free", new
            {
                issue = "GBA-042",
                authority = "real installed community package in AppContainer with simulated broker",
                steps = new[]
                {
                    new { action = "visible", invariant = "unconfigured state exposes spotify.setup.open" },
                    new { action = "spotify.setup.open", invariant = "setup view exposes exact redirect guidance and Check configuration action" },
                },
                observed = new
                {
                    initialNodeCount = Nodes(initial.Root).Count(),
                    setupNodeCount = Nodes(setupSnapshot.Root).Count(),
                    configured = backend.SpotifyConfiguration.IsConfigured,
                },
                limitation = "OAuth browser authorization is intentionally not exercised by auth-free evidence.",
            }));
        }
        else
        {
            traces.Add(await WriteTraceAsync("GBA-039-settings-root", new
            {
                issue = "GBA-039",
                authority = "real installed package in AppContainer",
                steps = new[]
                {
                    new { action = "visible", invariant = "settings root renders through the generic worker" },
                },
                observed = new { nodeCount = Nodes(initial.Root).Count() },
                limitation = "Installed-widget permission detail requires host-owned settings/catalog path injection and is not claimed by this slice.",
            }));
        }

        await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
        await client.StopAsync();
        }
        catch (Exception exception)
        {
            gaps.Add(new EvidenceGapDescriptor(
                package.Manifest.Id,
                exception.GetType().Name,
                SanitizeEvidenceMessage(exception.Message)));
            Console.Error.WriteLine(
                $"EVIDENCE GAP {package.Manifest.Id}: {exception.GetType().Name}: " +
                SanitizeEvidenceMessage(exception.Message));
        }
    }

    var packageEvidence = deployment.Packages
        .Where(package => package.Manifest.Id is
            "org.gbar.firstparty.games-apps" or
            "org.gbar.firstparty.settings" or
            "org.gbar.samples.spotify")
        .Select(package => PreservePackageArchive(package, packageDirectory))
        .OrderBy(package => package.Id, StringComparer.Ordinal)
        .ToArray();
    var index = new
    {
        schemaVersion = 1,
        generatedUtc = DateTimeOffset.UtcNow,
        evidenceAuthority = "standalone widget-body harness: retained installed .gbarwidget archive -> generic AppContainer worker -> simulated broker companion -> production bridge style resolver",
        packages = packageEvidence,
        snapshots = exported,
        traces,
        gaps,
        limitations = new[]
        {
            "The backend is deterministic and simulated; no external accounts, radio hardware, or process launch is used.",
            "PNG rendering is a separate standalone widget-body stage built from production renderer sources; this index contains authoritative semantic snapshots and computed GBSS styles.",
            "This harness does not exercise the OverlayHost window, shell/tray/footer composition, z-order, focus ownership, input routing, or shell/widget transition fidelity.",
            "Settings permission-detail and Spotify OAuth completion are not claimed by this first slice.",
        },
    };
    await File.WriteAllTextAsync(
        Path.Combine(outputDirectory, "evidence-index.json"),
        JsonSerializer.Serialize(index, CreateEvidenceJsonOptions()));
    Console.WriteLine($"Exported {exported.Count} authoritative snapshots and {traces.Count} traces to {outputDirectory}.");

    async Task ExportSnapshotAsync(
        PackageFixture package,
        ConfiguredWidget configured,
        string state,
        ViewSnapshot snapshot)
    {
        var validation = ViewSnapshotValidator.Validate(snapshot);
        Assert.Equal(0, validation.Count);
        var renderStyles = BridgeRenderStyleResolver.Resolve(snapshot, configured.CompiledTheme);
        using var snapshotDocument = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        var fileName = $"{package.Manifest.Id}.{state}.json";
        var path = Path.Combine(snapshotDirectory, fileName);
        var payload = new
        {
            snapshot = snapshotDocument.RootElement.Clone(),
            renderStyles,
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, CreateEvidenceJsonOptions()));
        var allNodes = Nodes(snapshot.Root).ToArray();
        exported.Add(new EvidenceSnapshotDescriptor(
            package.Manifest.Id,
            state,
            $"snapshots/{fileName}",
            snapshot.Sequence,
            allNodes.Length,
            allNodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() == allNodes.Length,
            renderStyles.Count == allNodes.Length,
            snapshot.InitialFocusId));
    }

    async Task<EvidenceTraceDescriptor> WriteTraceAsync(string name, object payload)
    {
        var fileName = $"{name}.json";
        await File.WriteAllTextAsync(
            Path.Combine(traceDirectory, fileName),
            JsonSerializer.Serialize(payload, CreateEvidenceJsonOptions()));
        return new EvidenceTraceDescriptor(name, $"traces/{fileName}");
    }
}

static EvidencePackageDescriptor PreservePackageArchive(
    PackageFixture package,
    string packageDirectory)
{
    var fileName = $"{package.Manifest.Id}-{package.Manifest.Version}.gbarwidget";
    var destination = Path.Combine(packageDirectory, fileName);
    File.Copy(package.PackagePath, destination, overwrite: false);
    return new EvidencePackageDescriptor(
        package.Manifest.Id,
        package.Manifest.Version,
        package.Manifest.Publisher,
        $"packages/{fileName}",
        Convert.ToHexString(SHA256.HashData(
            File.ReadAllBytes(destination))).ToLowerInvariant());
}

static string SanitizeEvidenceMessage(string message)
{
    if (string.IsNullOrWhiteSpace(message)) return "The evidence stage failed without a diagnostic.";

    var sanitized = System.Text.RegularExpressions.Regex.Replace(
        message,
        @"(?i)(?:(?<![a-z0-9+.-])[a-z]:[\\/]|\\\\|/home/|/users/)[^\r\n\""']+",
        "[local-path-redacted]");
    sanitized = System.Text.RegularExpressions.Regex.Replace(
        sanitized,
        @"(?i)([?&](?:code|state|access_token|refresh_token|client_secret)=)[^&\s]+",
        "$1[secret-redacted]");
    sanitized = System.Text.RegularExpressions.Regex.Replace(
        sanitized,
        @"(?i)bearer\s+[a-z0-9._~+\-/]+=*",
        "Bearer [secret-redacted]");
    sanitized = sanitized.Replace(Environment.UserName, "[user-redacted]",
        StringComparison.OrdinalIgnoreCase).Trim();
    if (sanitized.Length > 500) sanitized = sanitized[..500];
    return sanitized;
}

static JsonSerializerOptions CreateEvidenceJsonOptions()
{
    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    return options;
}

static async Task RunCatalogAsync(
    BridgeCatalog catalog,
    IReadOnlyList<PackageFixture> packages,
    Func<PackageFixture, string> widgetId,
    string route)
{
    foreach (var package in packages)
    {
        try
        {
            var configured = catalog.GetConfigured(widgetId(package));
            var backend = CreateBackend();
            using var consentRoot = new TemporaryDirectory("gba-firstparty-consent");
            var consent = new ConsentStore(consentRoot.Path);
            var identity = new BrokerWidgetIdentity(
                configured.PackageId,
                configured.PublisherId,
                configured.InstanceId);
            foreach (var capability in configured.DeclaredCapabilities)
                await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);

            await using var client = new WidgetProcessClient(new WidgetProcessOptions
            {
                ExecutablePath = configured.WorkerExecutable,
                Arguments = configured.WorkerArguments,
                WidgetInstanceId = configured.InstanceId,
                ConnectTimeout = TimeSpan.FromSeconds(10),
                RequestTimeout = TimeSpan.FromSeconds(4),
                MaximumRestartAttempts = 0,
                MemoryLimitBytes = configured.MemoryLimitMb * 1024L * 1024L,
                IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
                IsolationKey = configured.IsolationKey,
                ReadOnlyPaths = configured.ReadOnlyPaths,
                CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
                    configured.PackageId,
                    configured.PublisherId,
                    configured.InstanceId,
                    configured.DeclaredCapabilities,
                    consent,
                    backend,
                    context),
            });

            await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var snapshot = await WaitForSnapshotAsync(client, package.ExpectedText);
            Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
            Assert.True(client.IsRunning,
                $"{package.Manifest.Name} {route} worker exited after render.");

            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            await ExerciseControlAsync(package, client, snapshot, backend, route);
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
            await client.StopAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{package.Manifest.Name} failed through the {route} generic-worker route.",
                exception);
        }
    }
}

static async Task ExerciseControlAsync(
    PackageFixture package,
    WidgetProcessClient client,
    ViewSnapshot snapshot,
    SimulatedPlatformBrokerBackend backend,
    string route)
{
    string actionId;
    Func<int> calls;
    switch (package.Manifest.Id)
    {
        case "org.gbar.firstparty.audio-mixer":
            actionId = "output.mute.toggle";
            calls = () => backend.AudioControlCalls;
            break;
        case "org.gbar.firstparty.network-controls":
            actionId = "wifi.radio.toggle";
            calls = () => backend.WifiRadioControlCalls;
            break;
        case "org.gbar.firstparty.games-apps":
            var openCatalog = Nodes(snapshot.Root).Single(node =>
                string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent(
                "games.open-catalog", openCatalog.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App");
            var add = Nodes(snapshot.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent(
                "games.toggle-curation", add.Id));
            await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true);
            await client.SendActionAsync(new WidgetActionEvent("back", "games.catalog"));
            snapshot = await WaitForActionSnapshotAsync(
                client, "games.launch", "Conformance Library App");
            actionId = "games.launch";
            calls = () => backend.AppLibraryLaunchCalls;
            break;
        case "org.gbar.firstparty.media-sessions":
            actionId = "media.toggle";
            calls = () => backend.MediaControlCalls;
            break;
        default:
            throw new InvalidOperationException(
                $"No conformance control is defined for {package.Manifest.Id}.");
    }

    var source = Nodes(snapshot.Root).FirstOrDefault(node =>
        string.Equals(node.ActionId, actionId, StringComparison.Ordinal));
    Assert.True(source is not null,
        $"{package.Manifest.Name} {route} snapshot omitted action '{actionId}'.");
    var before = calls();
    await client.SendActionAsync(new WidgetActionEvent(actionId, source!.Id));
    await WaitUntilAsync(() => calls() > before);
}

static SimulatedPlatformBrokerBackend CreateBackend()
{
    var backend = new SimulatedPlatformBrokerBackend
    {
        AudioOutput = new AudioOutputSummary(0.72, false),
        AudioInput = new AudioInputSummary(0.45, false),
        NetworkStatus = new NetworkStatusSummary(
            NetworkConnectivity.Internet,
            NetworkTransportKind.Wifi,
            NetworkWirelessAvailability.Available,
            NetworkDetailsAccess.Available,
            NetworkConnectionAttemptState.None,
            null,
            "wifi-current",
            "Conformance Wi-Fi",
            87),
        WifiRadio = new WifiRadioSummary(WifiRadioState.On, true),
        BluetoothRadioState = BluetoothRadioState.On,
    };
    backend.SetAudioSessions(
        [new AudioSessionSummary("audio-one", "Conformance Game", 0.63, false, true)]);
    backend.SetAudioDevices(
    [
        new AudioDeviceSummary("output-one", "Conformance Speakers", AudioDeviceDirection.Output, true),
        new AudioDeviceSummary("input-one", "Conformance Microphone", AudioDeviceDirection.Input, true),
    ]);
    backend.SetAppLibrary(
    [
        new AppLibraryItemSummary(
            "app-conformance", "Conformance Library App", AppLibraryKind.Application),
    ]);
    backend.SetAvailableWifiNetworks(
    [
        new AvailableWifiNetworkSummary(
            "wifi-current", "Conformance Wi-Fi", 87, WifiSecurityKind.Personal,
            false, true, true),
    ]);
    backend.SetBluetoothDevices(
        [new BluetoothDeviceSummary("bt-one", "Conformance Controller", true, true, true)]);
    backend.SetRecentActivities(
        [new RecentActivitySummary("activity-one", "Conformance Editor", RecentActivityKind.Application, true, true)]);
    backend.SetMediaSessions(
    [
        new MediaSessionSummary(
            "media-one", "Conformance Player", "Conformance Song", "Conformance Artist",
            MediaPlaybackStatus.Playing, 30_000, 180_000,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    return backend;
}

static async Task<ViewSnapshot> WaitForSnapshotAsync(
    WidgetProcessClient client,
    string expectedText)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    ViewSnapshot? latest = null;
    while (DateTime.UtcNow < deadline)
    {
        latest = await client.GetSnapshotAsync();
        Assert.Equal(0, ViewSnapshotValidator.Validate(latest).Count);
        if (Nodes(latest.Root).Any(node =>
                (node.Text ?? string.Empty).Contains(expectedText, StringComparison.Ordinal)))
            return latest;
        await Task.Delay(40);
    }
    var renderedText = latest is null
        ? "none"
        : string.Join(" | ", Nodes(latest.Root)
            .Select(node => node.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text)));
    throw new InvalidOperationException(
        $"Expected rendered broker data '{expectedText}', latest root was " +
        $"'{latest?.Root.Id ?? "none"}' with text: {renderedText}");
}

static async Task<ViewSnapshot> WaitForActionSnapshotAsync(
    WidgetProcessClient client,
    string actionId,
    string expectedText,
    bool? selected = null)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    while (DateTime.UtcNow < deadline)
    {
        var snapshot = await client.GetSnapshotAsync();
        Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
        if (Nodes(snapshot.Root).Any(node =>
                string.Equals(node.ActionId, actionId, StringComparison.Ordinal) &&
                Nodes(node).Any(descendant =>
                    (descendant.Text ?? string.Empty).Contains(
                        expectedText, StringComparison.Ordinal)) &&
                (selected is null || node.IsSelected == selected)))
            return snapshot;
        await Task.Delay(40);
    }
    throw new InvalidOperationException(
        $"Snapshot omitted action '{actionId}' for '{expectedText}'.");
}

static async Task WaitUntilAsync(Func<bool> predicate)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
    while (DateTime.UtcNow < deadline)
    {
        if (predicate()) return;
        await Task.Delay(25);
    }
    throw new TimeoutException("A brokered widget action was not observed.");
}

static IEnumerable<ViewNode> Nodes(ViewNode node)
{
    yield return node;
    foreach (var child in node.Children)
        foreach (var nested in Nodes(child))
            yield return nested;
}

static void AssertWorkerArguments(
    ConfiguredWidget configured,
    string packageRoot,
    WidgetManifest manifest)
{
    Assert.SequenceEqual(
    [
        "--package-root", Path.GetFullPath(packageRoot),
        "--widget-assembly", Path.GetFullPath(
            manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar),
            packageRoot),
        "--widget-type", manifest.Entrypoint.Type,
    ], configured.WorkerArguments);
}

static void AssertResidency(WidgetManifest manifest, WidgetResidencyPolicy actual)
{
    var expected = manifest.ResidencyPolicy ??
        (manifest.BackgroundPolicy == "suspend"
            ? new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden }
            : new WidgetResidencyPolicy());
    Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
    Assert.Equal(expected.Mode, actual.Mode);
    Assert.Equal(expected.IdleSeconds, actual.IdleSeconds);
}

file sealed class Deployment : IDisposable
{
    private readonly TemporaryDirectory _root;
    private Deployment(TemporaryDirectory root) => _root = root;

    public required string WorkerHostPath { get; init; }
    public string RootPath => _root.Path;
    public required string BundledWorkerHostPath { get; init; }
    public required string EmptyTrustedCatalogPath { get; init; }
    public required string BundledCatalogPath { get; init; }
    public required string InstalledCatalogRoot { get; init; }
    public required IReadOnlyList<PackageFixture> Packages { get; init; }
    public PackageFixture? YtMusicPackage { get; init; }

    public static async Task<Deployment> CreateAsync(
        bool installAsCommunity,
        bool includeEvidencePackages = false,
        bool ytMusicOnly = false)
    {
        var temporary = new TemporaryDirectory("gba-firstparty-conformance");
        try
        {
            var repo = FindRepositoryRoot();
            var workerSource = Path.Combine(AppContext.BaseDirectory, "WidgetWorkerHost.exe");
            Assert.True(File.Exists(workerSource), "WidgetWorkerHost.exe was not copied to test output.");
            var runtime = Path.Combine(temporary.Path, "runtime");
            var workerDirectory = Path.Combine(runtime, "WidgetWorkerHost");
            Directory.CreateDirectory(workerDirectory);
            CopyWorkerHostDeployment(AppContext.BaseDirectory, workerDirectory);
            var workerHost = Path.Combine(workerDirectory, "WidgetWorkerHost.exe");

            var packageSpecs = new List<PackageSpec>
            {
                new PackageSpec("media-sessions", "src/FirstPartyWidgets/MediaSessionsWidget", "MediaSessions", WidgetGlyph.Music,
                    typeof(MediaSessionsWidget), "Conformance Song"),
                new PackageSpec("games-apps", "src/FirstPartyWidgets/GamesAppsWidget", "GamesApps", WidgetGlyph.Play,
                    typeof(GamesAppsWidget), "Build your library"),
                new PackageSpec("audio-mixer", "src/FirstPartyWidgets/AudioMixerWidget", "AudioMixer", WidgetGlyph.Volume,
                    typeof(AudioMixerWidget), "Conformance Game"),
                new PackageSpec("network-controls", "src/FirstPartyWidgets/NetworkControlsWidget", "NetworkControls", WidgetGlyph.Wifi,
                    typeof(NetworkControlsWidget), "Conformance Wi-Fi"),
            };
            if (includeEvidencePackages)
            {
                packageSpecs.Add(new PackageSpec(
                    "settings", "src/FirstPartyWidgets/SettingsWidget", "Settings",
                    WidgetGlyph.Settings, typeof(SettingsWidget), "Overlay"));
                packageSpecs.Add(new PackageSpec(
                    "spotify", "samples/SpotifyWidget", "Spotify",
                    WidgetGlyph.Music, typeof(SpotifyWidget), "Client ID required"));
            }
            var fixtures = new List<PackageFixture>();
            foreach (var spec in packageSpecs)
            {
                var projectRoot = Path.GetFullPath(
                    spec.ProjectRelativeRoot.Replace('/', Path.DirectorySeparatorChar), repo);
                var manifest = ManifestJson.Deserialize(
                    await File.ReadAllBytesAsync(Path.Combine(projectRoot, "manifest.json")));
                Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
                var bundleRoot = Path.Combine(runtime, spec.Directory);
                Directory.CreateDirectory(Path.Combine(bundleRoot, "payload"));
                Directory.CreateDirectory(Path.Combine(bundleRoot, "styles"));
                File.Copy(Path.Combine(projectRoot, "manifest.json"),
                    Path.Combine(bundleRoot, "manifest.json"));
                File.Copy(Path.Combine(projectRoot, "styles", "default.gbss"),
                    Path.Combine(bundleRoot, "styles", "default.gbss"));
                File.Copy(spec.WidgetType.Assembly.Location,
                    Path.Combine(bundleRoot, manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar)));
                var packagePath = Path.Combine(temporary.Path, $"{manifest.Id}.gbarwidget");
                ZipFile.CreateFromDirectory(bundleRoot, packagePath, CompressionLevel.NoCompression, false);
                fixtures.Add(new PackageFixture(
                    spec.ShellId,
                    spec.Icon,
                    spec.ExpectedText,
                    manifest,
                    bundleRoot,
                    packagePath,
                    manifest.Permissions.Concat(manifest.OptionalPermissions)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
            }

            var emptyCatalog = Path.Combine(temporary.Path, "empty-catalog.json");
            await File.WriteAllTextAsync(emptyCatalog,
                "{\"catalogVersion\":1,\"widgets\":[],\"bundledWidgets\":[]}");
            var bundledCatalog = Path.Combine(temporary.Path, "bundled-catalog.json");
            await File.WriteAllTextAsync(bundledCatalog, JsonSerializer.Serialize(new
            {
                catalogVersion = 1,
                genericWorkerExecutable = "runtime/WidgetWorkerHost/WidgetWorkerHost.exe",
                widgets = Array.Empty<object>(),
                bundledWidgets = fixtures.Select(fixture => new
                {
                    id = fixture.ShellId,
                    packageId = fixture.Manifest.Id,
                    instanceId = $"{fixture.ShellId}.default",
                    packageRoot = $"runtime/{packageSpecs.Single(spec => spec.ShellId == fixture.ShellId).Directory}",
                    icon = JsonNamingPolicy.CamelCase.ConvertName(fixture.Icon.ToString()),
                    quickActions = Array.Empty<object>(),
                }),
            }));

            var installedRoot = Path.Combine(temporary.Path, "installed");
            PackageFixture? ytMusicPackage = null;
            if (installAsCommunity)
            {
                var catalog = new WidgetCatalog(installedRoot);
                if (!ytMusicOnly)
                {
                    foreach (var fixture in fixtures)
                        await catalog.InstallAsync(fixture.PackagePath);
                    foreach (var fixture in fixtures)
                        await catalog.SetEnabledAsync(fixture.Manifest.Id, true);
                }

                var ytProjectRoot = Path.Combine(repo, "samples", "YtMusicWidget");
                var ytManifest = ManifestJson.Deserialize(
                    await File.ReadAllBytesAsync(Path.Combine(ytProjectRoot, "manifest.json")));
                var ytOutput = Path.Combine(temporary.Path, "ytmusic-community-package");
                await RunCommunityPackageScriptAsync(
                    Path.Combine(ytProjectRoot, "Build-CommunityPackage.ps1"),
                    ytOutput,
                    installedRoot,
                    version: null,
                    install: true);
                var installedYt = (await catalog.DiscoverAsync()).Widgets
                    .Single(widget => widget.Id == ytManifest.Id)
                    .ActiveVersion;
                ytMusicPackage = new PackageFixture(
                    ytManifest.Id,
                    ytManifest.Presentation.Icon,
                    "Conformance Song",
                    ytManifest,
                    installedYt.InstallPath,
                    Path.Combine(ytOutput, $"{ytManifest.Id}-{ytManifest.Version}.gbarwidget"),
                    ytManifest.Permissions.Concat(ytManifest.OptionalPermissions)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }
            return new Deployment(temporary)
            {
                WorkerHostPath = workerSource,
                BundledWorkerHostPath = workerHost,
                EmptyTrustedCatalogPath = emptyCatalog,
                BundledCatalogPath = bundledCatalog,
                InstalledCatalogRoot = installedRoot,
                Packages = fixtures,
                YtMusicPackage = ytMusicPackage,
            };
        }
        catch
        {
            temporary.Dispose();
            throw;
        }
    }

    public void Dispose() => _root.Dispose();

    public async Task<string> BuildYtMusicVersionAsync(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var repo = FindRepositoryRoot();
        var projectRoot = Path.Combine(repo, "samples", "YtMusicWidget");
        var output = Path.Combine(_root.Path, $"ytmusic-community-{version}");
        await RunCommunityPackageScriptAsync(
            Path.Combine(projectRoot, "Build-CommunityPackage.ps1"),
            output,
            catalogRoot: null,
            version,
            install: false);
        var manifest = ManifestJson.Deserialize(
            await File.ReadAllBytesAsync(Path.Combine(output, "package-root", "manifest.json")));
        return Path.Combine(output, $"{manifest.Id}-{manifest.Version}.gbarwidget");
    }

    private static void CopyWorkerHostDeployment(
        string sourceDirectory,
        string destinationDirectory)
    {
        const int maximumAssets = 128;
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WidgetWorkerHost.exe",
            "WidgetWorkerHost.deps.json",
            "WidgetWorkerHost.runtimeconfig.json",
        };
        var dependencies = Path.Combine(sourceDirectory, "WidgetWorkerHost.deps.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(dependencies));
        var root = document.RootElement;
        if (!root.TryGetProperty("runtimeTarget", out var runtimeTarget) ||
            !runtimeTarget.TryGetProperty("name", out var runtimeNameElement) ||
            runtimeNameElement.GetString() is not { Length: > 0 } runtimeName ||
            !root.TryGetProperty("targets", out var targets) ||
            !targets.TryGetProperty(runtimeName, out var target))
            throw new InvalidOperationException(
                "WidgetWorkerHost.deps.json did not expose its selected runtime target.");

        foreach (var library in target.EnumerateObject())
        {
            AddAssetGroup(library.Value, "runtime", assets);
            AddAssetGroup(library.Value, "native", assets);
            AddAssetGroup(library.Value, "resources", assets);
            AddAssetGroup(library.Value, "runtimeTargets", assets);
            if (assets.Count > maximumAssets)
                throw new InvalidOperationException(
                    "WidgetWorkerHost dependency closure exceeded its conformance bound.");
        }

        var sourceRoot = Path.GetFullPath(sourceDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinationRoot = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var asset in assets.Order(StringComparer.OrdinalIgnoreCase))
        {
            var relative = asset.Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathFullyQualified(relative))
                throw new InvalidOperationException(
                    "WidgetWorkerHost dependency closure contained an absolute path.");
            var source = Path.GetFullPath(relative, sourceRoot);
            var destination = Path.GetFullPath(relative, destinationRoot);
            if (!source.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                !destination.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "WidgetWorkerHost dependency closure escaped its deployment root.");
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"WidgetWorkerHost dependency '{asset}' was absent from the build output.",
                    source);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static async Task RunCommunityPackageScriptAsync(
        string script,
        string outputDirectory,
        string? catalogRoot,
        string? version,
        bool install)
    {
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var arguments = new List<string>
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", script,
            "-Configuration", "Release",
            "-OutputDirectory", outputDirectory,
        };
        if (catalogRoot is not null)
        {
            arguments.Add("-Catalog");
            arguments.Add(catalogRoot);
        }
        if (version is not null)
        {
            arguments.Add("-Version");
            arguments.Add(version);
        }
        if (install) arguments.Add("-Install");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = Process.Start(start) ??
            throw new InvalidOperationException("YT Music package script did not start.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw new TimeoutException("YT Music public package workflow exceeded three minutes.");
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var diagnostic = (output + Environment.NewLine + error).Trim();
            if (diagnostic.Length > 2_000) diagnostic = diagnostic[..2_000];
            throw new InvalidOperationException(
                $"YT Music public package workflow failed ({process.ExitCode}): {diagnostic}");
        }
    }

    private static void AddAssetGroup(
        JsonElement library,
        string property,
        ISet<string> assets)
    {
        if (!library.TryGetProperty(property, out var group)) return;
        if (group.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                $"WidgetWorkerHost dependency group '{property}' was malformed.");
        foreach (var asset in group.EnumerateObject())
            assets.Add(asset.Name);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "OverlayHost", "widget-catalog.json")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

file sealed record PackageSpec(
    string ShellId,
    string ProjectRelativeRoot,
    string Directory,
    WidgetGlyph Icon,
    Type WidgetType,
    string ExpectedText);

file sealed record PackageFixture(
    string ShellId,
    WidgetGlyph Icon,
    string ExpectedText,
    WidgetManifest Manifest,
    string BundleRoot,
    string PackagePath,
    IReadOnlyList<string> DeclaredCapabilities);

file sealed record EvidenceSnapshotDescriptor(
    string PackageId,
    string State,
    string SnapshotPath,
    long Sequence,
    int NodeCount,
    bool NodeIdsUnique,
    bool EveryNodeHasComputedStyles,
    string? InitialFocusId);

file sealed record EvidencePackageDescriptor(
    string Id,
    string Version,
    string Publisher,
    string Archive,
    string ArchiveSha256);

file sealed record EvidenceTraceDescriptor(
    string Name,
    string TracePath);

file sealed record EvidenceGapDescriptor(
    string PackageId,
    string ErrorType,
    string Message);

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string prefix)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

file static class Assert
{
    public static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException(
                $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
