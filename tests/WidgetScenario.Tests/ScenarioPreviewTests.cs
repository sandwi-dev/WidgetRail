using System.Diagnostics;
using System.Text.Json;
using GameBarAlternative.GbarCli;
using GameBarAlternative.WidgetSdk;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.WidgetScenario.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ScenarioPreviewTests
{
    private static FixtureDirectory? _basic;
    private static FixtureDirectory? _capability;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext _)
    {
        _basic = await FixtureDirectory.CreateAsync("PreviewBasic", BasicSource);
        _capability = await FixtureDirectory.CreateAsync("PreviewMedia", CapabilitySource);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _capability?.Dispose();
        _basic?.Dispose();
    }

    [TestMethod]
    public async Task GeneratedBasicScenarioRunsOutsideCliAndPinsExactContent()
    {
        var fixture = Required(_basic);
        var outside = Path.Combine(fixture.Parent, "outside-secret.txt");
        await File.WriteAllTextAsync(outside, "must-not-be-readable");
        Environment.SetEnvironmentVariable("DLV108_OUTSIDE_PATH", outside);
        var destination = Path.Combine(fixture.Path, "stable.snapshot.json");
        try
        {
            var result = await RunCliAsync(
                ["preview", fixture.Path, "--scenario", "stable", "--output", destination]);

            Assert.AreEqual(0, result.Code, result.Error);
            StringAssert.Contains(result.Output, "isolated preview worker");
            using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(destination));
            Assert.AreEqual(1, document.RootElement.GetProperty("version").GetInt32());
            Assert.AreEqual("stable", document.RootElement.GetProperty("scenario").GetString());
            var snapshot = document.RootElement.GetProperty("snapshot");
            var focus = snapshot.GetProperty("initialFocusId").GetString()!;
            StringAssert.StartsWith(focus, "pid-");
            var workerPid = int.Parse(focus.AsSpan(4));
            Assert.AreNotEqual(Environment.ProcessId, workerPid);
            Assert.IsTrue(ProcessExited(workerPid), "The isolated scenario worker remained alive.");
            StringAssert.Contains(document.RootElement.GetRawText(), "outside-denied");
            Assert.IsFalse(document.RootElement.GetRawText().Contains(
                "must-not-be-readable", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DLV108_OUTSIDE_PATH", null);
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [TestMethod]
    public async Task CapabilityScenarioUsesOnlyItsTypedFakeService()
    {
        var result = await RunCliAsync(
            ["preview", Required(_capability).Path, "--scenario", "media"]);

        Assert.AreEqual(0, result.Code, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.AreEqual("media", document.RootElement.GetProperty("scenario").GetString());
        StringAssert.Contains(document.RootElement.GetRawText(), "Fixture Song");
        StringAssert.Contains(document.RootElement.GetRawText(), "Fixture Artist");
    }

    [TestMethod]
    public async Task MalformedCrashHangAndStaleFactoriesFailSafely()
    {
        var fixture = Required(_basic);
        foreach (var scenario in new[] { "wrong", "crash", "hang", "changing" })
        {
            var destination = Path.Combine(fixture.Path, scenario + ".snapshot.json");
            await File.WriteAllTextAsync(destination, "sentinel");
            var result = await RunCliAsync(
                ["preview", fixture.Path, "--scenario", scenario, "--output", destination]);
            Assert.AreEqual(1, result.Code, $"{scenario}: {result.Error}");
            Assert.IsFalse(result.Error.Contains(fixture.Path, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("sentinel", await File.ReadAllTextAsync(destination));
        }

        var neighbor = await RunCliAsync(["preview", fixture.Path]);
        Assert.AreEqual(0, neighbor.Code, neighbor.Error);
        StringAssert.Contains(neighbor.Output, "stable");
    }

    [TestMethod]
    public async Task CancellationTerminatesWorkerAndNeighboringInvocationStillRuns()
    {
        var fixture = Required(_basic);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var cancelled = await RunCliAsync(
            ["preview", fixture.Path, "--scenario", "hang"], cancellation.Token);
        Assert.AreEqual(130, cancelled.Code, cancelled.Error);

        var neighbor = await RunCliAsync(
            ["preview", fixture.Path, "--scenario", "stable"]);
        Assert.AreEqual(0, neighbor.Code, neighbor.Error);
        using var document = JsonDocument.Parse(neighbor.Output);
        Assert.AreEqual("stable", document.RootElement.GetProperty("scenario").GetString());
    }

    [TestMethod]
    public void ScenarioDefinitionOwnsOneVersionedWidgetAndFakeServiceSnapshot()
    {
        var services = new WidgetTestHostServicesBuilder().Build();
        var widget = new ContractWidget();
        var definition = new WidgetScenarioDefinition(widget, services);

        Assert.AreEqual(WidgetScenarioDefinition.CurrentVersion, definition.Version);
        Assert.AreSame(widget, definition.Widget);
        Assert.AreSame(services, definition.HostServices);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new WidgetScenarioDefinition(widget, services, version: 2));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new WidgetScenarioDefinition(null!, services));
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new WidgetScenarioDefinition(widget, null!));
    }

    [TestMethod]
    public async Task OutputInputsAreRejectedBeforeFactoryExecution()
    {
        var fixture = Required(_basic);
        var manifest = Path.Combine(fixture.Path, "gbar.scenarios.json");
        var before = await File.ReadAllBytesAsync(manifest);

        var result = await RunCliAsync(
            ["preview", fixture.Path, "--scenario", "crash", "--output", manifest]);

        Assert.AreEqual(2, result.Code, result.Error);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(manifest));
    }

    private static FixtureDirectory Required(FixtureDirectory? fixture) =>
        fixture ?? throw new InvalidOperationException("Scenario fixture was not initialized.");

    private static bool ProcessExited(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task<CliResult> RunCliAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await CliApplication.RunAsync(
            args, output, error, remoteHttpHandler: null, cancellationToken);
        return new(code, output.ToString(), error.ToString());
    }

    private sealed record CliResult(int Code, string Output, string Error);

    private sealed class ContractWidget : Widget
    {
        public override WidgetView Render() => new(UI.Text("contract", "contract"));
    }

    private sealed class FixtureDirectory : IDisposable
    {
        private FixtureDirectory(string parent, string path)
        {
            Parent = parent;
            Path = path;
        }

        internal string Parent { get; }
        internal string Path { get; }

        internal static async Task<FixtureDirectory> CreateAsync(
            string name,
            string scenarioSource)
        {
            var parent = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "gbar-external-scenarios", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parent);
            var path = System.IO.Path.Combine(parent, name);
            try
            {
                var created = await RunCliAsync(
                    ["new", "widget", name, "--output", path,
                     "--id", $"dev.preview.{name.ToLowerInvariant()}",
                     "--publisher", "dev.preview"]);
                Assert.AreEqual(0, created.Code, created.Error);
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(path, "src", "Scenarios.cs"), scenarioSource);
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(path, "gbar.scenarios.json"), Manifest(name));
                await RunProcessAsync("dotnet",
                    ["restore", name + ".csproj", "--configfile", "NuGet.Config"], path);
                await RunProcessAsync("dotnet",
                    ["build", name + ".csproj", "-c", "Release", "--no-restore"], path);
                return new FixtureDirectory(parent, path);
            }
            catch
            {
                if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
                throw;
            }
        }

        private static string Manifest(string name) => $$"""
            {
              "version": 1,
              "assembly": "bin/Release/net8.0/{{name}}.dll",
              "providerType": "dev.preview.Scenarios",
              "scenarios": [
                { "name": "stable", "factory": "Stable" },
                { "name": "media", "factory": "Media" },
                { "name": "changing", "factory": "Changing" },
                { "name": "wrong", "factory": "Wrong" },
                { "name": "crash", "factory": "Crash" },
                { "name": "hang", "factory": "Hang" }
              ]
            }
            """;

        private static async Task RunProcessAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string workingDirectory)
        {
            var start = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ??
                throw new InvalidOperationException($"Could not start {fileName}.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new AssertFailedException($"{fileName} exceeded 60 seconds.");
            }
            var combined = (await output) + (await error);
            Assert.AreEqual(0, process.ExitCode, combined);
        }

        public void Dispose()
        {
            if (Directory.Exists(Parent)) Directory.Delete(Parent, recursive: true);
        }
    }

    private const string BasicSource = """
        using GameBarAlternative.WidgetProtocol;
        using GameBarAlternative.WidgetSdk;

        namespace dev.preview;

        public static class Scenarios
        {
            public static WidgetScenarioDefinition Stable()
            {
                return new(new StableWidget(), new WidgetTestHostServicesBuilder().Build());
            }

            public static WidgetScenarioDefinition Changing() =>
                new(new ChangingWidget(), new WidgetTestHostServicesBuilder().Build());
            public static string Wrong() => "wrong return";
            public static WidgetScenarioDefinition Crash()
            {
                Environment.FailFast("scenario crash");
                throw new InvalidOperationException();
            }
            public static WidgetScenarioDefinition Hang()
            {
                Thread.Sleep(Timeout.Infinite);
                throw new InvalidOperationException();
            }
            public static WidgetScenarioDefinition Media() => Stable();
        }

        public sealed class StableWidget : Widget
        {
            private bool _active;
            protected override ValueTask OnActivatedAsync(CancellationToken token)
            {
                _active = true;
                return ValueTask.CompletedTask;
            }
            public override WidgetView Render()
            {
                var outside = "outside-denied";
                try
                {
                    var path = Environment.GetEnvironmentVariable("DLV108_OUTSIDE_PATH");
                    if (!string.IsNullOrEmpty(path)) outside = File.ReadAllText(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
                var id = "pid-" + Environment.ProcessId;
                return new WidgetView(
                    UI.Stack("root", UI.Text(_active ? "active" : "created", "state"),
                        UI.Text(outside, "outside"), UI.Button("Ready", "ready", id)),
                    InitialFocusId: id);
            }
        }

        public sealed class ChangingWidget : Widget
        {
            private int _render;
            public override WidgetView Render() =>
                new(UI.Text("render-" + ++_render, "changing"));
        }
        """;

    private const string CapabilitySource = """
        using GameBarAlternative.WidgetProtocol;
        using GameBarAlternative.WidgetSdk;

        namespace dev.preview;

        public static class Scenarios
        {
            public static WidgetScenarioDefinition Media()
            {
                var session = new WidgetMediaSession(
                    "fixture", "Fixture Player", "Fixture Song", "Fixture Artist",
                    WidgetMediaPlaybackStatus.Playing, 1000, 60000, 1234, 1, true,
                    true, true, true, true, true);
                var services = new WidgetTestHostServicesBuilder()
                    .WithResponse(WidgetMediaCapabilities.GetSessions,
                        (IReadOnlyList<WidgetMediaSession>)[session])
                    .Build();
                return new(new MediaWidget(), services);
            }
            public static WidgetScenarioDefinition Stable() => Media();
            public static WidgetScenarioDefinition Changing() => Media();
            public static WidgetScenarioDefinition Wrong() => Media();
            public static WidgetScenarioDefinition Crash() => Media();
            public static WidgetScenarioDefinition Hang() => Media();
        }

        public sealed class MediaWidget : Widget
        {
            private string _title = "Loading";
            private string _artist = "";
            protected override async ValueTask OnActivatedAsync(CancellationToken token)
            {
                var sessions = await HostServices.Media.GetSessionsAsync(token);
                _title = sessions[0].Title;
                _artist = sessions[0].Artist;
            }
            public override WidgetView Render() =>
                new(UI.Stack("root", UI.Text(_title, "title"), UI.Text(_artist, "artist")));
        }
        """;
}
