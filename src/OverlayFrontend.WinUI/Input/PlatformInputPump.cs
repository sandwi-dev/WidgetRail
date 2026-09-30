using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Input polling only; WinUI owns frame scheduling and navigation.</summary>
internal sealed partial class PlatformInputPump : IDisposable, IPlatformDispatcher
{
    private readonly DispatcherQueue dispatcher;
    private readonly DispatcherQueueTimer navigationTimer;
    private readonly DispatcherQueueTimer guideTimer;
    private readonly nint ownedWindow;
    private readonly IOverlayPlatformNative nativeBackend;
    private readonly ControllerInputOwnership inputOwnership = new();
    private readonly ControllerHandoffRelease handoffRelease = new();
    internal Task<bool> WaitForHandoffReleaseAsync() => handoffRelease.Begin();
    internal void CancelHandoffRelease() => handoffRelease.Cancel();
    private readonly PlatformInputDiagnostics diagnostics = new(PlatformInputDiagnostics.DefaultPath);
    private OverlayPlatformSession? session;
    private volatile bool closed;
    private bool visible;
    private bool viewMenuShortcut;
    private bool shortcutConsumed;
    private int navigationTraceRemaining = Environment.GetCommandLineArgs().Contains("--trace-controller-input") ? 256 : 0;
    internal void TraceInput(string message) => diagnostics.Write(message);
    public event Action<ControllerFrame>? FrameReceived;
    public event Action? ToggleRequested;
    internal event Action<nint>? ExternalForegroundObserved;
    public event Action<Exception>? Failed;
    internal bool IsForeground => IsForegroundProcess();
    internal bool IsActive => !closed && visible && session is not null;
    internal nint PlacementWindow
    {
        get
        {
            var remembered = session?.RememberedForegroundTarget ?? 0;
            return (nint)(session?.ResolveForegroundTarget((nuint)ownedWindow, remembered != 0 && IsWindow((nint)remembered) != 0) ?? (nuint)ownedWindow);
        }
    }

    public PlatformInputPump(DispatcherQueue dispatcher, nint hwnd, IOverlayPlatformNative? backend = null)
    {
        this.dispatcher = dispatcher;
        ownedWindow = hwnd;
        nativeBackend = backend ?? new OverlayPlatformNative();
        diagnostics.Write($"Input owner created pid={Environment.ProcessId} hwnd={hwnd} backend={backend?.GetType().Name ?? nameof(OverlayPlatformNative)}");
        navigationTimer = dispatcher.CreateTimer();
        navigationTimer.Interval = TimeSpan.FromMilliseconds(15);
        navigationTimer.Tick += (_, _) => Guard(Read);
        guideTimer = dispatcher.CreateTimer();
        guideTimer.Interval = TimeSpan.FromMilliseconds(25);
        guideTimer.Tick += (_, _) => Guard(() =>
        {
            if (session is null) return;
            if (viewMenuShortcut)
            {
                var shortcut = session.PollViewMenuShortcut();
                shortcutConsumed = shortcut.Consumed;
                if (shortcut.Pressed) { diagnostics.Write("View + Menu shortcut accepted"); ToggleRequested?.Invoke(); }
            }
            else if (session.PollLegacyGuide(Now) is { } signal) Handle(signal);
        });
        try
        {
            session = new(nativeBackend, this, () => Guard(Drain),
                message => diagnostics.Write($"Native: {message}"));
            session.SetOwnedWindows((nuint)hwnd);
            UpdateGuidePolling();
        }
        catch { Dispose(); throw; }
    }

    public bool TryEnqueue(Action action) => !closed && dispatcher.TryEnqueue(() =>
    {
        if (!closed) action();
    });

    internal void ApplyControllerSettings(ControllerSettings settings) => Guard(() =>
    {
        if (session is null || controllerRecoveryBlocked) return;
        var enabled = settings.OpenShortcut == ControllerOpenShortcut.ViewMenu;
        if (viewMenuShortcut == enabled) return;
        session.SetViewMenuShortcut(enabled);
        viewMenuShortcut = enabled;
        shortcutConsumed = false;
        UpdateGuidePolling();
        diagnostics.Write($"Controller shortcut configured={settings.OpenShortcut}");
    });

    public void SetVisible(bool value)
    {
        if (closed || session is null) return;
        visible = value;
        if (!value) handoffRelease.Cancel();
        if (controllerRecoveryBlocked) return;
        var foreground = IsForegroundProcess();
        session.SetWindowState(value, foreground);
        inputOwnership.Reset();
        var ownership = inputOwnership.Update(value, foreground);
        if (value)
        {
            if (ownership.Prime) session.PrimeController(true, Now);
            navigationTimer.Start();
        }
        else navigationTimer.Stop();
        RecordWindowState("SetVisible");
    }

    public bool PrepareShow()
    {
        var target = GetForegroundWindow();
        if (!IsForegroundProcess()) session?.ObserveForegroundTarget((nuint)target, target != 0 && IsWindow(target) != 0);
        RecordWindowState("PrepareShow");
        // F1/keyboard must keep Settings reachable while native recovery is
        // required. A neutral-input timeout simply cancels this opening attempt.
        if (controllerRecoveryBlocked) return true;
        try { session?.PrepareVisible(); return true; }
        catch (PlatformException error) when (error.Status == PlatformStatus.ControllerIsolationUnavailable)
        { diagnostics.Write("Exclusive control could not prepare this opening; release controls and retry."); return false; }
    }

    internal void TraceForegroundState(string reason) => RecordWindowState(reason);

    public bool AcquireForeground()
    {
        var confirmed = session?.AcquireForeground() == true;
        RecordWindowState($"Foreground acquisition confirmed={confirmed}");
        return confirmed;
    }

    private void Read()
    {
        if (session is null || !visible || controllerRecoveryBlocked) return;
        var candidate = GetForegroundWindow();
        if (IsCurrentExternalForeground(candidate) && ExternalForegroundObserved is not null)
        {
            inputOwnership.Reset();
            session.ObserveForegroundTarget((nuint)candidate, true);
            diagnostics.Write($"External foreground observed hwnd={candidate}; requesting overlay dismissal");
            ExternalForegroundObserved.Invoke(candidate);
            if (closed || !visible) return;
        }
        var foreground = IsForegroundProcess();
        session.SetWindowState(true, foreground);
        var ownership = inputOwnership.Update(true, foreground);
        // Foreground restoration is a fresh input boundary. Prime held state so
        // input used in another application cannot become a new overlay press.
        if (ownership.Prime) session.PrimeController(true, Now);
        session.RetryPendingNotifications();
        for (var count = 0; count < 16; ++count)
        {
            var frame = session.ReadController(foreground, Now);
            // Handoff observes physical state, not the shortcut-filtered state
            // delivered to widgets. Held View/Menu must not look released.
            var releaseFrame = frame;
            if (viewMenuShortcut)
            {
                const ushort chord = 0x30;
                frame.RecoveryChordPressed = 0;
                if (shortcutConsumed || (frame.State.Buttons & chord) == chord)
                {
                    frame.State.Buttons &= unchecked((ushort)~chord);
                    frame.PressedButtons &= unchecked((ushort)~chord);
                    frame.ReleasedButtons &= unchecked((ushort)~chord);
                }
            }
            if (navigationTraceRemaining > 0 && (frame.DpadNavigation.Phase != NavigationPhase.None || frame.StickNavigation.Phase != NavigationPhase.None))
            {
                --navigationTraceRemaining;
                diagnostics.Write($"Adapter navigation delivered={ownership.Deliver} dpad={frame.DpadNavigation.Direction}/{frame.DpadNavigation.Phase} stick={frame.StickNavigation.Direction}/{frame.StickNavigation.Phase} path={frame.ReadPath} buttons={frame.State.Buttons:X4} pending={frame.RemainingFrames}");
            }
            if (ownership.Deliver)
            {
                AdjustHandoffReleaseValidationFrame(ref releaseFrame);
                handoffRelease.Observe(releaseFrame);
                FrameReceived?.Invoke(frame);
            }
            if (closed || !visible) return;
            if (ownership.Deliver && !IsForegroundProcess()) { inputOwnership.Reset(); return; }
            if (frame.RemainingFrames == 0) break;
        }
    }

    private void Drain()
    {
        if (session is null) return;
        for (var count = 0; count < 32; ++count)
        {
            if (session.ReadEvent(Now) is not { } value) return;
            Handle(value);
            if (closed) return;
        }
        TryEnqueue(() => Guard(Drain));
    }

    partial void AdjustHandoffReleaseValidationFrame(ref ControllerFrame frame);

    private void Handle(PlatformEvent value)
    {
        if (value.Kind == PlatformEventKind.GuideToggleRequested && !viewMenuShortcut)
        {
            RecordWindowState($"Guide accepted source={value.GuideSource} timestamp={value.TimestampMilliseconds}");
            ToggleRequested?.Invoke();
            RecordWindowState("Guide handler completed");
        }
        if (value.Kind == PlatformEventKind.LegacyGuidePollingChanged) UpdateGuidePolling();
        if (value.Kind is PlatformEventKind.VisibilityChanged or PlatformEventKind.FocusChanged)
            diagnostics.Write($"Native window state event={value.Kind} value={value.Value}");
    }

    private void UpdateGuidePolling()
    {
        var required = !controllerRecoveryBlocked && (viewMenuShortcut || session?.RequiresLegacyGuidePolling == true);
        diagnostics.Write($"Legacy Guide polling required={required}");
        if (required) guideTimer.Start();
        else guideTimer.Stop();
    }

    private void Guard(Action action)
    {
        if (closed) return;
        try { action(); }
        catch (PlatformException error) when (error.Status == PlatformStatus.NotInitialized &&
            session?.ControllerControlState == PlatformControllerControlState.RecoveryRequired)
        {
            controllerRecoveryBlocked = true;
            navigationTimer.Stop(); guideTimer.Stop(); inputOwnership.Reset();
            diagnostics.Write("Controller recovery required; F1 and Settings recovery remain available.");
        }
        catch (Exception error)
        {
            diagnostics.Write($"Input failed: {error}");
            Dispose();
            Failed?.Invoke(error);
        }
    }

    private void RecordWindowState(string reason)
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        diagnostics.Write($"{reason}; pid={Environment.ProcessId} requestedVisible={visible} windowVisible={IsWindowVisible(ownedWindow) != 0} foregroundHwnd={foreground} foregroundPid={foregroundProcess}");
    }

    private static ulong Now => (ulong)Environment.TickCount64;
    internal bool IsCurrentExternalForeground(nint observed)
    {
        var current = GetForegroundWindow();
        GetWindowThreadProcessId(current, out var process);
        return !closed && ForegroundDismissalPolicy.ShouldDismiss(visible, observed, current,
            current != 0 && IsWindow(current) != 0, process, (uint)Environment.ProcessId);
    }

    private static bool IsForegroundProcess()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var process);
        return process == (uint)Environment.ProcessId;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindow(nint hwnd);

    public void Dispose()
    {
        if (closed) return;
        RecordWindowState("Input owner disposing");
        closed = true;
        handoffRelease.Cancel();
        navigationTimer.Stop();
        guideTimer.Stop();
        session?.Dispose();
        session = null;
        diagnostics.Dispose();
    }
}
