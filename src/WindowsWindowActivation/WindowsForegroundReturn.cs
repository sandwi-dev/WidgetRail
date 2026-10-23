namespace WidgetRail.WindowsWindowActivation;

public sealed partial class WindowsTaskWindowActivation
{
    /// <summary>Snapshot the external foreground before showing host UI. No enumeration or activation.</summary>
    public unsafe TaskWindowTarget? CaptureForegroundTarget()
    {
        var window = GetForegroundWindow();
        if (window == 0) return null;
        _ = GetWindowThreadProcessId(window, out var process);
        if (process == 0 || process == (uint)Environment.ProcessId) return null;
        char* buffer = stackalloc char[256];
        var length = GetClassNameW(window, buffer, 256);
        if (length == 0) return null;
        using var handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, 0, process);
        if (handle.IsInvalid || GetProcessTimes(handle, out var created, out _, out _, out _) == 0) return null;
        var target = new TaskWindowTarget((ulong)(nuint)window, process, created, new string(buffer, 0, length));
        return GetForegroundWindow() == window && IsCurrent(target) ? target : null;
    }

    /// <summary>
    /// Return activation before hiding host UI, while it still owns foreground.
    /// Unlike an explicit task switch, never restore a hidden/minimized target or
    /// take foreground away from another application. One attempt, no input injection.
    /// </summary>
    public bool TryReturnForeground(TaskWindowTarget target)
    {
        if (!IsCurrent(target)) return false;
        var window = (nint)(nuint)target.Handle;
        if (IsWindowVisible(window) == 0 || IsIconic(window) != 0 || !IsOverlayForeground) return false;
        var accepted = SetForegroundWindow(window) != 0;
        Trace("foreground-return accepted=" + accepted, target);
        return accepted;
    }
}
