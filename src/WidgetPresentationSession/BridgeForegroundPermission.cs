using System.Runtime.InteropServices;

namespace WidgetRail.WidgetPresentationSession;

/// <summary>Foreground permission for the trusted broker only; never grants worker input access.</summary>
internal static partial class BridgeForegroundPermission
{
    internal static uint ForegroundProcess
    {
        get { GetWindowThreadProcessId(GetForegroundWindow(), out var process); return process; }
    }

    internal static bool Allow(uint process, out int error)
    {
        var allowed = AllowSetForegroundWindow(process) != 0;
        error = allowed ? 0 : Marshal.GetLastPInvokeError();
        return allowed;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int AllowSetForegroundWindow(uint process);
}
