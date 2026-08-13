using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using GameBarAlternative.AvaloniaPrototype.Integration;
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.WidgetProtocol;

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
            var process = Process.GetCurrentProcess();
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
            var startedAtUtc = process.StartTime.ToUniversalTime();
            window.Show();
            await WaitUntilAsync(() =>
                window.IntegratedShell?.Coordinator.ViewModel.Widgets.Count > 0 &&
                window.IntegratedShell.Coordinator.CurrentFrame is not null,
                TimeSpan.FromSeconds(20),
                "The real widget catalog did not publish an initial frame.");
            var shell = window.IntegratedShell!;
            var firstCompleteFrameMilliseconds = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            // One compositor backing surface keeps logical responsive reflow distinct from native
            // swap-chain allocation. Production launches still apply each admitted surface hint.
            window.Width = 1440;
            window.Height = 810;
            shell.SetEvidenceViewport(new Size(1440, 810));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var widgetSamples = new List<WidgetEvidence>();
            var responsiveSamples = new List<ResponsiveEvidence>();
            var allNodeKinds = new HashSet<ViewNodeKind>();
            var memoryOwnershipCheckpoints = new List<MemoryOwnershipCheckpoint>
            {
                CaptureMemoryOwnership("initial-frame", process, window, shell),
            };
            foreach (var widget in shell.Coordinator.ViewModel.Widgets.ToArray())
            {
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:before-select", process, window, shell));
                var stopwatch = Stopwatch.StartNew();
                await shell.Coordinator.SelectWidgetAsync(widget.Id);
                await WaitUntilAsync(() =>
                    shell.Coordinator.CurrentFrame?.Authority.WidgetId == widget.Id &&
                    Equals(shell.AdmittedAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                    !shell.Coordinator.ViewModel.IsBusy,
                    TimeSpan.FromSeconds(20),
                    $"Widget '{widget.Id}' did not admit a complete frame.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:after-crossfade", process, window, shell));
                var frame = shell.Coordinator.CurrentFrame!;
                var nodes = Flatten(frame.Snapshot.Root).ToArray();
                foreach (var kind in nodes.Select(node => node.Kind)) allNodeKinds.Add(kind);
                var semanticControls = CaptureSemanticControls(shell, frame.Snapshot.Root);
                widgetSamples.Add(new WidgetEvidence(
                    widget.Id,
                    widget.Name,
                    frame.Authority.RuntimeGeneration,
                    frame.Authority.PresentationGeneration,
                    frame.Authority.SessionGeneration,
                    frame.Authority.WidgetInstanceId,
                    frame.Authority.SnapshotSequence,
                    frame.Authority.ActiveInputScopeId,
                    nodes.Length,
                    nodes.Count(node => node.IsFocusable),
                    nodes.Select(node => node.Kind).Distinct().Order().ToArray(),
                    shell.RealizedSemanticControls,
                    semanticControls.Count,
                    semanticControls.Count(control => control.StandardUiaIdentity),
                    semanticControls.All(control => control.BoundsHaveArea &&
                        (control.Contained || control.HonestlyScrollClipped) && control.StandardUiaIdentity),
                    frame.Snapshot.AdvancedPresentation?.Kind.ToString(),
                    frame.Snapshot.AdvancedPresentation?.Preset.ToString(),
                    stopwatch.Elapsed.TotalMilliseconds,
                    shell.Coordinator.ViewModel.HasFailure,
                    shell.Coordinator.ViewModel.StatusText));

                foreach (var fixture in new[]
                         {
                             new Size(420, 340),
                             new Size(978, 466),
                             new Size(1180, 680),
                             new Size(1440, 810),
                         })
                {
                    shell.SetEvidenceViewport(fixture);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                        $"widget:{widget.Id}:logical-size:{fixture.Width}x{fixture.Height}", process, window, shell));
                    var controls = CaptureSemanticControls(shell, frame.Snapshot.Root);
                    responsiveSamples.Add(new ResponsiveEvidence(
                        widget.Id,
                        fixture.Width,
                        fixture.Height,
                        window.RenderScaling,
                        shell.Bounds.Width,
                        shell.Bounds.Height,
                        window.Bounds.Width,
                        window.Bounds.Height,
                        shell.IsCompact,
                        controls.Count,
                        controls.Count(control => control.HonestlyScrollClipped),
                        controls.All(control => control.BoundsHaveArea &&
                            (control.Contained || control.HonestlyScrollClipped) && control.StandardUiaIdentity)));
                }
                await WaitUntilAsync(
                    () => shell.PendingArtworkRequestCount == 0,
                    TimeSpan.FromSeconds(5),
                    $"Widget '{widget.Id}' artwork did not settle.");
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:artwork-settled", process, window, shell));
            }
            memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                "after-8-widget-4-size-traversal", process, window, shell));

            // Return only the logical viewport to the representative visible shell. The one backing
            // surface remains owned and every live bridge/worker stays in the process-tree total.
            shell.SetEvidenceViewport(new Size(1180, 680));
            await WaitUntilAsync(() =>
                Equals(shell.AdmittedAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                !shell.Coordinator.ViewModel.IsBusy,
                TimeSpan.FromSeconds(20),
                "The representative visible frame did not settle before resource sampling.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                "settled-visible-before-sample", process, window, shell));

            var visibleProcesses = FindCandidateProcesses(process, window.BridgeProcessId);
            var visibleIdle = await SampleAsync(visibleProcesses, TimeSpan.FromMilliseconds(1500));
            window.Hide();
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                "after-hide-before-sample", process, window, shell));
            var hiddenProcesses = FindCandidateProcesses(process, window.BridgeProcessId);
            var hiddenAfterUse = await SampleAsync(hiddenProcesses, TimeSpan.FromMilliseconds(2000));
            memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                "after-hide", process, window, shell));
            window.Show();
            await WaitUntilAsync(() =>
                Equals(shell.AdmittedAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                !shell.Coordinator.ViewModel.IsBusy,
                TimeSpan.FromSeconds(20),
                "The shell did not reactivate after the hide/show profiling phase.");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                "after-show-reactivation", process, window, shell));

            await using var executableStream = File.OpenRead(executable);
            var executableHash = Convert.ToHexString(await SHA256.HashDataAsync(executableStream));
            var transitions = shell.TransitionSamples;
            var transitionPassed = shell.Coordinator.ViewModel.Widgets.All(widget =>
                transitions.Where(sample => sample.WidgetId == widget.Id)
                    .GroupBy(sample => sample.SnapshotSequence)
                    .Any(group => group.Select(sample => sample.Phase)
                        .SequenceEqual(Enum.GetValues<Navigation.TransitionPhase>()))) &&
                transitions.All(sample => sample.TransparentShellRoot &&
                    sample.OpaqueBlackFallbackAbsent && sample.AvaloniaSurfaceCoveragePresent &&
                    sample.VisualChildCount > 0);
            var responsivePassed = responsiveSamples.All(sample => sample.ReachableOrScrollClipped);
            var allWidgetsPassed = widgetSamples.Count == shell.Coordinator.ViewModel.Widgets.Count &&
                widgetSamples.All(sample => !sample.HasFailure && sample.RequiredSemanticControlsPassed);
            var focusedMappingProofPassed = !string.IsNullOrWhiteSpace(arguments.SourceCommit) &&
                string.Equals(
                    arguments.FocusedVerificationCommit,
                    arguments.SourceCommit,
                    StringComparison.OrdinalIgnoreCase);
            var totalVisibleMiB = visibleIdle.TotalPrivateMemoryBytes / 1024d / 1024d;
            var candidatePrivateMemoryBytes = visibleIdle.Processes
                .Where(entry => string.Equals(entry.Role, "AvaloniaOverlayPrototype", StringComparison.Ordinal))
                .Sum(entry => entry.PrivateMemoryBytes);
            var candidateVisibleMiB = candidatePrivateMemoryBytes / 1024d / 1024d;
            var resourceOwnership = new ResourceOwnershipEvidence(
                candidateVisibleMiB,
                (visibleIdle.TotalPrivateMemoryBytes - candidatePrivateMemoryBytes) / 1024d / 1024d,
                shell.TrackedRenderCount,
                shell.OwnedArtworkBitmapCount,
                shell.OwnedDecodedArtworkBytes,
                shell.PendingArtworkRequestCount,
                shell.TrackedRenderCount == 1 && shell.PendingArtworkRequestCount == 0);
            var nodeKindCoverage = new NodeKindCoverageEvidence(
                allNodeKinds.Order().ToArray(),
                Enum.GetValues<ViewNodeKind>(),
                focusedMappingProofPassed,
                "Generic_renderer_maps_every_current_node_kind_to_standard_Avalonia_controls_and_UIA",
                focusedMappingProofPassed);

            var artifact = new MeasurementArtifact(
                "AVP-004-INTEGRATION",
                arguments.SourceCommit ?? "unavailable",
                startedAtUtc,
                Environment.OSVersion.VersionString,
                Environment.Version.ToString(),
                FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "unavailable",
                typeof(Avalonia.Application).Assembly.GetName().Version?.ToString() ?? "unavailable",
                typeof(ObservableObject).Assembly.GetName().Version?.ToString() ?? "unavailable",
                RuntimeInformation.ProcessArchitecture.ToString(),
                executableHash,
                firstCompleteFrameMilliseconds,
                window.NativeGameInputAvailable,
                window.NativeLegacyGuidePollingRequired,
                shell.Coordinator.ViewModel.Widgets.Count,
                widgetSamples,
                allWidgetsPassed,
                nodeKindCoverage,
                responsiveSamples,
                responsivePassed,
                transitions,
                transitionPassed,
                shell.TransitionPresenter.PageTransition?.GetType().Name ??
                    (arguments.ReducedMotion ? "ReducedMotion" : "unavailable"),
                visibleIdle,
                hiddenAfterUse,
                resourceOwnership,
                memoryOwnershipCheckpoints,
                candidateVisibleMiB,
                totalVisibleMiB,
                candidateVisibleMiB < 350,
                candidateVisibleMiB < 500,
                totalVisibleMiB < 500,
                hiddenAfterUse.NormalizedCpuPercent < 0.5,
                "OverlayPlatformInterop ABI v1 (production GameInput Guide/controller/placement owner; no Avalonia-owned XInput reader)",
                "WidgetPresentationSession over the existing authenticated WidgetBridge transport",
                new[]
                {
                    "Physical Guide, controller feel, compositor transparency, and display clipping remain planner/user verdicts.",
                    "The retained transition samples inspect Avalonia visual-surface coverage at start/mid/end; they do not claim a physical compositor verdict.",
                    "The exact-commit run records the active monitor's real RenderScaling; 100/125/150-percent synthetic Windows-scale coverage is retained by focused headless tests.",
                    "GPU presentation cost remains unavailable without an authorized ETW/PresentMon capture lane.",
                    "Credential-gated Spotify and artwork/enrichment behavior remains dependent on the user's configured package state.",
                });

            var evidencePath = Path.GetFullPath(arguments.EvidencePath);
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(artifact, JsonOptions));
            await window.ShutdownAsync();
            desktop.Shutdown(allWidgetsPassed && responsivePassed && transitionPassed &&
                nodeKindCoverage.RetainedFinalVerificationPassed &&
                resourceOwnership.SupersededResourcesReleased && candidateVisibleMiB < 500 ? 0 : 1);
        }
        catch (Exception exception)
        {
            var failurePath = Path.GetFullPath(arguments.EvidencePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(failurePath)!);
            await File.WriteAllTextAsync(failurePath, JsonSerializer.Serialize(new
            {
                assignment = "AVP-004-INTEGRATION",
                sourceCommit = arguments.SourceCommit ?? "unavailable",
                errorType = exception.GetType().Name,
                error = exception.Message,
            }, JsonOptions));
            await window.ShutdownAsync();
            desktop.Shutdown(1);
        }
    }

    private static List<SemanticControlEvidence> CaptureSemanticControls(
        IntegratedShellView shell,
        ViewNode root)
    {
        var nodes = Flatten(root).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var shellBounds = new Rect(shell.Bounds.Size);
        return shell.GetVisualDescendants().OfType<Control>()
            .Where(control => control.IsEffectivelyVisible &&
                control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is { } id &&
                nodes.TryGetValue(id, out var node) &&
                node.Kind is ViewNodeKind.Text or ViewNodeKind.Button or ViewNodeKind.Slider or
                    ViewNodeKind.ActionSurface or ViewNodeKind.TextEntry)
            .Select(control =>
            {
                var nodeId = control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty)!;
                var origin = control.TranslatePoint(default, shell);
                var bounds = origin is null ? default : new Rect(origin.Value, control.Bounds.Size);
                var area = bounds.Width > 0 && bounds.Height > 0;
                var contained = area && shellBounds.Contains(bounds.TopLeft) && shellBounds.Contains(bounds.BottomRight);
                var hasScrollAncestor = control.GetVisualAncestors().OfType<ScrollViewer>().Any();
                var automationId = AutomationProperties.GetAutomationId(control);
                var automationName = AutomationProperties.GetName(control);
                return new SemanticControlEvidence(
                    nodeId,
                    control.GetType().Name,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    area,
                    contained,
                    area && !contained && hasScrollAncestor,
                    !string.IsNullOrWhiteSpace(automationId) && !string.IsNullOrWhiteSpace(automationName),
                    automationId ?? string.Empty);
            }).ToList();
    }

    private static Process[] FindCandidateProcesses(Process root, int? bridgeProcessId)
    {
        var ids = new HashSet<int> { root.Id };
        if (bridgeProcessId is { } bridge) ids.Add(bridge);
        var started = root.StartTime.ToUniversalTime() - TimeSpan.FromSeconds(1);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.StartTime.ToUniversalTime() >= started &&
                    (process.ProcessName.Contains("Widget", StringComparison.OrdinalIgnoreCase) ||
                     process.ProcessName.Contains("Spotify", StringComparison.OrdinalIgnoreCase)))
                    ids.Add(process.Id);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                if (!ids.Contains(process.Id)) process.Dispose();
            }
        }
        return ids.Select(id => id == root.Id ? root : Process.GetProcessById(id)).ToArray();
    }

    private static async Task<ResourceSample> SampleAsync(Process[] processes, TimeSpan duration)
    {
        var starts = new Dictionary<int, TimeSpan>();
        foreach (var process in processes)
        {
            try { process.Refresh(); starts[process.Id] = process.TotalProcessorTime; }
            catch (InvalidOperationException) { }
        }
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(duration);
        elapsed.Stop();
        var entries = new List<ProcessResource>();
        var cpu = TimeSpan.Zero;
        foreach (var process in processes)
        {
            try
            {
                process.Refresh();
                if (starts.TryGetValue(process.Id, out var start)) cpu += process.TotalProcessorTime - start;
                entries.Add(new ProcessResource(
                    process.ProcessName, process.Id, process.PrivateMemorySize64,
                    process.PrivateMemorySize64 / 1024d / 1024d));
            }
            catch (InvalidOperationException) { }
            if (process.Id != Environment.ProcessId) process.Dispose();
        }
        return new ResourceSample(
            elapsed.Elapsed.TotalMilliseconds,
            cpu.TotalMilliseconds / elapsed.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100,
            entries.Sum(entry => entry.PrivateMemoryBytes),
            entries);
    }

    private static MemoryOwnershipCheckpoint CaptureMemoryOwnership(
        string phase,
        Process process,
        MainWindow window,
        IntegratedShellView shell)
    {
        process.Refresh();
        var managedLiveBytes = GC.GetTotalMemory(forceFullCollection: false);
        var managedHeapSizeBytes = GC.GetGCMemoryInfo().HeapSizeBytes;
        var decodedArtworkBytes = shell.OwnedDecodedArtworkBytes;
        var skia = window.CaptureSkiaResourceCache();
        var renderTargetBytesEstimate = checked(
            (long)Math.Ceiling(window.Bounds.Width * window.RenderScaling) *
            (long)Math.Ceiling(window.Bounds.Height * window.RenderScaling) * 4);
        return new MemoryOwnershipCheckpoint(
            phase,
            process.PrivateMemorySize64,
            managedLiveBytes,
            managedHeapSizeBytes,
            shell.TrackedRenderCount,
            shell.OwnedArtworkBitmapCount,
            decodedArtworkBytes,
            shell.PendingArtworkRequestCount,
            skia.Available,
            skia.ResourceCount,
            skia.ResourceBytes,
            skia.UnavailableReason,
            renderTargetBytesEstimate,
            Math.Max(0, process.PrivateMemorySize64 - managedHeapSizeBytes - decodedArtworkBytes - skia.ResourceBytes));
    }

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout,
        string failure)
    {
        var deadline = Stopwatch.StartNew();
        while (!predicate())
        {
            if (deadline.Elapsed >= timeout) throw new TimeoutException(failure);
            await Task.Delay(25);
        }
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    private sealed record MeasurementArtifact(
        string Assignment,
        string SourceCommit,
        DateTime ProcessStartedAtUtc,
        string OperatingSystem,
        string DotNetRuntime,
        string ExecutableProductVersion,
        string AvaloniaVersion,
        string CommunityToolkitMvvmVersion,
        string ProcessArchitecture,
        string ExecutableSha256,
        double ColdStartToFirstCompleteFrameMilliseconds,
        bool NativeGameInputAvailable,
        bool NativeLegacyGuidePollingRequired,
        int InstalledWidgetCount,
        IReadOnlyList<WidgetEvidence> Widgets,
        bool AllInstalledWidgetsPassed,
        NodeKindCoverageEvidence NodeKindCoverage,
        IReadOnlyList<ResponsiveEvidence> ResponsiveFixtures,
        bool ResponsiveEvidencePassed,
        IReadOnlyList<IntegratedTransitionSample> TransitionSurfaceSamples,
        bool TransitionSurfaceDiagnosticsPassed,
        string PageTransition,
        ResourceSample VisibleIdleSample,
        ResourceSample HiddenAfterUseSample,
        ResourceOwnershipEvidence ResourceOwnership,
        IReadOnlyList<MemoryOwnershipCheckpoint> MemoryOwnershipCheckpoints,
        double VisibleCandidatePrivateMemoryMiB,
        double VisibleProcessTreePrivateMemoryMiB,
        bool CandidatePrivateMemoryUnder350MiB,
        bool CandidatePrivateMemoryUnder500MiB,
        bool ProcessTreePrivateMemoryUnder500MiB,
        bool HiddenRenderingEffectivelyIdle,
        string ControllerDependency,
        string SessionDependency,
        IReadOnlyList<string> UnavailableOrManualEvidence);

    private sealed record NodeKindCoverageEvidence(
        IReadOnlyList<ViewNodeKind> OrdinaryLifecycleObservedKinds,
        IReadOnlyList<ViewNodeKind> FocusedGenericMappingKinds,
        bool FocusedGenericMappingProofPassed,
        string FocusedGenericMappingTest,
        bool RetainedFinalVerificationPassed);

    private sealed record ResourceOwnershipEvidence(
        double CandidateProcessPrivateMemoryMiB,
        double BridgeWorkersAndProvidersPrivateMemoryMiB,
        int TrackedRenderCount,
        int OwnedArtworkBitmapCount,
        long OwnedDecodedArtworkBytes,
        int PendingArtworkRequestCount,
        bool SupersededResourcesReleased);

    private sealed record MemoryOwnershipCheckpoint(
        string Phase,
        long CandidatePrivateMemoryBytes,
        long ManagedLiveBytes,
        long ManagedHeapSizeBytes,
        int TrackedRenderCount,
        int OwnedArtworkBitmapCount,
        long OwnedDecodedArtworkBytes,
        int PendingArtworkRequestCount,
        bool SkiaResourceCacheAvailable,
        int SkiaResourceCount,
        long SkiaResourceBytes,
        string? SkiaResourceUnavailableReason,
        long CurrentRenderTargetBytesEstimate,
        long NativeSkiaAndOtherUnattributedBytes);

    private sealed record WidgetEvidence(
        string WidgetId,
        string Name,
        string RuntimeGeneration,
        string PresentationGeneration,
        long SessionGeneration,
        string WidgetInstanceId,
        long SnapshotSequence,
        string ActiveInputScopeId,
        int SemanticNodeCount,
        int FocusableNodeCount,
        IReadOnlyList<ViewNodeKind> NodeKinds,
        int MaximumRealizedSemanticControls,
        int VisibleRequiredSemanticControlCount,
        int StandardUiaIdentityCount,
        bool RequiredSemanticControlsPassed,
        string? AdvancedPresentationKind,
        string? AdvancedPresentationPreset,
        double SwitchToCompleteFrameMilliseconds,
        bool HasFailure,
        string Status);

    private sealed record ResponsiveEvidence(
        string WidgetId,
        double RequestedWidthDip,
        double RequestedHeightDip,
        double ActualRenderScaling,
        double ActualShellWidthDip,
        double ActualShellHeightDip,
        double BackingSurfaceWidthDip,
        double BackingSurfaceHeightDip,
        bool CompactBranch,
        int VisibleRequiredSemanticControls,
        int HonestlyScrollClippedControls,
        bool ReachableOrScrollClipped);

    private sealed record SemanticControlEvidence(
        string NodeId,
        string ControlType,
        double X,
        double Y,
        double Width,
        double Height,
        bool BoundsHaveArea,
        bool Contained,
        bool HonestlyScrollClipped,
        bool StandardUiaIdentity,
        string AutomationId);

    private sealed record ProcessResource(
        string Role,
        int ProcessId,
        long PrivateMemoryBytes,
        double PrivateMemoryMiB);

    private sealed record ResourceSample(
        double IntervalMilliseconds,
        double NormalizedCpuPercent,
        long TotalPrivateMemoryBytes,
        IReadOnlyList<ProcessResource> Processes);
}
