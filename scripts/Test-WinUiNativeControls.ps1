[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-native-controls'),
    [ValidateSet('select','text-entry','slider','context-menu','embedded-media')][string[]]$Fixture,
    [switch]$IncludeMedia
)
$ErrorActionPreference = 'Stop'
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
if ($Fixture) { $cases = @($cases | Where-Object { $Fixture -contains ($_.Flag -replace '^--validate-', '') }) }
$summary = [Collections.Generic.List[object]]::new()
# Requires an analyzer-built x64 Debug frontend and exclusive package deployment.
# Every fixture is host-owned; none creates a physical controller reader.
try {
    foreach ($case in $cases) {
        $started = [DateTime]::UtcNow
        $raw = & winapp run $project --no-build --arch x64 -p Platform=x64 --detach --json --args $case.Flag
        if ($LASTEXITCODE -ne 0) { throw "Failed to launch $($case.Flag): $raw" }
        $launch = ($raw -join "`n") | ConvertFrom-Json
        $ownedPid = [int]$launch.ProcessId
        if ($ownedPid -le 0) { throw 'Launcher did not return an owned process ID.' }
        $launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory "$($case.Flag.TrimStart('-'))-launch.json")
        try {
            $resultPath = Join-Path $diagnostics $case.File
            $deadline = [DateTime]::UtcNow.AddSeconds(45)
            $result = $null
            do {
                $file = Get-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
                if ($file -and $file.LastWriteTimeUtc -ge $started) {
                    try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { }
                    if ($result.result -in @('passed', 'failed') -or
                        ($case.Media -and ($result.passed -eq $false -or $result.phase -eq 'complete'))) { break }
                }
                if (-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Validation process exited before its result.' }
                Start-Sleep -Milliseconds 150
            } while ([DateTime]::UtcNow -lt $deadline)
            if ($null -ne $result) { $result | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory $case.File) }
            & winapp ui screenshot -a $ownedPid --capture-screen -o (Join-Path $OutputDirectory "$($case.Flag.TrimStart('-')).png") --json | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not capture native control pixels.' }
            $passed = $result.result -eq 'passed' -or ($case.Media -and $result.passed -eq $true -and $result.phase -eq 'complete')
            if (-not $passed -or @($result.checks).Count -eq 0) {
                throw "Fixture failed or timed out: $($case.Flag): $($result.error)"
            }
            $summary.Add(@{ fixture=$case.Flag; result='passed'; checks=@($result.checks).Count })
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
