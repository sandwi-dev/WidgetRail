using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WidgetRail.OverlayPlatformClient;

/// <summary>Native task-switch handoff, matching the original overlay's identity checks.</summary>
public sealed partial class WindowsTaskWindowActivation : ITaskWindowActivationPlatform
{
    public long UptimeMilliseconds => Environment.TickCount64;
    public bool IsOverlayForeground
    {
        get { _ = GetWindowThreadProcessId(GetForegroundWindow(), out var process); return process == (uint)Environment.ProcessId; }
    }

    public unsafe bool IsCurrent(TaskWindowTarget target)
    {
        if (target.Handle == 0 || (IntPtr.Size == 4 && target.Handle > uint.MaxValue) ||
            target.ProcessId == 0 || target.ProcessId == (uint)Environment.ProcessId ||
            string.IsNullOrEmpty(target.ClassName) || target.ClassName.Length >= 256) return false;
        var window = (nint)(nuint)target.Handle;
        if (IsWindow(window) == 0 || GetAncestor(window, 2 /* GA_ROOT */) != window) return false;
        _ = GetWindowThreadProcessId(window, out var process);
        if (process != target.ProcessId) return false;
        char* buffer = stackalloc char[256];
        var length = GetClassNameW(window, buffer, 256);
        if (length == 0 || !new ReadOnlySpan<char>(buffer, length).SequenceEqual(target.ClassName)) return false;
        using var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, 0, process);
        return !handle.IsInvalid && GetProcessTimes(handle, out var created, out _, out _, out _) != 0 && created == target.ProcessCreated;
    }

    public bool RequestActivation(TaskWindowTarget target)
    {
        if (!IsCurrent(target)) return false;
        var window = (nint)(nuint)target.Handle;
        if (IsIconic(window) != 0 && ShowWindowAsync(window, 9 /* SW_RESTORE */) == 0) return false;
        return SetForegroundWindow(window) != 0;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindow(nint window);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetAncestor(nint window, uint flags);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int GetClassNameW(nint window, char* buffer, int capacity);
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeProcessHandle OpenProcess(uint access, int inherit, uint process);
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetProcessTimes(SafeProcessHandle process, out ulong created, out ulong exited, out ulong kernel, out ulong user);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsIconic(nint window);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ShowWindowAsync(nint window, int command);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetForegroundWindow(nint window);
}
