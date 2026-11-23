[CmdletBinding()]
param([Parameter(Mandatory)][long]$Window, [Parameter(Mandatory)][uint32]$ExpectedProcess)
$ErrorActionPreference = 'Stop'
# Read-only measurement of the generated helper's exact HWND, in physical pixels.
# No input, positioning, focus, capture, or production window mutation.
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GeneratedGeometry {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError=true)] public static extern int GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError=true)] public static extern int GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError=true)] public static extern int ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll", SetLastError=true)] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
[uint32]$actualProcess = 0
if ([GeneratedGeometry]::GetWindowThreadProcessId([IntPtr]$Window, [ref]$actualProcess) -eq 0 -or $actualProcess -ne $ExpectedProcess) {
    throw 'Generated window process identity mismatch.'
}
$previous = [GeneratedGeometry]::SetThreadDpiAwarenessContext([IntPtr](-4))
if ($previous -eq 0) { throw 'Could not establish physical-pixel measurement context.' }
try {
    $rect = [GeneratedGeometry+Rect]::new()
    if ([GeneratedGeometry]::GetWindowRect([IntPtr]$Window, [ref]$rect) -eq 0) { throw 'GetWindowRect failed.' }
    $client = [GeneratedGeometry+Rect]::new()
    $origin = [GeneratedGeometry+Point]::new()
    if ([GeneratedGeometry]::GetClientRect([IntPtr]$Window, [ref]$client) -eq 0 -or
        [GeneratedGeometry]::ClientToScreen([IntPtr]$Window, [ref]$origin) -eq 0) { throw 'Client geometry read failed.' }
    @{outer=@{x=$rect.Left;y=$rect.Top;width=$rect.Right-$rect.Left;height=$rect.Bottom-$rect.Top};
      client=@{x=$origin.X;y=$origin.Y;width=$client.Right-$client.Left;height=$client.Bottom-$client.Top}} | ConvertTo-Json -Compress
} finally {
    $null = [GeneratedGeometry]::SetThreadDpiAwarenessContext($previous)
}
