using System.Runtime.InteropServices;

namespace WidgetRail.OverlayFrontend.WinUI.Input;

internal sealed partial class NativeBrowserDialogController
{
    private nint foregroundHook, windowHook;
    private WinEventCallback? windowEvent;

    private void StartWindowObservation(uint browserProcess)
    {
        StopWindowObservation();
        windowEvent ??= OnWindowEvent; // Root the callback for the controller lifetime.
        // Out-of-context hooks arrive on this UI thread. They are hints only:
        // Chromium may reuse an HWND without creating/destroying its native window.
        foregroundHook = SetWinEventHook(3, 3, 0, windowEvent, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND
        windowHook = SetWinEventHook(0x8000, 0x800B, 0, windowEvent, browserProcess, 0, 0);
        if (foregroundHook == 0 || windowHook == 0) Trace("event-hook-unavailable", Marshal.GetLastPInvokeError(), 0);
    }
    private void OnWindowEvent(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time)
    {
        if (!Active || disposed) return;
        if (kind == 3 || objectId == 0 && childId == 0) nextInspection = 0;
    }
    private void StopWindowObservation()
    {
        if (foregroundHook != 0) { _ = UnhookWinEvent(foregroundHook); foregroundHook = 0; }
        if (windowHook != 0) { _ = UnhookWinEvent(windowHook); windowHook = 0; }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void WinEventCallback(nint hook, uint kind, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventCallback callback, uint process, uint thread, uint flags);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int UnhookWinEvent(nint hook);
}
