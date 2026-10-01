[CmdletBinding()]
param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot
$output=Join-Path $repository ('artifacts/bridge-verification/'+[guid]::NewGuid().ToString('N'))
$catalog=Get-Content (Join-Path $repository 'eng/widget-catalog.json') -Raw | ConvertFrom-Json
& (Join-Path $PSScriptRoot 'New-WinUiBundledWorkspace.ps1') -WidgetId @($catalog.bundledWidgets.id) -IncludeSettings -Configuration $Configuration -OutputDirectory $output | Out-Host
$priorRoot=$env:WRAIL_TEST_INSTALLATION_ROOT
$priorConfiguration=$env:WRAIL_TEST_CONFIGURATION
try {
    $env:WRAIL_TEST_INSTALLATION_ROOT=Join-Path $output 'installation'
    $env:WRAIL_TEST_CONFIGURATION=$Configuration
    & dotnet run --project (Join-Path $repository 'tests/WidgetBridge.Tests/WidgetBridge.Tests.csproj') -c $Configuration -p:ContinuousIntegrationBuild=true "-bl:$output/bridge-tests.binlog"
    if($LASTEXITCODE -ne 0){throw 'Bridge verification failed against the coherent managed installation.'}
} finally {
    $env:WRAIL_TEST_INSTALLATION_ROOT=$priorRoot
    $env:WRAIL_TEST_CONFIGURATION=$priorConfiguration
}
