param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$Accumulated,
    [switch]$Animated,
    [switch]$EdgeOnly
)
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh native chat comparison result directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
$result=Join-Path $output 'result.json'
$arguments=@('--validate-styles','--validation-platform-activation',"--native-chat-scroll-output=`"$result`"")
if($Accumulated){$arguments+='--native-chat-scroll-accumulated'}
if($Animated){$arguments+='--native-chat-scroll-animated'}
if($EdgeOnly){$arguments+='--native-chat-scroll-edge-only'}
$exe=Join-Path (Resolve-Path -LiteralPath $CandidateDirectory).Path 'OverlayFrontend.WinUI.exe'
$app=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(180)
    while(!(Test-Path -LiteralPath $result)) {
        if($app.HasExited){throw "Native chat fixture exited early: $($app.ExitCode)"}
        if([DateTime]::UtcNow -gt $deadline){throw 'Native chat fixture exceeded 180 seconds.'}
        Start-Sleep -Milliseconds 200
    }
    $data=Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    $data | Select-Object passed,accumulated,animated,edgeOnly,error,@{n='directions';e={@($_.results | Select-Object direction,frames,wrongDirection,reads)}} | ConvertTo-Json -Depth 5
    if(!$data.passed){throw "Native chat comparison failed; evidence: $result"}
} finally {
    if(!$app.HasExited){
        & "$PSScriptRoot/../../scripts/Close-WinUiTestShell.ps1" -AppPid $app.Id
        if(!$app.WaitForExit(15000)){Stop-Process -Id $app.Id}
    }
    $app.WaitForExit()
    @{exitCode=$app.ExitCode} | ConvertTo-Json | Set-Content (Join-Path $output 'shutdown.json')
    $app.Dispose()
}
