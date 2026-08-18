[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:MSBUILDDISABLENODEREUSE = '1'
$env:UseSharedCompilation = 'false'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot 'tests\FirstPartyWidgetConformance.Tests\FirstPartyWidgetConformance.Tests.csproj'
$evidence = if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    Join-Path $repositoryRoot 'artifacts\acceptance\community-addon-recovery.json'
} else {
    [System.IO.Path]::GetFullPath($OutputFile)
}
$realCatalog = Join-Path ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData)) 'WidgetRail\widgets'

function Get-RealCatalogFingerprint {
    $statePath = Join-Path $realCatalog 'catalog-state.json'
    $stateHash = if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        (Get-FileHash -LiteralPath $statePath -Algorithm SHA256).Hash
    } else {
        'absent'
    }
    $versions = @()
    $packages = Join-Path $realCatalog 'packages'
    if (Test-Path -LiteralPath $packages -PathType Container) {
        $versions = Get-ChildItem -LiteralPath $packages -Directory |
            Sort-Object Name |
            ForEach-Object {
                $id = $_.Name
                Get-ChildItem -LiteralPath $_.FullName -Directory |
                    Sort-Object Name |
                    ForEach-Object { "$id/$($_.Name)" }
            }
    }
    return (($stateHash, $versions) | ConvertTo-Json -Compress)
}

$before = Get-RealCatalogFingerprint
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $evidence) | Out-Null

& dotnet run --project $project `
    --configuration $Configuration `
    --no-launch-profile `
    --property:UseSharedCompilation=false `
    --property:BuildInParallel=false `
    -- `
    --community-recovery-acceptance `
    --acceptance-output $evidence
if ($LASTEXITCODE -ne 0) {
    throw "Community-addon recovery acceptance failed with exit code $LASTEXITCODE."
}

$after = Get-RealCatalogFingerprint
if ($before -cne $after) {
    throw 'The real user catalog changed while isolated acceptance was running.'
}
if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) {
    throw "Community-addon recovery acceptance did not produce evidence: $evidence"
}
$result = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json
if (@($result.packages).Count -ne 2 -or
    @($result.packages.id) -notcontains 'widgetrail.samples.spotify' -or
    @($result.packages.id) -notcontains 'widgetrail.samples.ytmusic') {
    throw 'Community-addon recovery evidence did not cover both exact current addons.'
}

Write-Host 'Spotify/YT Music Community-addon recovery acceptance passed.'
Write-Host "Evidence: $evidence"
Write-Host 'The real user catalog was read only and remained unchanged.'
