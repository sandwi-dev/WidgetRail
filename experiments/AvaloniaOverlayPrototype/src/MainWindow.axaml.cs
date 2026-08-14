using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Skia;
using Avalonia.VisualTree;
using GameBarAlternative.AvaloniaPrototype.Input;
using GameBarAlternative.AvaloniaPrototype.Diagnostics;
using GameBarAlternative.AvaloniaPrototype.Integration;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.AvaloniaPrototype.Platform;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Views;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.AvaloniaPrototype;

public sealed partial class MainWindow : Window
{
    private readonly PrototypeLifecycle lifecycle = new();
    private readonly VisibleSessionScreenAnchor visibleSessionScreenAnchor = new();
    private readonly List<ScreenAnchorPlacementEvidence> placementAnchorHistory = [];
    private Screens? subscribedScreens;
    private readonly DispatcherTimer platformTimer;
    private readonly InputTraceRecorder inputTrace = new(
        PrototypeArguments.Current.InputTracePath,
        PrototypeArguments.Current.SourceCommit);
    private BridgeProcessHost? bridgeHost;
    private IOverlayPlatformClient? platform;
    private IntegratedShellView? integratedShell;
    private CancellationTokenSource? yHold;
    private bool yHoldCompleted;
    private bool keyboardEnterHeld;
    private bool integrationStarted;
    private bool closing;
    private bool shutdownRequested;
    private bool shutdownComplete;
    private string? platformWidgetId;
    private PixelRect? stableWorkArea;
    private double stableRenderScaling;
    private GameBarAlternative.WidgetPresentationSession.WidgetPresentationAuthority? stableEnvelopeAuthority;
    private WidgetEnvelopeConstraints? evidenceEnvelopeConstraints;
    private bool applyingShellPlacement;

    public MainWindow()
    {
        InitializeComponent();
        Content = new Border
        {
            Padding = new Avalonia.Thickness(28),
            Child = new TextBlock { Text = "Connecting to the retained widget runtime…", FontSize = 20 },
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        platformTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(25), DispatcherPriority.Input, OnPlatformTick);
        Opened += OnOpened;
        Activated += (_, _) =>
        {
            inputTrace.Record("window-activated", IsVisible, true, FocusedSemanticId());
            SetInputActive(IsVisible);
            integratedShell?.EnsureManagedFocus();
        };
        Deactivated += (_, _) =>
        {
            inputTrace.Record("window-deactivated", IsVisible, false, FocusedSemanticId());
            SetInputActive(false);
        };
        ScalingChanged += OnScalingChanged;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            if (subscribedScreens is not null)
            {
                subscribedScreens.Changed -= OnScreensChanged;
                subscribedScreens = null;
            }
            lifecycle.Hide();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty) _ = ApplyVisibilityAsync(IsVisible);
        };
        PositionChanged += (_, _) => ApplyStableShellPlacement(placementReason: "position-changed");
    }

    public IntegratedShellView? IntegratedShell => integratedShell;
    public int? BridgeProcessId => bridgeHost?.ProcessId;
    public bool NativeGameInputAvailable => platform?.HasGameInput == true;
    public bool NativeLegacyGuidePollingRequired => platform?.RequiresLegacyGuidePolling == true;
    public PrototypeLifecycle Lifecycle => lifecycle;
    internal CandidateShutdownEvidence? LastShutdownEvidence { get; private set; }
    internal InputTraceFlushResult? LastInputTraceFlushResult { get; private set; }
    internal ScreenAnchorSnapshot? VisibleSessionAnchor => visibleSessionScreenAnchor.Current;
    internal ScreenAnchorPlacementEvidence? LastPlacementAnchor { get; private set; }
    internal IReadOnlyList<ScreenAnchorPlacementEvidence> PlacementAnchorHistory => placementAnchorHistory;

    internal SkiaResourceCacheSnapshot CaptureSkiaResourceCache()
    {
        try
        {
            var platformImpl = PlatformImpl;
            if (platformImpl is null) return new(false, 0, 0, "Top-level platform unavailable");
            var feature = platformImpl.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature))
                as ISkiaSharpApiLeaseFeature;
            if (feature is null) return new(false, 0, 0, "Skia lease feature unavailable");
            using var lease = feature.Lease();
            if (lease.GrContext is null) return new(false, 0, 0, "GPU GRContext unavailable");
            lease.GrContext.GetResourceCacheUsage(out var resourceCount, out var resourceBytes);
            return new(true, resourceCount, resourceBytes, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(false, 0, 0, exception.GetType().Name);
        }
    }

    public async Task ShutdownAsync()
    {
        if (shutdownComplete) return;
        var shutdownClock = System.Diagnostics.Stopwatch.StartNew();
        shutdownRequested = true;
        closing = true;
        platformTimer.Stop();
        CancelHeldInput();
        bridgeHost?.CaptureOwnedProcessTree();
        if (platform is not null)
        {
            platform.GuideToggleRequested -= OnGuideToggleRequested;
            platform.InputReceived -= OnPlatformInputReceived;
            platform.ControllerStateObserved -= OnControllerStateObserved;
            platform.Dispose();
            platform = null;
        }
        Task? shellShutdown = null;
        var shellShutdownCompletedNormally = true;
        if (integratedShell is not null)
        {
            integratedShell.FrameAdmitted -= OnIntegratedFrameAdmitted;
            integratedShell.EnvelopeTransitionStarting -= OnEnvelopeTransitionStarting;
            integratedShell.AbsoluteChromeBoundsProvider = null;
            shellShutdown = integratedShell.DisposeAsync().AsTask();
            if (await Task.WhenAny(shellShutdown, Task.Delay(TimeSpan.FromSeconds(3))) == shellShutdown)
                await shellShutdown;
            else
            {
                shellShutdownCompletedNormally = false;
                inputTrace.Record("shell-shutdown-timeout", IsVisible, IsActive, detail: "3 second graceful deadline");
            }
            integratedShell = null;
        }
        ProcessTreeShutdownEvidence processTree = new([], [], true, false, 0);
        if (bridgeHost is not null)
        {
            processTree = shellShutdown is null
                ? await DisposeBridgeAndCaptureAsync(bridgeHost)
                : await bridgeHost.StopProcessAsync();
            bridgeHost = null;
        }
        if (shellShutdown is { IsCompleted: false })
        {
            try { await shellShutdown.WaitAsync(TimeSpan.FromSeconds(1)); }
            catch (TimeoutException) { }
        }
        lifecycle.Hide();
        shutdownClock.Stop();
        LastShutdownEvidence = new CandidateShutdownEvidence(
            shellShutdownCompletedNormally && processTree.BoundedNormalShutdownPassed,
            shellShutdownCompletedNormally,
            processTree,
            shutdownClock.Elapsed.TotalMilliseconds);
        inputTrace.Record("shutdown-complete", false, false, detail:
            $"normal={LastShutdownEvidence.BoundedNormalShutdownPassed};" +
            $"forced={processTree.ForcedTerminationUsed};remaining={processTree.RemainingProcessIds.Count};" +
            $"milliseconds={shutdownClock.Elapsed.TotalMilliseconds:F0}");
        LastInputTraceFlushResult = await inputTrace.FlushAsync(
            PrototypeArguments.Current.SourceCommit ?? "working-tree-candidate");
        shutdownComplete = true;
        Close();
    }

    private async void OnOpened(object? sender, EventArgs args)
    {
        if (integrationStarted) return;
        integrationStarted = true;
        lifecycle.Show();
        try
        {
            var installation = ResolveInstallationRoot(PrototypeArguments.Current.InstallationPath);
            bridgeHost = await BridgeProcessHost.StartAsync(installation);
            var coordinator = new WidgetIntegrationCoordinator(bridgeHost.Session, new AvaloniaUiScheduler());
            integratedShell = new IntegratedShellView(coordinator, PrototypeArguments.Current.ReducedMotion);
            integratedShell.FrameAdmitted += OnIntegratedFrameAdmitted;
            integratedShell.EnvelopeTransitionStarting += OnEnvelopeTransitionStarting;
            integratedShell.AbsoluteChromeBoundsProvider = CaptureAbsoluteChromeBounds;
            Content = integratedShell;

            var nativePath = Path.Combine(installation, "OverlayPlatformInterop.dll");
            platform = new OverlayPlatformClient(nativePath);
            platform.GuideToggleRequested += OnGuideToggleRequested;
            platform.InputReceived += OnPlatformInputReceived;
            platform.ControllerStateObserved += OnControllerStateObserved;
            if (TryGetPlatformHandle()?.Handle is { } handle && handle != 0)
                platform.Attach(handle);
            EnsureVisibleSessionScreenAnchor();
            subscribedScreens = Screens;
            subscribedScreens.Changed += OnScreensChanged;
            UpdateShellEnvelopeConstraints();
            ApplyStableShellPlacement(force: true, placementReason: "initial-visible-session");
            platform.SetWindowState(IsVisible, IsActive);
            inputTrace.Record("native-platform-ready", IsVisible, IsActive, detail:
                $"gameInput={platform.HasGameInput};legacyGuide={platform.RequiresLegacyGuidePolling}");
            platformTimer.Start();
            await integratedShell.InitializeAsync();
            integratedShell.EnsureManagedFocus();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Content = new Border
            {
                Margin = new Avalonia.Thickness(24),
                Padding = new Avalonia.Thickness(24),
                CornerRadius = new Avalonia.CornerRadius(16),
                Background = Avalonia.Media.Brush.Parse("#F02B2430"),
                Child = new TextBlock
                {
                    Text = $"Avalonia migration candidate could not start\n\n{exception.Message}\n\nBuild src/OverlayHost Release, then launch with --installation <Release path>.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
            };
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (keyboardEnterHeld)
            {
                e.Handled = true;
                return;
            }
            keyboardEnterHeld = true;
        }

        var semantic = e.Key switch
        {
            Key.Up => SemanticInput.Up,
            Key.Down => SemanticInput.Down,
            Key.Left => SemanticInput.Left,
            Key.Right => SemanticInput.Right,
            Key.Enter => SemanticInput.Activate,
            Key.Escape or Key.B => SemanticInput.Back,
            _ => (SemanticInput?)null,
        };
        if (semantic is not { } input) return;
        var focused = FocusManager?.GetFocusedElement() as Control;
        var handled = integratedShell?.RouteKeyboard(input, focused) == true;
        if (!handled && integratedShell is not null && input == SemanticInput.Back)
        {
            Hide();
            handled = true;
        }
        if (handled) e.Handled = true;
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        keyboardEnterHeld = false;
        e.Handled = true;
    }

    private void OnPlatformTick(object? sender, EventArgs args)
    {
        try
        {
            platform?.Tick(IsVisible && IsActive);
            ApplyStableShellPlacement(placementReason: "platform-timer");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            inputTrace.Record("native-tick-failed", IsVisible, IsActive, FocusedSemanticId(),
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnGuideToggleRequested(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(() =>
        {
            inputTrace.Record("guide-toggle", IsVisible, IsActive, FocusedSemanticId());
            if (IsVisible)
            {
                Hide();
                return;
            }
            Show();
            Activate();
            integratedShell?.EnsureManagedFocus();
        }, DispatcherPriority.Input);

    private void OnControllerStateObserved(object? sender, OverlayControllerObservation observation) =>
        inputTrace.Record(
            "native-controller-state",
            IsVisible,
            IsActive,
            FocusedSemanticId(),
            $"connected={observation.Connected};primed={observation.Primed};" +
            $"readPath={observation.ReadPath};foregroundExclusive={observation.ForegroundExclusive}");

    private void OnPlatformInputReceived(object? sender, OverlayPlatformInput input) =>
        Dispatcher.UIThread.Post(async () =>
        {
            var shell = integratedShell;
            if (!CanRouteNativeInput(IsVisible, shell is not null))
            {
                inputTrace.Record("native-input-rejected", IsVisible, IsActive, FocusedSemanticId(),
                    IsVisible ? "shell-unavailable" : "overlay-hidden", false);
                return;
            }
            if (!input.Connected)
            {
                CancelHeldInput();
                inputTrace.Record("native-controller-disconnected", IsVisible, IsActive, FocusedSemanticId());
                return;
            }
            shell!.EnsureManagedFocus();
            var focused = FocusManager?.GetFocusedElement() as Control;
            if (input.Navigation is { } navigation)
            {
                var navigationHandled = shell.RouteKeyboard(navigation, focused);
                inputTrace.Record("native-input-routed", IsVisible, IsActive, FocusedSemanticId(),
                    $"navigation={navigation};phase={input.Phase}", navigationHandled);
                return;
            }
            if (input.Button is not { } button) return;
            if (button == ControllerButton.Y)
            {
                var yHandled = await HandleYAsync(input.Phase, focused, shell);
                inputTrace.Record("native-input-routed", IsVisible, IsActive, FocusedSemanticId(),
                    $"button={button};phase={input.Phase}", yHandled);
                return;
            }
            var handled = await shell.RouteControllerButtonAsync(button, input.Phase, focused);
            if (!handled && button == ControllerButton.B && input.Phase == ControllerEventPhase.Pressed)
            {
                Hide();
                handled = true;
            }
            inputTrace.Record("native-input-routed", IsVisible, IsActive, FocusedSemanticId(),
                $"button={button};phase={input.Phase}", handled);
        }, DispatcherPriority.Input);

    internal async Task<bool> HandleYAsync(
        ControllerEventPhase phase,
        Control? focused,
        IntegratedShellView? shell = null)
    {
        shell ??= integratedShell;
        if (shell is null) return false;
        var trayFocused = focused is not null && shell.TrayButtons.Contains(focused);
        if (!trayFocused)
            return await shell.RouteControllerButtonAsync(ControllerButton.Y, phase, focused);
        if (phase == ControllerEventPhase.Pressed)
        {
            yHold?.Cancel();
            yHold?.Dispose();
            yHold = new CancellationTokenSource();
            yHoldCompleted = false;
            var token = yHold.Token;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(700), token);
                yHoldCompleted = true;
                await shell.Coordinator.RestartActiveAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return true;
        }
        if (phase != ControllerEventPhase.Released) return false;
        yHold?.Cancel();
        yHold?.Dispose();
        yHold = null;
        if (yHoldCompleted) return true;
        if (await shell.Coordinator.InvokeQuickActionForButtonAsync(ControllerButton.Y)) return true;
        return await shell.RouteControllerButtonAsync(ControllerButton.Y, phase, focused);
    }

    private async Task ApplyVisibilityAsync(bool visible)
    {
        if (closing) return;
        if (visible) lifecycle.Show(); else lifecycle.Hide();
        inputTrace.Record(visible ? "window-shown" : "window-hidden", visible, IsActive, FocusedSemanticId());
        CancelHeldInput();
        platform?.SetWindowState(visible, visible && IsActive);
        if (integratedShell is not null)
        {
            try { await integratedShell.Coordinator.SetVisibleAsync(visible); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            if (visible) integratedShell.EnsureManagedFocus();
        }
    }

    private void SetInputActive(bool active)
    {
        platform?.SetWindowState(IsVisible, active);
        if (!active) CancelHeldInput();
    }

    private void CancelHeldInput()
    {
        keyboardEnterHeld = false;
        yHold?.Cancel();
        yHold?.Dispose();
        yHold = null;
        yHoldCompleted = false;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (shutdownComplete) return;
        args.Cancel = true;
        if (!shutdownRequested)
        {
            shutdownRequested = true;
            _ = ShutdownApplicationAsync();
        }
    }

    private async Task ShutdownApplicationAsync()
    {
        await ShutdownAsync();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown(LastShutdownEvidence?.BoundedNormalShutdownPassed == true ? 0 : 1);
    }

    private bool EnsureVisibleSessionScreenAnchor()
    {
        if (visibleSessionScreenAnchor.Current is not null) return true;
        var selected = Screens.ScreenFromWindow(this) ?? Screens.Primary ?? Screens.All.FirstOrDefault();
        if (selected is null) return false;
        var anchor = visibleSessionScreenAnchor.Initialize(ScreenAnchorDescriptor.FromScreen(selected));
        inputTrace.Record("screen-anchor-initialized", IsVisible, IsActive, detail: FormatAnchor(anchor));
        return true;
    }

    private void OnScreensChanged(object? sender, EventArgs args) =>
        ReconcileExplicitScreenAnchorChange(ScreenAnchorChangeReason.DisplayTopologyChanged);

    private void OnScalingChanged(object? sender, EventArgs args) =>
        ReconcileExplicitScreenAnchorChange(ScreenAnchorChangeReason.DpiChanged);

    private void ReconcileExplicitScreenAnchorChange(ScreenAnchorChangeReason reason)
    {
        if (!integrationStarted || closing || evidenceEnvelopeConstraints is not null ||
            !EnsureVisibleSessionScreenAnchor()) return;
        var before = visibleSessionScreenAnchor.Current!;
        var available = Screens.All.Select(ScreenAnchorDescriptor.FromScreen).ToArray();
        var preferred = Screens.ScreenFromWindow(this);
        var after = visibleSessionScreenAnchor.ReconcileExplicitChange(
            available,
            preferred is null ? null : ScreenAnchorDescriptor.FromScreen(preferred).Identity,
            reason);
        if (after.Revision == before.Revision) return;

        inputTrace.Record("screen-anchor-changed", IsVisible, IsActive, detail:
            $"reason={after.ChangeReason};old={FormatAnchor(before)};new={FormatAnchor(after)}");
        stableWorkArea = null;
        stableRenderScaling = 0;
        stableEnvelopeAuthority = null;
        UpdateShellEnvelopeConstraints();
        ApplyStableShellPlacement(force: true, placementReason: $"anchor-change:{after.ChangeReason}");
    }

    private static string FormatAnchor(ScreenAnchorSnapshot anchor) =>
        $"identity={anchor.Identity};bounds={anchor.Bounds.X},{anchor.Bounds.Y}," +
        $"{anchor.Bounds.Width},{anchor.Bounds.Height};workArea={anchor.WorkArea.X}," +
        $"{anchor.WorkArea.Y},{anchor.WorkArea.Width},{anchor.WorkArea.Height};" +
        $"scaling={anchor.RenderScaling:F3};revision={anchor.Revision}";

    private void OnIntegratedFrameAdmitted(object? sender, GameBarAlternative.WidgetPresentationSession.WidgetPresentationFrame frame)
    {
        if (platform is null) return;
        if (!string.Equals(platformWidgetId, frame.Authority.WidgetId, StringComparison.Ordinal))
        {
            platformWidgetId = frame.Authority.WidgetId;
            CancelHeldInput();
        }
        platform.SetWindowState(IsVisible, false);
        platform.SetWindowState(IsVisible, IsActive);
    }

    private void OnEnvelopeTransitionStarting(object? sender, WidgetEnvelopeTransitionRequest request)
    {
        if (platform is null || !EnsureVisibleSessionScreenAnchor()) return;
        ApplyWindowPlacement(
            request.Envelope,
            request.Authority,
            ResolveEnvelopeConstraints(),
            force: true,
            placementReason: "widget-envelope-transition");
    }

    private void UpdateShellEnvelopeConstraints()
    {
        if (integratedShell is null || !EnsureVisibleSessionScreenAnchor()) return;
        integratedShell.SetHostEnvelopeConstraints(ResolveEnvelopeConstraints());
    }

    private void ApplyStableShellPlacement(bool force = false, string placementReason = "routine-revalidation")
    {
        if (applyingShellPlacement || platform is null || integratedShell is null ||
            !EnsureVisibleSessionScreenAnchor() ||
            integratedShell.EnvelopeAuthority is not { } authority)
            return;
        var constraints = ResolveEnvelopeConstraints();
        if (!force && stableWorkArea == constraints.WorkArea &&
            Math.Abs(stableRenderScaling - constraints.RenderScaling) < 0.001 &&
            Equals(stableEnvelopeAuthority, authority) &&
            LastComputedPlacement is not null) return;
        var envelope = integratedShell.ResolveEnvelope(constraints);
        integratedShell.SetHostEnvelopeConstraints(constraints);
        ApplyWindowPlacement(envelope, authority, constraints, force, placementReason);
    }

    private void ApplyWindowPlacement(
        WidgetEnvelopeResolution envelope,
        GameBarAlternative.WidgetPresentationSession.WidgetPresentationAuthority authority,
        WidgetEnvelopeConstraints constraints,
        bool force,
        string placementReason)
    {
        if (platform is null || visibleSessionScreenAnchor.Current is not { } screenAnchor) return;
        if (!force && stableWorkArea == constraints.WorkArea &&
            Math.Abs(stableRenderScaling - constraints.RenderScaling) < 0.001 &&
            Equals(stableEnvelopeAuthority, authority) &&
            LastComputedPlacement is not null) return;
        var placement = platform.ComputePlacement(
            constraints.WorkArea,
            (uint)Math.Round(96 * constraints.RenderScaling),
            envelope.Window.Width,
            envelope.Window.Height);
        if (placement is not { } value) return;
        stableWorkArea = constraints.WorkArea;
        stableRenderScaling = constraints.RenderScaling;
        stableEnvelopeAuthority = authority;
        applyingShellPlacement = true;
        try
        {
            platform.ApplyPlacement(value);
            LastComputedPlacement = value;
            LastEnvelopeResolution = envelope;
            LastPlacementAnchor = new ScreenAnchorPlacementEvidence(
                authority.WidgetId,
                authority.SnapshotSequence,
                screenAnchor.Identity,
                screenAnchor.DisplayName,
                screenAnchor.Bounds,
                screenAnchor.WorkArea,
                screenAnchor.RenderScaling,
                screenAnchor.Revision,
                screenAnchor.ChangeReason,
                constraints.WorkArea,
                constraints.RenderScaling,
                evidenceEnvelopeConstraints is not null,
                placementReason);
            placementAnchorHistory.Add(LastPlacementAnchor);
        }
        finally { applyingShellPlacement = false; }
    }

    private WidgetEnvelopeConstraints ResolveEnvelopeConstraints()
    {
        if (evidenceEnvelopeConstraints is { } evidence) return evidence;
        return visibleSessionScreenAnchor.RetainForPlacement().ToEnvelopeConstraints();
    }

    internal void SetEvidenceEnvelopeConstraints(WidgetEnvelopeConstraints? constraints)
    {
        evidenceEnvelopeConstraints = constraints;
        if (platform is null || integratedShell is null || !EnsureVisibleSessionScreenAnchor() ||
            integratedShell.EnvelopeAuthority is not { } authority)
            return;
        var effective = ResolveEnvelopeConstraints();
        var envelope = integratedShell.ResolveEnvelope(effective);
        integratedShell.SetHostEnvelopeConstraints(effective);
        ApplyWindowPlacement(
            envelope,
            authority,
            effective,
            force: true,
            placementReason: constraints is null ? "evidence-fixture-restored" : "evidence-fixture");
    }

    private IntegratedAbsoluteChromeBounds? CaptureAbsoluteChromeBounds()
    {
        if (integratedShell is null || LastComputedPlacement is not { } windowBounds) return null;
        var guide = BoundsInShell(integratedShell.ControllerGuideElement, integratedShell);
        var tray = BoundsInShell(integratedShell.TrayElement, integratedShell);
        var scaling = LastPlacementAnchor?.EffectiveRenderScaling ??
            visibleSessionScreenAnchor.Current?.RenderScaling ?? 1;
        return new IntegratedAbsoluteChromeBounds(
            ToAbsolutePixelBounds(guide, windowBounds, scaling),
            ToAbsolutePixelBounds(tray, windowBounds, scaling));
    }

    private static Rect BoundsInShell(Control control, IntegratedShellView shell)
    {
        var origin = control.TranslatePoint(default, shell) ?? default;
        return new Rect(origin, control.Bounds.Size);
    }

    private static PixelRect ToAbsolutePixelBounds(Rect local, PixelRect window, double scaling)
    {
        var scale = scaling > 0 ? scaling : 1;
        return new PixelRect(
            window.X + (int)Math.Round(local.X * scale, MidpointRounding.AwayFromZero),
            window.Y + (int)Math.Round(local.Y * scale, MidpointRounding.AwayFromZero),
            (int)Math.Round(local.Width * scale, MidpointRounding.AwayFromZero),
            (int)Math.Round(local.Height * scale, MidpointRounding.AwayFromZero));
    }

    internal PixelRect? LastComputedPlacement { get; private set; }
    internal WidgetEnvelopeResolution? LastEnvelopeResolution { get; private set; }
    internal static WidgetEnvelopeResolution ResolveWidgetEnvelope(
        WidgetSurfaceHints? hints,
        PixelRect workArea,
        double scaling,
        double accessibilityScale = 1) =>
        WidgetEnvelopeResolver.Resolve(
            hints,
            new WidgetEnvelopeConstraints(
                workArea,
                scaling,
                accessibilityScale,
                EnvelopeInsets.PlatformPlacement));

    private static string ResolveInstallationRoot(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(requested);
        foreach (var seed in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var current = new DirectoryInfo(seed);
            for (var depth = 0; current is not null && depth < 8; depth++, current = current.Parent)
            {
                var productOutput = Path.Combine(current.FullName, "src", "OverlayHost", "out", "Release");
                if (File.Exists(Path.Combine(productOutput, "widget-catalog.json"))) return productOutput;
                if (File.Exists(Path.Combine(current.FullName, "widget-catalog.json")) &&
                    File.Exists(Path.Combine(current.FullName, "runtime", "Bridge", "WidgetBridge.exe")))
                    return current.FullName;
            }
        }
        throw new DirectoryNotFoundException("Could not locate a packaged OverlayHost Release installation.");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private string? FocusedSemanticId() =>
        (FocusManager?.GetFocusedElement() as Control)?.GetValue(SemanticTreeRenderer.SemanticNodeIdProperty);

    internal static bool CanRouteNativeInput(bool visible, bool shellAvailable) =>
        visible && shellAvailable;

    private static async Task<ProcessTreeShutdownEvidence> DisposeBridgeAndCaptureAsync(BridgeProcessHost host)
    {
        var dispose = host.DisposeAsync().AsTask();
        if (await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(7))) == dispose)
        {
            await dispose;
            return host.LastShutdownEvidence ?? new ProcessTreeShutdownEvidence([], [], true, false, 0);
        }
        return await host.StopProcessAsync();
    }
}

internal sealed record SkiaResourceCacheSnapshot(
    bool Available,
    int ResourceCount,
    long ResourceBytes,
    string? UnavailableReason);

internal sealed record CandidateShutdownEvidence(
    bool BoundedNormalShutdownPassed,
    bool ShellShutdownCompletedNormally,
    ProcessTreeShutdownEvidence ProcessTree,
    double TotalDurationMilliseconds);

internal sealed record ScreenAnchorPlacementEvidence(
    string WidgetId,
    long SnapshotSequence,
    string ScreenIdentity,
    string? ScreenDisplayName,
    PixelRect ScreenBounds,
    PixelRect AnchoredWorkArea,
    double AnchorRenderScaling,
    long AnchorRevision,
    ScreenAnchorChangeReason AnchorChangeReason,
    PixelRect EffectiveWorkArea,
    double EffectiveRenderScaling,
    bool EvidenceFixture,
    string PlacementReason);
