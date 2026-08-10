using System.IO.Compression;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameBarAlternative.GbarCli;
using GameBarAlternative.PlatformSettings;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetSdk;

if (args is ["--dev-persistent-grandchild", ..])
{
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args is ["--dev-persistent-child", var descendantPath, ..])
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    start.ArgumentList.Add("--dev-persistent-grandchild");
    using var grandchild = Process.Start(start) ?? throw new InvalidOperationException("Could not start fake grandchild.");
    await File.WriteAllLinesAsync(descendantPath, [Environment.ProcessId.ToString(), grandchild.Id.ToString()]);
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args.Contains("--development-catalog-root", StringComparer.Ordinal))
{
    var readyPath = DevelopmentArgument(args, "--development-ready-path");
    var nonce = DevelopmentArgument(args, "--development-ready-nonce");
    var catalog = Path.GetFullPath(DevelopmentArgument(args, "--development-catalog-root"));
    var widgetId = DevelopmentArgument(args, "--development-widget-id");
    var instance = DevelopmentArgument(args, "--development-widget-instance");
    var installed = await new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalog).DiscoverAsync();
    var expected = installed.Widgets.Single(widget => widget.Id == widgetId);
    var brokenProbe = args.Contains("--development-probe-only", StringComparer.Ordinal) &&
                      expected.ActiveVersion.Manifest.Entrypoint.Type == "Missing.Widget";
    if (!args.Contains("--development-probe-only", StringComparer.Ordinal) &&
        widgetId.EndsWith(".descendant-tree", StringComparison.Ordinal))
    {
        var generation = Directory.GetParent(Path.GetDirectoryName(readyPath)!)!.FullName;
        var reportedDescendantsPath = Path.Combine(generation, "descendants.txt");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        start.ArgumentList.Add("--dev-persistent-child");
        start.ArgumentList.Add(reportedDescendantsPath);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start fake child.");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!File.Exists(reportedDescendantsPath) && DateTime.UtcNow < deadline) await Task.Delay(10);
        if (!File.Exists(reportedDescendantsPath)) throw new TimeoutException("Fake descendants did not report their PIDs.");
    }
    if (!widgetId.EndsWith(".no-ready", StringComparison.Ordinal) && !brokenProbe)
    {
        if (widgetId.EndsWith(".forged-ready", StringComparison.Ordinal)) nonce = new string('0', 64);
        var payload = $"gbar-dev-ready-v1\n{nonce}\n{catalog}\n{widgetId}\n{instance}\n";
        var temporary = readyPath + ".tmp";
        await File.WriteAllTextAsync(temporary, payload);
        File.Move(temporary, readyPath);
    }
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Help describes the complete workflow", HelpWorks),
    ("Authority recovery is exact, stale-safe, and sanitized", AuthorityRecoveryWorkflow),
    ("Widget config is package scoped and rejects secrets", WidgetConfigWorkflow),
    ("New scaffolds a token-free controller widget", NewScaffolds),
    ("New requires a real SDK project outside the source checkout", NewScaffoldsOutsideCheckout),
    ("New rejects invalid package identity before writing", NewRejectsIdentity),
    ("Theme commands provide a deterministic end-to-end author workflow", ThemeWorkflow),
    ("Theme validation rejects unsafe content and unreachable styles", ThemeValidationSafety),
    ("Theme archives reject traversal collisions and executable content", ThemeArchiveSafety),
    ("Theme installation is immutable and catalog-compatible", ThemeInstallIsImmutable),
    ("Remote theme installation requires and verifies a pinned release asset", ThemeRemoteInstall),
    ("Validate accepts a scaffolded widget", ValidateScaffold),
    ("Validate rejects unsafe GBSS", ValidateRejectsUnsafeGbss),
    ("Validate rejects malformed manifest", ValidateRejectsManifest),
    ("Dev discovers only bounded declared source files", DevSourceDiscoveryIsScoped),
    ("Dev package watching matches bounded pack inputs and new directories", DevPackageWatchingIsComplete),
    ("Dev builds a scaffold into a catalog-valid package", DevBuildsIsolatedPackage),
    ("Dev builds cannot leave persistent compiler or build servers", DevBuildDisablesPersistentServers),
    ("Dev diagnostics are bounded and single-line", DevDiagnosticsAreSanitized),
    ("Dev rejects absent or forged readiness without replacing last good", DevReadinessFailsClosed),
    ("Dev rejects a broken worker entrypoint without replacing last good", DevBrokenEntrypointRetainsLastGood),
    ("Dev Job Object reclaims persistent child and grandchild processes", DevJobReclaimsDescendants),
    ("Dev retains last good and cleans its process tree on cancellation", DevRetainsAndCleans),
    ("Render previews a valid snapshot", RenderSnapshot),
    ("Render rejects assembly execution and unbounded snapshot inputs", RenderFailsClosed),
    ("Scenario manifests are bounded and execution fails closed", ScenarioPreviewTests.Run),
    ("Controller replay follows focus and shortcuts", ReplayFocusAndActions),
    ("Pack produces reproducible catalog-valid archives", PackIsReproducible),
    ("Pack and install reject unlaunchable directory shapes before publication", DirectoryShapeLimitsAreEnforced),
    ("Install list disable and enable form a local distribution workflow", LocalDistributionWorkflow),
    ("Uninstall is explicit disabled-only and cleans every package version", UninstallWorkflow),
    ("Catalog repair lists and removes only inactive excess versions", CatalogRepairWorkflow),
    ("Pack rejects invalid identity without publishing an archive", PackRejectsInvalidManifest),
    ("Pack rejects source reparse points", PackRejectsReparsePoints),
    ("Install rejects traversal archives through the CLI", InstallRejectsTraversal),
    ("Remote install verifies a pinned package and leaves it disabled", RemoteDistributionWorkflow),
    ("Remote updates require explicit disable and preserve enabled versions on failure", RemoteUpdateRequiresDisable),
    ("Version selection and rollback are explicit disabled-only operations", VersionSelectionAndRollback),
    ("GitHub shorthand resolves directly to a release asset", GitHubShorthandResolves),
    ("Remote install requires a valid SHA-256 pin before network access", RemoteRequiresHash),
    ("Remote sources reject unsafe schemes authorities and literals", RemoteRejectsUnsafeSources),
    ("Remote downloader follows bounded HTTPS redirects", RemoteRedirectsAreBounded),
    ("Remote downloader enforces declared and streamed byte limits", RemoteDownloadIsBounded),
    ("Remote downloader rejects encoded payloads", RemoteRejectsContentEncoding),
    ("Remote installer reports malformed archives without crashing", RemoteRejectsMalformedArchive),
    ("Remote downloader removes temporary files after integrity failure", RemoteHashMismatchCleansUp),
    ("Remote package remains write-locked until installation completes", RemotePackageHasIntegrityGuard),
    ("Remote downloader enforces response and overall timeouts", RemoteTimeoutsAreBounded),
    ("Remote request failures redact signed URL secrets", RemoteFailuresRedactSecrets),
    ("Caller cancellation stops remote installation", RemoteCancellationIsBounded),
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
    foreach (var command in new[] { "new", "validate", "dev", "preview", "render", "replay", "pack", "install", "uninstall", "repair", "authority-recovery", "list", "enable", "disable", "version" })
        Assert.Contains(command, result.Output);
    Assert.Contains("repair manages quarantined installed-catalog generations", result.Output);
    Assert.Contains("no force-clear", result.Output);
    var version = await RunCli("version", "help");
    Assert.Equal(0, version.Code);
    foreach (var command in new[] { "list", "select", "rollback" })
        Assert.Contains($"version {command}", version.Output);
    var theme = await RunCli("theme", "help");
    Assert.Equal(0, theme.Code);
    foreach (var command in new[] { "new", "validate", "preview", "pack", "inspect", "install", "list" })
        Assert.Contains($"theme {command}", theme.Output);
}

static async Task AuthorityRecoveryWorkflow()
{
    const string token = "0123456789ABCDEF0123456789ABCDEF";
    const string legacyToken =
        "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    const string profile = "GameBarAlternative.Widget.0123456789ABCDEF";
    const string sensitivePath = @"C:\Users\private\package\secret.dll";
    const string sensitiveDescriptor = "D:(A;;FA;;;S-1-5-21-PRIVATE)";

    var empty = await RunAuthorityRecovery(
        new TestAuthorityRecoveryClient([]), "list");
    Assert.Equal(0, empty.Code);
    Assert.Contains("No pending AppContainer authority recovery transactions.",
        empty.Output);

    var client = new TestAuthorityRecoveryClient(
        [
            new AuthorityRecoverySummary(token, profile, 3, IsLegacy: false),
            new AuthorityRecoverySummary(
                legacyToken, profile + ".Legacy", 2, IsLegacy: true),
        ]);
    var listed = await RunAuthorityRecovery(client, "list");
    Assert.Equal(0, listed.Code);
    Assert.Contains(token, listed.Output);
    Assert.Contains($"profile={profile}", listed.Output);
    Assert.Contains("targets=3", listed.Output);
    Assert.Contains("format=current", listed.Output);
    Assert.Contains(legacyToken, listed.Output);
    Assert.Contains("targets=2", listed.Output);
    Assert.Contains("format=legacy", listed.Output);
    Assert.DoesNotContain(sensitivePath, listed.Output);
    Assert.DoesNotContain(sensitiveDescriptor, listed.Output);

    foreach (var invalidToken in new[]
             {
                 "not-a-token",
                 new string('A', 31),
                 new string('A', 33),
                 new string('A', 63),
                 new string('A', 65),
                 new string('a', 32),
                 new string('G', 32),
             })
    {
        var invalid = await RunAuthorityRecovery(client, "retry", invalidToken);
        Assert.Equal(2, invalid.Code);
        Assert.Contains("uppercase hexadecimal token", invalid.Error);
    }
    Assert.Equal(0, client.RetryCount);

    var staleClient = new TestAuthorityRecoveryClient(
        [new AuthorityRecoverySummary(token, profile, 3, IsLegacy: false)],
        retryFailure: new AuthorityRecoveryClientException(
            "stale_confirmation",
            new IOException($"{sensitivePath} {sensitiveDescriptor}")));
    var stale = await RunAuthorityRecovery(staleClient, "retry", token);
    Assert.Equal(1, stale.Code);
    Assert.Contains("stale_confirmation", stale.Error);
    Assert.DoesNotContain(sensitivePath, stale.Error);
    Assert.DoesNotContain(sensitiveDescriptor, stale.Error);

    var pendingClient = new TestAuthorityRecoveryClient(
        [new AuthorityRecoverySummary(token, profile, 3, IsLegacy: false)],
        retryFailure: new AuthorityRecoveryClientException(
            "recovery_not_verified",
            new IOException($"{sensitivePath} {sensitiveDescriptor}")));
    var pending = await RunAuthorityRecovery(pendingClient, "retry", token);
    Assert.Equal(1, pending.Code);
    Assert.Contains("recovery_not_verified", pending.Error);
    Assert.Equal(token, pendingClient.ListPending().Single().ConfirmationToken);
    Assert.DoesNotContain(sensitivePath, pending.Error);

    var invalidClient = new TestAuthorityRecoveryClient(
        retryFailure: new AuthorityRecoveryClientException("invalid_confirmation"));
    var invalidAdapter = await RunAuthorityRecovery(invalidClient, "retry", token);
    Assert.Equal(2, invalidAdapter.Code);
    Assert.Contains("valid confirmation token", invalidAdapter.Error);

    var unavailableClient = new TestAuthorityRecoveryClient(
        listFailure: new AuthorityRecoveryClientException(
            "unexpected_internal_code",
            new IOException($"{sensitivePath} {sensitiveDescriptor}")));
    var unavailable = await RunAuthorityRecovery(unavailableClient, "list");
    Assert.Equal(1, unavailable.Code);
    Assert.Contains("recovery_unavailable", unavailable.Error);
    Assert.DoesNotContain("unexpected_internal_code", unavailable.Error);
    Assert.DoesNotContain(sensitivePath, unavailable.Error);
    Assert.DoesNotContain(sensitiveDescriptor, unavailable.Error);

    var recovered = await RunAuthorityRecovery(client, "retry", token);
    Assert.Equal(0, recovered.Code);
    Assert.Equal(1, client.RetryCount);
    Assert.Equal(token, client.LastRetryToken);
    Assert.Contains(token, recovered.Output);

    var legacyRecovered = await RunAuthorityRecovery(client, "retry", legacyToken);
    Assert.Equal(0, legacyRecovered.Code);
    Assert.Equal(2, client.RetryCount);
    Assert.Equal(legacyToken, client.LastRetryToken);

    foreach (var rejected in new[]
             {
                 new[] { "clear" },
                 new[] { "list", "--journal", sensitivePath },
                 new[] { "retry", token, "--force" },
             })
    {
        var result = await RunAuthorityRecovery(client, rejected);
        Assert.Equal(2, result.Code);
        Assert.DoesNotContain(sensitivePath, result.Error);
    }

    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    using var cancelledOutput = new StringWriter();
    using var cancelledError = new StringWriter();
    var cancelled = await CliApplication.RunAsync(
        ["authority-recovery", "retry", token],
        cancelledOutput,
        cancelledError,
        remoteHttpHandler: null,
        cancellation.Token);
    Assert.Equal(130, cancelled);
    Assert.Contains("operation cancelled", cancelledError.ToString());
}

static async Task WidgetConfigWorkflow()
{
    using var temp = new TemporaryDirectory();
    const string widget = "org.gbar.samples.spotify";
    const string publisher = "org.gbar.samples";
    var root = temp.Path;
    var set = await RunCli("config", "set", widget, "client-id", "0123456789abcdef",
        "--publisher", publisher, "--settings-root", root);
    Assert.Equal(0, set.Code);

    var get = await RunCli("config", "get", widget, "client-id",
        "--publisher", publisher, "--settings-root", root);
    Assert.Equal(0, get.Code);
    Assert.Contains("0123456789abcdef", get.Output);

    var list = await RunCli("config", "list", widget,
        "--publisher", publisher, "--settings-root", root);
    Assert.Equal(0, list.Code);
    Assert.Contains("client-id", list.Output);
    Assert.DoesNotContain("0123456789abcdef", list.Output);

    var rejected = await RunCli("config", "set", widget, "client-secret", "do-not-store",
        "--publisher", publisher, "--settings-root", root);
    Assert.Equal(2, rejected.Code);
    Assert.Contains("Secret-like", rejected.Error);

    var remove = await RunCli("config", "remove", widget, "client-id",
        "--publisher", publisher, "--settings-root", root);
    Assert.Equal(0, remove.Code);
    var missing = await RunCli("config", "get", widget, "client-id",
        "--publisher", publisher, "--settings-root", root);
    Assert.Equal(2, missing.Code);
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
    var manifest = ManifestJson.Deserialize(
        await File.ReadAllBytesAsync(Path.Combine(destination, "manifest.json")));
    Assert.Equal(WidgetGlyph.Connection, manifest.Presentation.Icon);
    Assert.Equal(WidgetResidencyPolicies.UnloadAfterIdle, manifest.ResidencyPolicy?.Mode);
    Assert.Equal(300, manifest.ResidencyPolicy?.IdleSeconds);
}

static async Task NewScaffoldsOutsideCheckout()
{
    using var temp = new TemporaryDirectory();
    var originalDirectory = Environment.CurrentDirectory;
    var originalTemplateRoot = Environment.GetEnvironmentVariable("GBAR_TEMPLATE_ROOT");
    var sdkProject = Path.GetFullPath(Path.Combine(originalDirectory,
        "src", "WidgetSdk", "WidgetSdk.csproj"));
    Assert.True(File.Exists(sdkProject), "Repository WidgetSdk project is unavailable.");
    var sourceTemplate = Path.Combine(originalDirectory, "templates", "ControllerWidget");
    var externalRoot = Path.Combine(temp.Path, "external");
    var externalTemplate = Path.Combine(externalRoot, "templates", "ControllerWidget");
    Directory.CreateDirectory(externalTemplate);
    foreach (var source in Directory.EnumerateFiles(sourceTemplate, "*", SearchOption.AllDirectories))
    {
        var destination = Path.Combine(externalTemplate, Path.GetRelativePath(sourceTemplate, source));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination);
    }

    try
    {
        Environment.CurrentDirectory = externalRoot;
        Environment.SetEnvironmentVariable("GBAR_TEMPLATE_ROOT", externalTemplate);
        var rejectedTarget = Path.Combine(externalRoot, "RejectedWidget");
        var rejected = await RunCli(
            "new", "widget", "RejectedWidget", "--output", rejectedTarget);
        Assert.Equal(2, rejected.Code);
        Assert.Contains("WidgetSdk is not published as a supported package", rejected.Error);
        Assert.True(!Directory.Exists(rejectedTarget),
            "Missing SDK resolution wrote a partial scaffold.");

        var destination = Path.Combine(externalRoot, "ExternalWidget");
        var created = await RunCli(
            "new", "widget", "ExternalWidget",
            "--output", destination,
            "--id", "dev.test.external-widget",
            "--publisher", "dev.test",
            "--sdk-project", sdkProject);
        Assert.Equal(0, created.Code);
        var project = Path.Combine(destination, "ExternalWidget.csproj");
        var projectText = await File.ReadAllTextAsync(project);
        Assert.Contains("ProjectReference", projectText);
        Assert.DoesNotContain("PackageReference", projectText);
        var build = await RunProcessAsync(
            "dotnet", ["build", project, "--configuration", "Release", "--nologo"],
            TimeSpan.FromSeconds(90));
        Assert.Equal(0, build.Code);
    }
    finally
    {
        Environment.CurrentDirectory = originalDirectory;
        Environment.SetEnvironmentVariable("GBAR_TEMPLATE_ROOT", originalTemplateRoot);
    }
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

static async Task ThemeWorkflow()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "Ocean Theme");
    var created = await RunCli("theme", "new", "Ocean Night", "--output", source,
        "--id", "dev.test.ocean-night", "--publisher", "dev.test", "--version", "2.3.4");
    Assert.Equal(0, created.Code);
    Assert.Contains("dev.test.ocean-night 2.3.4", created.Output);
    Assert.True(File.Exists(Path.Combine(source, "theme.json")), "Theme manifest was not scaffolded.");
    Assert.True(File.Exists(Path.Combine(source, "theme.gbss")), "Starter GBSS was not scaffolded.");

    var validated = await RunCli("theme", "validate", source);
    Assert.Equal(0, validated.Code);
    Assert.Contains("Valid theme: dev.test.ocean-night 2.3.4 by dev.test", validated.Output);
    var preview = await RunCli("theme", "preview", source);
    Assert.Equal(0, preview.Code);
    Assert.Contains("Computed preview: Ocean Night", preview.Output);
    Assert.Contains("tray-item:focused:", preview.Output);
    Assert.Contains("button:focused:", preview.Output);
    Assert.Contains("accessibility overrides are not simulated", preview.Output);

    var first = Path.Combine(temp.Path, "first.gbartheme");
    var second = Path.Combine(temp.Path, "second.gbartheme");
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", first)).Code);
    foreach (var file in Directory.EnumerateFiles(source)) File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch);
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", second)).Code);
    Assert.SequenceEqual(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
    using (var archive = ZipFile.OpenRead(first))
    {
        Assert.SequenceEqual(["theme.gbss", "theme.json"], archive.Entries.Select(entry => entry.FullName));
        Assert.True(archive.Entries.All(entry => entry.LastWriteTime.DateTime == new DateTime(1980, 1, 1)),
            "Theme package timestamps are not reproducible.");
    }
    var inspect = await RunCli("theme", "inspect", first);
    Assert.Equal(0, inspect.Code);
    Assert.Contains("Publisher: dev.test", inspect.Output);
    Assert.Contains("publisher identity is not authenticated", inspect.Output);
}

static async Task ThemeValidationSafety()
{
    using var temp = new TemporaryDirectory();
    var source = await CreateThemeSourceAsync(temp.Path, "dev.test.safe", "dev.test", "1.0.0");
    await File.WriteAllTextAsync(Path.Combine(source, "unused.gbss"), "button { color: #123456; }");
    var unused = await RunCli("theme", "validate", source);
    Assert.Equal(1, unused.Code);
    Assert.Contains("unreferenced_style", unused.Error);

    File.Delete(Path.Combine(source, "unused.gbss"));
    await File.WriteAllTextAsync(Path.Combine(source, "theme.gbss"), "button { background: url(https://bad.example/x); }");
    var unsafeStyle = await RunCli("theme", "validate", source);
    Assert.Equal(1, unsafeStyle.Code);
    Assert.Contains("unsafe_value", unsafeStyle.Error);

    await File.WriteAllTextAsync(Path.Combine(source, "theme.gbss"), "button { color: #ffffff; }");
    await File.WriteAllTextAsync(Path.Combine(source, "payload.exe"), "not executable");
    var executable = await RunCli("theme", "validate", source);
    Assert.Equal(1, executable.Code);
    Assert.Contains("unsupported_theme_file", executable.Error);

    var invalidIdentity = await RunCli("theme", "new", "Bad", "--output", Path.Combine(temp.Path, "bad"),
        "--id", "org.other.bad", "--publisher", "dev.test");
    Assert.Equal(2, invalidIdentity.Code);
    Assert.Contains("owned by its publisher", invalidIdentity.Error);
    var invalidPublisher = await RunCli("theme", "new", "Bad Publisher",
        "--output", Path.Combine(temp.Path, "bad-publisher"), "--publisher", "dev..test");
    Assert.Equal(2, invalidPublisher.Code);
    Assert.Contains("reverse-DNS", invalidPublisher.Error);
}

static async Task ThemeArchiveSafety()
{
    using var temp = new TemporaryDirectory();
    var manifest = ThemeManifestBytes("dev.test.attack", "dev.test", "1.0.0");
    var traversal = Path.Combine(temp.Path, "traversal.gbartheme");
    using (var archive = ZipFile.Open(traversal, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.gbss", "button { color: #fff; }"u8.ToArray());
        WriteArchiveEntry(archive, "../escape.gbss", "button { color: #000; }"u8.ToArray());
    }
    var traversalResult = await RunCli("theme", "validate", traversal);
    Assert.Equal(1, traversalResult.Code);
    Assert.Contains("invalid_path", traversalResult.Error);

    var collision = Path.Combine(temp.Path, "collision.gbartheme");
    using (var archive = ZipFile.Open(collision, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.gbss", "button { color: #fff; }"u8.ToArray());
        WriteArchiveEntry(archive, "THEME.GBSS", "button { color: #000; }"u8.ToArray());
    }
    var collisionResult = await RunCli("theme", "validate", collision);
    Assert.Equal(1, collisionResult.Code);
    Assert.Contains("path_collision", collisionResult.Error);

    var executable = Path.Combine(temp.Path, "executable.gbartheme");
    using (var archive = ZipFile.Open(executable, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.gbss", "button { color: #fff; }"u8.ToArray());
        WriteArchiveEntry(archive, "theme.dll", [0x4d, 0x5a]);
    }
    var executableResult = await RunCli("theme", "validate", executable);
    Assert.Equal(1, executableResult.Code);
    Assert.Contains("unsupported_theme_file", executableResult.Error);

    var symlink = Path.Combine(temp.Path, "symlink.gbartheme");
    using (var archive = ZipFile.Open(symlink, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.gbss", "button { color: #fff; }"u8.ToArray());
        var link = archive.CreateEntry("linked.gbss");
        link.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
        using var stream = link.Open();
        stream.Write("theme.gbss"u8);
    }
    var symlinkResult = await RunCli("theme", "validate", symlink);
    Assert.Equal(1, symlinkResult.Code);
    Assert.Contains("symlink_entry", symlinkResult.Error);
    Assert.True(!File.Exists(Path.Combine(temp.Path, "escape.gbss")), "Theme traversal wrote outside validation.");
}

static async Task ThemeInstallIsImmutable()
{
    using var temp = new TemporaryDirectory();
    var source = await CreateThemeSourceAsync(temp.Path, "dev.test.installed", "dev.test", "4.0.0");
    var package = Path.Combine(temp.Path, "installed.gbartheme");
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", package)).Code);
    var settingsRoot = Path.Combine(temp.Path, "settings");
    var install = await RunCli("theme", "install", package, "--settings-root", settingsRoot);
    Assert.Equal(0, install.Code);
    var installedFile = Path.Combine(settingsRoot, "themes", "dev.test.installed", "4.0.0", "theme.gbss");
    Assert.True(File.Exists(installedFile), "Theme entry was not installed.");
    var installedBytes = await File.ReadAllBytesAsync(installedFile);
    var listed = await RunCli("theme", "list", "--settings-root", settingsRoot);
    Assert.Equal(0, listed.Code);
    Assert.Contains("valid    dev.test.installed  4.0.0", listed.Output);
    Assert.Contains("[dev.test]", listed.Output);
    var duplicate = await RunCli("theme", "install", package, "--settings-root", settingsRoot);
    Assert.Equal(1, duplicate.Code);
    Assert.Contains("version_exists", duplicate.Error);
    Assert.SequenceEqual(installedBytes, await File.ReadAllBytesAsync(installedFile));

    for (var index = 0; index < ThemeCatalog.MaximumThemes - 1; index++)
    {
        var id = $"dev.test.filler-{index}";
        var directory = Path.Combine(settingsRoot, "themes", id, "1.0.0");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "theme.json"), ThemeManifestBytes(id, "dev.test", "1.0.0"));
        await File.WriteAllTextAsync(Path.Combine(directory, "theme.gbss"), "button { color: #ffffff; }");
    }
    var overflowSource = await CreateThemeSourceAsync(temp.Path, "dev.test.overflow", "dev.test", "1.0.0");
    var overflowPackage = Path.Combine(temp.Path, "overflow.gbartheme");
    Assert.Equal(0, (await RunCli("theme", "pack", overflowSource, "--output", overflowPackage)).Code);
    var overflow = await RunCli("theme", "install", overflowPackage, "--settings-root", settingsRoot);
    Assert.Equal(1, overflow.Code);
    Assert.Contains("too_many_themes", overflow.Error);
}

static async Task ThemeRemoteInstall()
{
    using var temp = new TemporaryDirectory();
    var source = await CreateThemeSourceAsync(temp.Path, "dev.test.remote-theme", "dev.test", "1.2.0");
    var package = Path.Combine(temp.Path, "remote.gbartheme");
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", package)).Code);
    var payload = await File.ReadAllBytesAsync(package);
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    var requests = 0;
    string? requestedUri = null;
    using var handler = new StubHttpHandler((request, _) =>
    {
        requests++;
        requestedUri = request.RequestUri!.AbsoluteUri;
        return Task.FromResult(Response(HttpStatusCode.OK, payload));
    });
    var settings = Path.Combine(temp.Path, "settings");

    var missingHash = await RunCliWithHandler(handler, "theme", "install",
        "https://themes.example/remote.gbartheme", "--settings-root", settings);
    Assert.Equal(2, missingHash.Code);
    Assert.Contains("requires --sha256", missingHash.Error);
    Assert.Equal(0, requests);

    var installed = await RunCliWithHandler(handler, "theme", "install",
        "github:sample-org/themes@v1.2.0/remote.gbartheme",
        "--sha256", hash, "--settings-root", settings);
    Assert.Equal(0, installed.Code);
    Assert.Contains("Installed dev.test.remote-theme 1.2.0", installed.Output);
    Assert.Contains($"Downloaded SHA-256: {hash.ToLowerInvariant()}", installed.Output);
    Assert.Equal("https://github.com/sample-org/themes/releases/download/v1.2.0/remote.gbartheme", requestedUri);
    Assert.Equal(1, requests);
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

    await File.WriteAllBytesAsync(path, [0xff]);
    var invalidEncoding = await RunCli("validate", path);
    Assert.Equal(1, invalidEncoding.Code);
    Assert.Contains("invalid_encoding", invalidEncoding.Error);
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

static Task DevSourceDiscoveryIsScoped()
{
    using var temp = new TemporaryDirectory();
    var root = Path.Combine(temp.Path, "ScopedWidget");
    Directory.CreateDirectory(Path.Combine(root, "src", "nested"));
    Directory.CreateDirectory(Path.Combine(root, "styles"));
    Directory.CreateDirectory(Path.Combine(root, "assets"));
    Directory.CreateDirectory(Path.Combine(root, "build"));
    Directory.CreateDirectory(Path.Combine(root, "native"));
    Directory.CreateDirectory(Path.Combine(root, ".vs"));
    Directory.CreateDirectory(Path.Combine(root, "bin", "Debug"));
    Directory.CreateDirectory(Path.Combine(root, "obj"));
    File.WriteAllText(Path.Combine(root, "ScopedWidget.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    File.WriteAllBytes(Path.Combine(root, "manifest.json"), ManifestJson.Serialize(
        BuildManifest("dev.test.scoped", "dev.test", "1.0.0")));
    File.WriteAllText(Path.Combine(root, "src", "Widget.cs"), "class Widget { }");
    File.WriteAllText(Path.Combine(root, "src", "nested", "State.cs"), "class State { }");
    File.WriteAllText(Path.Combine(root, "styles", "default.gbss"), "text { color: #fff; }");
    File.WriteAllText(Path.Combine(root, "settings.json"), "{ \"enabled\": true }");
    File.WriteAllText(Path.Combine(root, "Labels.resx"), "<root />");
    File.WriteAllBytes(Path.Combine(root, "assets", "icon.png"), [0x89, 0x50, 0x4e, 0x47]);
    File.WriteAllText(Path.Combine(root, "build", "local.props"), "<Project />");
    File.WriteAllText(Path.Combine(root, "build", "local.targets"), "<Project />");
    File.WriteAllText(Path.Combine(root, "native", "widget.cpp"), "void render() {}");
    File.WriteAllText(Path.Combine(root, ".vs", "hidden.json"), "{}");
    File.WriteAllText(Path.Combine(root, "bin", "Debug", "Generated.cs"), "class Generated { }");
    File.WriteAllText(Path.Combine(root, "obj", "AssemblyInfo.cs"), "class AssemblyInfo { }");

    var source = DevWidgetSource.Discover(root);
    var files = DevSourceWatcher.Capture(source);
    Assert.Equal(DevWidgetSourceKind.Project, source.Kind);
    Assert.True(files.Contains(Path.Combine(root, "src", "Widget.cs")), "Declared C# source was omitted.");
    Assert.True(files.Contains(Path.Combine(root, "src", "nested", "State.cs")), "Nested source was omitted.");
    Assert.True(files.Contains(Path.Combine(root, "styles", "default.gbss")), "GBSS source was omitted.");
    foreach (var relative in new[]
             {
                 "settings.json", "Labels.resx", Path.Combine("assets", "icon.png"),
                 Path.Combine("build", "local.props"), Path.Combine("build", "local.targets"),
                 Path.Combine("native", "widget.cpp"),
             })
        Assert.True(files.Contains(Path.Combine(root, relative)), $"MSBuild input {relative} was omitted.");
    Assert.True(!files.Any(path => path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase)), "Build output leaked into the watch set.");
    Assert.True(!files.Any(path => path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase)), "Intermediate output leaked into the watch set.");
    Assert.True(!files.Any(path => path.Contains(Path.DirectorySeparatorChar + ".vs" + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase)), "Visual Studio state leaked into the watch set.");
    return Task.CompletedTask;
}

static async Task DevPackageWatchingIsComplete()
{
    using var temp = new TemporaryDirectory();
    var root = CreatePackageSource(temp.Path, "dev.test.package-watch", "dev.test", "1.0.0");
    Directory.CreateDirectory(Path.Combine(root, "assets"));
    File.WriteAllText(Path.Combine(root, "assets", "cover.txt"), "asset");
    File.WriteAllText(Path.Combine(root, "payload", "support.dat"), "support");
    File.Delete(Path.Combine(root, "styles", "default.gbss"));
    var source = DevWidgetSource.Discover(root);
    var files = DevSourceWatcher.Capture(source);
    Assert.True(files.Contains(Path.Combine(root, "assets", "cover.txt")),
        "Package asset consumed by PackAsync was omitted from the watch set.");
    Assert.True(files.Contains(Path.Combine(root, "payload", "support.dat")),
        "Supporting payload consumed by PackAsync was omitted from the watch set.");

    var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using (var watcher = DevSourceWatcher.Create(source, () => changed.TrySetResult()))
    {
        Assert.True(watcher.WatchedDirectories.Contains(Path.Combine(root, "styles")),
            "Initially empty styles directory was not watched.");
        await File.WriteAllTextAsync(Path.Combine(root, "styles", "new.gbss"),
            "text { color: #fff; }");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    var projectRoot = Path.Combine(temp.Path, "ProjectWatch");
    Assert.Equal(0, (await RunCli("new", "widget", "ProjectWatch", "--output", projectRoot,
        "--id", "dev.test.project-watch", "--publisher", "dev.test")).Code);
    var existingContent = Path.Combine(projectRoot, "widget-data.json");
    await File.WriteAllTextAsync(existingContent, "{ \"revision\": 1 }");
    using var projectChanged = new SemaphoreSlim(0);
    using var projectWatcher = DevSourceWatcher.Create(
        DevWidgetSource.Discover(projectRoot), () => projectChanged.Release());
    await File.WriteAllTextAsync(existingContent, "{ \"revision\": 2 }");
    Assert.True(await projectChanged.WaitAsync(TimeSpan.FromSeconds(3)),
        "An existing JSON MSBuild input did not trigger a rebuild.");
    while (projectChanged.Wait(0)) { }
    var newDirectory = Path.Combine(projectRoot, "new-feature");
    Directory.CreateDirectory(newDirectory);
    var newResource = Path.Combine(newDirectory, "Feature.resx");
    await File.WriteAllTextAsync(newResource, "<root />");
    Assert.True(await projectChanged.WaitAsync(TimeSpan.FromSeconds(3)),
        "A newly created resource directory did not trigger a rebuild.");
    projectWatcher.Refresh();
    Assert.True(projectWatcher.WatchedDirectories.Contains(newDirectory),
        "New project source directory was not adopted by bounded non-recursive watchers.");
    Assert.True(projectWatcher.Files.Contains(newResource),
        "New non-C# MSBuild input was not adopted after refresh.");
}

static async Task DevBuildsIsolatedPackage()
{
    using var temp = new TemporaryDirectory();
    var sourceRoot = Path.Combine(temp.Path, "DevPanel");
    var scaffold = await RunCli("new", "widget", "DevPanel", "--output", sourceRoot,
        "--id", "dev.test.dev-panel", "--publisher", "dev.test");
    Assert.Equal(0, scaffold.Code);
    var source = DevWidgetSource.Discover(sourceRoot);
    var generation = Path.Combine(temp.Path, "generation");
    Directory.CreateDirectory(generation);
    using var output = new StringWriter();
    using var error = new StringWriter();
    var prepared = await DevGenerationBuilder.PrepareAsync(
        source, generation, "Release", TimeSpan.FromSeconds(90), output, error,
        CancellationToken.None);
    Assert.True(File.Exists(prepared.PackagePath), "Dev build did not create an immutable package.");
    Assert.Equal("dev.test.dev-panel", prepared.Manifest.Id);
    var inspection = await new GameBarAlternative.WidgetCatalog.WidgetCatalog(
            Path.Combine(temp.Path, "validation"))
        .CreateInstaller().ValidateAsync(prepared.PackagePath);
    Assert.Equal("dev.test.dev-panel", inspection.Id);
    Assert.True(inspection.Manifest.Entrypoint.Assembly.StartsWith("payload/", StringComparison.Ordinal),
        "Dev entrypoint did not remain in the isolated package payload.");
}

static Task DevDiagnosticsAreSanitized()
{
    var message = DevSession.SafeMessage(new Exception("first\r\nsecond\0" + new string('x', 2_000)));
    Assert.True(!message.Contains('\r') && !message.Contains('\n') && !message.Contains('\0'),
        "Dev diagnostic retained control characters.");
    Assert.True(message.Length <= 1_001, "Dev diagnostic exceeded its output bound.");
    return Task.CompletedTask;
}

static Task DevBuildDisablesPersistentServers()
{
    var project = Path.Combine("C:\\source", "Widget.csproj");
    var output = Path.Combine("C:\\staging", "payload");
    var start = DevGenerationBuilder.CreateBuildStartInfo(project, "Release", output);
    var arguments = start.ArgumentList.ToArray();

    Assert.True(arguments.Contains("--disable-build-servers", StringComparer.Ordinal),
        "Dev builds did not disable persistent build servers.");
    Assert.True(arguments.Contains("--property:UseSharedCompilation=false", StringComparer.Ordinal),
        "Dev builds did not disable the persistent Roslyn compiler server.");
    Assert.True(arguments.Contains("--property:BuildInParallel=false", StringComparer.Ordinal),
        "Dev builds did not bound nested build concurrency.");
    Assert.True(arguments.Contains("--property:MSBuildNodeReuse=false", StringComparer.Ordinal),
        "Dev builds did not disable MSBuild node reuse.");
    Assert.Equal("1", start.Environment["MSBUILDDISABLENODEREUSE"]);
    Assert.Equal(Path.GetDirectoryName(project), start.WorkingDirectory);
    Assert.True(start.RedirectStandardOutput && start.RedirectStandardError,
        "Dev build diagnostics must remain captured after process isolation was enabled.");
    return Task.CompletedTask;
}

static async Task DevReadinessFailsClosed()
{
    foreach (var suffix in new[] { "no-ready", "forged-ready" })
    {
        using var temp = new TemporaryDirectory();
        var sourceRoot = Path.Combine(temp.Path, "ReadinessPanel");
        Assert.Equal(0, (await RunCli("new", "widget", "ReadinessPanel", "--output", sourceRoot,
            "--id", $"dev.test.{suffix}", "--publisher", "dev.test")).Code);
        var outputBuffer = new StringWriter();
        var errorBuffer = new StringWriter();
        await using var session = new DevSession(
            DevWidgetSource.Discover(sourceRoot), Environment.ProcessPath!, "Release",
            TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(75),
            TextWriter.Synchronized(outputBuffer), TextWriter.Synchronized(errorBuffer),
            readyTimeout: TimeSpan.FromSeconds(2));
        // Disabling persistent build servers intentionally makes a cold nested
        // build slightly slower. Keep the authentication deadline at two
        // seconds, but give the isolated build a scheduler-safe test budget.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = session.RunAsync(cancellation.Token);
        await WaitUntilAsync(() => errorBuffer.ToString().Contains(
                suffix == "no-ready" ? "did not authenticate" : "did not authenticate the exact",
                StringComparison.Ordinal), TimeSpan.FromSeconds(15));
        Assert.True(session.ActiveHostProcessId is null,
            "Unauthenticated candidate replaced the active host.");
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
    }
}

static async Task DevRetainsAndCleans()
{
    using var temp = new TemporaryDirectory();
    var sourceRoot = Path.Combine(temp.Path, "LivePanel");
    Assert.Equal(0, (await RunCli("new", "widget", "LivePanel", "--output", sourceRoot,
        "--id", "dev.test.live-panel", "--publisher", "dev.test")).Code);
    var outputBuffer = new StringWriter();
    var errorBuffer = new StringWriter();
    var output = TextWriter.Synchronized(outputBuffer);
    var error = TextWriter.Synchronized(errorBuffer);
    var session = new DevSession(
        DevWidgetSource.Discover(sourceRoot), Environment.ProcessPath!, "Release",
        TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(75), output, error);
    var sessionRoot = session.SessionRoot;
    // Dev intentionally disables every persistent build/compiler server. A
    // cold Release generation can exceed ten seconds on a busy test host, so
    // keep the product's 90-second build bound while giving this integration
    // fixture enough scheduler headroom to observe both generations.
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var run = session.RunAsync(cancellation.Token);
    int? activePid = null;
    try
    {
        await WaitUntilAsync(() => outputBuffer.ToString().Contains("Ready:", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30));
        var firstPid = session.ActiveHostProcessId;
        activePid = firstPid;
        Assert.True(firstPid.HasValue, "Dev host was not retained after the first good build.");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "styles", "default.gbss"),
            "button { background: url(https://unsafe.example/x); }");
        await WaitUntilAsync(() => errorBuffer.ToString().Contains("Retained the last-good", StringComparison.Ordinal),
            TimeSpan.FromSeconds(5));
        Assert.Equal(firstPid, session.ActiveHostProcessId);
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
    }
    finally
    {
        cancellation.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await session.DisposeAsync();
    }
    Assert.True(!Directory.Exists(sessionRoot), "Dev cancellation leaked its temporary catalog.");
    Assert.True(activePid.HasValue, "Dev integration did not capture the child host PID.");
    AssertProcessExited(activePid.GetValueOrDefault());
}

static async Task DevBrokenEntrypointRetainsLastGood()
{
    using var temp = new TemporaryDirectory();
    var sourceRoot = Path.Combine(temp.Path, "EntrypointPanel");
    Assert.Equal(0, (await RunCli("new", "widget", "EntrypointPanel", "--output", sourceRoot,
        "--id", "dev.test.entrypoint-panel", "--publisher", "dev.test")).Code);
    var outputBuffer = new StringWriter();
    var errorBuffer = new StringWriter();
    var session = new DevSession(
        DevWidgetSource.Discover(sourceRoot), Environment.ProcessPath!, "Release",
        TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(75),
        TextWriter.Synchronized(outputBuffer), TextWriter.Synchronized(errorBuffer),
        readyTimeout: TimeSpan.FromMilliseconds(250));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var run = session.RunAsync(cancellation.Token);
    int? lastGoodPid = null;
    try
    {
        await WaitUntilAsync(() => outputBuffer.ToString().Contains("Ready:", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30));
        lastGoodPid = session.ActiveHostProcessId;
        Assert.True(lastGoodPid.HasValue, "Initial valid entrypoint did not publish a last-good host.");
        var expectedPid = lastGoodPid.GetValueOrDefault();

        var manifestPath = Path.Combine(sourceRoot, "manifest.json");
        var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(manifestPath));
        await File.WriteAllBytesAsync(manifestPath, ManifestJson.Serialize(manifest with
        {
            Entrypoint = manifest.Entrypoint with { Type = "Missing.Widget" },
        }));

        await WaitUntilAsync(() => errorBuffer.ToString().Contains("did not authenticate", StringComparison.Ordinal),
            TimeSpan.FromSeconds(15));
        Assert.Equal(lastGoodPid, session.ActiveHostProcessId);
        Assert.True(!Process.GetProcessById(expectedPid).HasExited,
            "Broken entrypoint probe retired the last-good host.");
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
    }
    finally
    {
        cancellation.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await session.DisposeAsync();
    }
    Assert.True(lastGoodPid.HasValue, "Entrypoint retention test did not capture the last-good PID.");
    AssertProcessExited(lastGoodPid.GetValueOrDefault());
}

static async Task DevJobReclaimsDescendants()
{
    using var temp = new TemporaryDirectory();
    var sourceRoot = Path.Combine(temp.Path, "TreePanel");
    Assert.Equal(0, (await RunCli("new", "widget", "TreePanel", "--output", sourceRoot,
        "--id", "dev.test.descendant-tree", "--publisher", "dev.test")).Code);
    var outputBuffer = new StringWriter();
    var errorBuffer = new StringWriter();
    var session = new DevSession(
        DevWidgetSource.Discover(sourceRoot), Environment.ProcessPath!, "Release",
        TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(75),
        TextWriter.Synchronized(outputBuffer), TextWriter.Synchronized(errorBuffer));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var run = session.RunAsync(cancellation.Token);
    var processIds = new List<int>();
    try
    {
        await WaitUntilAsync(() => outputBuffer.ToString().Contains("Ready:", StringComparison.Ordinal),
            TimeSpan.FromSeconds(30));
        var hostPid = session.ActiveHostProcessId;
        Assert.True(hostPid.HasValue, "Descendant test did not retain its interactive host.");
        processIds.Add(hostPid.GetValueOrDefault());
        var descendantPath = Path.Combine(session.SessionRoot, "generation-000001", "descendants.txt");
        await WaitUntilAsync(() => File.Exists(descendantPath), TimeSpan.FromSeconds(3));
        processIds.AddRange((await File.ReadAllLinesAsync(descendantPath)).Select(int.Parse));
        Assert.Equal(3, processIds.Distinct().Count());
        foreach (var processId in processIds)
            Assert.True(!Process.GetProcessById(processId).HasExited,
                $"Fake process PID {processId} was not alive before cleanup.");

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        await session.DisposeAsync();
        foreach (var processId in processIds) AssertProcessExited(processId);
    }
    finally
    {
        cancellation.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        try { await session.DisposeAsync(); } catch (CliOperationException) { }
        foreach (var processId in processIds) ForceStopProcess(processId);
    }
}

static void AssertProcessExited(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        Assert.True(process.HasExited, $"Child host PID {processId} survived session cleanup.");
    }
    catch (ArgumentException)
    {
        // The PID no longer exists, which is the expected process-tree state.
    }
}

static void ForceStopProcess(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
    catch (ArgumentException)
    {
        // Already reclaimed.
    }
    catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
    {
        // Best-effort fallback used only after the real cleanup assertion has
        // already succeeded or failed.
    }
}

static string DevelopmentArgument(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Length)
        throw new ArgumentException($"Missing fake development-host argument {name}.");
    return values[index + 1];
}

static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!predicate())
    {
        if (DateTime.UtcNow >= deadline) throw new TimeoutException("Timed out waiting for dev session state.");
        await Task.Delay(25);
    }
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
    Assert.Contains("Active input scope: root", result.Output);
    Assert.Contains("▶ Button #apply", result.Output);
    Assert.Contains("focusPersistence=transport.apply", result.Output);
    Assert.Contains("a11y=\"Apply\"", result.Output);
    Assert.Contains("focus=[left:lower,right:raise]", result.Output);
    Assert.Contains("RightBumper:raise", result.Output);
}

static async Task RenderFailsClosed()
{
    using var temp = new TemporaryDirectory();
    var assemblyPath = Path.Combine(temp.Path, "untrusted-widget.dll");
    await File.WriteAllBytesAsync(assemblyPath, [0x4d, 0x5a, 0x00, 0x00]);
    var destination = Path.Combine(temp.Path, "preserve.snapshot.json");
    await File.WriteAllTextAsync(destination, "preserve-existing-output");

    var assembly = await RunCli(
        "render", assemblyPath,
        "--type", "Untrusted.Widget",
        "--instance", "untrusted.instance",
        "--output", destination);
    Assert.Equal(1, assembly.Code);
    Assert.Contains("never loads author code into the CLI process", assembly.Error);
    Assert.Contains("Use gbar dev for isolated AppContainer execution", assembly.Error);
    Assert.DoesNotContain("Untrusted.Widget", assembly.Error);
    Assert.DoesNotContain("untrusted-widget.dll", assembly.Error);
    Assert.Equal("preserve-existing-output", await File.ReadAllTextAsync(destination));

    var validPath = Path.Combine(temp.Path, "valid.json");
    await File.WriteAllBytesAsync(validPath, SnapshotJson.Serialize(BuildSnapshot()));
    var obsoleteOptions = await RunCli(
        "render", validPath, "--type", "Legacy.Widget", "--output", destination);
    Assert.Equal(2, obsoleteOptions.Code);
    Assert.Contains("--type and --instance are unavailable", obsoleteOptions.Error);
    Assert.Equal("preserve-existing-output", await File.ReadAllTextAsync(destination));

    var oversizedPath = Path.Combine(temp.Path, "oversized.json");
    await File.WriteAllBytesAsync(
        oversizedPath,
        new byte[RenderCommand.MaximumSnapshotBytes + 1]);
    var oversized = await RunCli("render", oversizedPath, "--output", destination);
    Assert.Equal(1, oversized.Code);
    Assert.Contains(
        $"between 1 and {RenderCommand.MaximumSnapshotBytes} bytes",
        oversized.Error);
    Assert.Equal("preserve-existing-output", await File.ReadAllTextAsync(destination));

    await using var growing = new MisreportedReadStream(
        actualLength: RenderCommand.MaximumSnapshotBytes + 1L,
        reportedLength: 1);
    var growth = await Assert.ThrowsAsync<CliOperationException>(
        () => RenderCommand.ReadSnapshotAsync(growing));
    Assert.Contains(
        $"between 1 and {RenderCommand.MaximumSnapshotBytes} bytes",
        growth.Message);
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
    Assert.Contains("disabled  dev.test.local  2.0.0", listed.Output);
    Assert.Equal(0, (await RunCli("enable", "dev.test.local", "--catalog", catalog)).Code);
    Assert.Contains("enabled   dev.test.local", (await RunCli("list", "--catalog", catalog)).Output);
    Assert.Equal(0, (await RunCli("disable", "dev.test.local", "--catalog", catalog)).Code);
    Assert.Contains("disabled  dev.test.local", (await RunCli("list", "--catalog", catalog)).Output);
    Assert.Equal(0, (await RunCli("enable", "dev.test.local", "--catalog", catalog)).Code);
    Assert.Contains("enabled   dev.test.local", (await RunCli("list", "--catalog", catalog)).Output);

    var updateSource = CreatePackageSource(temp.Path, "dev.test.local", "dev.test", "3.0.0");
    var updatePackage = Path.Combine(temp.Path, "local-update.gbarwidget");
    Assert.Equal(0, (await RunCli("pack", updateSource, "--output", updatePackage)).Code);
    var blockedUpdate = await RunCli("install", updatePackage, "--catalog", catalog);
    Assert.Equal(1, blockedUpdate.Code);
    Assert.Contains("Disable it before installing an update", blockedUpdate.Error);
    Assert.True(!Directory.Exists(Path.Combine(catalog, "packages", "dev.test.local", "3.0.0")),
        "A local update bypassed disabled-only review.");
}

static async Task DirectoryShapeLimitsAreEnforced()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(
        temp.Path, "dev.test.directory-limit", "dev.test", "1.0.0");
    AddDeepAssets(source, 255);
    var tail = Path.Combine(source, "assets", "tail");
    Directory.CreateDirectory(tail);
    await File.WriteAllTextAsync(Path.Combine(tail, "asset.txt"), "x");
    Assert.Equal(1_025, CountRequiredDirectories(source));
    var package = Path.Combine(temp.Path, "directory-limit.gbarwidget");

    var pack = await RunCli("pack", source, "--output", package);
    Assert.Equal(1, pack.Code);
    Assert.Contains("too_many_launch_directories", pack.Error);
    Assert.True(!File.Exists(package),
        "An unlaunchable package shape left a published pack output.");

    var rawPackage = Path.Combine(temp.Path, "directory-limit-raw.gbarwidget");
    ZipFile.CreateFromDirectory(
        source, rawPackage, CompressionLevel.NoCompression, includeBaseDirectory: false);
    var catalog = Path.Combine(temp.Path, "catalog");
    var install = await RunCli("install", rawPackage, "--catalog", catalog);
    Assert.Equal(1, install.Code);
    Assert.Contains("too_many_launch_directories", install.Error);
    Assert.True(!Directory.Exists(Path.Combine(
            catalog, "packages", "dev.test.directory-limit")),
        "An unlaunchable package shape published installed bytes.");
    Assert.Equal(0, (await new GameBarAlternative.WidgetCatalog.WidgetCatalog(catalog)
        .DiscoverAsync()).Widgets.Count);
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

static async Task UninstallWorkflow()
{
    using var temp = new TemporaryDirectory();
    var catalog = Path.Combine(temp.Path, "catalog");
    foreach (var version in new[] { "1.0.0", "2.0.0" })
    {
        var package = await CreatePackedPackageAsync(temp.Path, "dev.test.uninstall", version);
        Assert.Equal(0, (await RunCli("install", package, "--catalog", catalog)).Code);
    }
    Assert.Equal(0, (await RunCli("enable", "dev.test.uninstall", "--catalog", catalog)).Code);
    var blocked = await RunCli("uninstall", "dev.test.uninstall", "--catalog", catalog);
    Assert.Equal(1, blocked.Code);
    Assert.Contains("Disable it before uninstalling", blocked.Error);

    Assert.Equal(0, (await RunCli("disable", "dev.test.uninstall", "--catalog", catalog)).Code);
    var removed = await RunCli("uninstall", "dev.test.uninstall", "--catalog", catalog);
    Assert.Equal(0, removed.Code);
    Assert.Contains("Uninstalled dev.test.uninstall (2 versions: 2.0.0, 1.0.0)", removed.Output);
    Assert.Contains("No widgets installed", (await RunCli("list", "--catalog", catalog)).Output);
    Assert.True(!Directory.Exists(Path.Combine(catalog, "packages", "dev.test.uninstall")),
        "CLI uninstall retained package files.");
    Assert.True(!Directory.EnumerateDirectories(Path.Combine(catalog, "staging"), ".uninstall-*").Any(),
        "CLI uninstall retained retired package files.");

    var missing = await RunCli("uninstall", "dev.test.uninstall", "--catalog", catalog);
    Assert.Equal(1, missing.Code);
    Assert.Contains("is not installed", missing.Error);
}

static async Task CatalogRepairWorkflow()
{
    using var temp = new TemporaryDirectory();
    var catalogRoot = Path.Combine(temp.Path, "repair-catalog");
    var permissive = new WidgetCatalog(catalogRoot, new WidgetCatalogOptions
    {
        MaximumVersionsPerWidget = 10,
    });
    for (var major = 1; major <= 9; major++)
    {
        var package = await CreatePackedPackageAsync(
            temp.Path, "dev.test.repair", $"{major}.0.0");
        await permissive.InstallAsync(package);
    }
    await permissive.SetEnabledAsync("dev.test.repair", true);
    await File.WriteAllTextAsync(Path.Combine(
        catalogRoot, "packages", "dev.test.repair", "9.0.0", "manifest.json"),
        "repair must not trust this manifest");

    var normalList = await RunCli("list", "--catalog", catalogRoot);
    Assert.Equal(1, normalList.Code);
    Assert.Contains("installed_widget_version_limit", normalList.Error);
    var repairList = await RunCli("repair", "list", "--catalog", catalogRoot);
    Assert.Equal(0, repairList.Code);
    Assert.Contains("Catalog directory quotas: exceeded (installed_widget_version_limit)",
        repairList.Output);
    Assert.Contains("enabled-selected-protected  dev.test.repair  1.0.0", repairList.Output);
    Assert.Contains("removable  dev.test.repair  9.0.0", repairList.Output);

    var selected = await RunCli(
        "repair", "remove", "dev.test.repair", "1.0.0", "--catalog", catalogRoot);
    Assert.Equal(1, selected.Code);
    Assert.Contains("selected_version", selected.Error);
    var removed = await RunCli(
        "repair", "remove", "dev.test.repair", "9.0.0", "--catalog", catalogRoot);
    Assert.Equal(0, removed.Code);
    Assert.Contains("Removed inactive version dev.test.repair 9.0.0", removed.Output);
    Assert.True(!Directory.Exists(Path.Combine(
            catalogRoot, "packages", "dev.test.repair", "9.0.0")),
        "CLI repair retained the exact retired version.");
    var recovered = await RunCli("list", "--catalog", catalogRoot);
    Assert.Equal(0, recovered.Code);
    Assert.Contains("(8 versions)", recovered.Output);

    var enabledHistory = await RunCli(
        "repair", "remove", "dev.test.repair", "8.0.0", "--catalog", catalogRoot);
    Assert.Equal(0, enabledHistory.Code);
    Assert.True(!Directory.Exists(Path.Combine(
            catalogRoot, "packages", "dev.test.repair", "8.0.0")),
        "CLI repair retained inactive history for an enabled package.");
    var activeOnly = await RunCli("list", "--catalog", catalogRoot);
    Assert.Equal(0, activeOnly.Code);
    Assert.Contains("enabled   dev.test.repair  1.0.0", activeOnly.Output);
    Assert.Contains("(7 versions)", activeOnly.Output);
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

static async Task RemoteDistributionWorkflow()
{
    using var temp = new TemporaryDirectory();
    var package = await CreatePackedPackageAsync(temp.Path, "dev.test.remote", "3.1.4");
    var payload = await File.ReadAllBytesAsync(package);
    var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    var requests = new List<string>();
    using var handler = new StubHttpHandler((request, _) =>
    {
        requests.Add(request.RequestUri!.AbsoluteUri);
        Assert.Contains("identity", string.Join(',', request.Headers.AcceptEncoding.Select(value => value.Value)));
        return Task.FromResult(Response(HttpStatusCode.OK, payload));
    });

    var catalog = Path.Combine(temp.Path, "catalog");
    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/release.gbarwidget",
        "--sha256", hash.ToUpperInvariant(), "--catalog", catalog);
    Assert.Equal(0, result.Code);
    Assert.Contains("Installed dev.test.remote 3.1.4", result.Output);
    Assert.Contains("(disabled)", result.Output);
    Assert.Contains($"Downloaded SHA-256: {hash}", result.Output);
    Assert.SequenceEqual(["https://widgets.example/release.gbarwidget"], requests);
    var listed = await RunCli("list", "--catalog", catalog);
    Assert.Contains("disabled  dev.test.remote", listed.Output);
}

static async Task RemoteUpdateRequiresDisable()
{
    using var temp = new TemporaryDirectory();
    var catalog = Path.Combine(temp.Path, "catalog");
    var first = await CreatePackedPackageAsync(temp.Path, "dev.test.remote-update", "1.0.0");
    Assert.Equal(0, (await RunCli("install", first, "--catalog", catalog)).Code);
    Assert.Equal(0, (await RunCli("enable", "dev.test.remote-update", "--catalog", catalog)).Code);

    var update = await CreatePackedPackageAsync(temp.Path, "dev.test.remote-update", "2.0.0");
    var payload = await File.ReadAllBytesAsync(update);
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromResult(Response(HttpStatusCode.OK, payload)));

    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/update.gbarwidget",
        "--sha256", hash, "--catalog", catalog);
    Assert.Equal(1, result.Code);
    Assert.Contains("Disable it before installing an update", result.Error);
    var preserved = await RunCli("list", "--catalog", catalog);
    Assert.Contains("enabled   dev.test.remote-update  1.0.0", preserved.Output);

    Assert.Equal(0, (await RunCli("disable", "dev.test.remote-update", "--catalog", catalog)).Code);
    var retry = await RunCliWithHandler(handler, "install", "https://widgets.example/update.gbarwidget",
        "--sha256", hash, "--catalog", catalog);
    Assert.Equal(0, retry.Code);
    Assert.Contains("Version 1.0.0 remains selected", retry.Output);
    var updated = await RunCli("list", "--catalog", catalog);
    Assert.Contains("disabled  dev.test.remote-update  1.0.0", updated.Output);
    Assert.Equal(0, (await RunCli("version", "select", "dev.test.remote-update", "2.0.0",
        "--catalog", catalog)).Code);
    Assert.Contains("disabled  dev.test.remote-update  2.0.0",
        (await RunCli("list", "--catalog", catalog)).Output);
}

static async Task VersionSelectionAndRollback()
{
    using var temp = new TemporaryDirectory();
    var catalog = Path.Combine(temp.Path, "catalog");
    foreach (var version in new[] { "1.0.0", "2.0.0", "3.0.0" })
    {
        var package = await CreatePackedPackageAsync(temp.Path, "dev.test.rollback", version);
        Assert.Equal(0, (await RunCli("install", package, "--catalog", catalog)).Code);
    }
    Assert.Equal(0, (await RunCli(
        "version", "select", "dev.test.rollback", "3.0.0", "--catalog", catalog)).Code);

    var versions = await RunCli("version", "list", "dev.test.rollback", "--catalog", catalog);
    Assert.Equal(0, versions.Code);
    Assert.Contains("active version 3.0.0", versions.Output);
    Assert.Contains("active  3.0.0", versions.Output);
    Assert.Contains("       2.0.0", versions.Output);

    Assert.Equal(0, (await RunCli("enable", "dev.test.rollback", "--catalog", catalog)).Code);
    var enabledRollback = await RunCli("version", "rollback", "dev.test.rollback", "--catalog", catalog);
    Assert.Equal(1, enabledRollback.Code);
    Assert.Contains("Disable it before", enabledRollback.Error);
    Assert.Contains("active version 3.0.0", (await RunCli(
        "version", "list", "dev.test.rollback", "--catalog", catalog)).Output);

    Assert.Equal(0, (await RunCli("disable", "dev.test.rollback", "--catalog", catalog)).Code);
    var rollback = await RunCli("version", "rollback", "dev.test.rollback", "--catalog", catalog);
    Assert.Equal(0, rollback.Code);
    Assert.Contains("from 3.0.0 to 2.0.0 (disabled)", rollback.Output);

    var explicitRollback = await RunCli(
        "version", "rollback", "dev.test.rollback", "--to", "1.0.0", "--catalog", catalog);
    Assert.Equal(0, explicitRollback.Code);
    Assert.Contains("from 2.0.0 to 1.0.0 (disabled)", explicitRollback.Output);

    var forward = await RunCli(
        "version", "select", "dev.test.rollback", "3.0.0", "--catalog", catalog);
    Assert.Equal(0, forward.Code);
    Assert.Contains("Selected dev.test.rollback 3.0.0 (disabled)", forward.Output);

    var newerPackage = await CreatePackedPackageAsync(temp.Path, "dev.test.rollback", "4.0.0");
    var newerInstall = await RunCli("install", newerPackage, "--catalog", catalog);
    Assert.Equal(0, newerInstall.Code);
    Assert.Contains("Version 3.0.0 remains selected", newerInstall.Output);
    var afterInstall = await RunCli("version", "list", "dev.test.rollback", "--catalog", catalog);
    Assert.Contains("active version 3.0.0", afterInstall.Output);
    Assert.Contains("       4.0.0", afterInstall.Output);

    var missing = await RunCli(
        "version", "select", "dev.test.rollback", "9.0.0", "--catalog", catalog);
    Assert.Equal(1, missing.Code);
    Assert.Contains("is not installed", missing.Error);
    var invalidDirection = await RunCli(
        "version", "rollback", "dev.test.rollback", "--to", "3.0.0", "--catalog", catalog);
    Assert.Equal(2, invalidDirection.Code);
    Assert.Contains("must be older", invalidDirection.Error);
}

static async Task GitHubShorthandResolves()
{
    using var temp = new TemporaryDirectory();
    var package = await CreatePackedPackageAsync(temp.Path, "dev.test.github", "1.0.0");
    var payload = await File.ReadAllBytesAsync(package);
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    string? requested = null;
    using var handler = new StubHttpHandler((request, _) =>
    {
        requested = request.RequestUri!.AbsoluteUri;
        return Task.FromResult(Response(HttpStatusCode.OK, payload));
    });
    var result = await RunCliWithHandler(handler, "install", "github:sample-org/game-bar-widget@v1.0.0/music.gbarwidget",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "catalog"));
    Assert.Equal(0, result.Code);
    Assert.Equal(
        "https://github.com/sample-org/game-bar-widget/releases/download/v1.0.0/music.gbarwidget",
        requested);

    var malformed = await RunCliWithHandler(handler, "install", "github:owner/repo@latest",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "unused"));
    Assert.Equal(2, malformed.Code);
    Assert.Contains("github:owner/repository@tag/asset.gbarwidget", malformed.Error);
}

static async Task RemoteRequiresHash()
{
    var requests = 0;
    using var handler = new StubHttpHandler((_, _) =>
    {
        requests++;
        return Task.FromResult(Response(HttpStatusCode.OK, []));
    });
    var missing = await RunCliWithHandler(handler, "install", "https://widgets.example/x.gbarwidget");
    Assert.Equal(2, missing.Code);
    Assert.Contains("requires --sha256", missing.Error);
    var malformed = await RunCliWithHandler(handler, "install", "https://widgets.example/x.gbarwidget", "--sha256", "1234");
    Assert.Equal(2, malformed.Code);
    Assert.Contains("exactly 64 hexadecimal", malformed.Error);
    Assert.Equal(0, requests);
}

static async Task RemoteRejectsUnsafeSources()
{
    using var handler = new StubHttpHandler((_, _) => throw new InvalidOperationException("Unsafe URL reached the network."));
    var hash = new string('0', 64);
    var unsafeSources = new[]
    {
        "http://widgets.example/x.gbarwidget",
        "ftp://widgets.example/x.gbarwidget",
        "https://user:secret@widgets.example/x.gbarwidget",
        "https://widgets.example/x.gbarwidget#fragment",
        "https://widgets.example:8443/x.gbarwidget",
        "https://localhost/x.gbarwidget",
        "https://127.0.0.1/x.gbarwidget",
        "https://10.1.2.3/x.gbarwidget",
        "https://169.254.1.1/x.gbarwidget",
        "https://172.20.1.1/x.gbarwidget",
        "https://192.168.1.1/x.gbarwidget",
        "https://[::1]/x.gbarwidget",
        "https://[fe80::1]/x.gbarwidget",
        "https://[fd00::1]/x.gbarwidget",
    };
    foreach (var source in unsafeSources)
    {
        var result = await RunCliWithHandler(handler, "install", source, "--sha256", hash);
        Assert.Equal(2, result.Code);
    }
}

static async Task RemoteRedirectsAreBounded()
{
    var calls = 0;
    using var redirectHandler = new StubHttpHandler((_, _) =>
    {
        calls++;
        var response = Response(HttpStatusCode.Found, []);
        response.Headers.Location = new Uri($"https://cdn.example/{calls}.gbarwidget");
        return Task.FromResult(response);
    });
    using var downloader = new RemotePackageDownloader(redirectHandler, new RemoteDownloadOptions
    {
        MaximumRedirects = 2,
    });
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/start.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("2-redirect limit", exception.Message);
    Assert.Equal(3, calls);

    using var unsafeRedirectHandler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.Found, []);
        response.Headers.Location = new Uri("http://cdn.example/package.gbarwidget");
        return Task.FromResult(response);
    });
    using var unsafeDownloader = new RemotePackageDownloader(unsafeRedirectHandler);
    var unsafeException = await Assert.ThrowsAsync<CliUsageException>(() => unsafeDownloader.DownloadAsync(
        new Uri("https://widgets.example/start.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("absolute HTTPS", unsafeException.Message);
}

static async Task RemoteDownloadIsBounded()
{
    using var temp = new TemporaryDirectory();
    using var headerHandler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.OK, [1]);
        response.Content.Headers.ContentLength = 9;
        return Task.FromResult(response);
    });
    using var headerDownloader = new RemotePackageDownloader(headerHandler, TestDownloadOptions(temp.Path, 8));
    var headerException = await Assert.ThrowsAsync<CliOperationException>(() => headerDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("Content-Length: 9", headerException.Message);

    using var streamHandler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(new RepeatingReadStream(9)),
    }));
    using var streamDownloader = new RemotePackageDownloader(streamHandler, TestDownloadOptions(temp.Path, 8));
    var streamException = await Assert.ThrowsAsync<CliOperationException>(() => streamDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("8-byte download limit", streamException.Message);

    using var emptyHandler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, [])));
    using var emptyDownloader = new RemotePackageDownloader(emptyHandler, TestDownloadOptions(temp.Path, 8));
    var emptyException = await Assert.ThrowsAsync<CliOperationException>(() => emptyDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("response was empty", emptyException.Message);
    Assert.True(!Directory.EnumerateFileSystemEntries(temp.Path).Any(), "Bounded downloads leaked temporary files.");
}

static async Task RemoteRejectsContentEncoding()
{
    using var handler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.OK, [1, 2, 3]);
        response.Content.Headers.ContentEncoding.Add("gzip");
        return Task.FromResult(response);
    });
    using var downloader = new RemotePackageDownloader(handler);
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("identity content encoding", exception.Message);
}

static async Task RemoteRejectsMalformedArchive()
{
    var payload = "not a widget archive"u8.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromResult(Response(HttpStatusCode.OK, payload)));
    using var temp = new TemporaryDirectory();

    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/broken.gbarwidget",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "catalog"));
    Assert.Equal(1, result.Code);
    Assert.Contains("invalid_archive", result.Error);
}

static async Task RemoteHashMismatchCleansUp()
{
    using var temp = new TemporaryDirectory();
    using var handler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, [1, 2, 3])));
    using var downloader = new RemotePackageDownloader(handler, TestDownloadOptions(temp.Path, 32));
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), new byte[32], CancellationToken.None));
    Assert.Contains("SHA-256 mismatch", exception.Message);
    Assert.True(!Directory.EnumerateFileSystemEntries(temp.Path).Any(), "Integrity failure leaked a package or directory.");
}

static async Task RemotePackageHasIntegrityGuard()
{
    using var temp = new TemporaryDirectory();
    var bytes = new byte[] { 1, 2, 3 };
    using var handler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, bytes)));
    using var downloader = new RemotePackageDownloader(handler, TestDownloadOptions(temp.Path, 32));
    var downloaded = await downloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), SHA256.HashData(bytes), CancellationToken.None);
    Assert.True(File.Exists(downloaded.PackagePath), "Downloaded package is missing.");
    await Assert.ThrowsAsync<IOException>(async () =>
    {
        await using var write = new FileStream(downloaded.PackagePath, FileMode.Open, FileAccess.Write, FileShare.Read);
    });
    await downloaded.DisposeAsync();
    Assert.True(!File.Exists(downloaded.PackagePath), "Disposed download was not deleted.");
    Assert.True(!Directory.EnumerateFileSystemEntries(temp.Path).Any(), "Disposed download directory was not deleted.");
}

static async Task RemoteTimeoutsAreBounded()
{
    using var responseHandler = new StubHttpHandler(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Response(HttpStatusCode.OK, []);
    });
    using var responseDownloader = new RemotePackageDownloader(responseHandler, new RemoteDownloadOptions
    {
        ResponseTimeout = TimeSpan.FromMilliseconds(20),
        OverallTimeout = TimeSpan.FromSeconds(2),
    });
    var responseException = await Assert.ThrowsAsync<CliOperationException>(() => responseDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("response timeout", responseException.Message);

    using var overallHandler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(new BlockingReadStream()),
    }));
    using var overallDownloader = new RemotePackageDownloader(overallHandler, new RemoteDownloadOptions
    {
        ResponseTimeout = TimeSpan.FromSeconds(2),
        OverallTimeout = TimeSpan.FromMilliseconds(20),
    });
    var overallException = await Assert.ThrowsAsync<CliOperationException>(() => overallDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.gbarwidget"), null, CancellationToken.None));
    Assert.Contains("overall timeout", overallException.Message);
}

static async Task RemoteFailuresRedactSecrets()
{
    const string secret = "super-secret-token";
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException(
            $"GET https://widgets.example/release.gbarwidget?token={secret} failed")));
    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/release.gbarwidget",
        "--sha256", new string('0', 64));
    Assert.Equal(1, result.Code);
    Assert.Contains("Remote package request failed", result.Error);
    Assert.DoesNotContain(secret, result.Error);
    Assert.DoesNotContain("?token=", result.Error);
}

static async Task RemoteCancellationIsBounded()
{
    using var handler = new StubHttpHandler(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        return Response(HttpStatusCode.OK, []);
    });
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await CliApplication.RunAsync(
        ["install", "https://widgets.example/release.gbarwidget", "--sha256", new string('0', 64)],
        output,
        error,
        handler,
        cancellation.Token);
    Assert.Equal(130, code);
    Assert.Contains("operation cancelled", error.ToString());
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
        (UI.Button("Apply", "apply", "apply")
            with { AccessibilityLabel = "Apply" })
            .PersistFocusAs("transport.apply")
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

static void AddDeepAssets(string source, int uniqueBranches)
{
    for (var index = 0; index < uniqueBranches; index++)
    {
        var directory = Path.Combine(
            source,
            "assets",
            $"edge-{index:000}",
            "one",
            "two",
            "three");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "asset.txt"), "x");
    }
}

static int CountRequiredDirectories(string source)
{
    var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
    };
    foreach (var file in Directory.EnumerateFiles(
                 source, "*", SearchOption.AllDirectories))
    {
        var segments = Path.GetRelativePath(source, file)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Split('/');
        for (var index = 1; index < segments.Length; index++)
            directories.Add(string.Join('/', segments.Take(index)));
    }
    return directories.Count;
}

static async Task<string> CreateThemeSourceAsync(string root, string id, string publisher, string version)
{
    var source = Path.Combine(root, $"theme-source-{Guid.NewGuid():N}");
    Directory.CreateDirectory(source);
    await File.WriteAllBytesAsync(Path.Combine(source, "theme.json"),
        ThemeManifestBytes(id, publisher, version));
    await File.WriteAllTextAsync(Path.Combine(source, "theme.gbss"),
        "panel { background: #10131a; } button:focused { outline-color: #ff7898; outline-width: 2px; }");
    return source;
}

static byte[] ThemeManifestBytes(string id, string publisher, string version) =>
    JsonSerializer.SerializeToUtf8Bytes(new ThemeManifestDocument
    {
        SchemaVersion = ThemeManifestDocument.CurrentSchemaVersion,
        Id = id,
        Publisher = publisher,
        Name = "CLI Test Theme",
        Version = version,
        EntryFile = "theme.gbss",
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

static async Task<string> CreatePackedPackageAsync(string root, string id, string version)
{
    var source = CreatePackageSource(root, id, "dev.test", version);
    var package = Path.Combine(root, $"{id}-{version}-{Guid.NewGuid():N}.gbarwidget");
    var result = await RunCli("pack", source, "--output", package);
    Assert.Equal(0, result.Code);
    return package;
}

static HttpResponseMessage Response(HttpStatusCode status, byte[] content) => new(status)
{
    Content = new ByteArrayContent(content),
};

static RemoteDownloadOptions TestDownloadOptions(string root, long maximumBytes) => new()
{
    MaximumBytes = maximumBytes,
    TemporaryDirectoryRoot = root,
};

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

static async Task<CliResult> RunAuthorityRecovery(
    IAuthorityRecoveryClient client,
    params string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    int code;
    try
    {
        code = await AuthorityRecoveryCommand.RunAsync(args, output, client);
    }
    catch (CliUsageException exception)
    {
        await error.WriteLineAsync($"error: {exception.Message}");
        code = 2;
    }
    catch (CliOperationException exception)
    {
        await error.WriteLineAsync($"error: {exception.Message}");
        code = 1;
    }
    return new CliResult(code, output.ToString(), error.ToString());
}

static async Task<CliResult> RunProcessAsync(
    string executable,
    IReadOnlyList<string> arguments,
    TimeSpan timeout)
{
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ??
        throw new InvalidOperationException($"Could not start {executable}.");
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var cancellation = new CancellationTokenSource(timeout);
    try
    {
        await process.WaitForExitAsync(cancellation.Token);
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        throw new TimeoutException($"{executable} exceeded {timeout.TotalSeconds:0} seconds.");
    }
    return new CliResult(process.ExitCode, await output, await error);
}

static async Task<CliResult> RunCliWithHandler(HttpMessageHandler handler, params string[] args)
{
    using var output = new StringWriter();
    using var error = new StringWriter();
    var code = await CliApplication.RunAsync(args, output, error, handler);
    return new CliResult(code, output.ToString(), error.ToString());
}

file sealed record CliResult(int Code, string Output, string Error);

file sealed class TestAuthorityRecoveryClient(
    IReadOnlyList<AuthorityRecoverySummary>? pending = null,
    AuthorityRecoveryClientException? retryFailure = null,
    AuthorityRecoveryClientException? listFailure = null)
    : IAuthorityRecoveryClient
{
    public int RetryCount { get; private set; }
    public string? LastRetryToken { get; private set; }

    public IReadOnlyList<AuthorityRecoverySummary> ListPending(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return listFailure is null
            ? pending ?? []
            : throw listFailure;
    }

    public void Retry(
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RetryCount++;
        LastRetryToken = confirmationToken;
        if (retryFailure is not null) throw retryFailure;
    }
}

file sealed class StubHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        send(request, cancellationToken);
}

file sealed class RepeatingReadStream : Stream
{
    private readonly long _length;
    private long _remaining;

    public RepeatingReadStream(long length)
    {
        _length = length;
        _remaining = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = (int)Math.Min(count, _remaining);
        Array.Fill(buffer, (byte)0x5A, offset, read);
        _remaining -= read;
        return read;
    }
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = (int)Math.Min(buffer.Length, _remaining);
        buffer.Span[..read].Fill(0x5A);
        _remaining -= read;
        return ValueTask.FromResult(read);
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

file sealed class MisreportedReadStream : Stream
{
    private readonly long _actualLength;
    private readonly long _reportedLength;
    private long _remaining;

    internal MisreportedReadStream(long actualLength, long reportedLength)
    {
        _actualLength = actualLength;
        _reportedLength = reportedLength;
        _remaining = actualLength;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _reportedLength;
    public override long Position
    {
        get => _actualLength - _remaining;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = (int)Math.Min(count, _remaining);
        Array.Fill(buffer, (byte)0x5A, offset, read);
        _remaining -= read;
        return read;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = (int)Math.Min(buffer.Length, _remaining);
        buffer.Span[..read].Fill(0x5A);
        _remaining -= read;
        return ValueTask.FromResult(read);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

file sealed class BlockingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

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
        if (Directory.Exists(Path))
            _ = DevSession.DeleteTreeWithRetriesAsync(Path).GetAwaiter().GetResult();
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

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException exception) { return exception; }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, got {exception.GetType().Name}: {exception.Message}");
        }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
