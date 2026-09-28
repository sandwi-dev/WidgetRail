param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/radial/matrix'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$null = New-Item -ItemType Directory -Force $OutputDirectory
$results = [Collections.Generic.List[object]]::new()
Add-Type -AssemblyName System.Drawing
function Test-IconPixels([string]$Path) {
    $bitmap = [Drawing.Bitmap]::new((Resolve-Path $Path).Path)
    try {
        # Centers derive from the authored ten-cell native Grid, including face/slot margins.
        foreach ($position in @(@(5,1),@(8,2),@(9,5),@(8,8),@(5,9),@(2,8),@(1,5),@(2,2))) {
            $cx = (20 + $position[0] * 36) * $bitmap.Width / 400
            $cy = (20 + $position[1] * 36) * $bitmap.Height / 400
            $radius = [Math]::Max(3,[int](12 * $bitmap.Width / 400))
            $ink = 0
            for ($y=[int]$cy-$radius; $y -lt [int]$cy+$radius; $y++) {
                for ($x=[int]$cx-$radius; $x -lt [int]$cx+$radius; $x++) {
                    $pixel=$bitmap.GetPixel($x,$y)
                    if ($pixel.R -gt 100 -and $pixel.G -gt 100 -and $pixel.B -gt 100) { $ink++ }
                }
            }
            if ($ink -lt 8) { return $false }
        }
        return $true
    } finally { $bitmap.Dispose() }
}
foreach ($case in @(@{Scale='1';Text='1'},@{Scale='1.25';Text='1'},@{Scale='0.5';Text='1'},@{Scale='1.25';Text='2'})) {
    $name = "scale-$($case.Scale)-text-$($case.Text)"
    $start = [DateTime]::UtcNow
    $launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json --args "--validate-production-shell --radial-fixture-scale=$($case.Scale) --radial-fixture-text-scale=$($case.Text)" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Radial native fixture failed to launch.' }
    $ownedProcess = [int]$launch.ProcessId
    try {
        $path = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/production-shell-result.json'
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        $result = $null
        do {
            $file = Get-Item $path -ErrorAction SilentlyContinue
            if ($file -and $file.LastWriteTimeUtc -ge $start) {
                $result = Get-Content $path -Raw | ConvertFrom-Json
                if ($result.result -in @('passed','failed')) { break }
            }
            Start-Sleep -Milliseconds 100
        } while ([DateTime]::UtcNow -lt $deadline)
        $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory "$name.json")
        if ($result.result -ne 'passed' -or $result.checks -notcontains 'reduced motion disables radial entrance animation') { throw "Radial fixture failed at ${name}: $($result.error)" }
        $shot = Join-Path $OutputDirectory "$name.png"
        $pixelsReady = $false
        for ($attempt=0; $attempt -lt 6; $attempt++) {
            winapp ui screenshot Overlay.Radial -a $ownedProcess -o $shot --json | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Radial screenshot failed.' }
            if (Test-IconPixels $shot) { $pixelsReady=$true; break }
            Start-Sleep -Milliseconds 250
        }
        if (!$pixelsReady) { throw "Radial icon pixels did not become visible in ${name}." }
        $results.Add(@{ case=$name; passed=$true; nativeChecks=$result.checks.Count; allEightIconPixels=$true })
    } finally {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedProcess
        Wait-Process -Id $ownedProcess -Timeout 10 -ErrorAction SilentlyContinue
    }
}
$results | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'results.json')
"Radial native matrix: $($results.Count) cases passed."
