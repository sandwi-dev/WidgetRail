[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a fresh modal evidence directory.' }
if (Get-Process -Name OverlayFrontend.WinUI -ErrorAction SilentlyContinue) { throw 'Close the current candidate before modal qualification.' }
$null = New-Item -ItemType Directory -Path $output
$started = [DateTime]::UtcNow
$launch = winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') `
    --no-build --arch x64 -p Platform=x64 --detach --json --args '--validate-modals' | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0) { throw 'Modal fixture failed to launch.' }
$owned = [int]$launch.ProcessId
$launch | ConvertTo-Json | Set-Content (Join-Path $output 'launch.json')
try {
    $path = Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/modal-controls-result.json'
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    $result = $null
    do {
        $file = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($file -and $file.LastWriteTimeUtc -ge $started) {
            try { $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json } catch { }
            if ($result.result -in @('passed', 'failed')) { break }
        }
        if (-not (Get-Process -Id $owned -ErrorAction SilentlyContinue)) { throw 'Modal fixture exited before its result.' }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    $result | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $output 'result.json')
    winapp ui inspect -a $owned --depth 16 --json | Set-Content (Join-Path $output 'automation-tree.json')
    winapp ui screenshot -a $owned --capture-screen -o (Join-Path $output 'modal.png') --json | Out-Null
    if (-not $result -or $result.result -ne 'passed' -or
        $result.checks -notcontains 'native modal automation exposes the dialog without inactive parent controls' -or
        $result.checks -notcontains 'native automation restores the parent while excluding the closing dialog') {
        throw "Modal workflow failed: $($result.error)"
    }
    "Passed $($result.checks.Count) native modal checks."
} finally {
    if (Get-Process -Id $owned -ErrorAction SilentlyContinue) {
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $owned
        Wait-Process -Id $owned -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $owned -ErrorAction SilentlyContinue) { throw 'Modal fixture did not close normally.' }
    }
}
