[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh output directory'}
New-Item -ItemType Directory -Path $output|Out-Null
$started=[DateTime]::UtcNow
$launch=winapp run (Join-Path $root 'src/OverlayFrontend.WinUI/OverlayFrontend.WinUI.csproj') --no-build --arch x64 -p Platform=x64 --detach --json --args '--validate-window-preview --validate-preview-resume'|ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or $launch.ProcessId -le 0){throw 'Preview resume fixture did not launch'}
$ownedPid=[int]$launch.ProcessId
$launch|ConvertTo-Json|Set-Content (Join-Path $output 'launch.json')
try {
 $diagnostic=Join-Path $env:LOCALAPPDATA 'WidgetRail/WinUI/diagnostics/window-preview-result.json'
 $deadline=[DateTime]::UtcNow.AddSeconds(35);$result=$null
 do {
  $file=Get-Item -LiteralPath $diagnostic -ErrorAction SilentlyContinue
  if($file -and $file.LastWriteTimeUtc -ge $started){
   try{$result=Get-Content -LiteralPath $diagnostic -Raw|ConvertFrom-Json}catch{$result=$null}
   if($result -and ($result.passed -eq $false -or $result.stage -eq 'PresenterResume')){break}
  }
  if(-not (Get-Process -Id $ownedPid -ErrorAction SilentlyContinue)){throw 'Preview fixture exited without a result'}
  Start-Sleep -Milliseconds 100
 }while([DateTime]::UtcNow -lt $deadline)
 if($result){$result|ConvertTo-Json -Depth 10|Set-Content (Join-Path $output 'result.json')}
 if(-not $result -or -not $result.passed -or $result.stage -ne 'PresenterResume' -or $result.checks.Count -ne 13){throw "Preview resume failed: $($result.error); expected 13 completed checks"}
 "Passed $($result.checks.Count) native preview resume checks."
}finally{
 if(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue){
  & (Join-Path $PSScriptRoot 'Close-WinUiTestShell.ps1') -AppPid $ownedPid
  Wait-Process -Id $ownedPid -Timeout 15 -ErrorAction SilentlyContinue
  if(Get-Process -Id $ownedPid -ErrorAction SilentlyContinue){throw 'Preview fixture did not finish normal shutdown'}
 }
}
