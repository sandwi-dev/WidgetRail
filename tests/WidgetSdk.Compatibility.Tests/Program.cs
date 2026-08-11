using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using GameBarAlternative.GbarCli;
using GameBarAlternative.WidgetSdk;

var root = FindRepositoryRoot();
var baselinePath = Path.Combine(root, "src", "WidgetSdk", "PublicApi.txt");
var current = WidgetSdkPublicApi.Generate(typeof(Widget).Assembly);
Require(current.Count is > 0 and <= 5000,
    $"WidgetSdk public API symbol count {current.Count} is outside the 1..5000 compatibility bound.");
Require(current.SequenceEqual(
        WidgetSdkPublicApi.Generate(typeof(Widget).Assembly), StringComparer.Ordinal),
    "WidgetSdk public API generation is not deterministic within one Release artifact.");

if (args is ["--update"])
{
    await File.WriteAllTextAsync(
        baselinePath,
        WidgetSdkPublicApi.Serialize(current));
    Console.WriteLine($"Updated {Path.GetRelativePath(root, baselinePath)} with {current.Count} public API symbols.");
    return 0;
}
if (args.Length != 0)
{
    Console.Error.WriteLine("Usage: WidgetSdk.Compatibility.Tests [--update]");
    return 2;
}

ApiDiffSelfTest();
ValidateReleaseUnit(root);
if (!File.Exists(baselinePath))
{
    Console.Error.WriteLine(
        "WidgetSdk public API baseline is missing. Run: dotnet run --project " +
        "tests/WidgetSdk.Compatibility.Tests/WidgetSdk.Compatibility.Tests.csproj " +
        "--configuration Release -- --update");
    return 1;
}
if (new FileInfo(baselinePath).Length > 1024 * 1024)
{
    Console.Error.WriteLine("WidgetSdk public API baseline exceeds its 1-MiB review bound.");
    return 1;
}

var baselineText = await File.ReadAllTextAsync(baselinePath);
var expected = WidgetSdkPublicApi.Parse(baselineText);
var diff = ApiDiff.Compare(expected, current);
if (diff.Removed.Count != 0 || diff.Added.Count != 0)
{
    foreach (var symbol in diff.Removed)
        Console.Error.WriteLine($"REMOVED_OR_CHANGED - {symbol}");
    foreach (var symbol in diff.Added)
        Console.Error.WriteLine($"COMPATIBLE_ADDITION_OR_CHANGED + {symbol}");
    Console.Error.WriteLine(
        "WidgetSdk public API differs from src/WidgetSdk/PublicApi.txt. " +
        "Review compatibility, then intentionally accept the pre-release surface with: " +
        "dotnet run --project tests/WidgetSdk.Compatibility.Tests/WidgetSdk.Compatibility.Tests.csproj " +
        "--configuration Release -- --update");
    return 1;
}

if (baselineText.Contains(root, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("WidgetSdk public API baseline contains an absolute checkout path.");
    return 1;
}

Console.WriteLine(
    $"WidgetSdk compatibility contract passed ({current.Count} public API symbols; release unit {ReleaseVersion(typeof(Widget).Assembly)})." );
return 0;

static void ApiDiffSelfTest()
{
    var addition = ApiDiff.Compare(["type A"], ["type A", "method A.M() -> void"]);
    Require(addition.Removed.Count == 0 &&
            addition.Added.SequenceEqual(["method A.M() -> void"], StringComparer.Ordinal),
        "Compatibility classifier did not identify an addition.");
    var removal = ApiDiff.Compare(["type A", "method A.M() -> void"], ["type A"]);
    Require(removal.Removed.SequenceEqual(["method A.M() -> void"], StringComparer.Ordinal) &&
            removal.Added.Count == 0,
        "Compatibility classifier did not identify a removal.");
    var signature = ApiDiff.Compare(
        ["method A.M(int value) -> void"],
        ["method A.M(string value) -> void"]);
    Require(signature.Removed.SequenceEqual(["method A.M(int value) -> void"], StringComparer.Ordinal) &&
            signature.Added.SequenceEqual(["method A.M(string value) -> void"], StringComparer.Ordinal),
        "Compatibility classifier did not identify both sides of a signature change.");
}

static void ValidateReleaseUnit(string root)
{
    var propsPath = Path.Combine(root, "eng", "WidgetSdkRelease.props");
    var document = XDocument.Load(propsPath, LoadOptions.None);
    var properties = document.Descendants("PropertyGroup").Elements()
        .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);
    var version = Required(properties, "WidgetSdkReleaseVersion");
    var packageId = Required(properties, "WidgetSdkPackageId");
    var templateVersion = Required(properties, "ControllerWidgetTemplateVersion");
    Require(string.Equals(packageId, "GameBarAlternative.WidgetSdk", StringComparison.Ordinal),
        $"Unsupported WidgetSdk package ID '{packageId}' in eng/WidgetSdkRelease.props.");
    Require(int.TryParse(templateVersion, out var parsedTemplateVersion) && parsedTemplateVersion > 0,
        $"Invalid ControllerWidget template version '{templateVersion}' in eng/WidgetSdkRelease.props.");

    foreach (var assembly in new[] { typeof(Widget).Assembly, typeof(CliApplication).Assembly })
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.Ordinal);
        Require(metadata.GetValueOrDefault("WidgetSdkReleaseVersion") == version,
            $"{assembly.GetName().Name} does not carry release version '{version}'.");
        Require(metadata.GetValueOrDefault("WidgetSdkPackageId") == packageId,
            $"{assembly.GetName().Name} does not carry package ID '{packageId}'.");
        Require(metadata.GetValueOrDefault("ControllerWidgetTemplateVersion") == templateVersion,
            $"{assembly.GetName().Name} does not carry template version '{templateVersion}'.");
        Require(ReleaseVersion(assembly) == version,
            $"{assembly.GetName().Name} informational version does not match '{version}'.");
    }

    using var manifestDocument = JsonDocument.Parse(File.ReadAllBytes(
        Path.Combine(root, "templates", "ControllerWidget", "template.json")));
    Require(manifestDocument.RootElement.GetProperty("templateVersion").GetInt32() == parsedTemplateVersion,
        "ControllerWidget template.json does not match eng/WidgetSdkRelease.props.");
    var projectTemplate = File.ReadAllText(Path.Combine(
        root, "templates", "ControllerWidget", "WidgetName.csproj.template"));
    Require(projectTemplate.Contains(
            "PackageReference Include=\"{{SdkPackageId}}\" Version=\"{{SdkVersion}}\"",
            StringComparison.Ordinal),
        "ControllerWidget project must bind its SDK dependency to the exact generated package version.");
}

static string Required(IReadOnlyDictionary<string, string> values, string key)
{
    if (!values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"eng/WidgetSdkRelease.props is missing '{key}'.");
    return value;
}

static string ReleaseVersion(Assembly assembly)
{
    var informational = assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException($"{assembly.GetName().Name} has no informational version.");
    var plus = informational.IndexOf('+');
    return plus < 0 ? informational : informational[..plus];
}

static string FindRepositoryRoot()
{
    var current = new DirectoryInfo(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "eng", "WidgetSdkRelease.props")) &&
            File.Exists(Path.Combine(current.FullName, "src", "WidgetSdk", "WidgetSdk.csproj")))
            return current.FullName;
        current = current.Parent;
    }
    throw new InvalidOperationException("Could not locate the repository WidgetSdk release contract.");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed record ApiDiff(
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Added)
{
    public static ApiDiff Compare(
        IReadOnlyCollection<string> expected,
        IReadOnlyCollection<string> current)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var currentSet = current.ToHashSet(StringComparer.Ordinal);
        return new(
            expectedSet.Except(currentSet, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            currentSet.Except(expectedSet, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
