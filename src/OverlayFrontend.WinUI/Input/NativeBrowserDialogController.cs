using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using WidgetRail.OverlayPlatformClient;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Controller keyboard navigation for one explicitly requested native browser dialog.</summary>
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
    private long expires;
    private long generation;
    private long commandVersion;
    private bool modalConfirmed, seenDialog;
    private long nextInspection;
    private Task? inspection, closing;
    internal bool Active { get; private set; }
    internal bool HasForeground => OwnsForeground(GetForegroundWindow());

    internal NativeBrowserDialogController(DispatcherQueue queue, Action<bool> changed, Action returnFocus)
    {
        this.queue = queue; this.changed = changed; this.returnFocus = returnFocus;
        timer = queue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(25);
        timer.Tick += (_, _) => ObserveWindow();
    }

    internal bool Begin(uint browserProcess, CancellationToken cancellationToken)
    {
        if (Active || inspection is { IsCompleted: false } || closing is { IsCompleted: false } || browserProcess == 0 || cancellationToken.IsCancellationRequested) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
        if (foregroundProcess != (uint)Environment.ProcessId) return false;
        process = browserProcess; target = 0;
        modalConfirmed = seenDialog = false; nextInspection = 0; commandVersion = 0;
        token = cancellationToken; expires = Environment.TickCount64 + 10_000;
        var version = ++generation;
        Active = true;
        cancellation = token.Register(() => queue.TryEnqueue(() => { if (version == generation) End(cancel: true); }));
        changed(true); timer.Start();
        return true;
    }

    private void ObserveWindow()
    {
        if (!Active) return;
        if (token.IsCancellationRequested) { End(cancel: true); return; }
        if (target != 0 && !IsTarget()) { End(cancel: false); return; }
        if (!seenDialog && Environment.TickCount64 >= expires) { End(cancel: false); return; }
        // WebView2 may create its native dialog host before raising the
        // ScreenCaptureStarting deferral. The admitted request owns the sole
        // visible native window, including that already-created host.
        if (target == 0)
        {
            var candidates = Windows(process).ToArray();
            if (candidates.Length != 1) return; // Never guess between multiple native windows.
            target = candidates[0];
        }
        if (inspection is { IsCompleted: false } || Environment.TickCount64 < nextInspection) return;
        nextInspection = Environment.TickCount64 + 150;
        inspection = InspectAsync(target, process, generation, commandVersion);
    }

    private async Task InspectAsync(nint window, uint expectedProcess, long version, long command)
    {
        var result = await Task.Run(() => QueryDialog(window, expectedProcess, false));
        if (!Active || version != generation || target != window || command != commandVersion) return;
        modalConfirmed = result == true;
        if (result == false && seenDialog) { End(cancel: false); return; }
        if (result != true) return;
        var first = !seenDialog; seenDialog = true;
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        if (first && (foregroundProcess == (uint)Environment.ProcessId || foreground == target)) _ = SetForegroundWindow(target);
    }

    internal bool OwnsForeground(nint window) => Active && modalConfirmed && target != 0 && window == target && IsTarget();
    internal bool OwnsControllerWindow(nint window)
    {
        if (!Active || !modalConfirmed || target == 0 || !IsTarget()) return false;
        GetWindowThreadProcessId(window, out var owner);
        return window == target || owner == (uint)Environment.ProcessId;
    }

    internal void Receive(ControllerFrame frame)
    {
        if (frame.Connected == 0 || frame.Primed != 0) return;
        var foreground = GetForegroundWindow();
        if (!OwnsControllerWindow(foreground)) return;
        if (foreground != target) { _ = SetForegroundWindow(target); return; }
        var navigation = frame.DpadNavigation.Phase != NavigationPhase.None ? frame.DpadNavigation : frame.StickNavigation;
        if (navigation.Phase != NavigationPhase.None)
        {
            var key = navigation.Direction switch
            { NavigationDirection.Left => 0x25, NavigationDirection.Up => 0x26, NavigationDirection.Right => 0x27, NavigationDirection.Down => 0x28, _ => 0 };
            if (key != 0) SendKey((ushort)key);
        }
        if ((frame.PressedButtons & 0x1000) != 0) { SendKey(0x20); InvalidateAfterActivation(); } // Space activates the focused native control.
        if ((frame.PressedButtons & 0x2000) != 0) { SendKey(0x1B); InvalidateAfterActivation(); }
        if ((frame.PressedButtons & 0x100) != 0) SendKey(0x09, shift: true);
        if ((frame.PressedButtons & 0x200) != 0) SendKey(0x09);
    }

    private void InvalidateAfterActivation()
    {
        // Share/Cancel can dismiss the dialog while its HWND survives. Do not
        // reuse a pre-gesture inspection to send another key into the webpage.
        ++commandVersion; modalConfirmed = false; nextInspection = 0;
    }

    private unsafe void SendKey(ushort key, bool shift = false)
    {
        if (!OwnsForeground(GetForegroundWindow())) return;
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

    private void End(bool cancel)
    {
        if (!Active) return;
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcess);
        var restore = !cancel && (foreground == target || foregroundProcess == (uint)Environment.ProcessId || foreground == 0);
        if (cancel && IsTarget())
        {
            var window = target; var expectedProcess = process;
            // Close the accessible dialog, never its reusable browser HWND.
            closing = Task.Run(() => QueryDialog(window, expectedProcess, true));
        }
        ++generation; Active = false; timer.Stop(); cancellation.Dispose(); target = 0; process = 0;
        changed(false);
        if (restore) returnFocus();
    }

    public void Dispose() => End(cancel: true);

    private static bool? QueryDialog(nint window, uint process, bool close)
    {
        try
        {
            var result = QueryNativeBrowserDialog((nuint)window, process, close ? 1u : 0u, out var present);
            return result >= 0 ? present != 0 : null;
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        { Diagnostics.FrontendFailureLog.Current.Write("native-browser-dialog-automation", error); return null; }
    }

    private sealed record WindowSearch(uint Process, HashSet<nint> Results);
    private static unsafe HashSet<nint> Windows(uint process)
    {
        var search = new WindowSearch(process, []);
        var handle = GCHandle.Alloc(search);
        try { _ = EnumWindows(&CollectWindow, GCHandle.ToIntPtr(handle)); }
        finally { handle.Free(); }
        return search.Results;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectWindow(nint window, nint context)
    {
        try
        {
            var search = (WindowSearch)GCHandle.FromIntPtr(context).Target!;
            GetWindowThreadProcessId(window, out var owner);
            if (owner == search.Process && IsWindowVisible(window) != 0) search.Results.Add(window);
        }
        catch { return 0; } // Exceptions cannot cross the native callback boundary.
        return 1;
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
    private static unsafe partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint context);
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
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial uint SendInput(uint count, NativeInput* inputs, int size);
    [LibraryImport("OverlayPlatformInterop.dll", EntryPoint = "WidgetRailOverlayPlatformQueryNativeBrowserDialog")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    private static partial int QueryNativeBrowserDialog(nuint window, uint process, uint close, out uint present);
}
