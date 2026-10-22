[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh output directory.'}
New-Item -ItemType Directory -Path $output|Out-Null
$bridge=Join-Path $repository 'tests/WidgetBridge.Tests/bin/Release/net8.0-windows10.0.19041.0/win-x64/WidgetBridge.Tests.exe'
$pipe='collection-return-'+[guid]::NewGuid().ToString('N')
$fixtureProcess=$null
try {
    $server=Start-Process -FilePath $bridge -ArgumentList @('--serve-return-validation',$pipe,"`"$output/profile`"") -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$output/bridge.log" -RedirectStandardError "$output/bridge-error.log"
    $launch=& winapp run (Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') -c Release --arch x64 -p Platform=x64 --no-build --detach --json `
        --args "--indexed-validation-pipe=$pipe --collection-return-output=`"$output/result.json`" --collection-return-start=`"$output/start`""
    if($LASTEXITCODE -ne 0){throw 'Fixture launch failed.'}
    $launch|Set-Content "$output/launch.json"
    $fixtureProcess=($launch|ConvertFrom-Json).ProcessId
    for($attempt=0;$attempt -lt 50 -and !(Test-Path "$output/start.ready");$attempt++){Start-Sleep -Milliseconds 100}
    & winapp ui focus Widget.return-mode -a $fixtureProcess --json | Set-Content "$output/foreground.json"
    if($LASTEXITCODE -ne 0){throw 'Foreground admission failed; no wheel input authorized by the fixture.'}
    $frontend=(Get-Process -Id $fixtureProcess).Path
    @{Commit=(& git -C $repository rev-parse HEAD);AppPid=$fixtureProcess;BridgePid=$server.Id;
        Frontend=(Get-FileHash (Join-Path (Split-Path $frontend) 'OverlayFrontend.WinUI.dll')).Hash;
        BridgeFixture=(Get-FileHash (Join-Path (Split-Path $bridge) 'WidgetBridge.Tests.dll')).Hash}|ConvertTo-Json|Set-Content "$output/provenance.json"
    Set-Content "$output/start" 'start'
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(!(Test-Path "$output/result.json") -and $timer.Elapsed.TotalSeconds -lt 90){
        if(!(Get-Process -Id $fixtureProcess -ErrorAction SilentlyContinue)){throw 'Fixture exited without a result.'}
        Start-Sleep -Milliseconds 250
    }
    if(!(Test-Path "$output/result.json")){throw 'Fixture timed out.'}
    & winapp ui screenshot -a $fixtureProcess -o "$output/final.png" | Out-Null
    $result=Get-Content "$output/result.json" -Raw|ConvertFrom-Json
    [pscustomobject]@{Passed=$result.passed;Checks=$result.checks.Count;Output=$output}|ConvertTo-Json
    if(-not $result.passed){throw 'Native return assertions failed; inspect result.json.'}
}
finally {
    if($null -ne $fixtureProcess -and (Get-Process -Id $fixtureProcess -ErrorAction SilentlyContinue)){
        try { & winapp ui get-value IndexedWidget.Status -a $fixtureProcess --json | Set-Content "$output/status.json" }
        finally { & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $fixtureProcess }
    }
}
