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
    private OverlayPlatformSession? session;
    private volatile bool closed;
    private bool visible;
    public event Action<ControllerFrame>? FrameReceived;
    public event Action? ToggleRequested;
    public event Action<Exception>? Failed;

    public PlatformInputPump(DispatcherQueue dispatcher, nint hwnd)
    {
        this.dispatcher = dispatcher;
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
            session = new(new OverlayPlatformNative(), this, () => Guard(Drain));
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
    }

    public void PrepareShow() => session?.PrepareVisible();

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
        if (value.Kind == PlatformEventKind.GuideToggleRequested) ToggleRequested?.Invoke();
        if (value.Kind == PlatformEventKind.LegacyGuidePollingChanged) UpdateGuidePolling();
    }

    private void UpdateGuidePolling()
    {
        if (session?.RequiresLegacyGuidePolling == true) guideTimer.Start();
        else guideTimer.Stop();
    }

    private void Guard(Action action)
    {
        if (closed) return;
        try { action(); }
        catch (Exception error) { Dispose(); Failed?.Invoke(error); }
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

    public void Dispose()
    {
        if (closed) return;
        closed = true;
        navigationTimer.Stop();
        guideTimer.Stop();
        session?.Dispose();
        session = null;
    }
}
