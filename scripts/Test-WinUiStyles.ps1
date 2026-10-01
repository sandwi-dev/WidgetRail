[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [switch]$ArtworkOnly, [switch]$NavigationOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh style evidence directory.' }
if (Get-Process -Name 'OverlayFrontend.WinUI' -ErrorAction SilentlyContinue) { throw 'The style fixture requires an exclusive frontend slot.' }
New-Item -ItemType Directory -Path $output | Out-Null
$arguments = '--validate-styles' + $(if ($ArtworkOnly) { ' --artwork-only' } else { '' }) + $(if ($NavigationOnly) { ' --directional-navigation-only' } else { '' })
$launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json --args $arguments | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Style fixture did not launch.' }
$ownedApp = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $result = $null
    do {
        $raw = winapp ui get-property Styles.Status -a $ownedApp -p Name --json 2>$null
        if ($LASTEXITCODE -eq 0) {
            $result = $raw | ConvertFrom-Json
            if ($result.properties.Name -match '^(PASS|FAIL):') { break }
        }
        if (-not (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue)) { throw 'Style fixture exited before publishing a result.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($result) { $result | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $output 'result.json') }
    if (-not $result -or $result.properties.Name -notmatch '^PASS: (\d+) native style checks') {
        throw "Native style fixture failed or timed out: $($result.properties.Name)"
    }
    "Passed $($Matches[1]) native style checks."
    if ($NavigationOnly) {
        # The native fixture leaves the responsive grid mounted. Exercise actual
        # keyboard routing too: preview handling must prevent a second WinUI move.
        foreach ($step in @(@('Widget.tile.3','Up','Widget.tile.0'), @('Widget.tile.0','Down','Widget.tile.3'), @('Widget.tile.2','Right','Widget.tile.2'))) {
            winapp ui focus $step[0] -a $ownedApp --json | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not focus the owned keyboard fixture.' }
            winapp ui send-keys $step[1] -a $ownedApp --via send-input --json | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not send the owned keyboard fixture key.' }
            $focus = winapp ui get-property $step[2] -a $ownedApp -p HasKeyboardFocus --json | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0 -or $focus.properties.HasKeyboardFocus -ne 'True') { throw "Keyboard $($step[1]) selected an unexpected target." }
        }
        'Passed 3 native keyboard navigation checks.'
    }
} finally {
    if (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedApp
        Wait-Process -Id $ownedApp -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue) { throw 'Style fixture did not finish normal shutdown.' }
    }
}
