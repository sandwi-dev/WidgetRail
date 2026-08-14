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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using GameBarAlternative.AvaloniaPrototype.Integration;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.WidgetPresentationSession;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype.Diagnostics;

internal static class EvidenceScenario
{
    private static readonly LogicalWorkAreaFixture[] ResponsiveWorkAreas =
    [
        new("compact", 420, 340),
        new("978", 978, 466),
        new("1180", 1180, 680),
        new("1440", 1440, 810),
    ];

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
            var evidenceAnchor = window.VisibleSessionAnchor ??
                throw new InvalidOperationException("The evidence window has no retained visible-session screen anchor.");
            var evidenceRenderScaling = evidenceAnchor.RenderScaling;
            var evidenceWorkingArea = evidenceAnchor.WorkArea;
            var firstCompleteFrameMilliseconds = (DateTime.UtcNow - startedAtUtc).TotalMilliseconds;

            var widgetSamples = new List<WidgetEvidence>();
            var responsiveSamples = new List<ResponsiveEvidence>();
            var envelopeSamples = new List<WidgetEnvelopeEvidence>();
            var offscreenCaptures = new List<OffscreenCaptureEvidence>();
            var captureRoot = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(arguments.EvidencePath))!,
                "offscreen-captures");
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
                    Equals(shell.EnvelopeAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                    window.LastEnvelopeResolution is not null &&
                    window.LastComputedPlacement is not null &&
                    !shell.Coordinator.ViewModel.IsBusy,
                    TimeSpan.FromSeconds(20),
                    $"Widget '{widget.Id}' did not admit a complete frame.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                if (!arguments.ReducedMotion)
                {
                    await Task.Delay(IntegratedShellView.ContentEnvelopeTransitionDuration +
                        TimeSpan.FromMilliseconds(40));
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    await WaitUntilAsync(() =>
                        Equals(shell.EnvelopeAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                        !shell.Coordinator.ViewModel.IsBusy,
                        TimeSpan.FromSeconds(5),
                        $"Widget '{widget.Id}' envelope transition did not settle on current authority.");
                }
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:envelope-settled", process, window, shell));
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:after-crossfade", process, window, shell));
                var admittedContent = shell.CurrentEnvelope.AdmittedContent;
                var admitted = await CaptureResponsiveFixtureAsync(
                    shell,
                    admittedContent,
                    widget.Id,
                    TimeSpan.FromSeconds(5),
                    phase => memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                        $"widget:{widget.Id}:admitted-envelope:{phase}", process, window, shell)),
                    applyFixture: false);
                var frame = admitted.Frame;
                var envelopeEvidence = CaptureEnvelopeEvidence(window, shell, frame);
                envelopeSamples.Add(envelopeEvidence);
                var nodes = Flatten(frame.Snapshot.Root).ToArray();
                foreach (var kind in nodes.Select(node => node.Kind)) allNodeKinds.Add(kind);
                var semanticControls = admitted.Controls;
                var requiredSemanticIds = admitted.ExpectedIds;
                var allRequiredObserved = requiredSemanticIds.All(requiredId =>
                    semanticControls.Any(control => string.Equals(control.NodeId, requiredId, StringComparison.Ordinal)));
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
                    allRequiredObserved && semanticControls.All(control => control.BoundsHaveArea &&
                        (control.Contained || control.HonestlyScrollClipped) && control.StandardUiaIdentity),
                    frame.Snapshot.AdvancedPresentation?.Kind.ToString(),
                    frame.Snapshot.AdvancedPresentation?.Preset.ToString(),
                    envelopeEvidence,
                    stopwatch.Elapsed.TotalMilliseconds,
                    shell.Coordinator.ViewModel.HasFailure,
                    shell.Coordinator.ViewModel.StatusText));

                foreach (var fixture in ResponsiveWorkAreas)
                {
                    var constraints = new WidgetEnvelopeConstraints(
                        CreateFixtureWorkArea(evidenceWorkingArea, fixture, evidenceRenderScaling),
                        evidenceRenderScaling,
                        AccessibilityScale: 1,
                        EnvelopeInsets.PlatformPlacement);
                    await Dispatcher.UIThread.InvokeAsync(
                        () => window.SetEvidenceEnvelopeConstraints(constraints),
                        DispatcherPriority.Normal);
                    await WaitForEnvelopeLayoutAsync(shell, widget.Id, TimeSpan.FromSeconds(2));
                    var responsive = await CaptureResponsiveFixtureAsync(
                        shell,
                        shell.CurrentEnvelope.AdmittedContent,
                        widget.Id,
                        TimeSpan.FromSeconds(5),
                        applyFixture: false);
                    var controls = responsive.Controls;
                    var geometry = responsive.Geometry;
                    var expectedWindow = shell.CurrentEnvelope.Window;
                    var actualRenderScaling = window.RenderScaling > 0 ? window.RenderScaling : 1;
                    var fixtureConstraintMatched = window.LastPlacementAnchor is
                        { EvidenceFixture: true } placementAnchor &&
                        placementAnchor.EffectiveWorkArea == constraints.WorkArea &&
                        Math.Abs(placementAnchor.EffectiveRenderScaling - constraints.RenderScaling) < 0.001 &&
                        Math.Abs(actualRenderScaling - constraints.RenderScaling) < 0.001 &&
                        Math.Abs(window.Bounds.Width - expectedWindow.Width) <= 0.75 &&
                        Math.Abs(window.Bounds.Height - expectedWindow.Height) <= 0.75;
                    responsiveSamples.Add(new ResponsiveEvidence(
                        widget.Id,
                        fixture.Name,
                        fixture.WidthDip,
                        fixture.HeightDip,
                        responsive.Frame.Authority.SnapshotSequence,
                        responsive.Frame.Authority.ActiveInputScopeId,
                        shell.CurrentEnvelope.AdmittedContent.Width,
                        shell.CurrentEnvelope.AdmittedContent.Height,
                        constraints.RenderScaling,
                        actualRenderScaling,
                        shell.Bounds.Width,
                        shell.Bounds.Height,
                        expectedWindow.Width,
                        expectedWindow.Height,
                        window.Bounds.Width,
                        window.Bounds.Height,
                        fixtureConstraintMatched,
                        responsive.Compact,
                        controls.Count,
                        controls.Count(control => control.HonestlyScrollClipped),
                        responsive.MissingExpectedIds,
                        responsive.ProbedFocusableIds,
                        responsive.UnreachableFocusableIds,
                        controls.All(control => control.BoundsHaveArea &&
                            (control.Contained || control.HonestlyScrollClipped) && control.StandardUiaIdentity),
                        geometry.PageHostWidthDip,
                        geometry.SemanticRootWidthDip,
                        geometry.PageWidthUtilization,
                        geometry.SemanticWidthUtilization,
                        geometry.MinimumReadableControlWidthDip,
                        geometry.MinimumReadableControlHeightDip,
                        geometry.ShellRegionsDoNotOverlap,
                        geometry.EffectiveVisibilityPassed,
                        geometry.MaximumHorizontalEmptyAreaRatio,
                        geometry.Passed && fixtureConstraintMatched));
                }
                await Dispatcher.UIThread.InvokeAsync(
                    () => window.SetEvidenceEnvelopeConstraints(null),
                    DispatcherPriority.Normal);
                await WaitForEnvelopeLayoutAsync(shell, widget.Id, TimeSpan.FromSeconds(2));
                offscreenCaptures.Add(await CaptureOffscreenAsync(
                    shell,
                    widget.Id,
                    widget.Name,
                    captureRoot,
                    evidenceRenderScaling,
                    offscreenCaptures.Count + 1));
                await WaitUntilAsync(
                    () => shell.PendingArtworkRequestCount == 0,
                    TimeSpan.FromSeconds(5),
                    $"Widget '{widget.Id}' artwork did not settle.");
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:artwork-settled", process, window, shell));
            }
            var compactTransitionWidgetId = envelopeSamples.First(sample =>
                string.Equals(sample.AuthoredMode, WidgetSurfaceMode.Compact.ToString(),
                    StringComparison.Ordinal)).WidgetId;
            var wideTransitionWidgetId = envelopeSamples.First(sample =>
                string.Equals(sample.AuthoredMode, WidgetSurfaceMode.Wide.ToString(),
                    StringComparison.Ordinal)).WidgetId;
            foreach (var transitionWidgetId in new[]
                     {
                         compactTransitionWidgetId,
                         wideTransitionWidgetId,
                         compactTransitionWidgetId,
                     })
            {
                await shell.Coordinator.SelectWidgetAsync(transitionWidgetId);
                await WaitUntilAsync(() =>
                    shell.Coordinator.CurrentFrame?.Authority.WidgetId == transitionWidgetId &&
                    Equals(shell.AdmittedAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                    Equals(shell.EnvelopeAuthority, shell.Coordinator.CurrentFrame?.Authority) &&
                    !shell.Coordinator.ViewModel.IsBusy,
                    TimeSpan.FromSeconds(20),
                    $"Directional envelope transition to '{transitionWidgetId}' did not settle.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                if (!arguments.ReducedMotion)
                    await Task.Delay(IntegratedShellView.ContentEnvelopeTransitionDuration +
                        TimeSpan.FromMilliseconds(40));
            }
            memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                "after-8-widget-authored-envelope-traversal", process, window, shell));

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
            var transitionChromePassed = transitions.Count > 0 &&
                transitions.All(sample => sample.AbsoluteGuideBounds is not null &&
                    sample.AbsoluteTrayBounds is not null) &&
                transitions.Select(sample => sample.AbsoluteGuideBounds).Distinct().Count() == 1 &&
                transitions.Select(sample => sample.AbsoluteTrayBounds).Distinct().Count() == 1;
            var completedTransitions = CompletedTransitions(transitions);
            var completedTransitionModes = completedTransitions
                .Select(group => group[0].ResponsiveMode)
                .ToArray();
            var bidirectionalEnvelopeTransitionChromePassed = transitionChromePassed &&
                completedTransitionModes.Zip(completedTransitionModes.Skip(1))
                    .Any(pair => pair.First == ContentResponsiveMode.Compact &&
                        pair.Second == ContentResponsiveMode.Wide) &&
                completedTransitionModes.Zip(completedTransitionModes.Skip(1))
                    .Any(pair => pair.First == ContentResponsiveMode.Wide &&
                        pair.Second == ContentResponsiveMode.Compact);
            var transitionPassed = bidirectionalEnvelopeTransitionChromePassed &&
                shell.Coordinator.ViewModel.Widgets.All(widget =>
                    completedTransitions.Any(group =>
                        string.Equals(group[0].WidgetId, widget.Id, StringComparison.Ordinal))) &&
                transitions.All(sample => sample.TransparentShellRoot &&
                    sample.OpaqueBlackFallbackAbsent && sample.AvaloniaSurfaceCoveragePresent &&
                    sample.VisualChildCount > 0);
            var distinctEnvelopeCount = envelopeSamples
                .Select(sample => $"{sample.AdmittedContent.Width:F1}x{sample.AdmittedContent.Height:F1}")
                .Distinct(StringComparer.Ordinal)
                .Count();
            var chromeBoundsInvariant = envelopeSamples.Count > 0 && envelopeSamples.All(sample =>
                sample.AbsoluteTrayBounds == envelopeSamples[0].AbsoluteTrayBounds &&
                sample.AbsoluteGuideBounds == envelopeSamples[0].AbsoluteGuideBounds);
            var widgetEnvelopeEvidencePassed = envelopeSamples.Count == shell.Coordinator.ViewModel.Widgets.Count &&
                distinctEnvelopeCount >= 4 && chromeBoundsInvariant &&
                envelopeSamples.All(sample => sample.AuthoredPairsAtomic && sample.WindowUnionContained &&
                    sample.ContentChromeDoNotOverlap && !sample.UsesFullWorkAreaBackdrop);
            var responsiveMatrixExpectedCount = shell.Coordinator.ViewModel.Widgets.Count * ResponsiveWorkAreas.Length;
            var responsiveMatrixPassed = responsiveSamples.Count == responsiveMatrixExpectedCount &&
                shell.Coordinator.ViewModel.Widgets.All(widget =>
                    responsiveSamples.Where(sample => sample.WidgetId == widget.Id)
                        .Select(sample => sample.FixtureName)
                        .Order(StringComparer.Ordinal)
                        .SequenceEqual(ResponsiveWorkAreas.Select(fixture => fixture.Name)
                            .Order(StringComparer.Ordinal))) &&
                responsiveSamples.All(sample => sample.ReachableOrScrollClipped &&
                    sample.FixtureConstraintMatched && sample.GeometryPassed);
            var responsivePassed = responsiveMatrixPassed;
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

            var installedWidgetCount = shell.Coordinator.ViewModel.Widgets.Count;
            var pageTransition = shell.TransitionPresenter.PageTransition?.GetType().Name ??
                (arguments.ReducedMotion ? "ReducedMotion" : "unavailable");
            await window.ShutdownAsync();
            var shutdownEvidence = window.LastShutdownEvidence ?? new CandidateShutdownEvidence(
                false,
                false,
                new Integration.ProcessTreeShutdownEvidence([], [], false, false, 0),
                0);
            var inputTraceFlush = window.LastInputTraceFlushResult ?? new InputTraceFlushResult(
                false,
                0,
                0,
                "Shutdown did not retain an input-trace flush result.");

            var artifact = new MeasurementArtifact(
                "AVP-004-REDESIGN",
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
                installedWidgetCount,
                widgetSamples,
                allWidgetsPassed,
                envelopeSamples,
                distinctEnvelopeCount,
                chromeBoundsInvariant,
                widgetEnvelopeEvidencePassed,
                window.PlacementAnchorHistory,
                nodeKindCoverage,
                responsiveSamples,
                responsiveMatrixExpectedCount,
                responsiveMatrixPassed,
                responsivePassed,
                offscreenCaptures,
                transitions,
                transitionChromePassed,
                bidirectionalEnvelopeTransitionChromePassed,
                transitionPassed,
                pageTransition,
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
                shutdownEvidence,
                new InputTracePersistenceEvidence(
                    inputTraceFlush.Succeeded,
                    inputTraceFlush.RequestedSequence,
                    inputTraceFlush.PersistedSequence,
                    inputTraceFlush.Failure),
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
            desktop.Shutdown(allWidgetsPassed && widgetEnvelopeEvidencePassed && responsivePassed && transitionPassed &&
                nodeKindCoverage.RetainedFinalVerificationPassed &&
                resourceOwnership.SupersededResourcesReleased && candidateVisibleMiB < 500 &&
                shutdownEvidence.BoundedNormalShutdownPassed && inputTraceFlush.Succeeded ? 0 : 1);
        }
        catch (Exception exception)
        {
            var failurePath = Path.GetFullPath(arguments.EvidencePath!);
            Directory.CreateDirectory(Path.GetDirectoryName(failurePath)!);
            await File.WriteAllTextAsync(failurePath, JsonSerializer.Serialize(new
            {
                assignment = "AVP-004-REDESIGN",
                sourceCommit = arguments.SourceCommit ?? "unavailable",
                errorType = exception.GetType().Name,
                error = exception.Message,
            }, JsonOptions));
            await window.ShutdownAsync();
            desktop.Shutdown(1);
        }
    }

    private static PixelRect CreateFixtureWorkArea(
        PixelRect actualWorkArea,
        LogicalWorkAreaFixture fixture,
        double renderScaling)
    {
        var width = (int)Math.Round(
            fixture.WidthDip * renderScaling,
            MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(
            fixture.HeightDip * renderScaling,
            MidpointRounding.AwayFromZero);
        return new PixelRect(
            actualWorkArea.X + ((actualWorkArea.Width - width) / 2),
            actualWorkArea.Bottom - height,
            width,
            height);
    }

    private static IReadOnlyList<IntegratedTransitionSample[]> CompletedTransitions(
        IReadOnlyList<IntegratedTransitionSample> samples)
    {
        var phases = Enum.GetValues<Navigation.TransitionPhase>();
        var completed = new List<IntegratedTransitionSample[]>();
        for (var index = 0; index <= samples.Count - phases.Length;)
        {
            var candidate = samples.Skip(index).Take(phases.Length).ToArray();
            if (candidate.Select(sample => sample.Phase).SequenceEqual(phases) &&
                candidate.All(sample => string.Equals(
                    sample.WidgetId, candidate[0].WidgetId, StringComparison.Ordinal) &&
                    sample.SnapshotSequence == candidate[0].SnapshotSequence))
            {
                completed.Add(candidate);
                index += phases.Length;
            }
            else
            {
                index++;
            }
        }
        return completed;
    }

    private static async Task WaitForEnvelopeLayoutAsync(
        IntegratedShellView shell,
        string expectedWidgetId,
        TimeSpan timeout)
    {
        var settlement = IntegratedShellView.ContentEnvelopeTransitionDuration +
            TimeSpan.FromMilliseconds(80);
        if (settlement > timeout)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        await Task.Delay(settlement);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        if (!string.Equals(shell.AdmittedWidgetId, expectedWidgetId, StringComparison.Ordinal) ||
            !Equals(shell.AdmittedAuthority, shell.Coordinator.CurrentFrame?.Authority))
            throw new InvalidOperationException(
                $"Widget '{expectedWidgetId}' lost admitted authority while its envelope settled.");
    }

    internal static async Task<ResponsiveFixtureCapture> CaptureResponsiveFixtureAsync(
        IntegratedShellView shell,
        Size fixture,
        string expectedWidgetId,
        TimeSpan timeout,
        Action<string>? recordProbeResidency = null,
        bool applyFixture = true)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (applyFixture)
                await Dispatcher.UIThread.InvokeAsync(
                    () => shell.SetEvidenceViewport(fixture),
                    DispatcherPriority.Normal);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var seed = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var frame = shell.Coordinator.CurrentFrame;
                var authorityBefore = shell.AdmittedAuthority;
                var semanticRoot = shell.ActiveSemanticRoot;
                if (frame is null || authorityBefore is null || semanticRoot is null ||
                    !string.Equals(frame.Authority.WidgetId, expectedWidgetId, StringComparison.Ordinal) ||
                    !Equals(authorityBefore, frame.Authority))
                    return null;

                semanticRoot.UpdateLayout();
                var controls = CaptureSemanticControls(
                    shell, semanticRoot, frame.Snapshot.Root, shell.IsCompact);
                var expectedIds = ExpectedNonVirtualizedRequiredIds(frame.Snapshot.Root, shell.IsCompact);
                return !Equals(authorityBefore, shell.AdmittedAuthority) ||
                    !Equals(frame.Authority, shell.Coordinator.CurrentFrame?.Authority) ||
                    !ReferenceEquals(semanticRoot, shell.ActiveSemanticRoot)
                    ? null
                    : new ResponsiveFixtureSeed(
                        frame, semanticRoot, shell.IsCompact, controls, expectedIds);
            }, DispatcherPriority.Render);
            if (seed is null)
            {
                await Task.Delay(20);
                continue;
            }

            if (recordProbeResidency is not null)
                await Dispatcher.UIThread.InvokeAsync(
                    () => recordProbeResidency("before-focus-reachability-probe"),
                    DispatcherPriority.Render);
            var reachability = await ProbeFocusableReachabilityAsync(shell, seed);
            if (recordProbeResidency is not null)
                await Dispatcher.UIThread.InvokeAsync(
                    () => recordProbeResidency("after-focus-reachability-probe"),
                    DispatcherPriority.Render);
            if (reachability is null)
            {
                await Task.Delay(20);
                continue;
            }

            if (applyFixture)
                await Dispatcher.UIThread.InvokeAsync(
                    () => shell.SetEvidenceViewport(fixture),
                    DispatcherPriority.Normal);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var capture = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!Equals(seed.Frame.Authority, shell.AdmittedAuthority) ||
                    !Equals(seed.Frame.Authority, shell.Coordinator.CurrentFrame?.Authority) ||
                    !ReferenceEquals(seed.SemanticRoot, shell.ActiveSemanticRoot))
                    return null;

                var controls = CaptureSemanticControls(
                        shell, seed.SemanticRoot, seed.Frame.Snapshot.Root, seed.Compact)
                    .Concat(seed.Controls)
                    .Concat(reachability.RevealedControls)
                    .GroupBy(control => control.NodeId, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(control => control.Contained).First())
                    .ToList();
                var missingExpectedIds = seed.ExpectedIds
                    .Where(expectedId => controls.All(control =>
                        !string.Equals(control.NodeId, expectedId, StringComparison.Ordinal)))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                var geometry = CaptureGeometry(
                    shell,
                    controls,
                    seed.ExpectedIds,
                    reachability.UnreachableFocusableIds.Count == 0);
                return new ResponsiveFixtureCapture(
                    seed.Frame,
                    seed.SemanticRoot,
                    seed.Compact,
                    controls,
                    seed.ExpectedIds,
                    missingExpectedIds,
                    reachability.ProbedFocusableIds,
                    reachability.UnreachableFocusableIds,
                    geometry);
            }, DispatcherPriority.Render);
            if (capture is not null) return capture;
            await Task.Delay(20);
        }

        throw new TimeoutException(
            $"Widget '{expectedWidgetId}' did not retain one admitted authority/root while sampling {fixture}.");
    }

    private static async Task<FocusableReachabilityResult?> ProbeFocusableReachabilityAsync(
        IntegratedShellView shell,
        ResponsiveFixtureSeed seed)
    {
        var focusableNodes = Flatten(seed.Frame.Snapshot.Root)
            .Where(node => node.IsFocusable && seed.ExpectedIds.Contains(node.Id))
            .Where(node => seed.Controls.All(control =>
                !string.Equals(control.NodeId, node.Id, StringComparison.Ordinal) || !control.Contained))
            .DistinctBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        if (focusableNodes.Length == 0) return new FocusableReachabilityResult([], [], []);

        var restoration = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsCurrent(seed, shell)) return null;
            var page = shell.ActivePage;
            if (page is null) return null;
            var offsets = page.GetVisualDescendants().OfType<ScrollViewer>()
                .Prepend(page as ScrollViewer)
                .OfType<ScrollViewer>()
                .Distinct()
                .Select(scroller => (Scroller: scroller, scroller.Offset))
                .ToArray();
            return new FocusScrollRestoration(
                TopLevel.GetTopLevel(shell)?.FocusManager?.GetFocusedElement() as Control,
                offsets);
        }, DispatcherPriority.Render);
        if (restoration is null) return null;

        var revealed = new List<SemanticControlEvidence>();
        var unreachable = new List<string>();
        try
        {
            foreach (var node in focusableNodes)
            {
                var target = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrent(seed, shell)) return null;
                    var control = seed.SemanticRoot.GetVisualDescendants().OfType<Control>()
                        .Prepend(seed.SemanticRoot)
                        .FirstOrDefault(candidate => string.Equals(
                            candidate.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty),
                            node.Id,
                            StringComparison.Ordinal));
                    var semanticListMatch = shell.ActivePage?.GetVisualDescendants().OfType<ListBox>()
                        .Select(candidate => new
                        {
                            List = candidate,
                            Item = candidate.ItemsSource?.OfType<ViewNode>().FirstOrDefault(item =>
                                ReferenceEquals(item, node) || Flatten(item).Any(child => ReferenceEquals(child, node))),
                        })
                        .FirstOrDefault(match => match.Item is not null);
                    if (semanticListMatch?.Item is { } semanticListItem)
                    {
                        semanticListMatch.List.ScrollIntoView(semanticListItem);
                        semanticListMatch.List.UpdateLayout();
                        control = seed.SemanticRoot.GetVisualDescendants().OfType<Control>()
                            .Prepend(seed.SemanticRoot)
                            .FirstOrDefault(candidate => string.Equals(
                                candidate.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty),
                                node.Id,
                                StringComparison.Ordinal));
                    }
                    if (control is null) return null;
                    var focusAccepted = node.IsDisabled is true ||
                        control.Focus(Avalonia.Input.NavigationMethod.Directional);
                    var current = TopLevel.GetTopLevel(shell)?.FocusManager?.GetFocusedElement() as Control;
                    var focusOwned = node.IsDisabled is true || focusAccepted && ReferenceEquals(current, control) &&
                        string.Equals(
                            current.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty),
                            node.Id,
                            StringComparison.Ordinal);
                    control.BringIntoView();
                    return new FocusableProbeTarget(control, node.IsDisabled is true, focusOwned);
                }, DispatcherPriority.Input);
                if (target is null)
                {
                    if (!await Dispatcher.UIThread.InvokeAsync(() => IsCurrent(seed, shell))) return null;
                    unreachable.Add(node.Id);
                    continue;
                }

                var observation = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrent(seed, shell)) return null;
                    // BringIntoView and focus ownership are layout concerns. Updating the admitted
                    // root here avoids rasterizing a transient compositor surface for every
                    // below-fold target; the coherent fixture capture still receives its ordinary
                    // render turn after the probe and restoration complete.
                    seed.SemanticRoot.UpdateLayout();
                    var nodes = Flatten(seed.Frame.Snapshot.Root)
                        .ToDictionary(node => node.Id, StringComparer.Ordinal);
                    var current = TopLevel.GetTopLevel(shell)?.FocusManager?.GetFocusedElement() as Control;
                    var focusStillOwned = target.Disabled || target.FocusOwned &&
                        ReferenceEquals(current, target.Control) &&
                        string.Equals(
                            current.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty),
                            node.Id,
                            StringComparison.Ordinal);
                    return new FocusableProbeObservation(
                        CreateSemanticControlEvidence(shell, target.Control, nodes),
                        target.Control.IsEffectivelyVisible,
                        focusStillOwned);
                }, DispatcherPriority.Render);
                if (observation is null) return null;
                revealed.Add(observation.Evidence);
                if (!observation.Evidence.BoundsHaveArea || !observation.Evidence.Contained ||
                    !observation.Evidence.StandardUiaIdentity || !observation.ActuallyVisible ||
                    !observation.FocusOwned)
                    unreachable.Add(node.Id);
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var (scroller, offset) in restoration.Offsets) scroller.Offset = offset;
                restoration.FocusedControl?.Focus(Avalonia.Input.NavigationMethod.Unspecified);
                seed.SemanticRoot.UpdateLayout();
            }, DispatcherPriority.Input);
        }

        var stillCurrent = await Dispatcher.UIThread.InvokeAsync(() => IsCurrent(seed, shell));
        return stillCurrent
            ? new FocusableReachabilityResult(
                revealed,
                focusableNodes.Select(node => node.Id).ToArray(),
                unreachable.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())
            : null;
    }

    private static bool IsCurrent(ResponsiveFixtureSeed seed, IntegratedShellView shell) =>
        Equals(seed.Frame.Authority, shell.AdmittedAuthority) &&
        Equals(seed.Frame.Authority, shell.Coordinator.CurrentFrame?.Authority) &&
        ReferenceEquals(seed.SemanticRoot, shell.ActiveSemanticRoot);

    private static List<SemanticControlEvidence> CaptureSemanticControls(
        IntegratedShellView shell,
        Control semanticRoot,
        ViewNode root,
        bool compact)
    {
        var nodes = Flatten(root).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var declaredVisible = DeclaredVisibleRequiredIds(root, compact);
        return semanticRoot.GetVisualDescendants().OfType<Control>().Prepend(semanticRoot)
            .Where(control => control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty) is { } id &&
                nodes.TryGetValue(id, out var node) &&
                declaredVisible.Contains(id) &&
                node.Kind is ViewNodeKind.Text or ViewNodeKind.Button or ViewNodeKind.Slider or
                    ViewNodeKind.ActionSurface or ViewNodeKind.TextEntry)
            .Select(control => CreateSemanticControlEvidence(shell, control, nodes))
            .Where(control => control.EffectivelyVisible)
            .ToList();
    }

    private static SemanticControlEvidence CreateSemanticControlEvidence(
        IntegratedShellView shell,
        Control control,
        IReadOnlyDictionary<string, ViewNode> nodes)
    {
        var nodeId = control.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty)!;
        var origin = control.TranslatePoint(default, shell);
        var bounds = origin is null ? default : new Rect(origin.Value, control.Bounds.Size);
        var area = bounds.Width > 0 && bounds.Height > 0;
        var shellBounds = new Rect(shell.Bounds.Size);
        var contained = area && shellBounds.Contains(bounds.TopLeft) && shellBounds.Contains(bounds.BottomRight);
        var hasScrollAncestor = control.GetVisualAncestors().OfType<ScrollViewer>().Any();
        var effectivelyReachable = control.IsEffectivelyVisible || area && !contained && hasScrollAncestor;
        var automationId = AutomationProperties.GetAutomationId(control);
        var automationName = AutomationProperties.GetName(control);
        var node = nodes[nodeId];
        var readable = node.Kind == ViewNodeKind.Text
            ? bounds.Height >= 14 && bounds.Width >= ((node.Text?.Length ?? 0) >= 20 ? 120 : 8)
            : bounds.Width >= 44 && bounds.Height >= 36;
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
            automationId ?? string.Empty,
            effectivelyReachable,
            readable);
    }

    private static IReadOnlySet<string> DeclaredVisibleRequiredIds(ViewNode root, bool compact)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Add(root, true);
        return ids;

        void Add(ViewNode node, bool ancestorVisible)
        {
            var visible = ancestorVisible && node.VisibleWhen switch
            {
                ResponsiveVisibility.CompactOnly => compact,
                ResponsiveVisibility.ExpandedOnly => !compact,
                _ => true,
            };
            if (!visible) return;
            if (node.Kind is ViewNodeKind.Text or ViewNodeKind.Button or ViewNodeKind.Slider or
                ViewNodeKind.ActionSurface or ViewNodeKind.TextEntry)
                ids.Add(node.Id);
            foreach (var child in node.Children) Add(child, visible);
        }
    }

    private static GeometryEvidence CaptureGeometry(
        IntegratedShellView shell,
        IReadOnlyList<SemanticControlEvidence> controls,
        IReadOnlySet<string> expectedIds,
        bool focusableReachabilityPassed)
    {
        var page = BoundsInShell(shell.PageHostElement, shell);
        var guide = BoundsInShell(shell.ControllerGuideElement, shell);
        var tray = BoundsInShell(shell.TrayElement, shell);
        var semantic = shell.ActiveSemanticRoot ?? throw new InvalidOperationException("No admitted semantic root.");
        var semanticBounds = BoundsInShell(semantic, shell);
        var availableSemanticWidth = Math.Max(1, page.Width - shell.PageHostElement.Padding.Left -
            shell.PageHostElement.Padding.Right - shell.PageHostElement.BorderThickness.Left -
            shell.PageHostElement.BorderThickness.Right);
        var pageWidthUtilization = page.Width /
            Math.Max(1, shell.CurrentEnvelope.AdmittedContent.Width);
        var semanticWidthUtilization = semanticBounds.Width / availableSemanticWidth;
        var maximumHorizontalEmptyAreaRatio = 1 - Math.Min(1, semanticWidthUtilization);
        var onScreenControls = controls.Where(control => control.Contained).ToArray();
        var minimumWidth = onScreenControls.Length == 0 ? 0 : onScreenControls.Min(control => control.Width);
        var minimumHeight = onScreenControls.Length == 0 ? 0 : onScreenControls.Min(control => control.Height);
        var readable = onScreenControls.All(control => control.Readable);
        var regionsDoNotOverlap = !Overlaps(page, guide) && !Overlaps(page, tray) && !Overlaps(guide, tray);
        var allExpectedObserved = expectedIds.All(expectedId =>
            controls.Any(control => string.Equals(control.NodeId, expectedId, StringComparison.Ordinal)));
        var effectiveVisibility = semantic.IsEffectivelyVisible && allExpectedObserved && focusableReachabilityPassed &&
            controls.All(control => control.EffectivelyVisible);
        var passed = pageWidthUtilization >= 0.88 && semanticWidthUtilization >= 0.92 &&
            maximumHorizontalEmptyAreaRatio <= 0.08 && readable && regionsDoNotOverlap && effectiveVisibility;
        return new GeometryEvidence(
            page.Width,
            semanticBounds.Width,
            pageWidthUtilization,
            semanticWidthUtilization,
            minimumWidth,
            minimumHeight,
            regionsDoNotOverlap,
            effectiveVisibility,
            maximumHorizontalEmptyAreaRatio,
            passed);
    }

    internal static IReadOnlySet<string> ExpectedNonVirtualizedRequiredIds(ViewNode root, bool compact)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Add(root, ancestorVisible: true, virtualizedAncestor: false);
        return ids;

        void Add(ViewNode node, bool ancestorVisible, bool virtualizedAncestor)
        {
            var visible = ancestorVisible && node.VisibleWhen switch
            {
                ResponsiveVisibility.CompactOnly => compact,
                ResponsiveVisibility.ExpandedOnly => !compact,
                _ => true,
            };
            if (!visible) return;
            if (!virtualizedAncestor && node.Kind is ViewNodeKind.Text or ViewNodeKind.Button or
                ViewNodeKind.Slider or ViewNodeKind.ActionSurface or ViewNodeKind.TextEntry)
                ids.Add(node.Id);
            var virtualized = virtualizedAncestor ||
                node.Kind == ViewNodeKind.Scroll ||
                node.Kind == ViewNodeKind.Grid && node.Children.Count > 64;
            foreach (var child in node.Children) Add(child, visible, virtualized);
        }
    }

    private static Rect BoundsInShell(Control control, IntegratedShellView shell)
    {
        var origin = control.TranslatePoint(default, shell) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    private static bool Overlaps(Rect left, Rect right) =>
        left.Left < right.Right && left.Right > right.Left &&
        left.Top < right.Bottom && left.Bottom > right.Top;

    private static WidgetEnvelopeEvidence CaptureEnvelopeEvidence(
        MainWindow window,
        IntegratedShellView shell,
        WidgetPresentationFrame frame)
    {
        if (!Equals(frame.Authority, shell.EnvelopeAuthority) ||
            window.LastEnvelopeResolution is not { } envelope ||
            window.LastComputedPlacement is not { } hwnd ||
            window.LastPlacementAnchor is not { EvidenceFixture: false } placementAnchor)
            throw new InvalidOperationException(
                $"Widget '{frame.Authority.WidgetId}' has no exact admitted envelope/window authority.");
        shell.UpdateLayout();
        var content = BoundsInShell(shell.PageHostElement, shell);
        var guide = BoundsInShell(shell.ControllerGuideElement, shell);
        var tray = BoundsInShell(shell.TrayElement, shell);
        var shellBounds = new Rect(shell.Bounds.Size);
        var pairAtomic = (frame.Snapshot.Surface?.PreferredWidth is null) ==
                (frame.Snapshot.Surface?.PreferredHeight is null) &&
            (frame.Snapshot.Surface?.MinimumWidth is null) ==
                (frame.Snapshot.Surface?.MinimumHeight is null);
        var contained = Contains(shellBounds, content) && Contains(shellBounds, guide) &&
            Contains(shellBounds, tray) &&
            Math.Abs(hwnd.Width - envelope.Window.Width * placementAnchor.EffectiveRenderScaling) <= 2 &&
            Math.Abs(hwnd.Height - envelope.Window.Height * placementAnchor.EffectiveRenderScaling) <= 2;
        var noOverlap = !Overlaps(content, guide) && !Overlaps(content, tray) && !Overlaps(guide, tray);
        return new WidgetEnvelopeEvidence(
            frame.Authority.WidgetId,
            frame.Authority.SnapshotSequence,
            frame.Snapshot.Surface?.Mode.ToString() ?? WidgetSurfaceMode.Adaptive.ToString(),
            frame.Snapshot.Surface?.PreferredWidth,
            frame.Snapshot.Surface?.PreferredHeight,
            frame.Snapshot.Surface?.MinimumWidth,
            frame.Snapshot.Surface?.MinimumHeight,
            pairAtomic,
            new LogicalSizeEvidence(envelope.AuthoredPreferredContent.Width, envelope.AuthoredPreferredContent.Height),
            new LogicalSizeEvidence(envelope.AuthoredMinimumContent.Width, envelope.AuthoredMinimumContent.Height),
            new LogicalSizeEvidence(envelope.AdmittedContent.Width, envelope.AdmittedContent.Height),
            new LogicalBoundsEvidence(content.X, content.Y, content.Width, content.Height),
            new PixelBoundsEvidence(hwnd.X, hwnd.Y, hwnd.Width, hwnd.Height),
            ToAbsolutePixelBounds(guide, hwnd, placementAnchor.EffectiveRenderScaling),
            ToAbsolutePixelBounds(tray, hwnd, placementAnchor.EffectiveRenderScaling),
            placementAnchor.ScreenIdentity,
            new PixelBoundsEvidence(
                placementAnchor.ScreenBounds.X,
                placementAnchor.ScreenBounds.Y,
                placementAnchor.ScreenBounds.Width,
                placementAnchor.ScreenBounds.Height),
            new PixelBoundsEvidence(
                placementAnchor.AnchoredWorkArea.X,
                placementAnchor.AnchoredWorkArea.Y,
                placementAnchor.AnchoredWorkArea.Width,
                placementAnchor.AnchoredWorkArea.Height),
            placementAnchor.AnchorRenderScaling,
            placementAnchor.AnchorRevision,
            placementAnchor.AnchorChangeReason,
            envelope.PreferredPairAdmitted,
            envelope.MinimumPairSatisfied,
            envelope.WorkAreaClamped,
            contained,
            noOverlap,
            envelope.UsesFullWorkAreaBackdrop);
    }

    private static PixelBoundsEvidence ToAbsolutePixelBounds(Rect local, PixelRect hwnd, double scaling)
    {
        var effectiveScaling = scaling > 0 ? scaling : 1;
        return new PixelBoundsEvidence(
            hwnd.X + (int)Math.Round(local.X * effectiveScaling, MidpointRounding.AwayFromZero),
            hwnd.Y + (int)Math.Round(local.Y * effectiveScaling, MidpointRounding.AwayFromZero),
            (int)Math.Round(local.Width * effectiveScaling, MidpointRounding.AwayFromZero),
            (int)Math.Round(local.Height * effectiveScaling, MidpointRounding.AwayFromZero));
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 0.5 && inner.Top >= outer.Top - 0.5 &&
        inner.Right <= outer.Right + 0.5 && inner.Bottom <= outer.Bottom + 0.5;

    private static async Task<OffscreenCaptureEvidence> CaptureOffscreenAsync(
        IntegratedShellView shell,
        string widgetId,
        string widgetName,
        string outputRoot,
        double renderScaling,
        int ordinal)
    {
        Directory.CreateDirectory(outputRoot);
        var scaling = renderScaling > 0 ? renderScaling : 1;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(shell.Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Ceiling(shell.Bounds.Height * scaling)));
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(shell);
        var safeId = string.Concat(widgetId.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        var fileName = $"{ordinal:D2}-{safeId}.png";
        var path = Path.Combine(outputRoot, fileName);
        await using (var stream = File.Create(path)) bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        await using var hashStream = File.OpenRead(path);
        var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));
        return new OffscreenCaptureEvidence(
            widgetId,
            widgetName,
            fileName,
            shell.Bounds.Width,
            shell.Bounds.Height,
            scaling,
            pixelSize.Width,
            pixelSize.Height,
            sha256);
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
        IReadOnlyList<WidgetEnvelopeEvidence> WidgetEnvelopes,
        int MateriallyDistinctAdmittedEnvelopeCount,
        bool AbsoluteChromeBoundsInvariant,
        bool WidgetEnvelopeEvidencePassed,
        IReadOnlyList<ScreenAnchorPlacementEvidence> PlacementAnchors,
        NodeKindCoverageEvidence NodeKindCoverage,
        IReadOnlyList<ResponsiveEvidence> ResponsiveFixtures,
        int ResponsiveMatrixExpectedCount,
        bool ResponsiveMatrixPassed,
        bool ResponsiveEvidencePassed,
        IReadOnlyList<OffscreenCaptureEvidence> OffscreenCaptures,
        IReadOnlyList<IntegratedTransitionSample> TransitionSurfaceSamples,
        bool TransitionAbsoluteChromeBoundsInvariant,
        bool BidirectionalEnvelopeTransitionChromePassed,
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
        CandidateShutdownEvidence ShutdownEvidence,
        InputTracePersistenceEvidence InputTracePersistence,
        string ControllerDependency,
        string SessionDependency,
        IReadOnlyList<string> UnavailableOrManualEvidence);

    private sealed record InputTracePersistenceEvidence(
        bool Succeeded,
        long RequestedSequence,
        long PersistedSequence,
        string? Failure);

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
        WidgetEnvelopeEvidence Envelope,
        double SwitchToCompleteFrameMilliseconds,
        bool HasFailure,
        string Status);

    private sealed record WidgetEnvelopeEvidence(
        string WidgetId,
        long SnapshotSequence,
        string AuthoredMode,
        double? AuthoredPreferredWidth,
        double? AuthoredPreferredHeight,
        double? AuthoredMinimumWidth,
        double? AuthoredMinimumHeight,
        bool AuthoredPairsAtomic,
        LogicalSizeEvidence ResolvedAuthoredPreferred,
        LogicalSizeEvidence ResolvedAuthoredMinimum,
        LogicalSizeEvidence AdmittedContent,
        LogicalBoundsEvidence AdmittedContentBounds,
        PixelBoundsEvidence HwndBounds,
        PixelBoundsEvidence AbsoluteGuideBounds,
        PixelBoundsEvidence AbsoluteTrayBounds,
        string ScreenIdentity,
        PixelBoundsEvidence ScreenBounds,
        PixelBoundsEvidence ScreenWorkArea,
        double ScreenRenderScaling,
        long ScreenAnchorRevision,
        ScreenAnchorChangeReason ScreenAnchorChangeReason,
        bool PreferredPairAdmitted,
        bool MinimumPairSatisfied,
        bool WorkAreaClamped,
        bool WindowUnionContained,
        bool ContentChromeDoNotOverlap,
        bool UsesFullWorkAreaBackdrop);

    private sealed record LogicalSizeEvidence(double Width, double Height);

    private sealed record LogicalBoundsEvidence(double X, double Y, double Width, double Height);

    private sealed record PixelBoundsEvidence(int X, int Y, int Width, int Height);

    private sealed record ResponsiveEvidence(
        string WidgetId,
        string FixtureName,
        double LogicalWorkAreaWidthDip,
        double LogicalWorkAreaHeightDip,
        long SnapshotSequence,
        string ActiveInputScopeId,
        double AdmittedContentWidthDip,
        double AdmittedContentHeightDip,
        double DeclaredRenderScaling,
        double ActualRenderScaling,
        double ActualShellWidthDip,
        double ActualShellHeightDip,
        double ExpectedHwndWidthDip,
        double ExpectedHwndHeightDip,
        double ActualHwndWidthDip,
        double ActualHwndHeightDip,
        bool FixtureConstraintMatched,
        bool CompactBranch,
        int VisibleRequiredSemanticControls,
        int HonestlyScrollClippedControls,
        IReadOnlyList<string> MissingExpectedIds,
        IReadOnlyList<string> ProbedFocusableIds,
        IReadOnlyList<string> UnreachableFocusableIds,
        bool ReachableOrScrollClipped,
        double PageHostWidthDip,
        double SemanticRootWidthDip,
        double PageWidthUtilization,
        double SemanticWidthUtilization,
        double MinimumReadableControlWidthDip,
        double MinimumReadableControlHeightDip,
        bool ShellRegionsDoNotOverlap,
        bool EffectiveVisibilityPassed,
        double MaximumHorizontalEmptyAreaRatio,
        bool GeometryPassed);

    private sealed record LogicalWorkAreaFixture(string Name, double WidthDip, double HeightDip);

    internal sealed record ResponsiveFixtureCapture(
        WidgetPresentationFrame Frame,
        Control SemanticRoot,
        bool Compact,
        IReadOnlyList<SemanticControlEvidence> Controls,
        IReadOnlySet<string> ExpectedIds,
        IReadOnlyList<string> MissingExpectedIds,
        IReadOnlyList<string> ProbedFocusableIds,
        IReadOnlyList<string> UnreachableFocusableIds,
        GeometryEvidence Geometry);

    private sealed record ResponsiveFixtureSeed(
        WidgetPresentationFrame Frame,
        Control SemanticRoot,
        bool Compact,
        IReadOnlyList<SemanticControlEvidence> Controls,
        IReadOnlySet<string> ExpectedIds);

    private sealed record FocusScrollRestoration(
        Control? FocusedControl,
        IReadOnlyList<(ScrollViewer Scroller, Vector Offset)> Offsets);

    private sealed record FocusableReachabilityResult(
        IReadOnlyList<SemanticControlEvidence> RevealedControls,
        IReadOnlyList<string> ProbedFocusableIds,
        IReadOnlyList<string> UnreachableFocusableIds);

    private sealed record FocusableProbeTarget(
        Control Control,
        bool Disabled,
        bool FocusOwned);

    private sealed record FocusableProbeObservation(
        SemanticControlEvidence Evidence,
        bool ActuallyVisible,
        bool FocusOwned);

    internal sealed record GeometryEvidence(
        double PageHostWidthDip,
        double SemanticRootWidthDip,
        double PageWidthUtilization,
        double SemanticWidthUtilization,
        double MinimumReadableControlWidthDip,
        double MinimumReadableControlHeightDip,
        bool ShellRegionsDoNotOverlap,
        bool EffectiveVisibilityPassed,
        double MaximumHorizontalEmptyAreaRatio,
        bool Passed);

    private sealed record OffscreenCaptureEvidence(
        string WidgetId,
        string WidgetName,
        string FileName,
        double LogicalWidthDip,
        double LogicalHeightDip,
        double RenderScaling,
        int PixelWidth,
        int PixelHeight,
        string Sha256);

    internal sealed record SemanticControlEvidence(
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
        string AutomationId,
        bool EffectivelyVisible,
        bool Readable);

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
