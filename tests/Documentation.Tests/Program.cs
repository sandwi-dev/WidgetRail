using System.Text.RegularExpressions;

var repository = FindRepositoryRoot(AppContext.BaseDirectory);
var failures = new List<string>();
var markdownLink = new Regex(
    @"!?\[[^\]\r\n]*\]\((?<target>[^)\r\n]+)\)",
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

var guidePath = Path.Combine(repository, "docs", "widget-authoring-guide.md");
var guide = File.ReadAllText(guidePath);
string[] requiredGuideContracts =
[
    "## Versions: three different contracts",
    "## Tutorial 2: make the controller model intentional",
    "## Tutorial 3: nested windows and scoped shortcuts",
    "## Tutorial 4: controller-owned scrolling",
    "## Tutorial 5: choose a useful surface without hard-coding a window",
    "## Lifecycle API",
    "## Typed audio and network capabilities",
    "## Package and install locally",
    "## Share from a GitHub repository",
    "## Isolation and security expectations",
    "## Resolution and monitor contract",
    "## Diagnostics and recovery",
];
foreach (var contract in requiredGuideContracts)
    if (!guide.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/widget-authoring-guide.md is missing '{contract}'.");

var companionPath = Path.Combine(repository, "docs", "community-companion-services.md");
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
        failures.Add($"docs/community-companion-services.md is missing '{contract}'.");

var quickstartPath = Path.Combine(repository, "docs", "widget-quickstart.md");
var quickstart = File.ReadAllText(quickstartPath);
string[] requiredAuthorityRecoveryContracts =
[
    "gbar authority-recovery retry <confirmation-token-from-list>",
    "It does not accept a journal path, content path, security",
    "raw clear, or force option",
    "Settings → Diagnostics",
    "initial focus is **Cancel**",
    "never renders or speaks the opaque confirmation token",
    "Community widgets cannot request this channel",
];
foreach (var contract in requiredAuthorityRecoveryContracts)
    if (!quickstart.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/widget-quickstart.md is missing '{contract}'.");

var publishing = File.ReadAllText(Path.Combine(
    repository, "docs", "publishing-and-installation.md"));
var cliReadme = File.ReadAllText(Path.Combine(
    repository, "tools", "GbarCli", "README.md"));
string[] requiredExternalAuthorContracts =
[
    ".gbar\\packages",
    "tests\\VolumeControl.Tests.csproj",
    "gbar pack .\\scratch\\VolumeControl",
    "entrypoint.assembly",
];
foreach (var contract in requiredExternalAuthorContracts)
    if (!quickstart.Contains(contract, StringComparison.Ordinal))
        failures.Add($"docs/widget-quickstart.md is missing '{contract}'.");
foreach (var (name, text) in new[]
         {
             ("docs/widget-quickstart.md", quickstart),
             ("docs/widget-authoring-guide.md", guide),
             ("docs/publishing-and-installation.md", publishing),
             ("tools/GbarCli/README.md", cliReadme),
         })
{
    if (text.Contains("--sdk-project", StringComparison.Ordinal) ||
        text.Contains("WidgetSdk.csproj", StringComparison.Ordinal))
        failures.Add($"{name} retains the checkout-bound SDK project workflow.");
}
if (!publishing.Contains("NuGet.Config", StringComparison.Ordinal) ||
    !publishing.Contains("bounded isolated Release build", StringComparison.Ordinal))
    failures.Add("docs/publishing-and-installation.md is missing the external source-pack contract.");
if (!cliReadme.Contains("GameBarAlternative.WidgetSdk", StringComparison.Ordinal) ||
    !cliReadme.Contains("private intermediates", StringComparison.Ordinal))
    failures.Add("tools/GbarCli/README.md is missing the offline scaffold/source-pack contract.");

RequireLink(Path.Combine(repository, "README.md"), "docs/widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "README.md"), "docs/community-companion-services.md");
RequireLink(Path.Combine(repository, "docs", "README.md"), "widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "docs", "README.md"), "community-companion-services.md");
RequireLink(Path.Combine(repository, "samples", "ClockWidget", "README.md"),
    "../../docs/widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "samples", "YtMusicWidget", "README.md"),
    "../../docs/community-companion-services.md");

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
    throw new DirectoryNotFoundException("Could not locate the GameBarAlternative repository root.");
}

static bool HasIgnoredSegment(string root, string path)
{
    var relative = Path.GetRelativePath(root, path);
    return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => segment is ".git" or "bin" or "obj" or "artifacts");
}
