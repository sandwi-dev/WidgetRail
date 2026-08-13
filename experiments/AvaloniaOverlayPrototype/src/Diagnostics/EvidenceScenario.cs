using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

internal static class EvidenceScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task RunAsync(
        MainWindow window,
        IClassicDesktopStyleApplicationLifetime desktop,
        PrototypeArguments arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(arguments.EvidencePath);
        try
        {
            FrameDiagnostics.Reset();
            var process = Process.GetCurrentProcess();
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
            var startedAtUtc = process.StartTime.ToUniversalTime();
            window.Show();
            var initial = await window.NavigateAsync(PrototypeRoute.Settings);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var firstCompleteFrameMilliseconds = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;
            var visibleIdle = await SampleAsync(process, TimeSpan.FromMilliseconds(1_250));

            var switchSamples = new List<SwitchSample>();
            foreach (var route in new[]
                     {
                         PrototypeRoute.AudioMixer,
                         PrototypeRoute.SpotifyPlayer,
                         PrototypeRoute.GameLauncher,
                         PrototypeRoute.Settings,
                     })
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await window.NavigateAsync(route);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                switchSamples.Add(new SwitchSample(route.ToString(), result.Outcome.ToString(), stopwatch.Elapsed.TotalMilliseconds));
            }

            var visiblePrivateMemoryBytes = process.PrivateMemorySize64;
            window.Hide();
            await Task.Delay(TimeSpan.FromSeconds(2));
            var hiddenAfterUse = await SampleAsync(process, TimeSpan.FromMilliseconds(2_000));

            var frames = FrameDiagnostics.RecordedFrames;
            var transitions = FrameDiagnostics.RecordedTransitions;
            await using var executableStream = File.OpenRead(executable);
            var executableHash = Convert.ToHexString(await SHA256.HashDataAsync(executableStream));
            var artifact = new MeasurementArtifact(
                "AVP-002",
                arguments.SourceCommit ?? "unavailable",
                startedAtUtc,
                Environment.OSVersion.VersionString,
                Environment.Version.ToString(),
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unavailable",
                typeof(Avalonia.Application).Assembly.GetName().Version?.ToString() ?? "unavailable",
                RuntimeInformation.ProcessArchitecture.ToString(),
                executableHash,
                firstCompleteFrameMilliseconds,
                initial.Outcome.ToString(),
                visiblePrivateMemoryBytes,
                visiblePrivateMemoryBytes / 1024d / 1024d < 250,
                visiblePrivateMemoryBytes / 1024d / 1024d < 500,
                visibleIdle,
                hiddenAfterUse,
                hiddenAfterUse.NormalizedCpuPercent < 0.5,
                switchSamples,
                frames,
                frames.All(frame =>
                    frame.TransparentRoot &&
                    frame.OpaqueBlackFallbackAbsent &&
                    frame.RequiredElementsContained &&
                    frame.AllVisibleRequiredElementsValid),
                transitions,
                Enum.GetValues<PrototypeRoute>().All(route =>
                    transitions.Where(sample => sample.Route == route)
                        .TakeLast(3)
                        .Select(sample => sample.Phase)
                        .SequenceEqual(Enum.GetValues<Navigation.TransitionPhase>())) &&
                transitions.All(sample =>
                    sample.TransparentRoot &&
                    sample.OpaqueBlackBrushAbsent &&
                    sample.AvaloniaSurfaceCoveragePresent),
                "Vortice.XInput 3.8.3",
                new[]
                {
                    "Transition samples inspect Avalonia visual/composition-surface brushes and coverage; the physical Windows compositor verdict remains manual.",
                    "Controller automation uses a fake narrow adapter; physical controller compatibility, reconnect, and feel remain planner/user checks.",
                    "XInput is limited to four XInput-compatible slots and does not provide durable device identity.",
                    "GPU frame cost unavailable in AVP-002 without an authorized ETW/PresentMon capture lane.",
                    "Hidden-before-first-frame was not sampled because delaying initial show would invalidate cold-start timing; hidden-after-use is retained instead.",
                    "Physical visual quality remains a planner launch and user verdict.",
                });

            var evidencePath = Path.GetFullPath(arguments.EvidencePath);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(artifact, JsonOptions));
            desktop.Shutdown();
        }
        catch (Exception exception)
        {
            var failurePath = Path.GetFullPath(arguments.EvidencePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(failurePath)!);
            await File.WriteAllTextAsync(
                failurePath,
                JsonSerializer.Serialize(
                    new
                    {
                        assignment = "AVP-002",
                        errorType = exception.GetType().Name,
                        error = "Evidence lifecycle failed; inspect the local process trace.",
                    },
                    JsonOptions));
            desktop.Shutdown(1);
        }
    }

    private static async Task<ResourceSample> SampleAsync(Process process, TimeSpan duration)
    {
        process.Refresh();
        var cpuStart = process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(duration);
        elapsed.Stop();
        process.Refresh();
        var cpuDelta = process.TotalProcessorTime - cpuStart;
        var cpuPercent = cpuDelta.TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100d;
        return new ResourceSample(
            elapsed.Elapsed.TotalMilliseconds,
            cpuPercent,
            process.PrivateMemorySize64,
            process.PrivateMemorySize64 / 1024d / 1024d);
    }

    private sealed record MeasurementArtifact(
        string Assignment,
        string SourceCommit,
        DateTime ProcessStartedAtUtc,
        string OperatingSystem,
        string DotNetRuntime,
        string PrototypeAssemblyVersion,
        string AvaloniaVersion,
        string ProcessArchitecture,
        string ExecutableSha256,
        double ColdStartToFirstCompleteFrameMilliseconds,
        string InitialNavigationOutcome,
        long VisiblePrivateMemoryBytes,
        bool VisiblePrivateMemoryUnder250MiB,
        bool VisiblePrivateMemoryUnder500MiB,
        ResourceSample VisibleIdleSample,
        ResourceSample HiddenAfterUseSample,
        bool HiddenRenderingEffectivelyIdle,
        IReadOnlyList<SwitchSample> SwitchToCompleteFrameSamples,
        IReadOnlyList<FrameSnapshot> CompleteFrames,
        bool CompleteFrameDiagnosticsPassed,
        IReadOnlyList<TransitionDiagnosticSample> TransitionSurfaceSamples,
        bool TransitionSurfaceDiagnosticsPassed,
        string ControllerDependency,
        IReadOnlyList<string> UnavailableOrManualEvidence);

    private sealed record ResourceSample(
        double IntervalMilliseconds,
        double NormalizedCpuPercent,
        long PrivateMemoryBytes,
        double PrivateMemoryMiB);

    private sealed record SwitchSample(string Route, string Outcome, double Milliseconds);
}
