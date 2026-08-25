using System.IO.Compression;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using WidgetRail.WrailCli;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetCatalog;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

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
    var installed = await new WidgetRail.WidgetCatalog.WidgetCatalog(catalog).DiscoverAsync();
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
        var payload = $"wrail-dev-ready-v1\n{nonce}\n{catalog}\n{widgetId}\n{instance}\n";
        var temporary = readyPath + ".tmp";
        await File.WriteAllTextAsync(temporary, payload);
        File.Move(temporary, readyPath);
    }
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 0;
}

if (args.Contains("--game-launcher-community-reference", StringComparer.Ordinal))
{
    await ExternalGameLauncherCommunityReference();
    Console.WriteLine("PASS Game Launcher public SDK Community repository");
    return 0;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Help describes the complete workflow", HelpWorks),
    ("Authority recovery is exact, stale-safe, and sanitized", AuthorityRecoveryWorkflow),
    ("Widget config is package scoped and rejects secrets", WidgetConfigWorkflow),
    ("New scaffolds a token-free controller widget", NewScaffolds),
    ("New ships four deterministic template profiles", AdvancedTemplateProfiles),
    ("Media template authors typed pinned layouts end to end", MediaTemplatePinnedLayouts),
    ("New validates a bounded versioned template transaction", ScaffoldTransactionScenarios.Run),
    ("CLI template and WidgetSdk form one release unit", WidgetSdkReleaseUnitScenarios.Run),
    ("Built wrail artifacts support an isolated external SDK consumer", ExternalVersionedSdkConsumer),
    ("External repository completes full application onboarding and author loop", ExternalFullApplicationOnboarding),
    ("Game Launcher exports as a public SDK Community repository", ExternalGameLauncherCommunityReference),
    ("Generated widget completes the offline external package journey", NewScaffoldsOutsideCheckout),
    ("New rejects invalid package identity before writing", NewRejectsIdentity),
    ("Theme commands provide a deterministic end-to-end author workflow", ThemeWorkflow),
    ("Theme validation rejects unsafe content and unreachable styles", ThemeValidationSafety),
    ("Theme archives reject traversal collisions and executable content", ThemeArchiveSafety),
    ("Theme installation is immutable and catalog-compatible", ThemeInstallIsImmutable),
    ("Theme removal is exact protected and shares catalog policy", ThemeRemovalIsExact),
    ("Remote theme installation requires and verifies a pinned release asset", ThemeRemoteInstall),
    ("Validate accepts a scaffolded widget", ValidateScaffold),
    ("Validate rejects unsafe WRSS", ValidateRejectsUnsafeWrss),
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
    ("Scenario manifests are bounded and isolated execution is declared", ScenarioPreviewTests.Run),
    ("Controller replay follows focus and shortcuts", ReplayFocusAndActions),
    ("Snapshot preview exposes cursor anchors without artwork authority", CursorPreviewIsOpaque),
    ("Pack produces reproducible catalog-valid archives", PackIsReproducible),
    ("Source pack failures identify the required author action", SourcePackFailureIsActionable),
    ("Pack and install reject unlaunchable directory shapes before publication", DirectoryShapeLimitsAreEnforced),
    ("Install list disable and enable form a local distribution workflow", LocalDistributionWorkflow),
    ("Full-trust install and enable require explicit disclosed approval", FullTrustCliConsent),
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

if (args is ["--test", var exactName])
{
    tests = tests.Where(test => string.Equals(
        test.Name, exactName, StringComparison.Ordinal)).ToArray();
    if (tests.Length == 0)
    {
        Console.Error.WriteLine($"Unknown exact Wrail CLI test: {exactName}");
        return 2;
    }
}
else if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: WrailCli.Tests [--test <exact-name>]");
    return 2;
}

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
    const string profile = "WidgetRail.Widget.0123456789ABCDEF";
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
    const string widget = "widgetrail.samples.spotify";
    const string publisher = "widgetrail.samples";
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
        .Where(path => !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        .Select(File.ReadAllText));
    Assert.DoesNotContain("{{", allText);
    Assert.Contains("dev.test.media-deck", allText);
    Assert.Contains("ControllerButton.RightBumper", allText);
    Assert.Contains("Template: basic", result.Output);
    var project = File.ReadAllText(Path.Combine(destination, "MediaDeck.csproj"));
    Assert.Contains("PackageReference Include=\"WidgetRail.WidgetSdk\"", project);
    Assert.DoesNotContain("ProjectReference", project);
    var expectedSdk = LocalWidgetSdkPackage.Create();
    Assert.Contains($"Version=\"{expectedSdk.Version}\"", project);
    var sdkPackage = Path.Combine(destination, ".widgetrail", "packages",
        expectedSdk.FileName);
    Assert.True(File.Exists(sdkPackage), "The offline SDK package was not scaffolded.");
    Assert.SequenceEqual(
        await File.ReadAllBytesAsync(sdkPackage), expectedSdk.Content);
    await AssertArchiveHasNoPathsAsync(sdkPackage, Environment.CurrentDirectory);
    var nuget = await File.ReadAllTextAsync(Path.Combine(destination, "NuGet.Config"));
    Assert.Contains("<clear />", nuget);
    Assert.Contains(".widgetrail/packages", nuget);
    Assert.True(File.Exists(Path.Combine(destination, "tests", "MediaDeck.Tests.csproj")),
        "The generated lifecycle scenario was not created.");
    var manifest = ManifestJson.Deserialize(
        await File.ReadAllBytesAsync(Path.Combine(destination, "manifest.json")));
    Assert.Equal(WidgetGlyph.Connection, manifest.Presentation.Icon);
    Assert.Equal(WidgetResidencyPolicies.UnloadAfterIdle, manifest.ResidencyPolicy?.Mode);
    Assert.Equal(300, manifest.ResidencyPolicy?.IdleSeconds);
}

static async Task AdvancedTemplateProfiles()
{
    using var temp = new TemporaryDirectory();
    foreach (var profile in WidgetTemplateProfiles.All)
    {
        var typeName = char.ToUpperInvariant(profile[0]) + profile[1..] + "Starter";
        var destination = Path.Combine(temp.Path, typeName);
        var id = $"dev.templates.{profile}";
        var created = await RunCli(
            "new", "widget", typeName, "--template", profile,
            "--output", destination, "--id", id, "--publisher", "dev.templates");
        Assert.True(created.Code == 0, "create: " + created.Error);
        Assert.Contains($"Template: {profile}", created.Output);

        var project = Path.Combine(destination, typeName + ".csproj");
        var build = await RunProcessAsync(
            "dotnet", ["build", project, "-c", "Release", "--nologo"],
            TimeSpan.FromSeconds(120), destination);
        Assert.True(build.Code == 0, "build: " + build.Output);

        var tests = await RunProcessAsync(
            "dotnet", ["run", "--project", Path.Combine(destination, "tests", typeName + ".Tests.csproj"),
                "-c", "Release"],
            TimeSpan.FromSeconds(120), destination);
        Assert.Equal(0, tests.Code);
        Assert.Contains("Passed!", tests.Output);

        var preview = await RunCli("preview", destination, "--scenario", "ready");
        Assert.Equal(0, preview.Code);
        Assert.Contains("\"scenario\":\"ready\"", preview.Output);
        Assert.Equal(0, (await RunCli("validate", destination)).Code);

        var first = Path.Combine(temp.Path, profile + ".wrwidget");
        var second = Path.Combine(temp.Path, profile + "-repeat.wrwidget");
        Assert.Equal(0, (await RunCli(
            "pack", destination, "--configuration", "Release", "--output", first)).Code);
        Assert.Equal(0, (await RunCli(
            "pack", destination, "--configuration", "Release", "--output", second)).Code);
        Assert.SequenceEqual(
            await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
    }

    var invalidTarget = Path.Combine(temp.Path, "InvalidStarter");
    var invalid = await RunCli(
        "new", "widget", "InvalidStarter", "--template", "unknown",
        "--output", invalidTarget);
    Assert.Equal(2, invalid.Code);
    Assert.Contains("Choose basic, data, media, or multipage", invalid.Error);
    Assert.True(!Directory.Exists(invalidTarget),
        "An invalid template selection published a partial target.");
}

static async Task MediaTemplatePinnedLayouts()
{
    using var temp = new TemporaryDirectory();
    var destination = Path.Combine(temp.Path, "PinnedMediaStarter");
    var created = await RunCli(
        "new", "widget", "PinnedMediaStarter", "--template", "media",
        "--output", destination, "--id", "dev.templates.pinned-media",
        "--publisher", "dev.templates");
    Assert.Equal(0, created.Code);
    Assert.Contains("Template: media", created.Output);

    var project = Path.Combine(destination, "PinnedMediaStarter.csproj");
    var build = await RunProcessAsync(
        "dotnet", ["build", project, "-c", "Release", "--nologo"],
        TimeSpan.FromSeconds(120), destination);
    Assert.True(build.Code == 0, "build: " + build.Output);

    var tests = await RunProcessAsync(
        "dotnet", ["run", "--project",
            Path.Combine(destination, "tests", "PinnedMediaStarter.Tests.csproj"),
            "--configuration", "Release", "--", "--no-ansi", "--no-progress",
            "--output", "Detailed", "--minimum-expected-tests", "4"],
        TimeSpan.FromSeconds(120), destination);
    Assert.True(tests.Code == 0, "tests: " + tests.Output);
    Assert.Contains("Passed!", tests.Output);

    var compact = await RunCli(
        "preview", destination, "--scenario", "ready",
        "--pinned-layout", "media.compact");
    Assert.Equal(0, compact.Code);
    Assert.Contains("Layout media.compact | Compact media", compact.Output);
    Assert.Contains("Initial focus: media.compact.play", compact.Output);
    Assert.Contains("Active input scope: media.compact.scope", compact.Output);

    var detailed = await RunCli(
        "preview", destination, "--scenario", "ready",
        "--pinned-layout", "media.detailed");
    Assert.Equal(0, detailed.Code);
    Assert.Contains("Layout media.detailed | Media and queue", detailed.Output);
    Assert.Contains("Up next", detailed.Output);
    Assert.Contains("Initial focus: media.detailed.play", detailed.Output);

    var all = await RunCli(
        "preview", destination, "--scenario", "ready", "--pinned-layout", "@all");
    Assert.Equal(0, all.Code);
    Assert.True(
        all.Output.IndexOf("Layout media.compact |", StringComparison.Ordinal) <
        all.Output.IndexOf("Layout media.detailed |", StringComparison.Ordinal),
        "Pinned layout declaration order changed.");
    Assert.Equal(0, (await RunCli("validate", destination)).Code);
}

static async Task NewScaffoldsOutsideCheckout()
{
    using var temp = new TemporaryDirectory();
    var originalDirectory = Environment.CurrentDirectory;
    var originalTemplateRoot = Environment.GetEnvironmentVariable("WRAIL_TEMPLATE_ROOT");
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
        Environment.SetEnvironmentVariable("WRAIL_TEMPLATE_ROOT", externalTemplate);
        var destination = Path.Combine(externalRoot, "VolumeControl");
        var created = await RunCli(
            "new", "widget", "VolumeControl",
            "--output", destination,
            "--id", "dev.example.volume-control",
            "--publisher", "dev.example");
        Assert.Equal(0, created.Code);
        Assert.Contains("Created VolumeControl", created.Output);
        Assert.Contains("ID: dev.example.volume-control", created.Output);
        Assert.Contains("local offline feed", created.Output);
        Assert.DoesNotContain(originalDirectory, created.Output);
        var project = Path.Combine(destination, "VolumeControl.csproj");
        var projectText = await File.ReadAllTextAsync(project);
        Assert.Contains("PackageReference", projectText);
        Assert.DoesNotContain(originalDirectory, projectText);
        Assert.DoesNotContain("WidgetSdk.csproj", projectText);
        var build = await RunProcessAsync(
            "dotnet", ["build", project, "--configuration", "Release", "--nologo"],
            TimeSpan.FromSeconds(90), destination);
        Assert.Equal(0, build.Code);
        Assert.Contains("Build succeeded", build.Output);

        var snapshot = Path.Combine(destination, "fixtures", "ready.snapshot.json");
        var scenario = await RunProcessAsync(
            "dotnet",
            ["run", "--project", Path.Combine(destination, "tests", "VolumeControl.Tests.csproj"),
             "--configuration", "Release"],
            TimeSpan.FromSeconds(90), destination);
        Assert.True(scenario.Code == 0, "tests: " + scenario.Output);
        Assert.Contains("Passed!", scenario.Output);
        var scenarioResult = Path.Combine(destination, "fixtures", "ready.scenario.json");
        var preview = await RunCli(
            "preview", destination, "--scenario", "ready", "--output", scenarioResult);
        Assert.True(preview.Code == 0, "preview: " + preview.Error);
        using (var document = JsonDocument.Parse(await File.ReadAllBytesAsync(scenarioResult)))
            await File.WriteAllTextAsync(snapshot,
                document.RootElement.GetProperty("snapshot").GetRawText());
        Assert.True(File.Exists(snapshot), "Generated scenario did not export its snapshot.");

        var validation = await RunCli("validate", destination);
        Assert.True(validation.Code == 0, "validate: " + validation.Error);
        Assert.Contains("Valid:", validation.Output);
        var canonical = Path.Combine(destination, "fixtures", "ready.canonical.json");
        var rendered = await RunCli("render", snapshot, "--output", canonical);
        Assert.True(rendered.Code == 0, "render: " + rendered.Error);
        Assert.Contains("Snapshot written", rendered.Output);
        var replayed = await RunCli(
            "replay", snapshot, Path.Combine(destination, "replays", "smoke.json"));
        Assert.True(replayed.Code == 0, "replay: " + replayed.Error);
        Assert.Contains("\"actionId\": \"primary\"", replayed.Output);

        CanonicalAuthorJourneyContract.VerifyQuickstart(
            originalDirectory,
            await File.ReadAllTextAsync(Path.Combine(destination, "src", "VolumeControl.cs")));

        var stylePath = Path.Combine(destination, "styles", "default.wrss");
        var validStyle = await File.ReadAllTextAsync(stylePath);
        await File.WriteAllTextAsync(stylePath, ".root { background: url(https://invalid.example); }");
        var invalidStyle = await RunCli("validate", destination);
        Assert.Equal(1, invalidStyle.Code);
        Assert.Contains("default.wrss", invalidStyle.Error);
        Assert.Contains("Validation failed", invalidStyle.Error);
        await File.WriteAllTextAsync(stylePath, validStyle);

        var packageOne = Path.Combine(externalRoot, "dev.example.volume-control-0.1.0.wrwidget");
        var packageOneRepeat = Path.Combine(externalRoot, "dev.example.volume-control-0.1.0-repeat.wrwidget");
        var packedOne = await RunCli(
            "pack", destination, "--configuration", "Release", "--output", packageOne);
        Assert.True(packedOne.Code == 0, "pack: " + packedOne.Error);
        Assert.Contains(Path.GetFileName(packageOne), packedOne.Output);
        Assert.Equal(0, (await RunCli(
            "pack", project, "--configuration", "Release", "--output", packageOneRepeat)).Code);
        Assert.SequenceEqual(
            await File.ReadAllBytesAsync(packageOne),
            await File.ReadAllBytesAsync(packageOneRepeat));
        await AssertPackageIsPortableAsync(packageOne, originalDirectory, externalRoot);

        var catalog = Path.Combine(externalRoot, "catalog");
        var installedOne = await RunCli("install", packageOne, "--catalog", catalog);
        Assert.Equal(0, installedOne.Code);
        Assert.Contains("dev.example.volume-control", installedOne.Output);

        var manifestPath = Path.Combine(destination, "manifest.json");
        var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(manifestPath));
        await File.WriteAllBytesAsync(
            manifestPath, ManifestJson.Serialize(manifest with { Version = "0.2.0" }));
        var packageTwo = Path.Combine(externalRoot, "dev.example.volume-control-0.2.0.wrwidget");
        Assert.Equal(0, (await RunCli(
            "pack", destination, "--output", packageTwo)).Code);
        Assert.Equal(0, (await RunCli(
            "install", packageTwo, "--catalog", catalog)).Code);
        var versions = await RunCli(
            "version", "list", "dev.example.volume-control", "--catalog", catalog);
        Assert.Contains("active version 0.1.0", versions.Output);
        Assert.Contains("       0.2.0", versions.Output);
        Assert.Equal(0, (await RunCli(
            "version", "select", "dev.example.volume-control", "0.2.0",
            "--catalog", catalog)).Code);
        Assert.Equal(0, (await RunCli(
            "enable", "dev.example.volume-control", "--catalog", catalog)).Code);
        Assert.Equal(0, (await RunCli(
            "disable", "dev.example.volume-control", "--catalog", catalog)).Code);
        Assert.Equal(0, (await RunCli(
            "version", "rollback", "dev.example.volume-control", "--catalog", catalog)).Code);
        Assert.Equal(0, (await RunCli(
            "version", "select", "dev.example.volume-control", "0.2.0",
            "--catalog", catalog)).Code);
        Assert.Equal(0, (await RunCli(
            "uninstall", "dev.example.volume-control", "--catalog", catalog)).Code);
        Assert.True(!Directory.Exists(Path.Combine(
                catalog, "packages", "dev.example.volume-control")),
            "Uninstall retained generated package versions.");
    }
    finally
    {
        Environment.CurrentDirectory = originalDirectory;
        Environment.SetEnvironmentVariable("WRAIL_TEMPLATE_ROOT", originalTemplateRoot);
    }
}

static async Task ExternalVersionedSdkConsumer()
{
    using var temp = new TemporaryDirectory();
    var distribution = Path.Combine(temp.Path, "wrail-dist");
    var repository = Path.Combine(temp.Path, "external-repository");
    var widget = Path.Combine(repository, "ExternalBasic");
    var nugetPackages = Path.Combine(temp.Path, "nuget-packages");
    Directory.CreateDirectory(distribution);
    Directory.CreateDirectory(Path.Combine(repository, ".git"));
    CopyWrailDistribution(AppContext.BaseDirectory, distribution);

    var wrail = Path.Combine(distribution, "wrail.exe");
    Assert.True(File.Exists(wrail), "The isolated wrail distribution omitted wrail.exe.");
    var created = await RunProcessAsync(
        wrail,
        ["new", "widget", "ExternalBasic", "--output", widget,
         "--id", "dev.external.basic", "--publisher", "dev.external",
         "--template", "basic"],
        TimeSpan.FromSeconds(30),
        repository,
        new Dictionary<string, string?> { ["WRAIL_TEMPLATE_ROOT"] = null });
    Assert.True(created.Code == 0, "external create: " + created.Output + created.Error);
    Assert.Contains("local offline feed", created.Output);

    var project = Path.Combine(widget, "ExternalBasic.csproj");
    var projectText = await File.ReadAllTextAsync(project);
    Assert.Contains("PackageReference Include=\"WidgetRail.WidgetSdk\"", projectText);
    Assert.DoesNotContain("ProjectReference", projectText);
    Assert.DoesNotContain(Environment.CurrentDirectory, projectText);
    var sdkReference = XDocument.Load(project).Descendants("PackageReference").Single(element =>
        string.Equals((string?)element.Attribute("Include"),
            "WidgetRail.WidgetSdk", StringComparison.Ordinal));
    var sdkVersion = (string?)sdkReference.Attribute("Version") ??
        throw new InvalidOperationException("The generated SDK reference omitted its exact version.");
    Assert.Contains("-dev.local.", sdkVersion);
    var generatedInputs = Directory.EnumerateFiles(widget, "*", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase) &&
                       !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                           StringComparison.OrdinalIgnoreCase) &&
                       !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase));
    foreach (var input in generatedInputs)
        Assert.DoesNotContain(Environment.CurrentDirectory, await File.ReadAllTextAsync(input));

    var sdkPackage = Directory.EnumerateFiles(
        Path.Combine(widget, ".widgetrail", "packages"), "*.nupkg").Single();
    using (var archive = ZipFile.OpenRead(sdkPackage))
    {
        var entries = archive.Entries.Select(entry => entry.FullName)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.SequenceEqual(
            new[]
            {
                "lib/net8.0/WidgetApplicationRuntime.dll",
                "lib/net8.0/WidgetProtocol.dll",
                "lib/net8.0/WidgetSdk.dll",
            },
            entries.Where(entry =>
                    entry.StartsWith("lib/net8.0/", StringComparison.Ordinal) &&
                    entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal));
    }
    await AssertArchiveHasNoPathsAsync(sdkPackage, Environment.CurrentDirectory);

    var build = await RunProcessAsync(
        "dotnet", ["build", project, "-c", "Release", "--nologo"],
        TimeSpan.FromSeconds(90), widget,
        new Dictionary<string, string?> { ["NUGET_PACKAGES"] = nugetPackages });
    Assert.True(build.Code == 0, "external build: " + build.Output + build.Error);
    var restoredSdk = Path.Combine(
        nugetPackages,
        "widgetrail.widgetsdk",
        sdkVersion.ToLowerInvariant(),
        "lib",
        "net8.0");
    Assert.True(File.Exists(Path.Combine(restoredSdk, "WidgetSdk.dll")),
        "The exact generated SDK version was not restored into the isolated package root.");
    Assert.True(File.Exists(Path.Combine(restoredSdk, "WidgetProtocol.dll")),
        "The isolated SDK restore omitted WidgetProtocol.dll.");
    Assert.True(File.Exists(Path.Combine(restoredSdk, "WidgetApplicationRuntime.dll")),
        "The isolated SDK restore omitted the narrow full-trust bootstrap.");
    Assert.True(!File.Exists(Path.Combine(restoredSdk, "PlatformBroker.dll")),
        "The public author SDK unexpectedly exposed PlatformBroker.dll.");
    Assert.SequenceEqual(
        new[] { "WidgetRail.WidgetRuntime.WidgetApplicationBootstrap" },
        typeof(WidgetApplicationBootstrap).Assembly.GetExportedTypes()
            .Select(type => type.FullName!).Order(StringComparer.Ordinal));
    Assert.SequenceEqual(
        new[] { sdkVersion.ToLowerInvariant() },
        Directory.EnumerateDirectories(Path.Combine(
                nugetPackages, "widgetrail.widgetsdk"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal));

    var externalApplication = Path.Combine(repository, "ExternalFullTrustConsumer");
    Directory.CreateDirectory(externalApplication);
    await File.WriteAllTextAsync(Path.Combine(externalApplication, "NuGet.Config"), """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="wrail-local" value="../ExternalBasic/.widgetrail/packages" />
          </packageSources>
        </configuration>
        """);
    var externalApplicationProject = Path.Combine(
        externalApplication, "ExternalFullTrustConsumer.csproj");
    await File.WriteAllTextAsync(externalApplicationProject, $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <RuntimeIdentifier>win-x64</RuntimeIdentifier>
            <UseAppHost>true</UseAppHost>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="WidgetRail.WidgetSdk" Version="{{sdkVersion}}" />
          </ItemGroup>
        </Project>
        """);
    File.Copy(
        Path.Combine(Environment.CurrentDirectory, "tests", "FullTrustAlphaFixture", "Program.cs"),
        Path.Combine(externalApplication, "Program.cs"));
    var externalApplicationProjectText = await File.ReadAllTextAsync(
        externalApplicationProject);
    Assert.Contains("PackageReference Include=\"WidgetRail.WidgetSdk\"",
        externalApplicationProjectText);
    Assert.DoesNotContain("ProjectReference", externalApplicationProjectText);
    var externalApplicationBuild = await RunProcessAsync(
        "dotnet", ["build", externalApplicationProject, "-c", "Release", "--nologo"],
        TimeSpan.FromSeconds(90), externalApplication,
        new Dictionary<string, string?> { ["NUGET_PACKAGES"] = nugetPackages });
    Assert.True(externalApplicationBuild.Code == 0,
        "external full-trust build: " + externalApplicationBuild.Output +
        externalApplicationBuild.Error);
    var externalApplicationOutput = Path.Combine(
        externalApplication, "bin", "Release", "net8.0", "win-x64");
    var externalApplicationExecutable = Path.Combine(
        externalApplicationOutput, "ExternalFullTrustConsumer.exe");
    Assert.True(File.Exists(externalApplicationExecutable),
        "The external full-trust consumer did not produce its package executable.");
    Assert.True(File.Exists(Path.Combine(
            externalApplicationOutput, "WidgetApplicationRuntime.dll")),
        "The external consumer omitted the narrow application bootstrap.");
    Assert.True(!File.Exists(Path.Combine(externalApplicationOutput, "WidgetRuntime.dll")) &&
                !File.Exists(Path.Combine(externalApplicationOutput, "PlatformBroker.dll")),
        "The external full-trust consumer acquired a host or domain assembly.");

    var externalInstance = "external.fulltrust." + Guid.NewGuid().ToString("N");
    var externalIdentity = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(externalInstance)))[..20].ToLowerInvariant();
    var externalState = Path.Combine(
        Path.GetTempPath(), "wrail-full-trust-alpha", externalIdentity);
    try
    {
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = externalApplicationExecutable,
            WidgetInstanceId = externalInstance,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            RequestTimeout = TimeSpan.FromSeconds(5),
            MaximumRestartAttempts = 0,
            IsolationPolicy = WidgetWorkerIsolationPolicy.FullTrustCommunity,
        });
        await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible);
        var externalSnapshot = await client.GetSnapshotAsync();
        var evidence = externalSnapshot.Root.Children.Single(
            node => node.Id == "alpha-result").Text ?? string.Empty;
        foreach (var expected in new[]
                 {
                     "child=True", "file=True", "database=True", "https=True",
                 })
            Assert.Contains(expected, evidence);
        await client.StopAsync();
    }
    finally
    {
        if (Directory.Exists(externalState))
            Directory.Delete(externalState, recursive: true);
    }
    var validation = await RunProcessAsync(
        wrail, ["validate", widget], TimeSpan.FromSeconds(30), repository);
    Assert.True(validation.Code == 0,
        "external validate: " + validation.Output + validation.Error);
    Assert.Contains("Valid:", validation.Output);
    var manifest = ManifestJson.Deserialize(
        await File.ReadAllBytesAsync(Path.Combine(widget, "manifest.json")));
    Assert.Equal("dev.external.basic", manifest.Id);
    Assert.Equal(0, manifest.Permissions.Count);
    Assert.Equal(0, manifest.OptionalPermissions.Count);

    var package = Path.Combine(repository, "dev.external.basic-0.1.0.wrwidget");
    var packed = await RunProcessAsync(
        wrail,
        ["pack", widget, "--configuration", "Release", "--output", package],
        TimeSpan.FromSeconds(120), repository,
        new Dictionary<string, string?> { ["NUGET_PACKAGES"] = nugetPackages });
    Assert.True(packed.Code == 0, "external pack: " + packed.Output + packed.Error);
    using (var archive = ZipFile.OpenRead(package))
    {
        var payload = archive.Entries.Where(entry => entry.FullName.StartsWith(
                "payload/", StringComparison.Ordinal)).Select(entry => entry.FullName).ToArray();
        Assert.SequenceEqual(
            new[] { "payload/ExternalBasic.deps.json", "payload/ExternalBasic.dll" },
            payload.Order(StringComparer.Ordinal));
    }
    await AssertArchiveHasNoPathsAsync(
        package, Environment.CurrentDirectory, distribution, repository);
}

static async Task ExternalFullApplicationOnboarding()
{
    using var temp = new TemporaryDirectory();
    var repositorySource = Environment.CurrentDirectory;
    var distribution = Path.Combine(temp.Path, "wrail-dist");
    var repository = Path.Combine(temp.Path, "external-full-application");
    var widget = Path.Combine(repository, "ExternalFullApplication");
    var packages = Path.Combine(temp.Path, "nuget-packages");
    var catalog = Path.Combine(temp.Path, "isolated-catalog");
    Directory.CreateDirectory(distribution);
    Directory.CreateDirectory(Path.Combine(repository, ".git"));
    CopyWrailDistribution(AppContext.BaseDirectory, distribution);
    var environment = new Dictionary<string, string?>
    {
        ["WRAIL_TEMPLATE_ROOT"] = null,
        ["NUGET_PACKAGES"] = packages,
    };
    var wrail = Path.Combine(distribution, "wrail.exe");
    var setupScript = Path.Combine(repositorySource, "samples", "FullApplicationWidget",
        "Export-ExternalReference.ps1");
    var created = await RunProcessAsync(
        "pwsh",
        ["-NoProfile", "-File", setupScript, "-Wrail", wrail, "-Output", widget],
        TimeSpan.FromSeconds(45), repository, environment);
    Assert.True(created.Code == 0, "external export: " + created.Output + created.Error);
    Assert.Contains("Exported the self-contained Full Application reference", created.Output);

    var project = Path.Combine(widget, "ExternalFullApplication.csproj");
    var projectText = await File.ReadAllTextAsync(project);
    Assert.Contains("PackageReference Include=\"WidgetRail.WidgetSdk\"", projectText);
    Assert.DoesNotContain("ProjectReference", projectText);
    Assert.DoesNotContain(repositorySource, projectText);
    var restore = await RunProcessAsync(
        "dotnet", ["restore", project, "--force", "--no-cache", "--nologo"],
        TimeSpan.FromSeconds(90), widget, environment);
    Assert.True(restore.Code == 0, "external restore: " + restore.Output + restore.Error);
    var build = await RunProcessAsync(
        "dotnet", ["build", project, "-c", "Release", "--no-restore", "--nologo"],
        TimeSpan.FromSeconds(90), widget, environment);
    Assert.True(build.Code == 0, "external build: " + build.Output + build.Error);
    var validation = await RunProcessAsync(
        wrail, ["validate", widget], TimeSpan.FromSeconds(30), repository, environment);
    Assert.True(validation.Code == 0,
        "external validate: " + validation.Output + validation.Error);

    var archive = Path.Combine(repository, "dev.external.full-application-0.1.0.wrwidget");
    var packed = await RunProcessAsync(
        wrail,
        ["pack", widget, "--configuration", "Release", "--output", archive],
        TimeSpan.FromSeconds(120), repository, environment);
    Assert.True(packed.Code == 0, "external pack: " + packed.Output + packed.Error);
    var installed = await RunProcessAsync(
        wrail, ["install", archive, "--catalog", catalog],
        TimeSpan.FromSeconds(60), repository, environment);
    Assert.True(installed.Code == 0,
        "external install: " + installed.Output + installed.Error);

    var scenarioOutput = Path.Combine(repository, "ready.scenario.json");
    var scenario = await RunProcessAsync(
        wrail,
        ["preview", widget, "--scenario", "ready", "--output", scenarioOutput],
        TimeSpan.FromSeconds(30), repository, environment);
    Assert.True(scenario.Code == 0,
        "external scenario: " + scenario.Output + scenario.Error);
    Assert.Contains("isolated preview worker", scenario.Output);
    Assert.Contains("10,000 private records", await File.ReadAllTextAsync(scenarioOutput));

    var removed = await RunProcessAsync(
        wrail, ["uninstall", "dev.external.full-application", "--catalog", catalog],
        TimeSpan.FromSeconds(60), repository, environment);
    Assert.True(removed.Code == 0,
        "external uninstall: " + removed.Output + removed.Error);
    Assert.True(!Directory.Exists(Path.Combine(
            catalog, "packages", "dev.external.full-application")),
        "External removal retained the isolated package directory.");

    var presentationEdit = await RunProcessAsync(
        "pwsh",
        ["-NoProfile", "-Command", """
            $source = '.\ExternalFullApplication\src\FullApplicationReferenceWidget.cs'
            $text = Get-Content -LiteralPath $source -Raw
            $updated = $text.Replace(
              'UI.Text("Reference Library", "full-app.heading")',
              'UI.Text("Reference Library · Edited", "full-app.heading")')
            if ($updated -eq $text) { throw 'The documented presentation edit target was absent.' }
            Set-Content -LiteralPath $source -Value $updated -NoNewline -Encoding utf8
            """],
        TimeSpan.FromSeconds(15), repository, environment);
    Assert.True(presentationEdit.Code == 0,
        "external edit: " + presentationEdit.Output + presentationEdit.Error);
    var editedBuild = await RunProcessAsync(
        "dotnet", ["build", project, "-c", "Release", "--no-restore", "--nologo"],
        TimeSpan.FromSeconds(90), widget, environment);
    Assert.True(editedBuild.Code == 0,
        "external edited build: " + editedBuild.Output + editedBuild.Error);
    var editedValidation = await RunProcessAsync(
        wrail, ["validate", widget], TimeSpan.FromSeconds(30), repository, environment);
    Assert.True(editedValidation.Code == 0,
        "external edited validate: " + editedValidation.Output + editedValidation.Error);
    var declarationPreview = await RunProcessAsync(
        wrail, ["preview", widget], TimeSpan.FromSeconds(30), repository, environment);
    Assert.True(declarationPreview.Code == 0,
        "external declaration preview: " + declarationPreview.Output + declarationPreview.Error);
    Assert.Contains("ready", declarationPreview.Output);
    var editedScenarioOutput = Path.Combine(repository, "ready.edited.scenario.json");
    var editedScenario = await RunProcessAsync(
        wrail,
        ["preview", widget, "--scenario", "ready", "--output", editedScenarioOutput],
        TimeSpan.FromSeconds(30), repository, environment);
    Assert.True(editedScenario.Code == 0,
        "external edited scenario: " + editedScenario.Output + editedScenario.Error);
    var scenarioAssertion = await RunProcessAsync(
        "pwsh",
        ["-NoProfile", "-Command", """
            $result = Get-Content -LiteralPath '.\ready.edited.scenario.json' -Raw |
              ConvertFrom-Json
            if ($result.snapshot.root.children[0].text -ne 'Reference Library · Edited') {
              throw 'The isolated scenario did not contain the edited heading.'
            }
            """],
        TimeSpan.FromSeconds(15), repository, environment);
    Assert.True(scenarioAssertion.Code == 0,
        "external scenario assertion: " + scenarioAssertion.Output + scenarioAssertion.Error);
    var editedArchive = Path.Combine(repository, "ExternalFullApplication-edited.wrwidget");
    var editedPack = await RunProcessAsync(
        wrail,
        ["pack", widget, "--configuration", "Release", "--output", editedArchive],
        TimeSpan.FromSeconds(120), repository, environment);
    Assert.True(editedPack.Code == 0,
        "external edited pack: " + editedPack.Output + editedPack.Error);
    await AssertArchiveHasNoPathsAsync(
        archive, repositorySource, distribution, repository, packages, catalog);
    await AssertArchiveHasNoPathsAsync(
        editedArchive, repositorySource, distribution, repository, packages, catalog);
    foreach (var input in Directory.EnumerateFiles(widget, "*", SearchOption.AllDirectories)
                 .Where(path => !path.Contains(
                     $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                     StringComparison.OrdinalIgnoreCase) &&
                     !path.Contains(
                         $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase) &&
                     !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
        Assert.DoesNotContain(repositorySource, await File.ReadAllTextAsync(input));
}

static async Task ExternalGameLauncherCommunityReference()
{
    using var temp = new TemporaryDirectory();
    var checkout = Environment.CurrentDirectory;
    var distribution = Path.Combine(temp.Path, "wrail-dist");
    var repository = Path.Combine(temp.Path, "external-game-launcher");
    var widget = Path.Combine(repository, "GameLauncherCommunity");
    var packages = Path.Combine(temp.Path, "nuget-packages");
    var catalog = Path.Combine(temp.Path, "catalog");
    Directory.CreateDirectory(distribution);
    Directory.CreateDirectory(Path.Combine(repository, ".git"));
    CopyWrailDistribution(AppContext.BaseDirectory, distribution);
    var environment = new Dictionary<string, string?>
    {
        ["WRAIL_TEMPLATE_ROOT"] = null,
        ["NUGET_PACKAGES"] = packages,
    };
    var wrail = Path.Combine(distribution, "wrail.exe");
    var exporter = Path.Combine(checkout, "src", "FirstPartyWidgets",
        "GameLauncherWidget", "Export-CommunityReference.ps1");
    var exported = await RunProcessAsync(
        "pwsh",
        ["-NoProfile", "-File", exporter, "-Wrail", wrail, "-Output", widget],
        TimeSpan.FromSeconds(45), repository, environment);
    Assert.True(exported.Code == 0,
        "game launcher export: " + exported.Output + exported.Error);
    Assert.Contains("self-contained Game Launcher Community reference", exported.Output);

    var project = Path.Combine(widget, "GameLauncherCommunity.csproj");
    var projectText = await File.ReadAllTextAsync(project);
    Assert.Contains("PackageReference Include=\"WidgetRail.WidgetSdk\"", projectText);
    Assert.DoesNotContain("ProjectReference", projectText);
    Assert.DoesNotContain(checkout, projectText);
    var sdkReference = XDocument.Load(project).Descendants("PackageReference").Single(element =>
        string.Equals((string?)element.Attribute("Include"),
            "WidgetRail.WidgetSdk", StringComparison.Ordinal));
    var sdkVersion = (string?)sdkReference.Attribute("Version") ??
        throw new InvalidOperationException("The Community reference omitted its exact SDK version.");
    Assert.True(!File.Exists(Path.Combine(widget, "src", "AssemblyInfo.cs")),
        "The external reference retained repository-only friend declarations.");
    foreach (var input in Directory.EnumerateFiles(widget, "*", SearchOption.AllDirectories)
                 .Where(path => !path.Contains(
                     $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                     StringComparison.OrdinalIgnoreCase) &&
                     !path.Contains(
                         $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.OrdinalIgnoreCase) &&
                     !path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
        Assert.DoesNotContain(checkout, await File.ReadAllTextAsync(input));

    var restore = await RunProcessAsync(
        "dotnet", ["restore", project, "--force", "--no-cache", "--nologo"],
        TimeSpan.FromSeconds(120), widget, environment);
    Assert.True(restore.Code == 0, "game launcher restore: " + restore.Output + restore.Error);
    var restoredSdk = Path.Combine(
        packages,
        "widgetrail.widgetsdk",
        sdkVersion.ToLowerInvariant(),
        "lib",
        "net8.0");
    Assert.True(File.Exists(Path.Combine(restoredSdk, "WidgetSdk.dll")),
        "The Community reference did not restore the exact SDK into its fresh cache.");
    Assert.True(File.Exists(Path.Combine(restoredSdk, "WidgetProtocol.dll")),
        "The Community reference SDK package omitted WidgetProtocol.dll.");
    var build = await RunProcessAsync(
        "dotnet", ["build", project, "-c", "Release", "--no-restore", "--nologo"],
        TimeSpan.FromSeconds(120), widget, environment);
    Assert.True(build.Code == 0, "game launcher build: " + build.Output + build.Error);
    var applicationOutput = Path.Combine(
        widget, "bin", "Release", "net8.0-windows10.0.19041.0", "win-x64");
    var staging = Path.Combine(repository, "game-launcher-package-root");
    Directory.CreateDirectory(Path.Combine(staging, "payload"));
    Directory.CreateDirectory(Path.Combine(staging, "styles"));
    File.Copy(Path.Combine(widget, "manifest.json"), Path.Combine(staging, "manifest.json"));
    File.Copy(Path.Combine(widget, "styles", "default.wrss"),
        Path.Combine(staging, "styles", "default.wrss"));
    foreach (var input in Directory.EnumerateFiles(
                 applicationOutput, "*", SearchOption.AllDirectories)
             .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                            !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
    {
        var destination = Path.Combine(
            staging, "payload", Path.GetRelativePath(applicationOutput, input));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(input, destination);
    }
    var validation = await RunProcessAsync(
        wrail, ["validate", staging], TimeSpan.FromSeconds(30), repository, environment);
    Assert.True(validation.Code == 0,
        "game launcher validate: " + validation.Output + validation.Error);

    var archive = Path.Combine(repository,
        "widgetrail.community.reference.game-launcher-0.2.0.wrwidget");
    var packed = await RunProcessAsync(
        wrail,
        ["pack", staging, "--output", archive],
        TimeSpan.FromSeconds(150), repository, environment);
    Assert.True(packed.Code == 0, "game launcher pack: " + packed.Output + packed.Error);
    var installed = await RunProcessAsync(
        wrail, ["install", archive, "--catalog", catalog, "--accept-full-trust"],
        TimeSpan.FromSeconds(60), repository, environment);
    Assert.True(installed.Code == 0,
        "game launcher install: " + installed.Output + installed.Error);
    var manifest = ManifestJson.Deserialize(
        await File.ReadAllBytesAsync(Path.Combine(widget, "manifest.json")));
    Assert.Equal("widgetrail.community.reference.game-launcher", manifest.Id);
    Assert.Equal("widgetrail.community.reference", manifest.Publisher);
    Assert.Equal(WidgetEntrypointRuntimes.FullTrustApplicationV1,
        manifest.Entrypoint.Runtime);
    Assert.Equal("payload/GameLauncherApplication.exe", manifest.Entrypoint.Executable);
    Assert.True(manifest.Permissions.Count == 0 && manifest.OptionalPermissions.Count == 0,
        "The autonomous Community application retained product capability authority.");
    Assert.True(File.Exists(Path.Combine(applicationOutput, "GameLauncherApplication.exe")),
        "The exported Community application did not build its ordinary executable.");
    foreach (var productAssembly in new[]
             {
                 "PlatformBroker.dll", "WindowsAppLibraryProvider.dll",
                 "PlatformSettings.dll", "GameLauncherWidget.dll",
             })
        Assert.True(!File.Exists(Path.Combine(applicationOutput, productAssembly)),
            $"The exported application retained product assembly {productAssembly}.");
    Assert.True(!File.Exists(Path.Combine(applicationOutput, "GameLauncherWidget.Core.dll")),
        "The source-export application unexpectedly split its single capability-free assembly.");
    Assert.True(File.Exists(Path.Combine(applicationOutput, "Microsoft.Windows.SDK.NET.dll")),
        "The exported application omitted its required Windows SDK runtime projection.");
    var coreBytes = await File.ReadAllBytesAsync(Path.Combine(
        applicationOutput, "GameLauncherApplication.dll"));
    foreach (var forbiddenCapability in new[]
             {
                 "system.apps.library.read.v1",
                 "system.apps.library.launch.v1",
                 "storage.private-state.v1",
                 "HostGameLauncherApplicationService",
             })
        Assert.True(!ContainsBytes(coreBytes, Encoding.UTF8.GetBytes(forbiddenCapability)) &&
                    !ContainsBytes(coreBytes, Encoding.Unicode.GetBytes(forbiddenCapability)),
            $"The shipped widget core retained dormant capability '{forbiddenCapability}'.");
    var snapshot = await new WidgetCatalog(catalog).DiscoverAsync();
    var candidate = snapshot.Widgets.Single();
    Assert.Equal(manifest.Id, candidate.Id);
    Assert.Equal(manifest.Publisher, candidate.ActiveVersion.Manifest.Publisher);
    Assert.True(!candidate.Enabled,
        "A newly installed Community reference became visible without explicit enablement.");
    await AssertArchiveHasNoPathsAsync(
        archive, checkout, distribution, repository, packages, catalog);
}

static void CopyWrailDistribution(string source, string destination)
{
    foreach (var name in new[]
             {
                 "wrail.exe", "wrail.dll", "wrail.deps.json", "wrail.runtimeconfig.json",
                 "PlatformBroker.dll",
                 "PlatformSettings.dll", "WidgetCatalog.dll", "WidgetProtocol.dll",
                 "WidgetApplicationRuntime.dll", "WidgetRuntime.dll", "WidgetSdk.dll",
                 "WidgetStyling.dll",
             })
    {
        var sourcePath = Path.Combine(source, name);
        Assert.True(File.Exists(sourcePath), $"Built wrail distribution omitted {name}.");
        File.Copy(sourcePath, Path.Combine(destination, name));
    }
    var sourceTemplates = Path.Combine(source, "templates");
    foreach (var sourcePath in Directory.EnumerateFiles(
                 sourceTemplates, "*", SearchOption.AllDirectories))
    {
        var destinationPath = Path.Combine(
            destination, "templates", Path.GetRelativePath(sourceTemplates, sourcePath));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath);
    }
}

static async Task AssertPackageIsPortableAsync(
    string package,
    params string[] forbiddenPaths)
{
    using (var archive = ZipFile.OpenRead(package))
    {
        Assert.True(archive.Entries.Any(entry => entry.FullName == "payload/VolumeControl.dll"),
            "Source packing omitted the declared entrypoint.");
        Assert.True(archive.Entries.All(entry =>
                !entry.FullName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)),
            "Source packing published compiler symbols.");
    }
    await AssertArchiveHasNoPathsAsync(package, forbiddenPaths);
}

static async Task AssertArchiveHasNoPathsAsync(
    string package,
    params string[] forbiddenPaths)
{
    using var archive = ZipFile.OpenRead(package);
    foreach (var entry in archive.Entries.Where(item => item.Length > 0))
    {
        await using var stream = entry.Open();
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        foreach (var forbidden in forbiddenPaths)
        {
            Assert.True(!ContainsBytes(content.GetBuffer().AsSpan(0, checked((int)content.Length)),
                    Encoding.UTF8.GetBytes(forbidden)) &&
                !ContainsBytes(content.GetBuffer().AsSpan(0, checked((int)content.Length)),
                    Encoding.Unicode.GetBytes(forbidden)),
                $"Package entry {entry.FullName} leaked the absolute source path '{forbidden}'.");
        }
    }
}

static bool ContainsBytes(ReadOnlySpan<byte> content, ReadOnlySpan<byte> value) =>
    value.Length != 0 && content.IndexOf(value) >= 0;

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
    Assert.True(File.Exists(Path.Combine(source, "theme.wrss")), "Starter WRSS was not scaffolded.");

    var validated = await RunCli("theme", "validate", source);
    Assert.Equal(0, validated.Code);
    Assert.Contains("Valid theme: dev.test.ocean-night 2.3.4 by dev.test", validated.Output);
    var preview = await RunCli("theme", "preview", source);
    Assert.Equal(0, preview.Code);
    Assert.Contains("Computed preview: Ocean Night", preview.Output);
    Assert.Contains("tray-item:focused:", preview.Output);
    Assert.Contains("button:focused:", preview.Output);
    Assert.Contains("accessibility overrides are not simulated", preview.Output);

    var first = Path.Combine(temp.Path, "first.wrtheme");
    var second = Path.Combine(temp.Path, "second.wrtheme");
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", first)).Code);
    foreach (var file in Directory.EnumerateFiles(source)) File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch);
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", second)).Code);
    Assert.SequenceEqual(await File.ReadAllBytesAsync(first), await File.ReadAllBytesAsync(second));
    using (var archive = ZipFile.OpenRead(first))
    {
        Assert.SequenceEqual(["theme.json", "theme.wrss"], archive.Entries.Select(entry => entry.FullName));
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
    await File.WriteAllTextAsync(Path.Combine(source, "unused.wrss"), "button { color: #123456; }");
    var unused = await RunCli("theme", "validate", source);
    Assert.Equal(1, unused.Code);
    Assert.Contains("unreferenced_style", unused.Error);

    File.Delete(Path.Combine(source, "unused.wrss"));
    await File.WriteAllTextAsync(Path.Combine(source, "theme.wrss"), "button { background: url(https://bad.example/x); }");
    var unsafeStyle = await RunCli("theme", "validate", source);
    Assert.Equal(1, unsafeStyle.Code);
    Assert.Contains("unsafe_value", unsafeStyle.Error);

    await File.WriteAllTextAsync(Path.Combine(source, "theme.wrss"), "button { color: #ffffff; }");
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
    var traversal = Path.Combine(temp.Path, "traversal.wrtheme");
    using (var archive = ZipFile.Open(traversal, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.wrss", "button { color: #fff; }"u8.ToArray());
        WriteArchiveEntry(archive, "../escape.wrss", "button { color: #000; }"u8.ToArray());
    }
    var traversalResult = await RunCli("theme", "validate", traversal);
    Assert.Equal(1, traversalResult.Code);
    Assert.Contains("invalid_path", traversalResult.Error);

    var collision = Path.Combine(temp.Path, "collision.wrtheme");
    using (var archive = ZipFile.Open(collision, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.wrss", "button { color: #fff; }"u8.ToArray());
        WriteArchiveEntry(archive, "THEME.WRSS", "button { color: #000; }"u8.ToArray());
    }
    var collisionResult = await RunCli("theme", "validate", collision);
    Assert.Equal(1, collisionResult.Code);
    Assert.Contains("path_collision", collisionResult.Error);

    var executable = Path.Combine(temp.Path, "executable.wrtheme");
    using (var archive = ZipFile.Open(executable, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.wrss", "button { color: #fff; }"u8.ToArray());
        WriteArchiveEntry(archive, "theme.dll", [0x4d, 0x5a]);
    }
    var executableResult = await RunCli("theme", "validate", executable);
    Assert.Equal(1, executableResult.Code);
    Assert.Contains("unsupported_theme_file", executableResult.Error);

    var symlink = Path.Combine(temp.Path, "symlink.wrtheme");
    using (var archive = ZipFile.Open(symlink, ZipArchiveMode.Create))
    {
        WriteArchiveEntry(archive, "theme.json", manifest);
        WriteArchiveEntry(archive, "theme.wrss", "button { color: #fff; }"u8.ToArray());
        var link = archive.CreateEntry("linked.wrss");
        link.ExternalAttributes = unchecked((int)(0xA1FFu << 16));
        using var stream = link.Open();
        stream.Write("theme.wrss"u8);
    }
    var symlinkResult = await RunCli("theme", "validate", symlink);
    Assert.Equal(1, symlinkResult.Code);
    Assert.Contains("symlink_entry", symlinkResult.Error);
    Assert.True(!File.Exists(Path.Combine(temp.Path, "escape.wrss")), "Theme traversal wrote outside validation.");
}

static async Task ThemeInstallIsImmutable()
{
    using var temp = new TemporaryDirectory();
    var source = await CreateThemeSourceAsync(temp.Path, "dev.test.installed", "dev.test", "4.0.0");
    var package = Path.Combine(temp.Path, "installed.wrtheme");
    Assert.Equal(0, (await RunCli("theme", "pack", source, "--output", package)).Code);
    var settingsRoot = Path.Combine(temp.Path, "settings");
    var install = await RunCli("theme", "install", package, "--settings-root", settingsRoot);
    Assert.Equal(0, install.Code);
    var installedFile = Path.Combine(settingsRoot, "themes", "dev.test.installed", "4.0.0", "theme.wrss");
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
        await File.WriteAllTextAsync(Path.Combine(directory, "theme.wrss"), "button { color: #ffffff; }");
    }
    var overflowSource = await CreateThemeSourceAsync(temp.Path, "dev.test.overflow", "dev.test", "1.0.0");
    var overflowPackage = Path.Combine(temp.Path, "overflow.wrtheme");
    Assert.Equal(0, (await RunCli("theme", "pack", overflowSource, "--output", overflowPackage)).Code);
    var overflow = await RunCli("theme", "install", overflowPackage, "--settings-root", settingsRoot);
    Assert.Equal(1, overflow.Code);
    Assert.Contains("too_many_themes", overflow.Error);
}

static async Task ThemeRemoteInstall()
{
    using var temp = new TemporaryDirectory();
    var source = await CreateThemeSourceAsync(temp.Path, "dev.test.remote-theme", "dev.test", "1.2.0");
    var package = Path.Combine(temp.Path, "remote.wrtheme");
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
        "https://themes.example/remote.wrtheme", "--settings-root", settings);
    Assert.Equal(2, missingHash.Code);
    Assert.Contains("requires --sha256", missingHash.Error);
    Assert.Equal(0, requests);

    var installed = await RunCliWithHandler(handler, "theme", "install",
        "github:sample-org/themes@v1.2.0/remote.wrtheme",
        "--sha256", hash, "--settings-root", settings);
    Assert.Equal(0, installed.Code);
    Assert.Contains("Installed dev.test.remote-theme 1.2.0", installed.Output);
    Assert.Contains($"Downloaded SHA-256: {hash.ToLowerInvariant()}", installed.Output);
    Assert.Equal("https://github.com/sample-org/themes/releases/download/v1.2.0/remote.wrtheme", requestedUri);
    Assert.Equal(1, requests);
}

static async Task ThemeRemovalIsExact()
{
    using var temp = new TemporaryDirectory();
    var settingsRoot = Path.Combine(temp.Path, "settings");
    var sourceOne = await CreateThemeSourceAsync(temp.Path, "dev.test.removable", "dev.test", "1.0.0");
    var sourceTwo = await CreateThemeSourceAsync(temp.Path, "dev.test.removable", "dev.test", "2.0.0");
    var firstPackage = Path.Combine(temp.Path, "remove-one.wrtheme");
    var secondPackage = Path.Combine(temp.Path, "remove-two.wrtheme");
    Assert.Equal(0, (await RunCli("theme", "pack", sourceOne, "--output", firstPackage)).Code);
    Assert.Equal(0, (await RunCli("theme", "pack", sourceTwo, "--output", secondPackage)).Code);
    Assert.Equal(0, (await RunCli("theme", "install", firstPackage, "--settings-root", settingsRoot)).Code);
    Assert.Equal(0, (await RunCli("theme", "install", secondPackage, "--settings-root", settingsRoot)).Code);

    var removed = await RunCli(
        "theme", "remove", "dev.test.removable", "1.0.0", "--settings-root", settingsRoot);
    Assert.Equal(0, removed.Code);
    Assert.Contains("Removed dev.test.removable 1.0.0", removed.Output);
    Assert.True(!Directory.Exists(Path.Combine(settingsRoot, "themes", "dev.test.removable", "1.0.0")),
        "CLI removal retained the exact inactive version.");
    Assert.True(Directory.Exists(Path.Combine(settingsRoot, "themes", "dev.test.removable", "2.0.0")),
        "CLI removal changed a sibling version.");

    var store = new PlatformSettingsStore(new PlatformSettingsPaths(settingsRoot));
    await store.UpdateAsync(current => current with
    {
        Appearance = current.Appearance with
        {
            ThemeId = "dev.test.removable",
            ThemeVersion = "2.0.0",
        },
    });
    var selected = await RunCli(
        "theme", "remove", "dev.test.removable", "2.0.0", "--settings-root", settingsRoot);
    Assert.Equal(1, selected.Code);
    Assert.Contains("selected_theme_protected", selected.Error);
    var builtIn = await RunCli(
        "theme", "remove", ThemeIdentity.BuiltInDefault, ThemeIdentity.BuiltInDefaultVersion,
        "--settings-root", settingsRoot);
    Assert.Equal(1, builtIn.Code);
    Assert.Contains("builtin_theme_protected", builtIn.Error);
    Assert.Equal("dev.test.removable", (await store.LoadAsync()).Appearance.ThemeId);
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

static async Task ValidateRejectsUnsafeWrss()
{
    using var temp = new TemporaryDirectory();
    var path = Path.Combine(temp.Path, "bad.wrss");
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
    File.WriteAllText(Path.Combine(root, "styles", "default.wrss"), "text { color: #fff; }");
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
    Assert.True(files.Contains(Path.Combine(root, "styles", "default.wrss")), "WRSS source was omitted.");
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
    File.Delete(Path.Combine(root, "styles", "default.wrss"));
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
        await File.WriteAllTextAsync(Path.Combine(root, "styles", "new.wrss"),
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
    var inspection = await new WidgetRail.WidgetCatalog.WidgetCatalog(
            Path.Combine(temp.Path, "validation"))
        .CreateInstaller().ValidateAsync(prepared.PackagePath);
    Assert.Equal("dev.test.dev-panel", inspection.Id);
    Assert.True(inspection.Manifest.Entrypoint.Assembly!.StartsWith("payload/", StringComparison.Ordinal),
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
    var packageRoot = Path.Combine(temp.Path, "LivePanelPackage");
    await PrepareDevPackageDirectoryAsync(sourceRoot, packageRoot);
    var outputBuffer = new StringWriter();
    var errorBuffer = new StringWriter();
    var output = TextWriter.Synchronized(outputBuffer);
    var error = TextWriter.Synchronized(errorBuffer);
    var session = new DevSession(
        DevWidgetSource.Discover(packageRoot), Environment.ProcessPath!, "Release",
        TimeSpan.FromSeconds(90), TimeSpan.FromMilliseconds(75), output, error);
    var sessionRoot = session.SessionRoot;
    // Real isolated build behavior and the 90-second product deadline have
    // dedicated tests. This lifecycle fixture starts from one catalog-valid
    // package directory so Ready/retention/cleanup cannot inherit cold-build
    // scheduler state from earlier dev cases.
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
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "styles", "default.wrss"),
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

static async Task PrepareDevPackageDirectoryAsync(string sourceRoot, string packageRoot)
{
    var buildRoot = Path.Combine(Path.GetDirectoryName(packageRoot)!,
        Path.GetFileName(packageRoot) + ".build");
    var prepared = await DevGenerationBuilder.PreparePackageAsync(
        DevWidgetSource.Discover(sourceRoot), buildRoot, "Release",
        TimeSpan.FromSeconds(90), TextWriter.Null, TextWriter.Null,
        CancellationToken.None);
    ZipFile.ExtractToDirectory(prepared.PackagePath, packageRoot);
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

static Task CursorPreviewIsOpaque()
{
    var snapshot = new ViewSnapshot
    {
        ProtocolVersion = ProtocolConstants.CursorCollectionVersion,
        Sequence = 1,
        WidgetInstanceId = "cursor.preview",
        ActiveInputScopeId = "root",
        Root = new ViewNode
        {
            Id = "root",
            Kind = ViewNodeKind.Scroll,
            ScrollAxis = ScrollAxis.Vertical,
            CollectionAnchorKey = "game.42",
            Children =
            [
                new ViewNode
                {
                    Id = "game.42",
                    Kind = ViewNodeKind.Button,
                    Text = "Game",
                    ActionId = "launch",
                    CollectionItemKey = "game.42",
                    ArtworkHandle = "library.art.42",
                    ImageFit = ImageFit.Contain,
                },
            ],
        },
    };
    var preview = SnapshotPreview.Format(snapshot);
    Assert.Contains("collectionAnchor=game.42", preview);
    Assert.Contains("collectionItem=game.42", preview);
    Assert.Contains("artworkHandle=library.art.42", preview);
    Assert.True(!preview.Contains("https://", StringComparison.Ordinal),
        "An opaque artwork preview exposed URL authority.");
    Assert.True(!preview.Contains("\\", StringComparison.Ordinal),
        "An opaque artwork preview exposed a filesystem path.");
    return Task.CompletedTask;
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
    Assert.Contains("Use wrail dev for isolated AppContainer execution", assembly.Error);
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

    var textSnapshot = new WidgetView(
        UI.TextEntry("", "Search", "search.commit", "search", 8),
        "search").CreateSnapshot("text-entry", 1);
    var textSteps = ControllerReplay.Run(textSnapshot, new InputReplay
    {
        Events = [new() { Button = ControllerButton.A, CommittedText = "Halo" }],
    });
    Assert.Equal("search.commit", textSteps.Single().ActionId);
    Assert.Equal("Halo", textSteps.Single().CommittedText);
    var canceled = ControllerReplay.Run(textSnapshot, new InputReplay
    {
        Events = [new() { Button = ControllerButton.A }],
    });
    Assert.Equal<string?>(null, canceled.Single().ActionId);
    try
    {
        ControllerReplay.Run(textSnapshot, new InputReplay
        {
            Events = [new() { Button = ControllerButton.A, CommittedText = "Too long!" }],
        });
        throw new InvalidOperationException("Expected an invalid committed-text replay.");
    }
    catch (CliOperationException)
    {
    }
    return Task.CompletedTask;
}

static async Task PackIsReproducible()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "dev.test.repro", "dev.test", "1.2.3");
    var first = Path.Combine(temp.Path, "first.wrwidget");
    var second = Path.Combine(temp.Path, "second.wrwidget");

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
    Assert.SequenceEqual(["manifest.json", "payload/Widget.dll", "styles/default.wrss"], paths);

    var sourceOnlyOption = await RunCli(
        "pack", source, "--configuration", "Release",
        "--output", Path.Combine(temp.Path, "ignored.wrwidget"));
    Assert.Equal(2, sourceOnlyOption.Code);
    Assert.Contains("apply only to a source project", sourceOnlyOption.Error);
}

static async Task SourcePackFailureIsActionable()
{
    using var temp = new TemporaryDirectory();
    var source = Path.Combine(temp.Path, "BrokenSourceWidget");
    Assert.Equal(0, (await RunCli(
        "new", "widget", "BrokenSourceWidget", "--output", source,
        "--id", "dev.test.broken-source", "--publisher", "dev.test")).Code);
    var manifestPath = Path.Combine(source, "manifest.json");
    var manifest = ManifestJson.Deserialize(await File.ReadAllBytesAsync(manifestPath));
    await File.WriteAllBytesAsync(manifestPath, ManifestJson.Serialize(manifest with
    {
        Entrypoint = manifest.Entrypoint with
        {
            Assembly = "payload/DeclaredButMissing.dll",
        },
    }));
    var package = Path.Combine(temp.Path, "must-not-exist.wrwidget");
    var result = await RunCli("pack", source, "--output", package);
    Assert.Equal(1, result.Code);
    Assert.Contains("Build succeeded but did not produce the declared entrypoint", result.Error);
    Assert.Contains("Set AssemblyName to match the manifest", result.Error);
    Assert.True(!File.Exists(package),
        "A failed source build published a partial package.");
}

static async Task LocalDistributionWorkflow()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "dev.test.local", "dev.test", "2.0.0");
    var package = Path.Combine(temp.Path, "local.wrwidget");
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
    var updatePackage = Path.Combine(temp.Path, "local-update.wrwidget");
    Assert.Equal(0, (await RunCli("pack", updateSource, "--output", updatePackage)).Code);
    var blockedUpdate = await RunCli("install", updatePackage, "--catalog", catalog);
    Assert.Equal(1, blockedUpdate.Code);
    Assert.Contains("Disable it before installing an update", blockedUpdate.Error);
    Assert.True(!Directory.Exists(Path.Combine(catalog, "packages", "dev.test.local", "3.0.0")),
        "A local update bypassed disabled-only review.");
}

static async Task FullTrustCliConsent()
{
    using var temp = new TemporaryDirectory();
    var catalog = Path.Combine(temp.Path, "catalog");
    var source = Path.Combine(temp.Path, "source");
    var package = Path.Combine(temp.Path, "full-trust.wrwidget");
    var manifest = BuildManifest(
        "dev.test.full-trust-cli", "dev.test", "1.0.0") with
    {
        Entrypoint = new WidgetEntrypoint(
            WidgetEntrypointRuntimes.FullTrustApplicationV1,
            Executable: "payload/Independent.exe"),
    };
    Directory.CreateDirectory(Path.Combine(source, "payload"));
    await File.WriteAllBytesAsync(
        Path.Combine(source, "manifest.json"), ManifestJson.Serialize(manifest));
    await File.WriteAllBytesAsync(
        Path.Combine(source, "payload", "Independent.exe"), [0x4d, 0x5a]);
    var packed = await RunCli("pack", source, "--output", package);
    Assert.Equal(0, packed.Code);
    Assert.True(File.Exists(package),
        "The generic packer did not emit the full-trust package.");

    var deniedInstall = await RunCli("install", package, "--catalog", catalog);
    Assert.Equal(1, deniedInstall.Code);
    Assert.Contains("full_trust_approval_required", deniedInstall.Error);
    Assert.True(!Directory.Exists(Path.Combine(
        catalog, "packages", manifest.Id, manifest.Version)),
        "Denied full-trust bytes were published.");

    var installed = await RunCli(
        "install", package, "--catalog", catalog, "--accept-full-trust");
    Assert.Equal(0, installed.Code);
    Assert.Contains("FULL TRUST APPROVED", installed.Output);
    Assert.Contains("ordinary current-user process", installed.Output);
    Assert.Contains("not in AppContainer", installed.Output);

    var deniedEnable = await RunCli("enable", manifest.Id, "--catalog", catalog);
    Assert.Equal(1, deniedEnable.Code);
    Assert.Contains("full_trust_approval_required", deniedEnable.Error);
    var enabled = await RunCli(
        "enable", manifest.Id, "--catalog", catalog, "--accept-full-trust");
    Assert.Equal(0, enabled.Code);
    Assert.Contains("FULL TRUST APPROVED", enabled.Output);
    Assert.Contains("files, network, registry, databases, and child processes",
        enabled.Output);

    Assert.Equal(0, (await RunCli(
        "disable", manifest.Id, "--catalog", catalog)).Code);
    Assert.Equal(0, (await RunCli(
        "uninstall", manifest.Id, "--catalog", catalog)).Code);
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
    var package = Path.Combine(temp.Path, "directory-limit.wrwidget");

    var pack = await RunCli("pack", source, "--output", package);
    Assert.Equal(1, pack.Code);
    Assert.Contains("too_many_launch_directories", pack.Error);
    Assert.True(!File.Exists(package),
        "An unlaunchable package shape left a published pack output.");

    var rawPackage = Path.Combine(temp.Path, "directory-limit-raw.wrwidget");
    ZipFile.CreateFromDirectory(
        source, rawPackage, CompressionLevel.NoCompression, includeBaseDirectory: false);
    var catalog = Path.Combine(temp.Path, "catalog");
    var install = await RunCli("install", rawPackage, "--catalog", catalog);
    Assert.Equal(1, install.Code);
    Assert.Contains("too_many_launch_directories", install.Error);
    Assert.True(!Directory.Exists(Path.Combine(
            catalog, "packages", "dev.test.directory-limit")),
        "An unlaunchable package shape published installed bytes.");
    Assert.Equal(0, (await new WidgetRail.WidgetCatalog.WidgetCatalog(catalog)
        .DiscoverAsync()).Widgets.Count);
}

static async Task PackRejectsInvalidManifest()
{
    using var temp = new TemporaryDirectory();
    var source = CreatePackageSource(temp.Path, "org.other.bad", "dev.test", "1.0.0");
    var package = Path.Combine(temp.Path, "invalid.wrwidget");
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

    var package = Path.Combine(temp.Path, "link.wrwidget");
    var result = await RunCli("pack", source, "--output", package);
    Assert.Equal(1, result.Code);
    Assert.Contains("reparse_point", result.Error);
    Assert.True(!File.Exists(package), "Reparse-containing packages must not be published.");
}

static async Task InstallRejectsTraversal()
{
    using var temp = new TemporaryDirectory();
    var package = Path.Combine(temp.Path, "traversal.wrwidget");
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
    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/release.wrwidget",
        "--sha256", hash.ToUpperInvariant(), "--catalog", catalog);
    Assert.Equal(0, result.Code);
    Assert.Contains("Installed dev.test.remote 3.1.4", result.Output);
    Assert.Contains("(disabled)", result.Output);
    Assert.Contains($"Downloaded SHA-256: {hash}", result.Output);
    Assert.SequenceEqual(["https://widgets.example/release.wrwidget"], requests);
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

    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/update.wrwidget",
        "--sha256", hash, "--catalog", catalog);
    Assert.Equal(1, result.Code);
    Assert.Contains("Disable it before installing an update", result.Error);
    var preserved = await RunCli("list", "--catalog", catalog);
    Assert.Contains("enabled   dev.test.remote-update  1.0.0", preserved.Output);

    Assert.Equal(0, (await RunCli("disable", "dev.test.remote-update", "--catalog", catalog)).Code);
    var retry = await RunCliWithHandler(handler, "install", "https://widgets.example/update.wrwidget",
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
    var result = await RunCliWithHandler(handler, "install", "github:sample-org/game-bar-widget@v1.0.0/music.wrwidget",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "catalog"));
    Assert.Equal(0, result.Code);
    Assert.Equal(
        "https://github.com/sample-org/game-bar-widget/releases/download/v1.0.0/music.wrwidget",
        requested);

    var malformed = await RunCliWithHandler(handler, "install", "github:owner/repo@latest",
        "--sha256", hash, "--catalog", Path.Combine(temp.Path, "unused"));
    Assert.Equal(2, malformed.Code);
    Assert.Contains("github:owner/repository@tag/asset.wrwidget", malformed.Error);
}

static async Task RemoteRequiresHash()
{
    var requests = 0;
    using var handler = new StubHttpHandler((_, _) =>
    {
        requests++;
        return Task.FromResult(Response(HttpStatusCode.OK, []));
    });
    var missing = await RunCliWithHandler(handler, "install", "https://widgets.example/x.wrwidget");
    Assert.Equal(2, missing.Code);
    Assert.Contains("requires --sha256", missing.Error);
    var malformed = await RunCliWithHandler(handler, "install", "https://widgets.example/x.wrwidget", "--sha256", "1234");
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
        "http://widgets.example/x.wrwidget",
        "ftp://widgets.example/x.wrwidget",
        "https://user:secret@widgets.example/x.wrwidget",
        "https://widgets.example/x.wrwidget#fragment",
        "https://widgets.example:8443/x.wrwidget",
        "https://localhost/x.wrwidget",
        "https://127.0.0.1/x.wrwidget",
        "https://10.1.2.3/x.wrwidget",
        "https://169.254.1.1/x.wrwidget",
        "https://172.20.1.1/x.wrwidget",
        "https://192.168.1.1/x.wrwidget",
        "https://[::1]/x.wrwidget",
        "https://[fe80::1]/x.wrwidget",
        "https://[fd00::1]/x.wrwidget",
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
        response.Headers.Location = new Uri($"https://cdn.example/{calls}.wrwidget");
        return Task.FromResult(response);
    });
    using var downloader = new RemotePackageDownloader(redirectHandler, new RemoteDownloadOptions
    {
        MaximumRedirects = 2,
    });
    var exception = await Assert.ThrowsAsync<CliOperationException>(() => downloader.DownloadAsync(
        new Uri("https://widgets.example/start.wrwidget"), null, CancellationToken.None));
    Assert.Contains("2-redirect limit", exception.Message);
    Assert.Equal(3, calls);

    using var unsafeRedirectHandler = new StubHttpHandler((_, _) =>
    {
        var response = Response(HttpStatusCode.Found, []);
        response.Headers.Location = new Uri("http://cdn.example/package.wrwidget");
        return Task.FromResult(response);
    });
    using var unsafeDownloader = new RemotePackageDownloader(unsafeRedirectHandler);
    var unsafeException = await Assert.ThrowsAsync<CliUsageException>(() => unsafeDownloader.DownloadAsync(
        new Uri("https://widgets.example/start.wrwidget"), null, CancellationToken.None));
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
        new Uri("https://widgets.example/x.wrwidget"), null, CancellationToken.None));
    Assert.Contains("Content-Length: 9", headerException.Message);

    using var streamHandler = new StubHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StreamContent(new RepeatingReadStream(9)),
    }));
    using var streamDownloader = new RemotePackageDownloader(streamHandler, TestDownloadOptions(temp.Path, 8));
    var streamException = await Assert.ThrowsAsync<CliOperationException>(() => streamDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.wrwidget"), null, CancellationToken.None));
    Assert.Contains("8-byte download limit", streamException.Message);

    using var emptyHandler = new StubHttpHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, [])));
    using var emptyDownloader = new RemotePackageDownloader(emptyHandler, TestDownloadOptions(temp.Path, 8));
    var emptyException = await Assert.ThrowsAsync<CliOperationException>(() => emptyDownloader.DownloadAsync(
        new Uri("https://widgets.example/x.wrwidget"), null, CancellationToken.None));
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
        new Uri("https://widgets.example/x.wrwidget"), null, CancellationToken.None));
    Assert.Contains("identity content encoding", exception.Message);
}

static async Task RemoteRejectsMalformedArchive()
{
    var payload = "not a widget archive"u8.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(payload));
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromResult(Response(HttpStatusCode.OK, payload)));
    using var temp = new TemporaryDirectory();

    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/broken.wrwidget",
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
        new Uri("https://widgets.example/x.wrwidget"), new byte[32], CancellationToken.None));
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
        new Uri("https://widgets.example/x.wrwidget"), SHA256.HashData(bytes), CancellationToken.None);
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
        new Uri("https://widgets.example/x.wrwidget"), null, CancellationToken.None));
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
        new Uri("https://widgets.example/x.wrwidget"), null, CancellationToken.None));
    Assert.Contains("overall timeout", overallException.Message);
}

static async Task RemoteFailuresRedactSecrets()
{
    const string secret = "super-secret-token";
    using var handler = new StubHttpHandler((_, _) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException(
            $"GET https://widgets.example/release.wrwidget?token={secret} failed")));
    var result = await RunCliWithHandler(handler, "install", "https://widgets.example/release.wrwidget",
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
        ["install", "https://widgets.example/release.wrwidget", "--sha256", new string('0', 64)],
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
    File.WriteAllText(Path.Combine(source, "styles", "default.wrss"), "text { color: #ffffff; }");
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
    await File.WriteAllTextAsync(Path.Combine(source, "theme.wrss"),
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
        EntryFile = "theme.wrss",
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

static async Task<string> CreatePackedPackageAsync(string root, string id, string version)
{
    var source = CreatePackageSource(root, id, "dev.test", version);
    var package = Path.Combine(root, $"{id}-{version}-{Guid.NewGuid():N}.wrwidget");
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
    TimeSpan timeout,
    string? workingDirectory = null,
    IReadOnlyDictionary<string, string?>? environment = null)
{
    var start = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
    if (environment is not null)
        foreach (var item in environment)
            if (item.Value is null) start.Environment.Remove(item.Key);
            else start.Environment[item.Key] = item.Value;
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
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wrail-cli-tests", Guid.NewGuid().ToString("N"));
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
