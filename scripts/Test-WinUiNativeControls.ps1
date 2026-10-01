[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-native-controls'),
    [ValidateSet('select','text-entry','slider','context-menu','embedded-media')][string[]]$Fixture,
    [switch]$IncludeMedia,
    [switch]$UsePlatformActivation,
    [switch]$ProcessFailures,
    [switch]$BehaviorOnly
)
$ErrorActionPreference = 'Stop'
if ($ProcessFailures -and (Get-Process -Name OverlayFrontend.WinUI -ErrorAction SilentlyContinue)) {
    throw 'Close the candidate before injecting failures into the isolated media environment.'
}
$root = Split-Path $PSScriptRoot
$project = Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$diagnostics = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics'
$null = New-Item -ItemType Directory -Force $OutputDirectory
$cases = @(
    @{ Flag='--validate-select'; File='select-controls-result.json' },
    @{ Flag='--validate-text-entry'; File='text-entry-result.json' },
    @{ Flag='--validate-slider'; File='slider-controls-result.json' },
    @{ Flag='--validate-context-menu'; File='context-menu-result.json' }
)
if ($IncludeMedia -or $Fixture -contains 'embedded-media') { $cases += @{ Flag='--validate-embedded-media'; File='embedded-media-result.json'; Media=$true } }
if ($BehaviorOnly -and ($IncludeMedia -or $Fixture -contains 'embedded-media')) { throw 'Media qualification requires its pixel captures.' }
if ($Fixture) { $cases = @($cases | Where-Object { $Fixture -contains ($_.Flag -replace '^--validate-', '') }) }
if ($ProcessFailures -and -not ($cases | Where-Object { $_.Media })) { throw 'Process-failure validation requires the isolated embedded-media fixture.' }
$summary = [Collections.Generic.List[object]]::new()
# Requires an analyzer-built x64 Debug frontend and exclusive package deployment.
# Fixtures are host-owned. Optional activation uses the existing native platform
# adapter and therefore requires exclusive use of that controller adapter too.
try {
    foreach ($case in $cases) {
        $started = [DateTime]::UtcNow
        $arguments = $case.Flag + $(if ($UsePlatformActivation) { ' --validation-platform-activation' } else { '' })
        if ($ProcessFailures -and $case.Media) { $arguments += ' --validate-media-process-failures' }
        $raw = & winapp run $project --no-build --arch x64 -p Platform=x64 --detach --json --args $arguments
        if ($LASTEXITCODE -ne 0) { throw "Failed to launch $($case.Flag): $raw" }
        $launch = ($raw -join "`n") | ConvertFrom-Json
        $ownedPid = [int]$launch.ProcessId
        if ($ownedPid -le 0) { throw 'Launcher did not return an owned process ID.' }
        $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$($case.Flag.TrimStart('-'))-launch.json")
        try {
            $ready = & winapp ui wait-for Shell.Close -a $ownedPid -t 8000 --json | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0 -or -not $ready.found) { throw 'Native fixture window did not become ready.' }
            $windows = & winapp ui list-windows -a $ownedPid --json | ConvertFrom-Json
            $mainWindow = @($windows | Where-Object title -Like 'WidgetRail*WinUI frontend')
            if ($mainWindow.Count -ne 1) { throw 'Could not identify the owned native fixture window.' }
            $mainHwnd = $mainWindow[0].hwnd
            $resultPaths = @((Join-Path $diagnostics $case.File))
            # Packaged development activation can virtualize LocalApplicationData.
            # Accept only a fresh result from this launch's exact package family.
            if ($launch.AUMID) {
                $family = ($launch.AUMID -split '!')[0]
                $resultPaths += Join-Path $env:LOCALAPPDATA "Packages/$family/LocalCache/Local/WidgetRail/WinUI/diagnostics/$($case.File)"
            }
            $deadline = [DateTime]::UtcNow.AddSeconds($(if ($ProcessFailures) { 90 } else { 45 }))
            $result = $null
            $capturedPhases = [Collections.Generic.HashSet[string]]::new()
            do {
                $file = $resultPaths | ForEach-Object { Get-Item -LiteralPath $_ -ErrorAction SilentlyContinue } |
                    Where-Object LastWriteTimeUtc -GE $started | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
                if ($file -and $file.LastWriteTimeUtc -ge $started) {
                    try { $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json } catch { }
                    if ($case.Media -and $result.phase -in @('owner-popup','owner-fullscreen') -and $capturedPhases.Add($result.phase)) {
                        & (Join-Path $PSScriptRoot 'Capture-WinUiTestWindow.ps1') -AppPid $ownedPid -WindowHandle $mainHwnd `
                            -OutputPath (Join-Path $OutputDirectory "$($result.phase).png") -RequireMediaPixels | Out-Null
                    }
                    if ($case.Media -and $result.phase -eq 'gpu-recovered' -and $capturedPhases.Add($result.phase)) {
                        & (Join-Path $PSScriptRoot 'Capture-WinUiTestWindow.ps1') -AppPid $ownedPid -WindowHandle $mainHwnd `
                            -OutputPath (Join-Path $OutputDirectory 'gpu-recovered.png') | Out-Null
                    }
                    if ($result.result -in @('passed', 'failed') -or
                        ($case.Media -and ($result.passed -eq $false -or $result.phase -eq 'complete'))) { break }
                }
                if (-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Validation process exited before its result.' }
                Start-Sleep -Milliseconds 150
            } while ([DateTime]::UtcNow -lt $deadline)
            if ($null -ne $result) { $result | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory $case.File) }
            if (-not $BehaviorOnly) {
                # Ordinary XAML controls can be captured from their owned
                # window without borrowing foreground from the user's desktop.
                [string[]]$captureOptions = if ($case.Media) { @('--capture-screen') } else { @() }
                & winapp ui screenshot -w $mainHwnd @captureOptions -o (Join-Path $OutputDirectory "$($case.Flag.TrimStart('-')).png") --json | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Could not capture native control pixels.' }
            }
            $passed = $result.result -eq 'passed' -or ($case.Media -and $result.passed -eq $true -and $result.phase -eq 'complete')
            if (-not $passed -or @($result.checks).Count -eq 0) {
                throw "Fixture failed or timed out: $($case.Flag): $($result.error)"
            }
            $summary.Add(@{ fixture=$case.Flag; result='passed'; checks=@($result.checks).Count; pixelsCaptured=(-not $BehaviorOnly) })
        }
        catch {
            $summary.Add(@{ fixture=$case.Flag; result='failed'; detail=$_.Exception.Message })
            throw
        }
        finally {
            if (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) {
                & winapp ui invoke Shell.Close -a $ownedPid --json | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "Could not close owned fixture $ownedPid; stop before redeploying." }
                Wait-Process -Id $ownedPid -Timeout 15 -ErrorAction SilentlyContinue
                if (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue) { throw "Owned fixture $ownedPid did not exit; stop before redeploying." }
            }
        }
    }
}
finally { $summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'results.json') }
$summary | Format-Table
