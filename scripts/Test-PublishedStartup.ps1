[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$FrontendDirectory,
    [Parameter(Mandatory)][string]$InstallationRoot,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$frontend=[IO.Path]::GetFullPath($FrontendDirectory)
$installation=[IO.Path]::GetFullPath($InstallationRoot)
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh smoke-test directory; existing profiles are never reset.'}
$exe=Join-Path $frontend 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath (Join-Path $installation 'widget-catalog.json'))){throw 'A complete published payload is required.'}
New-Item -ItemType Directory -Path "$output/profile" -Force | Out-Null
@{InstallationRoot=$installation;SettingsRoot="$output/profile";InstalledCatalogRoot="$output/profile/widgets";
    InitialWidgetId='settings';SwitchDiagnosticsPath="$output/startup.log"} | ConvertTo-Json | Set-Content "$output/shell.json"
$errorLog=Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/frontend-errors.log'
$initialErrors=if(Test-Path -LiteralPath $errorLog){(Get-Item -LiteralPath $errorLog).Length}else{0}
$app=$null
try {
    # Exercise ordinary startup in the actual trimmed publication. No validation
    # compilation, controller capture, account data, or synthetic key presses.
    $app=Start-Process -FilePath $exe -WorkingDirectory $frontend -WindowStyle Hidden -PassThru `
        -ArgumentList @('--shell-no-controller',"--shell-config=`"$output/shell.json`"")
    $deadline=[DateTime]::UtcNow.AddSeconds(40)
    $ready=$false
    while([DateTime]::UtcNow -lt $deadline){
        $app.Refresh()
        if($app.HasExited){throw "Published frontend exited during startup ($($app.ExitCode))."}
        if(Test-Path -LiteralPath "$output/startup.log"){
            $trace=Get-Content -LiteralPath "$output/startup.log" -Raw
            if($trace -and $trace.Contains('startup phase=bridge-notifications-queued') -and $trace.Contains('startup phase=initial-widget-ready')){$ready=$true;break}
        }
        Start-Sleep -Milliseconds 200
    }
    if(!$ready){throw 'Published frontend did not reach the initial widget after browser-owner initialization.'}
    if(Test-Path -LiteralPath $errorLog){
        $stream=[IO.File]::Open($errorLog,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
        try {
            if($stream.Length -ge $initialErrors){$null=$stream.Seek($initialErrors,[IO.SeekOrigin]::Begin)}
            $reader=[IO.StreamReader]::new($stream)
            $newErrors=$reader.ReadToEnd()
            if($newErrors -match "pid=$($app.Id) source=(shell-handled|process-startup|dispatcher-unhandled|app-unhandled)"){
                throw 'Published frontend reported a startup failure; inspect frontend-errors.log.'
            }
        } finally {$stream.Dispose()}
    }
    @{passed=$true;processId=$app.Id;frontend=$frontend;checks=@('Actual published executable','Browser owner initialized','Initial Settings widget ready','No handled startup failure')} |
        ConvertTo-Json | Set-Content "$output/result.json"
    Write-Output 'PASS Published startup reaches Settings after browser-owner initialization.'
} catch {
    @{passed=$false;error=$_.Exception.Message} | ConvertTo-Json | Set-Content "$output/result.json"
    throw
} finally {
    if($app -and !$app.HasExited){
        & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $app.Id
        Wait-Process -Id $app.Id -Timeout 15 -ErrorAction SilentlyContinue
        if(Get-Process -Id $app.Id -ErrorAction SilentlyContinue){throw 'Published startup probe did not close cleanly.'}
    }
}
