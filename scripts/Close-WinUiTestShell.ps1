[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid)
$ErrorActionPreference = 'Stop'
# A real WM_CLOSE uses MainWindow.AppWindow.Closing and its owned shutdown path.
# Production intentionally has no debug Close button. Target only the supplied PID.
if (-not ('WidgetRailTestWindowClose' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class WidgetRailTestWindowClose {
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    public static IntPtr FindMain(uint expectedProcess) {
        IntPtr found=IntPtr.Zero; int count=0;
        EnumWindows((window, parameter) => {
            GetWindowThreadProcessId(window, out uint process);
            if (process != expectedProcess) return true;
            var title=new StringBuilder(256);
            GetWindowTextW(window,title,title.Capacity);
            if (title.ToString()=="WidgetRail — WinUI frontend") {found=window;count++;}
            return true;
        },IntPtr.Zero);
        if(count!=1) throw new InvalidOperationException("Expected exactly one owned WidgetRail frontend window.");
        return found;
    }
}
"@
}
# EnumWindows includes a normally hidden overlay, which UIA's visible-window
# inventory intentionally omits after a successful gap/Guide dismissal.
$handle = [WidgetRailTestWindowClose]::FindMain([uint32]$AppPid)
[uint32]$verifiedProcess = 0
$null = [WidgetRailTestWindowClose]::GetWindowThreadProcessId($handle, [ref]$verifiedProcess)
if ($verifiedProcess -ne $AppPid) { throw 'Window owner changed before shutdown.' }
if (-not [WidgetRailTestWindowClose]::PostMessageW($handle, 0x0010, [UIntPtr]::Zero, [IntPtr]::Zero)) {
    throw 'The owned window did not accept WM_CLOSE.'
}

