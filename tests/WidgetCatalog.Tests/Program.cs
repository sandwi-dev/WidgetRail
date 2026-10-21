using WidgetRail.PlatformBroker;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;

var allTests = new (string Name, Func<Task> Run)[]
{
    ("Initial bundle seeds ordinary enabled widgets once and respects removals", InitialBundleLifecycle),
    ("Initial bundle preserves existing selections and rejects wrong identities", InitialBundleSafety),
    ("Host utilities retire only replaced packages and cannot be seeded again", RetiredHostUtilities),
    ("Reviewed updates reuse retained bytes after rollback and reject conflicting archives", RetainedUpdateReuse),
    ("Malformed retention receipts cannot delete packages or block catalog use", RetentionMalformedReceipts),
    ("Updates retain current and previous versions across repeated publication", RetentionUpdates),
    ("Retention preserves selected versions and failed updates at legacy capacity", RetentionFailures),
    ("Retention retries locked cleanup and recovers interrupted publication", RetentionRecovery),
    ("Startup admission skips inactive payloads and yields the requested enabled selection first", StartupActiveSelections),
    ("Startup admission rejects changed selections and cancelled enumeration", StartupSelectionRaces),
    ("Package SVG icons normalize a bounded static subset and reject active content",
        PackageSvgIconCatalogTests.Run),
    ("Reviewed updates preserve settings across exact package authorities", ReviewedSettingsContinuity),
    ("Reviewed updates preserve permission decisions and app-library identities", ReviewedPermissionContinuity),
    ("Update continuity preserves destination decisions and isolates other packages", UpdateContinuitySafety),
    ("Settings transfer preserves existing values and clear markers and fails closed", SettingsTransferSafety),
    ("Install and discovery are deterministic across IDs and versions", InstallAndDiscover),
    ("Enable and order state persists atomically", StatePersists),
    ("Version pins are disabled-only and schema-one state migrates", VersionPinningAndMigration),
    ("Update preparation latches the reviewed version before publication", UpdatePreparationPinsReviewedVersion),
    ("Concurrent first installs serialize before enablement", ConcurrentFirstInstallsAreSafe),
    ("Concurrent rollbacks are linearizable", ConcurrentRollbacksAreLinearizable),
    ("Uninstall is disabled-only and removes every immutable version", UninstallRemovesAllVersions),
    ("Pending uninstall recovery retires authority before same-package reuse",
        PendingUninstallRecoveryIsCommitBound),
    ("Missing pinned versions fail closed", MissingPinnedVersionFailsClosed),
    ("Independent catalog clients serialize state mutations", ConcurrentStatePersists),
    ("Lock-free discovery coexists with atomic state replacement", ConcurrentDiscoveryAndMutation),
    ("Reinstall never overwrites an immutable version", ReinstallDoesNotOverwrite),
    ("Public path and stream installs enforce disabled-only updates", PublicInstallsEnforceUpdatePolicy),
    ("Traversal paths are rejected before extraction", TraversalIsRejected),
    ("Case-colliding paths are rejected", CaseCollisionIsRejected),
    ("Case-colliding directory prefixes are rejected", DirectoryCaseCollisionIsRejected),
    ("Unix symlink entries are rejected", SymlinkIsRejected),
    ("Entry count and expanded size limits are enforced", LimitsAreEnforced),
    ("Data-heavy packages use the shared bounded budget", DataHeavyPackageBudget),
    ("Aggregate installed catalog limits reject work before publication", AggregateCatalogLimits),
    ("Over-limit catalogs expose bounded inactive-version recovery", OverLimitCatalogRecovery),
    ("Maximum catalog recovery projection stays within its budget", MaximumCatalogRecoveryIsBounded),
    ("Publisher and package identity are enforced", IdentityIsEnforced),
    ("Missing entrypoint assemblies are rejected", MissingEntrypointIsRejected),
    ("Tampered installed directory identity is rejected", TamperedInstallIsRejected),
    ("Tampered installed content fails closed", TamperedContentFailsClosed),
    ("Installed manifest and integrity metadata enforce consumed-byte bounds", InstalledMetadataBoundsFailClosed),
    ("Package cannot supply host integrity metadata", ReservedIntegrityPathIsRejected),
    ("Host compatibility is deterministic across API and architecture", HostCompatibility),
    ("Unsigned authority is stable only for one exact content tree", UnsignedAuthorityIsContentBound),
    ("Integrity verification parses the manifest bytes it hashed", VerificationReturnsHashedManifest),
    ("Launch leases pin verified bytes until session release", LaunchLeasePinsVerifiedBytes),
    ("Launch leases reject pre-start mutation and late namespace insertion", LaunchLeaseRejectsRaces),
    ("Launch leases reject runtime-incompatible file inventories", LaunchLeaseRejectsExcessFiles),
    ("Maximum package launch lease stays within its startup budget", MaximumLaunchLeaseIsBounded),
    ("Bounded reads reject bytes beyond a reported length", BoundedReadsRejectMisreportedLengths),
    ("Integrity hashing rejects early EOF and trailing bytes", IntegrityHashingRequiresExactLength),
};

var tests = args.Contains("--initial-bundle-only", StringComparer.Ordinal)
    ? allTests.Where(test => test.Name.StartsWith("Initial bundle", StringComparison.Ordinal)).ToArray()
    : args.Contains("--widge-193-only", StringComparer.Ordinal)
    ? allTests.Where(test => test.Name is
        "Uninstall is disabled-only and removes every immutable version" or
        "Pending uninstall recovery retires authority before same-package reuse")
        .ToArray()
    : allTests;

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
        Console.Error.WriteLine(exception);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;


static async Task<bool> SeedPackage(WidgetCatalog catalog, string path, string id)
{
    await using var input = File.OpenRead(path);
    return await catalog.SeedInitialPackageAsync(input, id);
}

static async Task InitialBundleLifecycle()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    const string id = "widgetrail.firstparty.audio-mixer";
    var archive = CreatePackage(temp.Path, id, "widgetrail.firstparty", "1.0.0");
    Assert.True(await SeedPackage(catalog, archive, id), "Fresh bundle was not installed");
    var widget = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.True(widget.Enabled, "Fresh bundle was not enabled");
    Assert.True(InstalledWidgetAuthority.PublisherId(widget.ActiveVersion).StartsWith("unsigned.", StringComparison.Ordinal),
        "A first-party label granted publisher authority");
    await catalog.SetEnabledAsync(id, false);
    Assert.True(!await SeedPackage(catalog, archive, id), "Repeated bundle seed changed selection");
    Assert.True(!(await catalog.DiscoverAsync()).Widgets.Single().Enabled, "Disabled widget was re-enabled");
    await catalog.UninstallAsync(id);
    var restarted = new WidgetCatalog(catalog.Root);
    Assert.True(!await SeedPackage(restarted, archive, id), "Removed widget was restored by an application restart");
    Assert.Equal(0, (await restarted.DiscoverAsync()).Widgets.Count);
    Assert.True(!await SeedPackage(restarted, CreatePackage(temp.Path, id, "widgetrail.firstparty", "2.0.0"), id),
        "A newer app bundle restored a removed widget");
    await restarted.InstallAsync(archive);
    Assert.Equal(1, (await restarted.DiscoverAsync()).Widgets.Count);
}

static async Task InitialBundleSafety()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    const string id = "dev.test.initial";
    var archive = CreatePackage(temp.Path, id, "dev.test", "1.0.0");
    await catalog.InstallAsync(archive);
    Assert.True(!await SeedPackage(catalog, CreatePackage(temp.Path, id, "dev.test", "2.0.0"), id),
        "Bundle changed a user-installed package");
    var widget = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.Equal(new Version(1, 0, 0), widget.ActiveVersion.Version);
    Assert.True(!widget.Enabled, "Bundle enabled an existing disabled package");
    await Assert.ThrowsAsync<WidgetPackageException>(() => SeedPackage(catalog, archive, "dev.test.different"));
    await Assert.ThrowsAsync<WidgetPackageException>(() => SeedPackage(catalog, archive, "widgetrail.firstparty.settings"));
    Assert.Equal(1, (await catalog.DiscoverAsync()).Widgets.Count);
    var other = new WidgetCatalog(Path.Combine(temp.Path, "parallel"));
    var results = await Task.WhenAll(SeedPackage(other, archive, id), SeedPackage(new WidgetCatalog(other.Root), archive, id));
    Assert.Equal(1, results.Count(installed => installed));
    Assert.Equal(1, (await other.DiscoverAsync()).Widgets.Count);
}

static async Task RetiredHostUtilities()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    foreach (var id in new[] { "widgetrail.firstparty.task-switcher", "widgetrail.firstparty.recent-apps" })
    {
        await catalog.InstallAsync(CreatePackage(temp.Path, id, "widgetrail.firstparty", "1.0.0"));
        await catalog.SetEnabledAsync(id, true);
    }
    const string retained = "dev.test.keep";
    await catalog.InstallAsync(CreatePackage(temp.Path, retained, "dev.test", "1.0.0"));
    var warnings = new List<string>();
    await catalog.RetireHostUtilityPackagesAsync(warnings.Add, CancellationToken.None);
    Assert.Equal(0, warnings.Count);
    Assert.Equal(retained, (await catalog.DiscoverAsync()).Widgets.Single().Id);
    await catalog.RetireHostUtilityPackagesAsync(warnings.Add, CancellationToken.None);
    Assert.True(!await SeedPackage(catalog, CreatePackage(temp.Path, "widgetrail.firstparty.task-switcher", "widgetrail.firstparty", "2.0.0"),
        "widgetrail.firstparty.task-switcher"), "An old initial bundle resurrected the retired widget");
    Assert.Equal(retained, (await catalog.DiscoverAsync()).Widgets.Single().Id);
}

static async Task<InstalledWidgetVersion> RetentionUpdate(WidgetCatalog catalog, string package,
    Func<InstalledWidgetVersion, InstalledWidgetVersion, CancellationToken, Task>? transfer = null,
    CancellationToken token = default)
{
    await using var input = File.OpenRead(package);
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("dev.test.retention")));
    return await catalog.UpdateFromFileAsync(input, hash, WidgetPackageTrustApproval.None,
        (_, _) => Task.CompletedTask, token, transfer ?? ((_, _, _) => Task.CompletedTask));
}

static async Task RetainedUpdateReuse()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    const string id = "dev.test.retention";
    await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "1.0.0"));
    var archive = CreatePackage(temp.Path, id, "dev.test", "2.0.0");
    var saved = await RetentionUpdate(catalog, archive);
    var payload = Path.Combine(saved.InstallPath, "payload", "Widget.dll");
    var stamp = File.GetLastWriteTimeUtc(payload);
    await catalog.RollbackAsync(id);
    var copies = 0;
    using var cancelled = new CancellationTokenSource();
    await Assert.ThrowsAsync<OperationCanceledException>(() => RetentionUpdate(catalog, archive,
        (_, _, _) => { cancelled.Cancel(); cancelled.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, cancelled.Token));
    Assert.Equal(new Version(1, 0, 0), (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version);
    var conflict = CreatePackage(temp.Path, id, "dev.test", "2.0.0", [new("payload/changed.txt", "different package")]);
    var rejected = await Assert.ThrowsAsync<WidgetPackageException>(() => RetentionUpdate(catalog, conflict,
        (_, _, _) => { copies++; return Task.CompletedTask; }));
    Assert.Equal("retained_version_mismatch", rejected.Code);
    Assert.Equal(0, copies);
    Assert.True(!File.Exists(Path.Combine(saved.InstallPath, "payload", "changed.txt")), "Conflicting bytes overwrote the retained version.");
    await catalog.SetEnabledAsync(id, true);
    var reused = await RetentionUpdate(catalog, archive, (_, _, _) => { copies++; return Task.CompletedTask; });
    var current = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.True(current.Enabled && current.ActiveVersion.Version == new Version(2, 0, 0), "Retained update did not preserve enabled state and select the reviewed version.");
    Assert.Equal(saved.ContentDigest, reused.ContentDigest);
    Assert.Equal(saved.InstallPath, reused.InstallPath);
    Assert.Equal(stamp, File.GetLastWriteTimeUtc(payload));
    Assert.Equal(1, copies);
    Assert.Equal(2, current.Versions.Count);
}

static async Task RetentionMalformedReceipts()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    const string id = "dev.test.retention";
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "1.0.0"));
    await RetentionUpdate(catalog, CreatePackage(temp.Path, id, "dev.test", "2.0.0"));
    var path = Path.Combine(root, "retention", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    foreach (var malformed in new[] { "{", "{}", new string('x', 4097),
        "{\"Id\":\"../outside\",\"Incoming\":\"2.0.0\"}",
        "{\"Id\":\"dev.test.retention\",\"Incoming\":\"../outside\"}" })
    {
        await File.WriteAllTextAsync(path, malformed);
        await catalog.SetEnabledAsync(id, false);
        var current = (await catalog.DiscoverAsync()).Widgets.Single();
        Assert.Equal(2, current.Versions.Count);
        Assert.Equal(new Version(2, 0, 0), current.ActiveVersion.Version);
    }
    // A valid install replaces a malformed maintenance receipt safely.
    await RetentionUpdate(catalog, CreatePackage(temp.Path, id, "dev.test", "3.0.0"));
    Assert.SequenceEqual(["3.0.0", "2.0.0"], (await catalog.DiscoverAsync()).Widgets.Single().Versions.Select(item => item.Version.ToString()));
}

static async Task RetentionUpdates()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    const string id = "dev.test.retention";
    await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "1.0.0"));
    await catalog.SetEnabledAsync(id, true);
    for (var major = 2; major <= 12; major++)
    {
        var result = await RetentionUpdate(catalog, CreatePackage(temp.Path, id, "dev.test", $"{major}.0.0"));
        Assert.True(!result.RetentionCleanupPending, "Unlocked retention remained pending.");
        var current = (await catalog.DiscoverAsync()).Widgets.Single();
        Assert.True(current.Enabled, "Retention disabled the widget.");
        Assert.Equal(new Version(major, 0, 0), current.ActiveVersion.Version);
        Assert.SequenceEqual([$"{major}.0.0", $"{major - 1}.0.0"], current.Versions.Select(item => item.Version.ToString()));
    }
    await catalog.SetEnabledAsync(id, false);
    await catalog.RollbackAsync(id);
    Assert.Equal(new Version(11, 0, 0), (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version);
    await catalog.SetActiveVersionAsync(id, new Version(12, 0, 0));
    Assert.Equal(2, (await catalog.DiscoverAsync()).Widgets.Single().Versions.Count);
}

static async Task RetentionFailures()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    const string id = "dev.test.retention";
    var legacy = new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 });
    for (var major = 1; major <= 8; major++)
        await legacy.InstallAsync(CreatePackage(temp.Path, id, "dev.test", $"{major}.0.0"));
    await legacy.SetActiveVersionAsync(id, new Version(3, 0, 0));
    await legacy.SetEnabledAsync(id, true);
    var catalog = new WidgetCatalog(root);
    var incoming = CreatePackage(temp.Path, id, "dev.test", "9.0.0");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RetentionUpdate(catalog, incoming, token: cancelled.Token));
    }
    Assert.Equal(8, (await catalog.DiscoverAsync()).Widgets.Single().Versions.Count);
    await Assert.ThrowsAsync<IOException>(() => RetentionUpdate(catalog, incoming,
        (_, _, _) => throw new IOException("simulated settings failure")));
    var failed = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.True(failed.Enabled && failed.ActiveVersion.Version == new Version(3, 0, 0), "Failed update changed selection or enabled state.");
    Assert.SequenceEqual(["8.0.0", "3.0.0"], failed.Versions.Select(item => item.Version.ToString()));
    await RetentionUpdate(catalog, incoming);
    Assert.SequenceEqual(["9.0.0", "3.0.0"], (await catalog.DiscoverAsync()).Widgets.Single().Versions.Select(item => item.Version.ToString()));
}

static async Task RetentionRecovery()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    const string id = "dev.test.retention";
    var catalog = new WidgetCatalog(root);
    var first = await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "1.0.0"));
    await RetentionUpdate(catalog, CreatePackage(temp.Path, id, "dev.test", "2.0.0"));
    // Windows prevents retirement while a running generation pins its files.
    if (OperatingSystem.IsWindows())
    {
        using var lease = File.Open(Path.Combine(first.InstallPath, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var updated = await RetentionUpdate(catalog, CreatePackage(temp.Path, id, "dev.test", "3.0.0"));
        Assert.True(updated.RetentionCleanupPending, "Locked generation was not reported pending.");
        Assert.Equal(new Version(3, 0, 0), (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version);
    }
    else await RetentionUpdate(catalog, CreatePackage(temp.Path, id, "dev.test", "3.0.0"));
    await catalog.SetEnabledAsync(id, false);
    Assert.SequenceEqual(["3.0.0", "2.0.0"], (await catalog.DiscoverAsync()).Widgets.Single().Versions.Select(item => item.Version.ToString()));
    // Simulate process exit after publication, before selection and cleanup.
    await new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 })
        .InstallAsync(CreatePackage(temp.Path, id, "dev.test", "4.0.0"));
    var receipt = Path.Combine(root, "retention", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".json");
    await File.WriteAllTextAsync(receipt, "{\"Id\":\"dev.test.retention\",\"Incoming\":\"4.0.0\",\"Previous\":\"3.0.0\"}");
    await new WidgetCatalog(root).SetEnabledAsync(id, false);
    var recovered = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.Equal(new Version(3, 0, 0), recovered.ActiveVersion.Version);
    Assert.SequenceEqual(["4.0.0", "3.0.0"], recovered.Versions.Select(item => item.Version.ToString()));
    Assert.True(!File.Exists(receipt), "Completed recovery retained its receipt.");
}

static async Task StartupActiveSelections()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var old = await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.first", "dev.test", "1.0.0"));
    var current = await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.first", "dev.test", "2.0.0"));
    var requested = await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.requested", "dev.test", "1.0.0"));
    var disabled = await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.disabled", "dev.test", "1.0.0"));
    await catalog.SetActiveVersionAsync(current.Id, current.Version);
    await catalog.SetEnabledAsync(current.Id, true);
    await catalog.SetEnabledAsync(requested.Id, true);
    File.Delete(Path.Combine(old.InstallPath, "manifest.json"));
    File.Delete(Path.Combine(disabled.InstallPath, "manifest.json"));
    var inventory = await catalog.InspectInventoryAsync();
    Assert.Equal(3, inventory.Widgets.Count);
    Assert.Equal(2, inventory.Widgets.Single(entry => entry.Id == current.Id).Versions.Count);
    Assert.Equal(disabled.Id, inventory.Widgets.Single(entry => entry.Id == disabled.Id).Name);
    var admitted = new List<CatalogWidget>();
    await foreach (var widget in catalog.DiscoverEnabledAsync([requested.Id])) admitted.Add(widget);
    Assert.SequenceEqual(new[] { requested.Id, current.Id }, admitted.Select(widget => widget.Id));
    Assert.Equal(current.Version, admitted[1].ActiveVersion.Version);
    Assert.True(admitted.All(widget => widget.Versions.Count == 1), "Startup hashed retained versions.");
    Assert.True(admitted.All(widget => widget.ActiveVersion.VerifiedFiles.Count > 0), "Startup returned an unverified executable descriptor.");
    await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.DiscoverAsync());
    File.AppendAllText(Path.Combine(requested.InstallPath, "manifest.json"), " ");
    await Assert.ThrowsAsync<WidgetPackageException>(async () => {
        await foreach (var _ in catalog.DiscoverEnabledAsync([requested.Id])) { }
    });
    // Inventory remains descriptive even when selected bytes fail validation.
    Assert.Equal(3, (await catalog.InspectInventoryAsync()).Widgets.Count);
}

static async Task StartupSelectionRaces()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var first = await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.first", "dev.test", "1.0.0"));
    var last = await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.last", "dev.test", "1.0.0"));
    await catalog.SetEnabledAsync(first.Id, true); await catalog.SetEnabledAsync(last.Id, true);
    await using (var stream = catalog.DiscoverEnabledAsync([first.Id]).GetAsyncEnumerator())
    {
        Assert.True(await stream.MoveNextAsync(), "Requested widget was not yielded.");
        await catalog.SetEnabledAsync(last.Id, false);
        var error = await Assert.ThrowsAsync<WidgetPackageException>(async () => { await stream.MoveNextAsync(); });
        Assert.Equal("catalog_changed", error.Code);
    }
    using var cancel = new CancellationTokenSource();
    await using var cancelled = catalog.DiscoverEnabledAsync(cancellationToken: cancel.Token).GetAsyncEnumerator();
    Assert.True(await cancelled.MoveNextAsync(), "Enabled widget missing before cancellation.");
    cancel.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(async () => { await cancelled.MoveNextAsync(); });
}

static async Task ReviewedSettingsContinuity()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    const string id = "dev.test.settings";
    var old = await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "1.0.0"));
    await catalog.SetEnabledAsync(id, true);
    var stateRoot = Path.Combine(temp.Path, "state");
    var store = new PrivateStateStore(stateRoot);
    var oldIdentity = new BrokerWidgetIdentity(id, InstalledWidgetAuthority.PublisherId(old), "old");
    var json = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"Enabled\":true,\"SetupSeen\":true}"));
    await store.WriteAsync(oldIdentity, new(json, 0), default);
    var package = CreatePackage(temp.Path, id, "dev.test", "2.0.0");
    var target = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    await using (var rejected = File.OpenRead(package))
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.UpdateFromFileAsync(rejected, target,
            WidgetPackageTrustApproval.None, (_, _) => throw new InvalidOperationException("not approved"),
            default, new WidgetSettingsTransfer(stateRoot, new ConsentStore(Path.Combine(temp.Path, "consent"))).CopyAsync));
    Assert.Equal(1, Directory.GetFiles(stateRoot, "*.json").Length);
    await using (var failedSettings = File.OpenRead(package))
        await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.UpdateFromFileAsync(failedSettings, target,
            WidgetPackageTrustApproval.None, (_, _) => Task.CompletedTask, default,
            (_, _, _) => throw new WidgetPackageException("settings_transfer_failed", "fixture")));
    Assert.Equal(1, (await catalog.DiscoverAsync()).Widgets.Single().Versions.Count);
    Assert.True((await catalog.DiscoverAsync()).Widgets.Single().Enabled, "Failed settings preparation disabled the widget.");
    await using var input = File.OpenRead(package);
    var next = await catalog.UpdateFromFileAsync(input, target, WidgetPackageTrustApproval.None,
        (_, _) => Task.CompletedTask, default, new WidgetSettingsTransfer(stateRoot, new ConsentStore(Path.Combine(temp.Path, "consent"))).CopyAsync);
    var nextIdentity = new BrokerWidgetIdentity(id, InstalledWidgetAuthority.PublisherId(next), "new");
    Assert.True(nextIdentity.PublisherId != oldIdentity.PublisherId, "Update reused security authority.");
    Assert.Equal(json, (await store.ReadAsync(nextIdentity, default)).CanonicalJsonBase64);
    Assert.Equal(json, (await store.ReadAsync(oldIdentity, default)).CanonicalJsonBase64);
    Assert.True((await catalog.DiscoverAsync()).Widgets.Single().Enabled, "Update disabled the widget.");
    // Raw install has no preference-transfer grant, even for the same manifest publisher/ID.
    await catalog.SetEnabledAsync(id, false);
    var raw = await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "3.0.0"));
    Assert.True(!(await store.ReadAsync(new(id, InstalledWidgetAuthority.PublisherId(raw), "raw"), default)).Exists,
        "Unreviewed install inherited settings.");
}

static async Task ReviewedPermissionContinuity()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    var stateRoot = Path.Combine(temp.Path, "state");
    var continuity = new AppLibraryContinuityStore(consent.RootDirectory);
    var transfer = new WidgetSettingsTransfer(stateRoot, consent);
    const string id = "dev.test.continuity";
    var read = PlatformCapabilities.AppLibraryReadV1;
    var launch = PlatformCapabilities.AppLibraryLaunchV1;
    var added = PlatformCapabilities.AppRunningReadV1;
    var old = await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "1.0.0", permissions: [read, launch]));
    var source = new BrokerWidgetIdentity(id, InstalledWidgetAuthority.PublisherId(old), "source");
    await consent.SetDecisionAsync(source, read, ConsentDecision.Grant);
    await consent.SetDecisionAsync(source, launch, ConsentDecision.Deny);
    var json = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"selected\":\"saved-fixture\"}"));
    await new PrivateStateStore(stateRoot).WriteAsync(source, new(json, 0), default);
    var issuer = new AppLibrarySavedIdIssuer(new byte[32]);
    var savedId = issuer.Issue(source, "steam:123");
    var previous = old;
    for (var version = 2; version <= 4; version++)
    {
        var archive = CreatePackage(temp.Path, id, "dev.test", $"{version}.0.0", permissions: [read, launch, added]);
        await using var input = File.OpenRead(archive);
        var next = await catalog.UpdateFromFileAsync(input, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))),
            WidgetPackageTrustApproval.None, (_, _) => Task.CompletedTask, default, transfer.CopyAsync);
        var target = new BrokerWidgetIdentity(id, InstalledWidgetAuthority.PublisherId(next), "target");
        Assert.Equal<ConsentDecision?>(ConsentDecision.Grant, await consent.GetDecisionAsync(target, read));
        Assert.Equal<ConsentDecision?>(ConsentDecision.Deny, await consent.GetDecisionAsync(target, launch));
        Assert.Equal<ConsentDecision?>(null, await consent.GetDecisionAsync(target, added));
        Assert.Equal(savedId, issuer.Issue(await continuity.ResolveAsync(target, default), "steam:123"));
        Assert.Equal(json, (await new PrivateStateStore(stateRoot).ReadAsync(target, default)).CanonicalJsonBase64);
        Assert.True(target.PublisherId != InstalledWidgetAuthority.PublisherId(previous), "Update reused security identity.");
        previous = next;
    }
    // Raw installation and an unapproved update cannot inherit this relationship.
    var raw = await catalog.InstallAsync(CreatePackage(temp.Path, id, "dev.test", "5.0.0", permissions: [read]));
    var rawIdentity = new BrokerWidgetIdentity(id, InstalledWidgetAuthority.PublisherId(raw), "raw");
    Assert.Equal<ConsentDecision?>(null, await consent.GetDecisionAsync(rawIdentity, read));
    Assert.Equal(rawIdentity, await continuity.ResolveAsync(rawIdentity, default));
    // Failure to read consent blocks publication without selecting the replacement.
    await File.WriteAllTextAsync(consent.DocumentFile, "{broken");
    await using var failed = File.OpenRead(CreatePackage(temp.Path, id, "dev.test", "6.0.0", permissions: [read]));
    await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.UpdateFromFileAsync(failed,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))), WidgetPackageTrustApproval.None,
        (_, _) => Task.CompletedTask, default, transfer.CopyAsync));
    Assert.Equal(previous.Version, (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version);
}

static async Task UpdateContinuitySafety()
{
    using var temp = new TemporaryDirectory();
    var consent = new ConsentStore(Path.Combine(temp.Path, "consent"));
    var source = new BrokerWidgetIdentity("dev.test.continuity", "unsigned." + new string('a', 64), "source");
    var target = source with { PublisherId = "unsigned." + new string('b', 64) };
    var read = PlatformCapabilities.AppLibraryReadV1;
    var launch = PlatformCapabilities.AppLibraryLaunchV1;
    await consent.SetDecisionsAsync(source, [read, launch], ConsentDecision.Grant);
    await consent.SetDecisionAsync(target, read, ConsentDecision.Deny);
    await consent.CopyUpdateDecisionsAsync(source, target, [read], default);
    Assert.Equal<ConsentDecision?>(ConsentDecision.Deny, await consent.GetDecisionAsync(target, read));
    Assert.Equal<ConsentDecision?>(null, await consent.GetDecisionAsync(target, launch));
    Assert.Equal<ConsentDecision?>(ConsentDecision.Grant, await consent.GetDecisionAsync(source, read));
    var other = target with { PackageId = "dev.test.other" };
    await Assert.ThrowsAsync<BrokerException>(() => consent.CopyUpdateDecisionsAsync(source, other, [read], default));
    var scopes = new AppLibraryContinuityStore(consent.RootDirectory);
    await Assert.ThrowsAsync<BrokerException>(() => scopes.KeepAsync(source, other, default));
    // A retained destination with its own settings predates continuity metadata.
    await scopes.KeepAsync(source, target, default, destinationHasState: true);
    Assert.Equal(target, await scopes.ResolveAsync(target, default));
    await scopes.KeepAsync(source, target, default);
    Assert.Equal(target, await scopes.ResolveAsync(target, default));
    var fresh = target with { PublisherId = "unsigned." + new string('c', 64) };
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => consent.CopyUpdateDecisionsAsync(source, fresh, [read], cancellation.Token));
    await Assert.ThrowsAsync<OperationCanceledException>(() => scopes.KeepAsync(source, fresh, cancellation.Token));
    Assert.Equal<ConsentDecision?>(null, await consent.GetDecisionAsync(fresh, read));
    Assert.Equal(fresh, await scopes.ResolveAsync(fresh, default));
    // Independent store instances must respect a destination denial racing the update.
    await Task.WhenAll(consent.CopyUpdateDecisionsAsync(source, fresh, [read], default),
        new ConsentStore(consent.RootDirectory).SetDecisionAsync(fresh, read, ConsentDecision.Deny));
    Assert.Equal<ConsentDecision?>(ConsentDecision.Deny, await consent.GetDecisionAsync(fresh, read));
}

static async Task SettingsTransferSafety()
{
    using var temp = new TemporaryDirectory();
    var store = new PrivateStateStore(temp.Path);
    var source = new BrokerWidgetIdentity("dev.test.widget", "unsigned." + new string('a', 64), "one");
    var dest = source with { PublisherId = "unsigned." + new string('b', 64) };
    var value = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"enabled\":true}"));
    await store.WriteAsync(source, new(value, 0), default);
    await store.ClearAsync(dest, new(0), default);
    await store.CopyIfAbsentAsync(source, dest, default);
    var cleared = await store.ReadAsync(dest, default);
    Assert.True(!cleared.Exists && cleared.Revision == 1, "Transfer resurrected cleared state.");
    var existing = source with { PublisherId = "unsigned." + new string('c', 64) };
    var otherValue = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"enabled\":false}"));
    await store.WriteAsync(existing, new(otherValue, 0), default);
    await store.CopyIfAbsentAsync(source, existing, default);
    Assert.Equal(otherValue, (await store.ReadAsync(existing, default)).CanonicalJsonBase64);
    var otherWidget = dest with { PackageId = "dev.test.other" };
    await Assert.ThrowsAsync<BrokerException>(() => store.CopyIfAbsentAsync(source, otherWidget, default));
    var fresh = source with { PublisherId = "unsigned." + new string('d', 64) };
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() => store.CopyIfAbsentAsync(source, fresh, cancelled.Token));
    Assert.True(!(await store.ReadAsync(fresh, default)).Exists, "Cancelled transfer wrote state.");
    // Clearing the source is propagated without resurrecting its older preferences.
    await store.ClearAsync(source, new(1), default);
    await store.CopyIfAbsentAsync(source, fresh, default);
    Assert.True(!(await store.ReadAsync(fresh, default)).Exists, "Source clear marker was ignored.");
    var corrupt = source with { PublisherId = "unsigned." + new string('e', 64) };
    var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(corrupt.PublisherId + "\0" + corrupt.PackageId))).ToLowerInvariant();
    var corruptPath = Path.Combine(temp.Path, key + ".json");
    await File.WriteAllTextAsync(corruptPath, "bad data");
    await Assert.ThrowsAsync<BrokerException>(() => store.CopyIfAbsentAsync(source, corrupt, default));
    Assert.Equal("bad data", await File.ReadAllTextAsync(corruptPath));
}

static async Task InstallAndDiscover()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    foreach (var package in new[]
    {
        CreatePackage(temp.Path, "dev.test.zeta", "dev.test", "1.0.0"),
        CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"),
        CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "2.0.0"),
    })
        await catalog.InstallAsync(package);

    var snapshot = await catalog.DiscoverAsync();
    Assert.SequenceEqual(["dev.test.alpha", "dev.test.zeta"], snapshot.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual(["2.0.0", "1.0.0"], snapshot.Widgets[0].Versions.Select(item => item.Version.ToString()));
    Assert.Equal("1.0.0", snapshot.Widgets[0].ActiveVersion.Version.ToString());
    Assert.True(snapshot.Widgets[0].Versions.Any(version => version.Version == new Version(2, 0, 0)),
        "The installed update disappeared while the reviewed version remained pinned.");
    Assert.True(snapshot.Widgets.All(widget => !widget.Enabled),
        "Newly installed widget IDs must remain disabled until the user explicitly enables them.");
}

static async Task StatePersists()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"));
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.beta", "dev.test", "1.0.0"));

    await catalog.SetEnabledAsync("dev.test.alpha", true);
    await catalog.SetOrderAsync(["dev.test.beta"]);
    var reloaded = await new WidgetCatalog(root).DiscoverAsync();
    Assert.SequenceEqual(["dev.test.beta", "dev.test.alpha"], reloaded.Widgets.Select(widget => widget.Id));
    Assert.True(!reloaded.Widgets[0].Enabled, "Disabled state was not persisted.");
    Assert.True(reloaded.Widgets[1].Enabled, "Explicitly enabled state was not persisted.");
    Assert.True(!Directory.EnumerateFiles(root, ".catalog-state.*.tmp").Any(), "Atomic state temporary file leaked.");
}

static async Task VersionPinningAndMigration()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"));
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "2.0.0"));

    await File.WriteAllTextAsync(Path.Combine(root, "catalog-state.json"), """
        {
          "version": 1,
          "widgets": [
            { "id": "dev.test.alpha", "enabled": false, "order": 0 }
          ]
        }
        """);
    Assert.Equal("2.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());

    await catalog.SetActiveVersionAsync("dev.test.alpha", new Version(1, 0, 0));
    var pinned = (await new WidgetCatalog(root).DiscoverAsync()).Widgets.Single();
    Assert.Equal("1.0.0", pinned.ActiveVersion.Version.ToString());
    await catalog.SetOrderAsync(["dev.test.alpha"]);
    Assert.Equal("1.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());
    var migratedState = await File.ReadAllTextAsync(Path.Combine(root, "catalog-state.json"));
    Assert.True(migratedState.Contains("\"version\": 2", StringComparison.Ordinal),
        "The first mutation did not migrate legacy state to schema version 2.");
    Assert.True(migratedState.Contains("\"activeVersion\": \"1.0.0\"", StringComparison.Ordinal),
        "The selected version was not persisted.");

    await catalog.SetEnabledAsync("dev.test.alpha", true);
    var enabled = await Assert.ThrowsAsync<WidgetPackageException>(
        () => catalog.SetActiveVersionAsync("dev.test.alpha", new Version(2, 0, 0)));
    Assert.Equal("widget_enabled", enabled.Code);
    Assert.Equal("1.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());

    await catalog.SetEnabledAsync("dev.test.alpha", false);
    await catalog.SetActiveVersionAsync("dev.test.alpha", new Version(2, 0, 0));
    Assert.Equal("2.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());
}

static async Task UpdatePreparationPinsReviewedVersion()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.update", "dev.test", "1.0.0"));
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.update", "dev.test", "2.0.0"));
    Assert.Equal("1.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());

    await catalog.SetEnabledAsync("dev.test.update", true);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(
        () => catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.update", "dev.test", "3.0.0")));
    Assert.Equal("widget_enabled", exception.Code);
    Assert.Equal("1.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());
}

static async Task ConcurrentFirstInstallsAreSafe()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var firstCatalog = new WidgetCatalog(root);
    var secondCatalog = new WidgetCatalog(root);
    var firstTask = firstCatalog.InstallAsync(
        CreatePackage(temp.Path, "dev.test.concurrent-install", "dev.test", "1.0.0"));
    var secondTask = secondCatalog.InstallAsync(
        CreatePackage(temp.Path, "dev.test.concurrent-install", "dev.test", "2.0.0"));
    var firstCompleted = await Task.WhenAny(firstTask, secondTask);
    var reviewedVersion = (await firstCompleted).Version;
    await Task.WhenAll(firstTask, secondTask);

    await new WidgetCatalog(root).SetEnabledAsync("dev.test.concurrent-install", true);
    var widget = (await new WidgetCatalog(root).DiscoverAsync()).Widgets.Single();
    Assert.True(widget.Enabled, "Concurrent install fixture did not enable after publication.");
    Assert.Equal(reviewedVersion, widget.ActiveVersion.Version);
    Assert.Equal(2, widget.Versions.Count);
}

static async Task ConcurrentRollbacksAreLinearizable()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 });
    foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" })
        await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.concurrent-rollback", "dev.test", version));
    await catalog.SetActiveVersionAsync("dev.test.concurrent-rollback", new Version(3, 0, 0));

    var changes = await Task.WhenAll(
        new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 }).RollbackAsync("dev.test.concurrent-rollback"),
        new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 }).RollbackAsync("dev.test.concurrent-rollback"));
    Assert.SequenceEqual(["2.0.0", "3.0.0"],
        changes.Select(change => change.PreviousVersion.ToString()).Order(StringComparer.Ordinal));
    Assert.SequenceEqual(["1.0.0", "2.0.0"],
        changes.Select(change => change.SelectedVersion.ToString()).Order(StringComparer.Ordinal));
    Assert.Equal("1.0.0", (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion.Version.ToString());
}

static async Task MissingPinnedVersionFailsClosed()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"));
    await File.WriteAllTextAsync(Path.Combine(root, "catalog-state.json"), """
        {
          "version": 2,
          "widgets": [
            { "id": "dev.test.alpha", "enabled": false, "order": 0, "activeVersion": "9.0.0" }
          ]
        }
        """);

    var exception = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.DiscoverAsync());
    Assert.Equal("active_version_missing", exception.Code);

    var wrongRepair = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.InstallAsync(
        CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "8.0.0")));
    Assert.Equal("active_version_missing", wrongRepair.Code);
    Assert.True(!Directory.Exists(Path.Combine(root, "packages", "dev.test.alpha", "8.0.0")),
        "A non-pinned repair version was published.");
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "9.0.0"));
    var repaired = (await catalog.DiscoverAsync()).Widgets.Single();
    Assert.Equal("9.0.0", repaired.ActiveVersion.Version.ToString());
    Assert.True(!repaired.Enabled, "Repairing a missing exact pin must force the widget disabled.");

    var totalRoot = Path.Combine(temp.Path, "total-catalog");
    var totalCatalog = new WidgetCatalog(totalRoot);
    var totalInstalled = await totalCatalog.InstallAsync(
        CreatePackage(temp.Path, "dev.test.total", "dev.test", "1.0.0"));
    await totalCatalog.SetActiveVersionAsync("dev.test.total", new Version(1, 0, 0));
    Directory.Delete(totalInstalled.InstallPath, recursive: true);
    Assert.Equal("active_version_missing",
        (await Assert.ThrowsAsync<WidgetPackageException>(() => totalCatalog.DiscoverAsync())).Code);
    await totalCatalog.InstallAsync(CreatePackage(temp.Path, "dev.test.total", "dev.test", "1.0.0"));
    var totalRepaired = (await totalCatalog.DiscoverAsync()).Widgets.Single();
    Assert.Equal("1.0.0", totalRepaired.ActiveVersion.Version.ToString());
    Assert.True(!totalRepaired.Enabled, "Total-loss pin repair did not remain disabled.");
}

static async Task UninstallRemovesAllVersions()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 });
    foreach (var version in new[] { "1.0.0", "2.0.0" })
        await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.remove", "dev.test", version));
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.keep", "dev.test", "1.0.0"));
    await catalog.SetOrderAsync(["dev.test.remove", "dev.test.keep"]);
    await catalog.SetEnabledAsync("dev.test.remove", true);

    var enabled = await Assert.ThrowsAsync<WidgetPackageException>(
        () => catalog.UninstallAsync("dev.test.remove"));
    Assert.Equal("widget_enabled", enabled.Code);
    Assert.True(Directory.Exists(Path.Combine(root, "packages", "dev.test.remove")),
        "Rejected uninstall mutated the package tree.");

    await catalog.SetEnabledAsync("dev.test.remove", false);
    var inspected = await catalog.InspectUninstallAsync("dev.test.remove");
    Assert.Equal("dev.test.remove", inspected.Id);
    Assert.Equal(2, inspected.VersionCount);
    Assert.True(!inspected.Enabled, "Disabled uninstall inspection was enabled.");
    Assert.Equal(64, inspected.ConfirmationToken.Length);
    var forgedPublisher = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        catalog.UninstallConfirmedAsync(
            inspected.Id, "dev.publisher.forged", inspected.ActiveVersion,
            inspected.ConfirmationToken));
    Assert.Equal("confirmation_stale", forgedPublisher.Code);
    Assert.True(Directory.Exists(Path.Combine(root, "packages", "dev.test.remove")),
        "Forged publisher uninstall mutated the package tree.");
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.remove", "dev.test", "3.0.0"));
    var stale = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        catalog.UninstallConfirmedAsync(
            inspected.Id, inspected.PublisherId, inspected.ActiveVersion,
            inspected.ConfirmationToken));
    Assert.Equal("confirmation_stale", stale.Code);
    Assert.Equal(3, (await catalog.DiscoverAsync()).Widgets
        .Single(widget => widget.Id == "dev.test.remove").Versions.Count);
    var current = await catalog.InspectUninstallAsync("dev.test.remove");
    var profileLifecycle = new WidgetRail.PlatformSettings.BrowserProfileLifecycleStore(Path.GetDirectoryName(root)!);
    var browserProfile = await profileLifecycle.RegisterAsync(current.Id, "persistent-uninstall-test", "persistent-uninstall-test.json");
    var privateStore = new PrivateStateStore(Path.Combine(Path.GetDirectoryName(root)!, "widget-state"));
    var dataIdentity = new BrokerWidgetIdentity(current.Id, current.PublisherId, "uninstall-test");
    await privateStore.WriteAsync(dataIdentity, new(Convert.ToBase64String("{\"version\":1}"u8), null), default);
    var removed = await catalog.UninstallConfirmedAsync(
        current.Id, current.PublisherId, current.ActiveVersion, current.ConfirmationToken);
    Assert.True(!(await privateStore.ReadAsync(dataIdentity, default)).Exists, "Uninstall retained owned private data.");
    var browserRemoval = await profileLifecycle.ReadAsync(current.Id);
    Assert.True(browserRemoval.Removed && browserRemoval.Pending.Contains(browserProfile!), "Uninstall lost browser cleanup evidence.");
    Assert.Equal("dev.test.remove", removed.Id);
    Assert.True(!removed.CleanupPending, "Unlocked package cleanup unexpectedly remained pending.");
    Assert.SequenceEqual(["3.0.0", "2.0.0", "1.0.0"],
        removed.RemovedVersions.Select(version => version.ToString()));
    Assert.True(!Directory.Exists(Path.Combine(root, "packages", "dev.test.remove")),
        "Uninstall retained immutable package versions.");
    Assert.True(!Directory.EnumerateDirectories(Path.Combine(root, "staging"), ".uninstall-*").Any(),
        "Uninstall retained a retired package tree.");
    var remaining = (await new WidgetCatalog(root, new WidgetCatalogOptions { RetainedVersionsPerWidget = 8 }).DiscoverAsync()).Widgets.Single();
    Assert.Equal("dev.test.keep", remaining.Id);
    Assert.Equal(0, remaining.Order);
    var state = await File.ReadAllTextAsync(Path.Combine(root, "catalog-state.json"));
    Assert.True(!state.Contains("dev.test.remove", StringComparison.Ordinal),
        "Uninstall retained catalog state for the removed widget.");
    await Assert.ThrowsAsync<KeyNotFoundException>(() => catalog.UninstallAsync("dev.test.remove"));
}

static async Task PendingUninstallRecoveryIsCommitBound()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var participant = new TestUninstallAuthorityParticipant(root)
        { FailBeforeCommit = true };
    var catalog = new WidgetCatalog(
        root, null, TimeProvider.System, participant);
    await catalog.InstallAsync(CreatePackage(
        temp.Path, "dev.test.pending", "dev.test", "1.0.0"));
    var inspection = await catalog.InspectUninstallAsync("dev.test.pending");
    var failed = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        catalog.UninstallConfirmedAsync(
            inspection.Id, inspection.PublisherId,
            inspection.ActiveVersion, inspection.ConfirmationToken));
    Assert.Equal("registration_cleanup_failed", failed.Code);
    Assert.Equal("dev.test.pending",
        (await catalog.DiscoverAsync()).Widgets.Single().Id);
    Assert.True(Directory.Exists(Path.Combine(
        root, "packages", "dev.test.pending")),
        "Precommit registration failure did not roll the package directory back.");

    participant.FailBeforeCommit = false;
    inspection = await catalog.InspectUninstallAsync("dev.test.pending");
    var removed = await catalog.UninstallConfirmedAsync(
        inspection.Id, inspection.PublisherId,
        inspection.ActiveVersion, inspection.ConfirmationToken);
    Assert.True(!removed.CleanupPending,
        "Committed pending-uninstall fixture unexpectedly retained cleanup.");
    Assert.Equal(2, participant.Calls);
    Assert.True(!Directory.EnumerateFileSystemEntries(
            Path.Combine(root, "staging")).Any(),
        "Committed uninstall retained marker or staged package evidence.");

    var recoveryRoot = Path.Combine(temp.Path, "recovery-catalog");
    var legacy = new WidgetCatalog(recoveryRoot);
    var archiveV1 = CreatePackage(
        temp.Path, "dev.test.recovery", "dev.test", "1.0.0");
    await legacy.InstallAsync(archiveV1);
    var recoveredWidget = (await legacy.DiscoverAsync()).Widgets.Single();
    await File.WriteAllTextAsync(
        Path.Combine(recoveryRoot, "catalog-state.json"),
        "{\"version\":2,\"widgets\":[]}");
    var staging = Path.Combine(recoveryRoot, "staging");
    Directory.CreateDirectory(staging);
    var retired = Path.Combine(staging, ".uninstall-recovery");
    var marker = PendingWidgetUninstallStore.MarkerPath(retired);
    await PendingWidgetUninstallStore.WriteAsync(
        marker,
        new PendingWidgetUninstall(
            1,
            recoveredWidget.Id,
            recoveredWidget.Versions.Select(InstalledWidgetAuthority.PublisherId)
                .ToArray()),
        CancellationToken.None);
    Directory.Move(
        Path.Combine(recoveryRoot, "packages", recoveredWidget.Id), retired);

    var archiveV2 = CreatePackage(
        temp.Path, recoveredWidget.Id, "dev.test", "2.0.0");
    var blocked = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        new WidgetCatalog(recoveryRoot).InstallAsync(archiveV2));
    Assert.Equal("pending_uninstall_cleanup", blocked.Code);
    Assert.True(File.Exists(marker) && Directory.Exists(retired),
        "A caller without a cleanup participant erased pending evidence.");

    var recoveryParticipant = new TestUninstallAuthorityParticipant(recoveryRoot);
    var recoveryCatalog = new WidgetCatalog(
        recoveryRoot, null, TimeProvider.System, recoveryParticipant);
    var fresh = await recoveryCatalog.InstallAsync(archiveV2);
    Assert.Equal("2.0.0", fresh.Version.ToString());
    Assert.Equal(1, recoveryParticipant.Calls);
    Assert.True(!File.Exists(marker) && !Directory.Exists(retired),
        "Pending recovery did not retire marker and staged content.");

    var preStageRoot = Path.Combine(temp.Path, "prestage-catalog");
    var preStageCatalog = new WidgetCatalog(preStageRoot);
    await preStageCatalog.InstallAsync(CreatePackage(
        temp.Path, "dev.test.prestage", "dev.test", "1.0.0"));
    var preStageWidget = (await preStageCatalog.DiscoverAsync()).Widgets.Single();
    var preStageDirectory = Path.Combine(
        preStageRoot, "staging", ".uninstall-prestage");
    Directory.CreateDirectory(Path.GetDirectoryName(preStageDirectory)!);
    var preStageMarker = PendingWidgetUninstallStore.MarkerPath(preStageDirectory);
    await PendingWidgetUninstallStore.WriteAsync(
        preStageMarker,
        new PendingWidgetUninstall(
            1,
            preStageWidget.Id,
            preStageWidget.Versions.Select(InstalledWidgetAuthority.PublisherId)
                .ToArray()),
        CancellationToken.None);
    var preStageRecovery = new WidgetCatalog(
        preStageRoot, null, TimeProvider.System,
        new TestUninstallAuthorityParticipant(preStageRoot));
    await preStageRecovery.InstallAsync(CreatePackage(
        temp.Path, "dev.test.unrelated", "dev.test", "1.0.0"));
    Assert.True(!File.Exists(preStageMarker),
        "Pre-stage recovery retained an abandoned pending marker.");
    Assert.True(Directory.Exists(Path.Combine(
        preStageRoot, "packages", preStageWidget.Id)),
        "Pre-stage recovery removed the still-installed source package.");

    var pendingRoot = Path.Combine(temp.Path, "postcommit-catalog");
    var pendingParticipant = new TestUninstallAuthorityParticipant(pendingRoot)
        { CleanupPending = true };
    var pendingCatalog = new WidgetCatalog(
        pendingRoot, null, TimeProvider.System, pendingParticipant);
    await pendingCatalog.InstallAsync(CreatePackage(
        temp.Path, "dev.test.postcommit", "dev.test", "1.0.0"));
    var pendingInspection = await pendingCatalog.InspectUninstallAsync(
        "dev.test.postcommit");
    var pendingResult = await pendingCatalog.UninstallConfirmedAsync(
        pendingInspection.Id, pendingInspection.PublisherId,
        pendingInspection.ActiveVersion, pendingInspection.ConfirmationToken);
    Assert.True(pendingResult.CleanupPending,
        "Postcommit provider cleanup did not retain CleanupPending.");
    Assert.Equal(1, Directory.EnumerateFiles(
        Path.Combine(pendingRoot, "staging"),
        ".uninstall-*.pending.json", SearchOption.TopDirectoryOnly).Count());
    pendingParticipant.CleanupPending = false;
    await pendingCatalog.InstallAsync(CreatePackage(
        temp.Path, "dev.test.after-pending", "dev.test", "1.0.0"));
    Assert.Equal(0, Directory.EnumerateFiles(
        Path.Combine(pendingRoot, "staging"),
        ".uninstall-*.pending.json", SearchOption.TopDirectoryOnly).Count());
}

static async Task ConcurrentStatePersists()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var first = new WidgetCatalog(root);
    await first.InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"));
    await first.InstallAsync(CreatePackage(temp.Path, "dev.test.beta", "dev.test", "1.0.0"));

    var second = new WidgetCatalog(root);
    await Task.WhenAll(
        first.SetEnabledAsync("dev.test.alpha", true),
        second.SetEnabledAsync("dev.test.beta", true));

    var reloaded = await new WidgetCatalog(root).DiscoverAsync();
    Assert.True(reloaded.Widgets.Single(widget => widget.Id == "dev.test.alpha").Enabled,
        "The first concurrent state mutation was lost.");
    Assert.True(reloaded.Widgets.Single(widget => widget.Id == "dev.test.beta").Enabled,
        "The second concurrent state mutation was lost.");
}

static async Task ConcurrentDiscoveryAndMutation()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var writer = new WidgetCatalog(root);
    var reader = new WidgetCatalog(root);
    await writer.InstallAsync(CreatePackage(temp.Path, "dev.test.read-race", "dev.test", "1.0.0"));

    var mutations = Task.Run(async () =>
    {
        for (var index = 0; index < 40; index++)
            await writer.SetEnabledAsync("dev.test.read-race", index % 2 == 0);
    });
    var discoveries = Task.Run(async () =>
    {
        for (var index = 0; index < 160; index++)
            Assert.Equal(1, (await reader.DiscoverAsync()).Widgets.Count);
    });
    await Task.WhenAll(mutations, discoveries);
}

static async Task ReinstallDoesNotOverwrite()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var package = CreatePackage(temp.Path, "dev.test.clock", "dev.test", "1.0.0");
    var first = await catalog.InstallAsync(package);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.InstallAsync(package));
    Assert.Equal("version_already_installed", exception.Code);
    Assert.True(File.Exists(Path.Combine(first.InstallPath, "manifest.json")), "Original install was damaged.");
    var staging = Path.Combine(catalog.Root, "staging");
    Assert.True(!Directory.EnumerateFileSystemEntries(staging).Any(), "Failed install left staged content.");
}

static async Task PublicInstallsEnforceUpdatePolicy()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.guarded", "dev.test", "1.0.0"));
    await catalog.SetEnabledAsync("dev.test.guarded", true);

    var pathException = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.guarded", "dev.test", "2.0.0")));
    Assert.Equal("widget_enabled", pathException.Code);
    var streamPackage = CreatePackage(temp.Path, "dev.test.guarded", "dev.test", "3.0.0");
    await using var stream = new FileStream(streamPackage, FileMode.Open, FileAccess.Read, FileShare.Read);
    var streamException = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.InstallAsync(stream));
    Assert.Equal("widget_enabled", streamException.Code);
    Assert.True(!Directory.Exists(Path.Combine(root, "packages", "dev.test.guarded", "2.0.0")) &&
                !Directory.Exists(Path.Combine(root, "packages", "dev.test.guarded", "3.0.0")),
        "A public install API bypassed disabled-only update review.");
    var staging = Path.Combine(root, "staging");
    Assert.True(!Directory.EnumerateFileSystemEntries(staging).Any(),
        "A package rejected by pre-publish policy leaked staged content.");
}

static async Task TraversalIsRejected()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(temp.Path, "dev.test.badpath", "dev.test", "1.0.0",
        extras: [new ExtraEntry("../escaped.txt", "no")]);
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.CreateInstaller().ValidateAsync(package));
    Assert.Equal("invalid_path", exception.Code);
    Assert.True(!File.Exists(Path.Combine(temp.Path, "escaped.txt")), "Traversal wrote outside the catalog.");
}

static async Task CaseCollisionIsRejected()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(temp.Path, "dev.test.collision", "dev.test", "1.0.0",
        extras:
        [
            new ExtraEntry("assets/Icon.png", "one"),
            new ExtraEntry("assets/icon.png", "two"),
        ]);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(
        () => new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller().ValidateAsync(package));
    Assert.Equal("path_collision", exception.Code);
}

static async Task DirectoryCaseCollisionIsRejected()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(temp.Path, "dev.test.dircollision", "dev.test", "1.0.0",
        extras: [new ExtraEntry("Payload/extra.txt", "case collision")]);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(
        () => new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller().ValidateAsync(package));
    Assert.Equal("path_collision", exception.Code);
}

static async Task SymlinkIsRejected()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(temp.Path, "dev.test.symlink", "dev.test", "1.0.0",
        configure: archive =>
        {
            var link = archive.CreateEntry("payload/link");
            link.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using var writer = new StreamWriter(link.Open(), Encoding.UTF8, leaveOpen: false);
            writer.Write("Widget.dll");
        });
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(
        () => new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller().ValidateAsync(package));
    Assert.Equal("unsupported_entry_type", exception.Code);
}

static async Task DataHeavyPackageBudget()
{
    using var temp = new TemporaryDirectory();
    var chunk = new string('x', 24 * 1024 * 1024);
    var package = CreatePackage(temp.Path, "dev.test.large-data", "dev.test", "1.0.0",
        extras: [new ExtraEntry("data/a.bin", chunk), new ExtraEntry("data/b.bin", chunk), new ExtraEntry("data/c.bin", chunk)]);
    var options = new WidgetCatalogOptions();
    Assert.Equal(256L * 1024 * 1024, options.MaximumTotalBytes);
    Assert.Equal(32L * 1024 * 1024, options.MaximumEntryBytes);
    await new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller().ValidateAsync(package);
    var bounded = new WidgetCatalog(Path.Combine(temp.Path, "bounded"), options with { MaximumTotalBytes = 64L * 1024 * 1024 });
    Assert.Equal("package_too_large", (await Assert.ThrowsAsync<WidgetPackageException>(() => bounded.CreateInstaller().ValidateAsync(package))).Code);
}

static async Task LimitsAreEnforced()
{
    using var temp = new TemporaryDirectory();
    var countPackage = CreatePackage(temp.Path, "dev.test.count", "dev.test", "1.0.0",
        extras: [new ExtraEntry("one.txt", "1"), new ExtraEntry("two.txt", "2")]);
    var countCatalog = new WidgetCatalog(Path.Combine(temp.Path, "count-catalog"), new WidgetCatalogOptions
    {
        MaximumArchiveEntries = 3,
        MaximumEntryBytes = 1024,
        MaximumTotalBytes = 4096,
    });
    Assert.Equal("too_many_entries",
        (await Assert.ThrowsAsync<WidgetPackageException>(() => countCatalog.CreateInstaller().ValidateAsync(countPackage))).Code);

    var sizePackage = CreatePackage(temp.Path, "dev.test.size", "dev.test", "1.0.0",
        extras: [new ExtraEntry("large.bin", new string('x', 2048))]);
    var sizeCatalog = new WidgetCatalog(Path.Combine(temp.Path, "size-catalog"), new WidgetCatalogOptions
    {
        MaximumEntryBytes = 1024,
        MaximumTotalBytes = 4096,
    });
    Assert.Equal("entry_too_large",
        (await Assert.ThrowsAsync<WidgetPackageException>(() => sizeCatalog.CreateInstaller().ValidateAsync(sizePackage))).Code);

    var directoryShape = CreatePackage(
        temp.Path,
        "dev.test.directory-shape",
        "dev.test",
        "1.0.0",
        extras: Enumerable.Range(0, 256)
            .Select(index => new ExtraEntry(
                $"assets-{index:000}/one/two/three/entry.txt", "x"))
            .ToArray());
    var directoryException = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        new WidgetCatalog(Path.Combine(temp.Path, "directory-catalog"))
            .CreateInstaller()
            .ValidateAsync(directoryShape));
    Assert.Equal("too_many_launch_directories", directoryException.Code);
}

static async Task AggregateCatalogLimits()
{
    using var temp = new TemporaryDirectory();

    _ = Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetCatalog(
        Path.Combine(temp.Path, "invalid-options"),
        AggregateOptions(maximumArchiveEntries: 2, maximumInstalledEntries: 2)));

    var directRoot = Path.Combine(temp.Path, "direct-id-catalog");
    Directory.CreateDirectory(Path.Combine(directRoot, "packages", "dev.test.direct-a"));
    Directory.CreateDirectory(Path.Combine(directRoot, "packages", "dev.test.direct-b"));
    var directIdCatalog = new WidgetCatalog(
        directRoot, AggregateOptions(maximumIds: 1));
    Assert.Equal(
        "installed_widget_id_limit",
        (await Assert.ThrowsAsync<WidgetPackageException>(() =>
            directIdCatalog.DiscoverAsync())).Code);

    var idRoot = Path.Combine(temp.Path, "id-catalog");
    var idCatalog = new WidgetCatalog(idRoot, AggregateOptions(maximumIds: 1));
    await idCatalog.InstallAsync(CreatePackage(temp.Path, "dev.test.id-a", "dev.test", "1.0.0"));
    var idLimit = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        idCatalog.InstallAsync(CreatePackage(temp.Path, "dev.test.id-b", "dev.test", "1.0.0")));
    Assert.Equal("installed_widget_id_limit", idLimit.Code);
    Assert.True(!Directory.Exists(Path.Combine(idRoot, "packages", "dev.test.id-b")),
        "An ID-limit rejection published the incoming package.");

    var perWidgetRoot = Path.Combine(temp.Path, "per-widget-catalog");
    var perWidget = new WidgetCatalog(
        perWidgetRoot, AggregateOptions(maximumVersionsPerWidget: 1));
    await perWidget.InstallAsync(CreatePackage(
        temp.Path, "dev.test.versioned", "dev.test", "1.0.0"));
    var perWidgetLimit = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        perWidget.InstallAsync(CreatePackage(
            temp.Path, "dev.test.versioned", "dev.test", "2.0.0")));
    Assert.Equal("installed_widget_version_limit", perWidgetLimit.Code);
    Assert.True(!Directory.Exists(Path.Combine(
            perWidgetRoot, "packages", "dev.test.versioned", "2.0.0")),
        "A per-widget version-limit rejection published the incoming package.");

    var versionRoot = Path.Combine(temp.Path, "version-catalog");
    var versionCatalog = new WidgetCatalog(
        versionRoot, AggregateOptions(maximumVersions: 1));
    await versionCatalog.InstallAsync(CreatePackage(
        temp.Path, "dev.test.total-a", "dev.test", "1.0.0"));
    var versionLimit = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        versionCatalog.InstallAsync(CreatePackage(
            temp.Path, "dev.test.total-b", "dev.test", "1.0.0")));
    Assert.Equal("installed_version_limit", versionLimit.Code);

    var entryRoot = Path.Combine(temp.Path, "entry-catalog");
    var entryCatalog = new WidgetCatalog(entryRoot, AggregateOptions(
        maximumArchiveEntries: 2,
        maximumInstalledEntries: 4));
    await entryCatalog.InstallAsync(CreatePackage(
        temp.Path, "dev.test.entry-a", "dev.test", "1.0.0"));
    var entryLimit = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        entryCatalog.InstallAsync(CreatePackage(
            temp.Path, "dev.test.entry-b", "dev.test", "1.0.0")));
    Assert.Equal("installed_entry_limit", entryLimit.Code);
    Directory.CreateDirectory(Path.Combine(
        entryRoot, "packages", "dev.test.entry-a", "1.0.0", "unexpected-directory"));
    Assert.Equal(
        "integrity_limit",
        (await Assert.ThrowsAsync<WidgetPackageException>(() =>
            entryCatalog.DiscoverAsync())).Code);

    var byteA = CreatePackage(temp.Path, "dev.test.byte-a", "dev.test", "1.0.0");
    var byteB = CreatePackage(temp.Path, "dev.test.byte-b", "dev.test", "1.0.0");
    var inspection = await new WidgetCatalog(Path.Combine(temp.Path, "inspection"))
        .CreateInstaller().ValidateAsync(byteA);
    var byteRoot = Path.Combine(temp.Path, "byte-catalog");
    var byteCatalog = new WidgetCatalog(byteRoot, AggregateOptions(
        maximumEntryBytes: inspection.TotalUncompressedBytes,
        maximumTotalBytes: inspection.TotalUncompressedBytes,
        maximumInstalledBytes: inspection.TotalUncompressedBytes +
                               InstalledPackageIntegrity.MaximumMetadataBytes));
    await byteCatalog.InstallAsync(byteA);
    var byteLimit = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        byteCatalog.InstallAsync(byteB));
    Assert.Equal("installed_byte_limit", byteLimit.Code);

    var unexpected = Path.Combine(versionRoot, "packages", "unexpected.txt");
    await File.WriteAllTextAsync(unexpected, "not a package directory");
    Assert.Equal(
        "invalid_catalog_entry",
        (await Assert.ThrowsAsync<WidgetPackageException>(() => versionCatalog.DiscoverAsync())).Code);

    var timedCatalog = new WidgetCatalog(
        perWidgetRoot,
        AggregateOptions(maximumDiscoveryDuration: TimeSpan.FromSeconds(1)),
        new AdvancingTimeProvider(TimeSpan.FromSeconds(2)));
    Assert.Equal(
        "installed_discovery_time_limit",
        (await Assert.ThrowsAsync<WidgetPackageException>(() => timedCatalog.DiscoverAsync())).Code);
}

static async Task OverLimitCatalogRecovery()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var permissive = new WidgetCatalog(root, AggregateOptions(maximumVersionsPerWidget: 3));
    foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" })
        await permissive.InstallAsync(CreatePackage(
            temp.Path, "dev.test.repair", "dev.test", version));
    await permissive.SetEnabledAsync("dev.test.repair", true);

    var untrustedManifest = Path.Combine(
        root, "packages", "dev.test.repair", "3.0.0", "manifest.json");
    await File.WriteAllTextAsync(untrustedManifest, "not trusted by repair inspection");
    var constrained = new WidgetCatalog(
        root, AggregateOptions(maximumVersionsPerWidget: 2));
    var health = await constrained.InspectHealthAsync();
    Assert.Equal("installed_widget_version_limit", health.FailureCode);
    Assert.Equal(3, health.Candidates.Count);
    Assert.True(health.Candidates.Single(item => item.Version == new Version(1, 0, 0)) is
            { Selected: true, WidgetEnabled: true, CanRemove: false },
        "Repair inspection did not protect the selected version.");
    Assert.True(health.Candidates.Single(item => item.Version == new Version(3, 0, 0)).CanRemove,
        "Repair inspection did not expose an inactive excess version.");

    var selected = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        constrained.RemoveInactiveVersionAsync("dev.test.repair", new Version(1, 0, 0)));
    Assert.Equal("selected_version", selected.Code);
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await Assert.ThrowsAsync<OperationCanceledException>(() =>
        constrained.RemoveInactiveVersionAsync(
            "dev.test.repair", new Version(3, 0, 0), cancelled.Token));
    Assert.True(Directory.Exists(Path.GetDirectoryName(untrustedManifest)!),
        "Cancellation retired a version before the commit point.");

    var oversizedEntries = Enumerable.Range(0, 65)
        .Select(index => Path.Combine(
            Path.GetDirectoryName(untrustedManifest)!, $"oversized-{index:D2}"))
        .ToArray();
    foreach (var entry in oversizedEntries) Directory.CreateDirectory(entry);
    var oversized = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        constrained.RemoveInactiveVersionAsync(
            "dev.test.repair", new Version(3, 0, 0)));
    Assert.Equal("integrity_limit", oversized.Code);
    Assert.True(Directory.Exists(Path.GetDirectoryName(untrustedManifest)!),
        "An over-budget version tree was retired before validation completed.");
    foreach (var entry in oversizedEntries) Directory.Delete(entry);

    var removed = await constrained.RemoveInactiveVersionAsync(
        "dev.test.repair", new Version(3, 0, 0));
    Assert.Equal(new Version(3, 0, 0), removed.Version);
    Assert.True(!Directory.Exists(Path.Combine(
            root, "packages", "dev.test.repair", "3.0.0")),
        "Exact-version repair retained the retired directory.");
    var recovered = await constrained.DiscoverAsync();
    Assert.Equal(2, recovered.Widgets.Single().Versions.Count);

    var enabledHealth = await constrained.InspectHealthAsync();
    Assert.True(enabledHealth.Candidates.Single(item => item.Version == new Version(2, 0, 0)) is
            { WidgetEnabled: true, Selected: false, CanRemove: true },
        "Inactive enabled-widget history was not actionable.");
    await constrained.RemoveInactiveVersionAsync(
        "dev.test.repair", new Version(2, 0, 0));
    var activeOnly = await constrained.DiscoverAsync();
    var activeWidget = activeOnly.Widgets.Single();
    Assert.True(activeWidget.Enabled &&
                activeWidget.ActiveVersion.Version == new Version(1, 0, 0) &&
                activeWidget.Versions.Count == 1,
        "Enabled-history repair changed or removed the selected generation.");
}

static async Task MaximumCatalogRecoveryIsBounded()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    for (var index = 0; index < 256; index++)
    {
        var idRoot = Path.Combine(root, "packages", $"dev.test.repair{index:D3}");
        Directory.CreateDirectory(Path.Combine(idRoot, "1.0.0"));
        Directory.CreateDirectory(Path.Combine(idRoot, "2.0.0"));
    }
    var catalog = new WidgetCatalog(root);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var stopwatch = Stopwatch.StartNew();
    var health = await catalog.InspectHealthAsync();
    stopwatch.Stop();
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    Console.WriteLine(
        $"METRIC catalog_repair versions={health.Candidates.Count} " +
        $"milliseconds={stopwatch.Elapsed.TotalMilliseconds:F3} allocated_bytes={allocated}");
    Assert.True(health.IsWithinDirectoryLimits && health.Candidates.Count == 512,
        "Maximum supported version inventory did not produce a complete health projection.");
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
        "Maximum supported health projection exceeded five seconds.");
    Assert.True(allocated < 32L * 1024 * 1024,
        "Maximum supported health projection allocated 32 MiB or more.");
}

static WidgetCatalogOptions AggregateOptions(
    int maximumIds = 8,
    int maximumVersionsPerWidget = 8,
    int maximumVersions = 16,
    int maximumArchiveEntries = 8,
    int maximumInstalledEntries = 64,
    long maximumEntryBytes = 4 * 1024,
    long maximumTotalBytes = 8 * 1024,
    long maximumInstalledBytes = 64 * 1024,
    TimeSpan? maximumDiscoveryDuration = null) => new()
{
    MaximumArchiveEntries = maximumArchiveEntries,
    MaximumEntryBytes = maximumEntryBytes,
    MaximumTotalBytes = maximumTotalBytes,
    MaximumInstalledWidgetIds = maximumIds,
    MaximumVersionsPerWidget = maximumVersionsPerWidget,
    RetainedVersionsPerWidget = maximumVersionsPerWidget,
    MaximumInstalledVersions = maximumVersions,
    MaximumInstalledEntries = maximumInstalledEntries,
    MaximumInstalledBytes = maximumInstalledBytes,
    MaximumDiscoveryDuration = maximumDiscoveryDuration ?? TimeSpan.FromSeconds(30),
};

static async Task IdentityIsEnforced()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(temp.Path, "org.other.clock", "dev.test", "1.0.0");
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(
        () => new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller().ValidateAsync(package));
    Assert.Equal("identity_mismatch", exception.Code);
}

static async Task MissingEntrypointIsRejected()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(temp.Path, "dev.test.empty", "dev.test", "1.0.0", includeEntrypoint: false);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(
        () => new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller().ValidateAsync(package));
    Assert.Equal("missing_entrypoint", exception.Code);
}

static async Task TamperedInstallIsRejected()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var installed = await catalog.InstallAsync(
        CreatePackage(temp.Path, "dev.test.tamper", "dev.test", "1.0.0"));
    var wrongDirectory = Path.Combine(Path.GetDirectoryName(installed.InstallPath)!, "9.0.0");
    Directory.Move(installed.InstallPath, wrongDirectory);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.DiscoverAsync());
    Assert.Equal("identity_mismatch", exception.Code);
}

static async Task TamperedContentFailsClosed()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var installed = await catalog.InstallAsync(
        CreatePackage(temp.Path, "dev.test.content-tamper", "dev.test", "1.0.0"));
    await File.AppendAllTextAsync(
        Path.Combine(installed.InstallPath, "payload", "Widget.dll"), "tampered");
    var content = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.DiscoverAsync());
    Assert.Equal("package_tampered", content.Code);

    using var metadataTemp = new TemporaryDirectory();
    var metadataCatalog = new WidgetCatalog(Path.Combine(metadataTemp.Path, "catalog"));
    var metadataInstalled = await metadataCatalog.InstallAsync(
        CreatePackage(metadataTemp.Path, "dev.test.metadata-tamper", "dev.test", "1.0.0"));
    await File.WriteAllTextAsync(
        Path.Combine(metadataInstalled.InstallPath, ".wrail-integrity.json"),
        "{\"schemaVersion\":1,\"algorithm\":\"sha256-content-tree-v1\",\"contentDigest\":\"00\"}");
    var metadata = await Assert.ThrowsAsync<WidgetPackageException>(
        () => metadataCatalog.DiscoverAsync());
    Assert.Equal("invalid_integrity_metadata", metadata.Code);
}

static async Task InstalledMetadataBoundsFailClosed()
{
    using var manifestTemp = new TemporaryDirectory();
    var manifestCatalog = new WidgetCatalog(Path.Combine(manifestTemp.Path, "catalog"));
    var manifestInstalled = await manifestCatalog.InstallAsync(
        CreatePackage(manifestTemp.Path, "dev.test.manifest-bound", "dev.test", "1.0.0"));
    await File.WriteAllBytesAsync(
        Path.Combine(manifestInstalled.InstallPath, "manifest.json"),
        new byte[(1024 * 1024) + 1]);
    var manifest = await Assert.ThrowsAsync<WidgetPackageException>(
        () => manifestCatalog.DiscoverAsync());
    Assert.Equal("invalid_manifest", manifest.Code);

    using var metadataTemp = new TemporaryDirectory();
    var metadataCatalog = new WidgetCatalog(Path.Combine(metadataTemp.Path, "catalog"));
    var metadataInstalled = await metadataCatalog.InstallAsync(
        CreatePackage(metadataTemp.Path, "dev.test.metadata-bound", "dev.test", "1.0.0"));
    await File.WriteAllBytesAsync(
        Path.Combine(metadataInstalled.InstallPath, ".wrail-integrity.json"),
        new byte[(4 * 1024) + 1]);
    var metadata = await Assert.ThrowsAsync<WidgetPackageException>(
        () => metadataCatalog.DiscoverAsync());
    Assert.Equal("invalid_integrity_metadata", metadata.Code);
}

static async Task ReservedIntegrityPathIsRejected()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(
        temp.Path,
        "dev.test.integrity-reserved",
        "dev.test",
        "1.0.0",
        extras: [new ExtraEntry(".wrail-integrity.json", "attacker supplied")]);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(() =>
        new WidgetCatalog(Path.Combine(temp.Path, "catalog")).CreateInstaller()
            .ValidateAsync(package));
    Assert.Equal("reserved_path", exception.Code);
}

static Task HostCompatibility()
{
    var manifest = new WidgetManifest
    {
        Id = "dev.test.compatibility",
        Publisher = "dev.test",
        Name = "Compatibility",
        Version = "1.0.0",
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/Widget.dll", "Example.Widget"),
        Permissions = [],
        Architectures = ["x64"],
    };
    var x64V1 = new WidgetHostContext(1, "x64");
    Assert.Equal(1, WidgetHostContext.Current.HostApiMajor);
    Assert.True(ProtocolConstants.CurrentVersion > WidgetHostContext.Current.HostApiMajor,
        "Optional declarative protocol features must evolve independently of package host API 1.");
    Assert.True(WidgetHostCompatibility.Evaluate(manifest, x64V1).IsSupported,
        "Matching host API and architecture were rejected.");
    Assert.Equal("requires_newer_host_api", WidgetHostCompatibility.Evaluate(
        manifest with { HostApi = new HostApiRange("2.0", 2) }, x64V1).Code);
    Assert.Equal("unsupported_host_api", WidgetHostCompatibility.Evaluate(
        manifest, new WidgetHostContext(2, "x64")).Code);
    Assert.Equal("unsupported_architecture", WidgetHostCompatibility.Evaluate(
        manifest, new WidgetHostContext(1, "arm64")).Code);
    return Task.CompletedTask;
}

static async Task UnsignedAuthorityIsContentBound()
{
    using var temp = new TemporaryDirectory();
    var package = CreatePackage(
        temp.Path, "dev.test.authority", "dev.test", "1.0.0");
    var first = await new WidgetCatalog(Path.Combine(temp.Path, "first"))
        .InstallAsync(package);
    var sameBytes = await new WidgetCatalog(Path.Combine(temp.Path, "same"))
        .InstallAsync(package);
    var changedBytes = await new WidgetCatalog(Path.Combine(temp.Path, "changed"))
        .InstallAsync(CreatePackage(
            temp.Path,
            "dev.test.authority",
            "dev.test",
            "1.0.0",
            extras: [new ExtraEntry("payload/content.txt", "different bytes")]));

    var authority = InstalledWidgetAuthority.PublisherId(first);
    Assert.Equal(authority, InstalledWidgetAuthority.PublisherId(sameBytes));
    Assert.True(!string.Equals(
            authority,
            InstalledWidgetAuthority.PublisherId(changedBytes),
            StringComparison.Ordinal),
        "Different bytes with the same asserted ID and version inherited authority.");
    Assert.True(!string.Equals(authority, first.Manifest.Publisher, StringComparison.Ordinal),
        "Unsigned authority trusted the self-asserted publisher label directly.");
}

static async Task VerificationReturnsHashedManifest()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    var installed = await catalog.InstallAsync(
        CreatePackage(
            temp.Path,
            "dev.test.manifest-pair",
            "dev.test",
            "1.0.0",
            extras: [new ExtraEntry("styles/default.wrss", "button { color: #123456; }")]));

    var verification = InstalledPackageIntegrity.Verify(
        root, installed.InstallPath, new WidgetCatalogOptions());
    Assert.Equal(installed.ContentDigest, verification.ContentDigest);
    Assert.Equal(installed.Manifest.Id, verification.Manifest.Id);
    Assert.Equal(installed.Manifest.Version, verification.Manifest.Version);
    Assert.Equal(1, verification.WrssDigests.Count);
    Assert.Equal(
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(
            Path.Combine(installed.InstallPath, "styles", "default.wrss")))).ToLowerInvariant(),
        verification.WrssDigests["styles/default.wrss"]);

    await File.WriteAllTextAsync(
        Path.Combine(installed.InstallPath, "manifest.json"), "{}");
    Assert.Equal(installed.Manifest.Id, verification.Manifest.Id);
    var tampered = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.DiscoverAsync());
    Assert.Equal("invalid_manifest", tampered.Code);
}

static async Task LaunchLeasePinsVerifiedBytes()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(
        temp.Path,
        "dev.test.launch-lease",
        "dev.test",
        "1.0.0",
        extras: [new ExtraEntry("assets/state.txt", "verified asset")]));
    var installed = (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion;
    Assert.Equal(3, installed.VerifiedFiles.Count);
    Assert.True(installed.VerifiedFiles.Values.All(file => file.Sha256.Length == 64),
        "Discovery did not retain the complete verified file inventory.");

    var entrypoint = Path.Combine(installed.InstallPath, "payload", "Widget.dll");
    var movedEntrypoint = entrypoint + ".moved";
    var movedRoot = installed.InstallPath + ".moved";
    using (var lease = InstalledPackageLaunchLease.Acquire(root, installed))
    {
        Assert.Equal(installed.ContentDigest, lease.ContentDigest);
        var fileTargets = lease.Targets.Where(target =>
            target.Kind == InstalledPackageContentTargetKind.ReadOnlyFile).ToArray();
        var rootTargets = lease.Targets.Where(target =>
            target.Kind == InstalledPackageContentTargetKind.AuthorityRootDirectory).ToArray();
        var directoryTargets = lease.Targets.Where(target =>
            target.Kind == InstalledPackageContentTargetKind.ReadOnlyDirectory).ToArray();
        Assert.Equal(installed.VerifiedFiles.Count, fileTargets.Length);
        Assert.True(rootTargets.Length == 1 && string.Equals(
                rootTargets[0].Path,
                installed.InstallPath,
                StringComparison.OrdinalIgnoreCase),
            "Launch authority omitted the package-root traversal grant.");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(
                1 + directoryTargets.Length + fileTargets.Length,
                lease.Targets.Count);
            var identitiesByPath = new Dictionary<string, InstalledPackageObjectIdentity>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var target in lease.Targets)
            {
                Assert.True(target.ObjectIdentity is not null,
                    "Windows launch target omitted object identity evidence.");
                var identity = target.ObjectIdentity!.Value;
                if (identitiesByPath.TryGetValue(target.Path, out var prior))
                    Assert.Equal(prior, identity);
                else
                    identitiesByPath.Add(target.Path, identity);
            }
            Assert.True(lease.Targets.All(target =>
                    target.ObjectIdentity is { } identity &&
                    identity.FileId.Length == 32 &&
                    identity.FileId.Any(character => character != '0') &&
                    identity.FileId.All(character =>
                        character is >= '0' and <= '9' or >= 'A' and <= 'F')),
                "Launch lease returned malformed Windows object identities.");
            _ = Assert.Throws<IOException>(() => File.WriteAllText(entrypoint, "changed"));
            _ = Assert.Throws<IOException>(() => File.Delete(entrypoint));
            _ = Assert.Throws<IOException>(() => File.Move(entrypoint, movedEntrypoint));
            _ = Assert.Throws<IOException>(() => Directory.Move(installed.InstallPath, movedRoot));
        }
    }

    if (OperatingSystem.IsWindows())
    {
        File.Move(entrypoint, movedEntrypoint);
        File.Move(movedEntrypoint, entrypoint);
        Directory.Move(installed.InstallPath, movedRoot);
        Directory.Move(movedRoot, installed.InstallPath);
    }
    await File.WriteAllTextAsync(entrypoint, "released");
    Assert.Equal("released", await File.ReadAllTextAsync(entrypoint));
}

static async Task LaunchLeaseRejectsRaces()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.InstallAsync(CreatePackage(
        temp.Path,
        "dev.test.launch-race",
        "dev.test",
        "1.0.0",
        extras: [new ExtraEntry("assets/state.txt", "verified asset")]));
    var installed = (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion;

    var latePath = Path.Combine(installed.InstallPath, "payload", "LateDependency.dll");
    var insertion = Assert.Throws<WidgetPackageException>(() =>
        InstalledPackageLaunchLease.Acquire(
            root,
            installed,
            beforeFinalInventoryCheck: () => File.WriteAllText(latePath, "late")));
    Assert.Equal("package_launch_integrity", insertion.Code);

    File.Delete(latePath);
    await File.WriteAllTextAsync(
        Path.Combine(installed.InstallPath, "assets", "state.txt"),
        "mutated asset");
    var mutation = Assert.Throws<WidgetPackageException>(() =>
        InstalledPackageLaunchLease.Acquire(root, installed));
    Assert.Equal("package_launch_integrity", mutation.Code);
}

static async Task MaximumLaunchLeaseIsBounded()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    var extras = Enumerable.Range(0, 510)
        .Select(index => new ExtraEntry($"assets/entry-{index:000}.txt", "x"))
        .ToArray();
    await catalog.InstallAsync(CreatePackage(
        temp.Path,
        "dev.test.maximum-launch",
        "dev.test",
        "1.0.0",
        extras));
    var installed = (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion;
    Assert.Equal(512, installed.VerifiedFiles.Count);

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    using var lease = InstalledPackageLaunchLease.Acquire(root, installed);
    stopwatch.Stop();
    Assert.Equal(512, lease.Targets.Count(target =>
        target.Kind == InstalledPackageContentTargetKind.ReadOnlyFile));
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
        $"Maximum launch lease acquisition exceeded five seconds ({stopwatch.Elapsed.TotalMilliseconds:0} ms).");
    Console.WriteLine(
        $"METRIC package_launch_lease files=512 milliseconds={stopwatch.Elapsed.TotalMilliseconds:F3}");
}

static async Task LaunchLeaseRejectsExcessFiles()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var options = new WidgetCatalogOptions
    {
        MaximumArchiveEntries = InstalledPackageLaunchLease.MaximumReadOnlyFiles + 1,
    };
    var catalog = new WidgetCatalog(root, options);
    var extras = Enumerable.Range(0, InstalledPackageLaunchLease.MaximumReadOnlyFiles - 1)
        .Select(index => new ExtraEntry($"assets/entry-{index:0000}.txt", "x"))
        .ToArray();
    await catalog.InstallAsync(CreatePackage(
        temp.Path,
        "dev.test.excess-launch-files",
        "dev.test",
        "1.0.0",
        extras));
    var installed = (await catalog.DiscoverAsync()).Widgets.Single().ActiveVersion;
    Assert.Equal(
        InstalledPackageLaunchLease.MaximumReadOnlyFiles + 1,
        installed.VerifiedFiles.Count);

    var failure = Assert.Throws<WidgetPackageException>(() =>
        InstalledPackageLaunchLease.Acquire(root, installed));
    Assert.Equal("package_launch_integrity", failure.Code);
}

static Task BoundedReadsRejectMisreportedLengths()
{
    using var exact = new MisreportedLengthStream([0x10, 0x20], reportedLength: 2);
    Assert.SequenceEqual(new byte[] { 0x10, 0x20 }, BoundedFileReader.ReadAll(exact, maximumBytes: 2));

    using var excess = new MisreportedLengthStream([0x10, 0x20], reportedLength: 1);
    _ = Assert.Throws<InvalidDataException>(() => BoundedFileReader.ReadAll(excess, maximumBytes: 1));

    using var truncated = new MisreportedLengthStream([0x10], reportedLength: 2);
    _ = Assert.Throws<InvalidDataException>(() => BoundedFileReader.ReadAll(truncated, maximumBytes: 2));

    using var changing = new MisreportedLengthStream(
        [0x10], reportedLength: 1, lengthAfterRead: 2);
    _ = Assert.Throws<InvalidDataException>(() => BoundedFileReader.ReadAll(changing, maximumBytes: 2));

    using var nonSeekable = new NonSeekableReadStream([0x10, 0x20]);
    _ = Assert.Throws<InvalidDataException>(() =>
        BoundedFileReader.ReadAll(nonSeekable, maximumBytes: 1));

    using var empty = new MisreportedLengthStream([], reportedLength: 0);
    Assert.Equal(0, BoundedFileReader.ReadAll(empty, maximumBytes: int.MaxValue).Length);
    return Task.CompletedTask;
}

static Task IntegrityHashingRequiresExactLength()
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    var buffer = new byte[4];

    using var excess = new MisreportedLengthStream([0x10, 0x20], reportedLength: 1);
    _ = Assert.Throws<InvalidDataException>(() =>
        BoundedFileReader.AppendExact(hash, excess, expectedLength: 1, buffer));

    using var truncated = new MisreportedLengthStream([0x10], reportedLength: 2);
    _ = Assert.Throws<InvalidDataException>(() =>
        BoundedFileReader.AppendExact(hash, truncated, expectedLength: 2, buffer));

    using var changing = new MisreportedLengthStream(
        [0x10], reportedLength: 1, lengthAfterRead: 2);
    _ = Assert.Throws<InvalidDataException>(() =>
        BoundedFileReader.AppendExact(hash, changing, expectedLength: 1, buffer));

    using var interruptedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    using var interrupted = new NonSeekableReadStream(new byte[128 * 1024]);
    using var cancellation = new CancellationTokenSource();
    var checkpoints = 0;
    _ = Assert.Throws<OperationCanceledException>(() =>
        BoundedFileReader.AppendExact(
            interruptedHash,
            interrupted,
            expectedLength: 128 * 1024,
            buffer: new byte[64 * 1024],
            checkpoint: () =>
            {
                if (++checkpoints == 2) cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }));
    Assert.Equal(2, checkpoints);
    return Task.CompletedTask;
}

static string CreatePackage(
    string root,
    string id,
    string publisher,
    string version,
    IReadOnlyList<ExtraEntry>? extras = null,
    Action<ZipArchive>? configure = null,
    bool includeEntrypoint = true,
    IReadOnlyList<string>? permissions = null)
{
    var path = Path.Combine(root, $"{id}-{version}-{Guid.NewGuid():N}.wrwidget");
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = publisher,
        Name = id,
        Version = version,
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/Widget.dll", "Example.Widget"),
        Permissions = permissions ?? [],
    };
    WriteEntry(archive, "manifest.json", Encoding.UTF8.GetString(ManifestJson.Serialize(manifest)));
    if (includeEntrypoint) WriteEntry(archive, "payload/Widget.dll", "not-a-real-assembly");
    foreach (var extra in extras ?? []) WriteEntry(archive, extra.Path, extra.Content);
    configure?.Invoke(archive);
    return path;
}

static void WriteEntry(ZipArchive archive, string path, string content)
{
    var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
    writer.Write(content);
}

file sealed record ExtraEntry(string Path, string Content);

file sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "widget-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }
    public string Path { get; }
    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

file sealed class AdvancingTimeProvider(TimeSpan step) : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() =>
        Interlocked.Add(ref _timestamp, step.Ticks);
}

file sealed class MisreportedLengthStream(
    byte[] content,
    long reportedLength,
    long? lengthAfterRead = null) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length =>
        _position > 0 && lengthAfterRead is { } changed ? changed : reportedLength;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var available = content.Length - _position;
        if (available <= 0) return 0;
        var read = Math.Min(available, count);
        Array.Copy(content, _position, buffer, offset, read);
        _position += read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

file sealed class NonSeekableReadStream(byte[] content) : Stream
{
    private int _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var available = content.Length - _position;
        if (available <= 0) return 0;
        var read = Math.Min(available, count);
        Array.Copy(content, _position, buffer, offset, read);
        _position += read;
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

file sealed class TestUninstallAuthorityParticipant(string catalogRoot) :
    IWidgetUninstallAuthorityParticipant
{
    internal bool FailBeforeCommit { get; set; }
    internal bool CleanupPending { get; set; }
    internal int Calls { get; private set; }

    public async Task<WidgetUninstallAuthorityCommit> RetirePackageAsync(
        string packageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        var staging = Path.Combine(catalogRoot, "staging");
        var marker = Directory.EnumerateFiles(
            staging, ".uninstall-*.pending.json", SearchOption.TopDirectoryOnly)
            .Single();
        var pending = await PendingWidgetUninstallStore.ReadAsync(
            marker, cancellationToken);
        Assert.Equal(packageId, pending.PackageId);
        Assert.True(pending.PublisherAuthorities.Count > 0,
            "Pending uninstall omitted verified publisher authorities.");
        var retired = marker[..^".pending.json".Length];
        Assert.True(Directory.Exists(retired) ||
                    !Directory.Exists(Path.Combine(catalogRoot, "packages", packageId)),
            "Authority retirement ran before package staging.");
        if (FailBeforeCommit)
            throw new WidgetPackageException(
                "registration_cleanup_failed",
                "Synthetic precommit registration cleanup failure.");
        return new WidgetUninstallAuthorityCommit(true, CleanupPending);
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

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task<T> ThrowsAsync<T, TValue>(Func<Task<TValue>> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
