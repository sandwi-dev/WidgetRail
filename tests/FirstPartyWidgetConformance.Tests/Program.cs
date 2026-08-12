using System.IO.Compression;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.FirstPartyWidgets.GamesApps;
using GameBarAlternative.FirstPartyWidgets.GameLauncher;
using GameBarAlternative.FirstPartyWidgets.MediaSessions;
using GameBarAlternative.FirstPartyWidgets.NetworkControls;
using GameBarAlternative.FirstPartyWidgets.Settings;
using GameBarAlternative.Tests.FullApplicationWidgetFixture;
using GameBarAlternative.Samples.SpotifyWidget;
using GameBarAlternative.Samples.YtMusicWidget;
using GameBarAlternative.GbarCli;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;
using GameBarAlternative.WindowsAppLibraryProvider;
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

if (args.Contains("--community-recovery-acceptance", StringComparer.Ordinal))
{
    var outputIndex = Array.IndexOf(args, "--acceptance-output");
    var output = outputIndex < 0
        ? null
        : outputIndex + 1 < args.Length && !string.IsNullOrWhiteSpace(args[outputIndex + 1])
            ? Path.GetFullPath(args[outputIndex + 1])
            : throw new ArgumentException("--acceptance-output requires a file path.");
    await CommunityRecoveryPackagesRunIsolated(output);
    Console.WriteLine("PASS current Spotify/YT Music Community recovery acceptance");
    return 0;
}

if (args.Contains("--spotify-installed-acceptance", StringComparer.Ordinal))
{
    static string RequiredArgument(string[] values, string name)
    {
        var index = Array.IndexOf(values, name);
        return index >= 0 && index + 1 < values.Length &&
               !string.IsNullOrWhiteSpace(values[index + 1])
            ? Path.GetFullPath(values[index + 1])
            : throw new ArgumentException($"{name} requires a path.");
    }
    await SpotifyInstalledPackageRunsIsolated(
        RequiredArgument(args, "--catalog"),
        RequiredArgument(args, "--package"),
        RequiredArgument(args, "--acceptance-output"));
    Console.WriteLine("PASS exact installed Spotify Community package acceptance");
    return 0;
}

if (args.Contains("--steam-artwork-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    await InstalledSteamArtworkRunsIsolated(installed.Catalog);
    Console.WriteLine("PASS exact installed Steam artwork acceptance");
    return 0;
}

if (args.Contains("--gog-installed-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    await InstalledGogRunsIsolated(installed.Catalog);
    Console.WriteLine("PASS opt-in GOG installed-game generic-worker acceptance");
    return 0;
}

if (args.Contains("--running-app-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    await InstalledRunningAppRunsIsolated(installed.Catalog);
    Console.WriteLine("PASS exact installed running-app acceptance");
    return 0;
}

if (args.Contains("--text-entry-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    var packages = deployment.Packages
        .Where(package => package.Manifest.Id is
            "org.gbar.firstparty.game-launcher" or
            "org.gbar.firstparty.network-controls")
        .ToArray();
    Assert.Equal(2, packages.Length);
    await RunCatalogAsync(
        installed.Catalog,
        packages,
        package => package.Manifest.Id,
        "installed-text-entry",
        textEntryOnly: true);
    Console.WriteLine("PASS exact installed TextEntry bridge acceptance");
    return 0;
}

if (args.Contains("--full-application-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(
        installAsCommunity: true,
        fullApplicationOnly: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    await FullApplicationPackageRunsIsolated(
        installed.Catalog,
        deployment.Packages.Single());
    Console.WriteLine("PASS installed full-application process-tree acceptance");
    return 0;
}

if (args.Contains("--media-sessions-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(
        installAsCommunity: true,
        mediaSessionsOnly: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    await MediaSessionsPackageRunsIsolated(
        installed.Catalog,
        deployment.Packages.Single());
    Console.WriteLine("PASS installed Media Sessions lifecycle and retry acceptance");
    return 0;
}

if (args.Contains("--games-apps-installed-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    var package = deployment.Packages.Single(candidate =>
        candidate.Manifest.Id == "org.gbar.firstparty.games-apps");
    await RunCatalogAsync(
        installed.Catalog,
        [package],
        candidate => candidate.Manifest.Id,
        "installed-normalized-app-library");
    Console.WriteLine("PASS installed Games & Apps normalized app-library acceptance");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bundled catalog derives runtime policy from real manifests", BundledCatalogUsesManifests),
    ("Bundled catalog rejects unsafe and ambiguous package sources", BundledCatalogRejectsUnsafeSources),
    ("Real first-party packages merge through the community catalog path", InstalledPackagesMerge),
    ("First-party and advanced sample packages run through generic AppContainer worker and broker", PackagesRunIsolated),
    ("Exact maximum directory package packs installs and runs isolated", MaximumDirectoryPackageRunsIsolated),
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
        new[] { "media-sessions", "games-apps", "game-launcher", "audio-mixer", "network-controls", "spotify" },
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
        Assert.Equal(package.Manifest.ResourceRequest.MemoryMb, configured.MemoryRequestMb);
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
    Assert.Equal(7, load.Catalog.Widgets.Count);
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
        Assert.Equal(0, configured.ReadOnlyPaths.Count);
        Assert.True(configured.ContentLeaseFactory is not null,
            $"Installed {package.Manifest.Name} omitted exact launch authority.");
        using var contentLease = configured.ContentLeaseFactory!(CancellationToken.None);
        AssertWorkerArguments(
            configured,
            contentLease.Targets.Single(target => target.Target.Kind ==
                AppContainerAuthorityTargetKind.AuthorityRootDirectory).Target.Path,
            package.Manifest);
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

static async Task FullApplicationPackageRunsIsolated(
    BridgeCatalog catalog,
    PackageFixture package)
{
    var configured = catalog.GetConfigured(package.Manifest.Id);
    Assert.True(configured.RequiresAppContainer,
        "Full-application fixture did not use the mandatory AppContainer route.");
    Assert.Equal(1_024, configured.MemoryRequestMb);
    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = configured.WorkerExecutable,
        Arguments = configured.WorkerArguments,
        WidgetInstanceId = configured.InstanceId,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromSeconds(4),
        MaximumRestartAttempts = 0,
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = configured.IsolationKey,
        ReadOnlyPaths = configured.ReadOnlyPaths,
        ContentLeaseFactory = configured.ContentLeaseFactory,
    });

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var snapshot = await WaitForSnapshotAsync(client, "helper-ready:");
    Assert.Equal("private-bytes:131072", Nodes(snapshot.Root).Single(node =>
        node.Id == "full-app.private").Text);
    var helperText = Nodes(snapshot.Root).Single(node =>
        node.Id == "full-app.helper").Text ?? string.Empty;
    var helperPid = int.Parse(helperText["helper-ready:".Length..], CultureInfo.InvariantCulture);
    var workerPid = client.WorkerProcessId ??
        throw new InvalidOperationException("Full-application worker PID is unavailable.");
    using var worker = Process.GetProcessById(workerPid);
    using var helper = Process.GetProcessById(helperPid);
    Assert.True(!worker.HasExited && !helper.HasExited,
        "Full-application process tree was not live after first render.");
    Assert.True(client.AppliedJobAccounting?.ActiveProcesses >= 2,
        "Worker Job accounting omitted the owned helper process.");
    Assert.Equal(null, client.AppliedJobMemoryLimitBytes);
    Assert.Equal(null, client.AppliedJobActiveProcessLimit);

    await client.StopAsync();
    await Task.WhenAll(
        worker.WaitForExitAsync(),
        helper.WaitForExitAsync()).WaitAsync(TimeSpan.FromSeconds(4));
    Assert.True(worker.HasExited && helper.HasExited,
        "Kill-on-close left a full-application process-tree member alive.");
}

static async Task InstalledSteamArtworkRunsIsolated(BridgeCatalog catalog)
{
    using var steam = new InstalledSteamArtworkFixture();
    await using var provider = new WindowsAppLibraryProvider(
        [new SteamGameLibrarySource(
            new WindowsSteamApplicationSource([steam.Root]),
            new InstalledArtworkSteamLauncher())],
        ShellStaExecutor.Shared);
    var simulator = CreateBackend();
    await using var backend = new CompositePlatformBrokerBackend(
        simulator, simulator,
        activity: simulator,
        bluetooth: simulator,
        media: simulator,
        appLibrary: provider,
        privateSecrets: simulator,
        loopbackHttp: simulator,
        privateState: simulator,
        spotify: simulator);
    var configured = catalog.GetConfigured("org.gbar.firstparty.game-launcher");
    Assert.True(configured.RequiresAppContainer,
        "Installed Steam artwork did not use the generic AppContainer route.");
    using var consentRoot = new TemporaryDirectory("gba-installed-steam-artwork-consent");
    var consent = new ConsentStore(consentRoot.Path);
    var identity = new BrokerWidgetIdentity(
        configured.PackageId, configured.PublisherId, configured.InstanceId);
    foreach (var capability in configured.DeclaredCapabilities)
        await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);
    var artwork = new AppLibraryArtworkRegistry();
    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = configured.WorkerExecutable,
        Arguments = configured.WorkerArguments,
        WidgetInstanceId = configured.InstanceId,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromSeconds(4),
        MaximumRestartAttempts = 0,
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = configured.IsolationKey,
        ReadOnlyPaths = configured.ReadOnlyPaths,
        ContentLeaseFactory = configured.ContentLeaseFactory,
        CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.DeclaredCapabilities,
            consent,
            backend,
            context,
            artwork),
    });

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    var snapshot = await WaitForSnapshotAsync(client, "Installed Steam One");
    var firstHandle = ArtworkHandle(snapshot, "Installed Steam One");
    var neighborHandle = ArtworkHandle(snapshot, "Installed Steam Two");
    Assert.True(artwork.IsCurrent(identity, firstHandle),
        "Installed Steam first handle was not current in the host registry.");
    Assert.True(artwork.IsCurrent(identity, neighborHandle),
        "Installed Steam neighbor handle was not current in the host registry.");
    var directPage = await provider.QueryAppLibraryAsync(
        new AppLibraryBackendCursorRequest(
            new AppLibraryBackendQuery(), null, null, 64),
        CancellationToken.None);
    var directFirst = directPage.Items.Single(item =>
        item.DisplayName == "Installed Steam One");
    Assert.True((await provider.GetAppLibraryIconAsync(
        directFirst.ProviderAppId, CancellationToken.None)).PngBase64 is not null,
        "Installed Steam provider lost first artwork before registry resolution.");
    var firstPixels = await artwork.ResolveAsync(
        identity, firstHandle, CancellationToken.None);
    var neighborPixels = await artwork.ResolveAsync(
        identity, neighborHandle, CancellationToken.None);
    Assert.True(firstPixels is not null,
        "Installed Steam first artwork did not resolve current bounded pixels.");
    Assert.True(neighborPixels is not null,
        "Installed Steam neighbor artwork did not resolve current bounded pixels.");

    steam.ReplaceFirstArtwork();
    Assert.Equal<string?>(null, await artwork.ResolveAsync(
        identity, firstHandle, CancellationToken.None));
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.refresh", "game-launcher.refresh"));
    snapshot = await WaitForArtworkRotation(
        client, "Installed Steam One", firstHandle);
    var rotatedHandle = ArtworkHandle(snapshot, "Installed Steam One");
    Assert.True(rotatedHandle != firstHandle,
        "Installed Steam artwork replacement reused the prior handle.");
    Assert.Equal(neighborHandle, ArtworkHandle(snapshot, "Installed Steam Two"));
    Assert.Equal<string?>(null, await artwork.ResolveAsync(
        identity, firstHandle, CancellationToken.None));
    var rotatedPixels = await artwork.ResolveAsync(
        identity, rotatedHandle, CancellationToken.None);
    Assert.True(rotatedPixels is not null && rotatedPixels != firstPixels,
        "Installed Steam replacement reused stale decoded pixels.");

    steam.RemoveFirstArtwork();
    Assert.Equal<string?>(null, await artwork.ResolveAsync(
        identity, rotatedHandle, CancellationToken.None));
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.refresh", "game-launcher.refresh"));
    snapshot = await WaitForArtworkRotation(
        client, "Installed Steam One", rotatedHandle);
    var removedHandle = ArtworkHandle(snapshot, "Installed Steam One");
    Assert.Equal<string?>(null, await artwork.ResolveAsync(
        identity, removedHandle, CancellationToken.None));
    Assert.Equal(neighborHandle, ArtworkHandle(snapshot, "Installed Steam Two"));

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.StopAsync();

    static string ArtworkHandle(ViewSnapshot snapshot, string displayName)
    {
        var tile = Nodes(snapshot.Root).Single(node =>
            node.ActionId == "game-launcher.launch" &&
            Nodes(node).Any(descendant => string.Equals(
                descendant.Text, displayName, StringComparison.Ordinal)));
        return Nodes(tile).Single(node => node.ArtworkHandle is not null).ArtworkHandle!;
    }

    static async Task<ViewSnapshot> WaitForArtworkRotation(
        WidgetProcessClient client,
        string displayName,
        string priorHandle)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var current = await client.GetSnapshotAsync();
            Assert.Equal(0, ViewSnapshotValidator.Validate(current).Count);
            var tile = Nodes(current.Root).FirstOrDefault(node =>
                node.ActionId == "game-launcher.launch" &&
                Nodes(node).Any(descendant => string.Equals(
                    descendant.Text, displayName, StringComparison.Ordinal)));
            var handle = tile is null
                ? null
                : Nodes(tile).SingleOrDefault(node => node.ArtworkHandle is not null)
                    ?.ArtworkHandle;
            if (handle is not null && handle != priorHandle) return current;
            await Task.Delay(40);
        }
        throw new TimeoutException(
            $"Installed Steam artwork for {displayName} did not rotate.");
    }
}

static async Task InstalledGogRunsIsolated(BridgeCatalog catalog)
{
    using var fixture = new InstalledGogFixture();
    var enabled = false;
    var registry = new InstalledGogRegistry(fixture.Records);
    var launcher = new InstalledGogLauncher();
    await using var provider = new WindowsAppLibraryProvider(
        [new GogGameLibrarySource(
            new GogInstalledGameApplicationSource(
                registry, _ => enabled, fixture.ClientPath),
            launcher)],
        ShellStaExecutor.Shared);
    var simulator = CreateBackend();
    await using var backend = new CompositePlatformBrokerBackend(
        simulator, simulator,
        activity: simulator,
        bluetooth: simulator,
        media: simulator,
        appLibrary: provider,
        privateSecrets: simulator,
        loopbackHttp: simulator,
        privateState: simulator,
        spotify: simulator);
    var configured = catalog.GetConfigured("org.gbar.firstparty.game-launcher");
    using var consentRoot = new TemporaryDirectory("gba-installed-gog-consent");
    var consent = new ConsentStore(consentRoot.Path);
    var identity = new BrokerWidgetIdentity(
        configured.PackageId, configured.PublisherId, configured.InstanceId);
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
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = configured.IsolationKey,
        ReadOnlyPaths = configured.ReadOnlyPaths,
        ContentLeaseFactory = configured.ContentLeaseFactory,
        CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.DeclaredCapabilities,
            consent,
            backend,
            context),
    });

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    var disabled = await WaitForSnapshotAsync(client, "No installed games");
    Assert.True(!Nodes(disabled.Root).Any(node =>
        string.Equals(node.Text, fixture.DisplayName, StringComparison.Ordinal)),
        "Disabled GOG source reached the installed worker.");
    Assert.Equal(0, registry.ReadCount);

    enabled = true;
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.refresh", "game-launcher.refresh"));
    var enabledSnapshot = await WaitForSnapshotAsync(client, fixture.DisplayName);
    Assert.True(Nodes(enabledSnapshot.Root).Any(node =>
        string.Equals(node.Text, "GOG", StringComparison.Ordinal)),
        "Enabled GOG source attribution did not reach the installed worker.");
    Assert.True(registry.ReadCount > 0,
        "Enabled GOG source did not read the fixed registration surface.");

    var launch = Nodes(enabledSnapshot.Root).Single(node =>
        node.ActionId == "game-launcher.launch" &&
        Nodes(node).Any(descendant => string.Equals(
            descendant.Text, fixture.DisplayName, StringComparison.Ordinal)));
    registry.Records.Clear();
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.launch", launch.Id));
    await WaitForSnapshotAsync(client, "The selected game is no longer installed");
    Assert.Equal(0, launcher.Count);

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.StopAsync();
}

static async Task InstalledRunningAppRunsIsolated(BridgeCatalog catalog)
{
    var backend = CreateBackend(gameLibraryCount: 128);
    backend.SetRunningAppBackend([
        new("stable-manual-app", "fixture-instance", "A Conformance Manual App",
            AppLibraryKind.Application, "Windows"),
    ]);
    var configured = catalog.GetConfigured("org.gbar.firstparty.game-launcher");
    Assert.True(configured.RequiresAppContainer,
        "Installed running-app route did not use the generic AppContainer worker.");
    using var consentRoot = new TemporaryDirectory("gba-installed-running-app-consent");
    var consent = new ConsentStore(consentRoot.Path);
    var identity = new BrokerWidgetIdentity(
        configured.PackageId, configured.PublisherId, configured.InstanceId);
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
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = configured.IsolationKey,
        ReadOnlyPaths = configured.ReadOnlyPaths,
        ContentLeaseFactory = configured.ContentLeaseFactory,
        CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
            configured.PackageId,
            configured.PublisherId,
            configured.InstanceId,
            configured.DeclaredCapabilities,
            consent,
            backend,
            context),
    });

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    var library = await WaitForSnapshotAsync(client, "Conformance Game 00000");
    var open = Nodes(library.Root).Single(node =>
        node.ActionId == "game-launcher.running.open");
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.running.open", open.Id));
    var running = await WaitForActionSnapshotAsync(
        client, "game-launcher.manual.toggle", "A Conformance Manual App",
        requireEnabled: true);
    var add = Nodes(running.Root).Single(node =>
        node.ActionId == "game-launcher.manual.toggle" &&
        Nodes(node).Any(descendant => descendant.Text == "A Conformance Manual App"));
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.manual.toggle", add.Id));
    _ = await WaitForSnapshotAsync(client, "Already included");
}

static async Task MaximumDirectoryPackageRunsIsolated()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: false);
    var fixture = deployment.Packages[0];
    var source = Path.Combine(deployment.RootPath, "maximum-directory-source");
    CopyDirectory(fixture.BundleRoot, source);
    for (var index = 0; index < 255; index++)
    {
        var directory = Path.Combine(
            source,
            "assets",
            $"edge-{index:000}",
            "one",
            "two",
            "three");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "asset.txt"), "x");
    }

    var packagePath = Path.Combine(deployment.RootPath, "maximum-directory.gbarwidget");
    var packStopwatch = Stopwatch.StartNew();
    var pack = await RunCliAsync("pack", source, "--output", packagePath);
    packStopwatch.Stop();
    Assert.Equal(0, pack.Code);
    Assert.True(File.Exists(packagePath),
        "The exact directory-bound package was not published.");

    var catalogRoot = Path.Combine(deployment.RootPath, "maximum-directory-catalog");
    Assert.Equal(0, (await RunCliAsync(
        "install", packagePath, "--catalog", catalogRoot)).Code);
    Assert.Equal(0, (await RunCliAsync(
        "enable", fixture.Manifest.Id, "--catalog", catalogRoot)).Code);
    var load = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        catalogRoot,
        deployment.WorkerHostPath);
    var configured = load.Catalog.GetConfigured(fixture.Manifest.Id);
    Assert.True(configured.ContentLeaseFactory is not null,
        "The exact directory-bound package omitted content admission.");
    using (var lease = configured.ContentLeaseFactory!(CancellationToken.None))
    {
        Assert.Equal(1_024, lease.Targets.Count(target => target.Target.Kind is
            AppContainerAuthorityTargetKind.AuthorityRootDirectory or
            AppContainerAuthorityTargetKind.VerifiedDirectory));
        Assert.Equal(258, lease.Targets.Count(target => target.Target.Kind ==
            AppContainerAuthorityTargetKind.VerifiedFile));
    }

    TimeSpan activationElapsed = default;
    await RunCatalogAsync(
        load.Catalog,
        [fixture with { BundleRoot = source, PackagePath = packagePath }],
        package => package.Manifest.Id,
        "maximum-directory installed",
        elapsed => activationElapsed = elapsed);
    Assert.True(packStopwatch.Elapsed < TimeSpan.FromSeconds(10),
        $"Exact directory-bound packing exceeded ten seconds ({packStopwatch.Elapsed.TotalMilliseconds:0} ms).");
    Assert.True(activationElapsed > TimeSpan.Zero &&
                activationElapsed < TimeSpan.FromSeconds(10),
        $"Exact directory-bound activation exceeded ten seconds ({activationElapsed.TotalMilliseconds:0} ms).");
    Console.WriteLine(
        $"METRIC exact_directory_package directories=1024 files=258 packMilliseconds={packStopwatch.Elapsed.TotalMilliseconds:F3} activationMilliseconds={activationElapsed.TotalMilliseconds:F3}");
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
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = source.IsolationKey,
        ReadOnlyPaths = source.ReadOnlyPaths,
        ContentLeaseFactory = source.ContentLeaseFactory,
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

static async Task CommunityRecoveryPackagesRunIsolated(string? acceptanceOutput = null)
{
    using var deployment = await Deployment.CreateAsync(
        installAsCommunity: true,
        communityRecoveryOnly: true);
    var packages = new[]
    {
        deployment.SpotifyCommunityPackage ??
            throw new InvalidOperationException("Spotify community package was not produced."),
        deployment.YtMusicPackage ??
            throw new InvalidOperationException("YT Music community package was not produced."),
    };
    Assert.True(
        Version.Parse(packages[0].Manifest.Version) > Version.Parse("0.2.10"),
        "Spotify source package must use a version newer than the stale 0.2.10 payload.");
    Assert.True(
        Version.Parse(packages[1].Manifest.Version) > Version.Parse("0.2.6"),
        "YT Music source package must use a version newer than 0.2.6.");
    var previousVersions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["org.gbar.samples.spotify"] = "0.2.10",
        ["org.gbar.samples.ytmusic"] = "0.2.6",
    };

    var catalog = new WidgetCatalog(deployment.InstalledCatalogRoot);
    var snapshot = await catalog.DiscoverAsync();
    Assert.Equal(2, snapshot.Widgets.Count);
    var loaded = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    Assert.True(loaded.InstalledCatalogValid,
        "The exact current Community packages were rejected by the production catalog route.");
    Assert.Equal(0, loaded.Warnings.Count);

    var evidence = new List<object>();
    foreach (var package in packages)
    {
        var inspection = await new WidgetCatalog(
                Path.Combine(deployment.RootPath, "validation", package.Manifest.Id))
            .CreateInstaller().ValidateAsync(package.PackagePath);
        Assert.Equal(package.Manifest.Id, inspection.Id);
        Assert.Equal(package.Manifest.Version, inspection.Version.ToString());

        var selected = snapshot.Widgets.Single(widget => widget.Id == package.Manifest.Id);
        Assert.True(selected.Enabled, $"{package.Manifest.Name} was not explicitly enabled.");
        Assert.Equal(package.Manifest.Version, selected.ActiveVersion.Version.ToString());
        Assert.Equal(2, selected.Versions.Count);
        var previous = selected.Versions.Single(version =>
            version.Version.ToString() == previousVersions[package.Manifest.Id]);
        var digestCatalog = new WidgetCatalog(Path.Combine(
            deployment.RootPath, "digest-verification", package.Manifest.Id));
        var independentlyInstalled = await digestCatalog.InstallAsync(package.PackagePath);
        Assert.Equal(independentlyInstalled.ContentDigest, selected.ActiveVersion.ContentDigest);
        Assert.True(!string.Equals(
                previous.ContentDigest,
                selected.ActiveVersion.ContentDigest,
                StringComparison.Ordinal),
            $"{package.Manifest.Name} remained selected on the seeded older payload digest.");

        var configured = loaded.Catalog.GetConfigured(package.Manifest.Id);
        Assert.True(configured.RequiresAppContainer,
            $"{package.Manifest.Name} bypassed the generic Community AppContainer route.");
        Assert.Equal(deployment.WorkerHostPath, configured.WorkerExecutable);
        var backend = CreateBackend(
            spotifyReady: package.Manifest.Id == "org.gbar.samples.spotify");
        if (package.Manifest.Id == "org.gbar.samples.ytmusic")
        {
            backend.LoopbackHandler = (_, port, isPost, request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(YtmDesktopApiClient.CompanionPort, port);
                Assert.True(!isPost && request.Path == "/",
                    "Credential-free YT Music startup requested an unexpected companion route.");
                return Task.FromResult(new LoopbackJsonResponse(
                    200, "{\"authRequired\":true}", []));
            };
        }

        using var consentRoot = new TemporaryDirectory("gba-community-recovery-consent");
        var consent = new ConsentStore(consentRoot.Path);
        var identity = new BrokerWidgetIdentity(
            configured.PackageId, configured.PublisherId, configured.InstanceId);
        foreach (var capability in configured.DeclaredCapabilities)
            await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);

        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = configured.WorkerExecutable,
            Arguments = configured.WorkerArguments,
            WidgetInstanceId = configured.InstanceId,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = TimeSpan.FromSeconds(5),
            MaximumRestartAttempts = 0,
            StartupExitDiagnostics = WidgetWorkerStartupDiagnostics.LoaderExitCodes,
            IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
            ContentLeaseFactory = configured.ContentLeaseFactory,
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
        var first = await WaitForSnapshotAsync(client, package.ExpectedText);
        Assert.Equal(0, ViewSnapshotValidator.Validate(first).Count);
        Assert.Equal(1, client.Starts);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);

        evidence.Add(new
        {
            id = package.Manifest.Id,
            previousVersion = previous.Version.ToString(),
            version = package.Manifest.Version,
            packageSha256 = Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(package.PackagePath))).ToLowerInvariant(),
            selectedContentDigest = selected.ActiveVersion.ContentDigest,
            firstSnapshotSequence = first.Sequence,
        });
    }

    if (acceptanceOutput is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(acceptanceOutput)!);
        await File.WriteAllTextAsync(acceptanceOutput, JsonSerializer.Serialize(new
        {
            packages = evidence,
            phases = new[]
            {
                "seed-enabled-disable-install-select-enable",
                "selected-payload-digest-match",
                "generic-appcontainer-visible-first-snapshot",
            },
        }, CreateEvidenceJsonOptions()));
    }
}

static async Task SpotifyInstalledPackageRunsIsolated(
    string catalogRoot,
    string packagePath,
    string acceptanceOutput)
{
    const string packageId = "org.gbar.samples.spotify";
    const string currentVersion = "0.2.14";
    string[] rollbackVersions = ["0.2.11", "0.2.12", "0.2.13"];
    Assert.True(File.Exists(packagePath), "The exact Spotify archive is missing.");

    using var validationRoot = new TemporaryDirectory("gba-spotify-dlv055-validation");
    var inspection = await new WidgetCatalog(validationRoot.Path)
        .CreateInstaller().ValidateAsync(packagePath);
    Assert.Equal(packageId, inspection.Id);
    Assert.Equal(currentVersion, inspection.Version.ToString());

    var catalog = new WidgetCatalog(catalogRoot);
    var snapshot = await catalog.DiscoverAsync();
    var selected = snapshot.Widgets.Single(widget => widget.Id == packageId);
    Assert.True(selected.Enabled, "Spotify 0.2.14 is not enabled.");
    Assert.Equal(currentVersion, selected.ActiveVersion.Version.ToString());
    var rollbacks = rollbackVersions.Select(rollbackVersion =>
        selected.Versions.Single(version =>
            version.Version.ToString() == rollbackVersion)).ToArray();
    Assert.True(rollbacks.All(rollback =>
            rollback.Version != selected.ActiveVersion.Version),
        "Spotify rollback versions were not retained as distinct inactive versions.");

    using var digestRoot = new TemporaryDirectory("gba-spotify-dlv055-digest");
    var independentlyInstalled = await new WidgetCatalog(digestRoot.Path)
        .InstallAsync(packagePath);
    Assert.Equal(
        independentlyInstalled.ContentDigest,
        selected.ActiveVersion.ContentDigest);
    Assert.True(rollbacks.All(rollback =>
            !string.Equals(
                rollback.ContentDigest,
                selected.ActiveVersion.ContentDigest,
                StringComparison.Ordinal)),
        "Spotify 0.2.14 must have a distinct digest from every retained rollback.");

    var installedManifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(
        Path.Combine(selected.ActiveVersion.InstallPath, "manifest.json")));
    Assert.Equal(currentVersion, installedManifest.Version);
    Assert.Equal(packageId, installedManifest.Id);

    using var bridgeRoot = new TemporaryDirectory("gba-spotify-dlv055-bridge");
    var trustedCatalog = Path.Combine(bridgeRoot.Path, "trusted-catalog.json");
    await File.WriteAllTextAsync(
        trustedCatalog,
        "{\"catalogVersion\":1,\"widgets\":[],\"bundledWidgets\":[]}");
    var workerHost = Path.Combine(AppContext.BaseDirectory, "WidgetWorkerHost.exe");
    Assert.True(File.Exists(workerHost), "WidgetWorkerHost.exe is missing from test output.");
    var loaded = await BridgeCatalog.LoadWithInstalledAsync(
        trustedCatalog, catalogRoot, workerHost);
    Assert.True(loaded.InstalledCatalogValid, "The installed catalog was not valid.");
    var configured = loaded.Catalog.GetConfigured(packageId);
    Assert.True(configured.RequiresAppContainer, "Spotify did not require AppContainer isolation.");
    Assert.True(
        configured.ContentLeaseFactory is not null,
        "Spotify omitted exact installed-package launch authority.");
    using (var contentLease = configured.ContentLeaseFactory!(CancellationToken.None))
    {
        AssertWorkerArguments(
            configured,
            contentLease.Targets.Single(target => target.Target.Kind ==
                AppContainerAuthorityTargetKind.AuthorityRootDirectory).Target.Path,
            installedManifest);
    }

    var backend = CreateBackend(spotifyReady: true);
    using var consentRoot = new TemporaryDirectory("gba-spotify-dlv055-consent");
    var consent = new ConsentStore(consentRoot.Path);
    var identity = new BrokerWidgetIdentity(
        configured.PackageId, configured.PublisherId, configured.InstanceId);
    foreach (var capability in configured.DeclaredCapabilities)
        await consent.SetDecisionAsync(identity, capability, ConsentDecision.Grant);

    await using var client = new WidgetProcessClient(new WidgetProcessOptions
    {
        ExecutablePath = configured.WorkerExecutable,
        Arguments = configured.WorkerArguments,
        WidgetInstanceId = configured.InstanceId,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        RequestTimeout = TimeSpan.FromSeconds(5),
        MaximumRestartAttempts = 0,
        StartupExitDiagnostics = WidgetWorkerStartupDiagnostics.LoaderExitCodes,
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = configured.IsolationKey,
        ReadOnlyPaths = configured.ReadOnlyPaths,
        ContentLeaseFactory = configured.ContentLeaseFactory,
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
    var first = await WaitForSnapshotAsync(client, "Conformance Spotify Song");
    Assert.Equal(0, ViewSnapshotValidator.Validate(first).Count);
    Assert.Equal(1, client.Starts);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);

    Directory.CreateDirectory(Path.GetDirectoryName(acceptanceOutput)!);
    await File.WriteAllTextAsync(acceptanceOutput, JsonSerializer.Serialize(new
    {
        packageId,
        sourceVersion = currentVersion,
        archiveFileName = Path.GetFileName(packagePath),
        archiveSha256 = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(packagePath))).ToLowerInvariant(),
        installedRoot = selected.ActiveVersion.InstallPath,
        installedManifestVersion = installedManifest.Version,
        selectedVersion = selected.ActiveVersion.Version.ToString(),
        selectedContentDigest = selected.ActiveVersion.ContentDigest,
        independentlyInstalledContentDigest = independentlyInstalled.ContentDigest,
        rollbacks = rollbacks.Select(rollback => new
        {
            version = rollback.Version.ToString(),
            contentDigest = rollback.ContentDigest,
            retainedInactive = true,
        }).ToArray(),
        enabled = selected.Enabled,
        requiresAppContainer = configured.RequiresAppContainer,
        firstSnapshotSequence = first.Sequence,
        firstSnapshotValid = true,
    }, CreateEvidenceJsonOptions()));
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

static void CopyDirectory(string source, string destination)
{
    foreach (var directory in Directory.EnumerateDirectories(
                 source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(
            destination, Path.GetRelativePath(source, directory)));
    }
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.EnumerateFiles(
                 source, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(destination, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: false);
    }
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
            IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
            ContentLeaseFactory = configured.ContentLeaseFactory,
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
        var expectedInitialText = package.Manifest.Id == "org.gbar.samples.spotify"
            ? "Client ID required"
            : package.ExpectedText;
        var initial = await WaitForSnapshotAsync(client, expectedInitialText);
        await ExportSnapshotAsync(package, configured, "initial", initial);
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);

        if (package.Manifest.Id == "org.gbar.firstparty.games-apps")
        {
            Assert.True(Nodes(initial.Root).Any(node =>
                string.Equals(node.ActionId, "games.launch", StringComparison.Ordinal) &&
                string.Equals(node.AccessibilityLabel,
                    "Conformance Trusted Game, Game, Ready", StringComparison.Ordinal)),
                "The trusted Game was not auto-curated in the real package path.");
            Assert.True(Nodes(initial.Root).Any(node =>
                string.Equals(node.ActionId, "games.launch", StringComparison.Ordinal) &&
                node.StyleClasses.Contains("gbar-app-tile", StringComparer.Ordinal)),
                "Games & Apps did not retain the shared AppTile geometry classes.");
            Assert.True(!Nodes(initial.Root).Any(node =>
                (node.Text ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal)),
                "An Application was auto-curated even though it remains opt-in.");
            var open = Nodes(initial.Root).Single(node =>
                string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.open-catalog", open.Id));
            var catalog = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App");
            Assert.True(Nodes(catalog.Root).Any(node =>
                string.Equals(node.Id, "games.section", StringComparison.Ordinal) &&
                node.StyleClasses.Contains("gbar-section-header", StringComparer.Ordinal)),
                "The Catalog did not retain the shared section hierarchy.");
            Assert.True(Nodes(catalog.Root).Count() < ProtocolConstants.MaximumNodeCount,
                "The bounded Catalog page exceeded the protocol node budget.");
            await ExportSnapshotAsync(package, configured, "catalog", catalog);
            var add = Nodes(catalog.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal) &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.toggle-curation", add.Id));
            var selected = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true);
            await client.SendActionAsync(new WidgetActionEvent("back", selected.Root.Id));
            var populated = await WaitForActionSnapshotAsync(
                client, "games.launch", "Conformance Library App");
            await ExportSnapshotAsync(package, configured, "populated", populated);
            var reopen = Nodes(populated.Root).Single(node =>
                string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.open-catalog", reopen.Id));
            var removalCatalog = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true);
            var remove = Nodes(removalCatalog.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal) &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.toggle-curation", remove.Id));
            var removing = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: false);
            await ExportSnapshotAsync(package, configured, "removing", removing);
            await client.SendActionAsync(new WidgetActionEvent("back", removing.Root.Id));
            var removed = await WaitForActionSnapshotAsync(
                client, "games.launch", "Conformance Trusted Game");
            Assert.True(!Nodes(removed.Root).Any(node =>
                string.Equals(node.ActionId, "games.launch", StringComparison.Ordinal) &&
                Nodes(node).Any(descendant => (descendant.Text ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal))),
                "Removing one Catalog entry left the removed row in the Library.");
            Assert.Equal(1, Nodes(removed.Root).Count(node =>
                string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal)));
            await ExportSnapshotAsync(package, configured, "removed", removed);
            traces.Add(await WriteTraceAsync("GBA-038-games-apps", new
            {
                issue = "GBA-038",
                authority = "real installed package in AppContainer with simulated broker",
                steps = new[]
                {
                    new { action = "visible", invariant = "trusted Game is automatically curated while Application remains absent" },
                    new { action = "games.open-catalog", invariant = "catalog exposes Conformance Library App" },
                    new { action = "games.toggle-curation", invariant = "selected state becomes true" },
                    new { action = "back", invariant = "curated root exposes games.launch" },
                    new { action = "games.toggle-curation", invariant = "removing one saved Application preserves the unrelated trusted Game" },
                    new { action = "back", invariant = "Library retains exactly one Add applications action" },
                },
                observed = new
                {
                    backend.AppLibraryReadCalls,
                    initialNodeCount = Nodes(initial.Root).Count(),
                    catalogNodeCount = Nodes(catalog.Root).Count(),
                    populatedNodeCount = Nodes(populated.Root).Count(),
                    removingNodeCount = Nodes(removing.Root).Count(),
                    removedNodeCount = Nodes(removed.Root).Count(),
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
    string route,
    Action<TimeSpan>? firstRenderObserved = null,
    bool textEntryOnly = false)
{
    foreach (var package in packages)
    {
        SimulatedPlatformBrokerBackend? backend = null;
        try
        {
            var configured = catalog.GetConfigured(widgetId(package));
            backend = CreateBackend(
                spotifyReady: package.Manifest.Id == "org.gbar.samples.spotify",
                gameLibraryCount: package.Manifest.Id == "org.gbar.firstparty.game-launcher"
                    ? 10_000 : 2);
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
                IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
                IsolationKey = configured.IsolationKey,
                ReadOnlyPaths = configured.ReadOnlyPaths,
                ContentLeaseFactory = configured.ContentLeaseFactory,
                CompanionSessionFactory = context => new BrokerWidgetProcessCompanion(
                    configured.PackageId,
                    configured.PublisherId,
                    configured.InstanceId,
                    configured.DeclaredCapabilities,
                    consent,
                    backend,
                    context),
            });

            var activationStopwatch = Stopwatch.StartNew();
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
            var snapshot = await WaitForSnapshotAsync(client, package.ExpectedText);
            Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
            if (package.Manifest.Id is
                "org.gbar.firstparty.game-launcher" or
                "org.gbar.firstparty.network-controls")
            {
                var entry = Nodes(snapshot.Root).Single(node =>
                    node.Kind == ViewNodeKind.TextEntry);
                var styles = BridgeRenderStyleResolver.Resolve(
                    snapshot, configured.CompiledTheme);
                Assert.True(styles.ContainsKey(entry.Id),
                    $"{package.Manifest.Name} TextEntry was omitted from bridge computed styles.");
                Assert.Equal(Nodes(snapshot.Root).Count(), styles.Count);
            }
            activationStopwatch.Stop();
            firstRenderObserved?.Invoke(activationStopwatch.Elapsed);
            Assert.True(client.IsRunning,
                $"{package.Manifest.Name} {route} worker exited after render.");
            if (package.Manifest.Id == "org.gbar.firstparty.network-controls")
            {
                var protectedEntry = Nodes(snapshot.Root).Single(node =>
                    node.Kind == ViewNodeKind.TextEntry &&
                    string.Equals(node.ActionId, "wifi.connect.protected",
                        StringComparison.Ordinal));
                Assert.Equal(string.Empty, protectedEntry.TextEntryValue);
                Assert.Equal(63, protectedEntry.TextEntryMaximumLength);
            }

            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            if (textEntryOnly)
                await ExerciseTextEntryAsync(package, client, snapshot);
            else
                await ExerciseControlAsync(package, client, snapshot, backend, route);
            if (package.Manifest.Id == "org.gbar.samples.spotify")
            {
                await consent.SetDecisionAsync(
                    identity,
                    WidgetSpotifyCapabilities.PlaybackReadCapabilityId,
                    ConsentDecision.Deny);
                var refresh = Nodes(snapshot.Root).First(node =>
                    string.Equals(node.ActionId, "spotify.refresh", StringComparison.Ordinal));
                await client.SendActionAsync(new WidgetActionEvent(
                    "spotify.refresh", refresh.Id));
                var denied = await WaitForSnapshotAsync(client, "Spotify permission is off");
                Assert.True(Nodes(denied.Root).Any(node =>
                        string.Equals(node.ActionId, "spotify.retry", StringComparison.Ordinal)),
                    "Spotify permission revocation did not expose the safe retry state.");
                Assert.True(!Nodes(denied.Root).Any(node =>
                        string.Equals(node.Text, "Conformance Spotify Song",
                            StringComparison.Ordinal)),
                    "Spotify permission revocation retained provider-derived playback data.");
            }
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
            await client.StopAsync();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{package.Manifest.Name} failed through the {route} generic-worker route " +
                $"after app-library refresh/read calls " +
                $"{backend?.AppLibraryRefreshCalls}/{backend?.AppLibraryReadCalls}.",
                exception);
        }
    }
}

static async Task MediaSessionsPackageRunsIsolated(
    BridgeCatalog catalog,
    PackageFixture package)
{
    var configured = catalog.GetConfigured(package.Manifest.Id);
    var backend = CreateBackend();
    using var consentRoot = new TemporaryDirectory("gba-media-installed-consent");
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
        IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
        IsolationKey = configured.IsolationKey,
        ReadOnlyPaths = configured.ReadOnlyPaths,
        ContentLeaseFactory = configured.ContentLeaseFactory,
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
    _ = await WaitForSnapshotAsync(client, "Conformance Song");

    backend.SetMediaSessions([]);
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.MediaSessionsReadV1,
        PlatformCapabilities.MediaSessionsChanged,
        new MediaSessionsChangedEvent([])));
    _ = await WaitForSnapshotAsync(client, "Nothing is playing");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    backend.MediaSessionsHandler = _ => Task.FromException<IReadOnlyList<MediaSessionSummary>>(
        new BrokerException("platform_unavailable", "controlled transient failure"));
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var unavailable = await WaitForSnapshotAsync(client, "Windows media controls unavailable");
    var retry = Nodes(unavailable.Root).Single(node => node.ActionId == "media.retry");

    backend.MediaSessionsHandler = null;
    backend.SetMediaSessions([
        new MediaSessionSummary(
            "media-recovered", "Conformance Player", "Recovered Song",
            "Conformance Artist", MediaPlaybackStatus.Playing, 5_000, 180_000,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    await client.SendActionAsync(new WidgetActionEvent("media.retry", retry.Id));
    _ = await WaitForSnapshotAsync(client, "Recovered Song");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    var readStarted = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var readCanceled = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var staleRead = new TaskCompletionSource<IReadOnlyList<MediaSessionSummary>>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    backend.MediaSessionsHandler = token =>
    {
        readStarted.TrySetResult();
        token.Register(() => readCanceled.TrySetResult());
        return staleRead.Task;
    };
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var background = client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await readCanceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    staleRead.SetResult([
        new MediaSessionSummary(
            "media-stale", "Conformance Player", "Stale Song", "Artist",
            MediaPlaybackStatus.Paused, 0, 1_000, 1, 1, true,
            true, true, true, true, true),
    ]);
    await background.WaitAsync(TimeSpan.FromSeconds(2));

    backend.MediaSessionsHandler = null;
    backend.SetMediaSessions([
        new MediaSessionSummary(
            "media-current", "Conformance Player", "Current Song", "Artist",
            MediaPlaybackStatus.Paused, 0, 1_000, 1, 1, true,
            true, true, true, true, true),
    ]);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var current = await WaitForSnapshotAsync(client, "Current Song");
    Assert.True(!Nodes(current.Root).Any(node =>
            (node.Text ?? string.Empty).Contains("Stale Song", StringComparison.Ordinal)),
        "A cancellation-ignoring stale Media Sessions read reached the replacement generation.");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    await client.StopAsync();
    Assert.True(!client.IsRunning, "The installed Media Sessions worker did not stop cleanly.");
}

static async Task ExerciseTextEntryAsync(
    PackageFixture package,
    WidgetProcessClient client,
    ViewSnapshot snapshot)
{
    if (package.Manifest.Id == "org.gbar.firstparty.network-controls")
    {
        Assert.True(Nodes(snapshot.Root).Any(node =>
                node.Kind == ViewNodeKind.TextEntry &&
                node.ActionId == "wifi.connect.protected"),
            "Protected Network Controls TextEntry was not admitted.");
        return;
    }

    Assert.Equal("org.gbar.firstparty.game-launcher", package.Manifest.Id);
    var search = Nodes(snapshot.Root).Single(node =>
        node.Kind == ViewNodeKind.TextEntry &&
        node.ActionId == "game-launcher.search.commit");
    const string query = "Conformance Game 09999";
    await client.SendActionAsync(new WidgetActionEvent(
        "game-launcher.search.commit", search.Id)
        { CommittedText = query });
    var filtered = await WaitForActionSnapshotAsync(
        client, "game-launcher.search.commit", query);
    Assert.Equal(query, Nodes(filtered.Root).Single(node =>
        node.Kind == ViewNodeKind.TextEntry &&
        node.ActionId == "game-launcher.search.commit").TextEntryValue);
}

static async Task ExerciseControlAsync(
    PackageFixture package,
    WidgetProcessClient client,
    ViewSnapshot snapshot,
    SimulatedPlatformBrokerBackend backend,
    string route)
{
    if (package.Manifest.Id == "org.gbar.firstparty.audio-mixer")
    {
        await ExerciseAudioDashboardControlsAsync(client, snapshot, backend);
        return;
    }
    if (package.Manifest.Id == "org.gbar.firstparty.network-controls")
    {
        await ExerciseNetworkControlsUnpairAsync(client, snapshot, backend);
        return;
    }

    string actionId;
    ViewNode? explicitSource = null;
    Func<int> calls;
    switch (package.Manifest.Id)
    {
        case "org.gbar.firstparty.games-apps":
            var openCatalog = Nodes(snapshot.Root).Single(node =>
                string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent(
                "games.open-catalog", openCatalog.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App");
            var add = Nodes(snapshot.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal) &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal));
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
        case "org.gbar.firstparty.game-launcher":
            Assert.Equal(64, Nodes(snapshot.Root).Count(node =>
                node.ActionId == "game-launcher.launch"));
            Assert.True(Nodes(snapshot.Root).Count() < 1_024,
                "Game Launcher serialized an unbounded semantic tree.");
            Assert.True(Nodes(snapshot.Root).Any(node => node.ArtworkHandle is not null),
                "Game Launcher did not project lazy opaque artwork handles.");
            var nextPage = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.next");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.next", nextPage.Id));
            snapshot = await WaitForSnapshotAsync(client, "Conformance Game 00064");
            explicitSource = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.launch" &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Game 00064", StringComparison.Ordinal));
            Assert.Equal(explicitSource.Id, snapshot.InitialFocusId);
            var search = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.search.commit");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.search.commit", search.Id)
                { CommittedText = "Conformance Game 09999" });
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.launch", "Conformance Game 09999");
            Assert.Equal(1, Nodes(snapshot.Root).Count(node =>
                node.ActionId == "game-launcher.launch"));
            Assert.Equal("Conformance Game 09999", Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.search.commit").TextEntryValue);
            explicitSource = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.launch");
            var openAdd = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.add.open");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.add.open", openAdd.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.manual.included", "Conformance Game 00000");
            var addSearch = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.search.commit");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.search.commit", addSearch.Id)
                { CommittedText = "A Conformance Manual App" });
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.manual.toggle", "A Conformance Manual App");
            var manual = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.manual.toggle");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.manual.toggle", manual.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.manual.toggle", "Added");
            var back = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.add.back");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.add.back", back.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.launch", "A Conformance Manual App",
                requireEnabled: true);
            if (!Nodes(snapshot.Root).Any(node =>
                    node.ActionId == "game-launcher.launch" &&
                    (node.AccessibilityLabel ?? string.Empty).Contains(
                        "Conformance Game 00000", StringComparison.Ordinal)))
                snapshot = await WaitForSnapshotAsync(client, "Conformance Game 00000");
            Assert.Equal(65, Nodes(snapshot.Root).Count(node =>
                node.ActionId == "game-launcher.launch"));
            Assert.Equal(64, Nodes(snapshot.Root).Count(node =>
                node.ActionId == "game-launcher.launch" &&
                node.CollectionItemKey is not null));
            var hide = Nodes(snapshot.Root).First(node =>
                node.ActionId == "game-launcher.launch" &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Game 00000", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.hide", hide.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.hidden.open", "Hidden (1)");
            var openHidden = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.hidden.open");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.hidden.open", openHidden.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.restore", "Conformance Game 00000");
            var restore = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.restore");
            Assert.True(restore.IsDisabled is not true,
                "The current installed hidden row did not expose Restore.");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.restore", restore.Id));
            snapshot = await WaitForNodeTextSnapshotAsync(
                client, "game-launcher.status", "No hidden games");
            var hiddenBack = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.hidden.back" &&
                node.Id == "game-launcher.hidden.empty.action");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.hidden.back", hiddenBack.Id));
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.launch", "Conformance Game 00000",
                requireEnabled: true);
            var librarySearch = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.search.commit");
            await client.SendActionAsync(new WidgetActionEvent(
                "game-launcher.search.commit", librarySearch.Id)
                { CommittedText = "A Conformance Manual App" });
            snapshot = await WaitForActionSnapshotAsync(
                client, "game-launcher.launch", "A Conformance Manual App",
                requireEnabled: true);
            explicitSource = Nodes(snapshot.Root).Single(node =>
                node.ActionId == "game-launcher.launch");
            actionId = "game-launcher.launch";
            calls = () => backend.AppLibraryLaunchCalls;
            break;
        case "org.gbar.firstparty.media-sessions":
            actionId = "media.toggle";
            calls = () => backend.MediaControlCalls;
            break;
        case "org.gbar.samples.spotify":
            actionId = "spotify.next";
            calls = () => backend.SpotifyPlaybackControlCalls;
            break;
        default:
            throw new InvalidOperationException(
                $"No conformance control is defined for {package.Manifest.Id}.");
    }

    var source = explicitSource ?? Nodes(snapshot.Root).FirstOrDefault(node =>
        string.Equals(node.ActionId, actionId, StringComparison.Ordinal));
    Assert.True(source is not null,
        $"{package.Manifest.Name} {route} snapshot omitted action '{actionId}'.");
    var before = calls();
    var actionFailed = new TaskCompletionSource<WidgetActionFailure>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    EventHandler<WidgetActionFailure> failureHandler = (_, failure) =>
        actionFailed.TrySetResult(failure);
    client.ActionFailed += failureHandler;
    try
    {
        await client.SendActionAsync(new WidgetActionEvent(actionId, source!.Id));
        var effect = WaitUntilAsync(() => calls() > before);
        var terminal = await Task.WhenAny(effect, actionFailed.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (ReferenceEquals(terminal, actionFailed.Task))
        {
            var failure = await actionFailed.Task;
            throw new InvalidOperationException(
                $"{package.Manifest.Name} action failed before its broker effect: " +
                $"{failure.ActionId}/{failure.SourceElementId}: {failure.Message}");
        }
        try
        {
            await effect;
        }
        catch (TimeoutException exception)
        {
            var diagnostic = await client.GetSnapshotAsync();
            var status = Nodes(diagnostic.Root).FirstOrDefault(node =>
                string.Equals(node.Id, "games.status", StringComparison.Ordinal))?.Text ??
                "<missing games.status>";
            throw new TimeoutException(
                $"{package.Manifest.Name} produced no broker effect; widget status: {status}",
                exception);
        }
    }
    finally
    {
        client.ActionFailed -= failureHandler;
    }
    if (package.Manifest.Id == "org.gbar.samples.spotify")
    {
        Assert.Equal(
            SpotifyPlaybackOperation.Next,
            backend.LastSpotifyPlaybackCommand?.Operation);
    }
}

static async Task ExerciseNetworkControlsUnpairAsync(
    WidgetProcessClient client,
    ViewSnapshot snapshot,
    SimulatedPlatformBrokerBackend backend)
{
    var details = Nodes(snapshot.Root).Single(node =>
        node.ActionId == "network.details.open");
    await client.SendActionAsync(new WidgetActionEvent(
        "network.details.open", details.Id));
    snapshot = await WaitForSnapshotAsync(client, "192.0.2.10");
    Assert.Equal(1, backend.NetworkConnectionDetailsReadCalls);
    backend.NetworkConnectionDetails = backend.NetworkConnectionDetails with
    {
        Revision = 2,
        Connectivity = NetworkConnectionDetailsConnectivity.Constrained,
        IpAddresses = ["198.51.100.20"],
    };
    backend.Publish(new BrokerPlatformEvent(
        PlatformCapabilities.NetworkDetailsReadV1,
        PlatformCapabilities.NetworkDetailsChanged,
        new NetworkConnectionDetailsChangedEvent(2)));
    snapshot = await WaitForSnapshotAsync(client, "198.51.100.20");
    Assert.Equal(2, backend.NetworkConnectionDetailsReadCalls);
    Assert.True(!Nodes(snapshot.Root).Any(node =>
        node.Text?.Contains("interface", StringComparison.OrdinalIgnoreCase) == true ||
        node.Text?.Contains("guid", StringComparison.OrdinalIgnoreCase) == true),
        "Connection details exposed native adapter identity.");
    var back = Nodes(snapshot.Root).Single(node =>
        node.ActionId == "network.details.close");
    await client.SendActionAsync(new WidgetActionEvent(
        "network.details.close", back.Id));
    snapshot = await WaitForSnapshotAsync(client, "Conformance Wi-Fi");

    var bluetoothTab = Nodes(snapshot.Root).Single(node =>
        node.Id == "network.tab.bluetooth");
    await client.SendActionAsync(new WidgetActionEvent(
        "network.tab.select", bluetoothTab.Id));
    snapshot = await WaitForSnapshotAsync(client, "Conformance Controller");
    var target = Nodes(snapshot.Root).Single(node =>
        node.ActionId == "bluetooth.device.unpair.open" &&
        string.Equals(node.Text, "Conformance Controller", StringComparison.Ordinal));

    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.open", target.Id));
    var confirmation = await WaitForActionSnapshotAsync(
        client, "bluetooth.device.unpair.confirm", "Remove device");
    var cancel = Nodes(confirmation.Root).Single(node =>
        node.ActionId == "bluetooth.device.unpair.cancel");
    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.cancel", cancel.Id));
    snapshot = await WaitForSnapshotAsync(client, "Conformance Controller");
    Assert.Equal(2, (await backend.GetBluetoothAsync(CancellationToken.None)).Devices.Count);

    target = Nodes(snapshot.Root).Single(node =>
        node.ActionId == "bluetooth.device.unpair.open" &&
        string.Equals(node.Text, "Conformance Controller", StringComparison.Ordinal));
    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.open", target.Id));
    confirmation = await WaitForActionSnapshotAsync(
        client, "bluetooth.device.unpair.confirm", "Remove device");
    var remove = Nodes(confirmation.Root).Single(node =>
        node.ActionId == "bluetooth.device.unpair.confirm");
    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.confirm", remove.Id));
    snapshot = await WaitForSnapshotAsync(client, "Conformance Headset");
    var authoritative = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(1, authoritative.Devices.Count);
    Assert.Equal("Conformance Headset", authoritative.Devices.Single().DisplayName);

    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.open", target.Id));
    Assert.Equal(1, (await backend.GetBluetoothAsync(CancellationToken.None)).Devices.Count);
    Assert.True(!Nodes(snapshot.Root).Any(node =>
            node.Text?.Contains("bt-one", StringComparison.Ordinal) == true),
        "The generic worker exposed the broker's opaque Bluetooth identity as text.");
}

static async Task ExerciseAudioDashboardControlsAsync(
    WidgetProcessClient client,
    ViewSnapshot snapshot,
    SimulatedPlatformBrokerBackend backend)
{
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    snapshot = await client.GetSnapshotAsync();
    Assert.SequenceEqual(
        [ControllerButton.LeftBumper, ControllerButton.X, ControllerButton.RightBumper],
        snapshot.QuickActions.Select(action => action.Button));
    Assert.True(snapshot.QuickActions[0].Label.Contains("72% to 67%", StringComparison.Ordinal),
        "Dashboard LB label omitted its current and target volume.");
    Assert.True(snapshot.QuickActions[1].Label.Contains(
            "Mute master output at 72%", StringComparison.Ordinal),
        "Dashboard X label omitted its current mute/volume state.");
    Assert.True(snapshot.QuickActions[2].Label.Contains("72% to 77%", StringComparison.Ordinal),
        "Dashboard RB label omitted its current and target volume.");

    snapshot = await PressAsync(ControllerButton.LeftBumper, 301, expectedCalls: 1);
    Assert.Equal(0.67, backend.AudioOutput.Volume);
    Assert.True(snapshot.QuickActions[0].Label.Contains("67% to 62%", StringComparison.Ordinal),
        "Dashboard LB label did not reconcile to the provider result.");

    snapshot = await PressAsync(ControllerButton.RightBumper, 302, expectedCalls: 2);
    Assert.Equal(0.72, backend.AudioOutput.Volume);
    Assert.True(snapshot.QuickActions[2].Label.Contains("72% to 77%", StringComparison.Ordinal),
        "Dashboard RB label did not reconcile to the provider result.");

    snapshot = await PressAsync(ControllerButton.X, 303, expectedCalls: 3);
    Assert.True(backend.AudioOutput.IsMuted,
        "Dashboard X did not mute the simulated master output.");
    Assert.True(snapshot.QuickActions[1].Label.Contains(
            "Unmute master output at 72%", StringComparison.Ordinal),
        "Dashboard X label did not reconcile to muted state.");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
    var opened = await client.GetSnapshotAsync();
    var slider = Nodes(opened.Root).Single(node =>
        string.Equals(node.Id, "audio.master.volume.slider", StringComparison.Ordinal));
    Assert.Equal(0.72, slider.Value);
    Assert.True((slider.AccessibilityLabel ?? string.Empty).Contains("muted", StringComparison.Ordinal),
        "Opening Audio Mixer did not expose the reconciled dashboard mute result.");

    return;

    async Task<ViewSnapshot> PressAsync(
        ControllerButton button,
        long inputSequence,
        int expectedCalls)
    {
        var actionFailed = new TaskCompletionSource<WidgetActionFailure>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<WidgetActionFailure> failureHandler = (_, failure) =>
            actionFailed.TrySetResult(failure);
        client.ActionFailed += failureHandler;
        var action = snapshot.QuickActions.Single(item => item.Button == button);
        Assert.True(action.Capability is not null,
            $"Audio dashboard {button} omitted exact capability authority.");
        var handled = await client.SendControllerInputAsync(
            new ControllerInputEvent(
                button,
                ControllerEventPhase.Pressed,
                ControllerInputContext.DashboardQuickAction,
                Sequence: inputSequence,
                SnapshotSequence: snapshot.Sequence),
            new WidgetDashboardGestureAuthority(
                action.Capability!.CapabilityId,
                action.Capability.OperationId,
                inputSequence,
                snapshot.Sequence,
                TimeSpan.FromSeconds(2)));
        Assert.True(handled, $"Audio dashboard {button} was not accepted.");
        try
        {
            var controlObserved = WaitUntilAsync(() => backend.AudioControlCalls == expectedCalls);
            var terminal = await Task.WhenAny(controlObserved, actionFailed.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));
            if (ReferenceEquals(terminal, actionFailed.Task))
            {
                var failure = await actionFailed.Task;
                throw new InvalidOperationException(
                    $"Audio dashboard {button} failed before its broker effect: " +
                    $"{failure.ActionId}/{failure.SourceElementId}: {failure.Message}");
            }
            try
            {
                await controlObserved;
            }
            catch (TimeoutException exception)
            {
                var diagnostic = await client.GetSnapshotAsync();
                var status = Nodes(diagnostic.Root).FirstOrDefault(node =>
                    string.Equals(node.Id, "audio.status", StringComparison.Ordinal))?.Text ??
                    "<missing audio.status>";
                throw new TimeoutException(
                    $"Audio dashboard {button} produced no broker effect; widget status: {status}",
                    exception);
            }
        }
        finally
        {
            client.ActionFailed -= failureHandler;
        }
        snapshot = await client.GetSnapshotAsync();
        return snapshot;
    }
}

static SimulatedPlatformBrokerBackend CreateBackend(
    bool spotifyReady = false,
    int gameLibraryCount = 2)
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
        NetworkConnectionDetails = new NetworkConnectionDetailsSummary(
            1, NetworkConnectionDetailsState.Available,
            NetworkConnectionDetailsConnectivity.Internet,
            NetworkTransportKind.Wifi,
            ["192.0.2.10"], ["192.0.2.1"], ["9.9.9.9"]),
        WifiRadio = new WifiRadioSummary(WifiRadioState.On, true),
        BluetoothRadioState = BluetoothRadioState.On,
    };
    if (spotifyReady)
    {
        var scopes = new[]
        {
            SpotifyAuthorizationScope.PlaybackStateRead,
            SpotifyAuthorizationScope.PlaybackStateControl,
            SpotifyAuthorizationScope.LocalPlayback,
            SpotifyAuthorizationScope.PlaylistsRead,
        };
        backend.SpotifyConfiguration = new(
            true, "http://127.0.0.1:43827/callback/");
        backend.SpotifyAuthorization = new(
            SpotifyAuthorizationState.Connected, scopes, scopes, null);
        backend.SpotifyPlayback = new(
            true,
            true,
            12_000,
            180_000,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SpotifyRepeatState.Off,
            false,
            new SpotifyPlaybackItemSummary(
                SpotifyPlaybackItemType.Track,
                "Conformance Spotify Song",
                "Conformance Artist",
                "Conformance Context",
                null,
                "spotify:track:conformance"),
            new SpotifyPlaybackDisallowedActions(
                false, false, false, false, false, false, false, false),
            "Spotify");
    }
    backend.SetAudioSessions(
        [new AudioSessionSummary("audio-one", "Conformance Game", 0.63, false, true)]);
    backend.SetAudioDevices(
    [
        new AudioDeviceSummary("output-one", "Conformance Speakers", AudioDeviceDirection.Output, true),
        new AudioDeviceSummary("input-one", "Conformance Microphone", AudioDeviceDirection.Input, true),
    ]);
    if (gameLibraryCount > 2)
    {
        backend.SetAppLibraryBackend(Enumerable.Range(0, gameLibraryCount).Select(index =>
                new AppLibraryBackendItemSummary(
                    $"game-{index:D5}", $"stable-game-{index:D5}",
                    $"Conformance Game {index:D5}", AppLibraryKind.Game,
                    ArtworkRevision: $"art-{index:D5}", SourceAttribution: "Steam"))
            .Prepend(new AppLibraryBackendItemSummary(
                "manual-app", "stable-manual-app", "A Conformance Manual App",
                AppLibraryKind.Application, SourceAttribution: "Windows")));
    }
    else
    {
        backend.SetAppLibrary(
        [
            new AppLibraryItemSummary(
                "game-conformance", "saved-game-conformance",
                AppLibraryPresentation("Conformance Trusted Game", AppLibraryKind.Game, "Steam")),
            new AppLibraryItemSummary(
                "app-conformance", "saved-app-conformance",
                AppLibraryPresentation("Conformance Library App", AppLibraryKind.Application, "Windows")),
        ]);
    }
    backend.SetAvailableWifiNetworks(
    [
        new AvailableWifiNetworkSummary(
            "wifi-current", "Conformance Wi-Fi", 87, WifiSecurityKind.Personal,
            true, false, false),
    ]);
    backend.SetBluetoothDevices(
    [
        new BluetoothDeviceSummary("bt-one", "Conformance Controller", true, true, true),
        new BluetoothDeviceSummary("bt-two", "Conformance Headset", true, false, true),
    ]);
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

static AppLibraryItemPresentation AppLibraryPresentation(
    string displayName,
    AppLibraryKind kind,
    string sourceDisplayName) =>
    new(
        displayName,
        kind,
        new AppLibrarySourceReference($"source-{sourceDisplayName.ToLowerInvariant()}", sourceDisplayName),
        new AppLibraryAvailabilitySummary(AppLibraryAvailabilityState.Installed, true, "installed"),
        new AppLibraryArtworkSet([]),
        Metadata: null,
        new AppLibraryCapabilitySet([AppLibraryAction.Launch]),
        ActiveOperation: null);

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
    bool? selected = null,
    bool requireEnabled = false)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    ViewSnapshot? latest = null;
    while (DateTime.UtcNow < deadline)
    {
        latest = await client.GetSnapshotAsync();
        Assert.Equal(0, ViewSnapshotValidator.Validate(latest).Count);
        if (Nodes(latest.Root).Any(node =>
                string.Equals(node.ActionId, actionId, StringComparison.Ordinal) &&
                Nodes(node).Any(descendant =>
                    (descendant.Text ?? string.Empty).Contains(
                        expectedText, StringComparison.Ordinal)) &&
                (selected is null || (selected.Value
                    ? node.IsSelected == true
                    : node.IsSelected is not true)) &&
                (!requireEnabled || node.IsDisabled is not true)))
            return latest;
        await Task.Delay(40);
    }
    var actionDetails = latest is null
        ? "none"
        : string.Join(" | ", Nodes(latest.Root)
            .Where(node => string.Equals(node.ActionId, actionId, StringComparison.Ordinal))
            .Select(node => $"{node.Id}:selected={node.IsSelected}:" + string.Join(",",
                Nodes(node).Select(descendant => descendant.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text)))));
    throw new InvalidOperationException(
        $"Snapshot omitted action '{actionId}' for '{expectedText}'. " +
        $"Latest matching actions: {actionDetails}");
}

static async Task<ViewSnapshot> WaitForNodeTextSnapshotAsync(
    WidgetProcessClient client,
    string nodeId,
    string expectedText)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    ViewSnapshot? latest = null;
    while (DateTime.UtcNow < deadline)
    {
        latest = await client.GetSnapshotAsync();
        Assert.Equal(0, ViewSnapshotValidator.Validate(latest).Count);
        if (Nodes(latest.Root).Any(node =>
                string.Equals(node.Id, nodeId, StringComparison.Ordinal) &&
                string.Equals(node.Text, expectedText, StringComparison.Ordinal)))
            return latest;
        await Task.Delay(40);
    }
    var actual = latest is null ? "<no snapshot>" :
        Nodes(latest.Root).FirstOrDefault(node =>
            string.Equals(node.Id, nodeId, StringComparison.Ordinal))?.Text ?? "<missing>";
    throw new InvalidOperationException(
        $"Snapshot node '{nodeId}' did not reach '{expectedText}'; latest was '{actual}'.");
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
    public PackageFixture? SpotifyCommunityPackage { get; init; }
    public PackageFixture? YtMusicPackage { get; init; }

    public static async Task<Deployment> CreateAsync(
        bool installAsCommunity,
        bool includeEvidencePackages = false,
        bool ytMusicOnly = false,
        bool communityRecoveryOnly = false,
        bool fullApplicationOnly = false,
        bool mediaSessionsOnly = false)
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

            var packageSpecs = fullApplicationOnly
                ? new List<PackageSpec>
                {
                    new PackageSpec(
                        "full-application",
                        "tests/FullApplicationWidgetFixture",
                        "FullApplication",
                        WidgetGlyph.Play,
                        typeof(FullApplicationWidget),
                        "helper-ready:"),
                }
                : mediaSessionsOnly
                    ? new List<PackageSpec>
                    {
                        new PackageSpec(
                            "media-sessions",
                            "src/FirstPartyWidgets/MediaSessionsWidget",
                            "MediaSessions",
                            WidgetGlyph.Music,
                            typeof(MediaSessionsWidget),
                            "Conformance Song"),
                    }
                    : new List<PackageSpec>
                {
                new PackageSpec("media-sessions", "src/FirstPartyWidgets/MediaSessionsWidget", "MediaSessions", WidgetGlyph.Music,
                    typeof(MediaSessionsWidget), "Conformance Song"),
                new PackageSpec("games-apps", "src/FirstPartyWidgets/GamesAppsWidget", "GamesApps", WidgetGlyph.Play,
                    typeof(GamesAppsWidget), "Conformance Trusted Game"),
                new PackageSpec("game-launcher", "src/FirstPartyWidgets/GameLauncherWidget", "GameLauncher", WidgetGlyph.Play,
                    typeof(GameLauncherWidget), "Conformance Game 00000"),
                new PackageSpec("audio-mixer", "src/FirstPartyWidgets/AudioMixerWidget", "AudioMixer", WidgetGlyph.Volume,
                    typeof(AudioMixerWidget), "Conformance Game"),
                new PackageSpec("network-controls", "src/FirstPartyWidgets/NetworkControlsWidget", "NetworkControls", WidgetGlyph.Wifi,
                    typeof(NetworkControlsWidget), "Conformance Wi-Fi"),
                new PackageSpec("spotify", "samples/SpotifyWidget", "Spotify",
                    WidgetGlyph.Music, typeof(SpotifyWidget), "Conformance Spotify Song"),
                };
            if (includeEvidencePackages)
            {
                packageSpecs.Add(new PackageSpec(
                    "settings", "src/FirstPartyWidgets/SettingsWidget", "Settings",
                    WidgetGlyph.Settings, typeof(SettingsWidget), "Overlay"));
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
                if (spec.WidgetType == typeof(FullApplicationWidget))
                {
                    var outputDirectory = Path.GetDirectoryName(spec.WidgetType.Assembly.Location)
                        ?? throw new InvalidOperationException("Full-application fixture output is unavailable.");
                    foreach (var extension in new[] { ".exe", ".deps.json", ".runtimeconfig.json" })
                    {
                        var name = "FullApplicationWidgetFixture" + extension;
                        File.Copy(
                            Path.Combine(outputDirectory, name),
                            Path.Combine(bundleRoot, "payload", name));
                    }
                }
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
            PackageFixture? spotifyCommunityPackage = null;
            PackageFixture? ytMusicPackage = null;
            if (installAsCommunity)
            {
                var catalog = new WidgetCatalog(installedRoot);
                if (!ytMusicOnly && !communityRecoveryOnly)
                {
                    foreach (var fixture in fixtures)
                        await catalog.InstallAsync(fixture.PackagePath);
                    foreach (var fixture in fixtures)
                        await catalog.SetEnabledAsync(fixture.Manifest.Id, true);
                }

                if (communityRecoveryOnly)
                {
                    var spotifyProjectRoot = Path.Combine(repo, "samples", "SpotifyWidget");
                    var spotifyManifest = ManifestJson.Deserialize(
                        await File.ReadAllBytesAsync(Path.Combine(
                            spotifyProjectRoot, "manifest.json")));
                    var spotifySeedOutput = Path.Combine(
                        temporary.Path, "spotify-community-seed");
                    await RunCommunityPackageScriptAsync(
                        Path.Combine(spotifyProjectRoot, "Build-CommunityPackage.ps1"),
                        spotifySeedOutput,
                        installedRoot,
                        version: "0.2.10",
                        install: true);
                    var seededSpotify = (await catalog.DiscoverAsync()).Widgets
                        .Single(widget => widget.Id == spotifyManifest.Id);
                    Assert.True(seededSpotify.Enabled,
                        "Spotify older-version seed was not enabled before update.");
                    Assert.Equal("0.2.10", seededSpotify.ActiveVersion.Version.ToString());

                    var spotifyOutput = Path.Combine(
                        temporary.Path, "spotify-community-package");
                    await RunCommunityPackageScriptAsync(
                        Path.Combine(spotifyProjectRoot, "Build-CommunityPackage.ps1"),
                        spotifyOutput,
                        installedRoot,
                        version: null,
                        install: true);
                    var installedSpotify = (await catalog.DiscoverAsync()).Widgets
                        .Single(widget => widget.Id == spotifyManifest.Id)
                        .ActiveVersion;
                    spotifyCommunityPackage = new PackageFixture(
                        spotifyManifest.Id,
                        spotifyManifest.Presentation.Icon,
                        "Conformance Spotify Song",
                        spotifyManifest,
                        installedSpotify.InstallPath,
                        Path.Combine(spotifyOutput,
                            $"{spotifyManifest.Id}-{spotifyManifest.Version}.gbarwidget"),
                        spotifyManifest.Permissions.Concat(spotifyManifest.OptionalPermissions)
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
                }

                var ytProjectRoot = Path.Combine(repo, "samples", "YtMusicWidget");
                var ytManifest = ManifestJson.Deserialize(
                    await File.ReadAllBytesAsync(Path.Combine(ytProjectRoot, "manifest.json")));
                if (communityRecoveryOnly)
                {
                    var ytSeedOutput = Path.Combine(
                        temporary.Path, "ytmusic-community-seed");
                    await RunCommunityPackageScriptAsync(
                        Path.Combine(ytProjectRoot, "Build-CommunityPackage.ps1"),
                        ytSeedOutput,
                        installedRoot,
                        version: "0.2.6",
                        install: true);
                    var seededYt = (await catalog.DiscoverAsync()).Widgets
                        .Single(widget => widget.Id == ytManifest.Id);
                    Assert.True(seededYt.Enabled,
                        "YT Music older-version seed was not enabled before update.");
                    Assert.Equal("0.2.6", seededYt.ActiveVersion.Version.ToString());
                }
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
                    communityRecoveryOnly ? "Pair device" : "Conformance Song",
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
                SpotifyCommunityPackage = spotifyCommunityPackage,
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
            throw new InvalidOperationException("Community package script did not start.");
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
            throw new TimeoutException("Community public package workflow exceeded three minutes.");
        }
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            var diagnostic = (output + Environment.NewLine + error).Trim();
            if (diagnostic.Length > 2_000) diagnostic = diagnostic[..2_000];
            throw new InvalidOperationException(
                $"Community public package workflow failed ({process.ExitCode}): {diagnostic}");
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

file sealed class InstalledSteamArtworkFixture : IDisposable
{
    private readonly TemporaryDirectory _directory =
        new("gba-installed-steam-artwork");

    internal InstalledSteamArtworkFixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "steamapps"));
        Directory.CreateDirectory(Path.Combine(Root, "appcache", "librarycache"));
        WriteManifest("111", "Installed Steam One");
        WriteManifest("222", "Installed Steam Two");
        FirstArtworkPath = WriteArtwork("111", CreatePng(12, 18, 11));
        _ = WriteArtwork("222", CreatePng(15, 10, 23));
    }

    internal string Root => _directory.Path;
    internal string FirstArtworkPath { get; }

    internal void ReplaceFirstArtwork()
    {
        File.WriteAllBytes(FirstArtworkPath, CreatePng(19, 17, 47));
        File.SetLastWriteTimeUtc(FirstArtworkPath, DateTime.UtcNow.AddSeconds(5));
    }

    internal void RemoveFirstArtwork() => File.Delete(FirstArtworkPath);

    public void Dispose() => _directory.Dispose();

    private void WriteManifest(string appId, string displayName) =>
        File.WriteAllText(
            Path.Combine(Root, "steamapps", $"appmanifest_{appId}.acf"),
            $"\"AppState\" {{ \"appid\" \"{appId}\" \"name\" \"{displayName}\" }}");

    private string WriteArtwork(string appId, byte[] bytes)
    {
        var path = Path.Combine(
            Root, "appcache", "librarycache", $"{appId}_icon.png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] CreatePng(int width, int height, byte seed)
    {
        var bgra = new byte[checked(width * height * 4)];
        for (var index = 0; index < bgra.Length; index += 4)
        {
            bgra[index] = (byte)(seed + index);
            bgra[index + 1] = (byte)(seed * 3 + index);
            bgra[index + 2] = (byte)(seed * 7 + index);
            bgra[index + 3] = 255;
        }
        return WindowsAppIconSource.EncodePng(bgra, width, height);
    }
}

file sealed class InstalledArtworkSteamLauncher : IWindowsSteamLauncher
{
    public void Launch(string exactSteamAppId, CancellationToken cancellationToken) =>
        cancellationToken.ThrowIfCancellationRequested();
}

file sealed class InstalledGogFixture : IDisposable
{
    private readonly TemporaryDirectory _directory = new("gba-installed-gog");

    internal InstalledGogFixture()
    {
        ClientPath = Path.Combine(_directory.Path, "GOG Galaxy", "GalaxyClient.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(ClientPath)!);
        File.WriteAllBytes(ClientPath, [1, 2, 3]);
        var install = Path.Combine(_directory.Path, "Installed GOG Game");
        Directory.CreateDirectory(install);
        File.WriteAllText(Path.Combine(install, "goggame-100.info"),
            JsonSerializer.Serialize(new
            {
                gameId = "100",
                rootGameId = "100",
                name = DisplayName,
                playTasks = Array.Empty<object>(),
            }));
        Records.Add(new(
            WindowsGogRegistryReader.Registry32,
            "100", "100", DisplayName, install));
    }

    internal string ClientPath { get; }
    internal string DisplayName => "Installed GOG Game";
    internal List<GogRegistryRecord> Records { get; } = [];

    public void Dispose() => _directory.Dispose();
}

file sealed class InstalledGogRegistry(
    List<GogRegistryRecord> records) : IGogRegistryReader
{
    internal List<GogRegistryRecord> Records => records;
    internal int ReadCount { get; private set; }

    public GogRegistrySnapshot Enumerate(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return new(true, records.ToArray());
    }

    public GogRegistryRecord? ReadExact(
        string registryView,
        string keyName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        return records.SingleOrDefault(record =>
            string.Equals(record.RegistryView, registryView, StringComparison.Ordinal) &&
            string.Equals(record.KeyName, keyName, StringComparison.Ordinal));
    }
}

file sealed class InstalledGogLauncher : IWindowsGogLauncher
{
    internal int Count { get; private set; }

    public void Launch(
        string productId,
        string installLocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Count++;
    }
}

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
