using System.IO.Compression;
using System.Text;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Install and discovery are deterministic across IDs and versions", InstallAndDiscover),
    ("Enable and order state persists atomically", StatePersists),
    ("Version pins are disabled-only and schema-one state migrates", VersionPinningAndMigration),
    ("Update preparation latches the reviewed version before publication", UpdatePreparationPinsReviewedVersion),
    ("Concurrent first installs serialize before enablement", ConcurrentFirstInstallsAreSafe),
    ("Concurrent rollbacks are linearizable", ConcurrentRollbacksAreLinearizable),
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
    ("Publisher and package identity are enforced", IdentityIsEnforced),
    ("Missing entrypoint assemblies are rejected", MissingEntrypointIsRejected),
    ("Tampered installed directory identity is rejected", TamperedInstallIsRejected),
    ("Host compatibility is deterministic across API and architecture", HostCompatibility),
    ("Unsigned authority is stable only for one exact package version", UnsignedAuthorityIsVersionBound),
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
        Console.Error.WriteLine(exception);
    }
}
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

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
    var catalog = new WidgetCatalog(root);
    foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" })
        await catalog.InstallAsync(CreatePackage(temp.Path, "dev.test.concurrent-rollback", "dev.test", version));
    await catalog.SetActiveVersionAsync("dev.test.concurrent-rollback", new Version(3, 0, 0));

    var changes = await Task.WhenAll(
        new WidgetCatalog(root).RollbackAsync("dev.test.concurrent-rollback"),
        new WidgetCatalog(root).RollbackAsync("dev.test.concurrent-rollback"));
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
}

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

static Task UnsignedAuthorityIsVersionBound()
{
    var manifest = new WidgetManifest
    {
        Id = "dev.test.authority",
        Publisher = "dev.test",
        Name = "Authority",
        Version = "1.0.0",
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/Widget.dll", "Example.Widget"),
        Permissions = [],
    };
    var first = InstalledWidgetAuthority.PublisherId(manifest);
    Assert.Equal(first, InstalledWidgetAuthority.PublisherId(manifest));
    Assert.True(!string.Equals(
            first,
            InstalledWidgetAuthority.PublisherId(manifest with { Version = "2.0.0" }),
            StringComparison.Ordinal),
        "A different unsigned package version inherited the same authority identity.");
    Assert.True(!string.Equals(first, manifest.Publisher, StringComparison.Ordinal),
        "Unsigned authority trusted the self-asserted publisher label directly.");
    return Task.CompletedTask;
}

static string CreatePackage(
    string root,
    string id,
    string publisher,
    string version,
    IReadOnlyList<ExtraEntry>? extras = null,
    Action<ZipArchive>? configure = null,
    bool includeEntrypoint = true)
{
    var path = Path.Combine(root, $"{id}-{version}-{Guid.NewGuid():N}.gbarwidget");
    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
    var manifest = new WidgetManifest
    {
        Id = id,
        Publisher = publisher,
        Name = id,
        Version = version,
        HostApi = new HostApiRange("1.0", 1),
        Entrypoint = new WidgetEntrypoint("dotnet-worker", "payload/Widget.dll", "Example.Widget"),
        Permissions = [],
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
