using System.IO.Compression;
using System.Text.Json;
using GameBarAlternative.FirstPartyWidgets.AudioMixer;
using GameBarAlternative.FirstPartyWidgets.GamesApps;
using GameBarAlternative.FirstPartyWidgets.MediaSessions;
using GameBarAlternative.FirstPartyWidgets.NetworkControls;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetBridge;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("FirstPartyWidgetConformance.Tests skipped: Windows AppContainer is required.");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Bundled catalog derives runtime policy from real manifests", BundledCatalogUsesManifests),
    ("Bundled catalog rejects unsafe and ambiguous package sources", BundledCatalogRejectsUnsafeSources),
    ("Real first-party packages merge through the community catalog path", InstalledPackagesMerge),
    ("All first-party packages run through generic AppContainer worker and broker", PackagesRunIsolated),
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
    Assert.Equal(4, load.Catalog.Widgets.Count);

    foreach (var package in deployment.Packages)
    {
        var configured = load.Catalog.GetConfigured(package.Manifest.Id);
        Assert.True(configured.RequiresAppContainer,
            $"Installed {package.Manifest.Name} must require AppContainer.");
        Assert.Equal(deployment.WorkerHostPath, configured.WorkerExecutable);
        Assert.Equal(InstalledWidgetAuthority.PublisherId(package.Manifest), configured.PublisherId);
        Assert.SequenceEqual(package.DeclaredCapabilities, configured.DeclaredCapabilities);
        AssertWorkerArguments(configured, configured.ReadOnlyPaths.Single(), package.Manifest);
        AssertResidency(package.Manifest, configured.ResidencyPolicy);
    }
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
    throw new InvalidOperationException(
        $"Expected rendered broker data '{expectedText}', latest root was '{latest?.Root.Id ?? "none"}'.");
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
    public required string BundledWorkerHostPath { get; init; }
    public required string EmptyTrustedCatalogPath { get; init; }
    public required string BundledCatalogPath { get; init; }
    public required string InstalledCatalogRoot { get; init; }
    public required IReadOnlyList<PackageFixture> Packages { get; init; }

    public static async Task<Deployment> CreateAsync(bool installAsCommunity)
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

            var packageSpecs = new[]
            {
                new PackageSpec("media-sessions", "MediaSessions", WidgetGlyph.Music,
                    typeof(MediaSessionsWidget), "Conformance Song"),
                new PackageSpec("games-apps", "GamesApps", WidgetGlyph.Play,
                    typeof(GamesAppsWidget), "Conformance Library App"),
                new PackageSpec("audio-mixer", "AudioMixer", WidgetGlyph.Volume,
                    typeof(AudioMixerWidget), "Conformance Game"),
                new PackageSpec("network-controls", "NetworkControls", WidgetGlyph.Wifi,
                    typeof(NetworkControlsWidget), "Conformance Wi-Fi"),
            };
            var fixtures = new List<PackageFixture>();
            foreach (var spec in packageSpecs)
            {
                var projectRoot = Path.Combine(repo, "src", "FirstPartyWidgets", $"{spec.Directory}Widget");
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
            if (installAsCommunity)
            {
                var catalog = new WidgetCatalog(installedRoot);
                foreach (var fixture in fixtures)
                    await catalog.InstallAsync(fixture.PackagePath);
                foreach (var fixture in fixtures)
                    await catalog.SetEnabledAsync(fixture.Manifest.Id, true);
            }
            return new Deployment(temporary)
            {
                WorkerHostPath = workerSource,
                BundledWorkerHostPath = workerHost,
                EmptyTrustedCatalogPath = emptyCatalog,
                BundledCatalogPath = bundledCatalog,
                InstalledCatalogRoot = installedRoot,
                Packages = fixtures,
            };
        }
        catch
        {
            temporary.Dispose();
            throw;
        }
    }

    public void Dispose() => _root.Dispose();

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
