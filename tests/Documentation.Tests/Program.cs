using System.Text.RegularExpressions;
using System.Text.Json;

var repository = FindRepositoryRoot(AppContext.BaseDirectory);
var failures = new List<string>();
// This map checks feature-family discoverability, not completeness of prose.
// New public SDK files need an explicit documentation destination.
var sdkDocMap = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(
    Path.Combine(repository, "tests", "Documentation.Tests", "sdk-doc-map.json")))!;
var publicDeclaration = new Regex(
    @"^public\s+(?:(?:sealed|abstract|readonly|static|partial)\s+)*(?:class|record|struct|interface|enum)\s+",
    RegexOptions.Multiline | RegexOptions.CultureInvariant);
var publicApiFiles = new[] { "WidgetSdk", "WidgetApplicationRuntime" }
    .SelectMany(name => Directory.EnumerateFiles(Path.Combine(repository, "src", name), "*.cs"))
    .Where(path => publicDeclaration.IsMatch(File.ReadAllText(path)))
    .Select(path => Path.GetRelativePath(repository, path).Replace('\\', '/'))
    .ToHashSet(StringComparer.Ordinal);
foreach (var source in publicApiFiles)
    if (!sdkDocMap.ContainsKey(source)) failures.Add($"Public SDK file needs a documentation topic: {source}");
foreach (var (source, topic) in sdkDocMap)
{
    if (!publicApiFiles.Contains(source)) failures.Add($"Stale SDK documentation mapping: {source}");
    if (!File.Exists(Path.Combine(repository, topic))) failures.Add($"Missing SDK topic: {source} -> {topic}");
}
var markdownLink = new Regex(
    @"!?\[[^\]]*\]\((?<target>[^)\r\n]+)\)",
    RegexOptions.CultureInvariant);
var markdownFiles = Directory.EnumerateFiles(repository, "*.md", SearchOption.AllDirectories)
    .Where(path => !HasIgnoredSegment(repository, path))
    .Order(StringComparer.OrdinalIgnoreCase)
    .ToArray();

foreach (var markdown in markdownFiles)
{
    var text = File.ReadAllText(markdown);
    foreach (Match match in markdownLink.Matches(text))
    {
        var rawTarget = match.Groups["target"].Value.Trim();
        if (rawTarget.StartsWith('<') && rawTarget.EndsWith('>'))
            rawTarget = rawTarget[1..^1];
        if (rawTarget.Length == 0 || rawTarget.StartsWith('#') ||
            Uri.TryCreate(rawTarget, UriKind.Absolute, out _))
            continue;

        var pathPart = rawTarget.Split('#', 2)[0];
        if (pathPart.Length == 0) continue;
        var decoded = Uri.UnescapeDataString(pathPart).Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(decoded, Path.GetDirectoryName(markdown)!);
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
            failures.Add($"{Relative(markdown)} -> {rawTarget}");
    }
}

var guidePath = Path.Combine(repository, "docs", "reference", "sdk-reference.md");
var guide = File.ReadAllText(guidePath);
var modelPath = Path.Combine(repository, "docs", "reference", "widget-model-reference.md");
var model = File.ReadAllText(modelPath);
var modelSource = File.ReadAllText(Path.Combine(
    repository, "src", "WidgetSdk", "WidgetModel.cs"));
var optimisticCommandSource = File.ReadAllText(Path.Combine(
    repository, "src", "WidgetSdk", "WidgetOptimisticCommand.cs"));
string[] requiredModelContracts =
[
    "WidgetModel<TState>", "CreateModel(initialState)", "CreateForTesting(initialState)",
    "var snapshot = _model.Snapshot;", "Set(value)", "Update(state => next)",
    "Equality", "Observers", "WidgetResource<TValue>", "WidgetCursorResource<TItem>",
    "WidgetNavigator<TRoute>", "async-work.md#optimistic-actions",
];
foreach (var contract in requiredModelContracts)
    if (!model.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/developers/widget-model.md is missing '{contract}'.");
string[] requiredModelSourceContracts =
[
    "public TState Value",
    "public WidgetModelSnapshot<TState> Snapshot",
    "public WidgetModelUpdate<TState> Set(TState value)",
    "public WidgetModelUpdate<TState> Update(Func<TState, TState> updater)",
    "public WidgetModelUpdate<TState, TResult> Update<TResult>",
    "public static WidgetModel<TState> CreateForTesting(",
    "if (LifecycleState != WidgetLifecycleState.Destroying) Invalidate();",
];
foreach (var contract in requiredModelSourceContracts)
    if (!modelSource.Contains(contract, StringComparison.Ordinal))
        failures.Add($"WidgetModel source no longer supports documented claim '{contract}'.");
string[] requiredOptimisticCommandSourceContracts =
[
    "Func<TExecution, WidgetOperationContext, ValueTask<TResult>> Execute",
    "_model.UpdateBeforePublication(",
];
foreach (var contract in requiredOptimisticCommandSourceContracts)
    if (!optimisticCommandSource.Contains(contract, StringComparison.Ordinal))
        failures.Add(
            $"WidgetOptimisticCommand source no longer supports documented claim '{contract}'.");
string[] requiredGuideContracts =
[
    "declarative-ui.md", "collections.md", "controller-input.md", "visual-content.md",
    "accessibility.md", "async-work.md", "routes.md", "capabilities.md",
    "cli-workflows.md", "WidgetModel<TState>", "WidgetResource<TValue>",
    "WidgetCursorResource<TItem>", "WidgetNavigator<TRoute>", "WidgetOperations",
];
foreach (var contract in requiredGuideContracts)
    if (!guide.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/developers/widget-authoring-guide.md is missing '{contract}'.");

var companionPath = Path.Combine(repository, "docs", "reference", "community-companion-services.md");
var companion = File.ReadAllText(companionPath);
string[] requiredCompanionContracts =
[
    "## Manifest declarations",
    "## Typed SDK",
    "## Lifecycle, consent, and dashboard actions",
    "## HTTP security boundary",
    "## Limits",
    "## Secret identity and persistence",
    "## Errors and recovery",
    "## Testing pattern",
    "InvalidateBearerSecretOnUnauthorized",
];
foreach (var contract in requiredCompanionContracts)
    if (!companion.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/reference/community-companion-services.md is missing '{contract}'.");

var quickstart = string.Join("\n", new[] { "cli-projects.md", "cli-scenarios.md", "cli-packages.md" }
    .Select(name => File.ReadAllText(Path.Combine(repository, "docs", "reference", name))));
var recovery = File.ReadAllText(Path.Combine(repository, "docs", "maintainers", "authority-recovery.md"));
var scenarios = File.ReadAllText(Path.Combine(repository, "docs", "reference", "cli-scenarios.md"));
string[] requiredAuthorityRecoveryContracts =
[
    "wrail authority-recovery retry <confirmation-token-from-list>",
    "It does not accept a journal path, content path, security",
    "force option",
    "Settings → Diagnostics",
    "initial focus is **Cancel**",
    "never renders or speaks the opaque confirmation token",
    "Community widgets cannot request this channel",
];
foreach (var contract in requiredAuthorityRecoveryContracts)
    if (!recovery.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/maintainers/authority-recovery.md is missing '{contract}'.");

string[] requiredScenarioContracts =
[
    "WidgetScenarioDefinition",
    "--scenario running",
    "AppContainer/Job worker",
    "Lifecycle transitions",
    "The CLI process never loads the assembly",
];
foreach (var contract in requiredScenarioContracts)
    if (!scenarios.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/developers/widget-authoring-guide.md is missing '{contract}'.");
if (!quickstart.Contains("--scenario muted", StringComparison.Ordinal) ||
    !quickstart.Contains("WidgetScenarioResult", StringComparison.Ordinal))
    failures.Add("docs/developers/widget-quickstart.md is missing the executable scenario workflow.");

var publishing = File.ReadAllText(Path.Combine(
    repository, "docs", "developers", "publishing-and-installation.md"));
var cliReadme = File.ReadAllText(Path.Combine(
    repository, "tools", "WrailCli", "README.md"));
string[] requiredExternalAuthorContracts =
[
    ".widgetrail\\packages",
    "tests\\VolumeControl.Tests.csproj",
    "wrail pack .\\scratch\\VolumeControl",
    "entrypoint.assembly",
];
foreach (var contract in requiredExternalAuthorContracts)
    if (!quickstart.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/developers/widget-quickstart.md is missing '{contract}'.");
foreach (var (name, text) in new[]
         {
             ("docs/developers/widget-quickstart.md", quickstart),
             ("docs/developers/widget-authoring-guide.md", guide),
             ("docs/developers/publishing-and-installation.md", publishing),
             ("tools/WrailCli/README.md", cliReadme),
         })
{
    if (text.Contains("--sdk-project", StringComparison.Ordinal) ||
        text.Contains("WidgetSdk.csproj", StringComparison.Ordinal))
        failures.Add($"{name} retains the checkout-bound SDK project workflow.");
}
if (!publishing.Contains("NuGet.Config", StringComparison.Ordinal) ||
    !publishing.Contains("bounded isolated Release build", StringComparison.Ordinal))
    failures.Add("docs/developers/publishing-and-installation.md is missing the external source-pack contract.");
if (!cliReadme.Contains("WidgetRail.WidgetSdk", StringComparison.Ordinal) ||
    !cliReadme.Contains("private intermediates", StringComparison.Ordinal))
    failures.Add("tools/WrailCli/README.md is missing the offline scaffold/source-pack contract.");

var coreDomainRoots = new[]
{
    Path.Combine(repository, "src", "PlatformBroker"),
    Path.Combine(repository, "src", "WidgetBridge"),
    Path.Combine(repository, "src", "WidgetCatalog"),
    Path.Combine(repository, "src", "WidgetProtocol"),
    Path.Combine(repository, "src", "WidgetRuntime"),
    Path.Combine(repository, "src", "WidgetSdk"),
};
string[] retiredDomainMarkers =
[
    "external.spotify.",
    "WidgetSpotify",
    "SpotifyPlatformBroker",
    "WindowsSpotifyProvider",
    "widgetrail.samples.spotify",
    "widgetrail.community.reference.game-launcher",
    "widgetrail.samples.game-launcher",
    "widgetrail.firstparty.game-launcher",
    "HostGameLauncherApplicationService",
];
foreach (var root in coreDomainRoots)
{
    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                 .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".json")
                 .Where(path => !HasIgnoredSegment(repository, path)))
    {
        var source = File.ReadAllText(file);
        foreach (var marker in retiredDomainMarkers)
        {
            // DLV-259 owns persisted-state migration. Exact retired consent
            // tombstones remain data migration metadata, not executable domain code.
            if (Path.GetFileName(file) == "ConsentStore.cs" &&
                marker == "external.spotify.")
                continue;
            if (source.Contains(marker, StringComparison.Ordinal))
                failures.Add($"{Relative(file)} retains retired product domain '{marker}'.");
        }
    }
}
if (!File.ReadAllText(Path.Combine(repository, "src", "PlatformBroker", "Contracts.cs"))
        .Contains("IAppLibraryPlatformBrokerBackend", StringComparison.Ordinal))
    failures.Add("Generic App Library behavior was removed with the retired product domains.");
foreach (var retiredPath in new[]
         {
             Path.Combine(repository, "src", "WindowsSpotifyProvider", "WindowsSpotifyProvider.csproj"),
             Path.Combine(repository, "src", "SpotifyPlaybackProtocol", "SpotifyPlaybackProtocol.csproj"),
             Path.Combine(repository, "src", "SpotifyPlaybackHost", "SpotifyPlaybackHost.csproj"),
             Path.Combine(repository, "src", "FirstPartyWidgets", "GameLauncherWidget"),
         })
    if (File.Exists(retiredPath))
        failures.Add($"{Relative(retiredPath)} is a retired product-owned domain path.");

var overlayBuild = File.ReadAllText(Path.Combine(repository, "src", "OverlayHost", "build.ps1"));
var bridgeOutputDeclaration = overlayBuild.IndexOf(
    "$bridgeOutput = Join-Path $outputDirectory 'runtime\\Bridge'", StringComparison.Ordinal);
var cleanupLoop = Regex.Match(overlayBuild,
    @"foreach\s*\(\$hostRuntimeOutput\s+in\s+@\((?<paths>[\s\S]*?)\)\)\s*\{\s*Remove-GeneratedDirectory\s+-Path\s+\$hostRuntimeOutput\s*\}");
var bridgePublish = overlayBuild.IndexOf(
    "..\\WidgetBridge\\WidgetBridge.csproj", StringComparison.Ordinal);
if (!cleanupLoop.Success ||
    !cleanupLoop.Groups["paths"].Value.Contains("$bridgeOutput", StringComparison.Ordinal) ||
    !cleanupLoop.Groups["paths"].Value.Contains("runtime\\SpotifyPlaybackHost", StringComparison.Ordinal) ||
    bridgeOutputDeclaration < 0 || cleanupLoop.Index <= bridgeOutputDeclaration ||
    bridgePublish <= cleanupLoop.Index + cleanupLoop.Length)
    failures.Add("OverlayHost must clean Bridge and retired Spotify runtime output before republishing.");



RequireLink(Path.Combine(repository, "README.md"), "docs/developers/widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "README.md"), "docs/reference/community-companion-services.md");
RequireLink(Path.Combine(repository, "docs", "README.md"), "developers/widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "docs", "README.md"), "developers/widget-model.md");
RequireLink(Path.Combine(repository, "docs", "README.md"), "reference/community-companion-services.md");
RequireLink(Path.Combine(repository, "docs", "developers", "widget-authoring-guide.md"),
    "widget-model.md");
RequireLink(Path.Combine(repository, "src", "WidgetSdk", "README.md"),
    "../../docs/developers/widget-model.md");
RequireLink(Path.Combine(repository, "docs", "README.md"),
    "../samples/PlayniteLibraryWidget/README.md");
RequireLink(Path.Combine(repository, "README.md"),
    "samples/PlayniteLibraryWidget/README.md");
RequireLink(Path.Combine(repository, "samples", "ClockWidget", "README.md"),
    "../../docs/developers/widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "samples", "YtMusicWidget", "README.md"),
    "../../docs/reference/community-companion-services.md");

// Public learning paths must stay discoverable independently of detailed contracts.
foreach (var topic in new[] { "concepts", "navigation", "styling", "data-and-lifecycle", "media-and-pinning" })
    RequireLink(Path.Combine(repository, "docs", "README.md"), $"developers/{topic}.md");
foreach (var plan in new[] { "delivery-plan.md", "review-planner-goal.md", "implementation-agent-goal.md" })
    if (!File.Exists(Path.Combine(repository, "docs", plan))) failures.Add($"Missing retained plan: {plan}");
foreach (var removed in new[] { "archive", "history" })
    if (Directory.Exists(Path.Combine(repository, "docs", removed)) &&
        Directory.EnumerateFiles(Path.Combine(repository, "docs", removed), "*", SearchOption.AllDirectories).Any())
        failures.Add($"Retired notes remain: {removed}");
RequireLink(Path.Combine(repository, "docs", "README.md"), "images/README.md");

if (failures.Count != 0)
{
    Console.Error.WriteLine($"Documentation contract failed ({failures.Count}):");
    foreach (var failure in failures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        Console.Error.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine($"Documentation contract passed ({markdownFiles.Length} Markdown files checked)." );
return 0;

void RequireLink(string file, string target)
{
    if (!File.ReadAllText(file).Contains($"]({target})", StringComparison.Ordinal))
        failures.Add($"{Relative(file)} does not link to {target}.");
}

string Relative(string path) => Path.GetRelativePath(repository, path).Replace('\\', '/');

static string FindRepositoryRoot(string start)
{
    for (var current = new DirectoryInfo(start); current is not null; current = current.Parent)
        if (File.Exists(Path.Combine(current.FullName, "README.md")) &&
            File.Exists(Path.Combine(current.FullName, "docs", "README.md")) &&
            File.Exists(Path.Combine(current.FullName, "global.json")))
            return current.FullName;
    throw new DirectoryNotFoundException("Could not locate the WidgetRail repository root.");
}

static bool HasIgnoredSegment(string root, string path)
{
    var relative = Path.GetRelativePath(root, path);
    // Owner-retained working plans are not public guides; preserve their text,
    // including historical links, while validating every public Markdown page.
    if (relative.Replace('\\', '/') is "docs/implementation-agent-goal.md" or
        "docs/review-planner-goal.md" or "docs/delivery-plan.md") return true;
    return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => segment is ".git" or "bin" or "obj" or "artifacts" or "history" or "archive" or "logs");
}
