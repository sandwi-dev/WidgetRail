using System.IO.Compression;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using WidgetRail.FirstPartyWidgets.AudioMixer;
using WidgetRail.FirstPartyWidgets.GamesApps;
using WidgetRail.FirstPartyWidgets.MediaSessions;
using WidgetRail.FirstPartyWidgets.NetworkControls;
using WidgetRail.FirstPartyWidgets.Settings;
using WidgetRail.Tests.FullApplicationWidgetFixture;
using SpotifySampleWidget = WidgetRail.Samples.SpotifyWidget.SpotifyWidget;
using WidgetRail.Samples.YtMusicWidget;
using WidgetRail.Samples.FullApplicationWidget;
using WidgetRail.WrailCli;
using WidgetRail.PlatformBroker;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

// Playnite now owns a full-trust application: package admission is covered by
// WidgetBridge.Tests/FullTrustCommunityScenarios; detailed indexed workflows by
// PlayniteLibraryWidget.Tests and PlayniteLibraryCommunityApplication.Tests.
// Retired or misspelled acceptance modes must not silently run the default suite.
string[] supportedAcceptanceModes =
[
    "--ytmusic-community-acceptance", "--community-recovery-acceptance",
    "--text-entry-acceptance", "--full-application-acceptance",
    "--full-application-reference-acceptance", "--media-sessions-acceptance",
    "--games-apps-installed-acceptance",
];
var unknownAcceptanceMode = args.FirstOrDefault(argument =>
    argument.StartsWith("--", StringComparison.Ordinal) &&
    argument.EndsWith("-acceptance", StringComparison.Ordinal) &&
    !supportedAcceptanceModes.Contains(argument, StringComparer.Ordinal));
if (unknownAcceptanceMode is not null)
{
    Console.Error.WriteLine($"Unsupported acceptance mode '{unknownAcceptanceMode}'. " +
        "Use the current package runtime and provider test suites.");
    return 2;
}

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
    return await ExportEvidenceAsync(Path.GetFullPath(args[evidenceOutputIndex + 1])) ? 0 : 1;
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

if (args.Contains("--package-icon-contract", StringComparer.Ordinal))
{
    await ShippedPackageIconContracts();
    Console.WriteLine("PASS shipped package icon contracts");
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
        .Where(package => package.Manifest.Id == "widgetrail.firstparty.network-controls")
        .ToArray();
    Assert.Equal(1, packages.Length);
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

if (args.Contains("--full-application-reference-acceptance", StringComparer.Ordinal))
{
    using var deployment = await Deployment.CreateAsync(
        installAsCommunity: true,
        fullApplicationReferenceOnly: true);
    var installed = await BridgeCatalog.LoadWithInstalledAsync(
        deployment.EmptyTrustedCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    Assert.True(installed.InstalledCatalogValid,
        "Full Application reference installed catalog was rejected: " +
        string.Join(" | ", installed.Warnings));
    Assert.True(installed.Catalog.Widgets.Any(widget =>
            string.Equals(widget.Id, "widgetrail.samples.full-application", StringComparison.Ordinal)),
        "Full Application reference was not admitted: " +
        string.Join(" | ", installed.Warnings));
    await RunCatalogAsync(
        installed.Catalog,
        deployment.Packages,
        package => package.Manifest.Id,
        "installed-full-application-reference");
    Console.WriteLine("PASS installed full-application reference generic-worker acceptance");
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
        candidate.Manifest.Id == "widgetrail.firstparty.games-apps");
    var backend = CreateBackend();
    await RunCatalogAsync(
        installed.Catalog,
        [package],
        candidate => candidate.Manifest.Id,
        "installed-normalized-app-library",
        sharedBackend: backend);
    await RunCatalogAsync(
        installed.Catalog,
        [package],
        candidate => candidate.Manifest.Id,
        "installed-saved-library-restart",
        verifyGamesAppsRestart: true,
        sharedBackend: backend);
    Console.WriteLine("PASS installed Games & Apps normalized app-library acceptance");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Shipped package icons retain exact manifest, SVG, and fallback authority",
        ShippedPackageIconContracts),
    ("Bundled catalog derives runtime policy from real manifests", BundledCatalogUsesManifests),
    ("Bundled catalog rejects unsafe and ambiguous package sources", BundledCatalogRejectsUnsafeSources),
    ("Real first-party packages merge through the community catalog path", InstalledPackagesMerge),
    ("First-party and advanced sample packages run through generic AppContainer worker and broker", PackagesRunIsolated),
    ("Exact maximum directory package packs installs and runs isolated", MaximumDirectoryPackageRunsIsolated),
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

static async Task ShippedPackageIconContracts()
{
    var repository = FindRepositoryRootForTest();
    var expected = new[]
    {
        new IconContract("src/FirstPartyWidgets/MediaSessionsWidget",
            WidgetGlyph.Music, "media-sessions.mark", "assets/icons/media-sessions.svg"),
        new IconContract("src/FirstPartyWidgets/GamesAppsWidget",
            WidgetGlyph.Play, "games-apps.mark", "assets/icons/games-apps.svg"),
        new IconContract("src/FirstPartyWidgets/AudioMixerWidget",
            WidgetGlyph.Volume, "audio-mixer.mark", "assets/icons/audio-mixer.svg"),
        new IconContract("src/FirstPartyWidgets/NetworkControlsWidget",
            WidgetGlyph.Wifi, "network-controls.mark", "assets/icons/network-controls.svg"),
        new IconContract("samples/EmbeddedMediaWidget",
            WidgetGlyph.Play, "embedded-media.mark", "assets/icons/embedded-media.svg"),
        new IconContract("samples/YouTubeWidget",
            WidgetGlyph.Play, "youtube.brand.red", "assets/icons/youtube-red.svg"),
        new IconContract("samples/YtMusicWidget",
            WidgetGlyph.Music, "ytmusic.mark", "assets/icons/yt-music.svg"),
        new IconContract("samples/PlayniteLibraryWidget",
            WidgetGlyph.Play, "playnite-library.mark", "assets/icons/playnite-library.svg"),
        new IconContract("samples/ClockWidget",
            WidgetGlyph.Connection, "clock.mark", "assets/icons/clock.svg"),
        new IconContract("samples/FullApplicationWidget",
            WidgetGlyph.Settings, "full-application.mark", "assets/icons/full-application.svg"),
    };
    using var temporary = new TemporaryDirectory("wrail-package-icon-contract");
    var catalog = new WidgetCatalog(Path.Combine(temporary.Path, "catalog"));
    foreach (var contract in expected)
    {
        var packageRoot = Path.GetFullPath(
            contract.ProjectRoot.Replace('/', Path.DirectorySeparatorChar), repository);
        var manifestBytes = await File.ReadAllBytesAsync(
            Path.Combine(packageRoot, "manifest.json"));
        var manifest = ManifestJson.Deserialize(manifestBytes);
        Assert.Equal(0, WidgetManifestValidator.Validate(manifest).Count);
        Assert.Equal(contract.Fallback, manifest.Presentation.Icon);
        Assert.Equal(contract.AssetId, manifest.Presentation.PackageIcon?.AssetId);
        Assert.Equal(WidgetPackageIconColorMode.OriginalColor,
            manifest.Presentation.PackageIcon?.ColorMode);
        Assert.Equal(1, manifest.IconAssets.Count);
        Assert.Equal(contract.AssetPath, manifest.IconAssets[contract.AssetId].Path);
        var sourceAsset = Path.Combine(packageRoot,
            contract.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(sourceAsset), $"{manifest.Id} package icon source is missing.");

        var staging = Path.Combine(temporary.Path, manifest.Id);
        Directory.CreateDirectory(staging);
        await File.WriteAllBytesAsync(Path.Combine(staging, "manifest.json"), manifestBytes);
        var stagedAsset = Path.Combine(staging,
            contract.AssetPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(stagedAsset)!);
        File.Copy(sourceAsset, stagedAsset);
        var entrypoint = manifest.Entrypoint.Assembly ?? manifest.Entrypoint.Executable ??
            throw new InvalidOperationException($"{manifest.Id} has no package entrypoint.");
        var stagedEntrypoint = Path.Combine(staging,
            entrypoint.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(stagedEntrypoint)!);
        await File.WriteAllBytesAsync(stagedEntrypoint, [0x4d, 0x5a]);
        var archive = Path.Combine(temporary.Path, manifest.Id + ".wrwidget");
        ZipFile.CreateFromDirectory(staging, archive, CompressionLevel.NoCompression, false);
        var inspection = await catalog.CreateInstaller().ValidateAsync(archive);
        Assert.Equal(manifest.Id, inspection.Id);
        Assert.Equal(manifest.Version, inspection.Version.ToString());
    }

    var settings = ManifestJson.Deserialize(await File.ReadAllBytesAsync(Path.Combine(
        repository, "src", "FirstPartyWidgets", "SettingsWidget", "manifest.json")));
    Assert.Equal(WidgetGlyph.Settings, settings.Presentation.Icon);
    Assert.True(settings.Presentation.PackageIcon is null,
        "Settings must retain its accepted semantic gear without package-icon metadata.");
    Assert.SequenceEqual(
        new[] { "settings.accessibility", "settings.appearance", "settings.controllers", "settings.diagnostics", "settings.overlay", "settings.widgets" },
        settings.IconAssets.Keys.Order(StringComparer.Ordinal));
    foreach (var asset in settings.IconAssets.Values)
        Assert.True(File.Exists(Path.Combine(repository, "src", "FirstPartyWidgets", "SettingsWidget", asset.Path)),
            "Settings home icon asset is missing.");
}

static async Task BundledCatalogUsesManifests()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: false);
    deployment.SealBundledPackages();
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
        Assert.Equal(package.Manifest.ResourceRequest.MemoryMb, configured.MemoryRequestMb);
        AssertResidency(package.Manifest, configured.ResidencyPolicy);
        Assert.True(configured.ReadOnlyPaths.Count == 1 &&
            Path.GetFullPath(configured.ReadOnlyPaths[0]) == Path.GetFullPath(package.BundleRoot),
            $"{package.Manifest.Name} package root was not the only read-only package grant.");
        AssertWorkerArguments(configured, package.BundleRoot, package.Manifest);
        var descriptor = configured.PublicDescriptor();
        Assert.Equal(package.Manifest.Presentation.PackageIcon, descriptor.PackageIcon);
        Assert.SequenceEqual(package.Manifest.IconAssets.Keys.Order(StringComparer.Ordinal),
            descriptor.IconAssets.Select(asset => asset.AssetId).Order(StringComparer.Ordinal));
        Assert.SequenceEqual(package.Manifest.IconAssets.Keys.Order(StringComparer.Ordinal),
            configured.DeclaredPackageIconAssetIds.Order(StringComparer.Ordinal));
    }
}

static async Task BundledCatalogRejectsUnsafeSources()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: false);
    deployment.SealBundledPackages();
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
        "widgetrail.firstparty.wrong-package");
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
    deployment.SealBundledPackages();
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

static async Task MaximumDirectoryPackageRunsIsolated()
{
    using var deployment = await Deployment.CreateAsync(installAsCommunity: false);
    var fixture = deployment.Packages[0];
    var source = Path.Combine(deployment.RootPath, "maximum-directory-source");
    CopyDirectory(fixture.BundleRoot, source);
    Directory.CreateDirectory(Path.Combine(source, "assets"));
    var remainingDirectories = 1_024 -
        (Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories).Count() + 1);
    for (var index = 0; remainingDirectories > 0; index++)
    {
        var directory = Path.Combine(source, "assets");
        foreach (var segment in new[] { $"edge-{index:000}", "one", "two", "three" })
        {
            if (remainingDirectories == 0) break;
            directory = Path.Combine(directory, segment);
            remainingDirectories--;
        }
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "asset.txt"), "x");
    }
    var expectedFileCount = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Count();

    var packagePath = Path.Combine(deployment.RootPath, "maximum-directory.wrwidget");
    var packStopwatch = Stopwatch.StartNew();
    var pack = await RunCliAsync("pack", source, "--output", packagePath);
    packStopwatch.Stop();
    Assert.True(pack.Code == 0, $"Maximum-directory package failed to pack: {pack.Error}");
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
        Assert.Equal(expectedFileCount, lease.Targets.Count(target => target.Target.Kind ==
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
        $"METRIC exact_directory_package directories=1024 files={expectedFileCount} packMilliseconds={packStopwatch.Elapsed.TotalMilliseconds:F3} activationMilliseconds={activationElapsed.TotalMilliseconds:F3}");
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
        "The public wrail pack workflow did not publish a .wrwidget archive.");
    var packageInspection = await new WidgetCatalog(
            Path.Combine(deployment.RootPath, "validation-only"))
        .CreateInstaller().ValidateAsync(package.PackagePath);
    Assert.Equal(package.Manifest.Id, packageInspection.Id);
    Assert.Equal(package.Manifest.Version, packageInspection.Version.ToString());
    Assert.SequenceEqual(
        ["assets/icons/yt-music.svg", "manifest.json", "payload/YtMusicWidget.dll", "styles/default.wrss"],
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
    var companionMode = 0; // 0 = playing, 1 = connected idle, 2 = transient failure.
    string TrackId() => $"conformance-track-{Volatile.Read(ref trackGeneration)}";
    string TrackState() => JsonSerializer.Serialize(new
    {
        id = Volatile.Read(ref companionMode) == 1 ? string.Empty : TrackId(),
        playing = Volatile.Read(ref companionMode) != 1,
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
            title = Volatile.Read(ref companionMode) == 1
                ? null
                : $"Conformance Song {Volatile.Read(ref trackGeneration)}",
            author = Volatile.Read(ref companionMode) == 1
                ? null
                : "Conformance Artist",
            videoId = Volatile.Read(ref companionMode) == 1 ? null : TrackId(),
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
            (false, "/track/state") when Volatile.Read(ref companionMode) == 2 =>
                new LoopbackJsonResponse(503, "{}", []),
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

    using var consentRoot = new TemporaryDirectory("wrail-ytmusic-community-consent");
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

    Volatile.Write(ref companionMode, 2);
    await client.SendActionAsync(new WidgetActionEvent("refresh", "refresh"));
    var retained = await WaitForSnapshotAsync(client, "showing last known track");
    Assert.True(Nodes(retained.Root).Any(node =>
        node.Id == "track-title" &&
        (node.Text ?? string.Empty).StartsWith("Conformance Song", StringComparison.Ordinal)),
        "Transient installed companion failure replaced last-good media.");
    Volatile.Write(ref companionMode, 1);
    await client.SendActionAsync(new WidgetActionEvent("refresh", "refresh"));
    var idle = await WaitForSnapshotAsync(client, "No track playing");
    Assert.Equal(0, idle.QuickActions.Count);
    Assert.True(!Nodes(idle.Root).Any(node => node.ActionId == "toggle-playback"),
        "Connected-idle installed state retained playback authority.");
    Volatile.Write(ref companionMode, 0);
    await client.SendActionAsync(new WidgetActionEvent("refresh", "refresh"));
    _ = await WaitForSnapshotAsync(client, "Conformance Song");
    phases.Add("transient-last-good-connected-idle-and-recovery");

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
        Path.Combine(repo, "eng", "widget-catalog.json")));
    Assert.True(!productionCatalog.RootElement.GetProperty("widgets")
            .EnumerateArray().Any(widget =>
                widget.TryGetProperty("packageId", out var packageId) &&
                packageId.GetString()?.Contains("ytmusic", StringComparison.OrdinalIgnoreCase) == true),
        "YT Music still has a trusted catalog fallback.");
    var buildScript = File.ReadAllText(Path.Combine(repo, "scripts", "Build-WinUiReleasePayload.ps1"));
    Assert.True(!buildScript.Contains("YtMusicWidget.Worker", StringComparison.Ordinal),
        "WinUI payload still publishes the retired trusted YT Music worker.");
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
                "Physical controller input, WinUI shell composition, and visible transition fidelity require a packaged manual playtest.",
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
        deployment.YtMusicPackage ??
            throw new InvalidOperationException("YT Music community package was not produced."),
    };
    Assert.True(
        Version.Parse(packages[1].Manifest.Version) > Version.Parse("0.2.6"),
        "YT Music source package must use a version newer than 0.2.6.");
    var previousVersions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["widgetrail.samples.ytmusic"] = "0.2.6",
    };

    var catalog = new WidgetCatalog(deployment.InstalledCatalogRoot);
    var snapshot = await catalog.DiscoverAsync();
    Assert.Equal(1, snapshot.Widgets.Count);
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
        var backend = CreateBackend();
        if (package.Manifest.Id == "widgetrail.samples.ytmusic")
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

        using var consentRoot = new TemporaryDirectory("wrail-community-recovery-consent");
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
        if (File.Exists(Path.Combine(current.FullName, "eng", "widget-catalog.json")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Repository root was not found.");
}

static async Task<bool> ExportEvidenceAsync(string outputDirectory)
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
        deployment.TrustedSettingsCatalogPath,
        deployment.InstalledCatalogRoot,
        deployment.WorkerHostPath);
    Assert.True(installed.InstalledCatalogValid,
        "The installed catalog was rejected while exporting evidence.");

    var exported = new List<EvidenceSnapshotDescriptor>();
    var traces = new List<EvidenceTraceDescriptor>();
    var gaps = new List<EvidenceGapDescriptor>();
    foreach (var package in deployment.Packages.Where(package =>
                 package.Manifest.Id is
                    "widgetrail.firstparty.games-apps" or
                    "widgetrail.firstparty.settings" or
                    "widgetrail.samples.spotify"))
    {
        try
        {
        var trustedSettings = package.Manifest.Id == "widgetrail.firstparty.settings";
        var configured = installed.Catalog.GetConfigured(trustedSettings ? "settings" : package.Manifest.Id);
        var backend = CreateBackend();
        using var consentRoot = new TemporaryDirectory("wrail-evidence-consent");
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
            Arguments = trustedSettings
                ? configured.WorkerArguments.Concat(new[]
                {
                    "--settings-root", Path.Combine(deployment.RootPath, "settings-profile"),
                    "--host-exclusive-controller-control", "false",
                    "--host-held-dpad-scroll", "false",
                    "--host-startup-registration", "false",
                }).ToArray()
                : configured.WorkerArguments,
            WidgetInstanceId = configured.InstanceId,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = TimeSpan.FromSeconds(8),
            MaximumRestartAttempts = 0,
            IsolationPolicy = trustedSettings ? WidgetWorkerIsolationPolicy.HostTrustedJobOnly
                : WidgetWorkerIsolationPolicy.RequireAppContainer,
            IsolationKey = configured.IsolationKey,
            ReadOnlyPaths = configured.ReadOnlyPaths,
            ContentLeaseFactory = configured.ContentLeaseFactory,
            CompanionSessionFactory = trustedSettings ? null : context => new BrokerWidgetProcessCompanion(
                configured.PackageId,
                configured.PublisherId,
                configured.InstanceId,
                configured.DeclaredCapabilities,
                consent,
                backend,
                context),
        });

        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        var expectedInitialText = package.Manifest.Id == "widgetrail.samples.spotify"
            ? "Client ID required"
            : package.ExpectedText;
        ViewSnapshot initial;
        if (package.Manifest.Id == "widgetrail.firstparty.games-apps")
        {
            var rendered = await WaitForIndexedActionAsync(
                client, "games.launch", expectedInitialText, requireEnabled: false);
            await using var initialLease = rendered.Lease;
            initial = rendered.Snapshot;
            await ExportSnapshotAsync(package, configured, "initial", initial,
                [initialLease.Lease.Range]);
        }
        else
        {
            initial = await WaitForSnapshotAsync(client, expectedInitialText);
            await ExportSnapshotAsync(package, configured, "initial", initial);
        }
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);

        if (package.Manifest.Id == "widgetrail.firstparty.games-apps")
        {
            var trusted = await WaitForIndexedActionAsync(client, "games.launch", "Conformance Trusted Game");
            await using (var trustedLease = trusted.Lease)
            {
                initial = trusted.Snapshot;
                Assert.True(trusted.Item.Root.Kind == ViewNodeKind.ActionSurface &&
                    trusted.Item.Root.StyleClasses.Contains("games-app-tile", StringComparer.Ordinal),
                    "Games & Apps did not expose its themed application ActionSurface.");
                Assert.Equal(1, trustedLease.Lease.Range.Source.Count);
                Assert.True(!trustedLease.Lease.Range.Items.Any(item => Nodes(item.Root).Any(node =>
                    (node.Text ?? string.Empty).Contains("Conformance Library App", StringComparison.Ordinal))),
                    "An Application was auto-curated even though it remains opt-in.");
            }
            var open = GamesCompactNavigationAction(initial, "games.open-catalog");
            await client.SendActionAsync(new WidgetActionEvent("games.open-catalog", open.Id));
            var catalog = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", requireEnabled: true);
            Assert.True(Nodes(catalog.Root).Any(node => node.Id == "games.catalog.grid"),
                "The Catalog did not expose its bounded application grid.");
            Assert.True(Nodes(catalog.Root).Count() < ProtocolConstants.MaximumNodeCount,
                "The bounded Catalog page exceeded the protocol node budget.");
            await ExportSnapshotAsync(package, configured, "catalog", catalog);
            var add = Nodes(catalog.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal) &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.toggle-curation", add.Id));
            var selected = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true, requireEnabled: true);
            await client.SendActionAsync(new WidgetActionEvent("games.open-library",
                GamesCompactNavigationAction(selected, "games.open-library").Id));
            var populatedRow = await WaitForIndexedActionAsync(client, "games.launch", "Conformance Library App");
            var populated = populatedRow.Snapshot;
            await using (var populatedLease = populatedRow.Lease)
            {
                Assert.Equal(2, populatedLease.Lease.Range.Source.Count);
                await ExportSnapshotAsync(package, configured, "populated", populated, [populatedLease.Lease.Range]);
            }
            await client.SendActionAsync(new WidgetActionEvent("games.open-catalog",
                GamesCompactNavigationAction(populated, "games.open-catalog").Id));
            var removalCatalog = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true, requireEnabled: true);
            var remove = Nodes(removalCatalog.Root).Single(node =>
                string.Equals(node.ActionId, "games.toggle-curation", StringComparison.Ordinal) &&
                (node.AccessibilityLabel ?? string.Empty).Contains(
                    "Conformance Library App", StringComparison.Ordinal));
            await client.SendActionAsync(new WidgetActionEvent("games.toggle-curation", remove.Id));
            var removing = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: false, requireEnabled: true);
            await ExportSnapshotAsync(package, configured, "removing", removing);
            await client.SendActionAsync(new WidgetActionEvent("games.open-library",
                GamesCompactNavigationAction(removing, "games.open-library").Id));
            var removedRow = await WaitForIndexedActionAsync(client, "games.launch", "Conformance Trusted Game");
            var removed = removedRow.Snapshot;
            await using (var removedLease = removedRow.Lease)
            {
                Assert.Equal(1, removedLease.Lease.Range.Source.Count);
                Assert.True(!removedLease.Lease.Range.Items.Any(item => Nodes(item.Root).Any(node =>
                    (node.Text ?? string.Empty).Contains("Conformance Library App", StringComparison.Ordinal))),
                    "Removing one Catalog entry left the removed row in the Library.");
                _ = GamesCompactNavigationAction(removed, "games.open-catalog");
                await ExportSnapshotAsync(package, configured, "removed", removed, [removedLease.Lease.Range]);
            }
            traces.Add(await WriteTraceAsync("GBA-038-games-apps", new
            {
                issue = "GBA-038",
                authority = "real installed package in AppContainer with simulated broker",
                steps = new[]
                {
                    new { action = "visible", invariant = "trusted Game is automatically curated while Application remains absent" },
                    new { action = "games.open-catalog", invariant = "catalog exposes Conformance Library App" },
                    new { action = "games.toggle-curation", invariant = "selected state becomes true" },
                    new { action = "games.open-library", invariant = "leased Library row exposes games.launch" },
                    new { action = "games.toggle-curation", invariant = "removing one saved Application preserves the unrelated trusted Game" },
                    new { action = "games.open-library", invariant = "compact navigation retains exactly one Add applications action" },
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
        else if (trustedSettings)
        {
            traces.Add(await WriteTraceAsync("GBA-039-settings-root", new
            {
                issue = "GBA-039",
                authority = "production trusted Settings worker with isolated profile and installed catalog",
                steps = new[]
                {
                    new { action = "visible", invariant = "settings root renders through its dedicated trusted worker" },
                },
                observed = new { nodeCount = Nodes(initial.Root).Count() },
                limitation = "Settings mutations and private diagnostics are not invoked by this slice.",
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
            "widgetrail.firstparty.games-apps" or
            "widgetrail.samples.spotify")
        .Select(package => PreservePackageArchive(package, packageDirectory))
        .OrderBy(package => package.Id, StringComparer.Ordinal)
        .ToArray();
    var index = new
    {
        schemaVersion = 1,
        generatedUtc = DateTimeOffset.UtcNow,
        evidenceAuthority = "standalone widget-body harness: installed AppContainer packages with simulated broker, plus production trusted Settings worker with isolated profile; production bridge style resolver",
        packages = packageEvidence,
        trustedSettingsRuntime = Directory.EnumerateFiles(Path.Combine(deployment.RootPath, "runtime", "Settings"), "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(path => new
            {
                path = Path.GetRelativePath(deployment.RootPath, path),
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
            }).ToArray(),
        snapshots = exported,
        traces,
        gaps,
        limitations = new[]
        {
            "The backend is deterministic and simulated; no external accounts, radio hardware, or process launch is used.",
            "This index contains authoritative parent snapshots, separately retained indexed ranges, and parent WRSS styles; it is not native pixel evidence.",
            "This harness does not exercise the WinUI window, shell/tray/footer composition, z-order, focus ownership, input routing, or shell/widget transition fidelity.",
            "Settings permission-detail and Spotify OAuth completion are not claimed by this first slice.",
        },
    };
    await File.WriteAllTextAsync(
        Path.Combine(outputDirectory, "evidence-index.json"),
        JsonSerializer.Serialize(index, CreateEvidenceJsonOptions()));
    Console.WriteLine($"Exported {exported.Count} authoritative snapshots and {traces.Count} traces to {outputDirectory}.");
    return gaps.Count == 0;

    async Task ExportSnapshotAsync(
        PackageFixture package,
        ConfiguredWidget configured,
        string state,
        ViewSnapshot snapshot,
        IReadOnlyList<IndexedCollectionRange>? indexedRanges = null)
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
            indexedRanges,
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
    var fileName = $"{package.Manifest.Id}-{package.Manifest.Version}.wrwidget";
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
    bool textEntryOnly = false,
    bool verifyGamesAppsRestart = false,
    SimulatedPlatformBrokerBackend? sharedBackend = null)
{
    foreach (var package in packages)
    {
        SimulatedPlatformBrokerBackend? backend = null;
        try
        {
            var configured = catalog.GetConfigured(widgetId(package));
            backend = sharedBackend ?? CreateBackend();
            using var consentRoot = new TemporaryDirectory("wrail-firstparty-consent");
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
            var expectedText = verifyGamesAppsRestart
                ? "Conformance Library App" : package.ExpectedText;
            ViewSnapshot snapshot;
            if (package.Manifest.Id == "widgetrail.firstparty.games-apps")
            {
                var rendered = await WaitForIndexedActionAsync(
                    client, "games.launch", expectedText, requireEnabled: false);
                await using var renderedLease = rendered.Lease;
                snapshot = rendered.Snapshot;
            }
            else
                snapshot = await WaitForSnapshotAsync(client, expectedText);
            Assert.Equal(0, ViewSnapshotValidator.Validate(snapshot).Count);
            if (package.Manifest.Id == "widgetrail.firstparty.network-controls")
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
            if (package.Manifest.Id == "widgetrail.firstparty.network-controls")
            {
                var protectedEntry = Nodes(snapshot.Root).Single(node =>
                    node.Kind == ViewNodeKind.TextEntry &&
                    string.Equals(node.ActionId, "wifi.connect.protected",
                        StringComparison.Ordinal));
                Assert.Equal(string.Empty, protectedEntry.TextEntryValue);
                Assert.Equal(63, protectedEntry.TextEntryMaximumLength);
            }

            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive);
            if (verifyGamesAppsRestart)
            {
                Assert.Equal("widgetrail.firstparty.games-apps", package.Manifest.Id);
                var restored = await WaitForIndexedActionAsync(
                    client, "games.launch", "Conformance Library App");
                await using var restoredLease = restored.Lease;
                snapshot = restored.Snapshot;
                Assert.True(restored.Item.Root.IsDisabled is not true,
                    "The saved application did not become interactive in a fresh installed worker.");
                var compactNavigation = Nodes(snapshot.Root).Single(node => node.Id == "games.sections.compact");
                Assert.Equal(1, Nodes(compactNavigation).Count(node =>
                    node.Kind == ViewNodeKind.Button &&
                    string.Equals(node.ActionId, "games.open-catalog", StringComparison.Ordinal)));
            }
            else if (textEntryOnly)
                AssertNetworkTextEntry(package, snapshot);
            else
                await ExerciseControlAsync(package, client, snapshot, backend, route);
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
    using var consentRoot = new TemporaryDirectory("wrail-media-installed-consent");
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
    var currentMedia = await WaitForSnapshotAsync(client, "Conformance Song");

    await client.SetLifecycleStateAsync(WidgetLifecycleState.Background);
    backend.MediaSessionsHandler = _ => Task.FromResult<IReadOnlyList<MediaSessionSummary>>([
        new MediaSessionSummary(
            string.Empty, "Identity-less Player", "Unowned Song", "Artist",
            MediaPlaybackStatus.Paused, 0, 1_000, 1, 1, true,
            true, true, true, true, true),
    ]);
    await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
    var identityFailure = await WaitForSnapshotAsync(client, "invalid_backend_data");
    Assert.True(Nodes(identityFailure.Root).Any(node =>
            node.Id == "media.track-title" &&
            string.Equals(node.Text, "Conformance Song", StringComparison.Ordinal)),
        "Identity-less installed snapshot replaced current media.");
    backend.MediaSessionsHandler = null;
    backend.SetMediaSessions([
        new MediaSessionSummary(
            "media-identity-recovered", "Conformance Player", "Identity Recovered Song",
            "Conformance Artist", MediaPlaybackStatus.Playing, 5_000, 180_000,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1, true,
            true, true, true, true, true),
    ]);
    var identityRetry = Nodes(identityFailure.Root).Single(node =>
        node.ActionId == "media.retry");
    await client.SendActionAsync(new WidgetActionEvent("media.retry", identityRetry.Id));
    currentMedia = await WaitForSnapshotAsync(client, "Identity Recovered Song");
    Assert.True(currentMedia.QuickActions.Count > 0,
        "Recovered installed current session omitted truthful quick actions.");

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

static void AssertNetworkTextEntry(PackageFixture package, ViewSnapshot snapshot)
{
    Assert.Equal("widgetrail.firstparty.network-controls", package.Manifest.Id);
    Assert.True(Nodes(snapshot.Root).Any(node =>
            node.Kind == ViewNodeKind.TextEntry &&
            node.ActionId == "wifi.connect.protected"),
        "Protected Network Controls TextEntry was not admitted.");
}

static async Task ExerciseControlAsync(
    PackageFixture package,
    WidgetProcessClient client,
    ViewSnapshot snapshot,
    SimulatedPlatformBrokerBackend backend,
    string route)
{
    if (package.Manifest.Id == "widgetrail.samples.full-application")
    {
        var first = Nodes(snapshot.Root).First(node => node.ActionId == "full-app.open");
        await client.SendActionAsync(new WidgetActionEvent(first.ActionId!, first.Id));
        var details = await WaitForNodeTextSnapshotAsync(
            client, "full-app.details.title", "Document 00000");
        var handled = await client.SendControllerInputAsync(new ControllerInputEvent(
            ControllerButton.B,
            ControllerEventPhase.Pressed,
            ControllerInputContext.OpenWidget,
            FocusedElementId: "full-app.details.back",
            Sequence: 1,
            ActiveInputScopeId: details.ActiveInputScopeId,
            SnapshotSequence: details.Sequence));
        Assert.True(handled, "Full Application reference did not consume scoped Back.");
        var returned = await WaitForSnapshotAsync(client, "10,000 private records");
        Assert.Equal(first.Id, returned.InitialFocusId);
        return;
    }
    if (package.Manifest.Id == "widgetrail.firstparty.audio-mixer")
    {
        await ExerciseAudioDashboardControlsAsync(client, snapshot, backend);
        return;
    }
    if (package.Manifest.Id == "widgetrail.firstparty.network-controls")
    {
        await ExerciseNetworkControlsUnpairAsync(client, snapshot, backend);
        return;
    }

    string actionId;
    Func<int> calls;
    switch (package.Manifest.Id)
    {
        case "widgetrail.firstparty.games-apps":
            var compactNavigation = Nodes(snapshot.Root).Single(node => node.Id == "games.sections.compact");
            var openCatalog = Nodes(compactNavigation).Single(node =>
                node.Kind == ViewNodeKind.Button && node.ActionId == "games.open-catalog");
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
            snapshot = await WaitForActionSnapshotAsync(
                client, "games.toggle-curation", "Conformance Library App", selected: true,
                requireEnabled: true);
            var libraryDestination = Nodes(Nodes(snapshot.Root).Single(node =>
                    node.Id == "games.sections.compact")).Single(node =>
                node.Kind == ViewNodeKind.Button && node.ActionId == "games.open-library");
            await client.SendActionAsync(new WidgetActionEvent("games.open-library", libraryDestination.Id));
            var launch = await WaitForIndexedActionAsync(
                client, "games.launch", "Conformance Library App");
            await using (var launchLease = launch.Lease)
            {
                var launchesBefore = backend.AppLibraryLaunchCalls;
                var admission = await launchLease.AdmitInputAsync(
                    new IndexedCollectionInputRequest(
                        new IndexedCollectionItemReference(launchLease.Lease.LeaseId, launch.Item.Key),
                        ControllerButton.A),
                    new IndexedCollectionInputContext(launchLease.Lease.Range.ScopeId, launch.Snapshot.Sequence));
                Assert.Equal<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued, admission);
                await WaitUntilAsync(() => backend.AppLibraryLaunchCalls > launchesBefore);
                Assert.Equal(launchesBefore + 1, backend.AppLibraryLaunchCalls);
            }
            return;
        case "widgetrail.firstparty.media-sessions":
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
    snapshot = await WaitForActionSnapshotAsync(
        client, "bluetooth.device.details", "Conformance Controller", requireEnabled: true);
    var (confirmation, removalElementId) = await OpenRemovalConfirmationAsync(snapshot);
    var cancel = Nodes(confirmation.Root).Single(node =>
        node.ActionId == "bluetooth.device.unpair.cancel");
    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.cancel", cancel.Id));
    snapshot = await WaitForActionSnapshotAsync(
        client, "bluetooth.device.details", "Conformance Controller", requireEnabled: true);
    Assert.Equal(2, (await backend.GetBluetoothAsync(CancellationToken.None)).Devices.Count);

    (confirmation, removalElementId) = await OpenRemovalConfirmationAsync(snapshot);
    var remove = Nodes(confirmation.Root).Single(node =>
        node.ActionId == "bluetooth.device.unpair.confirm");
    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.confirm", remove.Id));
    snapshot = await WaitForSnapshotAsync(client, "Removed Conformance Controller");
    var authoritative = await backend.GetBluetoothAsync(CancellationToken.None);
    Assert.Equal(1, authoritative.Devices.Count);
    Assert.Equal("Conformance Headset", authoritative.Devices.Single().DisplayName);
    Assert.Equal("bt-one", backend.LastBluetoothDeviceId);

    await client.SendActionAsync(new WidgetActionEvent(
        "bluetooth.device.unpair.open", removalElementId));
    Assert.Equal(1, (await backend.GetBluetoothAsync(CancellationToken.None)).Devices.Count);
    Assert.True(!Nodes(snapshot.Root).Any(node =>
            node.Text?.Contains("bt-one", StringComparison.Ordinal) == true),
        "The generic worker exposed the broker's opaque Bluetooth identity as text.");

    async Task<(ViewSnapshot Confirmation, string RemovalElementId)> OpenRemovalConfirmationAsync(ViewSnapshot devices)
    {
        var target = Nodes(devices.Root).Single(node =>
            node.ActionId == "bluetooth.device.details" &&
            string.Equals(node.Text, "Conformance Controller", StringComparison.Ordinal));
        var previousDeviceId = backend.LastBluetoothDeviceId;
        await client.SendActionAsync(new WidgetActionEvent(target.ActionId!, target.Id));
        var options = await WaitForActionSnapshotAsync(
            client, "bluetooth.device.unpair.open", "Remove device", requireEnabled: true);
        Assert.Equal(2, (await backend.GetBluetoothAsync(CancellationToken.None)).Devices.Count);
        Assert.Equal(previousDeviceId, backend.LastBluetoothDeviceId);
        var removal = Nodes(options.Root).Single(node => node.ActionId == "bluetooth.device.unpair.open");
        await client.SendActionAsync(new WidgetActionEvent(removal.ActionId!, removal.Id));
        var pending = await WaitForActionSnapshotAsync(
            client, "bluetooth.device.unpair.confirm", "Remove device", requireEnabled: true);
        Assert.Equal(2, (await backend.GetBluetoothAsync(CancellationToken.None)).Devices.Count);
        Assert.Equal(previousDeviceId, backend.LastBluetoothDeviceId);
        return (pending, removal.Id);
    }
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
        NetworkConnectionDetails = new NetworkConnectionDetailsSummary(
            1, NetworkConnectionDetailsState.Available,
            NetworkConnectionDetailsConnectivity.Internet,
            NetworkTransportKind.Wifi,
            ["192.0.2.10"], ["192.0.2.1"], ["9.9.9.9"]),
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
            "game-conformance", "saved-game-conformance",
            AppLibraryPresentation("Conformance Trusted Game", AppLibraryKind.Game, "Steam")),
        new AppLibraryItemSummary(
            "app-conformance", "saved-app-conformance",
            AppLibraryPresentation("Conformance Library App", AppLibraryKind.Application, "Windows")),
    ]);
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

static ViewNode GamesCompactNavigationAction(ViewSnapshot snapshot, string actionId) =>
    Nodes(Nodes(snapshot.Root).Single(node => node.Id == "games.sections.compact"))
        .Single(node => node.Kind == ViewNodeKind.Button && node.ActionId == actionId);

// Indexed rows are intentionally absent from the parent snapshot. Verify the same
// leased semantic item and input admission path used by the WinUI collection host.
static async Task<(ViewSnapshot Snapshot, WidgetProcessIndexedLease Lease, IndexedCollectionItem Item)>
    WaitForIndexedActionAsync(
        WidgetProcessClient client,
        string actionId,
        string expectedText,
        bool requireEnabled = true)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
    ViewSnapshot? latest = null;
    while (DateTime.UtcNow < deadline)
    {
        latest = await client.GetSnapshotAsync();
        Assert.Equal(0, ViewSnapshotValidator.Validate(latest).Count);
        foreach (var collection in Nodes(latest.Root).Where(node => node.IndexedCollection is { Count: > 0 }))
        {
            var source = collection.IndexedCollection!;
            for (var start = 0; start < source.Count; start += IndexedCollectionLimits.MaximumRangeItems)
            {
                var lease = await client.AcquireIndexedRangeAsync(
                    new IndexedCollectionRangeRequest(collection.Id, source, start,
                        Math.Min(IndexedCollectionLimits.MaximumRangeItems, source.Count - start),
                        Guid.NewGuid().ToString("N")), client.Starts);
                var item = lease.Lease.Range.Items.FirstOrDefault(candidate =>
                    string.Equals(candidate.Root.ActionId, actionId, StringComparison.Ordinal) &&
                    Nodes(candidate.Root).Any(node =>
                        (node.Text ?? string.Empty).Contains(expectedText, StringComparison.Ordinal)) &&
                    (!requireEnabled || candidate.Root.IsDisabled is not true));
                if (item is not null)
                    return (latest, lease, item);
                await lease.DisposeAsync();
            }
        }
        await Task.Delay(40);
    }
    throw new InvalidOperationException(
        $"Indexed snapshot omitted action '{actionId}' for '{expectedText}'; latest root '{latest?.Root.Id}'.");
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
    var assembly = manifest.Entrypoint?.Assembly ??
        throw new InvalidOperationException(
            $"{manifest.Id} does not declare a managed-worker assembly.");
    Assert.SequenceEqual(
    [
        "--package-root", Path.GetFullPath(packageRoot),
        "--widget-assembly", Path.GetFullPath(
            assembly.Replace('/', Path.DirectorySeparatorChar),
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
    public required string TrustedSettingsCatalogPath { get; init; }
    public required string InstalledCatalogRoot { get; init; }
    public required IReadOnlyList<PackageFixture> Packages { get; init; }
    public PackageFixture? SpotifyCommunityPackage { get; init; }
    public PackageFixture? YtMusicPackage { get; init; }

    public void SealBundledPackages()
    {
        foreach (var package in Packages)
            InstalledPackageIntegrity.Seal(RootPath, package.BundleRoot, new WidgetCatalogOptions());
    }

    public static async Task<Deployment> CreateAsync(
        bool installAsCommunity,
        bool includeEvidencePackages = false,
        bool ytMusicOnly = false,
        bool communityRecoveryOnly = false,
        bool fullApplicationOnly = false,
        bool fullApplicationReferenceOnly = false,
        bool mediaSessionsOnly = false)
    {
        var temporary = new TemporaryDirectory("wrail-firstparty-conformance");
        try
        {
            var repo = FindRepositoryRoot();
            var workerSource = Path.Combine(AppContext.BaseDirectory, "WidgetWorkerHost.exe");
            Assert.True(File.Exists(workerSource), "WidgetWorkerHost.exe was not copied to test output.");
            var runtime = Path.Combine(temporary.Path, "runtime");
            var workerDirectory = Path.Combine(runtime, "WidgetWorkerHost");
            Directory.CreateDirectory(workerDirectory);
            CopyWorkerDeployment(AppContext.BaseDirectory, workerDirectory, "WidgetWorkerHost");
            var workerHost = Path.Combine(workerDirectory, "WidgetWorkerHost.exe");

            var packageSpecs = ytMusicOnly
                ? new List<PackageSpec>()
                : fullApplicationReferenceOnly
                ? new List<PackageSpec>
                {
                    new PackageSpec(
                        "full-application-reference",
                        "samples/FullApplicationWidget",
                        "FullApplicationReference",
                        WidgetGlyph.Settings,
                        typeof(FullApplicationReferenceWidget),
                        "10,000 private records"),
                }
                : fullApplicationOnly
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
                File.Copy(Path.Combine(projectRoot, "styles", "default.wrss"),
                    Path.Combine(bundleRoot, "styles", "default.wrss"));
                foreach (var asset in manifest.IconAssets.Values)
                {
                    var sourceAsset = Path.Combine(projectRoot,
                        asset.Path.Replace('/', Path.DirectorySeparatorChar));
                    var stagedAsset = Path.Combine(bundleRoot,
                        asset.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(stagedAsset)!);
                    File.Copy(sourceAsset, stagedAsset);
                }
                var assembly = manifest.Entrypoint?.Assembly ??
                    throw new InvalidOperationException(
                        $"{manifest.Id} does not declare a managed-worker assembly.");
                File.Copy(spec.WidgetType.Assembly.Location,
                    Path.Combine(bundleRoot, assembly.Replace('/', Path.DirectorySeparatorChar)));
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
                var packagePath = Path.Combine(temporary.Path, $"{manifest.Id}.wrwidget");
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
            var trustedSettingsCatalog = Path.Combine(temporary.Path, "trusted-settings-catalog.json");
            var settingsRoot = Path.Combine(runtime, "Settings");
            if (includeEvidencePackages)
            {
                CopyWorkerDeployment(AppContext.BaseDirectory, settingsRoot, "SettingsWidget.Worker");
                InstalledPackageIntegrity.Seal(temporary.Path, settingsRoot, new WidgetCatalogOptions());
            }
            await File.WriteAllTextAsync(trustedSettingsCatalog, JsonSerializer.Serialize(new
            {
                catalogVersion = 1,
                widgets = includeEvidencePackages ? new[]
                {
                    new
                    {
                        id = "settings", packageId = "widgetrail.firstparty.settings",
                        publisherId = "widgetrail.firstparty", name = "Settings", instanceId = "settings.default",
                        workerExecutable = "runtime/Settings/SettingsWidget.Worker.exe",
                        styleFile = "runtime/Settings/styles/default.wrss", iconPackageRoot = "runtime/Settings",
                        declaredCapabilities = Array.Empty<string>(),
                    },
                } : [],
                bundledWidgets = Array.Empty<object>(),
            }));
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
                    foreach (var fixture in fixtures.Where(fixture => fixture.Manifest.Id != "widgetrail.firstparty.settings"))
                    {
                        await catalog.InstallAsync(fixture.PackagePath);
                        await catalog.SetEnabledAsync(fixture.Manifest.Id, true);
                    }
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
                            $"{spotifyManifest.Id}-{spotifyManifest.Version}.wrwidget"),
                        spotifyManifest.Permissions.Concat(spotifyManifest.OptionalPermissions)
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
                }

                var ytProjectRoot = Path.Combine(repo, "tests", "YtMusicCompanionFixture");
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
                    Path.Combine(ytOutput, $"{ytManifest.Id}-{ytManifest.Version}.wrwidget"),
                    ytManifest.Permissions.Concat(ytManifest.OptionalPermissions)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }
            return new Deployment(temporary)
            {
                WorkerHostPath = workerSource,
                BundledWorkerHostPath = workerHost,
                EmptyTrustedCatalogPath = emptyCatalog,
                BundledCatalogPath = bundledCatalog,
                TrustedSettingsCatalogPath = trustedSettingsCatalog,
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
        var projectRoot = Path.Combine(repo, "tests", "YtMusicCompanionFixture");
        var output = Path.Combine(_root.Path, $"ytmusic-community-{version}");
        await RunCommunityPackageScriptAsync(
            Path.Combine(projectRoot, "Build-CommunityPackage.ps1"),
            output,
            catalogRoot: null,
            version,
            install: false);
        var manifest = ManifestJson.Deserialize(
            await File.ReadAllBytesAsync(Path.Combine(output, "package-root", "manifest.json")));
        return Path.Combine(output, $"{manifest.Id}-{manifest.Version}.wrwidget");
    }

    private static void CopyWorkerDeployment(
        string sourceDirectory,
        string destinationDirectory,
        string workerName)
    {
        const int maximumAssets = 128;
        var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            workerName + ".exe",
            workerName + ".deps.json",
            workerName + ".runtimeconfig.json",
        };
        var dependencies = Path.Combine(sourceDirectory, workerName + ".deps.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(dependencies));
        var root = document.RootElement;
        if (!root.TryGetProperty("runtimeTarget", out var runtimeTarget) ||
            !runtimeTarget.TryGetProperty("name", out var runtimeNameElement) ||
            runtimeNameElement.GetString() is not { Length: > 0 } runtimeName ||
            !root.TryGetProperty("targets", out var targets) ||
            !targets.TryGetProperty(runtimeName, out var target))
            throw new InvalidOperationException(
                $"{workerName}.deps.json did not expose its selected runtime target.");

        foreach (var library in target.EnumerateObject())
        {
            AddAssetGroup(library.Value, "runtime", assets);
            AddAssetGroup(library.Value, "native", assets);
            AddAssetGroup(library.Value, "resources", assets);
            AddAssetGroup(library.Value, "runtimeTargets", assets);
            if (assets.Count > maximumAssets)
                throw new InvalidOperationException(
                    $"{workerName} dependency closure exceeded its conformance bound.");
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
                    $"{workerName} dependency closure contained an absolute path.");
            var source = Path.GetFullPath(relative, sourceRoot);
            var destination = Path.GetFullPath(relative, destinationRoot);
            if (!source.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase) ||
                !destination.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{workerName} dependency closure escaped its deployment root.");
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    $"{workerName} dependency '{asset}' was absent from the build output.",
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
            if (File.Exists(Path.Combine(current.FullName, "eng", "widget-catalog.json")))
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

file sealed record IconContract(
    string ProjectRoot,
    WidgetGlyph Fallback,
    string AssetId,
    string AssetPath);

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
