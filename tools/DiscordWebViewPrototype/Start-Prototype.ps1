param(
    [ValidateSet('Discord','Capture')][string]$Mode='Discord',
    [string]$CandidateDirectory,
    [switch]$SelfTest,
    [switch]$Audio
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$CandidateDirectory){
    $latest=Join-Path $repo 'artifacts/discord-webview/latest-candidate.txt'
    if(!(Test-Path -LiteralPath $latest)){throw 'Build the prototype first, or supply -CandidateDirectory.'}
    $CandidateDirectory=[IO.File]::ReadAllText($latest).Trim()
}
$exe=Join-Path $CandidateDirectory 'OverlayFrontend.WinUI.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build the validation-enabled candidate first; see README.md.'}
& "$PSScriptRoot/Stage.ps1"
$assets=Join-Path $repo 'artifacts/discord-webview/assets'
if($Mode -eq 'Discord'){
    $existing=@(Get-CimInstance Win32_Process -Filter "Name = 'OverlayFrontend.WinUI.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.Contains('--discord-webview-prototype=') -and $_.CommandLine.Contains($assets)
    })
    if($existing.Count){throw 'This Discord prototype profile is already open. Close it before launching another candidate.'}
}
if(!(Test-Path -LiteralPath (Join-Path $CandidateDirectory 'WebRtcCapturePrototype.dll'))){throw 'Candidate is missing its matching capture DLL.'}
$arguments=@("--discord-webview-prototype=$assets")
if($Mode -eq 'Capture'){
    $arguments=@("--discord-capture-probe=$assets")
    if($SelfTest){$arguments+='--capture-probe-selftest';if($Audio){$arguments+='--capture-probe-audio'}}
}elseif($SelfTest -or $Audio){throw 'Automatic capture is restricted to the synthetic fixture mode.'}
# An interactive prototype window must be visible to the user for sign-in and source choice.
$process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Normal -PassThru
Write-Output "Started $Mode prototype, process $($process.Id)."
