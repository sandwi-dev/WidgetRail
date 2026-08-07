using System.IO.Compression;
using System.Text;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Install and discovery are deterministic across IDs and versions", InstallAndDiscover),
    ("Enable and order state persists atomically", StatePersists),
    ("Independent catalog clients serialize state mutations", ConcurrentStatePersists),
    ("Reinstall never overwrites an immutable version", ReinstallDoesNotOverwrite),
    ("Traversal paths are rejected before extraction", TraversalIsRejected),
    ("Case-colliding paths are rejected", CaseCollisionIsRejected),
    ("Case-colliding directory prefixes are rejected", DirectoryCaseCollisionIsRejected),
    ("Unix symlink entries are rejected", SymlinkIsRejected),
    ("Entry count and expanded size limits are enforced", LimitsAreEnforced),
    ("Publisher and package identity are enforced", IdentityIsEnforced),
    ("Missing entrypoint assemblies are rejected", MissingEntrypointIsRejected),
    ("Tampered installed directory identity is rejected", TamperedInstallIsRejected),
    ("Host compatibility is deterministic across API and architecture", HostCompatibility),
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
        await catalog.CreateInstaller().InstallAsync(package);

    var snapshot = await catalog.DiscoverAsync();
    Assert.SequenceEqual(["dev.test.alpha", "dev.test.zeta"], snapshot.Widgets.Select(widget => widget.Id));
    Assert.SequenceEqual(["2.0.0", "1.0.0"], snapshot.Widgets[0].Versions.Select(item => item.Version.ToString()));
    Assert.Equal("2.0.0", snapshot.Widgets[0].ActiveVersion.Version.ToString());
    Assert.True(snapshot.Widgets.All(widget => !widget.Enabled),
        "Newly installed widget IDs must remain disabled until the user explicitly enables them.");
}

static async Task StatePersists()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var catalog = new WidgetCatalog(root);
    await catalog.CreateInstaller().InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"));
    await catalog.CreateInstaller().InstallAsync(CreatePackage(temp.Path, "dev.test.beta", "dev.test", "1.0.0"));

    await catalog.SetEnabledAsync("dev.test.alpha", true);
    await catalog.SetOrderAsync(["dev.test.beta"]);
    var reloaded = await new WidgetCatalog(root).DiscoverAsync();
    Assert.SequenceEqual(["dev.test.beta", "dev.test.alpha"], reloaded.Widgets.Select(widget => widget.Id));
    Assert.True(!reloaded.Widgets[0].Enabled, "Disabled state was not persisted.");
    Assert.True(reloaded.Widgets[1].Enabled, "Explicitly enabled state was not persisted.");
    Assert.True(!Directory.EnumerateFiles(root, ".catalog-state.*.tmp").Any(), "Atomic state temporary file leaked.");
}

static async Task ConcurrentStatePersists()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "catalog");
    var first = new WidgetCatalog(root);
    await first.CreateInstaller().InstallAsync(CreatePackage(temp.Path, "dev.test.alpha", "dev.test", "1.0.0"));
    await first.CreateInstaller().InstallAsync(CreatePackage(temp.Path, "dev.test.beta", "dev.test", "1.0.0"));

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

static async Task ReinstallDoesNotOverwrite()
{
    using var temp = new TemporaryDirectory();
    var catalog = new WidgetCatalog(Path.Combine(temp.Path, "catalog"));
    var package = CreatePackage(temp.Path, "dev.test.clock", "dev.test", "1.0.0");
    var first = await catalog.CreateInstaller().InstallAsync(package);
    var exception = await Assert.ThrowsAsync<WidgetPackageException>(() => catalog.CreateInstaller().InstallAsync(package));
    Assert.Equal("version_already_installed", exception.Code);
    Assert.True(File.Exists(Path.Combine(first.InstallPath, "manifest.json")), "Original install was damaged.");
    var staging = Path.Combine(catalog.Root, "staging");
    Assert.True(!Directory.EnumerateFileSystemEntries(staging).Any(), "Failed install left staged content.");
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
    var installed = await catalog.CreateInstaller().InstallAsync(
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
