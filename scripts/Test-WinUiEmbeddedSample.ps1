[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ShellConfig,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $OutputDirectory) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory $OutputDirectory | Out-Null
$resultPath = Join-Path $OutputDirectory 'result.json'
$ownedPid = 0
try {
    $raw = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json `
        --args "--shell-config=$([IO.Path]::GetFullPath($ShellConfig)) --shell-no-controller --validation-platform-activation --validate-embedded-sample=$resultPath"
    if ($LASTEXITCODE -ne 0) { throw "Sample launch failed: $raw" }
    $launch = ($raw -join "`n") | ConvertFrom-Json
    $ownedPid = [int]$launch.ProcessId
    $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
    $captured = @{}
    $pixelFailures = @()
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    $result = $null
    do {
        if (Test-Path $resultPath) {
            try { $result = Get-Content $resultPath -Raw | ConvertFrom-Json } catch { $result = $null }
            if ($result.phase -and !$captured.ContainsKey($result.phase)) {
                & (Join-Path $PSScriptRoot 'Capture-WinUiTestWindow.ps1') -AppPid $ownedPid -WindowHandle ([string]$result.peerHwnd) `
                    -OutputPath (Join-Path $OutputDirectory ($result.phase + '.png')) | Out-Null
                # Both sealed local videos contain a large bright yellow marker.
                # A live document/visible HWND alone does not prove video pixels.
                Add-Type -AssemblyName System.Drawing
                $bitmap = [Drawing.Bitmap]::new((Join-Path $OutputDirectory ($result.phase + '.png')))
                try {
                    $markers = 0
                    for ($y=0; $y -lt $bitmap.Height; $y+=2) {
                        for ($x=0; $x -lt $bitmap.Width; $x+=2) {
                            $pixel=$bitmap.GetPixel($x,$y)
                            if ($pixel.R -gt 180 -and $pixel.G -gt 170 -and $pixel.B -lt 130) { ++$markers }
                        }
                    }
                    if ($markers -lt 200) { $pixelFailures += $result.phase }
                } finally { $bitmap.Dispose() }
                $captured[$result.phase] = $true
            }
            if ($result -and $result.PSObject.Properties.Name -contains 'passed') { break }
        }
        if (!(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Sample frontend exited before completion.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$result -or !$result.passed) { throw "Sample workflow failed or timed out. See $resultPath" }
    if (!$captured.ContainsKey('compact-passive') -or !$captured.ContainsKey('compact-interactive')) { throw 'Missing compact media pixel evidence.' }
    if ($pixelFailures.Count -gt 0) { throw "Local video marker missing in $($pixelFailures -join ', ')." }
    winapp ui screenshot Widget.media-shell.root -a $ownedPid -o (Join-Path $OutputDirectory 'sample.png') --json | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Sample screenshot failed.' }
    "$($result.checks.Count) actual sample checks passed."
} finally {
    if ($ownedPid -gt 0 -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
    }
}
