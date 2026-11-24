[CmdletBinding()]
param([ValidateSet('Release','Debug')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$output=Join-Path $root ('artifacts/replay-integration/'+[guid]::NewGuid().ToString('N'))
$native=Join-Path $output 'native'
& (Join-Path $root 'tools/DiscordWebViewPrototype/Build-Native.ps1') -Validation -OutputDirectory $native
if($LASTEXITCODE){throw 'Replay capture worker build failed.'}
$project=Join-Path $root 'tests/CaptureReplay.Integration.Tests/CaptureReplay.Integration.Tests.csproj'
& dotnet run --project $project -c $Configuration "-bl:$output/integration-{}.binlog" -- $native (Join-Path $native 'WindowCaptureFixture.exe') (Join-Path $output 'evidence')
exit $LASTEXITCODE
