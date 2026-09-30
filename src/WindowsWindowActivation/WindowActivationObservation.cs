using System.Runtime.InteropServices;

namespace WidgetRail.WindowsWindowActivation;

/// <summary>Read-only HWND/input-queue evidence; no titles, keystrokes or activation.</summary>
public sealed record WindowActivationObservation(long Timestamp, uint CallerThread, long Foreground,
    uint ForegroundThread, uint ForegroundProcess, bool GuiInfoAvailable, uint GuiFlags,
    long Active, long Focus, long Capture, long MenuOwner, uint LastInputTick, bool LastInputAvailable,
    long Target, uint TargetThread, uint TargetProcess, bool TargetValid, bool TargetVisible, bool TargetMinimized)
{
    public long Qpc { get; init; }
}

public sealed partial class WindowsTaskWindowActivation
{
    public static WindowActivationObservation Observe(nint target)
    {
        var qpc = System.Diagnostics.Stopwatch.GetTimestamp();
        var foreground = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foreground, out var foregroundProcess);
        var targetThread = GetWindowThreadProcessId(target, out var targetProcess);
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        var guiAvailable = GetGUIThreadInfo(foregroundThread, ref gui) != 0;
        var last = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        var lastAvailable = GetLastInputInfo(ref last) != 0;
        return new(Environment.TickCount64, GetCurrentThreadId(), (long)foreground, foregroundThread, foregroundProcess,
            guiAvailable, gui.Flags, (long)gui.Active, (long)gui.Focus, (long)gui.Capture, (long)gui.MenuOwner,
            last.Tick, lastAvailable, (long)target, targetThread, targetProcess,
            IsWindow(target) != 0, IsWindowVisible(target) != 0, IsIconic(target) != 0) { Qpc = qpc };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal uint Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { internal uint Size, Tick; }
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetLastInputInfo(ref LastInputInfo info);
    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int IsWindowVisible(nint window);
    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetCurrentThreadId();
}
