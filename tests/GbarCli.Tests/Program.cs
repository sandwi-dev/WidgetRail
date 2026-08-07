using System.IO.Compression;
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
    Assert.Contains("enabled   dev.test.local  2.0.0", listed.Output);
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

file sealed record CliResult(int Code, string Output, string Error);

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
}
