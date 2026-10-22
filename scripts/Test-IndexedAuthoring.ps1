[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('entry','reveal','continuity','widget','memento')][string]$Mode,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh evidence directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$pipe = 'indexed-authoring-' + [guid]::NewGuid().ToString('N')
$bridge = Join-Path $repository 'tests/WidgetBridge.Tests/bin/Release/net8.0-windows10.0.19041.0/win-x64/WidgetBridge.Tests.exe'
$fixtureProcess = $null
try {
    $server = Start-Process -FilePath $bridge -ArgumentList @('--serve-indexed-validation', $pipe) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$output/bridge.log" -RedirectStandardError "$output/bridge-error.log"
    $fixtureArgs = "--indexed-validation-pipe=$pipe --validation-platform-activation --indexed-authoring-regression"
    if ($Mode -notin @('widget','memento')) { $fixtureArgs += " --indexed-$Mode-validation=`"$output/result.json`"" }
    $launch = & winapp run (Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -c Release --arch x64 -p Platform=x64 --no-build --detach --json --args $fixtureArgs
    if ($LASTEXITCODE -ne 0) { throw "Native launch failed: $launch" }
    $fixtureProcess = ($launch | ConvertFrom-Json).ProcessId
    $launch | Set-Content "$output/launch.json"
    @{ Mode=$Mode; Commit=(& git -C $repository rev-parse HEAD); AppPid=$fixtureProcess; BridgePid=$server.Id;
        Frontend=(Get-FileHash (Join-Path $repository 'src/OverlayFrontend.WinUI/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/OverlayFrontend.WinUI.dll')).Hash;
        BridgeFixture=(Get-FileHash (Join-Path (Split-Path $bridge) 'WidgetBridge.Tests.dll')).Hash } | ConvertTo-Json | Set-Content "$output/provenance.json"
    if ($Mode -in @('widget','memento')) {
        & winapp ui wait-for Widget.items.Item.0 -a $fixtureProcess -t 10000 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Native fixture did not load.' }
        & winapp ui focus Widget.items.Item.0 -a $fixtureProcess --json | Set-Content "$output/focus.json"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot grant fixture keyboard focus.' }
        if ($Mode -eq 'widget') {
            try { & (Join-Path $PSScriptRoot 'Test-WinUiIndexedWidget.ps1') -AppPid $fixtureProcess -OutputDirectory $output }
            finally { $fixtureProcess = $null } # That established test script owns closing its window.
        }
        else {
            & winapp ui invoke IndexedWidget.MementoProbe -a $fixtureProcess | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not start native memento checks.' }
            & winapp ui wait-for IndexedWidget.MementoResult -a $fixtureProcess --value 'Passed memento recreation checks:' --contains -t 40000 | Out-Null
            $mementoExit = $LASTEXITCODE
            & winapp ui get-value IndexedWidget.MementoResult -a $fixtureProcess --json | Set-Content "$output/result.json"
            & winapp ui screenshot -a $fixtureProcess -o "$output/final.png" | Out-Null
            if ($mementoExit -ne 0) { throw 'Native memento checks failed; see result.json.' }
            Get-Content "$output/result.json"
        }
    }
    else {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!(Test-Path "$output/result.json") -and $timer.Elapsed.TotalSeconds -lt 90) {
            if (!(Get-Process -Id $fixtureProcess -ErrorAction SilentlyContinue)) { throw 'Fixture exited without a result.' }
            Start-Sleep -Milliseconds 200
        }
        if (!(Test-Path "$output/result.json")) { throw 'Native fixture timed out.' }
        & winapp ui screenshot -a $fixtureProcess -o "$output/final.png" | Out-Null
        $result = Get-Content "$output/result.json" -Raw | ConvertFrom-Json
        [pscustomobject]@{ Mode=$Mode; Passed=$result.passed; Checks=$result.checks.Count; Output=$output } | ConvertTo-Json
        if (-not $result.passed) { throw 'Native assertions failed; see result.json.' }
    }
}
finally {
    if ($null -ne $fixtureProcess -and (Get-Process -Id $fixtureProcess -ErrorAction SilentlyContinue)) {
        try { & winapp ui get-value IndexedWidget.Status -a $fixtureProcess --json | Set-Content "$output/status.json" }
        finally {
            # An established fixture script may already have requested close.
            if (Get-Process -Id $fixtureProcess -ErrorAction SilentlyContinue) {
                & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $fixtureProcess
            }
        }
    }
}
