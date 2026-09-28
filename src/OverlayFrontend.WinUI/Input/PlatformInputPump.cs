using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Input polling only; WinUI owns frame scheduling and navigation.</summary>
internal sealed partial class PlatformInputPump : IDisposable, IPlatformDispatcher
{
    private readonly DispatcherQueue dispatcher;
    private readonly DispatcherQueueTimer navigationTimer;
    private readonly DispatcherQueueTimer guideTimer;
    private readonly nint ownedWindow;
    private readonly PlatformInputDiagnostics diagnostics = new(PlatformInputDiagnostics.DefaultPath);
    private OverlayPlatformSession? session;
    private volatile bool closed;
    private bool visible;
    public event Action<ControllerFrame>? FrameReceived;
    public event Action? ToggleRequested;
    public event Action<Exception>? Failed;
    internal bool IsForeground => IsForegroundProcess();

    public PlatformInputPump(DispatcherQueue dispatcher, nint hwnd, IOverlayPlatformNative? backend = null)
    {
        this.dispatcher = dispatcher;
        ownedWindow = hwnd;
        diagnostics.Write($"Input owner created pid={Environment.ProcessId} hwnd={hwnd} backend={backend?.GetType().Name ?? nameof(OverlayPlatformNative)}");
        navigationTimer = dispatcher.CreateTimer();
        navigationTimer.Interval = TimeSpan.FromMilliseconds(15);
        navigationTimer.Tick += (_, _) => Guard(Read);
        guideTimer = dispatcher.CreateTimer();
        guideTimer.Interval = TimeSpan.FromMilliseconds(25);
        guideTimer.Tick += (_, _) => Guard(() =>
        {
            if (session?.PollLegacyGuide(Now) is { } signal) Handle(signal);
        });
        try
        {
            session = new(backend ?? new OverlayPlatformNative(), this, () => Guard(Drain),
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

    public void SetVisible(bool value)
    {
        if (closed || session is null) return;
        visible = value;
        session.SetWindowState(value, IsForegroundProcess());
        if (value)
        {
            session.PrimeController(IsForegroundProcess(), Now);
            navigationTimer.Start();
        }
        else navigationTimer.Stop();
        RecordWindowState("SetVisible");
    }

    public void PrepareShow()
    {
        RecordWindowState("PrepareShow");
        session?.PrepareVisible();
    }

    public bool AcquireForeground()
    {
        var confirmed = session?.AcquireForeground() == true;
        RecordWindowState($"Foreground acquisition confirmed={confirmed}");
        return confirmed;
    }

    private void Read()
    {
        if (session is null || !visible) return;
        var foreground = IsForegroundProcess();
        session.SetWindowState(true, foreground);
        session.RetryPendingNotifications();
        for (var count = 0; count < 16; ++count)
        {
            var frame = session.ReadController(foreground, Now);
            FrameReceived?.Invoke(frame);
            if (closed) return;
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

    private void Handle(PlatformEvent value)
    {
        if (value.Kind == PlatformEventKind.GuideToggleRequested)
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
        var required = session?.RequiresLegacyGuidePolling == true;
        diagnostics.Write($"Legacy Guide polling required={required}");
        if (required) guideTimer.Start();
        else guideTimer.Stop();
    }

    private void Guard(Action action)
    {
        if (closed) return;
        try { action(); }
        catch (Exception error)
        {
            diagnostics.Write($"Input failed: {error}");
            Dispose();
            Failed?.Invoke(error);
        }
    }

    private void RecordWindowState(string reason)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        diagnostics.Write($"{reason}; requestedVisible={visible} windowVisible={IsWindowVisible(ownedWindow) != 0} foregroundPid={foregroundProcess}");
    }

    private static ulong Now => (ulong)Environment.TickCount64;
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

    public void Dispose()
    {
        if (closed) return;
        RecordWindowState("Input owner disposing");
        closed = true;
        navigationTimer.Stop();
        guideTimer.Stop();
        session?.Dispose();
        session = null;
        diagnostics.Dispose();
    }
}
