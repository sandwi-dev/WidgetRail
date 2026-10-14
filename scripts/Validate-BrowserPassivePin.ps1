[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Frontend,
    [Parameter(Mandatory)][string]$ShellConfig,
    [Parameter(Mandatory)][string]$OutputPath,
    [switch]$Radial,
    [switch]$Popup,
    [string]$MatteSource,
    # Explicit opt-in: transparent pixels can expose another application.
    # Use only with permission to capture the pin's actual screen rectangle.
    [switch]$CapturePinScreenRectangle,
    [ValidateRange(1, 10)][int]$Cycles = 1
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$frontendPath = (Resolve-Path -LiteralPath $Frontend).Path
$configPath = (Resolve-Path -LiteralPath $ShellConfig).Path
$result = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath (Split-Path $result)) {
    $prefix = [IO.Path]::GetFileName($result)
    if (Get-ChildItem -LiteralPath (Split-Path $result) -File | Where-Object { $_.Name.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) }) {
        throw 'Use a fresh result path, including sidecars from interrupted runs.'
    }
}
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($config.InitialWidgetId -ne 'webapp-a') { throw 'Use the isolated synthetic webapp shell fixture.' }
New-Item -ItemType Directory -Path (Split-Path $result) -Force | Out-Null
$null = Get-Command winapp -ErrorAction Stop
Add-Type -AssemblyName System.Drawing
$matteProcess = $null
$process = $null
try {
if ($MatteSource) {
    $matteExe = (Resolve-Path -LiteralPath $MatteSource).Path
    $matteReady = "$result.matte-ready"
    $matteProcess = Start-Process -FilePath $matteExe -WindowStyle Hidden -PassThru -ArgumentList @('"WidgetRail synthetic backdrop"', ('"{0}"' -f $matteReady), '--matte')
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while (!(Test-Path -LiteralPath $matteReady) -and !$matteProcess.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (!(Test-Path -LiteralPath $matteReady)) { throw 'Synthetic backdrop did not open.' }
}
$arguments = @(('--shell-config="{0}"' -f $configPath), '--shell-no-controller', '--validation-platform-activation',
    ('--validate-webapp-shell="{0}"' -f $result), '--webapp-passive-pin', '--webapp-pin-raster')
if ($Radial) { $arguments += '--webapp-passive-pin-radial' }
if ($Popup) { $arguments += '--webapp-passive-pin-popup' }
if ($Cycles -gt 1) { $arguments += "--webapp-pin-cycles=$Cycles" }
$process = Start-Process -FilePath $frontendPath -WorkingDirectory (Split-Path $frontendPath) -WindowStyle Hidden -PassThru -ArgumentList $arguments
$process.Id | Set-Content "$result.pid"
$rasters = @()
    $observations = foreach ($cycle in 1..$Cycles) {
        foreach ($stage in @('passive','passive-hidden','passive-reopened','interactive','passive-after-input')) {
            [pscustomobject]@{ Cycle=$cycle; Stage=$stage; Prefix=$(if ($Cycles -eq 1) { "$result.$stage" } else { "$result.cycle-$cycle.$stage" }) }
        }
    }
    foreach ($observation in $observations) {
        $stage = $observation.Stage
        $stagePrefix = $observation.Prefix
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        $statePath = "$stagePrefix.json"
        while (!(Test-Path -LiteralPath $statePath)) {
            if ((Test-Path -LiteralPath $result) -or $process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "Stopped before $stage. Result: $result" }
            Start-Sleep -Milliseconds 100
        }
        # The fixture file can briefly be visible before the asynchronous write completes.
        $state = $null
        for ($attempt = 0; $attempt -lt 20 -and !$state; $attempt++) {
            try { $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
            catch { Start-Sleep -Milliseconds 50 }
        }
        if (!$state) { throw "Incomplete observation: $stage" }
        if ($CapturePinScreenRectangle) {
            & "$PSScriptRoot/Capture-WinUiTestWindow.ps1" -AppPid $process.Id -WindowHandle ([string]$state.hwnd) `
                -OutputPath "$stagePrefix.screen.png" -PassivePeer:(!$state.interactive)
        }
        elseif ($stage -eq 'passive-hidden' -and $matteProcess) {
            $coverage = Get-Content -LiteralPath $matteReady -Raw | ConvertFrom-Json
            if ($matteProcess.HasExited -or $state.bounds.X -lt $coverage.X -or $state.bounds.Y -lt $coverage.Y -or
                $state.bounds.X + $state.bounds.Width -gt $coverage.X + $coverage.Width -or
                $state.bounds.Y + $state.bounds.Height -gt $coverage.Y + $coverage.Height) { throw 'The synthetic backdrop does not cover this pin.' }
            # The fixture's full-display opaque window was created before the pin;
            # this bounded crop therefore contains only owned synthetic content.
            $screen = [Drawing.Bitmap]::new([int]$state.bounds.Width, [int]$state.bounds.Height)
            try {
                $graphics = [Drawing.Graphics]::FromImage($screen)
                try { $graphics.CopyFromScreen([int]$state.bounds.X, [int]$state.bounds.Y, 0, 0, $screen.Size) }
                finally { $graphics.Dispose() }
                $screen.Save("$stagePrefix.screen.png", [Drawing.Imaging.ImageFormat]::Png)
            } finally { $screen.Dispose() }
        }
        & winapp ui screenshot -w $state.hwnd -o "$stagePrefix.png" --json
        if ($LASTEXITCODE -ne 0) { throw 'Native screenshot failed.' }
        $bitmap = [Drawing.Bitmap]::new("$stagePrefix.png")
        try {
            $green = 0; $total = 0
            # Sample the synthetic solid-color page interior, away from text,
            # native controls, rounded edges and the interactive pointer circle.
            for ($y = 3; $y -le 6; $y++) {
                for ($x = 2; $x -le 7; $x++) {
                    $pixel = $bitmap.GetPixel([int]($bitmap.Width * $x / 10), [int]($bitmap.Height * $y / 10))
                    $total++
                    if ($pixel.R -lt 30 -and $pixel.G -gt 90 -and $pixel.B -gt 40 -and $pixel.B -lt 100) { $green++ }
                }
            }
            $rasters += [pscustomobject]@{ cycle=$observation.Cycle; stage=$stage; green=$green; total=$total; passed=($green / $total -ge .9) }
        }
        finally { $bitmap.Dispose() }
        New-Item -ItemType File -Path "$stagePrefix.captured" | Out-Null
    }
    $rasters | ConvertTo-Json | Set-Content "$result.rasters.json"
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while (!(Test-Path -LiteralPath $result) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    $summary = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if (!$summary.passed -or @($rasters | Where-Object { !$_.passed }).Count) { throw "Passive pin validation failed; inspect $result and its rasters." }
    Write-Output "PASS $($rasters.Count) native pin rasters and $($summary.checks.Count) lifecycle checks ($result)."
}
finally {
    if ($process -and !$process.HasExited) {
        & "$PSScriptRoot/Close-WinUiTestShell.ps1" -AppPid $process.Id
        Wait-Process -Id $process.Id -Timeout 20
    }
    if ($matteProcess -and !$matteProcess.HasExited) {
        $null = $matteProcess.CloseMainWindow()
        Wait-Process -Id $matteProcess.Id -Timeout 20
    }
}
