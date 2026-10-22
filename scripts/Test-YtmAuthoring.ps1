[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BridgeExecutable,
    [Parameter(Mandatory)][ValidateSet('reordered','removed','ambiguous','performance')][string]$Mode,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$bridgePath=(Resolve-Path -LiteralPath $BridgeExecutable).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh output directory to preserve earlier evidence.'}
New-Item -ItemType Directory -Path $output|Out-Null
$pipe='ytm-authoring-'+[guid]::NewGuid().ToString('N')
$bridgeProcess=$null
$appProcessId=$null
$measurements=[Collections.Generic.List[object]]::new()
try {
    $bridgeProcess=Start-Process -FilePath $bridgePath -ArgumentList @('--serve-ytm-navigation',$pipe,$Mode) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$output/bridge.log" -RedirectStandardError "$output/bridge-error.log"
    $flag=if($Mode -eq 'performance'){'--ytm-performance-output'}else{'--ytm-navigation-output'}
    $project=Join-Path $repository 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj'
    $launch=& winapp run $project -c Release --arch x64 -p Platform=x64 --no-build --detach --json `
        --args "--indexed-validation-pipe=$pipe $flag=`"$output/result.json`""
    if($LASTEXITCODE -ne 0){throw "Native launch failed: $launch"}
    $launch|Set-Content "$output/launch.json"
    $appProcessId=($launch|ConvertFrom-Json).ProcessId
    @{Mode=$Mode;Commit=(& git -C $repository rev-parse HEAD);Bridge=$bridgePath;
        YtmSha256=(Get-FileHash (Join-Path (Split-Path $bridgePath) 'YtMusicWidget.dll')).Hash;
        SdkSha256=(Get-FileHash (Join-Path (Split-Path $bridgePath) 'WidgetSdk.dll')).Hash;
        AppPid=$appProcessId;BridgePid=$bridgeProcess.Id}|ConvertTo-Json|Set-Content "$output/provenance.json"
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(!(Test-Path "$output/result.json") -and $timer.Elapsed.TotalSeconds -lt 60){
        $processes=Get-CimInstance Win32_Process
        $owned=@($processes|Where-Object{$_.ProcessId -eq $bridgeProcess.Id -or $_.ParentProcessId -eq $bridgeProcess.Id})
        foreach($process in $owned){
            $measurements.Add([pscustomobject]@{Seconds=$timer.Elapsed.TotalSeconds;ProcessId=$process.ProcessId;
                ParentProcessId=$process.ParentProcessId;Name=$process.Name;PrivateBytes=$process.PrivatePageCount;WorkingSet=$process.WorkingSetSize})
        }
        Start-Sleep -Milliseconds 500
    }
    if(!(Test-Path "$output/result.json")){throw 'Native fixture timed out; inspect its status and bridge logs.'}
    & winapp ui screenshot -a $appProcessId -o "$output/final.png" | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Could not capture native result.'}
    $result=Get-Content "$output/result.json" -Raw|ConvertFrom-Json
    [pscustomobject]@{Mode=$Mode;Passed=$result.passed;Output=$output}|ConvertTo-Json
    if(-not $result.passed){throw 'Fixture assertions failed; see result.json. Failure evidence is preserved.'}
}
finally {
    $measurements|Export-Csv "$output/process-memory.csv" -NoTypeInformation
    if($null -ne $appProcessId -and (Get-Process -Id $appProcessId -ErrorAction SilentlyContinue)){
        try { & winapp ui get-value IndexedWidget.Status -a $appProcessId --json | Set-Content "$output/status.json" }
        catch { Write-Warning "Could not save final native status: $_" }
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $appProcessId
    }
    # The bridge owns worker shutdown after the frontend disconnects. Do not
    # terminate unrelated overlay processes or install/update any package.
}
