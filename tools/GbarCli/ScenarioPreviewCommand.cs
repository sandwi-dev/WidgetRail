using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

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
            foreach (var declaration in manifest.Scenarios)
            {
                var suffix = declaration.Description is null
                    ? string.Empty
                    : $" - {declaration.Description}";
                await output.WriteLineAsync($"  {declaration.Name}{suffix}");
            }
            await output.WriteLineAsync(
                "Listing does not load the provider assembly. Select one name with --scenario to execute it in the isolated preview worker.");
            return 0;
        }

        ValidateScenarioName(scenarioName, "--scenario");
        var scenario = manifest.Scenarios.SingleOrDefault(item =>
            string.Equals(item.Name, scenarioName, StringComparison.Ordinal)) ??
            throw new CliUsageException(
                $"Scenario '{scenarioName}' was not declared. Available: {string.Join(", ", manifest.Scenarios.Select(item => item.Name))}.");
        var root = Path.GetDirectoryName(manifestPath)!;
        var assemblyPath = Path.GetFullPath(Path.Combine(root, manifest.Assembly));
        EnsureContained(root, assemblyPath);
        RejectPathReparsePoints(root, assemblyPath);
        if (!File.Exists(assemblyPath))
            throw new CliOperationException("The declared scenario assembly does not exist.");
        var instance = parsed.Option("--instance") ?? $"preview.{scenarioName}";
        ValidateInstance(instance);
        var destination = parsed.Option("--output") is { } requestedOutput
            ? ValidateOutput(requestedOutput, manifestPath, assemblyPath)
            : null;
        var result = await ExecuteAsync(Path.GetDirectoryName(assemblyPath)!, assemblyPath, manifest.ProviderType,
            scenario, instance, cancellationToken).ConfigureAwait(false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
        if (bytes.Length > WidgetRuntimeProtocol.DefaultMaximumMessageBytes)
            throw new CliOperationException("Scenario result exceeded the semantic preview bound.");
        if (destination is null)
        {
            await output.WriteLineAsync(Encoding.UTF8.GetString(bytes));
        }
        else
        {
            await WriteResultAsync(destination, bytes, cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                $"Scenario '{scenarioName}' completed in an isolated preview worker ({bytes.Length} bytes).");
        }
        return 0;
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
        loading the provider assembly. --scenario executes the one declared
        factory in a capability-free AppContainer/Job worker, transitions it
        through the normal lifecycle, and emits a versioned semantic result.
        The CLI never loads or executes scenario code in-process.
        """;

    private static async Task<WidgetScenarioResult> ExecuteAsync(
        string root,
        string assemblyPath,
        string providerType,
        ScenarioDeclaration scenario,
        string instance,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(
            Path.GetDirectoryName(typeof(CliApplication).Assembly.Location)!, "gbar.exe");
        if (!File.Exists(executable))
            throw new CliOperationException("The isolated preview worker executable is unavailable.");
        var isolationDigest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..32];
        var diagnostics = new List<WidgetScenarioDiagnostic>(4);
        await using var client = new WidgetProcessClient(new WidgetProcessOptions
        {
            ExecutablePath = executable,
            Arguments =
            [
                "__scenario-worker",
                "--scenario-root", root,
                "--scenario-assembly", assemblyPath,
                "--provider-type", providerType,
                "--scenario-factory", scenario.Factory,
            ],
            WidgetInstanceId = instance,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            RequestTimeout = TimeSpan.FromSeconds(5),
            MaximumRestartAttempts = 0,
            IsolationPolicy = WidgetWorkerIsolationPolicy.RequireAppContainer,
            IsolationKey = "scenario-preview:" + isolationDigest,
            ContentLeaseFactory = _ => PinnedDirectoryContentLease.Acquire(root),
            StartupExitDiagnostics = new Dictionary<int, string> { [72] = "invalid_scenario" },
        });
        client.Failed += (_, failure) =>
        {
            if (diagnostics.Count < 4)
                diagnostics.Add(new("worker", failure.DiagnosticCode ??
                    failure.Reason.ToString().ToLowerInvariant()));
        };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Visible, deadline.Token)
                .ConfigureAwait(false);
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Interactive, deadline.Token)
                .ConfigureAwait(false);
            var first = await client.GetSnapshotAsync(deadline.Token).ConfigureAwait(false);
            var second = await client.GetSnapshotAsync(deadline.Token).ConfigureAwait(false);
            if (!Equivalent(first, second))
                throw new CliOperationException(
                    "Scenario rendering was not deterministic across repeated snapshots.");
            await client.SetLifecycleStateAsync(WidgetLifecycleState.Background, deadline.Token)
                .ConfigureAwait(false);
            return new(WidgetScenarioResult.CurrentVersion, scenario.Name, second,
                diagnostics.ToArray());
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new CliOperationException(
                "Scenario execution exceeded the 10-second isolated-worker deadline.");
        }
        catch (Exception exception) when (exception is WidgetProcessException or
                                           WidgetProcessAdmissionException or
                                           WidgetProtocolViolationException or
                                           UnauthorizedAccessException)
        {
            throw new CliOperationException("The isolated scenario worker failed safely.");
        }
        catch (IOException)
        {
            throw new CliOperationException("The isolated scenario worker disconnected.");
        }
    }

    private static bool Equivalent(ViewSnapshot left, ViewSnapshot right) =>
        SnapshotJson.Serialize(left with { Sequence = 0 })
            .AsSpan()
            .SequenceEqual(SnapshotJson.Serialize(right with { Sequence = 0 }));

    private static async Task WriteResultAsync(
        string destination,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ValidateOutput(
        string destination,
        string manifestPath,
        string assemblyPath)
    {
        var path = Path.GetFullPath(destination);
        if (string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, assemblyPath, StringComparison.OrdinalIgnoreCase))
            throw new CliUsageException("Scenario output cannot overwrite scenario inputs.");
        return path;
    }

    private static void ValidateInstance(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.')))
            throw new CliUsageException(
                "--instance must be 1-128 ASCII letters, digits, dots, hyphens, or underscores.");
    }

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative is "." or ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            throw new CliOperationException("Scenario assembly escaped the scenario directory.");
    }

    private static void RejectPathReparsePoints(string root, string path)
    {
        var current = Path.GetFullPath(root);
        RejectReparsePoint(current, "Scenario directory");
        foreach (var segment in Path.GetRelativePath(current, path).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current))
                RejectReparsePoint(current, "Scenario assembly path");
        }
    }

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
