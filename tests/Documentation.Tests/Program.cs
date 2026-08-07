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

RequireLink(Path.Combine(repository, "README.md"), "docs/widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "docs", "README.md"), "widget-authoring-guide.md");
RequireLink(Path.Combine(repository, "samples", "ClockWidget", "README.md"),
    "../../docs/widget-authoring-guide.md");

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
