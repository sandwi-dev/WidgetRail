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
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.WidgetPresentationSession;
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
                    !shell.Coordinator.ViewModel.IsBusy,
                    TimeSpan.FromSeconds(20),
                    $"Widget '{widget.Id}' did not admit a complete frame.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                    $"widget:{widget.Id}:after-crossfade", process, window, shell));
                var admitted = await CaptureResponsiveFixtureAsync(
                    shell,
                    new Size(1440, 810),
                    widget.Id,
                    TimeSpan.FromSeconds(5));
                var frame = admitted.Frame;
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
                    var fixtureCapture = await CaptureResponsiveFixtureAsync(
                        shell,
                        fixture,
                        widget.Id,
                        TimeSpan.FromSeconds(5));
                    memoryOwnershipCheckpoints.Add(CaptureMemoryOwnership(
                        $"widget:{widget.Id}:logical-size:{fixture.Width}x{fixture.Height}", process, window, shell));
                    var fixtureFrame = fixtureCapture.Frame;
                    var controls = fixtureCapture.Controls;
                    var geometry = fixtureCapture.Geometry;
                    responsiveSamples.Add(new ResponsiveEvidence(
                        widget.Id,
                        fixtureFrame.Authority.SnapshotSequence,
                        fixtureFrame.Authority.ActiveInputScopeId,
                        fixture.Width,
                        fixture.Height,
                        window.RenderScaling,
                        shell.Bounds.Width,
                        shell.Bounds.Height,
                        window.Bounds.Width,
                        window.Bounds.Height,
                        fixtureCapture.Compact,
                        controls.Count,
                        controls.Count(control => control.HonestlyScrollClipped),
                        fixtureCapture.MissingExpectedIds,
                        fixtureCapture.UnreachableFocusableIds,
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
                        geometry.Passed));
                    if (fixture == new Size(978, 466))
                    {
                        offscreenCaptures.Add(await CaptureOffscreenAsync(
                            shell,
                            widget.Id,
                            widget.Name,
                            captureRoot,
                            window.RenderScaling,
                            offscreenCaptures.Count + 1));
                    }
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
            var responsivePassed = responsiveSamples.All(sample => sample.ReachableOrScrollClipped && sample.GeometryPassed);
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
                installedWidgetCount,
                widgetSamples,
                allWidgetsPassed,
                nodeKindCoverage,
                responsiveSamples,
                responsivePassed,
                offscreenCaptures,
                transitions,
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
            desktop.Shutdown(allWidgetsPassed && responsivePassed && transitionPassed &&
                nodeKindCoverage.RetainedFinalVerificationPassed &&
                resourceOwnership.SupersededResourcesReleased && candidateVisibleMiB < 500 &&
                shutdownEvidence.BoundedNormalShutdownPassed ? 0 : 1);
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

    internal static async Task<ResponsiveFixtureCapture> CaptureResponsiveFixtureAsync(
        IntegratedShellView shell,
        Size fixture,
        string expectedWidgetId,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
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

            var reachability = await ProbeFocusableReachabilityAsync(shell, seed);
            if (reachability is null)
            {
                await Task.Delay(20);
                continue;
            }

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
        var focusableIds = Flatten(seed.Frame.Snapshot.Root)
            .Where(node => node.IsFocusable && seed.ExpectedIds.Contains(node.Id))
            .Select(node => node.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (focusableIds.Length == 0) return new FocusableReachabilityResult([], []);

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
            foreach (var nodeId in focusableIds)
            {
                var control = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrent(seed, shell)) return null;
                    var target = seed.SemanticRoot.GetVisualDescendants().OfType<Control>()
                        .Prepend(seed.SemanticRoot)
                        .FirstOrDefault(candidate => string.Equals(
                            candidate.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty),
                            nodeId,
                            StringComparison.Ordinal));
                    if (target is null) return null;
                    target.Focus(Avalonia.Input.NavigationMethod.Directional);
                    target.BringIntoView();
                    return target;
                }, DispatcherPriority.Input);
                if (control is null)
                {
                    if (!await Dispatcher.UIThread.InvokeAsync(() => IsCurrent(seed, shell))) return null;
                    unreachable.Add(nodeId);
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                var evidence = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!IsCurrent(seed, shell)) return null;
                    seed.SemanticRoot.UpdateLayout();
                    var nodes = Flatten(seed.Frame.Snapshot.Root)
                        .ToDictionary(node => node.Id, StringComparer.Ordinal);
                    return CreateSemanticControlEvidence(shell, control, nodes);
                }, DispatcherPriority.Render);
                if (evidence is null) return null;
                revealed.Add(evidence);
                if (!evidence.BoundsHaveArea || !evidence.Contained ||
                    !evidence.StandardUiaIdentity || !evidence.EffectivelyVisible)
                    unreachable.Add(nodeId);
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var (scroller, offset) in restoration.Offsets) scroller.Offset = offset;
                restoration.FocusedControl?.Focus(Avalonia.Input.NavigationMethod.Unspecified);
            }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }

        var stillCurrent = await Dispatcher.UIThread.InvokeAsync(() => IsCurrent(seed, shell));
        return stillCurrent
            ? new FocusableReachabilityResult(
                revealed,
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
        var pageWidthUtilization = page.Width / Math.Max(1, shell.Bounds.Width);
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
        NodeKindCoverageEvidence NodeKindCoverage,
        IReadOnlyList<ResponsiveEvidence> ResponsiveFixtures,
        bool ResponsiveEvidencePassed,
        IReadOnlyList<OffscreenCaptureEvidence> OffscreenCaptures,
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
        CandidateShutdownEvidence ShutdownEvidence,
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
        long SnapshotSequence,
        string ActiveInputScopeId,
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
        IReadOnlyList<string> MissingExpectedIds,
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

    internal sealed record ResponsiveFixtureCapture(
        WidgetPresentationFrame Frame,
        Control SemanticRoot,
        bool Compact,
        IReadOnlyList<SemanticControlEvidence> Controls,
        IReadOnlySet<string> ExpectedIds,
        IReadOnlyList<string> MissingExpectedIds,
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
        IReadOnlyList<string> UnreachableFocusableIds);

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
