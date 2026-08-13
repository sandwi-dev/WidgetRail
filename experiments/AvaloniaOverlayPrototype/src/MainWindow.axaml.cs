using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Skia;
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
        Closing += OnClosing;
        Closed += (_, _) => lifecycle.Hide();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty) _ = ApplyVisibilityAsync(IsVisible);
        };
    }

    public IntegratedShellView? IntegratedShell => integratedShell;
    public int? BridgeProcessId => bridgeHost?.ProcessId;
    public bool NativeGameInputAvailable => platform?.HasGameInput == true;
    public bool NativeLegacyGuidePollingRequired => platform?.RequiresLegacyGuidePolling == true;
    public PrototypeLifecycle Lifecycle => lifecycle;
    internal CandidateShutdownEvidence? LastShutdownEvidence { get; private set; }

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
        await inputTrace.FlushAsync(PrototypeArguments.Current.SourceCommit ?? "working-tree-candidate");
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
            Content = integratedShell;

            var nativePath = Path.Combine(installation, "OverlayPlatformInterop.dll");
            platform = new OverlayPlatformClient(nativePath);
            platform.GuideToggleRequested += OnGuideToggleRequested;
            platform.InputReceived += OnPlatformInputReceived;
            platform.ControllerStateObserved += OnControllerStateObserved;
            if (TryGetPlatformHandle()?.Handle is { } handle && handle != 0)
                platform.Attach(handle);
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
        try { platform?.Tick(IsVisible && IsActive); }
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

    private void OnIntegratedFrameAdmitted(object? sender, GameBarAlternative.WidgetPresentationSession.WidgetPresentationFrame frame)
    {
        if (platform is null || Screens.ScreenFromWindow(this) is not { } screen) return;
        if (!string.Equals(platformWidgetId, frame.Authority.WidgetId, StringComparison.Ordinal))
        {
            platformWidgetId = frame.Authority.WidgetId;
            CancelHeldInput();
        }
        platform.SetWindowState(IsVisible, false);
        platform.SetWindowState(IsVisible, IsActive);
        var hints = frame.Snapshot.Surface;
        var desiredWidth = hints?.PreferredWidth ?? (hints?.Mode == WidgetSurfaceMode.Compact ? 420 : 978);
        var desiredHeight = hints?.PreferredHeight ?? (hints?.Mode == WidgetSurfaceMode.Compact ? 340 : 466);
        var scaling = RenderScaling <= 0 ? 1 : RenderScaling;
        var placement = platform.ComputePlacement(
            screen.WorkingArea,
            (uint)Math.Round(96 * scaling),
            desiredWidth,
            desiredHeight);
        if (placement is not { } value) return;
        LastComputedPlacement = value;
        if (!string.IsNullOrWhiteSpace(PrototypeArguments.Current.EvidencePath)) return;
        Position = value.Position;
        Width = value.Width / scaling;
        Height = value.Height / scaling;
    }

    internal PixelRect? LastComputedPlacement { get; private set; }

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
