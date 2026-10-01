[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$FixturePath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh style evidence directory.' }
if (Get-Process -Name 'OverlayFrontend.WinUI' -ErrorAction SilentlyContinue) { throw 'The style fixture requires an exclusive frontend slot.' }
New-Item -ItemType Directory -Path $output | Out-Null
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$arguments = '--validate-styles --playnite-layout-only --playnite-layout-fixture="' + $fixture + '"' 
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
} finally {
    if (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedApp
        Wait-Process -Id $ownedApp -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedApp -ErrorAction SilentlyContinue) { throw 'Style fixture did not finish normal shutdown.' }
    }
}
