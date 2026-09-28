[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-preview/ui')
)
# Explicit --validate-window-preview fixture only. Its source window is owned by
# that process; screen capture is test evidence, never the production pixel path.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PreviewCaptureGeometry {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect bounds);
 [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr window, ref Point point);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@
$diagnostic = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/window-preview-result.json'
$windows = winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
$window = $windows | Where-Object title -Like 'WidgetRail*WinUI frontend' | Select-Object -First 1
if (-not $window) { throw 'Owned preview fixture main window not found.' }
$hwnd = $window.hwnd
$results = [Collections.Generic.List[object]]::new()

function Wait-Stage([string]$Stage) {
    $until = [DateTime]::UtcNow.AddSeconds(16)
    while ([DateTime]::UtcNow -lt $until) {
        if (Test-Path -LiteralPath $diagnostic) {
            $state = Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json
            if (-not $state.passed) { throw $state.error }
            if ($state.stage -eq $Stage) { return $state }
        }
        Start-Sleep -Milliseconds 100
    }
    throw "Fixture did not reach $Stage."
}
function Assert-Pixels([string]$Stage, [bool]$Live, $State, [bool]$DirectCapture = $false) {
    # Settles only the 100 ms panel-binding timer. WGC pixels advance on the
    # native capture owner, including while the WinUI dispatcher is blocked.
    $path = Join-Path $OutputDirectory "$Stage.png"
    if ($DirectCapture) {
        # Test-only screen read: no UIA/message dispatch during the deliberately
        # blocked app dispatcher. Captures only this fixture window's rectangle.
        $priorDpi = [PreviewCaptureGeometry]::SetThreadDpiAwarenessContext([IntPtr](-4))
        try {
            $captureBounds = [PreviewCaptureGeometry+Rect]::new()
            if (-not [PreviewCaptureGeometry]::GetWindowRect([IntPtr]$hwnd,[ref]$captureBounds)) { throw 'Capture bounds missing.' }
            $capture = [Drawing.Bitmap]::new($captureBounds.Right-$captureBounds.Left,$captureBounds.Bottom-$captureBounds.Top)
            $graphics = [Drawing.Graphics]::FromImage($capture)
            try {
                $graphics.CopyFromScreen($captureBounds.Left,$captureBounds.Top,0,0,$capture.Size)
                $capture.Save($path,[Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $capture.Dispose() }
        } finally { if ($priorDpi -ne [IntPtr]::Zero) { [PreviewCaptureGeometry]::SetThreadDpiAwarenessContext($priorDpi) | Out-Null } }
        if ((Get-Content -LiteralPath $diagnostic -Raw | ConvertFrom-Json).stage -ne 'Expiring') { throw 'Blocked-dispatcher capture missed its bounded interval.' }
    } else {
        Start-Sleep -Milliseconds 350
        winapp ui screenshot -w $hwnd --capture-screen -o $path --json | Out-File (Join-Path $OutputDirectory "$Stage-capture.json")
        if ($LASTEXITCODE -ne 0) { throw "Screenshot failed at $Stage." }
    }
    $bitmap = [Drawing.Bitmap]::new($path)
    try {
        $green = 0; $outside = 0; $orange = 0; $magenta = 0
        $bounds = [PreviewCaptureGeometry+Rect]::new()
        $origin = [PreviewCaptureGeometry+Point]::new()
        if (-not [PreviewCaptureGeometry]::GetWindowRect([IntPtr]$hwnd,[ref]$bounds) -or
            -not [PreviewCaptureGeometry]::ClientToScreen([IntPtr]$hwnd,[ref]$origin)) { throw 'Cannot resolve capture geometry.' }
        $offsetX = $origin.X - $bounds.Left; $offsetY = $origin.Y - $bounds.Top
        $left = [Math]::Floor($State.clip.x * $State.scale + $offsetX) - 2
        $top = [Math]::Floor($State.clip.y * $State.scale + $offsetY) - 2
        $right = [Math]::Ceiling(($State.clip.x + $State.clip.width) * $State.scale + $offsetX) + 2
        $bottom = [Math]::Ceiling(($State.clip.y + $State.clip.height) * $State.scale + $offsetY) + 2
        for ($y = 0; $y -lt $bitmap.Height; $y += 2) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 2) {
                $pixel = $bitmap.GetPixel($x,$y)
                if ($pixel.R -lt 8 -and [Math]::Abs([int]$pixel.G - 208) -lt 8 -and [Math]::Abs([int]$pixel.B - 96) -lt 8) {
                    $green++
                    if ($x -lt $left -or $x -gt $right -or $y -lt $top -or $y -gt $bottom) { $outside++ }
                }
                if ($pixel.R -gt 248 -and [Math]::Abs([int]$pixel.G - 160) -lt 8 -and $pixel.B -lt 8) { $orange++ }
                if ([Math]::Abs([int]$pixel.R - 106) -lt 8 -and [Math]::Abs([int]$pixel.G - 21) -lt 8 -and [Math]::Abs([int]$pixel.B - 106) -lt 8) { $magenta++ }
            }
        }
        $evidence = @{ stage=$Stage; live=$Live; scale=$State.scale; green=$green; outsideClip=$outside; orange=$orange; magenta=$magenta; stats=$State.stats }
        $evidence | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory "$Stage-pixels.json")
        if ($Live -and $green -lt 5000) { throw "${Stage}: expected GPU green capture, found $green sample pixels." }
        if (-not $Live -and $green -ne 0) { throw "${Stage}: $green stale capture pixels remain." }
        $expectedBackground = ($State.clip.width * $State.clip.height * $State.scale * $State.scale / 4 - $orange) * 0.96
        # During a blocked dispatcher the opaque GPU output is safety-blanked.
        # Once the dispatcher runs, the expired surface must detach completely.
        if (-not $Live -and -not $DirectCapture -and $magenta -lt $expectedBackground) { throw "${Stage}: retired surface did not reveal the viewport background ($magenta pixels)." }
        if ($outside -ne 0) { throw "${Stage}: $outside capture pixels escaped the WinUI clip." }
        if ($orange -lt 1000) { throw "${Stage}: XAML occluder is not above the capture surface." }
        if ($magenta -lt 500) { throw "${Stage}: viewport background missing." }
        $results.Add(@{ name="$Stage native state and visible pixels"; passed=$true; evidence=$evidence })
    } finally { $bitmap.Dispose() }
}
try {
    $state = Wait-Stage 'Live'
    Assert-Pixels 'Live' $true $state
    foreach ($entry in @(
        @('Resize',$true), @('Transform',$true), @('Hide',$false), @('Show',$true),
        @('Expire',$false), @('Resume',$true), @('Deny',$false), @('Allow',$true),
        @('Exclude',$false), @('Restore',$true), @('Device',$true),
        @('Unload',$false), @('Reload',$true), @('Budget',$true), @('Collapse',$false), @('Expand',$true), @('Viewport',$true)
    )) {
        $stage = [string]$entry[0]
        winapp ui invoke "Preview.$stage" -w $hwnd --json | Out-File (Join-Path $OutputDirectory "$stage-invoke.json")
        if ($LASTEXITCODE -ne 0) { throw "Cannot invoke $stage." }
        if ($stage -eq 'Expire') {
            $expiring = Wait-Stage 'Expiring'
            Start-Sleep -Milliseconds 2300
            Assert-Pixels 'Expire-while-blocked' $false $expiring $true
        }
        $state = Wait-Stage $stage
        Assert-Pixels $stage ([bool]$entry[1]) $state
    }
    Copy-Item -LiteralPath $diagnostic -Destination (Join-Path $OutputDirectory 'fixture-result.json')
} catch {
    $results.Add(@{ name='Native preview fixture'; passed=$false; error=$_.ToString() })
    throw
} finally {
    $results | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'results.json')
}
"Window previews: $($results.Count) state/pixel stages passed."
