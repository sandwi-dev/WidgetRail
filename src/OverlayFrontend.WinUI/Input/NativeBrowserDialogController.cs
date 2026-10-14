using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Admits one explicitly requested browser dialog to the shared native pointer transport.</summary>
internal sealed partial class NativeBrowserDialogController : IDisposable
{
    private readonly DispatcherQueue queue;
    private readonly DispatcherQueueTimer timer;
    private readonly Action<bool> changed;
    private readonly Action returnFocus;
    private CancellationTokenRegistration cancellation;
    private CancellationToken token;
    private uint process;
    private nint target;
    private readonly NativeDialogLifetime state = new();
    private Func<ControllerPointerAppearance>? appearance;
    private readonly Action<string?> reportStatus;
    private long generation;
    private long commandVersion;
    private bool modalConfirmed, pointerInitialized;
    private long nextInspection;
    private ulong dialogIdentity;
    private bool disposed;
    private long nextCloseAttempt;
    private string? cancellationReason, lastInputRejection;
    private long receivedFrames, lastFrameAt, nextRoutingTrace;
    private bool? lastCircleVisible;
    private bool hostVisible = true, reacquireOnRestore;
    private TaskCompletionSource? completion;
    private long completionStarted, foregroundSettledAt;
    private nint completionForeground;
    private Task? inspection, closing;
    private readonly NativeDialogPointerInput pointer;
    private NativeDialogPointerInput.Bounds bounds;
    internal bool Active => state.Phase != NativeDialogPhase.Inactive;
    internal bool HasForeground => OwnsForeground(GetForegroundWindow());
    // Chromium may activate the selected sharing source before closing its dialog.
    // Resolve this short handoff before the shell mistakes it for an ordinary app switch.
    internal bool DeferExternalDismissal() => Active && state.HasOpened;
#if ENABLE_WIDGET_VALIDATION
    internal bool BlockCloseForValidation { get; set; }
    internal string PointerStatus => pointer.Status;
    internal (int X, int Y, bool Visible) PointerPosition => pointer.Position;
    internal nint DialogWindow => target;
#endif

    internal NativeBrowserDialogController(DispatcherQueue queue, Action<bool> changed, Action returnFocus, Action<string?> reportStatus, Action<string> traceInput)
    {
        this.queue = queue; this.changed = changed; this.returnFocus = returnFocus; this.reportStatus = reportStatus;
        pointer = new(traceInput);
        timer = queue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(25);
        timer.Tick += (_, _) => ObserveWindow();
    }

    internal bool Begin(uint browserProcess, Func<ControllerPointerAppearance> appearance, CancellationToken cancellationToken)
    {
        if (disposed || Active || inspection is { IsCompleted: false } || closing is { IsCompleted: false } || browserProcess == 0 || cancellationToken.IsCancellationRequested) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        if (foregroundProcess != (uint)Environment.ProcessId && foregroundProcess != browserProcess)
        {
            Diagnostics.FrontendFailureLog.Current.Write("native-browser-dialog-admission", null, "Sharing dialog preparation lost foreground ownership.");
            return false;
        }
        process = browserProcess; target = 0;
        modalConfirmed = pointerInitialized = false; this.appearance = appearance; nextInspection = 0; commandVersion = 0;
        pointer.Reset(); bounds = default; completionStarted = nextCloseAttempt = 0;
        dialogIdentity = 0; cancellationReason = lastInputRejection = null;
        receivedFrames = lastFrameAt = 0; nextRoutingTrace = Environment.TickCount64 + 2000; lastCircleVisible = null;
        hostVisible = true; reacquireOnRestore = false;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        token = cancellationToken; state.Begin(Environment.TickCount64);
        var version = ++generation;
        reportStatus(null);
        Trace("begin", 0, 0);
        // The browser process may have started long before this user gesture.
        // Explicitly grant it foreground eligibility while the host owns focus.
        if (foregroundProcess == (uint)Environment.ProcessId)
        {
            var granted = AllowSetForegroundWindow(browserProcess) != 0;
            Trace(granted ? "foreground-granted" : "foreground-grant-failed", granted ? 0 : Marshal.GetLastPInvokeError(), 0);
        }
        cancellation = token.Register(() => queue.TryEnqueue(() => { if (version == generation) RequestClose("request-cancelled"); }));
        StartWindowObservation(browserProcess);
        changed(true); timer.Start();
        return true;
    }

    private void ObserveWindow()
    {
        if (!Active) return;
        if (!hostVisible) { pointer.Reset("host-hidden"); return; }
        if (target == 0 || GetForegroundWindow() != target)
        { pointer.Reset("foreground-lost"); pointerInitialized = false; }
        else
        {
            if (modalConfirmed && !pointerInitialized) { pointer.Center(bounds); pointerInitialized = true; }
            pointer.ObservePhysicalInput(); PaintPointer();
        }
        var circleVisible = pointer.Position.Visible;
        if (lastCircleVisible != circleVisible)
        { lastCircleVisible = circleVisible; Trace(circleVisible ? "circle-visible" : "circle-hidden", 0, 0); }
        if (Environment.TickCount64 >= nextRoutingTrace)
        { nextRoutingTrace = Environment.TickCount64 + 2000; Trace("routing", 0, 0); }
        if (token.IsCancellationRequested) RequestClose("request-cancelled");
        else if (state.CancellationRequested) RequestClose(cancellationReason ?? "cancel");
        if (completionStarted != 0)
        {
            var now = Environment.TickCount64;
            var foreground = GetForegroundWindow();
            if (foreground != completionForeground) { completionForeground = foreground; foregroundSettledAt = now; }
            // Chromium posts source-window activation after removing the chooser.
            // Keep the modal lease until that focus handoff settles, with a hard bound.
            if (now - foregroundSettledAt >= 150 || now - completionStarted >= 1000) Finish();
            return;
        }
        // Foreground is an input eligibility signal, never dialog-closure evidence.
        if (inspection is { IsCompleted: false } || closing is { IsCompleted: false } || Environment.TickCount64 < nextInspection) return;
        nextInspection = Environment.TickCount64 + (target == 0 ? 150 : 500);
        inspection = InspectAsync(process, generation, commandVersion);
    }

    private async Task InspectAsync(uint expectedProcess, long version, long command)
    {
        var knownWindow = target; var knownIdentity = dialogIdentity;
        var restoring = reacquireOnRestore;
        var result = await Task.Run(() =>
        {
            var observed = FindDialog(expectedProcess, knownWindow, knownIdentity, false);
            // Re-showing a WebView may recreate accessibility objects without
            // completing the outstanding getDisplayMedia request. Rebind only
            // during this explicit host-visibility transition and unique admission.
            return restoring && observed.Result >= 0 && observed.Window == 0
                ? FindDialog(expectedProcess, 0, 0, false) : observed;
        });
        if (!Active || !hostVisible || version != generation || command != commandVersion) return;
        var previous = state.Phase;
        modalConfirmed = result.Result >= 0 && result.Window != 0;
        if (result.Result >= 0 && !modalConfirmed && state.HasOpened) nextInspection = Environment.TickCount64 + 150;
        state.Observe(result.Result >= 0 ? result.Window != 0 : null, Environment.TickCount64);
        if (state.Phase != previous) Trace(state.Phase.ToString(), result.Result, result.Inspected, result.Window);
        if (state.Phase == NativeDialogPhase.Failed)
        {
            if (previous != NativeDialogPhase.Failed) reportStatus("Chooser unavailable · B to cancel");
            return; // Keep the modal/placement lease. Unknown is not closed.
        }
        if (state.Phase == NativeDialogPhase.Completing)
        {
            pointer.Reset();
            completionStarted = foregroundSettledAt = Environment.TickCount64;
            completionForeground = GetForegroundWindow(); return;
        }
        if (!modalConfirmed) return;
        var first = target == 0 || target != result.Window;
        target = result.Window; dialogIdentity = result.Identity; bounds = result.Bounds; reportStatus(null);
        reacquireOnRestore = false;
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if ((first || restoring) && (foregroundProcess == (uint)Environment.ProcessId || foregroundProcess == process))
        {
            _ = SetForegroundWindow(target);
            if (OwnsForeground(GetForegroundWindow())) { pointer.Center(bounds); pointerInitialized = true; PaintPointer(); }
        }
    }

    private void PaintPointer()
    {
        if (target == 0) return;
        try { pointer.Paint(Math.Max(96u, GetDpiForWindow(target)), appearance?.Invoke() ?? ControllerPointerAppearance.Default); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { pointer.Reset(); Diagnostics.FrontendFailureLog.Current.Write("native-dialog-cursor", error); }
    }
    private void Trace(string phase, int result, uint inspected, nint candidate = 0) =>
        Diagnostics.FrontendFailureLog.Current.Write("native-browser-dialog", null,
            $"generation={generation} phase={phase} browserPid={process} hwnd={(candidate != 0 ? candidate : target)} identity={dialogIdentity} foreground={GetForegroundWindow()} confirmed={modalConfirmed} frames={receivedFrames} frameAgeMs={(lastFrameAt == 0 ? -1 : Environment.TickCount64 - lastFrameAt)} circle={pointer.Position.Visible} inspected={inspected} result=0x{result:X8} cancel={cancellationReason ?? "none"}");

    internal bool OwnsForeground(nint window) => Active && modalConfirmed && target != 0 && window == target && IsTarget();
    internal bool OwnsControllerWindow(nint window)
    {
        if (!Active) return false;
        GetWindowThreadProcessId(window, out var owner);
        return owner == (uint)Environment.ProcessId || owner == process;
    }

    internal void Receive(ControllerFrame frame)
    {
        if (!Active || disposed) return;
        receivedFrames++; lastFrameAt = Environment.TickCount64;
        if (frame.Connected == 0) { RejectInput("disconnected"); pointer.Reset("disconnected"); return; }
        if (frame.Primed != 0) { RejectInput("primed"); pointer.ReleaseInput(); return; }
        var foreground = GetForegroundWindow();
        if (!OwnsControllerWindow(foreground)) { RejectInput("foreign-foreground"); pointer.Reset(); return; }
        if ((frame.PressedButtons & 0x2000) != 0)
        {
            RequestClose("controller-B"); return;
        }
        if (!modalConfirmed || !IsTarget()) { RejectInput("not-confirmed"); return; }
        if (foreground != target) { RejectInput("awaiting-foreground"); _ = SetForegroundWindow(target); return; }
        if (state.CancellationRequested) { RejectInput("closing"); return; }
        if (lastInputRejection is not null) { Trace("input-ready", 0, 0); lastInputRejection = null; }
        if (pointer.Receive(frame, bounds, () => OwnsForeground(GetForegroundWindow()), ResolvePointerWindow))
            InvalidateAfterActivation();
        PaintPointer();
    }

    private nint ResolvePointerWindow(NativeDialogPointerInput.Point screen)
    {
        if (!IsTarget()) return 0;
        // Resolve within the admitted root. A transparent circle window must never
        // become the input target, and no unrelated top-level window is eligible.
        var current = target;
        for (var depth = 0; depth < 16; depth++)
        {
            var client = screen;
            if (ScreenToClient(current, ref client) == 0) return 0;
            var child = ChildWindowFromPointEx(current, client, 7);
            if (child == 0 || child == current) break;
            GetWindowThreadProcessId(child, out var owner);
            if (owner != process) return 0;
            current = child;
        }
        return current;
    }

    private void RejectInput(string reason)
    {
        if (reason == lastInputRejection) return;
        lastInputRejection = reason;
        Trace("input-" + reason, 0, 0);
    }

    internal void SetHostVisibility(bool value)
    {
        if (hostVisible == value && (!Active || !value)) return;
        hostVisible = value;
        if (!Active) return;
        ++commandVersion; modalConfirmed = false; pointerInitialized = false;
        pointer.Reset(value ? "host-restored" : "host-hidden");
        completionStarted = 0; state.Recheck(Environment.TickCount64);
        reacquireOnRestore = value && state.HasOpened;
        nextInspection = 0;
        if (value) timer.Start(); else timer.Stop();
        Trace(value ? "host-restored" : "host-hidden", 0, 0);
    }

    private void InvalidateAfterActivation()
    {
        // Share/Cancel can dismiss the dialog while its HWND survives. Do not
        // reuse a pre-gesture inspection to send another key into the webpage.
        ++commandVersion; modalConfirmed = false; nextInspection = 0;
    }

    private unsafe void SendKey(ushort key, bool shift = false)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        if (!Active || foregroundProcess != process) return;
        // Do not alter a physical keyboard chord or leave synthetic keys held.
        foreach (var modifier in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(modifier) & 0x8000) != 0) return;
        NativeInput* inputs = stackalloc NativeInput[4];
        var count = 0;
        if (shift) inputs[count++] = Key(0x10, false);
        inputs[count++] = Key(key, false); inputs[count++] = Key(key, true);
        if (shift) inputs[count++] = Key(0x10, true);
        var sent = SendInput((uint)count, inputs, sizeof(NativeInput));
        if (sent != count)
        {
            var held = new HashSet<ushort>();
            for (var index = 0; index < Math.Min(sent, (uint)count); index++)
            {
                var keyInput = inputs[index].Data.Keyboard;
                if ((keyInput.Flags & 2) != 0) held.Remove(keyInput.VirtualKey); else held.Add(keyInput.VirtualKey);
            }
            var releaseCount = 0;
            foreach (var heldKey in held) inputs[releaseCount++] = Key(heldKey, true);
            if (releaseCount != 0) _ = SendInput((uint)releaseCount, inputs, sizeof(NativeInput));
            Diagnostics.FrontendFailureLog.Current.Write("native-browser-dialog-input", null, "Windows did not accept the complete controller key gesture.");
        }
    }

    private bool IsTarget()
    {
        if (target == 0 || IsWindow(target) == 0 || IsWindowVisible(target) == 0) return false;
        GetWindowThreadProcessId(target, out var actual);
        return actual == process;
    }

    private void RequestClose(string reason)
    {
        if (!Active) return;
        if (!state.CancellationRequested)
        {
            cancellationReason = reason; state.RequestCancellation();
            ++commandVersion; nextInspection = 0; pointer.Reset(); Trace("close-requested", 0, 0);
        }
        if (closing is { IsCompleted: false } || inspection is { IsCompleted: false } || Environment.TickCount64 < nextCloseAttempt) return;
        nextCloseAttempt = Environment.TickCount64 + 1000;
#if ENABLE_WIDGET_VALIDATION
        if (BlockCloseForValidation) { Trace("close-deferred-fixture", 0, 0); return; }
#endif
        // An explicit hide/cancel may have moved focus to the overlay first.
        // Recover the admitted chooser only from our own foreground, never from
        // an unrelated application, before sending its native Escape action.
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        if (hostVisible && modalConfirmed && IsTarget() && foregroundProcess == (uint)Environment.ProcessId)
            _ = SetForegroundWindow(target);
        if (OwnsForeground(GetForegroundWindow())) SendKey(0x1B);
        var expectedProcess = process; var window = target; var identity = dialogIdentity; var version = generation;
        closing = CloseAsync();
        async Task CloseAsync()
        {
            var result = await Task.Run(() => FindDialog(expectedProcess, window, identity, true));
            if (!Active || version != generation) return;
            Trace("close-result", result.Result, result.Inspected, result.Window);
            // A successful Close call is only a request. Inspect afterward before
            // releasing controller/modal/placement ownership, even when hidden.
            nextInspection = 0;
        }
    }

    internal Task CloseBeforeHideAsync()
    {
        if (!Active) return Task.CompletedTask;
        RequestClose("overlay-hide");
        return completion!.Task;
    }

    private void Finish()
    {
        if (!Active) return;
        pointer.Reset();
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        var restore = hostVisible && (state.HasOpened || foreground == target || foregroundProcess == (uint)Environment.ProcessId || foreground == 0);
        Trace(state.CancellationRequested ? "cancelled-confirmed" : "completed", 0, 0);
        StopWindowObservation();
        ++generation; state.End(); appearance = null; reportStatus(null); timer.Stop(); cancellation.Dispose(); target = 0; process = 0;
        if (restore) returnFocus();
        changed(false);
        completion?.TrySetResult();
    }

    public void Dispose()
    {
        if (disposed) return;
        RequestClose("shutdown"); disposed = true;
        // Shutdown retires the browser controller itself; it cannot retain a UI lease.
        StopWindowObservation(); timer.Stop(); cancellation.Dispose(); ++generation; state.End(); pointer.Dispose();
        completion?.TrySetResult();
    }

    private static (int Result, nint Window, NativeDialogPointerInput.Bounds Bounds, uint Inspected, ulong Identity) FindDialog(uint process, nint knownWindow, ulong identity, bool close)
    {
        try
        {
            var result = ObserveSharingDialog(process, (nuint)knownWindow, identity, close ? 1u : 0u, out var window, out var bounds, out var inspected, out var foundIdentity);
            return (result, (nint)window, bounds, inspected, foundIdentity);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { Diagnostics.FrontendFailureLog.Current.Write("native-browser-dialog-automation", error); return (unchecked((int)0x80004005), 0, default, 0, 0); }
    }

    private static NativeInput Key(ushort key, bool released) => new()
    { Type = 1, Data = new() { Keyboard = new() { VirtualKey = key, Flags = released ? 2u : 0u } } };
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { internal ushort VirtualKey, Scan; internal uint Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }

    // Borrowed HWNDs; only the native dialog created during this request is controlled.
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindow(nint window);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindowVisible(nint window);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetForegroundWindow(nint window);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int AllowSetForegroundWindow(uint process);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, NativeInput* inputs, int size);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint ChildWindowFromPointEx(nint parent, NativeDialogPointerInput.Point point, uint flags);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ScreenToClient(nint window, ref NativeDialogPointerInput.Point point);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformObserveSharingDialog")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial int ObserveSharingDialog(uint process, nuint knownWindow, ulong expectedIdentity, uint close, out nuint window, out NativeDialogPointerInput.Bounds bounds, out uint inspected, out ulong identity);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint window);
}
