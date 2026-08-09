using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.GbarCli;

internal static class ScenarioPreviewCommand
{
    internal const string DefaultManifestName = "gbar.scenarios.json";
    internal const int MaximumManifestBytes = 64 * 1024;
    internal const int MaximumScenarios = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12,
    };

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            await output.WriteLineAsync(HelpText);
            return 0;
        }
        var parsed = new CommandArguments(
            args, "--scenario", "--output", "--instance");
        if (parsed.Positionals.Count != 1)
            throw new CliUsageException(
                "Usage: gbar preview <widget-directory|gbar.scenarios.json> [--scenario <name>] [--output <snapshot.json>] [--instance <id>]");

        var manifestPath = ResolveManifest(parsed.Positionals[0]);
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken)
            .ConfigureAwait(false);
        var scenarioName = parsed.Option("--scenario");
        if (scenarioName is null)
        {
            await output.WriteLineAsync($"Scenarios in {manifestPath}:");
            foreach (var scenario in manifest.Scenarios)
            {
                var suffix = scenario.Description is null
                    ? string.Empty
                    : $" - {scenario.Description}";
                await output.WriteLineAsync($"  {scenario.Name}{suffix}");
            }
            await output.WriteLineAsync(
                "Scenario execution is disabled until an isolated preview process is available; listing does not load the provider assembly.");
            return 0;
        }

        ValidateScenarioName(scenarioName, "--scenario");
        _ = manifest.Scenarios.SingleOrDefault(item =>
            string.Equals(item.Name, scenarioName, StringComparison.Ordinal)) ??
            throw new CliUsageException(
                $"Scenario '{scenarioName}' was not declared. Available: {string.Join(", ", manifest.Scenarios.Select(item => item.Name))}.");
        throw new CliOperationException(
            "Scenario execution is unavailable because isolated preview execution is not yet implemented. " +
            "Manifest listing and validation remain available without loading provider assemblies.");
    }

    internal const string HelpText = """
        Usage:
          gbar preview <widget-directory|gbar.scenarios.json>
          gbar preview <widget-directory|gbar.scenarios.json> --scenario <name> [--output <snapshot.json>] [--instance <id>]

        A directory uses gbar.scenarios.json. With no --scenario, declarations
        are listed without loading or executing the provider assembly.

        Manifest v1:
          {
            "version": 1,
            "assembly": "bin/Release/net8.0/MyWidget.Scenarios.dll",
            "providerType": "Dev.Example.MyWidgetScenarios",
            "scenarios": [
              { "name": "playing", "factory": "Playing", "description": "Local playback fixture" }
            ]
          }

        Manifest listing validates bounded declarations without resolving or
        loading the provider assembly. --scenario execution is intentionally
        disabled until factories can run in an isolated, forcibly terminable
        preview process. The CLI never executes scenario code in-process.
        """;

    private static string ResolveManifest(string source)
    {
        var path = Path.GetFullPath(source);
        if (Directory.Exists(path))
        {
            RejectReparsePoint(path, "Scenario directory");
            path = Path.Combine(path, DefaultManifestName);
        }
        if (!File.Exists(path))
            throw new CliUsageException(
                $"Scenario manifest does not exist: {path}. Run 'gbar preview help' for the v1 format.");
        RejectReparsePoint(path, "Scenario manifest");
        return path;
    }

    private static async Task<ScenarioManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(path).Length;
        if (length is <= 0 or > MaximumManifestBytes)
            throw new CliOperationException(
                $"Scenario manifest must be between 1 and {MaximumManifestBytes} bytes.");
        try
        {
            var manifest = JsonSerializer.Deserialize<ScenarioManifest>(
                await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false),
                JsonOptions) ?? throw new JsonException("Manifest payload was null.");
            ValidateManifest(manifest);
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new CliOperationException(
                $"Scenario manifest is invalid: {exception.Message}", exception);
        }
    }

    private static void ValidateManifest(ScenarioManifest manifest)
    {
        if (manifest.Version != 1)
            throw new CliOperationException(
                $"Unsupported scenario manifest version {manifest.Version}.");
        ValidateRelativeAssemblyPath(manifest.Assembly);
        ValidateTypeName(manifest.ProviderType);
        if (manifest.Scenarios is null || manifest.Scenarios.Count is < 1 or > MaximumScenarios)
            throw new CliOperationException(
                $"Scenario manifest must declare between 1 and {MaximumScenarios} scenarios.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in manifest.Scenarios)
        {
            if (scenario is null)
                throw new CliOperationException("Scenario declarations cannot be null.");
            ValidateScenarioName(scenario.Name, "scenario name");
            ValidateFactoryName(scenario.Factory);
            if (!names.Add(scenario.Name))
                throw new CliOperationException(
                    $"Scenario name '{scenario.Name}' is declared more than once.");
            if (scenario.Description is { } description &&
                (description.Length > 160 || description.Any(char.IsControl)))
                throw new CliOperationException(
                    $"Scenario '{scenario.Name}' description must be one line and at most 160 characters.");
        }
    }

    private static void RejectReparsePoint(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new CliOperationException($"{label} cannot traverse a reparse point.");
    }

    private static void ValidateRelativeAssemblyPath(string value)
    {
        var segments = value?.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.None);
        if (string.IsNullOrWhiteSpace(value) || value.Length > 240 ||
            Path.IsPathRooted(value) || value.Any(char.IsControl) ||
            value.Contains(':', StringComparison.Ordinal) ||
            segments is null || segments.Any(segment =>
                string.IsNullOrEmpty(segment) || segment is "." or "..") ||
            !Path.GetExtension(value).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            throw new CliOperationException(
                "Scenario assembly must be a bounded relative .dll path.");
    }

    private static void ValidateTypeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 ||
            !(char.IsAsciiLetter(value[0]) || value[0] == '_') ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or '+')))
            throw new CliOperationException(
                "Scenario providerType must be a bounded ASCII type name.");
    }

    private static void ValidateScenarioName(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            !char.IsAsciiLetterOrDigit(value[0]) ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
            throw new CliOperationException(
                $"{field} must be 1-64 ASCII letters, digits, or hyphens and start with a letter or digit.");
    }

    private static void ValidateFactoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !(char.IsAsciiLetter(value[0]) || value[0] == '_') ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
            throw new CliOperationException(
                "Scenario factory must be a bounded ASCII method name.");
    }

    private sealed record ScenarioManifest
    {
        public required int Version { get; init; }
        public required string Assembly { get; init; }
        public required string ProviderType { get; init; }
        public required IReadOnlyList<ScenarioDeclaration> Scenarios { get; init; }
    }

    private sealed record ScenarioDeclaration
    {
        public required string Name { get; init; }
        public required string Factory { get; init; }
        public string? Description { get; init; }
    }
}
