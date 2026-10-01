[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$WindowHandle,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$RequireMediaPixels,
    [switch]$PassiveTopmost
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('WidgetRailReadOnlyCapture' -as [type])) {
    # HWNDs are borrowed. These declarations allocate no native handles.
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WidgetRailReadOnlyCapture {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int IsIconic(IntPtr window);
    [DllImport("user32.dll", SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);
    public static uint Verify(uint expected, IntPtr window, bool passiveTopmost) {
        GetWindowThreadProcessId(window, out uint owner);
        if (owner != expected || IsWindowVisible(window) == 0 || IsIconic(window) != 0)
            throw new InvalidOperationException("The owned test window is unavailable.");
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint active);
        // Native popups may have their own top-level HWND. Accept only the
        // process or root owner already admitted by this explicit test target.
        // A passive pin must render while another process owns foreground.
        // Admit only an explicitly requested, owned, topmost/noactivate peer.
        const long passiveMask = 0x08000008;
        bool passive = passiveTopmost && (GetWindowLongPtrW(window, -20).ToInt64() & passiveMask) == passiveMask;
        if (passiveTopmost && !passive) throw new InvalidOperationException("The target is not a passive topmost peer.");
        if (!passive && active != expected && GetAncestor(foreground, 3) != window)
            throw new InvalidOperationException("The test is not foreground: foreground PID " + active + ", expected " + expected + ".");
        return active;
    }
}
'@
}
$window = [IntPtr]$(if ($WindowHandle.StartsWith('0x')) {
    [Convert]::ToInt64($WindowHandle.Substring(2), 16)
} else { [long]::Parse($WindowHandle, [Globalization.CultureInfo]::InvariantCulture) })
$process = Get-Process -Id $AppPid
if ($process.ProcessName -ne 'OverlayFrontend.WinUI') { throw 'The capture target is not a WinUI test frontend.' }
$created = $process.StartTime.ToUniversalTime()
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) { throw 'Use a fresh capture path.' }
$null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
$priorDpi = [WidgetRailReadOnlyCapture]::SetThreadDpiAwarenessContext([IntPtr](-4))
if ($priorDpi -eq [IntPtr]::Zero) { throw 'Could not establish physical-pixel capture coordinates.' }
try {
    $foregroundProcess = [WidgetRailReadOnlyCapture]::Verify([uint32]$AppPid, $window, $PassiveTopmost.IsPresent)
    $rectangle = [WidgetRailReadOnlyCapture+Rect]::new()
    $origin = [WidgetRailReadOnlyCapture+Point]::new()
    if ([WidgetRailReadOnlyCapture]::GetClientRect($window, [ref]$rectangle) -eq 0 -or
        [WidgetRailReadOnlyCapture]::ClientToScreen($window, [ref]$origin) -eq 0) { throw 'Could not read the owned client rectangle.' }
    $width = $rectangle.Right - $rectangle.Left
    $height = $rectangle.Bottom - $rectangle.Top
    if ($width -le 0 -or $height -le 0 -or [long]$width * $height -gt 16777216) { throw 'Capture extent is outside the test bound.' }
    $bitmap = [Drawing.Bitmap]::new($width, $height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($origin.X, $origin.Y, 0, 0, [Drawing.Size]::new($width, $height)); $capturedAt = [DateTime]::UtcNow }
        finally { $graphics.Dispose() }
        $null = [WidgetRailReadOnlyCapture]::Verify([uint32]$AppPid, $window, $PassiveTopmost.IsPresent)
        if ((Get-Process -Id $AppPid).StartTime.ToUniversalTime() -ne $created) { throw 'The process identity changed during capture.' }
        $green = 0
        if ($RequireMediaPixels) {
            # Unique sealed-fixture button color (#aade55), sampled on a 2px grid.
            # No web capture API or forced XAML raster is involved.
            for ($y=0; $y -lt $height; $y+=2) {
                for ($x=0; $x -lt $width; $x+=2) {
                    $pixel=$bitmap.GetPixel($x,$y)
                    if ([Math]::Abs([int]$pixel.R-170) -le 8 -and [Math]::Abs([int]$pixel.G-222) -le 8 -and
                        [Math]::Abs([int]$pixel.B-85) -le 8) { $green++ }
                }
            }
        }
        $bitmap.Save($OutputPath, [Drawing.Imaging.ImageFormat]::Png)
        $evidence=[ordered]@{processId=$AppPid; processCreated=$created; capturedAtUtc=$capturedAt; window=$WindowHandle; foregroundProcessId=$foregroundProcess;
            x=$origin.X; y=$origin.Y; width=$width; height=$height; mediaPixelSamples=$green; passiveTopmost=$PassiveTopmost.IsPresent; changesFocus=$false}
        $evidence | ConvertTo-Json | Set-Content ($OutputPath + '.json')
        if ($RequireMediaPixels -and $green -lt 100) { throw 'The sealed media pixels were not visible in the captured window.' }
        [pscustomobject]$evidence
    } finally { $bitmap.Dispose() }
} finally { $null = [WidgetRailReadOnlyCapture]::SetThreadDpiAwarenessContext($priorDpi) }
