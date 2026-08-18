[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$env:MSBUILDDISABLENODEREUSE = '1'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot 'tests\FirstPartyWidgetConformance.Tests\FirstPartyWidgetConformance.Tests.csproj'
$evidence = if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    Join-Path $repositoryRoot 'artifacts\acceptance\ytmusic-community-addon.json'
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
    --ytmusic-community-acceptance `
    --acceptance-output $evidence
if ($LASTEXITCODE -ne 0) {
    throw "YT Music Community-addon acceptance failed with exit code $LASTEXITCODE."
}

$after = Get-RealCatalogFingerprint
if ($before -cne $after) {
    throw 'The real user catalog changed while isolated acceptance was running. The test never targets it; inspect concurrent catalog activity.'
}
if (-not (Test-Path -LiteralPath $evidence -PathType Leaf)) {
    throw "YT Music acceptance did not produce evidence: $evidence"
}
$result = Get-Content -LiteralPath $evidence -Raw | ConvertFrom-Json
$expectedPhases = @(
    'clean-public-validate-pack-install',
    'generic-appcontainer-catalog-route',
    'explicit-required-and-optional-consent',
    'simulated-companion-pairing-and-secret-write',
    'dashboard-and-open-widget-controller-routing',
    'transient-last-good-connected-idle-and-recovery',
    'background-suspend-and-visible-resume',
    'worker-crash-and-bounded-restart',
    'host-force-reload-fresh-worker',
    'disabled-update-review-new-authority-and-repair',
    'disabled-exact-version-rollback-restores-reviewed-authority',
    'disable-uninstall-catalog-and-isolated-consent-cleanup',
    'no-trusted-fallback'
)
if (@($result.phases).Count -ne $expectedPhases.Count -or
    (Compare-Object -ReferenceObject $expectedPhases -DifferenceObject @($result.phases))) {
    throw 'YT Music acceptance evidence omitted or added an unexpected phase.'
}

Write-Host "YT Music Community-addon acceptance passed."
Write-Host "Evidence: $evidence"
Write-Host 'The real user catalog was read only and remained unchanged.'
