[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$testExe = Join-Path $repository "tests/DisplayProfilesWidget.Tests/bin/$Configuration/net8.0-windows/DisplayProfilesWidget.Tests.exe"
$bridge = Join-Path $repository "src/OverlayHost/out/$Configuration/runtime/Bridge/WidgetBridge.exe"
if (!(Test-Path -LiteralPath $testExe) -or !(Test-Path -LiteralPath $bridge)) {
    throw 'Build the display-profile tests and the complete overlay before running the guard check.'
}

function Start-GuardTest([string]$Mode) {
    $start = [Diagnostics.ProcessStartInfo]::new($testExe)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.ArgumentList.Add($Mode)
    $start.ArgumentList.Add($bridge)
    return [Diagnostics.Process]::Start($start)
}

$probe = Start-GuardTest '--guard-probe'
try {
    if (!$probe.WaitForExit(15000)) { throw 'Display helper startup check timed out.' }
    $stdout = $probe.StandardOutput.ReadToEnd()
    $stderr = $probe.StandardError.ReadToEnd()
    if ($probe.ExitCode -ne 0) { throw "Display helper startup failed: $stderr" }
    Write-Output $stdout.Trim()
} finally {
    if (!$probe.HasExited) { $probe.Kill(); $null = $probe.WaitForExit(5000) }
    $probe.Dispose()
}

$parent = Start-GuardTest '--guard-parent-exit'
$guard = $null
try {
    $line = $parent.StandardOutput.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
    if (!$line -or $line -notmatch '^\d+$') { throw 'Guard parent did not report a verified child.' }
    $guard = Get-Process -Id ([int]$line) -ErrorAction Stop
    $null = $guard.Handle # Keep its exact lifetime open before releasing the parent.
    $parent.StandardInput.WriteLine('exit')
    $parent.StandardInput.Flush()
    if (!$parent.WaitForExit(5000) -or !$guard.WaitForExit(10000)) { throw 'Guard did not finish after parent exit.' }
    if ($parent.ExitCode -ne 0 -or $guard.ExitCode -ne 2) { throw 'Guard was terminated instead of handling parent EOF.' }
    Write-Output 'PASS guard survived abrupt parent exit and handled EOF itself. No display configuration was sent.'
} finally {
    if (!$parent.HasExited) { $parent.Kill(); $null = $parent.WaitForExit(5000) }
    if ($guard) { $guard.Dispose() }
    $parent.Dispose()
}
