[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh indexed-entry evidence directory.' }
if (Get-Process -Name OverlayFrontend.WinUI -ErrorAction SilentlyContinue) { throw 'Close the current candidate before native entry qualification.' }
$null = New-Item -ItemType Directory -Path $output
$pipe = 'indexed-validation-' + [guid]::NewGuid().ToString('N')
$resultPath = Join-Path $output 'result.json'
$server = Start-Process -FilePath (Join-Path $root 'tests/WidgetBridge.Tests/bin/Debug/net8.0-windows10.0.19041.0/win-x64/WidgetBridge.Tests.exe') `
    -ArgumentList @('--serve-indexed-validation', $pipe) -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $output 'server.stdout.log') -RedirectStandardError (Join-Path $output 'server.stderr.log')
$owned = 0
try {
    $launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
        --no-build --arch x64 -p Platform=x64 --detach --json `
        --args "--indexed-validation-pipe=$pipe --validation-platform-activation --indexed-entry-validation=`"$resultPath`"" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Indexed entry fixture failed to launch.' }
    $owned = [int]$launch.ProcessId
    $launch | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
    $result = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(70)
    do {
        if (Test-Path -LiteralPath $resultPath) {
            try { $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json } catch { }
            if ($result) { break }
        }
        if (-not (Get-Process -Id $owned -ErrorAction SilentlyContinue)) { throw 'Entry fixture exited before publishing a result.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $result -or $result.passed -ne $true -or $result.checks.Count -lt 25) { throw "Indexed entry failed: $($result.error)" }
    "Passed $($result.checks.Count) native indexed-entry checks."
} finally {
    if ($owned -gt 0 -and (Get-Process -Id $owned -ErrorAction SilentlyContinue)) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $owned
        Wait-Process -Id $owned -Timeout 15 -ErrorAction SilentlyContinue
    }
    if (-not $server.WaitForExit(3000)) { $server.Kill($true); $server.WaitForExit() }
    $server.Dispose()
}
