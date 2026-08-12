using System.Text.Json;
using GameBarAlternative.GbarCli;

internal static class ScenarioPreviewTests
{
    internal static async Task Run()
    {
        await ExplainsTheIsolatedContractAsync();
        await ListsWithoutResolvingTheAssemblyAsync();
        await MissingAssemblyFailsWithoutWritingAsync();
        await RejectsMalformedAndUnboundedDeclarationsAsync();
    }

    private static async Task ExplainsTheIsolatedContractAsync()
    {
        var help = await RunCliAsync("preview", "help");
        Equal(0, help.Code);
        Contains("gbar.scenarios.json", help.Output);
        Contains("without resolving or", help.Output);
        Contains("loading the provider assembly", help.Output);
        Contains("capability-free AppContainer/Job worker", help.Output);
        Contains("never loads or executes scenario code in-process", help.Output);
    }

    private static async Task ListsWithoutResolvingTheAssemblyAsync()
    {
        using var temp = new ScenarioDirectory();
        await temp.WriteManifestAsync(
            Scenario("playing", "Playing", "Authenticated playback without OAuth"),
            Scenario("empty", "Empty", "No active playback"));

        var listed = await RunCliAsync("preview", temp.Path);
        Equal(0, listed.Code);
        Contains("playing - Authenticated playback without OAuth", listed.Output);
        Contains("empty - No active playback", listed.Output);
        Contains("Listing does not load the provider assembly", listed.Output);
        False(File.Exists(Path.Combine(temp.Path, ScenarioDirectory.MissingAssembly)),
            "Listing unexpectedly required or created the provider assembly.");

        var unknown = await RunCliAsync(
            "preview", temp.Path, "--scenario", "missing");
        Equal(2, unknown.Code);
        Contains("Available: playing, empty", unknown.Error);
    }

    private static async Task MissingAssemblyFailsWithoutWritingAsync()
    {
        using var temp = new ScenarioDirectory();
        await temp.WriteManifestAsync(Scenario("playing", "Playing"));
        var destination = Path.Combine(temp.Path, "playing.snapshot.json");

        var result = await RunCliAsync(
            "preview", temp.Path, "--scenario", "playing",
            "--output", destination, "--instance", "preview.playing");

        Equal(1, result.Code);
        Contains("declared scenario assembly does not exist", result.Error);
        DoesNotContain(ScenarioDirectory.MissingAssembly, result.Error);
        False(File.Exists(destination),
            "Fail-closed execution unexpectedly wrote a snapshot.");

        var manifestPath = Path.Combine(temp.Path, "gbar.scenarios.json");
        var before = await File.ReadAllTextAsync(manifestPath);
        var overwrite = await RunCliAsync(
            "preview", temp.Path, "--scenario", "playing", "--output", manifestPath);
        Equal(1, overwrite.Code);
        Contains("declared scenario assembly does not exist", overwrite.Error);
        Equal(before, await File.ReadAllTextAsync(manifestPath));
    }

    private static async Task RejectsMalformedAndUnboundedDeclarationsAsync()
    {
        using var temp = new ScenarioDirectory();
        await temp.WriteRawManifestAsync("""
            {
              "version": 1,
              "assembly": "missing-provider.dll",
              "providerType": "Tests.PreviewScenarios",
              "scenarios": [{ "name": "playing", "factory": "Playing" }],
              "unexpected": true
            }
            """);
        var unknownField = await RunCliAsync("preview", temp.Path);
        Equal(1, unknownField.Code);
        Contains("Scenario manifest is invalid", unknownField.Error);

        await temp.WriteManifestAsync(
            Scenario("playing", "Playing"), Scenario("playing", "Empty"));
        var duplicate = await RunCliAsync("preview", temp.Path);
        Equal(1, duplicate.Code);
        Contains("declared more than once", duplicate.Error);

        await temp.WriteRawManifestAsync("""
            {
              "version": 1,
              "assembly": "../outside.dll",
              "providerType": "Tests.PreviewScenarios",
              "scenarios": [{ "name": "playing", "factory": "Playing" }]
            }
            """);
        var traversal = await RunCliAsync("preview", temp.Path);
        Equal(1, traversal.Code);
        Contains("bounded relative .dll path", traversal.Error);

        await temp.WriteManifestAsync(Enumerable.Range(0, 33)
            .Select(index => Scenario($"scenario-{index}", $"Scenario{index}"))
            .ToArray());
        var tooMany = await RunCliAsync("preview", temp.Path);
        Equal(1, tooMany.Code);
        Contains("between 1 and 32 scenarios", tooMany.Error);

        await temp.WriteRawManifestAsync(new string(' ', 64 * 1024 + 1));
        var oversized = await RunCliAsync("preview", temp.Path);
        Equal(1, oversized.Code);
        Contains("between 1 and 65536 bytes", oversized.Error);
    }

    private static object Scenario(
        string name,
        string factory,
        string? description = null) => new { name, factory, description };

    private static async Task<CliResult> RunCliAsync(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(args, output, error);
        return new(code, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int Code, string Output, string Error);

    private sealed class ScenarioDirectory : IDisposable
    {
        internal const string MissingAssembly = "missing-provider.dll";

        internal ScenarioDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "gbar-scenario-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal Task WriteManifestAsync(params object[] scenarios) =>
            WriteRawManifestAsync(JsonSerializer.Serialize(new
            {
                version = 1,
                assembly = MissingAssembly,
                providerType = "Tests.PreviewScenarios",
                scenarios,
            }));

        internal Task WriteRawManifestAsync(string json) => File.WriteAllTextAsync(
            System.IO.Path.Combine(Path, "gbar.scenarios.json"), json);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }

    private static void False(bool value, string message)
    {
        if (value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Expected output to contain '{expected}'. Actual: {actual}");
    }

    private static void DoesNotContain(string expected, string actual)
    {
        if (actual.Contains(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Expected output not to contain '{expected}'.");
    }
}
