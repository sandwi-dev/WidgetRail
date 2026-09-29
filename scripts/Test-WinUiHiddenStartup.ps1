[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeInstallation,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$BridgeInstallation = (Resolve-Path -LiteralPath $BridgeInstallation).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory to isolate startup evidence and profile.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$profile = Join-Path $OutputDirectory 'profile'
$resultPath = Join-Path $OutputDirectory 'result.json'
$launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json `
    --args "--hidden --installation-root=`"$BridgeInstallation`" --settings-root=`"$profile`" --validate-hidden-startup=`"$resultPath`"" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Hidden startup fixture did not launch.' }
$ownedProcess = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $OutputDirectory 'launch.json')
$childProcesses = @()
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(55)
    $result = $null
    do {
        if (Test-Path -LiteralPath $resultPath) {
            try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { $result = $null }
            if ($result -and $result.pid -eq $ownedProcess) { break }
        }
        if (-not (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue)) { throw 'Hidden startup fixture exited before publishing its result.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $result -or $result.pid -ne $ownedProcess) { throw 'Hidden startup fixture did not finish.' }
    if (-not $result.passed -or @($result.checks).Count -ne 7) { throw "Hidden startup failed: $($result.error)" }
    $childProcesses = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ownedProcess" |
        Where-Object Name -eq 'WidgetBridge.exe' | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction Stop })
    if ($childProcesses.Count -ne 1) { throw 'Expected exactly one Bridge after first show and reopen.' }
    "Hidden startup native checks passed: $(@($result.checks).Count)."
} finally {
    if (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedProcess
        Wait-Process -Id $ownedProcess -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $ownedProcess -ErrorAction SilentlyContinue) { throw 'Hidden startup fixture did not finish normal shutdown.' }
    }
    foreach ($child in $childProcesses) {
        if (-not $child.WaitForExit(5000)) { throw 'Owned Bridge survived frontend shutdown.' }
        $child.Dispose()
    }
}
