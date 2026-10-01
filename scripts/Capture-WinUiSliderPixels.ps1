[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dir = [IO.Path]::GetFullPath($OutputDirectory)
if (Get-Process -Name OverlayFrontend.WinUI -ErrorAction SilentlyContinue) { throw 'The pixel fixture requires an exclusive frontend slot.' }
New-Item -ItemType Directory -Path $dir -ErrorAction Stop | Out-Null
$launch = (& winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json --args "--validate-slider --slider-pixels=$dir" | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'Launch failed' }
$ownedPid = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content "$dir/launch.json"
try {
    winapp ui wait-for Shell.Close -a $ownedPid -t 10000 --json | Out-Null
    $window = @(& winapp ui list-windows -a $ownedPid --json | ConvertFrom-Json | Where-Object title -Like 'WidgetRail*WinUI frontend')[0]
    $seen = [Collections.Generic.HashSet[string]]::new()
    $deadline = [DateTime]::UtcNow.AddSeconds(100)
    while (!(Test-Path "$dir/complete.json") -and [DateTime]::UtcNow -lt $deadline) {
        if (Test-Path "$dir/checkpoint.json") {
            $checkpoint = Get-Content "$dir/checkpoint.json" -Raw | ConvertFrom-Json
            if ($seen.Add($checkpoint.phase)) {
                $phase = $checkpoint.phase
                Copy-Item "$dir/checkpoint.json" "$dir/$phase.json"
                & winapp ui screenshot -w $window.hwnd --capture-screen -o "$dir/$phase.png" --json
                if ($LASTEXITCODE -ne 0) { throw 'Capture failed' }
                Set-Content "$dir/$phase.continue" ''
            }
        }
        Start-Sleep -Milliseconds 150
    }
    if ($seen.Count -ne 12) { throw 'Expected twelve endpoint/engagement/scale screen captures.' }
    if (!(Test-Path "$dir/complete.json")) { throw 'Timed out' }
} finally {
    & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
}
