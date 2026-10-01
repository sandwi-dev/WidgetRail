[CmdletBinding()]
param(
    [string]$OutputRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force -DisableNameChecking
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$null=Assert-ReleasePath $repository
if(!$OutputRoot){$OutputRoot=Join-Path $repository 'artifacts/releases'}
$OutputRoot=Assert-ReleasePath $OutputRoot
$properties=[xml](Get-Content -LiteralPath (Join-Path $repository 'eng/WidgetRailRelease.props') -Raw)
$version=[string]$properties.Project.PropertyGroup.WidgetRailReleaseVersion
$sdkProperties=[xml](Get-Content -LiteralPath (Join-Path $repository 'eng/WidgetSdkRelease.props') -Raw)
$sdkVersion=[string]$sdkProperties.Project.PropertyGroup.WidgetSdkReleaseVersion
$content=Get-Content -LiteralPath (Join-Path $repository 'eng/release-content.json') -Raw | ConvertFrom-Json
function Get-SourceCommit {
    $revision=(& git -C $repository rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$'){throw 'Cannot establish release source revision.'}
    $status=@(& git -C $repository status --porcelain --untracked-files=normal)
    if($LASTEXITCODE -ne 0 -or $status.Count){throw 'Release folders require a clean committed checkout.'}
    return $revision
}
$commit=Get-SourceCommit
if(Test-Path -LiteralPath (Join-Path $OutputRoot $version)){throw "Release $version already exists in $OutputRoot."}
$work=Assert-ReleasePath (Join-Path $repository ('artifacts/release-build/'+[guid]::NewGuid().ToString('N'))) -Within $repository
New-Item -ItemType Directory -Path (Split-Path $work) -Force | Out-Null
$lockPath=Join-Path $repository 'artifacts/release-build.lock'
try{$lease=[IO.File]::Open($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)}
catch{throw 'Another release build owns this checkout, or its lock is unavailable.'}
try {
    & (Join-Path $PSScriptRoot 'Build-WinUiReleasePayload.ps1') -OutputDirectory $work | Out-Host
    $payload=Get-Content -LiteralPath (Join-Path $work 'payload.json') -Raw | ConvertFrom-Json
    if($payload.dirty -or $payload.sourceCommit -cne $commit -or (Get-SourceCommit) -cne $commit){throw 'Release source changed during publication.'}
    $verifier=$payload.verifier
    $result=New-WidgetRailReleaseFolders -BuildRoot $payload.buildRoot -DeveloperRoot $payload.developerRoot -RepositoryRoot $repository `
        -OutputRoot $OutputRoot -Version $version -SourceCommit $commit -SdkVersion $sdkVersion -Content $content -VerifyCatalog {
            param($folder)
            & $verifier $folder | Out-Host
            if($LASTEXITCODE -ne 0){throw "Real catalog validation failed: $folder"}
        } -BeforePublish {if((Get-SourceCommit) -cne $commit){throw 'Source changed before release publication.'}}
    Write-Output "Release folders created: $result"
    Write-Output 'No registration, startup change, certificate trust, prerequisite installation or application launch was performed.'
} finally {$lease.Dispose()}
