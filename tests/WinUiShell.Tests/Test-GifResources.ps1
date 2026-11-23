param([Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][string]$Output,[Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$InputManifest)
$ErrorActionPreference='Stop'
$directory=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $directory){throw 'Use a fresh result directory.'}
New-Item -ItemType Directory -Path $directory|Out-Null
$cases=Get-Content $InputManifest -Raw|ConvertFrom-Json
$case=@($cases.cases|Where-Object name -eq $Name)
if($case.Count -ne 1){throw 'Expected one generated input.'}
$case[0]|ConvertTo-Json|Set-Content "$directory/input.json"
$result="$directory/result.json"
$samples=[Collections.Generic.List[object]]::new()
$terminalMilliseconds=[int]$case[0].terminalMilliseconds
if($terminalMilliseconds -lt 0 -or $terminalMilliseconds -gt 180000){throw 'Invalid generated endurance duration.'}
$arguments=@('--validate-styles','--validation-platform-activation',"--gif-resource-input=`"$($case[0].file)`"","--gif-resource-output=`"$result`"","--gif-resource-terminal-ms=$terminalMilliseconds")
$app=Start-Process -FilePath (Join-Path (Resolve-Path -LiteralPath $CandidateDirectory).Path 'OverlayFrontend.WinUI.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
$failure=$null
try {
    $deadline=[DateTime]::UtcNow.AddMilliseconds(90000+$terminalMilliseconds)
    while(!(Test-Path -LiteralPath $result)) {
        $app.Refresh()
        if($app.HasExited){throw "Fixture exited early: $($app.ExitCode)"}
        $samples.Add(@{utc=[DateTime]::UtcNow.ToString('O');privateBytes=$app.PrivateMemorySize64;workingSetBytes=$app.WorkingSet64;cpuMilliseconds=$app.TotalProcessorTime.TotalMilliseconds})
        if($app.PrivateMemorySize64 -gt 768MB){throw 'External watchdog: private bytes exceed 768 MiB.'}
        if([DateTime]::UtcNow -gt $deadline){throw 'External watchdog: native fixture exceeded its generated duration plus 90 seconds.'}
        Start-Sleep -Milliseconds 100
    }
    $data=Get-Content -LiteralPath $result -Raw|ConvertFrom-Json
    $data|Select-Object passed,@{n='checks';e={$_.checks.Count}},collectedImages,longestHeartbeatMilliseconds,endurance,error|ConvertTo-Json -Depth 3
    if(!$data.passed){throw 'Native GIF resource validation failed.'}
}catch {$failure=$_.Exception.Message; throw}
finally {
    @{failure=$failure;samples=$samples}|ConvertTo-Json -Depth 4|Set-Content "$directory/watchdog.json"
    if(!$app.HasExited) {
        if($failure -like 'External watchdog:*'){Stop-Process -Id $app.Id}
        else {& "$PSScriptRoot/../../scripts/Close-WinUiTestShell.ps1" -AppPid $app.Id; if(!$app.WaitForExit(15000)){Stop-Process -Id $app.Id}}
    }
    $app.WaitForExit()
    @{exitCode=$app.ExitCode}|ConvertTo-Json|Set-Content "$directory/shutdown.json"
    $app.Dispose()
}
