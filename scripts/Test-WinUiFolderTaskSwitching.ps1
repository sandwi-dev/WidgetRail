[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ShellConfig,
    [Parameter(Mandatory)][string]$TargetsPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$result = Join-Path $output 'result.json'
$signal = $result + '.input'
$targets = (Resolve-Path -LiteralPath $TargetsPath).Path
$config = (Resolve-Path -LiteralPath $ShellConfig).Path
$helper = $null
$ownedPid = 0
try {
    $helper = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList @(
        '-NoProfile', '-File', "`"$PSScriptRoot/Send-WinUiTaskCycleInput.ps1`"",
        '-SignalPath', "`"$signal`"", '-TargetsPath', "`"$targets`"", '-ReplaceForegroundGrant') `
        -RedirectStandardError (Join-Path $output 'input-helper.err')
    $arguments = "--shell-config=`"$config`" --shell-no-controller --validation-platform-activation --validate-task-cycle=`"$result`" --task-cycle-targets=`"$targets`" --task-cycle-folder-input"
    $launch = winapp run (Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -c Release --arch x64 -p Platform=x64 --no-build --detach --json --args $arguments | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Folder handoff fixture launch failed.' }
    $ownedPid = [int]$launch.ProcessId
    $until = [DateTime]::UtcNow.AddSeconds(40)
    while (!(Test-Path -LiteralPath $result)) {
        if ($helper.HasExited) { throw "Input helper exited; inspect $output/input-helper.err." }
        if (!(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) { throw 'Fixture exited before producing evidence.' }
        if ([DateTime]::UtcNow -ge $until) { throw 'Folder handoff fixture timed out.' }
        Start-Sleep -Milliseconds 100
    }
    $evidence = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if (!$evidence.passed) { throw $evidence.error }
    $switches = @($evidence.checks | Where-Object { $_ -like 'Switch * activated HWND *' })
    $denied = @($evidence.trace | Where-Object { $_ -like '*allowed=False*' -or $_ -like '*code=windows-denied*' })
    if ($switches.Count -ne 6 -or $denied.Count) { throw 'Six fresh, successful foreground handoffs are required.' }
    'PASS: folder launch followed by six window switches, with the previous broker grant replaced after every switch.'
} finally {
    [IO.File]::WriteAllText($signal + '.stop', 'stop')
    if ($helper) { [void]$helper.WaitForExit(5000); $helper.Dispose() }
    if ($ownedPid -and (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
        $app = Get-Process -Id $ownedPid -ErrorAction SilentlyContinue
        if ($app -and !$app.WaitForExit(15000)) { throw 'Owned fixture did not close cleanly.' }
    }
}
